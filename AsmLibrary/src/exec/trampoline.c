/*
 * exec/trampoline.c - trampoline 生成器（x86/x64）+ 常驻执行页/网关
 *
 * trampoline 流程（x64）:
 *   prologue(保存非易失寄存器+栈帧) → sub 0x50(shadow+栈参数) → sub 8(用户入口对齐)
 *   → [rec.active]=1 → 记录 saved_rsp/rbp → sub rsp,8 → mov rax,<用户区>; call rax
 *   （用户代码当函数调用；用户 ret 被引擎改写为 jmp 网关）
 *   → mid capture → （mode=call）rsp 归一+ABI 封送+call 目标 → final capture
 *   → epilogue（栈归一 + 成对出栈 + ret）
 *
 * 捕获序列（两架构同型）: push rcx/ecx → pushfq → push rax/eax → 取记录指针 →
 *   逐寄存器存入记录（lea 抓压栈前 rsp）→ pop-to-memory 回填 → 还原被 scratch 占用的寄存器。
 */
#include "exec/trampoline.h"
#include "util/util.h"

/* ---- 常驻执行页 + 网关 ---- */

static uint8_t *g_exec_page = NULL;
static AsmLock  g_page_lock = { 0 };

static uint8_t *ensure_exec_page(void)
{
    uint8_t *page = g_exec_page;
    if (page != NULL) return page;

    page = (uint8_t *)VirtualAlloc(NULL, ASMHOST_PAGE_SIZE,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (page == NULL) return NULL;

#ifdef _WIN64
    {
        /* FF 25 00000000 = jmp qword [rip+0] → 槽紧跟指令后 */
        page[ASMEXEC_GATE_OFF + 0] = 0xFF;
        page[ASMEXEC_GATE_OFF + 1] = 0x25;
        page[ASMEXEC_GATE_OFF + 2] = 0x00;
        page[ASMEXEC_GATE_OFF + 3] = 0x00;
        page[ASMEXEC_GATE_OFF + 4] = 0x00;
        page[ASMEXEC_GATE_OFF + 5] = 0x00;
        memset(page + ASMEXEC_GATE_OFF + 6, 0, 8);
    }
#else
    {
        uint32_t slot_va = (uint32_t)(uintptr_t)(page + ASMEXEC_GATE_OFF + 6);
        /* FF 25 <abs32> = jmp dword [abs32] */
        page[ASMEXEC_GATE_OFF + 0] = 0xFF;
        page[ASMEXEC_GATE_OFF + 1] = 0x25;
        memcpy(page + ASMEXEC_GATE_OFF + 2, &slot_va, 4);
        memset(page + ASMEXEC_GATE_OFF + 6, 0, 4);
    }
#endif
    FlushInstructionCache(GetCurrentProcess(), page, 16);
    g_exec_page = page;
    return page;
}

void *asmtramp_get_gate(void)
{
    void *gate = NULL;
    asmhost_lock_enter(&g_page_lock);
    {
        uint8_t *page = ensure_exec_page();
        if (page != NULL)
            gate = page + ASMEXEC_GATE_OFF;
    }
    asmhost_lock_leave(&g_page_lock);
    return gate;
}

/* ============ x64 ============ */

#ifdef _WIN64

#define X64_STACK_RESERVE 0x50   /* 32 shadow + 8 对齐 + 40 栈参数 */

static void e64_mov_rcx_imm(AsmEmit *b, uint64_t v)
{
    asmemit_byte(b, 0x48); asmemit_byte(b, 0xB9);
    asmemit_d64(b, v);
}

/* mov [rcx+disp32], r64 — reg_code: 源寄存器低 3 位; rex_r: 源为 r8-r15 */
static void e64_store_rcx(AsmEmit *b, int reg_code, int rex_r, size_t off)
{
    asmemit_byte(b, rex_r ? 0x4C : 0x48);
    asmemit_byte(b, 0x89);
    asmemit_byte(b, (uint8_t)(0x80 | ((reg_code & 7) << 3) | 0x01));
    asmemit_d32(b, (int32_t)off);
}

/* pop qword [rcx+disp32] */
static void e64_pop_rcx(AsmEmit *b, size_t off)
{
    asmemit_byte(b, 0x8F);
    asmemit_byte(b, 0x81);
    asmemit_d32(b, (int32_t)off);
}

/* mov rsp, [rcx+saved_rsp] */
static void e64_restore_rsp(AsmEmit *b, size_t off_rsp)
{
    asmemit_byte(b, 0x48); asmemit_byte(b, 0x8B);
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RSP & 7) << 3) | 0x01));
    asmemit_d32(b, (int32_t)off_rsp);
}

