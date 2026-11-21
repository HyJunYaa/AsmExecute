/*
 * util/util.c - 常用函数容器实现
 */
#include "util/util.h"

/* ---- 通用互斥锁 ----
 * enter 自带惰性初始化（静态零初始化的 init 标志保证只初始化一次） */
void asmhost_lock_init(AsmLock *lock)
{
    if (!lock->init)
    {
        InitializeCriticalSection(&lock->cs);
        lock->init = 1;
    }
}

void asmhost_lock_enter(AsmLock *lock)
{
    if (!lock->init)
    {
        InitializeCriticalSection(&lock->cs);
        lock->init = 1;
    }
    EnterCriticalSection(&lock->cs);
}

void asmhost_lock_leave(AsmLock *lock)
{
    LeaveCriticalSection(&lock->cs);
}

/* ---- 字节发射器 ---- */
void asmemit_byte(AsmEmit *e, uint8_t c)
{
    if (e->len < e->cap) e->p[e->len] = c;
    e->len++;
}

void asmemit_bytes(AsmEmit *e, const uint8_t *src, size_t n)
{
    size_t i;
    for (i = 0; i < n; i++) asmemit_byte(e, src[i]);
}

void asmemit_d32(AsmEmit *e, int32_t v)
{
    asmemit_byte(e, (uint8_t)(v & 0xFF));
    asmemit_byte(e, (uint8_t)((v >> 8) & 0xFF));
    asmemit_byte(e, (uint8_t)((v >> 16) & 0xFF));
    asmemit_byte(e, (uint8_t)((v >> 24) & 0xFF));
}

void asmemit_d64(AsmEmit *e, uint64_t v)
{
    int i;
    for (i = 0; i < 8; i++)
        asmemit_byte(e, (uint8_t)((v >> (8 * i)) & 0xFF));
}

/* ---- hex / 数值解析 ---- */
int asmutil_hex_val(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

size_t asmutil_parse_hex_bytes(const char *s, uint8_t *out, size_t max)
{
    size_t n = 0;
    int hi = -1;
    while (*s && n < max)
    {
        int v = asmutil_hex_val(*s);
        if (v >= 0)
        {
            if (hi < 0) hi = v;
            else { out[n++] = (uint8_t)((hi << 4) | v); hi = -1; }
        }
        else if (*s == ' ' || *s == ',')
        {
            /* 分隔符跳过 */
        }
        else break;
        s++;
    }
    return n;
}

uint64_t asmutil_parse_u64(const char *s)
{
    if (s == NULL) return 0;
    if (s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
        return (uint64_t)_strtoui64(s + 2, NULL, 16);
    return (uint64_t)_strtoui64(s, NULL, 16);
}

/* ---- 其它 ---- */
void asmutil_put64(uint8_t *buf, size_t off, uint64_t v)
{
    int i;
    for (i = 0; i < 8; i++)
        buf[off + i] = (uint8_t)((v >> (8 * i)) & 0xFF);
}

void asmutil_regs_to_str(const uint64_t *regs, char *buf, size_t cap)
{
    static const char *names[ASMR_REGCOUNT] = {
        "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
        "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"
    };
    size_t used = 0;
    int i;
    buf[0] = '\0';
    for (i = 0; i < ASMR_REGCOUNT; i++)
    {
        int w = _snprintf(buf + used, cap - used, "%s%s=%I64x",
                          i ? "," : "", names[i], regs[i]);
        if (w < 0 || (size_t)w >= cap - used) break;
        used += (size_t)w;
    }
}
