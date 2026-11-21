/*
 * core/types.h - 类型定义与公共常量（各模块共享）
 *
 * 本头只包含"数据"，不包含函数声明——函数接口见各模块头:
 *   util/util.h        常用函数容器（字节发射器/hex/数值解析/寄存器串化/通用锁）
 *   veh/veh.h          VEH 保护模块
 *   exec/trampoline.h  trampoline 生成器 + 常驻执行页/网关
 *   exec/exec.h        沙箱执行入口
 *   symbols/symbols.h  符号表模块
 *   pipe/pipe.h        命名管道服务模块
 */
#ifndef ASMHOST_TYPES_H
#define ASMHOST_TYPES_H

#include <windows.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

/* 构建内部：总是导出（公开头 asmhost.h 用它决定 dllexport） */
#ifndef ASMHOST_BUILD
#  define ASMHOST_BUILD
#endif

#include "asmhost.h"

/* ---- 常驻执行页布局 ----
 * +0x0000  网关桩:  jmp [槽]（槽 = 网关+6，每次 EXEC 写入 mid capture 地址）
 * +0x0040  用户代码区: EXEC 的机器码 + 末尾自动补 ret
 * +0x8000  trampoline + 恢复桩（每次 EXEC 重新生成） */
#define ASMHOST_PAGE_SIZE  0x10000
#define ASMHOST_MAX_CODE   0x8000
#define ASMEXEC_GATE_OFF   0x000
#define ASMEXEC_CODE_OFF   0x040
#define ASMEXEC_TRAMP_OFF  0x8000
#define ASMEXEC_MAX_USER   (ASMEXEC_TRAMP_OFF - ASMEXEC_CODE_OFF)

/* ---- 单次执行的记录（执行线程持有，VEH 通过 TLS 定位） ---- */
typedef struct AsmExecRecord {
    volatile LONG     active;        /* 1 = 沙箱代码正在执行 */
    volatile LONG     failed;        /* 1 = 发生异常并被恢复 */
    void             *recovery_rip;  /* 恢复桩入口 */
    uint64_t          saved_rsp;     /* 桩的已知好栈位置 */
    uint64_t          saved_rbp;
    void             *call_target;
    int               arg_count;
    uint64_t          args[8];

    /* 结果收集 */
    uint64_t          regs_mid[ASMR_REGCOUNT];
    uint64_t          regs_final[ASMR_REGCOUNT];
    uint64_t          flags_mid;
    uint64_t          flags_final;
    uint64_t          ret_value;
    uint64_t          exc_regs[ASMR_REGCOUNT];
    uint64_t          exc_flags;
    uint64_t          exc_rip;
    uint32_t          exc_code;
    uint64_t          exc_addr;
    int               exc_was_av;
    int               exc_rw;
} AsmExecRecord;

/* ---- 执行作业（工作线程参数） ---- */
typedef struct AsmExecJob {
    uint8_t          *exec_page;      /* 常驻执行页 */
    size_t            page_size;
    uint8_t          *trampoline;     /* 桩（页内 TRAMP_OFF） */
    uint8_t          *recovery;       /* 恢复桩（页内） */
    const uint8_t    *user_code;
    size_t            user_len;
    AsmExecRecord     rec;
    HANDLE            done_event;
    volatile LONG     abandoned;      /* 超时后放弃该线程 */
} AsmExecJob;

/* ---- 通用互斥锁容器（惰性初始化的关键节，实现在 util/util.c） ---- */
typedef struct AsmLock {
    CRITICAL_SECTION cs;
    int              init;
} AsmLock;

void asmhost_lock_init(AsmLock *lock);
void asmhost_lock_enter(AsmLock *lock);
void asmhost_lock_leave(AsmLock *lock);

#endif /* ASMHOST_TYPES_H */