/* 捕获全部寄存器 + flags。进入/退出时 rsp 同值（=捕获入口现场），寄存器不变。 */
static void e64_capture_regs(AsmEmit *b, AsmExecJob *job, int is_final)
{
    size_t off_regs = is_final ? offsetof(AsmExecRecord, regs_final)
                               : offsetof(AsmExecRecord, regs_mid);
    size_t off_flags = is_final ? offsetof(AsmExecRecord, flags_final)
                                : offsetof(AsmExecRecord, flags_mid);
    static const uint8_t lea_rdx[] = { 0x48, 0x8D, 0x94, 0x24, 0x18, 0x00, 0x00, 0x00 };
    int i;

    asmemit_byte(b, 0x51);            /* push rcx  → [E-8]  */
    asmemit_byte(b, 0x9C);            /* pushfq    → [E-16] */
    asmemit_byte(b, 0x50);            /* push rax  → [E-24] */
    e64_mov_rcx_imm(b, (uint64_t)(uintptr_t)&job->rec);

    e64_store_rcx(b, ASMR_RDX & 7, 0, off_regs + ASMR_RDX * 8);
    e64_store_rcx(b, ASMR_RBX & 7, 0, off_regs + ASMR_RBX * 8);
    e64_store_rcx(b, ASMR_RBP & 7, 0, off_regs + ASMR_RBP * 8);
    e64_store_rcx(b, ASMR_RSI & 7, 0, off_regs + ASMR_RSI * 8);
    e64_store_rcx(b, ASMR_RDI & 7, 0, off_regs + ASMR_RDI * 8);
    for (i = 0; i < 8; i++)
        e64_store_rcx(b, i, 1, off_regs + (ASMR_R8 + i) * 8);

    asmemit_bytes(b, lea_rdx, 8);     /* lea rdx, [rsp+24] → E（压栈前 rsp） */
    e64_store_rcx(b, ASMR_RSP & 7, 0, off_regs + ASMR_RSP * 8);

    /* 栈序回填: 顶→底 = rax, flags, rcx */
    e64_pop_rcx(b, off_regs + ASMR_RAX * 8);
    e64_pop_rcx(b, off_flags);
    e64_pop_rcx(b, off_regs + ASMR_RCX * 8);

    /* 还原被 scratch 占用的 rdx/rcx（先 rdx 后 rcx: rcx 仍是记录指针） */
    asmemit_byte(b, 0x48); asmemit_byte(b, 0x8B);
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RDX & 7) << 3) | 0x01));
    asmemit_d32(b, (int32_t)(off_regs + ASMR_RDX * 8));
    asmemit_byte(b, 0x48); asmemit_byte(b, 0x8B);
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RCX & 7) << 3) | 0x01));
    asmemit_d32(b, (int32_t)(off_regs + ASMR_RCX * 8));
}

/* epilogue 与 recovery 共用的收尾序列 */
static void e64_common_tail(AsmEmit *b, size_t off_active, size_t off_rsp)
{
    e64_mov_rcx_imm(b, 0);              /* mov rcx, rec（占位，回填） */
    asmemit_byte(b, 0xC7); asmemit_byte(b, 0x81);
    asmemit_d32(b, (int32_t)off_active);
    asmemit_d32(b, 0);
    e64_restore_rsp(b, off_rsp);
    /* add rsp, X64_STACK_RESERVE+8（0x50 栈帧 + 8 对齐垫片） */
    asmemit_byte(b, 0x48); asmemit_byte(b, 0x83); asmemit_byte(b, 0xC4);
    asmemit_byte(b, X64_STACK_RESERVE + 0x08);
    asmemit_byte(b, 0x41); asmemit_byte(b, 0x5F);   /* pop r15 */
    asmemit_byte(b, 0x41); asmemit_byte(b, 0x5E);   /* pop r14 */
    asmemit_byte(b, 0x41); asmemit_byte(b, 0x5D);   /* pop r13 */
    asmemit_byte(b, 0x41); asmemit_byte(b, 0x5C);   /* pop r12 */
    asmemit_byte(b, 0x5F);                           /* pop rdi */
    asmemit_byte(b, 0x5E);                           /* pop rsi */
    asmemit_byte(b, 0x5B);                           /* pop rbx */
    asmemit_byte(b, 0x5D);                           /* pop rbp */
    asmemit_byte(b, 0xC3);                           /* ret */
}

