/*
 * util/util.h - 常用函数容器（供各模块复用）
 *
 *  - AsmEmit  字节发射器（trampoline/恢复桩生成用）
 *  - hex/数值解析（管道协议用）
 *  - 寄存器快照转文本
 *  - 通用互斥锁（types.h 声明类型，实现在此）
 */
#ifndef ASMHOST_UTIL_H
#define ASMHOST_UTIL_H

#include <stdio.h>
#include "core/types.h"

/* ---- 字节发射器 ---- */
typedef struct AsmEmit {
    uint8_t *p;        /* 输出缓冲（可为 NULL：仅统计长度） */
    size_t   cap;      /* 缓冲容量 */
    size_t   len;      /* 已写入长度（超出 cap 时只增不写） */
} AsmEmit;

void asmemit_byte(AsmEmit *e, uint8_t c);
void asmemit_bytes(AsmEmit *e, const uint8_t *src, size_t n);
void asmemit_d32(AsmEmit *e, int32_t v);   /* 小端 4 字节 */
void asmemit_d64(AsmEmit *e, uint64_t v);  /* 小端 8 字节 */

/* ---- hex / 数值解析（协议与补丁数据用） ---- */
int      asmutil_hex_val(char c);
size_t   asmutil_parse_hex_bytes(const char *s, uint8_t *out, size_t max);
uint64_t asmutil_parse_u64(const char *s);

/* ---- 其它 ---- */
void asmutil_put64(uint8_t *buf, size_t off, uint64_t v);
void asmutil_regs_to_str(const uint64_t *regs, char *buf, size_t cap);

#endif /* ASMHOST_UTIL_H */
