/*
 * pipe/pipe.c - 命名管道服务端 + 行协议实现
 *
 * 协议（文本行，\n 结尾，兼容 CRLF/无 BOM）：
 *   连接后服务端先发:  OK proto=1 arch=<4|8> pid=<pid> version=<n> gate=<hex>
 *   HELLO              → 同上
 *   SYMBOLS            → BEGIN \n SYM <name> <hexaddr> \n ... \n END count=<n>
 *   EXEC mode=<code|call> call=<hex> args=<a,b,...> timeout=<ms> code_hex=<HHHH...>
 *                      → OK ret=<hex> regs=<r=v,...> flags=<hex>
 *                        ERR status=<exc|timeout|busy|badarg|internal> [...]
 *   PING               → PONG arch=<4|8>
 *   SHUTDOWN           → BYE 并停止服务
 */
#include "pipe/pipe.h"
#include "core/types.h"
#include "exec/exec.h"
#include "symbols/symbols.h"
#include "exec/trampoline.h"
#include "util/util.h"

#include <stdio.h>

#define PIPE_BUF_SIZE 0x8000

static volatile LONG g_pipe_running = 0;
static HANDLE g_pipe_thread = NULL;
static HANDLE g_pipe_stop_event = NULL;
static char   g_pipe_name[256];

/* ---- EXEC 命令处理 ---- */
static void handle_exec(const char *line, char *resp, size_t resp_cap)
{
    uint8_t code[ASMHOST_MAX_CODE];
    size_t code_len = 0;
    uint64_t args[8];
    int nargs = 0;
    uint64_t call_target = 0;
    uint32_t timeout_ms = 3000;
    const char *p = line;
    int mode_call = 0;
    asmhost_result result;
    int rc;

    while (*p)
    {
        char key[32];
        char val[PIPE_BUF_SIZE / 2];
        const char *eq, *sp;
        size_t klen, vlen;

        while (*p == ' ') p++;
        if (!*p) break;
        sp = strchr(p, ' ');
        eq = strchr(p, '=');
        if (eq == NULL || (sp != NULL && eq > sp))
        {
            p = sp ? sp + 1 : p + strlen(p);
            continue;
        }
        klen = (size_t)(eq - p);
        if (klen >= sizeof(key)) klen = sizeof(key) - 1;
        memcpy(key, p, klen);
        key[klen] = '\0';
        p = eq + 1;
        sp = strchr(p, ' ');
        vlen = sp ? (size_t)(sp - p) : strlen(p);
        if (vlen >= sizeof(val)) vlen = sizeof(val) - 1;
        memcpy(val, p, vlen);
        val[vlen] = '\0';
        p = sp ? sp + 1 : p + strlen(p);

        if (_stricmp(key, "mode") == 0)
            mode_call = (_stricmp(val, "call") == 0);
        else if (_stricmp(key, "call") == 0)
            call_target = asmutil_parse_u64(val);
        else if (_stricmp(key, "timeout") == 0)
            timeout_ms = (uint32_t)asmutil_parse_u64(val);
        else if (_stricmp(key, "code_hex") == 0)
            code_len = asmutil_parse_hex_bytes(val, code, sizeof(code));
        else if (_stricmp(key, "args") == 0)
        {
            const char *q = val;
            nargs = 0;
            while (*q && nargs < 8)
            {
                args[nargs++] = asmutil_parse_u64(q);
                q = strchr(q, ',');
                if (q == NULL) break;
                q++;
            }
        }
    }

    if (!mode_call)
        call_target = 0;

    if (code_len == 0 && call_target == 0)
    {
        _snprintf(resp, resp_cap, "ERR status=badarg");
        return;
    }

    memset(&result, 0, sizeof(result));
    rc = asmhost_exec(code_len > 0 ? code : NULL, code_len,
                      nargs > 0 ? args : NULL, nargs,
                      call_target != 0 ? (void *)(uintptr_t)call_target : NULL,
                      timeout_ms, &result);

    if (rc == ASMHOST_OK)
    {
        char regs[1024];
        asmutil_regs_to_str(result.regs_final, regs, sizeof(regs));
        _snprintf(resp, resp_cap, "OK ret=%I64x regs=%s flags=%I64x",
                  result.ret_value, regs, result.flags_final);
    }
    else if (rc == ASMHOST_EXC)
    {
        char regs[1024];
        asmutil_regs_to_str(result.exc_regs, regs, sizeof(regs));
        _snprintf(resp, resp_cap,
                  "ERR status=exc code=%x addr=%I64x av=%d rw=%d regs=%s flags=%I64x",
                  result.exc_code, result.exc_addr,
                  result.exc_was_access_violation, result.exc_read_or_write,
                  regs, result.exc_flags);
    }
    else if (rc == ASMHOST_TIMEOUT)
        _snprintf(resp, resp_cap, "ERR status=timeout");
    else if (rc == ASMHOST_BUSY)
        _snprintf(resp, resp_cap, "ERR status=busy");
    else
        _snprintf(resp, resp_cap, "ERR status=internal rc=%d", rc);
}