static void patch_rec64(uint8_t *base, size_t mov_pos, uint64_t v)
{
    int k;
    /* mov rcx, imm64: 48 B9 <imm64> → 立即数在 +2 */
    for (k = 0; k < 8; k++)
        base[mov_pos + 2 + k] = (uint8_t)((v >> (8 * k)) & 0xFF);
}

static size_t build_trampoline_x64(AsmExecJob *job, uint8_t *tramp,
                                   uint8_t *user_region, uint8_t *gate_slot,
                                   size_t cap)
{
    AsmEmit buf_; AsmEmit *b = &buf_;
    size_t off_active = offsetof(AsmExecRecord, active);
    size_t off_rsp = offsetof(AsmExecRecord, saved_rsp);
    size_t off_rbp = offsetof(AsmExecRecord, saved_rbp);
    size_t epilogue_pos = 0, recovery_pos = 0;
    size_t epilogue_rec_pos = 0, recovery_rec_pos = 0;
    size_t user_addr_pos = 0, mid_pos = 0;
    int i;

    b->p = tramp; b->cap = cap; b->len = 0;
    job->trampoline = tramp;

    /* ---- prologue ---- */
    {
        static const uint8_t pro[] = {
            0x55,                           /* push rbp */
            0x48, 0x89, 0xE5,               /* mov rbp, rsp */
            0x53,                           /* push rbx */
            0x56,                           /* push rsi */
            0x57,                           /* push rdi */
            0x41, 0x54,                     /* push r12 */
            0x41, 0x55,                     /* push r13 */
            0x41, 0x56,                     /* push r14 */
            0x41, 0x57                      /* push r15 */
        };
        asmemit_bytes(b, pro, sizeof(pro));
        /* sub rsp, 0x50 —— 32 shadow + 8 对齐 + 40 栈参数 */
        asmemit_byte(b, 0x48); asmemit_byte(b, 0x83); asmemit_byte(b, 0xEC);
        asmemit_byte(b, X64_STACK_RESERVE);
        /* sub rsp, 8 —— 用户代码入口 rsp%16==0（用户自己的 call 正确对齐） */
        asmemit_byte(b, 0x48); asmemit_byte(b, 0x83); asmemit_byte(b, 0xEC);
        asmemit_byte(b, 0x08);

        e64_mov_rcx_imm(b, (uint64_t)(uintptr_t)&job->rec);
        asmemit_byte(b, 0xC7); asmemit_byte(b, 0x81);   /* mov dword [rcx+active], 1 */
        asmemit_d32(b, (int32_t)off_active);
        asmemit_d32(b, 1);
        e64_store_rcx(b, ASMR_RSP & 7, 0, off_rsp);
        e64_store_rcx(b, ASMR_RBP & 7, 0, off_rbp);

        /* sub rsp, 8 → mov rax, <user_region>; call rax —— 把用户代码当函数调用:
         *   用户 ret 被引擎改写为 jmp 网关（与 home space 不冲突）;
         *   用户不写 ret 也安全（用户区域末尾自动补 ret） */
        asmemit_byte(b, 0x48); asmemit_byte(b, 0x83); asmemit_byte(b, 0xEC);
        asmemit_byte(b, 0x08);
        asmemit_byte(b, 0x48); asmemit_byte(b, 0xB8);
        user_addr_pos = b->len;
        asmemit_d64(b, 0);                              /* 占位，最后回填 */
        asmemit_byte(b, 0xFF); asmemit_byte(b, 0xD0);   /* call rax */
    }

    /* ---- mid capture（网关也会跳到这里） ---- */
    mid_pos = b->len;
    {
        int k;
        uint64_t mid_va = (uint64_t)(uintptr_t)(tramp + mid_pos);
        for (k = 0; k < 8; k++)
            gate_slot[k] = (uint8_t)((mid_va >> (8 * k)) & 0xFF);
    }
    e64_capture_regs(b, job, 0);

    /* ---- call seq（先归一 rsp 保证对齐） ---- */
    if (job->rec.call_target != NULL)
    {
        int nargs = job->rec.arg_count;
        e64_mov_rcx_imm(b, (uint64_t)(uintptr_t)&job->rec);
        e64_restore_rsp(b, off_rsp);       /* rsp = saved_rsp（16 对齐） */
        if (nargs > 0) { asmemit_byte(b, 0x48); asmemit_byte(b, 0xB9); asmemit_d64(b, job->rec.args[0]); }
        if (nargs > 1) { asmemit_byte(b, 0x48); asmemit_byte(b, 0xBA); asmemit_d64(b, job->rec.args[1]); }
        if (nargs > 2) { asmemit_byte(b, 0x49); asmemit_byte(b, 0xB8); asmemit_d64(b, job->rec.args[2]); }
        if (nargs > 3) { asmemit_byte(b, 0x49); asmemit_byte(b, 0xB9); asmemit_d64(b, job->rec.args[3]); }
        for (i = 4; i < nargs; i++)
        {
            asmemit_byte(b, 0x48); asmemit_byte(b, 0xB8);
            asmemit_d64(b, job->rec.args[i]);
            asmemit_byte(b, 0x48); asmemit_byte(b, 0x89);
            asmemit_byte(b, 0x84); asmemit_byte(b, 0x24);
            asmemit_d32(b, 0x20 + 8 * (i - 4));
        }
        asmemit_byte(b, 0x48); asmemit_byte(b, 0xB8);
        asmemit_d64(b, (uint64_t)(uintptr_t)job->rec.call_target);
        asmemit_byte(b, 0xFF); asmemit_byte(b, 0xD0);   /* call rax */
    }

    /* ---- final capture ---- */
    e64_capture_regs(b, job, 1);

    /* ---- epilogue ---- */
    epilogue_pos = b->len;
    e64_common_tail(b, off_active, off_rsp);
    epilogue_rec_pos = epilogue_pos;        /* tail 开头即 mov rcx, imm64 */

    /* ---- recovery stub ---- */
    recovery_pos = b->len;
    e64_common_tail(b, off_active, off_rsp);
    recovery_rec_pos = recovery_pos;

    /* ---- 拷贝用户代码到常驻区 + 回填 user 地址 ---- */
    {
        uint64_t user_va;
        int k;
        memset(user_region, 0xC3, ASMEXEC_TRAMP_OFF - ASMEXEC_CODE_OFF);
        memcpy(user_region, job->user_code, job->user_len);
        user_va = (uint64_t)(uintptr_t)user_region;
        for (k = 0; k < 8; k++)
            b->p[user_addr_pos + k] = (uint8_t)((user_va >> (8 * k)) & 0xFF);
    }

    /* 回填两处 mov rcx, rec */
    {
        uint64_t recv = (uint64_t)(uintptr_t)&job->rec;
        patch_rec64(b->p, epilogue_rec_pos, recv);
        patch_rec64(b->p, recovery_rec_pos, recv);
    }
    job->recovery = tramp + recovery_pos;
    job->rec.recovery_rip = tramp + recovery_pos;

    return b->len;
}

