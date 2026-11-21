/*
 * exec/exec.c - 沙箱执行入口（工作线程 + 超时控制 + 结果拷贝）
 */
#include "exec/exec.h"
#include "veh/veh.h"
#include "exec/trampoline.h"
#include "util/util.h"

static DWORD WINAPI exec_worker(LPVOID p)
{
    AsmExecJob *job = (AsmExecJob *)p;
    asmhost_tls_set(job);
    ((void (*)(void))job->trampoline)();
    asmhost_tls_set(NULL);
    SetEvent(job->done_event);
    return 0;
}

int asmhost_exec_internal(AsmExecJob *job, uint32_t timeout_ms, asmhost_result *out)
{
    DWORD wait;
    int status;

    job->done_event = CreateEventA(NULL, TRUE, FALSE, NULL);
    if (job->done_event == NULL)
        return ASMHOST_INTERNAL;

    {
        HANDLE h = CreateThread(NULL, 0, exec_worker, job, 0, NULL);
        if (h == NULL)
        {
            CloseHandle(job->done_event);
            return ASMHOST_INTERNAL;
        }
        CloseHandle(h);   /* 分离：超时语义由放弃的线程承担 */
    }

    wait = WaitForSingleObject(job->done_event,
                               timeout_ms == 0 ? INFINITE : (DWORD)timeout_ms);
    if (wait == WAIT_OBJECT_0)
    {
        status = job->rec.failed ? ASMHOST_EXC : ASMHOST_OK;
        asmhost_exec_fill_result(out, &job->rec, status);
    }
    else
    {
        InterlockedExchange(&job->abandoned, 1);
        status = ASMHOST_TIMEOUT;
        asmhost_exec_fill_result(out, &job->rec, status);
        return status;
    }
    return status;
}

ASMHOST_API int asmhost_exec(const uint8_t *code, size_t code_len,
                             const uint64_t *args, int arg_count,
                             void *call_target, uint32_t timeout_ms,
                             asmhost_result *out)
{
    AsmExecJob *job;
    uint8_t *page;
    size_t need;
    int rc;

    if (code_len > ASMEXEC_MAX_USER)
        return ASMHOST_BADARG;
    if (code == NULL && call_target == NULL)
        return ASMHOST_BADARG;
    if (arg_count < 0 || arg_count > 8)
        return ASMHOST_BADARG;
    if (asmhost_tls_get() != NULL)
        return ASMHOST_BUSY;   /* 不支持嵌套执行 */

    /* asmtramp_get_gate 返回页内网关地址，页基 = gate - GATE_OFF（未分配则分配） */
    {
        void *gate = asmtramp_get_gate();
        if (gate == NULL)
            return ASMHOST_INTERNAL;
        page = (uint8_t *)gate - ASMEXEC_GATE_OFF;
    }

    job = (AsmExecJob *)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(AsmExecJob));
    if (job == NULL)
        return ASMHOST_INTERNAL;
    job->exec_page = page;
    job->page_size = ASMHOST_PAGE_SIZE;
    job->user_code = code;
    job->user_len = code_len;
    job->rec.call_target = call_target;
    job->rec.arg_count = arg_count;
    if (args != NULL && arg_count > 0)
    {
        int i;
        for (i = 0; i < arg_count; i++) job->rec.args[i] = args[i];
    }

    need = asmtramp_build(job);
    if (need == 0 || need >= ASMHOST_PAGE_SIZE - ASMEXEC_TRAMP_OFF)
    {
        HeapFree(GetProcessHeap(), 0, job);
        return ASMHOST_INTERNAL;
    }
    FlushInstructionCache(GetCurrentProcess(), page,
                          ASMEXEC_CODE_OFF + code_len + 64);

    rc = asmhost_exec_internal(job, timeout_ms, out);

    HeapFree(GetProcessHeap(), 0, job);
    /* 常驻页保留（网关/用户区复用；超时放弃线程引用的内存不受影响） */
    return rc;
}

void asmhost_exec_fill_result(asmhost_result *out, AsmExecRecord *rec, int status)
{
    int i;
    if (out == NULL) return;
    memset(out, 0, sizeof(*out));
    out->status = status;
    out->ret_value = rec->regs_final[ASMR_RAX];
    for (i = 0; i < ASMR_REGCOUNT; i++)
    {
        out->regs_mid[i] = rec->regs_mid[i];
        out->regs_final[i] = rec->regs_final[i];
        out->exc_regs[i] = rec->exc_regs[i];
    }
    out->flags_mid = rec->flags_mid;
    out->flags_final = rec->flags_final;
    out->exc_flags = rec->exc_flags;
    out->exc_code = rec->exc_code;
    out->exc_addr = rec->exc_addr;
    out->exc_was_access_violation = rec->exc_was_av;
    out->exc_rip = rec->exc_rip;
    out->exc_read_or_write = rec->exc_rw;
}
