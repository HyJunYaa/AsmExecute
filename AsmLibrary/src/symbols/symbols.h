/*
 * symbols/symbols.h - 符号表模块接口
 */
#ifndef ASMHOST_SYMBOLS_H
#define ASMHOST_SYMBOLS_H

#include "core/types.h"

/* 注册符号（返回槽索引，-1 = 失败/表满） */
int  asmhost_symbols_add(const char *name, void *addr);
int  asmhost_symbols_count(void);
int  asmhost_symbols_get(const char *name, uint64_t *addr_out);
void asmhost_symbols_clear(void);
/* 枚举（cb 返回 0 停止）；返回当前符号总数 */
int  asmhost_symbols_enum(asmhost_sym_cb cb, void *user);

#endif /* ASMHOST_SYMBOLS_H */