/* ---- 符号枚举回调：把 SYM 行追加到缓冲 ---- */
typedef struct SymDump {
    char   *buf;
    size_t  cap;
    size_t *used;
    int     count;
} SymDump;

static int sym_dump_cb(const char *name, uint64_t addr, void *user)
{
    SymDump *d = (SymDump *)user;
    int w = _snprintf(d->buf + d->used[0], d->cap - d->used[0],
                      "SYM %s %I64x\n", name, addr);
    if (w > 0) d->used[0] += (size_t)w;
    d->count++;
    return 1;
}

static void write_line(HANDLE pipe, const char *text)
{
    char tmp[PIPE_BUF_SIZE];
    DWORD written;
    size_t l = strlen(text);
    if (l >= sizeof(tmp) - 1) l = sizeof(tmp) - 2;
    memcpy(tmp, text, l);
    tmp[l++] = '\n';
    tmp[l] = '\0';
    WriteFile(pipe, tmp, (DWORD)l, &written, NULL);
}

/* ---- 单客户端会话 ---- */
static void serve_client(HANDLE pipe)
{
    char req[PIPE_BUF_SIZE];
    char resp[PIPE_BUF_SIZE];
    DWORD n;
    size_t used = 0;
    DWORD mode = PIPE_READMODE_BYTE;

    SetNamedPipeHandleState(pipe, &mode, NULL, NULL);

    /* 欢迎行（gate = ret 重定向网关地址） */
    {
        char hello[192];
        _snprintf(hello, sizeof(hello),
                  "OK proto=1 arch=%d pid=%lu version=%d gate=%I64x",
                  asmhost_arch(), (unsigned long)GetCurrentProcessId(),
                  asmhost_version(),
                  (unsigned long long)(uintptr_t)asmtramp_get_gate());
        write_line(pipe, hello);
    }

    req[0] = '\0';
    for (;;)
    {
        char *nl;
        size_t linelen;
        size_t rawlen;
        char line[PIPE_BUF_SIZE];

        /* 取一行（缓冲内已有则直接处理，否则读） */
        nl = strchr(req, '\n');
        if (nl == NULL)
        {
            if (used >= sizeof(req) - 1) { used = 0; req[0] = '\0'; }
            if (!ReadFile(pipe, req + used, (DWORD)(sizeof(req) - 1 - used), &n, NULL) || n == 0)
                return;     /* 客户端断开 */
            used += n;
            req[used] = '\0';
            continue;
        }

        rawlen = (size_t)(nl - req);
        linelen = rawlen;
        if (linelen >= sizeof(line)) linelen = sizeof(line) - 1;
        memcpy(line, req, linelen);
        line[linelen] = '\0';
        /* 剥离行尾 \r/空格（客户端可能用 CRLF） */
        while (linelen > 0 && (line[linelen - 1] == '\r' || line[linelen - 1] == ' '))
            line[--linelen] = '\0';
        /* 剥离 UTF-8 BOM（防御） */
        if (linelen >= 3 && (unsigned char)line[0] == 0xEF &&
            (unsigned char)line[1] == 0xBB && (unsigned char)line[2] == 0xBF)
        {
            memmove(line, line + 3, linelen - 3);
            linelen -= 3;
            line[linelen] = '\0';
        }
        /* 消费原始行（含 \r 与 \n） */
        memmove(req, nl + 1, used - rawlen - 1);
        used -= (rawlen + 1);
        req[used] = '\0';

        if (_strnicmp(line, "HELLO", 5) == 0)
        {
            _snprintf(resp, sizeof(resp),
                      "OK proto=1 arch=%d pid=%lu version=%d gate=%I64x",
                      asmhost_arch(), (unsigned long)GetCurrentProcessId(),
                      asmhost_version(),
                      (unsigned long long)(uintptr_t)asmtramp_get_gate());
            write_line(pipe, resp);
        }
        else if (_strnicmp(line, "SYMBOLS", 7) == 0)
        {
            char big[PIPE_BUF_SIZE];
            size_t off = 0;
            SymDump d;
            DWORD written;
            big[0] = '\0';
            off += (size_t)_snprintf(big + off, sizeof(big) - off, "BEGIN\n");
            d.buf = big; d.cap = sizeof(big); d.used = &off; d.count = 0;
            asmhost_symbols_enum(sym_dump_cb, &d);
            off += (size_t)_snprintf(big + off, sizeof(big) - off,
                                     "END count=%d\n", d.count);
            WriteFile(pipe, big, (DWORD)off, &written, NULL);
        }
        else if (_strnicmp(line, "EXEC ", 5) == 0)
        {
            handle_exec(line + 5, resp, sizeof(resp));
            write_line(pipe, resp);
        }
        else if (_strnicmp(line, "PING", 4) == 0)
        {
            _snprintf(resp, sizeof(resp), "PONG arch=%d", asmhost_arch());
            write_line(pipe, resp);
        }
        else if (_strnicmp(line, "SHUTDOWN", 8) == 0)
        {
            write_line(pipe, "BYE");
            SetEvent(g_pipe_stop_event);
            return;
        }
        else
        {
            write_line(pipe, "ERR unknown command");
        }
    }
}

