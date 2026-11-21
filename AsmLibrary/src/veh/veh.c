/*
 * veh/veh.c - VEH 保护模块实现：异常捕获 + RIP 重定向恢复
 *
 * 原理：
 *  - AddVectoredExceptionHandler(1, ...) 安装为链首处理器。
 *  - 沙箱代码执行期间（rec->active == 1），任何异常都会先进入本处理器：
 *      1. 快照完整 CONTEXT（寄存器/标志/RIP）到记录
 *      2. 把 CONTEXT->Rip 重定向到恢复桩、Rsp/Rbp 恢复为已知好值
 *      3. 返回 EXCEPTION_CONTINUE_EXECUTION → 目标进程不崩溃
 *  - 恢复桩（trampoline 模块生成）负责回填结果并正常返回到调用者。
 */
#include "veh/veh.h"
#include "util/util.h"

static volatile LONG g_veh_installed = 0;
static PVOID g_veh_handle = NULL;
static DWORD g_tls_index = TLS_OUT_OF_INDEXES;

void asmhost_tls_set(AsmExecJob *job)
{
    if (g_tls_index != TLS_OUT_OF_INDEXES)
        TlsSetValue(g_tls_index, (LPVOID)job);
}

AsmExecJob *asmhost_tls_get(void)
{
    if (g_tls_index == TLS_OUT_OF_INDEXES)
        return NULL;
    return (AsmExecJob *)TlsGetValue(g_tls_index);
}

/* 从 CONTEXT 抓取通用寄存器到槽表 */
static void capture_ctx(CONTEXT *ctx, uint64_t *regs, uint64_t *flags)
{
#ifdef _WIN64
    regs[ASMR_RAX] = ctx->Rax;
    regs[ASMR_RCX] = ctx->Rcx;
    regs[ASMR_RDX] = ctx->Rdx;
    regs[ASMR_RBX] = ctx->Rbx;
    regs[ASMR_RSP] = ctx->Rsp;
    regs[ASMR_RBP] = ctx->Rbp;
    regs[ASMR_RSI] = ctx->Rsi;
    regs[ASMR_RDI] = ctx->Rdi;
    regs[ASMR_R8]  = ctx->R8;
    regs[ASMR_R9]  = ctx->R9;
    regs[ASMR_R10] = ctx->R10;
    regs[ASMR_R11] = ctx->R11;
    regs[ASMR_R12] = ctx->R12;
    regs[ASMR_R13] = ctx->R13;
    regs[ASMR_R14] = ctx->R14;
    regs[ASMR_R15] = ctx->R15;
    *flags = ctx->EFlags;
#else
    regs[ASMR_RAX] = ctx->Eax;
    regs[ASMR_RCX] = ctx->Ecx;
    regs[ASMR_RDX] = ctx->Edx;
    regs[ASMR_RBX] = ctx->Ebx;
    regs[ASMR_RSP] = ctx->Esp;
    regs[ASMR_RBP] = ctx->Ebp;
    regs[ASMR_RSI] = ctx->Esi;
    regs[ASMR_RDI] = ctx->Edi;
    *flags = ctx->EFlags;
#endif
}

LONG CALLBACK asmhost_veh_handler(PEXCEPTION_POINTERS ep)
{
    AsmExecJob *job = asmhost_tls_get();
    if (job == NULL || job->abandoned)
        return EXCEPTION_CONTINUE_SEARCH;
    if (!job->rec.active)
        return EXCEPTION_CONTINUE_SEARCH;

    PEXCEPTION_RECORD rec = ep->ExceptionRecord;
    PCONTEXT ctx = ep->ContextRecord;

    /* 快照异常现场 */
    capture_ctx(ctx, job->rec.exc_regs, &job->rec.exc_flags);
#ifdef _WIN64
    job->rec.exc_rip = (uint64_t)ctx->Rip;
#else
    job->rec.exc_rip = (uint64_t)ctx->Eip;
#endif
    job->rec.exc_code = rec->ExceptionCode;
    job->rec.exc_was_av = 0;
    job->rec.exc_rw = -1;
    if (rec->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && rec->NumberParameters >= 2)
    {
        job->rec.exc_was_av = 1;
        job->rec.exc_rw = (int)rec->ExceptionInformation[0];
        job->rec.exc_addr = rec->ExceptionInformation[1];
    }
    else
    {
        job->rec.exc_addr = (uint64_t)(uintptr_t)rec->ExceptionAddress;
    }
    job->rec.failed = 1;

    /* RIP 重定向到恢复桩，栈恢复为已知好值 */
#ifdef _WIN64
    ctx->Rip = (DWORD64)job->rec.recovery_rip;
    ctx->Rsp = job->rec.saved_rsp;
    ctx->Rbp = job->rec.saved_rbp;
#else
    ctx->Eip = (DWORD)job->rec.recovery_rip;
    ctx->Esp = (DWORD)job->rec.saved_rsp;
    ctx->Ebp = (DWORD)job->rec.saved_rbp;
#endif
    return EXCEPTION_CONTINUE_EXECUTION;
}

int asmhost_veh_install(void)
{
    if (InterlockedCompareExchange(&g_veh_installed, 1, 0) == 1)
        return 0;   /* 已安装 */

    if (g_tls_index == TLS_OUT_OF_INDEXES)
        g_tls_index = TlsAlloc();

    g_veh_handle = AddVectoredExceptionHandler(1, asmhost_veh_handler);
    if (g_veh_handle == NULL)
    {
        InterlockedExchange(&g_veh_installed, 0);
        return -1;
    }
    return 1;
}

void asmhost_veh_remove(void)
{
    if (InterlockedCompareExchange(&g_veh_installed, 0, 1) == 1)
    {
        if (g_veh_handle != NULL)
        {
            RemoveVectoredExceptionHandler(g_veh_handle);
            g_veh_handle = NULL;
        }
    }
}
