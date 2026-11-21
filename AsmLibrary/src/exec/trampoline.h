/*
 * exec/trampoline.h - trampoline 生成器 + 常驻执行页/网关模块接口
 */
#ifndef ASMHOST_TRAMPOLINE_H
#define ASMHOST_TRAMPOLINE_H

#include "core/types.h"

/* 为一次 EXEC 生成 trampoline + 恢复桩（写入常驻页 TRAMP_OFF 区域），
 * 拷贝用户代码到常驻区（CODE_OFF，末尾自动补 ret），
 * 并把网关槽指向本次的 mid capture。
 * 返回 trampoline 长度（0 = 失败）。 */
size_t asmtramp_build(AsmExecJob *job);

/* 返回常驻网关地址（首次调用分配常驻页）。NULL = 分配失败。 */
void *asmtramp_get_gate(void);

#endif /* ASMHOST_TRAMPOLINE_H */