/* ---- 服务线程 ---- */
static DWORD WINAPI pipe_server_thread(LPVOID p)
{
    (void)p;
    for (;;)
    {
        HANDLE pipe;
        DWORD dummy_mode;

        if (WaitForSingleObject(g_pipe_stop_event, 0) == WAIT_OBJECT_0)
            break;

        pipe = CreateNamedPipeA(
            g_pipe_name,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,                      /* 单实例 */
            PIPE_BUF_SIZE, PIPE_BUF_SIZE,
            0, NULL);

        if (pipe == INVALID_HANDLE_VALUE)
            break;

        dummy_mode = PIPE_READMODE_BYTE;
        SetNamedPipeHandleState(pipe, &dummy_mode, NULL, NULL);

        /* 阻塞等连接（客户端断开即回环；停机由 SHUTDOWN 命令触发） */
        {
            BOOL ok = ConnectNamedPipe(pipe, NULL);
            if (!ok && GetLastError() != ERROR_PIPE_CONNECTED)
            {
                CloseHandle(pipe);
                continue;
            }
        }

        if (WaitForSingleObject(g_pipe_stop_event, 0) == WAIT_OBJECT_0)
        {
            CloseHandle(pipe);
            break;
        }

        serve_client(pipe);
        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
    InterlockedExchange(&g_pipe_running, 0);
    return 0;
}

int asmhost_pipe_start(const char *pipe_name)
{
    if (InterlockedCompareExchange(&g_pipe_running, 1, 0) == 1)
        return 0;

    if (pipe_name == NULL)
        _snprintf(g_pipe_name, sizeof(g_pipe_name), "\\\\.\\pipe\\asmexec_%lu",
                  (unsigned long)GetCurrentProcessId());
    else
    {
        strncpy(g_pipe_name, pipe_name, sizeof(g_pipe_name) - 1);
        g_pipe_name[sizeof(g_pipe_name) - 1] = '\0';
    }

    g_pipe_stop_event = CreateEventA(NULL, TRUE, FALSE, NULL);
    if (g_pipe_stop_event == NULL)
    {
        InterlockedExchange(&g_pipe_running, 0);
        return 0;
    }

    g_pipe_thread = CreateThread(NULL, 0, pipe_server_thread, NULL, 0, NULL);
    if (g_pipe_thread == NULL)
    {
        CloseHandle(g_pipe_stop_event);
        InterlockedExchange(&g_pipe_running, 0);
        return 0;
    }
    return 1;
}

void asmhost_pipe_stop(void)
{
    if (!g_pipe_running) return;
    SetEvent(g_pipe_stop_event);
    if (g_pipe_thread != NULL)
    {
        WaitForSingleObject(g_pipe_thread, 3000);
        CloseHandle(g_pipe_thread);
        g_pipe_thread = NULL;
    }
    CloseHandle(g_pipe_stop_event);
    g_pipe_stop_event = NULL;
    InterlockedExchange(&g_pipe_running, 0);
}

int asmhost_pipe_running(void)
{
    return g_pipe_running ? 1 : 0;
}
