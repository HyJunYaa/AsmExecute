// AsmVeh.cs - 汇编级 VEH 保护（无 DLL 注入）
// 原理: 在目标进程分配一页 [VEH handler 机器码][data块]，经远程线程调
//       AddVectoredExceptionHandler 注册 handler。用户代码崩溃时 handler
//       判定是否属于本会话运行（flag==1 且异常码属于崩溃类），是则把线程
//       上下文 RSP/RIP 重置到恢复桩（abort stub），异常被吞掉，线程干净退出；
//       否则返回 CONTINUE_SEARCH 交还目标自身异常链。
// data块布局 (64 字节):
//   +0x00 u32 flag        1 = 本会话代码运行中
//   +0x04 u32 faultCount  拦截次数
//   +0x08 u32 lastExcCode 最近拦截的异常码
//   +0x0C u32 -
//   +0x10 u64 savedRsp    运行桩入口 RSP（桩写入；x86 仅用低 32 位）
//   +0x18 u64 abortRip    恢复桩地址（桩写入）
//   +0x20 u64 vehHandle   AddVectoredExceptionHandler 返回值
//   +0x28 u64 excAddr     AV 时 ExceptionInformation[1]（违例地址）
//   +0x30 u64 excRip      异常发生时的 RIP
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal sealed class AsmVehProtector : IDisposable
    {
        public const int DataSize = 80;

        private IntPtr _proc;
        private IntPtr _page;         // [handler][data] 一页
        private IntPtr _dataAddr;
        private ulong _rveh;          // 目标进程内 RemoveVectoredExceptionHandler 地址
        private ulong _exitThread;    // 目标进程内 ExitThread 地址
        private bool _is64;
        private bool _installed;

        public bool Installed { get { return _installed; } }
        public ulong DataAddress { get { return (ulong)_dataAddr.ToInt64(); } }

        /// <summary>崩溃类异常码（命中才拦截，其余交还目标异常链）</summary>
        private static readonly uint[] CrashCodes = new uint[]
        {
            0xC0000005,   // ACCESS_VIOLATION
            0xC0000006,   // IN_PAGE_ERROR
            0xC000001D,   // ILLEGAL_INSTRUCTION
            0xC0000094,   // INT_DIVIDE_BY_ZERO
            0xC0000095,   // INT_OVERFLOW
            0xC0000096,   // PRIVILEGED_INSTRUCTION
            0xC00000FD,   // STACK_OVERFLOW
            0x80000003    // BREAKPOINT
        };

        /// <summary>在目标进程安装汇编 VEH（每会话一次）</summary>
        public bool Install(int pid, bool is64, out string error)
        {
            error = null;
            _is64 = is64;
            _proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, pid);
            if (_proc == IntPtr.Zero)
            {
                error = "OpenProcess FAILED (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                return false;
            }

            try
            {
                // 解析目标进程内 AVEH/RVEH/ExitThread 远程地址：读目标 kernel32 导出表。
                // kernel32 本体多为转发出口（AVEH→ntdll.RtlAddVectoredExceptionHandler 等），
                // 解析器追踪一跳转发得到 ntdll 内真实地址。
                // 不能用"本进程地址 + kernel32 基址差值"推算：主程序与Target Arch不同时
                // （x64 主程序 → x86 目标）两边 kernel32 是不同二进制，差值算出的地址
                // 指向垃圾代码，远程线程一调用目标进程就崩。
                ulong aveh = RemoteSymbols.ResolveTargetApi(pid, is64,
                    "kernel32.dll", "AddVectoredExceptionHandler");
                _rveh = RemoteSymbols.ResolveTargetApi(pid, is64,
                    "kernel32.dll", "RemoveVectoredExceptionHandler");
                _exitThread = RemoteSymbols.ResolveTargetApi(pid, is64,
                    "kernel32.dll", "ExitThread");
                if (aveh == 0 || _rveh == 0 || _exitThread == 0)
                {
                    error = "Cannot resolve AVEH/RVEH/ExitThread from the target kernel32 export table" +
                        " (module enumeration failed or cross-bitness read denied)";
                    return false;
                }

                // 布局: [handler][data][regStub]（长度与地址值无关 → 先探测长度再按真实地址生成）
                byte[] probe = _is64 ? BuildHandlerX64(0) : BuildHandlerX86(0);
                int dataOff = (probe.Length + 15) & ~15;
                int regOff = dataOff + DataSize;

                _page = NativeInterop.VirtualAllocEx(_proc, IntPtr.Zero, (UIntPtr)0x1000,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_EXECUTE_READWRITE);
                if (_page == IntPtr.Zero)
                {
                    error = "VirtualAllocEx failed (target may have ACG dynamic-code restrictions)";
                    return false;
                }
                _dataAddr = new IntPtr(_page.ToInt64() + dataOff);
                ulong dataAddr = (ulong)_dataAddr.ToInt64();
                ulong handlerAddr = (ulong)_page.ToInt64();

                byte[] handler = _is64 ? BuildHandlerX64(dataAddr) : BuildHandlerX86(dataAddr);
                byte[] zero = new byte[DataSize];
                UIntPtr written;
                if (!NativeInterop.WriteProcessMemory(_proc, _page, handler,
                        (UIntPtr)handler.Length, out written) ||
                    !NativeInterop.WriteProcessMemory(_proc, _dataAddr, zero,
                        (UIntPtr)DataSize, out written))
                {
                    error = "WriteProcessMemory(handler) FAILED";
                    return false;
                }

                // 注册: 远程线程 AddVectoredExceptionHandler(1, handler)，桩放在页尾区段
                // （桩以 ExitThread 收尾 —— 远程线程桩不做最终 ret，见 veh_probe5 实验）
                byte[] regStub = _is64 ? BuildRegStubX64(aveh, _exitThread, handlerAddr, dataAddr)
                                       : BuildRegStubX86(aveh, _exitThread, handlerAddr, dataAddr);
                IntPtr regEntry = new IntPtr(_page.ToInt64() + regOff);
                if (!NativeInterop.WriteProcessMemory(_proc, regEntry, regStub,
                        (UIntPtr)regStub.Length, out written))
                {
                    error = "WriteProcessMemory(regStub) FAILED";
                    return false;
                }
                IntPtr thread, tid;
                thread = NativeInterop.CreateRemoteThread(_proc, IntPtr.Zero, UIntPtr.Zero,
                    regEntry, IntPtr.Zero, 0, out tid);
                if (thread == IntPtr.Zero)
                {
                    error = "CreateRemoteThread(register VEH) failed (err=" +
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                try
                {
                    NativeInterop.WaitForSingleObject(thread, 3000);
                    // 桩以 ExitThread(句柄) 收尾，退出码为句柄低 32 位（任意值）；
                    // 以data块里的句柄为准（桩已写入 [DATA+0x20]）
                    byte[] hbuf = new byte[8];
                    UIntPtr hread;
                    if (!NativeInterop.ReadProcessMemory(_proc,
                            new IntPtr(_dataAddr.ToInt64() + 0x20), hbuf, (UIntPtr)8, out hread) ||
                        BitConverter.ToUInt64(hbuf, 0) == 0)
                    {
                        error = "AddVectoredExceptionHandler failed (handle not written)";
                        return false;
                    }
                }
                finally
                {
                    NativeInterop.CloseHandle(thread);
                }
                _installed = true;
                return true;
            }
            finally
            {
                if (!_installed) Cleanup();
            }
        }

        /// <summary>读取data块；返回是否成功</summary>
        public bool Query(out uint faultCount, out uint excCode, out ulong excAddr, out ulong excRip)
        {
            faultCount = 0; excCode = 0; excAddr = 0; excRip = 0;
            if (!_installed) return false;
            byte[] buf = new byte[DataSize];
            UIntPtr read;
            if (!NativeInterop.ReadProcessMemory(_proc, _dataAddr, buf, (UIntPtr)DataSize, out read))
                return false;
            faultCount = BitConverter.ToUInt32(buf, 0x04);
            excCode = BitConverter.ToUInt32(buf, 0x08);
            excAddr = BitConverter.ToUInt64(buf, 0x28);
            excRip = BitConverter.ToUInt64(buf, 0x40);   // 入口记录仪里的 rip（ours 时即故障 rip）
            return true;
        }

        /// <summary>诊断: 读 handler 入口记录仪（每条异常的 code/rip/计数 + 当时 flag）</summary>
        public bool QueryRecorder(out uint seenCount, out uint seenCode, out ulong seenRip)
        {
            seenCount = 0; seenCode = 0; seenRip = 0;
            if (!_installed) return false;
            byte[] buf = new byte[DataSize];
            UIntPtr read;
            if (!NativeInterop.ReadProcessMemory(_proc, _dataAddr, buf, (UIntPtr)DataSize, out read))
                return false;
            seenCount = BitConverter.ToUInt32(buf, 0x48);
            seenCode = BitConverter.ToUInt32(buf, 0x38);
            seenRip = BitConverter.ToUInt64(buf, 0x40);
            LastFlagSeen = BitConverter.ToUInt32(buf, 0x4C);
            return true;
        }

        /// <summary>诊断: 最近一次 handler 进入时的 flag 值</summary>
        public uint LastFlagSeen;

        /// <summary>强制清运行标志（timeout后调用，避免 handler 长期误吞目标异常）</summary>
        public void ForceIdle()
        {
            if (!_installed) return;
            byte[] buf = new byte[DataSize];
            UIntPtr read;
            if (!NativeInterop.ReadProcessMemory(_proc, _dataAddr, buf, (UIntPtr)DataSize, out read))
                return;
            if (BitConverter.ToUInt32(buf, 0x00) == 0) return;
            byte[] zeroFlag = new byte[4];
            UIntPtr written;
            NativeInterop.WriteProcessMemory(_proc, _dataAddr, zeroFlag, (UIntPtr)4, out written);
        }

        public void Dispose()
        {
            if (_installed)
            {
                // 尽力撤销注册（读回 vehHandle）
                byte[] buf = new byte[8];
                UIntPtr read;
                if (NativeInterop.ReadProcessMemory(_proc,
                        new IntPtr(_dataAddr.ToInt64() + 0x20), buf, (UIntPtr)8, out read))
                {
                    ulong handle = BitConverter.ToUInt64(buf, 0);
                    if (handle != 0) RemoteRemove(handle);
                }
                _installed = false;
            }
            Cleanup();
        }

        private void RemoteRemove(ulong handle)
        {
            try
            {
                // ExitThread 地址按安装时的 delta 重算（_rveh 已含 delta，ExitThread 同理缓存）
                byte[] stub = _is64 ? BuildRemoveStubX64(handle, _rveh, _exitThread)
                                    : BuildRemoveStubX86(handle, _rveh, _exitThread);
                IntPtr tmp = NativeInterop.VirtualAllocEx(_proc, IntPtr.Zero, (UIntPtr)0x1000,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_EXECUTE_READWRITE);
                if (tmp == IntPtr.Zero) return;
                UIntPtr written;
                NativeInterop.WriteProcessMemory(_proc, tmp, stub, (UIntPtr)stub.Length, out written);
                IntPtr thread, tid;
                thread = NativeInterop.CreateRemoteThread(_proc, IntPtr.Zero, UIntPtr.Zero,
                    tmp, IntPtr.Zero, 0, out tid);
                if (thread != IntPtr.Zero)
                {
                    NativeInterop.WaitForSingleObject(thread, 2000);
                    NativeInterop.CloseHandle(thread);
                }
                NativeInterop.VirtualFreeEx(_proc, tmp, UIntPtr.Zero, NativeInterop.MEM_RELEASE);
            }
            catch { /* 尽力而为 */ }
        }

        private void Cleanup()
        {
            if (_page != IntPtr.Zero)
            {
                NativeInterop.VirtualFreeEx(_proc, _page, UIntPtr.Zero, NativeInterop.MEM_RELEASE);
                _page = IntPtr.Zero;
            }
            if (_proc != IntPtr.Zero)
            {
                NativeInterop.CloseHandle(_proc);
                _proc = IntPtr.Zero;
            }
        }

        // ================= shellcode 生成 =================

        /// <summary>微型发射器: 字节流 + rel8 标签回填</summary>
        private sealed class Emitter
        {
            public List<byte> B = new List<byte>();
            private Dictionary<string, int> _labels = new Dictionary<string, int>();
            private List<KeyValuePair<int, string>> _je = new List<KeyValuePair<int, string>>();

            public void Label(string name) { _labels[name] = B.Count; }
            public void Bytes(params int[] bs) { foreach (int b in bs) B.Add((byte)b); }
            public void Imm32(uint v) { for (int i = 0; i < 4; i++) B.Add((byte)((v >> (8 * i)) & 0xFF)); }
            public void Imm64(ulong v) { for (int i = 0; i < 8; i++) B.Add((byte)((v >> (8 * i)) & 0xFF)); }
            public void Abs32(long addr) { Imm32((uint)addr); }
            public void JeRel8(string label)
            {
                B.Add(0x74);
                _je.Add(new KeyValuePair<int, string>(B.Count, label));
                B.Add(0);
            }
            public byte[] Finish()
            {
                foreach (KeyValuePair<int, string> kv in _je)
                {
                    int target = _labels[kv.Value];
                    int rel = target - (kv.Key + 1);
                    if (rel < -128 || rel > 127)
                        throw new InvalidOperationException("VEH shellcode rel8 overflow");
                    B[kv.Key] = (byte)rel;
                }
                return B.ToArray();
            }
        }

        /* ---- x64 VEH handler ----
         * rcx = EXCEPTION_POINTERS*；只用易变寄存器(rax/rcx/rdx/r9)。
         * 入口记录 (code, rip, flag, 计数) 到data块 +0x38/+0x40/+0x4C/+0x48（诊断），再走过滤。
         * OURS 路径（与 AsmLibrary veh.c 同款）: 记录 fault → 清 flag →
         *   ctx.Rip = [DATA+0x18]（宿主写入的桩收尾段地址）、ctx.Rsp = [DATA+0x10]-24
         *   （等效"用户码已返回、3 个 push 仍在栈上"）→ EXCEPTION_CONTINUE_EXECUTION。
         *   线程在桩收尾段正常 ExitThread，进程存活。 */
        private static byte[] BuildHandlerX64(ulong dataAddr)
        {
            Emitter e = new Emitter();
            e.Bytes(0x49, 0xB9); e.Imm64(dataAddr);            // movabs r9, DATA
            e.Bytes(0x41, 0x8B, 0x01);                         // mov eax, [r9]         flagSeen 诊断
            e.Bytes(0x41, 0x89, 0x41, 0x4C);                   // mov [r9+0x4C], eax
            e.Bytes(0x48, 0x8B, 0x01);                         // mov rax, [rcx]        ExceptionRecord*
            e.Bytes(0x8B, 0x10);                               // mov edx, [rax]        ExceptionCode
            e.Bytes(0x41, 0x89, 0x51, 0x38);                   // mov [r9+0x38], edx    lastSeenCode
            e.Bytes(0x48, 0x8B, 0x49, 0x08);                   // mov rcx, [rcx+8]      CONTEXT*
            e.Bytes(0x48, 0x8B, 0x91, 0xF8, 0x00, 0x00, 0x00); // mov rdx, [rcx+0xF8]   rip
            e.Bytes(0x49, 0x89, 0x51, 0x40);                   // mov [r9+0x40], rdx    lastSeenRip
            e.Bytes(0x41, 0xFF, 0x41, 0x48);                   // inc dword [r9+0x48]   seenCount
            e.Bytes(0x8B, 0x10);                               // mov edx, [rax]        恢复 ExceptionCode（被 rip 记录覆写）
            e.Bytes(0x41, 0x83, 0x39, 0x00);                   // cmp dword [r9], 0
            e.JeRel8("notours");
            foreach (uint code in CrashCodes)
            {
                e.Bytes(0x81, 0xFA); e.Imm32(code);            // cmp edx, code
                e.JeRel8("ours");
            }
            e.Label("notours");
            e.Bytes(0x31, 0xC0, 0xC3);                         // xor eax,eax; ret (CONTINUE_SEARCH)
            e.Label("ours");
            e.Bytes(0x41, 0x89, 0x51, 0x08);                   // mov [r9+8], edx       lastExcCode
            e.Bytes(0x41, 0xFF, 0x41, 0x04);                   // inc dword [r9+4]      faultCount++
            e.Bytes(0x41, 0xC7, 0x01); e.Imm32(0);             // mov dword [r9], 0     flag=0
            e.Bytes(0x48, 0x8B, 0x50, 0x28);                   // mov rdx, [rax+0x28]   ExceptionInformation[1]
            e.Bytes(0x49, 0x89, 0x51, 0x28);                   // mov [r9+0x28], rdx    excAddr
            e.Bytes(0x49, 0x8B, 0x51, 0x10);                   // mov rdx, [r9+0x10]    savedRsp
            e.Bytes(0x48, 0x83, 0xEA, 0x18);                   // sub rdx, 24           （等效 3 push + ret 已消费）
            e.Bytes(0x48, 0x89, 0x91, 0x98, 0x00, 0x00, 0x00); // mov [rcx+0x98], rdx   ctx.Rsp
            e.Bytes(0x49, 0x8B, 0x51, 0x18);                   // mov rdx, [r9+0x18]    resumeRip（宿主写入）
            e.Bytes(0x48, 0x89, 0x91, 0xF8, 0x00, 0x00, 0x00); // mov [rcx+0xF8], rdx   ctx.Rip
            e.Bytes(0xB8, 0xFF, 0xFF, 0xFF, 0xFF);             // mov eax, EXCEPTION_CONTINUE_EXECUTION
            e.Bytes(0xC3);                                     // ret
            return e.Finish();
        }

        /* ---- x86 VEH handler（stdcall, ret 4）----
         * [esp+4] = EXCEPTION_POINTERS*；只用 eax/ecx/edx，data经绝对 imm32 访问。
         * x86 与 x64 的结构差异（实测确认）:
         *   EXCEPTION_POINTERS = { ER* +0, CTX* +4 }（+8 是 x64 布局，勿照搬）；
         *   CONTEXT.Eip=0xB8、Esp=0xC4；handler 返回约定 stdcall ret 4
         *   （实测：plain ret 会炸调度器，连 SEH 都走不到）。
         * OURS 路径同 x64: 记录后改 ctx（Eip=resumeEip、Esp=savedEsp-12）→ CONTINUE_EXECUTION。 */
        private static byte[] BuildHandlerX86(ulong dataAddr)
        {
            Emitter e = new Emitter();
            e.Bytes(0x8B, 0x44, 0x24, 0x04);                   // mov eax, [esp+4]      EXCEPTION_POINTERS*
            e.Bytes(0x8B, 0x08);                               // mov ecx, [eax]        ExceptionRecord*
            e.Bytes(0x8B, 0x11);                               // mov edx, [ecx]        ExceptionCode
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x38);   // mov [DATA+0x38], edx  lastSeenCode
            e.Bytes(0x8B, 0x50, 0x04);                         // mov edx, [eax+4]      CONTEXT*（x86 EP={ER*+0,CTX*+4}，+8 是 x64 布局！）
            e.Bytes(0x8B, 0x8A, 0xB8, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xB8]   eip（x86 CONTEXT.Eip=0xB8）
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x40);   // mov [DATA+0x40], ecx  lastSeenRip
            e.Bytes(0xFF, 0x05); e.Abs32((long)dataAddr + 0x48);   // inc dword [DATA+0x48] seenCount
            e.Bytes(0x8B, 0x08);                               // mov ecx, [eax]        恢复 ER（被 rip 记录覆写）
            e.Bytes(0x8B, 0x11);                               // mov edx, [ecx]        恢复 ExceptionCode
            e.Bytes(0x83, 0x3D); e.Abs32((long)dataAddr); e.Bytes(0x00);   // cmp dword [DATA], 0
            e.JeRel8("notours");
            foreach (uint code in CrashCodes)
            {
                e.Bytes(0x81, 0xFA); e.Imm32(code);            // cmp edx, code
                e.JeRel8("ours");
            }
            e.Label("notours");
            e.Bytes(0x31, 0xC0);                               // xor eax, eax
            e.Bytes(0xC2, 0x04, 0x00);             // ret 4（stdcall，实测：plain ret 会炸调度器，连 SEH 都走不到）
            e.Label("ours");
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 8);  // mov [DATA+8], edx     lastExcCode
            e.Bytes(0xFF, 0x05); e.Abs32((long)dataAddr + 4);  // inc dword [DATA+4]    faultCount++
            e.Bytes(0xC7, 0x05); e.Abs32((long)dataAddr); e.Imm32(0);  // mov dword [DATA], 0
            e.Bytes(0x8B, 0x44, 0x24, 0x04);                   // mov eax, [esp+4]      EP
            e.Bytes(0x8B, 0x48, 0x04);                         // mov ecx, [eax+4]      CONTEXT*（x86 布局 +4）
            e.Bytes(0x8B, 0x15); e.Abs32((long)dataAddr + 0x10);   // mov edx, [DATA+0x10] savedEsp
            e.Bytes(0x83, 0xEA, 0x0C);                         // sub edx, 12           （等效 3 push + ret 已消费）
            e.Bytes(0x89, 0x91, 0xC4, 0x00, 0x00, 0x00);       // mov [ecx+0xC4], edx   ctx.Esp（x86 CONTEXT.Esp=0xC4；0xBC 是 SegCs！）
            e.Bytes(0x8B, 0x15); e.Abs32((long)dataAddr + 0x18);   // mov edx, [DATA+0x18] resumeEip（宿主写入）
            e.Bytes(0x89, 0x91, 0xB8, 0x00, 0x00, 0x00);       // mov [ecx+0xB8], edx   ctx.Eip（0xB0 是 Eax！）
            e.Bytes(0xB8, 0xFF, 0xFF, 0xFF, 0xFF);             // mov eax, -1
            e.Bytes(0xC2, 0x04, 0x00);             // ret 4（stdcall）
            return e.Finish();
        }

        /* ---- x64 注册桩（线程入口）: AVEH(1, handler)，句柄存 [DATA+0x20]，ExitThread 收尾 ----
         * 线程入口 rsp≡8 (mod16)，x64 ABI 要求 call 处 rsp≡0 (mod16) → 先 push 对齐 */
        private static byte[] BuildRegStubX64(ulong aveh, ulong exitThread, ulong handlerAddr, ulong dataAddr)
        {
            Emitter e = new Emitter();
            e.Bytes(0x53);                                     // push rbx              栈对齐
            e.Bytes(0x48, 0xC7, 0xC1); e.Imm32(1);             // mov rcx, 1            First
            e.Bytes(0x48, 0xBA); e.Imm64(handlerAddr);         // movabs rdx, handler
            e.Bytes(0x48, 0xB8); e.Imm64(aveh);                // movabs rax, AVEH
            e.Bytes(0xFF, 0xD0);                               // call rax
            e.Bytes(0x5B);                                     // pop rbx               恢复
            e.Bytes(0x49, 0xB8); e.Imm64(dataAddr);            // movabs r8, DATA
            e.Bytes(0x49, 0x89, 0x40, 0x20);                   // mov [r8+0x20], rax    vehHandle
            e.Bytes(0x48, 0x89, 0xC1);                         // mov rcx, rax          exit code = 句柄
            e.Bytes(0x48, 0xB8); e.Imm64(exitThread);          // movabs rax, ExitThread
            e.Bytes(0xFF, 0xD0);                               // call rax（不返回）
            return e.Finish();
        }

        /* ---- x86 注册桩: push handler; push 1; call AVEH（stdcall 自清栈），ExitThread 收尾 ----
         * 注意压栈顺序: stdcall 从右向左, arg1=First(1) 必须最后推！
         * 反序会实际调用 AVEH(First=handler, Handler=1)，VEH 列表里登记的
         * handler 指针为 1，真实异常一来调度器 call 1 即崩。 */
        private static byte[] BuildRegStubX86(ulong aveh, ulong exitThread, ulong handlerAddr, ulong dataAddr)
        {
            Emitter e = new Emitter();
            e.Bytes(0x68); e.Abs32((long)handlerAddr);         // push handler（先推 = arg2 Handler）
            e.Bytes(0x6A, 0x01);                               // push 1（后推 = arg1 First）
            e.Bytes(0xB8); e.Abs32((long)aveh);                // mov eax, AVEH
            e.Bytes(0xFF, 0xD0);                               // call eax
            e.Bytes(0xBA); e.Abs32((long)dataAddr);            // mov edx, DATA
            e.Bytes(0x89, 0x42, 0x20);                         // mov [edx+0x20], eax   vehHandle
            e.Bytes(0x50);                                     // push eax              exit code = 句柄
            e.Bytes(0xB8); e.Abs32((long)exitThread);          // mov eax, ExitThread
            e.Bytes(0xFF, 0xD0);                               // call eax（不返回）
            return e.Finish();
        }

        /* ---- 撤销桩: RVEH(handle) 后 ExitThread 收尾（远程线程桩不做最终 ret）---- */
        private static byte[] BuildRemoveStubX64(ulong handle, ulong rveh, ulong exitThread)
        {
            Emitter e = new Emitter();
            e.Bytes(0x53);                                     // push rbx              栈对齐
            e.Bytes(0x48, 0xB9); e.Imm64(handle);              // movabs rcx, handle
            e.Bytes(0x48, 0xB8); e.Imm64(rveh);                // movabs rax, RVEH
            e.Bytes(0xFF, 0xD0);                               // call rax
            e.Bytes(0x5B);                                     // pop rbx
            e.Bytes(0x48, 0x89, 0xC1);                         // mov rcx, rax（exit code = RVEH 结果）
            e.Bytes(0x48, 0xB8); e.Imm64(exitThread);          // movabs rax, ExitThread
            e.Bytes(0xFF, 0xD0);                               // call rax（不返回）
            return e.Finish();
        }

        private static byte[] BuildRemoveStubX86(ulong handle, ulong rveh, ulong exitThread)
        {
            Emitter e = new Emitter();
            e.Bytes(0x68); e.Imm32((uint)handle);              // push handle
            e.Bytes(0xB8); e.Abs32((long)rveh);                // mov eax, RVEH
            e.Bytes(0xFF, 0xD0);                               // call eax（stdcall 自清栈）
            e.Bytes(0x50);                                     // push eax（exit code）
            e.Bytes(0xB8); e.Abs32((long)exitThread);          // mov eax, ExitThread
            e.Bytes(0xFF, 0xD0);                               // call eax（不返回）
            return e.Finish();
        }
    }
}
