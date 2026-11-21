/*
 * symbols/symbols.c - 符号表模块实现（惰性锁 + 定容数组）
 */
#include "symbols/symbols.h"

#define ASMHOST_MAX_SYMBOLS 256
#define ASMHOST_MAX_SYMBOL_LEN 96

typedef struct SymEntry {
    char     name[ASMHOST_MAX_SYMBOL_LEN];
    uint64_t addr;
} SymEntry;

static SymEntry  g_symbols[ASMHOST_MAX_SYMBOLS];
static int       g_symbol_count = 0;
static AsmLock   g_sym_lock = { 0 };

int asmhost_symbols_add(const char *name, void *addr)
{
    int n;
    if (name == NULL || addr == NULL)
        return -1;
    asmhost_lock_enter(&g_sym_lock);
    n = g_symbol_count;
    if (n >= ASMHOST_MAX_SYMBOLS)
    {
        asmhost_lock_leave(&g_sym_lock);
        return -1;
    }
    strncpy(g_symbols[n].name, name, ASMHOST_MAX_SYMBOL_LEN - 1);
    g_symbols[n].name[ASMHOST_MAX_SYMBOL_LEN - 1] = '\0';
    g_symbols[n].addr = (uint64_t)(uintptr_t)addr;
    g_symbol_count = n + 1;
    asmhost_lock_leave(&g_sym_lock);
    return n;
}

int asmhost_symbols_count(void)
{
    int n;
    asmhost_lock_enter(&g_sym_lock);
    n = g_symbol_count;
    asmhost_lock_leave(&g_sym_lock);
    return n;
}

int asmhost_symbols_get(const char *name, uint64_t *addr_out)
{
    int i, found = 0;
    if (name == NULL || addr_out == NULL) return 0;
    asmhost_lock_enter(&g_sym_lock);
    for (i = 0; i < g_symbol_count; i++)
    {
        if (_stricmp(g_symbols[i].name, name) == 0)
        {
            *addr_out = g_symbols[i].addr;
            found = 1;
            break;
        }
    }
    asmhost_lock_leave(&g_sym_lock);
    return found;
}

void asmhost_symbols_clear(void)
{
    asmhost_lock_enter(&g_sym_lock);
    g_symbol_count = 0;
    asmhost_lock_leave(&g_sym_lock);
}

int asmhost_symbols_enum(asmhost_sym_cb cb, void *user)
{
    int i, keep = 1;
    asmhost_lock_enter(&g_sym_lock);
    for (i = 0; i < g_symbol_count && keep; i++)
        keep = cb(g_symbols[i].name, g_symbols[i].addr, user);
    asmhost_lock_leave(&g_sym_lock);
    return g_symbol_count;
}