#else /* ============ x86 ============ */

static void e86_store_eax(AsmEmit *b, int reg_code, size_t off)
{
    asmemit_byte(b, 0x89);
    asmemit_byte(b, (uint8_t)(0x80 | ((reg_code & 7) << 3) | 0x00));
    asmemit_d32(b, (int32_t)off);
}

static void e86_pop_eax(AsmEmit *b, size_t off)
{
    asmemit_byte(b, 0x8F);
    asmemit_byte(b, 0x80);
    asmemit_d32(b, (int32_t)off);
}

static void e86_capture_regs(AsmEmit *b, AsmExecJob *job, int is_final)
{
    size_t off_regs = is_final ? offsetof(AsmExecRecord, regs_final)
                               : offsetof(AsmExecRecord, regs_mid);
    size_t off_flags = is_final ? offsetof(AsmExecRecord, flags_final)
                                : offsetof(AsmExecRecord, flags_mid);
    static const uint8_t lea_edx[] = { 0x8D, 0x94, 0x24, 0x0C, 0x00, 0x00, 0x00 };

    asmemit_byte(b, 0x51);    /* push ecx → [E-4]  */
    asmemit_byte(b, 0x9C);    /* pushfd   → [E-8]  */
    asmemit_byte(b, 0x50);    /* push eax → [E-12] */
    asmemit_byte(b, 0xB8);    /* mov eax, rec */
    asmemit_d32(b, (int32_t)(uintptr_t)&job->rec);

    e86_store_eax(b, ASMR_RDX & 7, off_regs + ASMR_RDX * 8);
    e86_store_eax(b, ASMR_RBX & 7, off_regs + ASMR_RBX * 8);
    e86_store_eax(b, ASMR_RBP & 7, off_regs + ASMR_RBP * 8);
    e86_store_eax(b, ASMR_RSI & 7, off_regs + ASMR_RSI * 8);
    e86_store_eax(b, ASMR_RDI & 7, off_regs + ASMR_RDI * 8);

    asmemit_bytes(b, lea_edx, 7);    /* lea edx, [esp+12] → E */
    e86_store_eax(b, ASMR_RSP & 7, off_regs + ASMR_RSP * 8);

    /* 栈序回填: 顶→底 = eax, flags, ecx */
    e86_pop_eax(b, off_regs + ASMR_RAX * 8);
    e86_pop_eax(b, off_flags);
    e86_pop_eax(b, off_regs + ASMR_RCX * 8);

    /* 还原被 scratch 占用的 edx/eax（先 edx 后 eax: eax 仍是记录指针） */
    asmemit_byte(b, 0x8B);
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RDX & 7) << 3) | 0x00));
    asmemit_d32(b, (int32_t)(off_regs + ASMR_RDX * 8));
    asmemit_byte(b, 0x8B);
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RAX & 7) << 3) | 0x00));
    asmemit_d32(b, (int32_t)(off_regs + ASMR_RAX * 8));
}

