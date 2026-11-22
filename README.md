# AsmExecute — 纯 C# 远程汇编执行工具集

一套"汇编编辑 → 校验 → 汇编 → 远程执行"的完整工具集，以 SharpDevelop 系 IDE 为开发载体
（.NET 4.0 legacy csproj + in-box MSBuild，兼容 Win7+）。零 C/C++ 依赖、零外部库。

> ⚠️ 仅用于**自有 / 已授权进程**的开发调试与学习研究。

## 两个工程（SharpDevelop 解决方案 asmexecute.sln）

| 工程 | 类型 | 说明 |
|---|---|---|
| **AsmStudio** | WinForms 窗体（C#） | 主程序：汇编编辑器（Intel/AT&T 双语法高亮与互相转换）、逐行校验、机器码 Hex 预览、远程执行、方案运行、多语言界面（English/简体中文）。支持撤回/重做、标签右键关闭/单独保存/关闭其他/全部关闭 |
| **GameSharp** | C# 控制台 | 冒烟测试靶场（模拟未知环境的"真实游戏"）：与主程序零共享，内部方法**不导出**、地址由 JIT 运行时产生，控制台只显示游戏状态、不泄露任何地址 |

方案脚本位于主程序工程的 `AsmStudio\Resources\Scheme\*.asmscheme`（构建时自动复制到输出目录 `Resources\`）。

## 构建与运行

- **批处理**：根目录 `build.bat [Debug^|Release] [x64^|x86^|all]` 一键编译两工程双架构（仅需系统内置 MSBuild）；`clean.bat` 清除产物
- **IDE**：SharpDevelop 打开 `asmexecute.sln` 直接构建

运行远程执行**无需目标加载任何 DLL**——无注入、无管道，目标进程里只有一段由主程序远程注册的纯汇编 VEH handler。

## 使用流程

1. **启动目标**：运行任意目标程序（如 GameSharp.exe，或你自己的进程）——无需预装任何组件
2. **启动主程序** AsmStudio.exe：【扫描进程】→ 选择目标 → 【连接】（默认 远程线程+VEH 模式）
3. 编辑汇编 →【验证 + 生成机器码】→【执行】。测试目标模拟未知环境：**内部符号不公开**，
   可调用的真实符号来自系统模块导出（符号下拉选模块载入，如 kernel32!GetCurrentProcessId），
   目标内部函数地址自行定位（扫描/逆向）
4. 右侧可勾选"执行后调用目标函数"直接以符号名调用（参数按目标 ABI 封送）
5. 【方案】菜单：冒烟方案默认扫描主程序运行目录的 Resources\（构建自动带入）——
   基础执行/AV 崩溃拦截/系统导出调用等，一键跑完
6. **崩溃安全性**：任意坏汇编码（空指针写/除零/非法指令）都会被 VEH handler 拦截，
   目标进程持续存活可继续测试；目标自身线程的异常不受影响（按 TEB 识别放行）

## 执行通道

| 通道 | 保护 | 说明 |
|---|---|---|
| **远程线程+VEH**（默认） | 汇编 VEH | 无 DLL 注入：纯汇编异常 handler 远程注册进目标，坏汇编码被拦截，目标不崩溃，异常现场回传 |
| **远程线程** | 无保护 | OpenProcess+VirtualAllocEx+CreateRemoteThread 兜底 |

VEH 保护原理说明：向量化异常处理是**每进程独立**的机制；远程目标的保护由"远程线程+VEH"通道的纯汇编 handler 实现——handler 只拦截**会话线程**（按 TEB 识别）且属于崩溃类的异常，目标自身线程的异常一律放行（见 Documents/architecture.md）。

## C# 自研汇编引擎（零外部依赖）

- x86/x64 保护模式编码；**Intel（NASM 风格）与 AT&T（GAS 风格）双语法**
- ~90 条常用指令：mov/lea/push/pop/xchg、算术逻辑组、移位组、jcc/call/jmp/setcc、
  串操作、bt 组、数据定义 db/dw/dd/dq、标签（两遍汇编，前向 rel32 / 后向 rel8 自动选择）
- 完整 ModRM/SIB 寻址、RIP 相对（`[rel lab]` / `[rip+lab]` / `lab(%rip)` 三种写法）、`[abs]` SIB 绝对寻址、moffs 特殊形、fs/gs 段前缀、lock/rep 前缀
- 低字节寄存器 `spl/bpl/sil/dil`（自动补空 REX 前缀；与 `ah/ch/dh/bh` 混用时报错而非静默错码）
- 符号表解析（目标模块导出 API 地址注入）
- **内置 149 条编码自检向量**（菜单 工具→自检），当前全部通过

## 文档

- `Documents/architecture.md` — 总体架构、远程执行通道与汇编 VEH 设计

## 兼容性

- C# 目标框架 .NET 4.0（Win7 装 .NET 4.0/4.8 即可），C# 5 语法
- 纯托管代码，无原生组件、无第三方库；构建仅需系统内置 MSBuild（无 C/C++ 工具链）
- Windows 7 / 8 / 10 / 11，x86 与 x64
