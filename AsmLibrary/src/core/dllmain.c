/*
 * core/dllmain.c - DLL 导出壳：公开 API → 模块函数的薄封装 + DllMain
 *
 * 实际逻辑按模块分文件夹:
 *   util/util.c        常用函数容器
 *   symbols/symbols.c  符号表
 *   veh/veh.c          VEH 保护
 *   exec/trampoline.c  trampoline 生成器 + 常驻页/网关
 *   exec/exec.c        沙箱执行
 *   pipe/pipe.c        命名管道服务
 */
#include "core/types.h"
#include "veh/veh.h"
#include "symbols/symbols.h"
#include "exec/exec.h"
#include "pipe/pipe.h"
#include "exec/trampoline.h"

/* ---- 基础 ---- */
ASMHOST_API int asmhost_version(void)
{
    return ASMHOST_VERSION;
}

ASMHOST_API int asmhost_arch(void)
{
#ifdef _WIN64
    return ASMHOST_ARCH_X64;
#else
    return ASMHOST_ARCH_X86;
#endif
}

ASMHOST_API void *asmhost_get_gate(void)
{
    return asmtramp_get_gate();
}

/* ---- VEH ---- */
ASMHOST_API int asmhost_install_veh(void)
{
    return asmhost_veh_install();
}

ASMHOST_API void asmhost_uninstall_veh(void)
{
    asmhost_veh_remove();
}

/* ---- 符号表 ---- */
ASMHOST_API int asmhost_register_symbol(const char *name, void *addr)
{
    return asmhost_symbols_add(name, addr);
}

ASMHOST_API int asmhost_symbol_count(void)
{
    return asmhost_symbols_count();
}

ASMHOST_API int asmhost_get_symbol(const char *name, uint64_t *addr_out)
{
    return asmhost_symbols_get(name, addr_out);
}

ASMHOST_API void asmhost_clear_symbols(void)
{
    asmhost_symbols_clear();
}

ASMHOST_API int asmhost_enum_symbols(asmhost_sym_cb cb, void *user)
{
    return asmhost_symbols_enum(cb, user);
}

/* ---- 管道服务 ---- */
ASMHOST_API int asmhost_start_pipe_server(const char *pipe_name)
{
    return asmhost_pipe_start(pipe_name);
}

ASMHOST_API void asmhost_stop_pipe_server(void)
{
    asmhost_pipe_stop();
}

ASMHOST_API int asmhost_pipe_is_running(void)
{
    return asmhost_pipe_running();
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)inst; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(inst);
    return TRUE;
}