static void e86_common_tail(AsmEmit *b, size_t off_active, size_t off_rsp)
{
    asmemit_byte(b, 0xB8);              /* mov eax, rec（占位） */
    asmemit_d32(b, 0);
    asmemit_byte(b, 0xC7); asmemit_byte(b, 0x80);
    asmemit_d32(b, (int32_t)off_active);
    asmemit_d32(b, 0);
    asmemit_byte(b, 0x8B);              /* mov esp, [eax+saved_rsp] */
    asmemit_byte(b, (uint8_t)(0x80 | ((ASMR_RSP & 7) << 3) | 0x00));
    asmemit_d32(b, (int32_t)off_rsp);
    asmemit_byte(b, 0x5F);              /* pop edi */
    asmemit_byte(b, 0x5E);              /* pop esi */
    asmemit_byte(b, 0x5B);              /* pop ebx */
    asmemit_byte(b, 0x5D);              /* pop ebp */
    asmemit_byte(b, 0xC3);              /* ret */
}

static size_t build_trampoline_x86(AsmExecJob *job, uint8_t *tramp,
                                   uint8_t *user_region, uint8_t *gate_slot,
                                   size_t cap)
{
    AsmEmit buf_; AsmEmit *b = &buf_;
    size_t off_active = offsetof(AsmExecRecord, active);
    size_t off_rsp = offsetof(AsmExecRecord, saved_rsp);
    size_t off_rbp = offsetof(AsmExecRecord, saved_rbp);
    size_t epilogue_pos = 0, recovery_pos = 0;
    size_t user_addr_pos = 0, mid_pos = 0;
    int i;

    b->p = tramp; b->cap = cap; b->len = 0;
    job->trampoline = tramp;

    /* ---- prologue ---- */
    {
        static const uint8_t pro[] = {
            0x55,                   /* push ebp */
            0x8B, 0xEC,             /* mov ebp, esp */
            0x53,                   /* push ebx */
            0x56,                   /* push esi */
            0x57                    /* push edi */
        };
        asmemit_bytes(b, pro, sizeof(pro));

        asmemit_byte(b, 0xB8);    /* mov eax, rec */
        asmemit_d32(b, (int32_t)(uintptr_t)&job->rec);
        asmemit_byte(b, 0xC7); asmemit_byte(b, 0x80);
        asmemit_d32(b, (int32_t)off_active);
        asmemit_d32(b, 1);
        e86_store_eax(b, ASMR_RSP & 7, off_rsp);
        e86_store_eax(b, ASMR_RBP & 7, off_rbp);

        /* mov eax, <user_region>; call eax —— 用户代码当函数调用（ret 由网关接住） */
        asmemit_byte(b, 0xB8);
        user_addr_pos = b->len;
        asmemit_d32(b, 0);        /* 占位，回填 */
        asmemit_byte(b, 0xFF); asmemit_byte(b, 0xD0);   /* call eax */
    }

    /* ---- mid capture（网关跳到这里） ---- */
    mid_pos = b->len;
    {
        int k;
        uint32_t mid_va = (uint32_t)(uintptr_t)(tramp + mid_pos);
        for (k = 0; k < 4; k++)
            gate_slot[k] = (uint8_t)((mid_va >> (8 * k)) & 0xFF);
    }
    e86_capture_regs(b, job, 0);

    /* ---- call seq（cdecl） ---- */
    if (job->rec.call_target != NULL)
    {
        int nargs = job->rec.arg_count;
        for (i = nargs - 1; i >= 0; i--)
        {
            asmemit_byte(b, 0x68);                        /* push imm32 */
            asmemit_d32(b, (int32_t)(uint32_t)job->rec.args[i]);
        }
        asmemit_byte(b, 0xB8);
        asmemit_d32(b, (int32_t)(uintptr_t)job->rec.call_target);
        asmemit_byte(b, 0xFF); asmemit_byte(b, 0xD0);    /* call eax */
        if (nargs > 0)
        {
            int bytes = 4 * nargs;
            if (bytes <= 127)
            {
                asmemit_byte(b, 0x83); asmemit_byte(b, 0xC4); asmemit_byte(b, (uint8_t)bytes);
            }
            else
            {
                asmemit_byte(b, 0x81); asmemit_byte(b, 0xC4);
                asmemit_d32(b, bytes);
            }
        }
    }

    /* ---- final capture ---- */
    e86_capture_regs(b, job, 1);

    /* ---- epilogue ---- */
    epilogue_pos = b->len;
    e86_common_tail(b, off_active, off_rsp);

    /* ---- recovery stub ---- */
    recovery_pos = b->len;
    e86_common_tail(b, off_active, off_rsp);

    /* ---- 拷贝用户代码到常驻区 + 回填 user 地址 ---- */
    {
        uint32_t user_va;
        int k;
        memset(user_region, 0xC3, ASMEXEC_TRAMP_OFF - ASMEXEC_CODE_OFF);
        memcpy(user_region, job->user_code, job->user_len);
        user_va = (uint32_t)(uintptr_t)user_region;
        for (k = 0; k < 4; k++)
            b->p[user_addr_pos + k] = (uint8_t)((user_va >> (8 * k)) & 0xFF);
    }

    /* 回填两处 mov eax, rec（+1 为立即数位置） */
    {
        uint32_t recv = (uint32_t)(uintptr_t)&job->rec;
        int k;
        for (k = 0; k < 4; k++)
        {
            b->p[epilogue_pos + 1 + k] = (uint8_t)((recv >> (8 * k)) & 0xFF);
            b->p[recovery_pos + 1 + k] = (uint8_t)((recv >> (8 * k)) & 0xFF);
        }
    }
    job->recovery = tramp + recovery_pos;
    job->rec.recovery_rip = tramp + recovery_pos;

    return b->len;
}

#endif /* _WIN64 */

/* ============ 模块入口（供 exec 模块调用） ============ */

size_t asmtramp_build(AsmExecJob *job)
{
    uint8_t *page = (uint8_t *)job->exec_page;
#ifdef _WIN64
    return build_trampoline_x64(job, page + ASMEXEC_TRAMP_OFF,
                                page + ASMEXEC_CODE_OFF,
                                page + ASMEXEC_GATE_OFF + 6,
                                ASMHOST_PAGE_SIZE - ASMEXEC_TRAMP_OFF);
#else
    return build_trampoline_x86(job, page + ASMEXEC_TRAMP_OFF,
                                page + ASMEXEC_CODE_OFF,
                                page + ASMEXEC_GATE_OFF + 6,
                                ASMHOST_PAGE_SIZE - ASMEXEC_TRAMP_OFF);
#endif
}
