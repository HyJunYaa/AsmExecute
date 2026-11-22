// RemoteThreadExecutor.cs - 远程线程兜底执行（无 DLL）
// 原理: OpenProcess → VirtualAllocEx(RWX) → 写入 [stub][data][user code] →
//       CreateRemoteThread(stub) → stub 执行用户代码/调用目标 → 结果写入 data →
//       ReadProcessMemory 取回。
// stub 为运行期生成的位置无关机器码（x64 / x86 两套），参数以 imm 直接编入。
// vehData != 0 时启用汇编 VEH 联动: 桩入口记录 savedRsp 并置 flag=1，
//   崩溃由 AsmVeh handler 在 handler 内 ExitThread 终止故障线程（异常被吞，进程存活）。
using System;
using AsmStudio.Localization;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal static class RemoteThreadExecutor
    {
        /* data 布局 (x64): +0 code, +8 result, +16 ExitThread, +24 target, +32 结果槽;
         * SNAP_OFF(0x80) 起为"用户码执行后、call target 前"的寄存器快照:
         *   x64: 16 个 GPR 各 8 字节（0x80..0xFF）
         *   x86: 8 个 GPR 各 4 字节（0x80..0x9F）
         * args 由 stub 内 imm 直接编码 */

        /// <summary>data 块内寄存器快照区偏移</summary>
        internal const int SNAP_OFF = 0x80;
        /// <summary>data 块总长（前缀区 + 快照区）</summary>
        internal const int DATA_SIZE = 0x100;

        /// <summary>诊断开关: true 时把生成的 stub/veh data转储到 %TEMP%\asmstub.bin</summary>
        public static bool DumpStub;

        /// <summary>环境变量开关（ASMSTUDIO_DUMP_STUB=1）：无需改动代码即可现场抓桩字节</summary>
        private static readonly bool DumpStubFromEnv =
            Environment.GetEnvironmentVariable("ASMSTUDIO_DUMP_STUB") == "1";

        public static ExecutionResult Execute(ProcessInfo target, byte[] code,
            ulong[] args, int nargs, ulong callTarget, int timeoutMs, ulong vehData)
        {
            ExecutionResult r = new ExecutionResult();
            bool is64 = target.Is64Bit;

            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, target.Pid);
            if (proc == IntPtr.Zero)
            {
                r.Status = ExecStatus.Internal;
                r.Message = "OpenProcess FAILED (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                return r;
            }

            IntPtr remote = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            bool keepPage = false;   // 超时: 远程线程可能仍在页内取指，此时绝不可释放该页
            try
            {
                // 远程memory: [stub][DATA_SIZE data][user code + ret]
                byte[] userCode;
                ulong stubCallTarget = callTarget;
                if (vehData != 0 && callTarget != 0)
                {
                    // 汇编 VEH 通道: call 语义改由"调用后奏"实现（赋参 + call 目标），
                    // 拼接在用户码之后（与 asmhost.h/PipeDll 语义一致: 先执行 code 再 call 目标）；
                    // 不用 stub 的 callTarget 分支（vehData!=0 时恒 0）。
                    // 后奏必须按目标架构生成——x64 用寄存器赋参（最多 4 个，栈参数无法布置）；
                    // x86 用 cdecl 压栈（从右向左 + 调用方清栈），原版只写了 x64 字节，
                    // x86 目标上 mode=call 会把 x64 机器码当 x86 执行。
                    List<byte> pre = new List<byte>();
                    if (is64)
                    {
                        if (nargs > 4)
                        {
                            r.Status = ExecStatus.BadArg;
                            r.Message = "汇编 VEH 通道远程 call 暂只支持 4 个以内参数（x64）";
                            return r;
                        }
                        if (nargs > 0) { pre.AddRange(new byte[] { 0x48, 0xB9 }); AddQ(pre, args[0]); }  // movabs rcx
                        if (nargs > 1) { pre.AddRange(new byte[] { 0x48, 0xBA }); AddQ(pre, args[1]); }  // movabs rdx
                        if (nargs > 2) { pre.AddRange(new byte[] { 0x49, 0xB8 }); AddQ(pre, args[2]); }  // movabs r8
                        if (nargs > 3) { pre.AddRange(new byte[] { 0x49, 0xB9 }); AddQ(pre, args[3]); }  // movabs r9
                        pre.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(pre, callTarget);                  // movabs rax, target
                        pre.AddRange(new byte[] { 0xFF, 0xD0 });                                         // call rax
                    }
                    else
                    {
                        for (int i = nargs - 1; i >= 0; i--)
                        { pre.Add(0x68); AddD(pre, (uint)args[i]); }                                 // push imm32（从右向左）
                        pre.Add(0xB8); AddD(pre, (uint)callTarget);                                  // mov eax, target
                        pre.AddRange(new byte[] { 0xFF, 0xD0 });                                     // call eax
                        if (nargs > 0)
                        {
                            int bytes = 4 * nargs;                                                   // 调用方清栈
                            if (bytes <= 127) pre.AddRange(new byte[] { 0x83, 0xC4, (byte)bytes });
                            else { pre.AddRange(new byte[] { 0x81, 0xC4 }); AddD(pre, (uint)bytes); }
                        }
                    }
                    byte[] codeBuf = new byte[(code != null ? code.Length : 0) + pre.Count + 1];
                    if (code != null) Array.Copy(code, 0, codeBuf, 0, code.Length);
                    Array.Copy(pre.ToArray(), 0, codeBuf, code != null ? code.Length : 0, pre.Count);
                    codeBuf[codeBuf.Length - 1] = 0xC3;
                    userCode = codeBuf;
                    stubCallTarget = 0;
                }
                else
                {
                    userCode = new byte[(code != null ? code.Length : 0) + 1];
                    if (code != null) Array.Copy(code, userCode, code.Length);
                    userCode[userCode.Length - 1] = 0xC3;   // 兜底 ret
                }

                // ExitThread 必须取目标进程内的地址（读目标 kernel32 导出表，追踪转发到 ntdll）：
                // 本进程地址在跨位数时毫无意义，同架构直接借用也只是碰巧（ASLR 每次开机全局一致）。
                ulong exitThread = RemoteSymbols.ResolveTargetApi(target.Pid, target.Is64Bit,
                    "kernel32.dll", "ExitThread");
                if (exitThread == 0)
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = LanguageService.T("Cannot resolve ExitThread from the target export table");
                    return r;
                }
                int epilogueOff;
                byte[] stub = is64
                    ? BuildStubX64(userCode.Length, args, nargs, stubCallTarget, vehData, exitThread, out epilogueOff)
                    : BuildStubX86(userCode.Length, args, nargs, stubCallTarget, vehData, exitThread, out epilogueOff);

                int total = stub.Length + DATA_SIZE + userCode.Length;

                remote = NativeInterop.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)total,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_EXECUTE_READWRITE);
                if (remote == IntPtr.Zero)
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "VirtualAllocEx FAILED";
                    return r;
                }

                if (vehData != 0)
                {
                    // 告知 VEH handler 崩溃恢复点 = 桩收尾段起始（写 [vehData+0x18]）
                    byte[] resume = new byte[8];
                    ulong resumeRip = (ulong)(remote.ToInt64() + epilogueOff);
                    for (int i = 0; i < 8; i++) resume[i] = (byte)((resumeRip >> (8 * i)) & 0xFF);
                    UIntPtr w;
                    NativeInterop.WriteProcessMemory(proc,
                        new IntPtr(unchecked((long)vehData)) + 0x18, resume, (UIntPtr)8, out w);
                    // 清零本次运行的 faultCount（每次执行独立统计）
                    NativeInterop.WriteProcessMemory(proc,
                        new IntPtr(unchecked((long)vehData)) + 0x04, new byte[4], (UIntPtr)4, out w);
                }

                IntPtr dataBase = new IntPtr(remote.ToInt64() + stub.Length);
                IntPtr codeBase = new IntPtr(dataBase.ToInt64() + DATA_SIZE);

                // data: +0 code, +8 result, +16 ExitThread, +24 target
                byte[] data = new byte[DATA_SIZE];
                Put64(data, 0, is64 ? (ulong)codeBase.ToInt64() : (uint)codeBase.ToInt64());
                Put64(data, 8, is64 ? (ulong)dataBase.ToInt64() + 32u : (uint)(dataBase.ToInt64() + 32));
                Put64(data, 16, is64 ? exitThread : (uint)exitThread);
                Put64(data, 24, is64 ? callTarget : (uint)callTarget);

                // 写入三段
                UIntPtr written;
                if (!NativeInterop.WriteProcessMemory(proc, remote, stub, (UIntPtr)stub.Length, out written))
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(stub) FAILED"; return r; }
                if (!NativeInterop.WriteProcessMemory(proc, dataBase, data, (UIntPtr)DATA_SIZE, out written))
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(data) FAILED"; return r; }
                if (userCode.Length > 1 &&
                    !NativeInterop.WriteProcessMemory(proc, codeBase, userCode, (UIntPtr)userCode.Length, out written))
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(code) FAILED"; return r; }
                if (DumpStub || DumpStubFromEnv)
                {
                    // 全量转储（stub+data+userCode 均已写入），供离线反汇编核对
                    byte[] dbg = new byte[stub.Length + DATA_SIZE + userCode.Length];
                    Array.Copy(stub, dbg, stub.Length);
                    Array.Copy(data, 0, dbg, stub.Length, DATA_SIZE);
                    if (userCode.Length > 0)
                        Array.Copy(userCode, 0, dbg, stub.Length + DATA_SIZE, userCode.Length);
                    System.IO.File.WriteAllBytes(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asmstub.bin"), dbg);
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asmstub.txt"),
                        "stubLen=" + stub.Length + " blob=0x" + remote.ToInt64().ToString("X") +
                        " epilogueOff=" + epilogueOff + " dataBase=0x" + dataBase.ToInt64().ToString("X") +
                        " codeBase=0x" + codeBase.ToInt64().ToString("X") + " codeLen=" + userCode.Length);
                }

                // CreateRemoteThread: 参数 = data 地址
                IntPtr tid;
                thread = NativeInterop.CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero,
                    remote, dataBase, 0, out tid);
                if (thread == IntPtr.Zero)
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "CreateRemoteThread FAILED (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                    return r;
                }

                uint wait = NativeInterop.WaitForSingleObject(thread,
                    timeoutMs <= 0 ? NativeInterop.INFINITE : (uint)timeoutMs);
                if (wait != 0 /* WAIT_OBJECT_0 */)
                {
                    /* 超时不代表线程已停：它可能仍在执行页内用户码并会继续取指/写结果槽。
                     * 释放该页 = 让目标线程在自己脚下踩空（AV → 目标进程可能被拖死）。
                     * 页与 data 一起故意泄漏，等线程自行退出/目标进程结束后由系统回收。 */
                    keepPage = true;
                    r.Status = ExecStatus.Timeout;
                    r.Message = LanguageService.T("Remote thread timed out (thread still running; execution page intentionally leaked)");
                    return r;
                }

                // 读结果槽
                byte[] resultBuf = new byte[8];
                UIntPtr read;
                IntPtr resultAddr = new IntPtr(dataBase.ToInt64() + 32);
                if (!NativeInterop.ReadProcessMemory(proc, resultAddr, resultBuf, (UIntPtr)8, out read))
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "ReadProcessMemory FAILED";
                    return r;
                }
                r.Status = ExecStatus.Ok;
                r.RetValue = BitConverter.ToUInt64(resultBuf, 0);
                r.RegsFinal[RegIdx.Rax] = r.RetValue;

                // 读回寄存器快照（桩在 call target 前写入：x64 16×8，x86 8×4）
                int snapLen = is64 ? 128 : 32;
                byte[] snap = new byte[snapLen];
                if (NativeInterop.ReadProcessMemory(proc,
                        new IntPtr(dataBase.ToInt64() + SNAP_OFF), snap, (UIntPtr)snapLen, out read))
                {
                    int n = is64 ? RegIdx.Count : 8;
                    for (int i = 0; i < n; i++)
                        r.RegsMid[i] = is64
                            ? BitConverter.ToUInt64(snap, i * 8)
                            : (ulong)BitConverter.ToUInt32(snap, i * 4);
                }
                r.Message = vehData != 0 ? LanguageService.T("RemoteThread+assembly VEH (no DLL)") : LanguageService.T("RemoteThread mode");
                return r;
            }
            finally
            {
                if (thread != IntPtr.Zero) NativeInterop.CloseHandle(thread);
                if (remote != IntPtr.Zero && !keepPage)
                    NativeInterop.VirtualFreeEx(proc, remote, UIntPtr.Zero, NativeInterop.MEM_RELEASE);
                NativeInterop.CloseHandle(proc);
            }
        }

        private static void Put64(byte[] buf, int off, ulong v)
        {
            for (int i = 0; i < 8; i++) buf[off + i] = (byte)((v >> (8 * i)) & 0xFF);
        }

        /* ---- x64 stub ----
         * rbx = data(rcx)；
         * [rbx+0]=code, [rbx+8]=result, [rbx+16]=ExitThread, [rbx+24]=target
         * 顺序: VEH 联动记录 → 执行 code（若有）→ call target → 存 rax → 清 flag → ExitThread(0) */
        private static byte[] BuildStubX64(int codeLen, ulong[] args, int nargs, ulong callTarget,
            ulong vehData, ulong exitThread, out int epilogueOff)
        {
            System.Collections.Generic.List<byte> s = new System.Collections.Generic.List<byte>(256);
            epilogueOff = 0;
            if (vehData != 0)
            {
                // 桩入口记录 savedRsp / flag=1（崩溃由 handler 内 ExitThread 终止线程）
                s.AddRange(new byte[] { 0x49, 0xBA }); AddQ(s, vehData);   // movabs r10, vehData
                s.AddRange(new byte[] { 0x49, 0x89, 0x62, 0x10 });         // mov [r10+0x10], rsp   savedRsp
                s.AddRange(new byte[] { 0x41, 0xC7, 0x02, 0x01, 0x00, 0x00, 0x00 }); // mov dword [r10], 1
                s.AddRange(new byte[] { 0x65, 0x48, 0x8B, 0x04, 0x25, 0x30, 0x00, 0x00, 0x00 }); // mov rax, gs:[0x30]  本线程 TEB
                s.AddRange(new byte[] { 0x49, 0x89, 0x42, 0x68 });         // mov [r10+0x68], rax（handler 据此只拦本会话线程）
            }
            s.AddRange(new byte[] { 0x48, 0x89, 0xCB });        // mov rbx, rcx   数据块指针
            if (vehData != 0)
            {
                // ModRM 0x5A = mod01, reg=011(rbx 源), rm=010+REX.B(r10 目标)：
                // 原误编码 0x53 = reg=010(rdx), rm=011+REX.B(r11) → mov [r11+0x74], rdx，
                // r11 恒为 0 → 写地址 0x74 → AV，用户码结果永远写不进结果槽。
                s.AddRange(new byte[] { 0x49, 0x89, 0x5A, 0x74 });         // mov [r10+0x74], rbx 数据块指针存锚点
            }
            s.Add(0x53);                       // push rbx
            s.Add(0x57);                       // push rdi
            s.Add(0x56);                       // push rsi
            s.AddRange(new byte[] { 0x48, 0x8B, 0x7B, 0x08 });  // mov rdi, [rbx+8]
            // mov rax, [rbx] (code); test; jz 跳过 call
            s.AddRange(new byte[] { 0x48, 0x8B, 0x03 });        // mov rax, [rbx]
            s.AddRange(new byte[] { 0x48, 0x85, 0xC0 });        // test rax, rax
            s.Add(0x74); s.Add(0x02);                            // jz +2（跳过 call rax）
            s.Add(0xFF); s.Add(0xD0);                            // call rax
            /* 用户码/被调函数可能毁掉 rbx/rdi/r10，用户码 ret 也会打乱 esp——
             * 一律经 VEH 块锚点（movabs 绝对地址）重载，不依赖 esp/寄存器存活 */
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0x48, 0xB9 }); AddQ(s, vehData);   // movabs rcx, vehData
                s.AddRange(new byte[] { 0x48, 0x8B, 0x59, 0x74 });         // mov rbx, [rcx+0x74]
                s.AddRange(new byte[] { 0x48, 0x8B, 0x7B, 0x08 });         // mov rdi, [rbx+8]
            }
            else
            {
                s.AddRange(new byte[] { 0x48, 0x8B, 0x5C, 0x24, 0x10 });   // mov rbx, [rsp+16]（无 VEH，尽力而为）
                s.AddRange(new byte[] { 0x8B, 0x7B, 0x08 });               // mov rdi, [rbx+8]
            }
            /* 用户码执行后、call target 前的寄存器快照：只有 "mov [rbx+disp32], reg"，
             * 不碰栈、不改任何寄存器值，因此不影响后续赋参与调用。 */
            AppendSnapStore64(s);
            if (callTarget != 0)
            {
                if (nargs > 0) { s.AddRange(new byte[] { 0x48, 0xB9 }); AddQ(s, args[0]); }   // mov rcx
                if (nargs > 1) { s.AddRange(new byte[] { 0x48, 0xBA }); AddQ(s, args[1]); }   // mov rdx
                if (nargs > 2) { s.AddRange(new byte[] { 0x49, 0xB8 }); AddQ(s, args[2]); }   // mov r8
                if (nargs > 3) { s.AddRange(new byte[] { 0x49, 0xB9 }); AddQ(s, args[3]); }   // mov r9
                for (int i = 4; i < nargs; i++)
                {
                    s.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(s, args[i]);              // mov rax
                    s.AddRange(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0x00, 0x00, 0x00 }); // mov [rsp+20], rax
                }
                s.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(s, callTarget);               // mov rax, target
                s.Add(0xFF); s.Add(0xD0);                                                 // call rax
            }
            s.AddRange(new byte[] { 0x48, 0x89, 0x07 });         // mov [rdi], rax
            epilogueOff = s.Count;                               // 崩溃恢复点：收尾段起始（flag=0 起）
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(s, vehData);              // movabs rax, vehData
                s.AddRange(new byte[] { 0xC7, 0x00, 0x00, 0x00, 0x00, 0x00 });        // mov dword [rax], 0
            }
            /* 注意: 不做 pop —— 线程以 ExitThread 终止，永不返回；
             * ExitThread 经寄存器间接调用（call [rbx+16] memory间接形式实测会崩，见 veh_probe9） */
            s.AddRange(new byte[] { 0x31, 0xC9 });               // xor ecx, ecx（exit code 0）
            s.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(s, exitThread);  // movabs rax, ExitThread
            s.AddRange(new byte[] { 0xFF, 0xD0 });               // call rax（不返回）
            return s.ToArray();
        }

        /* ---- x86 stub（stdcall 线程过程, ret 4）----
         * ebx = data([esp+16])；
         * [ebx+0]=code, [ebx+8]=result, [ebx+16]=ExitThread, [ebx+24]=target
         * call target 用 cdecl push imm32 参数, 调用方清栈 */
        private static byte[] BuildStubX86(int codeLen, ulong[] args, int nargs, ulong callTarget,
            ulong vehData, ulong exitThread, out int epilogueOff)
        {
            System.Collections.Generic.List<byte> s = new System.Collections.Generic.List<byte>(128);
            epilogueOff = 0;
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0x89, 0x25 });                     // mov [vehData+0x10], esp  savedEsp
                AddD(s, (uint)(vehData + 0x10));
                s.AddRange(new byte[] { 0xC7, 0x05 });                     // mov dword [vehData], 1
                AddD(s, (uint)vehData);
                AddD(s, 1);
                s.AddRange(new byte[] { 0x64, 0xA1, 0x18, 0x00, 0x00, 0x00 }); // mov eax, fs:[0x18]  本线程 TEB
                s.AddRange(new byte[] { 0x89, 0x05 });                     // mov [vehData+0x68], eax（handler 据此只拦本会话线程）
                AddD(s, (uint)(vehData + 0x68));
            }
            s.AddRange(new byte[] { 0x8B, 0x5C, 0x24, 0x04 });   // mov ebx, [esp+4]   线程参数=数据块（entry: [esp]=ret）
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0x89, 0x1D });                     // mov [vehData+0x74], ebx 数据块指针存锚点
                AddD(s, (uint)(vehData + 0x74));
            }
            s.Add(0x53);                       // push ebx（原始栈布局：3 push 供收尾/恢复一致）
            s.Add(0x57);                       // push edi
            s.Add(0x56);                       // push esi
            s.AddRange(new byte[] { 0x8B, 0x7B, 0x08 });         // mov edi, [ebx+8]
            s.AddRange(new byte[] { 0x8B, 0x03 });               // mov eax, [ebx]
            s.AddRange(new byte[] { 0x85, 0xC0 });               // test eax, eax
            s.Add(0x74); s.Add(0x02);                            // jz +2（跳过 call eax）
            s.Add(0xFF); s.Add(0xD0);                            // call eax
            /* 用户码/被调函数可能毁掉 ebx/edi（非常规约定，unepic 实测），且用户码
             * 末尾 ret 会弹掉 call 压入的返回地址（esp 位置不可预知）——一律从
             * VEH 块锚点绝对寻址重载，不依赖 esp 也不依赖寄存器存活 */
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0x8B, 0x1D });                     // mov ebx, [vehData+0x74]
                AddD(s, (uint)(vehData + 0x74));
                s.AddRange(new byte[] { 0x8B, 0x7B, 0x08 });               // mov edi, [ebx+8]
            }
            else
            {
                s.AddRange(new byte[] { 0x8B, 0x5C, 0x24, 0x08 });   // mov ebx, [esp+8]（无 VEH，尽力而为）
                s.AddRange(new byte[] { 0x8B, 0x7B, 0x08 });         // mov edi, [ebx+8]
            }
            /* 用户码执行后、call target 前的寄存器快照（同 x64：只写内存，不动寄存器/栈） */
            AppendSnapStore32(s);
            if (callTarget != 0)
            {
                for (int i = nargs - 1; i >= 0; i--)
                {
                    s.Add(0x68);                                 // push imm32
                    AddD(s, (uint)args[i]);
                }
                s.Add(0xFF); s.Add(0x53); s.Add(0x18);           // call dword [ebx+24]
                if (nargs > 0)
                {
                    int bytes = 4 * nargs;
                    if (bytes <= 127) { s.Add(0x83); s.Add(0xC4); s.Add((byte)bytes); }
                    else { s.Add(0x81); s.Add(0xC4); AddD(s, (uint)bytes); }
                }
            }
            s.AddRange(new byte[] { 0x89, 0x07 });               // mov [edi], eax
            epilogueOff = s.Count;                               // 崩溃恢复点：收尾段起始
            if (vehData != 0)
            {
                s.AddRange(new byte[] { 0xC7, 0x05 });           // mov dword [vehData], 0
                AddD(s, (uint)vehData);
                AddD(s, 0);
            }
            /* 注意: 不做 pop —— 线程以 ExitThread 终止，永不返回；
             * ExitThread 经寄存器间接调用（call [ebx+16] memory间接形式实测会崩） */
            s.AddRange(new byte[] { 0x6A, 0x00 });               // push 0（exit code 0）
            s.Add(0xB8); AddD(s, (uint)exitThread);              // mov eax, ExitThread
            s.AddRange(new byte[] { 0xFF, 0xD0 });               // call eax（不返回）
            return s.ToArray();
        }

        /* ---- 寄存器快照写入 ----
         * 统一形式 mov [rbx/ebx + disp32], reg（mod=10, rm=011），disp 即 data 块内偏移。
         * x64 的 ModRM 字节 = 0x80 | (reg<<3) | 3：rax→0x83 rcx→0x8B rdx→0x93 rbx→0x9B
         *                                        rsp→0xA3 rbp→0xAB rsi→0xB3 rdi→0xBB
         * r8..r15 仅 reg 字段扩展位不同（REX.R=1 → 0x4C），ModRM 字节相同。 */
        private static void AppendSnapStore64(System.Collections.Generic.List<byte> s)
        {
            SnapStore(s, 0x48, 0x83, SNAP_OFF + 0x00);   // mov [rbx+..], rax
            SnapStore(s, 0x48, 0x8B, SNAP_OFF + 0x08);   // mov [rbx+..], rcx
            SnapStore(s, 0x48, 0x93, SNAP_OFF + 0x10);   // mov [rbx+..], rdx
            SnapStore(s, 0x48, 0x9B, SNAP_OFF + 0x18);   // mov [rbx+..], rbx
            SnapStore(s, 0x48, 0xA3, SNAP_OFF + 0x20);   // mov [rbx+..], rsp
            SnapStore(s, 0x48, 0xAB, SNAP_OFF + 0x28);   // mov [rbx+..], rbp
            SnapStore(s, 0x48, 0xB3, SNAP_OFF + 0x30);   // mov [rbx+..], rsi
            SnapStore(s, 0x48, 0xBB, SNAP_OFF + 0x38);   // mov [rbx+..], rdi
            SnapStore(s, 0x4C, 0x83, SNAP_OFF + 0x40);   // mov [rbx+..], r8
            SnapStore(s, 0x4C, 0x8B, SNAP_OFF + 0x48);   // mov [rbx+..], r9
            SnapStore(s, 0x4C, 0x93, SNAP_OFF + 0x50);   // mov [rbx+..], r10
            SnapStore(s, 0x4C, 0x9B, SNAP_OFF + 0x58);   // mov [rbx+..], r11
            SnapStore(s, 0x4C, 0xA3, SNAP_OFF + 0x60);   // mov [rbx+..], r12
            SnapStore(s, 0x4C, 0xAB, SNAP_OFF + 0x68);   // mov [rbx+..], r13
            SnapStore(s, 0x4C, 0xB3, SNAP_OFF + 0x70);   // mov [rbx+..], r14
            SnapStore(s, 0x4C, 0xBB, SNAP_OFF + 0x78);   // mov [rbx+..], r15
        }

        private static void AppendSnapStore32(System.Collections.Generic.List<byte> s)
        {
            SnapStore(s, 0x00, 0x83, SNAP_OFF + 0x00);   // mov [ebx+..], eax
            SnapStore(s, 0x00, 0x8B, SNAP_OFF + 0x04);   // mov [ebx+..], ecx
            SnapStore(s, 0x00, 0x93, SNAP_OFF + 0x08);   // mov [ebx+..], edx
            SnapStore(s, 0x00, 0x9B, SNAP_OFF + 0x0C);   // mov [ebx+..], ebx
            SnapStore(s, 0x00, 0xA3, SNAP_OFF + 0x10);   // mov [ebx+..], esp
            SnapStore(s, 0x00, 0xAB, SNAP_OFF + 0x14);   // mov [ebx+..], ebp
            SnapStore(s, 0x00, 0xB3, SNAP_OFF + 0x18);   // mov [ebx+..], esi
            SnapStore(s, 0x00, 0xBB, SNAP_OFF + 0x1C);   // mov [ebx+..], edi
        }

        private static void SnapStore(System.Collections.Generic.List<byte> s, byte rex, byte modrm, int disp)
        {
            if (rex != 0) s.Add(rex);
            s.Add(0x89);
            s.Add(modrm);
            s.Add((byte)disp); s.Add((byte)(disp >> 8));
            s.Add((byte)(disp >> 16)); s.Add((byte)(disp >> 24));
        }

        private static void AddQ(System.Collections.Generic.List<byte> s, ulong v)
        {
            for (int i = 0; i < 8; i++) s.Add((byte)((v >> (8 * i)) & 0xFF));
        }
        private static void AddD(System.Collections.Generic.List<byte> s, uint v)
        {
            for (int i = 0; i < 4; i++) s.Add((byte)((v >> (8 * i)) & 0xFF));
        }
    }
}
