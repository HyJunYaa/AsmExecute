// RemoteThreadExecutor.cs - 远程线程兜底执行（无 DLL）
// 原理: OpenProcess → VirtualAllocEx(RWX) → 写入 [stub][data][user code] →
//       CreateRemoteThread(stub) → stub 执行用户代码/调用目标 → 结果写入 data →
//       ReadProcessMemory 取回。
// stub 为运行期生成的位置无关机器码（x64 / x86 两套），参数以 imm 直接编入。
// vehData != 0 时启用汇编 VEH 联动: 桩入口记录 savedRsp 并置 flag=1，
//   崩溃由 AsmVeh handler 在 handler 内 ExitThread 终止故障线程（异常被吞，进程存活）。
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal static class RemoteThreadExecutor
    {
        /* data 布局 (x64): +0 code, +8 result, +16 ExitThread, +24 target;
         * args 由 stub 内 imm 直接编码 */

        /// <summary>诊断开关: true 时把生成的 stub/veh 数据转储到 %TEMP%\asmstub.bin</summary>
        public static bool DumpStub;

        public static ExecutionResult Execute(ProcessInfo target, byte[] code,
            ulong[] args, int nargs, ulong callTarget, int timeoutMs, ulong vehData)
        {
            ExecutionResult r = new ExecutionResult();
            bool is64 = target.Is64Bit;

            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, target.Pid);
            if (proc == IntPtr.Zero)
            {
                r.Status = ExecStatus.Internal;
                r.Message = "OpenProcess 失败 (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                return r;
            }

            IntPtr remote = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            try
            {
                // 远程内存: [stub][DATA_SIZE data][user code + ret]
                byte[] userCode;
                ulong stubCallTarget = callTarget;
                if (vehData != 0 && callTarget != 0)
                {
                    // 汇编 VEH 通道: call 语义改由"调用前奏"实现（寄存器赋参 + call 目标），
                    // 前奏拼接在用户码之前；不用 stub 的 callTarget 分支（vehData!=0 时恒 0）。
                    // 参数 > 4 个暂不支持（x64 栈参数需在 call 帧 +0x20 处布置，前奏无法提供）。
                    if (nargs > 4)
                    {
                        r.Status = ExecStatus.BadArg;
                        r.Message = "汇编 VEH 通道远程 call 暂只支持 4 个以内参数";
                        return r;
                    }
                    List<byte> pre = new List<byte>();
                    if (nargs > 0) { pre.AddRange(new byte[] { 0x48, 0xB9 }); AddQ(pre, args[0]); }  // movabs rcx
                    if (nargs > 1) { pre.AddRange(new byte[] { 0x48, 0xBA }); AddQ(pre, args[1]); }  // movabs rdx
                    if (nargs > 2) { pre.AddRange(new byte[] { 0x49, 0xB8 }); AddQ(pre, args[2]); }  // movabs r8
                    if (nargs > 3) { pre.AddRange(new byte[] { 0x49, 0xB9 }); AddQ(pre, args[3]); }  // movabs r9
                    pre.AddRange(new byte[] { 0x48, 0xB8 }); AddQ(pre, callTarget);                  // movabs rax, target
                    pre.AddRange(new byte[] { 0xFF, 0xD0 });                                         // call rax
                    byte[] codeBuf = new byte[pre.Count + (code != null ? code.Length : 0) + 1];
                    Array.Copy(pre.ToArray(), codeBuf, pre.Count);
                    if (code != null) Array.Copy(code, 0, codeBuf, pre.Count, code.Length);
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
                    r.Message = "无法从目标进程导出表解析 ExitThread";
                    return r;
                }
                int epilogueOff;
                byte[] stub = is64
                    ? BuildStubX64(userCode.Length, args, nargs, stubCallTarget, vehData, exitThread, out epilogueOff)
                    : BuildStubX86(userCode.Length, args, nargs, stubCallTarget, vehData, exitThread, out epilogueOff);

                const int DATA_SIZE = 64;
                int total = stub.Length + DATA_SIZE + userCode.Length;

                remote = NativeInterop.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)total,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_EXECUTE_READWRITE);
                if (remote == IntPtr.Zero)
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "VirtualAllocEx 失败";
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
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(stub) 失败"; return r; }
                if (DumpStub)
                {
                    byte[] dbg = new byte[stub.Length + 64];
                    Array.Copy(stub, dbg, stub.Length);
                    Array.Copy(data, 0, dbg, stub.Length, 64);
                    System.IO.File.WriteAllBytes(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asmstub.bin"), dbg);
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asmstub.txt"),
                        "stubLen=" + stub.Length + " blob=0x" + remote.ToInt64().ToString("X") +
                        " epilogueOff=" + epilogueOff + " dataBase=0x" + dataBase.ToInt64().ToString("X"));
                }
                if (!NativeInterop.WriteProcessMemory(proc, dataBase, data, (UIntPtr)DATA_SIZE, out written))
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(data) 失败"; return r; }
                if (userCode.Length > 1 &&
                    !NativeInterop.WriteProcessMemory(proc, codeBase, userCode, (UIntPtr)userCode.Length, out written))
                { r.Status = ExecStatus.Internal; r.Message = "WriteProcessMemory(code) 失败"; return r; }

                // CreateRemoteThread: 参数 = data 地址
                IntPtr tid;
                thread = NativeInterop.CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero,
                    remote, dataBase, 0, out tid);
                if (thread == IntPtr.Zero)
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "CreateRemoteThread 失败 (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                    return r;
                }

                uint wait = NativeInterop.WaitForSingleObject(thread,
                    timeoutMs <= 0 ? NativeInterop.INFINITE : (uint)timeoutMs);
                if (wait != 0 /* WAIT_OBJECT_0 */)
                {
                    r.Status = ExecStatus.Timeout;
                    r.Message = "远程线程超时（线程仍在运行，未终止）";
                    return r;
                }

                // 读结果槽
                byte[] resultBuf = new byte[8];
                UIntPtr read;
                IntPtr resultAddr = new IntPtr(dataBase.ToInt64() + 32);
                if (!NativeInterop.ReadProcessMemory(proc, resultAddr, resultBuf, (UIntPtr)8, out read))
                {
                    r.Status = ExecStatus.Internal;
                    r.Message = "ReadProcessMemory 失败";
                    return r;
                }
                r.Status = ExecStatus.Ok;
                r.RetValue = BitConverter.ToUInt64(resultBuf, 0);
                r.RegsFinal[RegIdx.Rax] = r.RetValue;
                r.Message = vehData != 0 ? "远程线程+汇编VEH（无DLL）" : "RemoteThread 模式";
                return r;
            }
            finally
            {
                if (thread != IntPtr.Zero) NativeInterop.CloseHandle(thread);
                if (remote != IntPtr.Zero)
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
                // 用 r10 易变寄存器，不得动 rcx（其后 rbx,rcx 读线程参数）
                s.AddRange(new byte[] { 0x49, 0xBA }); AddQ(s, vehData);   // movabs r10, vehData
                s.AddRange(new byte[] { 0x49, 0x89, 0x62, 0x10 });         // mov [r10+0x10], rsp   savedRsp
                s.AddRange(new byte[] { 0x41, 0xC7, 0x02, 0x01, 0x00, 0x00, 0x00 }); // mov dword [r10], 1
            }
            s.Add(0x53);                       // push rbx
            s.Add(0x57);                       // push rdi
            s.Add(0x56);                       // push rsi
            s.AddRange(new byte[] { 0x48, 0x89, 0xCB });        // mov rbx, rcx
            s.AddRange(new byte[] { 0x48, 0x8B, 0x7B, 0x08 });  // mov rdi, [rbx+8]
            // mov rax, [rbx] (code); test; jz 跳过 call
            s.AddRange(new byte[] { 0x48, 0x8B, 0x03 });        // mov rax, [rbx]
            s.AddRange(new byte[] { 0x48, 0x85, 0xC0 });        // test rax, rax
            s.Add(0x74); s.Add(0x02);                            // jz +2（跳过 call rax）
            s.Add(0xFF); s.Add(0xD0);                            // call rax
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
             * ExitThread 经寄存器间接调用（call [rbx+16] 内存间接形式实测会崩，见 veh_probe9） */
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
            }
            s.Add(0x53);                       // push ebx
            s.Add(0x57);                       // push edi
            s.Add(0x56);                       // push esi
            s.AddRange(new byte[] { 0x8B, 0x5C, 0x24, 0x10 });   // mov ebx, [esp+16]
            s.AddRange(new byte[] { 0x8B, 0x7B, 0x08 });         // mov edi, [ebx+8]
            s.AddRange(new byte[] { 0x8B, 0x03 });               // mov eax, [ebx]
            s.AddRange(new byte[] { 0x85, 0xC0 });               // test eax, eax
            s.Add(0x74); s.Add(0x02);                            // jz +2（跳过 call eax）
            s.Add(0xFF); s.Add(0xD0);                            // call eax
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
             * ExitThread 经寄存器间接调用（call [ebx+16] 内存间接形式实测会崩） */
            s.AddRange(new byte[] { 0x6A, 0x00 });               // push 0（exit code 0）
            s.Add(0xB8); AddD(s, (uint)exitThread);              // mov eax, ExitThread
            s.AddRange(new byte[] { 0xFF, 0xD0 });               // call eax（不返回）
            return s.ToArray();
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
