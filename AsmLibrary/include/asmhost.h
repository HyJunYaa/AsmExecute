/*
 * asmhost.h - AsmLibrary 远程汇编执行内核（公开 C API）
 *
 * 加载进目标进程后提供:
 *   1. VEH 保护沙箱执行（坏汇编码不崩溃目标进程，完整捕获异常与寄存器现场）
 *   2. 符号注册表（目标程序公开函数/数据地址，供主程序快速调用）
 *   3. 命名管道服务端（主程序远程发送执行请求）
 *
 * 可被第三方程序直接调用（本进程内使用，无需管道）。
 * DLL 文件名: AsmLibrary.dll（x86/x64 同名，分目录部署）。
 *
 * 仅用于自有/已授权进程的开发调试与学习用途。
 */
#ifndef ASMHOST_H
#define ASMHOST_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(ASMHOST_BUILD)
#  define ASMHOST_API __declspec(dllexport)
#else
#  define ASMHOST_API
#endif

/* ---- 版本与架构 ---- */
#define ASMHOST_VERSION 0x0100
#define ASMHOST_ARCH_X86 4
#define ASMHOST_ARCH_X64 8

/* ---- 执行状态码 ---- */
enum {
    ASMHOST_OK        = 0,   /* 成功 */
    ASMHOST_EXC       = 1,   /* 异常被 VEH 捕获（详见 exc_code/exc_addr） */
    ASMHOST_TIMEOUT   = 2,   /* 执行超时（工作线程可能仍在运行） */
    ASMHOST_BADARG    = 3,   /* 参数无效 */
    ASMHOST_BUSY      = 4,   /* 已有执行在进行 */
    ASMHOST_INTERNAL  = 5    /* 内部错误 */
};

/* 寄存器槽顺序（x86 只用低 8 个） */
enum {
    ASMR_RAX = 0, ASMR_RCX, ASMR_RDX, ASMR_RBX,
    ASMR_RSP, ASMR_RBP, ASMR_RSI, ASMR_RDI,
    ASMR_R8, ASMR_R9, ASMR_R10, ASMR_R11,
    ASMR_R12, ASMR_R13, ASMR_R14, ASMR_R15,
    ASMR_REGCOUNT = 16
};

/* 执行结果 */
typedef struct AsmHostResult {
    int         status;                 /* ASMHOST_* */
    uint64_t    ret_value;              /* 最终 RAX/EAX */
    uint64_t    regs_mid[ASMR_REGCOUNT];    /* 用户代码执行完（call 前）的寄存器 */
    uint64_t    regs_final[ASMR_REGCOUNT];  /* 最终寄存器（call 后或 code 退出时） */
    uint64_t    flags_mid;
    uint64_t    flags_final;
    /* 异常现场（status == ASMHOST_EXC 时有效） */
    uint64_t    exc_regs[ASMR_REGCOUNT];
    uint64_t    exc_flags;
    uint32_t    exc_code;               /* NTSTATUS，如 0xC0000005 */
    uint64_t    exc_addr;               /* 异常地址（访问违例时为访问的目标地址） */
    int         exc_was_access_violation;
    int         exc_read_or_write;      /* 0=读 1=写 8=DEP执行 */
    uint64_t    exc_rip;                /* 异常时 RIP */
} asmhost_result;

/* 符号枚举回调: 返回 0 停止枚举 */
typedef int (*asmhost_sym_cb)(const char *name, uint64_t addr, void *user);

/* ---- 基础 ---- */
ASMHOST_API int  asmhost_version(void);
ASMHOST_API int  asmhost_arch(void);

/* ---- ret 重定向网关 ----
 * 返回固定网关地址（首次调用时分配常驻执行页）。
 * 汇编引擎把用户代码中的 ret 改写为 "把该地址写入 [rsp] 再 ret"，
 * 使 naive 内联代码的 ret 与其调用的 home space 不再冲突。 */
ASMHOST_API void *asmhost_get_gate(void);

/* ---- VEH 保护 ---- */
ASMHOST_API int  asmhost_install_veh(void);      /* 幂等；1=新装 0=已装 */
ASMHOST_API void asmhost_uninstall_veh(void);

/* ---- 符号表 ---- */
ASMHOST_API int  asmhost_register_symbol(const char *name, void *addr);
ASMHOST_API int  asmhost_symbol_count(void);
ASMHOST_API int  asmhost_get_symbol(const char *name, uint64_t *addr_out);
ASMHOST_API void asmhost_clear_symbols(void);
ASMHOST_API int  asmhost_enum_symbols(asmhost_sym_cb cb, void *user);

/* ---- 沙箱执行 ----
 * code       : 机器码（主程序本地汇编后传入）
 * code_len   : 长度（字节）
 * args       : 参数数组（call_target 非空时按目标 ABI 封送: x64 = RCX,RDX,R8,R9 + 栈;
 *              x86 = cdecl 从右向左压栈）
 * arg_count  : 参数个数（0..8）
 * call_target: 可选。非空时: 先执行 code，再按 ABI 封送参数并 call 该地址。
 *              code 可为 NULL（纯调用）。
 * timeout_ms : 超时毫秒（0 = 无限等待）
 * out        : 结果（可为 NULL，则丢弃）
 * 返回: ASMHOST_OK / ASMHOST_EXC / ASMHOST_TIMEOUT / ASMHOST_BADARG / ...
 */
ASMHOST_API int  asmhost_exec(const uint8_t *code, size_t code_len,
                              const uint64_t *args, int arg_count,
                              void *call_target, uint32_t timeout_ms,
                              asmhost_result *out);

/* ---- 命名管道服务（供主程序远程调用） ----
 * pipe_name: NULL → 默认 "\\.\pipe\asmexec_<pid>"
 * 返回: 1 = 已启动, 0 = 失败/已在运行
 */
ASMHOST_API int  asmhost_start_pipe_server(const char *pipe_name);
ASMHOST_API void asmhost_stop_pipe_server(void);
ASMHOST_API int  asmhost_pipe_is_running(void);

#ifdef __cplusplus
}
#endif

#endif /* ASMHOST_H */
