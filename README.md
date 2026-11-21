# AsmExecute — C#/C++ 混合远程汇编执行工具集

一套"汇编编辑 → 校验 → 汇编 → 远程执行"的完整工具集，以 SharpDevelop 系 IDE 为开发载体
（.NET 4.0 legacy csproj + in-box MSBuild，兼容 Win7+）。

> ⚠️ 仅用于**自有 / 已授权进程**的开发调试与学习研究。

## 四个工程（SharpDevelop 解决方案 asmexecute.sln）

| 工程 | 类型 | 说明 |
|---|---|---|
| **AsmStudio** | C# 控制台模板（含 WinForms 设计器窗体） | 主程序：汇编编辑器（Intel/AT&T 双语法高亮）、逐行校验、机器码 Hex 预览、远程执行、方案运行。Debug=Exe 控制台可看日志；Release=WinExe 纯窗体（csproj 条件 OutputType）。支持撤回/重做、标签右键关闭 |
| **AsmLibrary** | CppBinding C++ 动态库（C 源） | 内库（x86+x64 同名分目录 AsmLibrary.dll）：加载进目标进程，VEH 沙箱执行 + 符号表 + 命名管道服务。第三方程序可直接调用 |
| **TargetCpp** | CppBinding C++ 控制台 | 目标对象（控制台迷你游戏）：公开游戏风格函数/数据地址供主程序测试调用 |
| **TargetSharp** | C# 控制台（需允许不安全代码） | 同上的 C# 版本：演示托管方法通过 `GetFunctionPointerForDelegate` 公开原生可调用指针 |

方案脚本位于主程序工程的 `AsmStudio\Resources\*.asmscheme`（构建时自动复制到输出目录 `bin\debug\Resources\`）。

## 构建与运行（SharpDevelop IDE 内完成）

在 SharpDevelop 打开 `asmexecute.sln` 直接构建：

- **C# 工程**：内置 MSBuild，Debug 下产物在各工程 `bin\debug\`
- **原生工程**（CppBinding）：使用 g++ 编译器配置（x64/x86 平台在工具栏切换）

运行目标前，确保 `AsmLibrary.dll`（对应架构版本）与 TargetCpp.exe / TargetSharp.exe 同目录。

## 使用流程

1. **启动目标**：运行 TargetCpp.exe 或 TargetSharp.exe——目标打印符号地址表并开启命名管道 `\\.\pipe\asmexec_<pid>`
2. **启动主程序** AsmStudio.exe：【扫描进程】→ 选择目标 → 【连接】（默认 管道+DLL 模式，VEH 全保护）
3. 编辑汇编（如 `mov rax, GameAddScore` + `mov rcx, 5` + `call rax` + `ret`）→【验证 + 生成机器码】→【执行】
4. 右侧可勾选"执行后调用目标函数"直接以符号名调用（参数按目标 ABI 封送）
5. 【方案】菜单：默认扫描主程序运行目录的 Resources\（构建自动带入），也可浏览选择任意包含 `*.asmscheme` 的文件夹，按顺序执行方案步骤

## 执行通道

| 通道 | 保护 | 说明 |
|---|---|---|
| **管道+DLL**（默认） | VEH 全保护 | 目标自加载 AsmLibrary.dll；坏汇编码被 VEH 捕获，目标不崩溃，完整寄存器现场回传 |
| **远程线程** | 无保护 | OpenProcess+VirtualAllocEx+CreateRemoteThread 兜底，适用于未加载 DLL 的进程 |
| **本进程** | VEH 沙箱 | 主程序内直接执行（快速试验），需 `AsmLibrary.dll` 在主程序运行目录 |

## C# 自研汇编引擎（零外部依赖）

- x86/x64 保护模式编码；**Intel（NASM 风格）与 AT&T（GAS 风格）双语法**
- ~90 条常用指令：mov/lea/push/pop/xchg、算术逻辑组、移位组、jcc/call/jmp/setcc、
  串操作、bt 组、数据定义 db/dw/dd/dq、标签（两遍汇编，前向 rel32 / 后向 rel8 自动选择）
- 完整 ModRM/SIB 寻址、RIP 相对、`[abs]` SIB 绝对寻址、moffs 特殊形、fs/gs 段前缀、lock/rep 前缀
- 符号表解析（目标函数地址注入）；远程模式自动把 `ret` 改写为网关跳转（见 Documents/architecture.md）
- **内置 139 条编码自检向量**（菜单 工具→自检），当前全部通过

## AsmLibrary 公开 API（第三方调用）

```c
#include "asmhost.h"
asmhost_install_veh();
asmhost_register_symbol("MyFunc", MyFunc);
asmhost_start_pipe_server(NULL);          // 可选：供主程序远程调用
asmhost_exec(code, len, args, nargs, call_target, timeout_ms, &result);
```

## 文档

- `Documents/architecture.md` — 总体架构、VEH 沙箱与 trampoline 设计、ret 网关机制
- `Documents/protocol.md` — 管道协议（第三方接入规范）

## 兼容性

- C# 目标框架 .NET 4.0（Win7 装 .NET 4.0/4.8 即可），C# 5 语法
- 原生静态链接，无运行库 DLL 依赖；仅用 XP/Vista 级 Win32 API
- Windows 7 / 8 / 10 / 11，x86 与 x64
