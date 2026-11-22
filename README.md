# AsmExecute — C#/C++ 混合远程汇编执行工具集

一套"汇编编辑 → 校验 → 汇编 → 远程执行"的完整工具集，以 SharpDevelop 系 IDE 为开发载体
（.NET 4.0 legacy csproj + in-box MSBuild，兼容 Win7+）。

> ⚠️ 仅用于**自有 / 已授权进程**的开发调试与学习研究。

## 四个工程（SharpDevelop 解决方案 asmexecute.sln）

| 工程 | 类型 | 说明 |
|---|---|---|
| **AsmStudio** | WinForms 窗体（C#） | 主程序：汇编编辑器（Intel/AT&T 双语法高亮与互相转换）、逐行校验、机器码 Hex 预览、远程执行、方案运行、多语言界面（English/简体中文）。支持撤回/重做、标签右键关闭/单独保存/关闭其他/全部关闭 |
| **AsmLibrary** | CppBinding C++ 动态库（C 源） | 进程内执行引擎（x86+x64 同名分目录 AsmLibrary.dll）：加载进宿主进程后提供 VEH 沙箱执行 + 符号表 + 命名管道服务，第三方宿主可直接调用 |
| **TargetCpp** | CppBinding C++ 控制台 | 演示目标（控制台迷你游戏）：自加载 AsmLibrary.dll，公开游戏风格函数/数据地址供主程序测试调用 |
| **TargetSharp** | C# 控制台（需允许不安全代码） | 同上的 C# 版本：演示托管方法通过 `GetFunctionPointerForDelegate` 公开原生可调用指针 |

方案脚本位于主程序工程的 `AsmStudio\Resources\Scheme\*.asmscheme`（构建时自动复制到输出目录 `Resources\`）。

## 构建与运行

- **批处理**：根目录 `build.bat [Debug^|Release] [x64^|x86^|all]` 一键编译四工程双架构；`clean.bat` 清除产物
- **IDE**：SharpDevelop 打开 `asmexecute.sln` 直接构建（原生工程用 CppBinding 的 g++ 配置）

运行远程执行前无需目标加载任何 DLL；仅"本进程"模式需要 `AsmLibrary.dll` 在主程序运行目录。

## 使用流程

1. **启动目标**：运行任意目标程序（如 TargetCpp.exe，或你自己的进程）——无需预装任何组件
2. **启动主程序** AsmStudio.exe：【扫描进程】→ 选择目标 → 【连接】（默认 远程线程+VEH 模式）
3. 编辑汇编（如 `mov rax, GameAddScore` + `mov rcx, 5` + `call rax` + `ret`）→【验证 + 生成机器码】→【执行】
4. 右侧可勾选"执行后调用目标函数"直接以符号名调用（参数按目标 ABI 封送）；符号下拉可浏览目标各模块的导出 API
5. 【方案】菜单：默认扫描主程序运行目录的 Resources\（构建自动带入），也可浏览选择任意包含 `*.asmscheme` 的文件夹，按顺序执行方案步骤

## 执行通道

| 通道 | 保护 | 说明 |
|---|---|---|
| **远程线程+VEH**（默认） | 汇编 VEH | 无 DLL 注入：纯汇编异常 handler 远程注册进目标，坏汇编码被拦截，目标不崩溃，异常现场回传 |
| **远程线程** | 无保护 | OpenProcess+VirtualAllocEx+CreateRemoteThread 兜底 |
| **本进程** | VEH 沙箱 | 主程序内直接执行（快速试验），需 `AsmLibrary.dll` 在主程序运行目录 |

VEH 保护原理说明：向量化异常处理是**每进程独立**的机制，AsmLibrary.dll 只有被加载进某进程才能保护该进程；对远程目标的保护由"远程线程+VEH"通道的纯汇编 handler 实现（见 Documents/architecture.md）。

## C# 自研汇编引擎（零外部依赖）

- x86/x64 保护模式编码；**Intel（NASM 风格）与 AT&T（GAS 风格）双语法**
- ~90 条常用指令：mov/lea/push/pop/xchg、算术逻辑组、移位组、jcc/call/jmp/setcc、
  串操作、bt 组、数据定义 db/dw/dd/dq、标签（两遍汇编，前向 rel32 / 后向 rel8 自动选择）
- 完整 ModRM/SIB 寻址、RIP 相对、`[abs]` SIB 绝对寻址、moffs 特殊形、fs/gs 段前缀、lock/rep 前缀
- 符号表解析（目标模块导出 API 地址注入）
- **内置 139 条编码自检向量**（菜单 工具→自检），当前全部通过

## AsmLibrary 公开 API（第三方宿主调用）

```c
#include "asmhost.h"
asmhost_install_veh();
asmhost_register_symbol("MyFunc", MyFunc);
asmhost_start_pipe_server(NULL);          // 可选：对外提供命名管道服务
asmhost_exec(code, len, args, nargs, call_target, timeout_ms, &result);
```

## 文档

- `Documents/architecture.md` — 总体架构、VEH 沙箱与 trampoline 设计
- `Documents/protocol.md` — 管道协议（第三方接入规范）

## 兼容性

- C# 目标框架 .NET 4.0（Win7 装 .NET 4.0/4.8 即可），C# 5 语法
- 原生静态链接，无运行库 DLL 依赖；仅用 XP/Vista 级 Win32 API
- Windows 7 / 8 / 10 / 11，x86 与 x64
