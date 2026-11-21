# AsmExecute 架构说明

## 总体数据流

```
AsmStudio (C#)                         TargetCpp / TargetSharp
 ┌──────────────────┐   ①扫描/连接     ┌──────────────────────────┐
 │ C# 自研汇编引擎   │                  │  游戏(血量/得分)          │
 │  Intel/AT&T      │                  │  GameAddScore/...        │
 │  → 机器码 hex     │  ②命名管道       │  ┌────────────────────┐ │
 │  PipeClient ─────┼─────────────────►│  │ AsmLibrary.dll (C)  │ │
 │  RemoteThread ───┼─ ③远程线程兜底    │  │  VEH 沙箱执行器      │ │
 └──────────────────┘                  │  │  符号表              │ │
                                       │  │  管道服务端          │ │
                                       │  └────────────────────┘ │
                                       └──────────────────────────┘
```

- **主模式（管道+DLL）**：目标启动即加载 AsmLibrary.dll、注册符号、开管道。
  主程序本地汇编 → EXEC 发送机器码 hex → AsmLibrary 沙箱执行 → 回传 ret/寄存器/异常。
- **兜底（远程线程）**：OpenProcess + VirtualAllocEx(RWX) + 写入 [stub][data][code]
  + CreateRemoteThread(stub)。stub 由主程序按目标位数生成（位置无关、参数以 imm 编入），
  执行结果写入 data 块后 ReadProcessMemory 取回。无 VEH 保护。
- **本进程**：主程序 P/Invoke 自身架构的 AsmLibrary.dll 直接执行（复用同一沙箱）。

## AsmLibrary 沙箱执行器（VEH + 恢复桩）

### 常驻执行页布局（每架构 DLL 一页）

```
+0x0000  网关桩:  jmp qword [rip+0] / jmp dword [abs32]
+0x0006  网关槽:  每次 EXEC 前写入 mid capture 地址
+0x0040  用户代码区:  EXEC 的机器码 + 末尾自动补 ret（最大 ~30KB）
+0x8000  trampoline + 恢复桩（每次 EXEC 重新生成）
```

### 一次 EXEC 的流程（x64）

1. trampoline 生成：`push rbp; mov rbp,rsp; push rbx/rsi/rdi/r12-r15;`
   `sub rsp,0x50`（shadow+栈参数）; `sub rsp,8`（用户入口 16 字节对齐）;
   `mov rcx,rec; [rec.active]=1; [rec.saved_rsp/rbp] 记录`;
   `mov rax,<用户区>; call rax`（把用户代码当函数调用）
2. 用户代码执行；`ret` 已被主程序引擎改写为"把网关地址写入 [rsp] 再 ret"
   → 跳到网关 → `jmp [gate槽]` → 回到 **mid capture**；
   用户不写 ret 则顺落入 mid capture（栈不平衡由 epilogue 归一修复）
3. **mid capture**：push rcx/pushfq/push rax → 逐寄存器存入 rec.regs_mid
   （lea 抓压栈前 rsp；栈序 pop 回填 flags/rax/rcx；还原被 scratch 占用的 rdx/rcx）
4. （mode=call）`mov rsp,[rec.saved_rsp]` 归一 → 按 ABI 封送参数 → `call 目标`
5. **final capture**：同 mid，存 rec.regs_final/flags_final
6. epilogue：`mov rsp,[rec.saved_rsp]; add rsp,0x58; pop r15..rbp; ret`
7. 工作线程 SetEvent → 主调线程等待（超时则放弃线程，标 TIMEOUT）

### VEH 防崩溃

- `AddVectoredExceptionHandler(1, ...)` 装为链首；执行线程经 TLS 定位当前 job
- 异常发生时（rec.active==1）：
  1. 快照完整 CONTEXT → rec.exc_regs/exc_flags/exc_rip + exc_code/exc_addr（AV 读写方向）
  2. `ctx.Rip = 恢复桩; ctx.Rsp = rec.saved_rsp; ctx.Rbp = rec.saved_rbp`
  3. 返回 EXCEPTION_CONTINUE_EXECUTION → **目标进程不崩溃**
- 恢复桩与 epilogue 相同（active=0、栈归一、成对出栈、ret），异常状态由 rec.failed 标记

### ret 网关（关键机制）

naive 内联代码的 `ret` 槽与它自己发起调用的 home space（x64 shadow slot-0）重叠：
被调函数（尤其 CLR 反向 P/Invoke thunk）会向 home space 溅射寄存器，破坏 ret 槽。
因此主程序引擎在远程模式下把所有 `ret` 改写为：

```
mov dword [rsp], gate_low32
mov dword [rsp+4], gate_high32     ; 仅 x64
ret
```

不占用任何寄存器；ret 跳到常驻页固定网关，网关 `jmp [槽]` 到本次执行的 mid capture。

### 参数封送

- x64：args[0..3] → RCX/RDX/R8/R9（imm64 编入 trampoline）；args[4..7] → [rsp+0x20+8k]
- x86：cdecl，args 从右向左 `push imm32`，调用后 `add esp, 4*n`

## C# 汇编引擎结构

```
Lexer（行切分/注释/数值）
  ├─ IntelParser（NASM 风格: [base+idx*scale+dsp]、byte ptr、fs:[..]、rel/abs）
  └─ AtAndTParser（GAS 风格: %reg/$imm/disp(b,i,s)/'*'间接/尺寸后缀/双后缀 movzbl 等）
        → 统一 Statement IR（Intel 顺序）
Assembler（两遍: 首遍定尺寸/标签偏移+前向粘性 rel32；次遍产出字节；
           后向 rel8 溢出自动升级 rel32 重汇编）
Encoder（REX/前缀/ModRM/SIB/位移/立即数/分支前向后向/moffs/SIB 绝对/ret 网关改写）
TestVectors（137 条已知编码对照向量）
```

## 目标对象公开的符号

函数：`GameAddScore / GameGetScore / GameHeal / GameGetHealth / GameDamage /
GameGetPos / GameSlowCall / GameCrashNow / VirtualProtect`
数据：`g_score / g_hp / g_pos[3]`

- TargetCpp：`extern "C" __declspec(dllexport)` 原生函数
- TargetSharp：静态方法 → `UnmanagedFunctionPointer(Cdecl)` 委托 →
  `GetFunctionPointerForDelegate` 原生指针（GCHandle 保活）；数据槽用 Pinned 数组

## 工程与构建体系

- **asmexecute.sln**（SharpDevelop/VS 均可打开）：4 个工程
  - AsmStudio.csproj / TargetSharp.csproj — legacy C# csproj（ToolsVersion 4.0，
    TargetFrameworkVersion v4.0，无 targeting pack 时自动回退运行时目录程序集）
  - AsmLibrary.vcxproj / TargetCpp.vcxproj — CppBinding 工程（IDE 内用 g++ 构建）
- **命令行权威构建**（build.bat）：
  - build-csharp.bat — in-box MSBuild v4.0.30319 编两个 csproj，产物聚合 bin\x64\
  - build-native.bat — MCF GCC 双工具链 `-static` 静态链接，
    产出 bin\x64\AsmLibrary.dll / bin\x86\AsmLibrary.dll（同名分目录）+ 双架构 TargetCpp.exe
