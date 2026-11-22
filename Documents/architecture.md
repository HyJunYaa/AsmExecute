# AsmExecute 架构说明

## 总体数据流

```
AsmStudio (C#)                              目标进程（任意程序）
 ┌────────────────────────┐  ①扫描/连接     ┌──────────────────────────┐
 │ C# 自研汇编引擎         │                │  游戏逻辑 / 业务函数       │
 │  Intel/AT&T            │                │  （内部符号不公开）        │
 │  → 机器码              │  ②远程线程      │  ┌──────────────────────┐ │
 │  RemoteThreadExecutor ─┼───────────────►│  │ [stub][data][code]   │ │
 │  AsmVehProtector ──────┼─ ③远程注册      │  │  汇编 VEH handler     │ │
 │  （纯汇编 handler 页）   │    RWX 页       │  └──────────────────────┘ │
 └────────────────────────┘                └──────────────────────────┘
```

- **主模式（远程线程+VEH）**：目标无需加载任何组件。主程序远程写两页 RWX——
  VEH 页 `[handler][data 128B][regStub]`（远程线程调 AVEH 注册 handler），
  执行页 `[stub][data 64B][user code + ret]`（CreateRemoteThread(stub) 执行）。
  崩溃由 handler 在目标内重置上下文吞掉，宿主读回 data 块取异常现场。
- **兜底（远程线程）**：同一执行页，不注册 handler。坏汇编码即目标崩溃，仅用于对照/特殊场景。
- 无注入、无管道、无第三方 DLL 参与；符号全部来自目标模块的 PE 导出表（RemoteSymbols 读远程 PE）。

## 远程线程桩（RemoteThreadExecutor）

### 执行页布局（每次执行一页，0x1000 RWX）

```
+0x0000  stub：VEH 联动记录（savedEsp/Rsp、flag=1、TEB 存锚）→ push 保存寄存器
         → call [data+0]=用户码 → 结果经 VEH 块锚点重载指针后落 [data+8]
         → 收尾段(epilogueOff)：flag=0 → ExitThread（桩永不 ret）
+0x0040  data 64B：[+0]=code指针 [+8]=result指针 [+0x10]=ExitThread [+0x18]=callTarget
+0x0080+ 用户机器码 + 末尾自动补 ret
```

### 桩设计要点（全部实测教训）

- **绝不依赖被调方的寄存器存活**：用户码/目标函数可能毁掉 ebx/edi（unepic 实测不保 edi），
  结果落点指针从 VEH 块锚点 `[vehData+0x74]` 绝对寻址重载，不依赖 esp 也不依赖寄存器。
- **x64 桩以 ExitThread 收尾，永不 ret**；`call [rbx+16]` 内存间接形式实测会崩 → 用寄存器间接。
- **call 语义走"前奏"**：VEH 通道下 callTarget 拼进 userCode（寄存器赋参+call），>4 参数报 BadArg。

### 会话线程识别（TEB 锚）

桩入口把本线程 TEB（x86 `fs:[0x18]` / x64 `gs:[0x30]`）写入 VEH 块 `+0x68`。
handler 入口先比对当前 TEB：**非会话线程的异常一律放行**（只计 seenForeign）——
目标自身线程的合法内部异常（SEH/守卫页/C++ 异常）绝不被劫持、目标线程绝不被误杀。

## 汇编 VEH handler（AsmVeh）

### VEH 页布局

```
+0x0000  handler 机器码（x86 stdcall ret 4 / x64 MSx64）
+0x0070  data 128B（布局见 AsmVeh.cs 头注释）
+0x00F0  regStub：push 1; push handler; call AVEH → 句柄存 data+0x20 → ExitThread
```

### x86 关键事实（真机探针实锤）

- `EXCEPTION_POINTERS = { ER* +0, CTX* +4 }`（+8 是 x64 布局，勿照搬）
- `CONTEXT.Eip=0xB8、Esp=0xC4`（0xB0 是 Eax、0xBC 是 SegCs）
- handler 返回约定 **stdcall ret 4（0xC2 04 00）**——plain ret 会炸异常调度器，连 SEH 都走不到
- 注册桩压栈顺序 **push handler 先、push 1 后**（stdcall 从右向左），反序会把 handler 指针登记成 1

### 一次拦截的流程

1. 入口记录仪（寄存器全部取自 CONTEXT，非 live 值）：code/rip/8 个 GPR/EFlags/AV 访问地址/读写方向
   → data 块诊断槽
2. 过滤：会话线程（TEB ✓）+ flag==1 + 异常码 ∈ 崩溃类（AV/非法指令/除零/栈溢出/断点…）
3. OURS 路径：faultCount++、清 flag → `ctx.Eip = [data+0x18]`（桩收尾段）、
   `ctx.Esp = savedEsp-12`（等效 3 push 仍在栈上）→ CONTINUE_EXECUTION
4. 线程在收尾段 ExitThread 干净退出 → 宿主 Query 读回 faultCount/现场 → 目标进程存活

## C# 汇编引擎结构

```
Lexer（行切分/注释/数值）
  ├─ IntelParser（NASM 风格: [base+idx*scale+dsp]、byte ptr、fs:[..]、rel/abs）
  └─ AtAndTParser（GAS 风格: %reg/$imm/disp(b,i,s)/'*'间接/尺寸后缀/双后缀 movzbl 等）
        → 统一 Statement IR（Intel 顺序）
Assembler（两遍: 首遍定尺寸/标签偏移+前向粘性 rel32；次遍产出字节；
           后向 rel8 溢出自动升级 rel32 重汇编）
Encoder（REX/前缀/ModRM/SIB/位移/立即数/分支前向后向/moffs/SIB 绝对）
TestVectors（139 条已知编码对照向量）
```

## 测试目标（模拟未知环境）

GameSharp 是**冒烟测试靶场**（托管形态，刻意贴近真实程序）：

- 与主程序**零共享**：不加载任何主程序组件，无注入、无管道、无导出协作
- 内部方法**不导出**（真实游戏不会把内部地址给你），且方法地址由 JIT 运行时
  产生、每次启动都可能不同——最恶劣的未知目标形态
- 控制台只显示游戏状态本身（HP/Score），不显示任何地址
- 真实可调用的符号来自**系统模块导出**（kernel32/ntdll/…，符号面板按模块载入）；
  `{Symbol}` 占位符与 `call` 步骤均从该符号表解析

方案脚本（Resources\Scheme\*.asmscheme）即按此原则组织：
基础执行 / AT&T / x86 / AV 崩溃拦截 / 系统导出调用，全部不依赖目标内部符号。

## 工程与构建体系

- **asmexecute.sln**（SharpDevelop/VS 均可打开）：2 个工程
  - AsmStudio.csproj / GameSharp.csproj — legacy C# csproj（ToolsVersion 4.0，
    TargetFrameworkVersion v4.0，无 targeting pack 时自动回退运行时目录程序集）
- **命令行权威构建**（build.bat）：in-box MSBuild v4.0.30319 编两个 csproj（x64=AnyCPU /
  x86=Platform x86），产物聚合 bin\{Debug,Release}\{x64,x86}\；无任何 C/C++ 工具链依赖
