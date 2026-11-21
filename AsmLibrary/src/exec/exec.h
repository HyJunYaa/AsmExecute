/*
 * exec/exec.h - 沙箱执行模块接口
 */
#ifndef ASMHOST_EXEC_H
#define ASMHOST_EXEC_H

#include "core/types.h"

/* 同步沙箱执行（内部创建工作线程运行 trampoline，可带超时）。
 * 返回: ASMHOST_OK / ASMHOST_EXC / ASMHOST_TIMEOUT / ... */
int asmhost_exec_run(const uint8_t *code, size_t code_len,
                     const uint64_t *args, int arg_count,
                     void *call_target, uint32_t timeout_ms,
                     asmhost_result *out);

/* 记录 → 公开结果结构拷贝 */
void asmhost_exec_fill_result(asmhost_result *out, AsmExecRecord *rec, int status);

#endif /* ASMHOST_EXEC_H */
