// AsmVeh.cs - 汇编级 VEH 保护（无 DLL 注入）
// 原理: 在目标进程分配一页 [VEH handler 机器码][data块]，经远程线程调
//       AddVectoredExceptionHandler 注册 handler。用户代码崩溃时 handler
//       判定是否属于本会话运行（flag==1 且异常码属于崩溃类），是则把线程
//       上下文 RSP/RIP 重置到恢复桩（abort stub），异常被吞掉，线程干净退出；
//       否则返回 CONTINUE_SEARCH 交还目标自身异常链。
// data块布局 (112 字节；x86 槽位为 u32，x64 槽位为 u64，按 _is64 解读):
//   +0x00 u32 flag        1 = 本会话代码运行中
//   +0x04 u32 faultCount  拦截次数
//   +0x08 u32 lastExcCode 最近拦截的异常码
//   +0x0C x86 u32 faultEsp    异常时 CONTEXT.Esp（取自 CONTEXT，非 handler live esp）
//   +0x10 u64 savedRsp    运行桩入口 RSP（桩写入；x86 仅用低 32 位）
//   +0x18 u64 abortRip    恢复桩地址（桩写入）
//   +0x20 u64 vehHandle   AddVectoredExceptionHandler 返回值
//   +0x28 u64 excAddr     AV 时 ExceptionInformation[1]（违例访问地址）
//   +0x30 x86 u32 faultEax | x64 u64 faultRsp（异常时 CONTEXT.Rsp）
//   +0x34 x86 u32 faultEcx
//   +0x38 u32 recCode     入口记录仪：每次进 handler 记录的 ExceptionCode
//   +0x3C x86 u32 faultEbx
//   +0x40 u64 recRip      入口记录仪：异常发生时的 RIP
//   +0x48 u32 seenCount   入口记录仪：进入次数
//   +0x4C u32 flagSeen    入口记录仪：进入时的 flag
//   +0x50 x86 u32 faultEdx | x64 u64 faultRax（异常时 CONTEXT.Rax）
//   +0x54 x86 u32 faultEsi
//   +0x58 x86 u32 faultEdi | x64 u64 faultRcx
//   +0x5C x86 u32 faultEbp
//   +0x60 x86 u32 faultEflags | x64 u64 faultRbx
//   +0x64 int excRW       ExceptionInformation[0]（0=读 1=写 8=DEP，非 AV 无意义）
//   +0x68 u64 ourTeb      会话线程 TEB（桩入口写 fs:[0x18]/gs:[0x30]；0=未运行）
//   +0x6C u32 seenForeign (x86) 外线程异常计数 | x64 同槽被 ourTeb 占用
//   +0x70 u32 seenForeign (x64) 外线程异常计数（handler 放行时 ++）
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal sealed class AsmVehProtector : IDisposable
    {
        public const int DataSize = 128;

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

        /// <summary>异常现场全量快照（记录仪 + 故障寄存器）</summary>
        public sealed class VehSnapshot
        {
            public uint FaultCount, ExcCode, SeenCount, RecCode, FlagSeen, SeenForeign;
            public int ExcReadWrite = -1;
            public int Eflags = -1;
            public ulong ExcAddr, ExcRip;
            public readonly ulong[] Regs = new ulong[16];   // RegIdx 序；未记录槽位为 0
        }

        /// <summary>读回完整异常快照（含故障时寄存器，按目标位数解读槽位）</summary>
        public bool ReadSnapshot(out VehSnapshot s)
        {
            s = null;
            if (!_installed) return false;
            byte[] buf = new byte[DataSize];
            UIntPtr read;
            if (!NativeInterop.ReadProcessMemory(_proc, _dataAddr, buf, (UIntPtr)DataSize, out read))
                return false;
            s = new VehSnapshot();
            s.FaultCount = BitConverter.ToUInt32(buf, 0x04);
            s.ExcCode = BitConverter.ToUInt32(buf, 0x08);
            s.ExcAddr = BitConverter.ToUInt64(buf, 0x28);
            s.RecCode = BitConverter.ToUInt32(buf, 0x38);
            s.ExcRip = BitConverter.ToUInt64(buf, 0x40);
            s.SeenCount = BitConverter.ToUInt32(buf, 0x48);
            s.FlagSeen = BitConverter.ToUInt32(buf, 0x4C);
            s.SeenForeign = _is64 ? BitConverter.ToUInt32(buf, 0x70)
                                  : BitConverter.ToUInt32(buf, 0x6C);
            if (_is64)
            {
                s.Regs[RegIdx.Rsp] = BitConverter.ToUInt64(buf, 0x30);
                s.Regs[RegIdx.Rax] = BitConverter.ToUInt64(buf, 0x50);
                s.Regs[RegIdx.Rcx] = BitConverter.ToUInt64(buf, 0x58);
                s.Regs[RegIdx.Rbx] = BitConverter.ToUInt64(buf, 0x60);
                s.ExcReadWrite = (int)BitConverter.ToUInt64(buf, 0x64);
            }
            else
            {
                s.Regs[RegIdx.Rsp] = BitConverter.ToUInt32(buf, 0x0C);
                s.Regs[RegIdx.Rax] = BitConverter.ToUInt32(buf, 0x30);
                s.Regs[RegIdx.Rcx] = BitConverter.ToUInt32(buf, 0x34);
                s.Regs[RegIdx.Rbx] = BitConverter.ToUInt32(buf, 0x3C);
                s.Regs[RegIdx.Rdx] = BitConverter.ToUInt32(buf, 0x50);
                s.Regs[RegIdx.Rsi] = BitConverter.ToUInt32(buf, 0x54);
                s.Regs[RegIdx.Rdi] = BitConverter.ToUInt32(buf, 0x58);
                s.Regs[RegIdx.Rbp] = BitConverter.ToUInt32(buf, 0x5C);
                s.Eflags = (int)BitConverter.ToUInt32(buf, 0x60);
                s.ExcReadWrite = BitConverter.ToInt32(buf, 0x64);
            }
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
         * OURS 路径: 记录 fault → 清 flag →
         *   ctx.Rip = [DATA+0x18]（宿主写入的桩收尾段地址）、ctx.Rsp = [DATA+0x10]-24
         *   （等效"用户码已返回、3 个 push 仍在栈上"）→ EXCEPTION_CONTINUE_EXECUTION。
         *   线程在桩收尾段正常 ExitThread，进程存活。 */
        private static byte[] BuildHandlerX64(ulong dataAddr)
        {
            Emitter e = new Emitter();
            e.Bytes(0x49, 0xB9); e.Imm64(dataAddr);            // movabs r9, DATA
            /* ---- 会话线程识别（同 x86）：外线程异常只计数并放行 ---- */
            e.Bytes(0x65, 0x48, 0x8B, 0x04, 0x25, 0x30, 0x00, 0x00, 0x00); // mov rax, gs:[0x30]  本线程 TEB
            e.Bytes(0x49, 0x8B, 0x52, 0x68);                   // mov rdx, [r9+0x68]   会话线程 TEB（桩写入）
            e.Bytes(0x48, 0x39, 0xD0);                         // cmp rax, rdx
            e.JeRel8("ours_go");
            e.Bytes(0x41, 0xFF, 0x41, 0x6C);                   // inc dword [r9+0x6C]  seenForeign++
            e.Bytes(0x31, 0xC0);                               // xor eax, eax
            e.Bytes(0xC3);                                     // ret（foreign: CONTINUE_SEARCH）
            e.Label("ours_go");
            e.Bytes(0x41, 0x8B, 0x01);                         // mov eax, [r9]         flagSeen 诊断
            e.Bytes(0x41, 0x89, 0x41, 0x4C);                   // mov [r9+0x4C], eax
            e.Bytes(0x48, 0x8B, 0x01);                         // mov rax, [rcx]        ExceptionRecord*
            e.Bytes(0x8B, 0x10);                               // mov edx, [rax]        ExceptionCode
            e.Bytes(0x41, 0x89, 0x51, 0x38);                   // mov [r9+0x38], edx    lastSeenCode
            e.Bytes(0x48, 0x8B, 0x49, 0x08);                   // mov rcx, [rcx+8]      CONTEXT*
            e.Bytes(0x48, 0x8B, 0x91, 0xF8, 0x00, 0x00, 0x00); // mov rdx, [rcx+0xF8]   rip
            e.Bytes(0x49, 0x89, 0x51, 0x40);                   // mov [r9+0x40], rdx    lastSeenRip
            e.Bytes(0x48, 0x8B, 0x91, 0x98, 0x00, 0x00, 0x00); // mov rdx, [rcx+0x98]   ctx.Rsp
            e.Bytes(0x49, 0x89, 0x51, 0x30);                   // mov [r9+0x30], rdx    faultRsp
            e.Bytes(0x48, 0x8B, 0x91, 0x78, 0x00, 0x00, 0x00); // mov rdx, [rcx+0x78]   ctx.Rax
            e.Bytes(0x49, 0x89, 0x51, 0x50);                   // mov [r9+0x50], rdx    faultRax
            e.Bytes(0x48, 0x8B, 0x91, 0x80, 0x00, 0x00, 0x00); // mov rdx, [rcx+0x80]   ctx.Rcx
            e.Bytes(0x49, 0x89, 0x51, 0x58);                   // mov [r9+0x58], rdx    faultRcx
            e.Bytes(0x48, 0x8B, 0x91, 0x90, 0x00, 0x00, 0x00); // mov rdx, [rcx+0x90]   ctx.Rbx
            e.Bytes(0x49, 0x89, 0x51, 0x60);                   // mov [r9+0x60], rdx    faultRbx
            e.Bytes(0x48, 0x8B, 0x50, 0x20);                   // mov rdx, [rax+0x20]   ER.ExceptionInformation[0]（0=读 1=写）
            e.Bytes(0x49, 0x89, 0x51, 0x64);                   // mov [r9+0x64], rdx    excRW
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
         *   CONTEXT: Eax=0xB0 Ecx=0xAC Edx=0xA8 Ebx=0xA4 Esi=0xA0 Edi=0x9C
         *            Ebp=0xB4 Eip=0xB8 SegCs=0xBC EFlags=0xC0 Esp=0xC4；
         *   EXCEPTION_RECORD: ExceptionInformation[0]=+0x14（0=读 1=写）、[1]=+0x18（AV 访问地址）；
         *   handler 返回约定 stdcall ret 4（实测：plain ret 会炸调度器，连 SEH 都走不到）。
         * 入口先做完整现场记录仪（寄存器一律取自 CONTEXT 而非 live 值），再走过滤。
         * OURS 路径同 x64: 记录 fault → 清 flag → 改 ctx（Eip=resumeEip、Esp=savedEsp-12）
         *   → CONTINUE_EXECUTION，线程在桩收尾段 ExitThread。 */
        private static byte[] BuildHandlerX86(ulong dataAddr)
        {
            Emitter e = new Emitter();
            /* ---- 会话线程识别：非本会话线程（目标自身线程）的异常一律放行 ----
             * 桩入口把本线程 TEB 写入 [DATA+0x68]；这里比对 fs:[0x18]。
             * 外线程异常只计数（seenForeign）并 CONTINUE_SEARCH，绝不劫持目标线程，
             * 也不覆盖本会话记录仪槽位（避免诊断数据被外线程冲掉）。 */
            e.Bytes(0x64, 0xA1, 0x18, 0x00, 0x00, 0x00);       // mov eax, fs:[0x18]    本线程 TEB
            e.Bytes(0x8B, 0x0D); e.Abs32((long)dataAddr + 0x68);   // mov ecx, [DATA+0x68]  会话线程 TEB（桩写入）
            e.Bytes(0x39, 0xC8);                               // cmp eax, ecx
            e.JeRel8("ours_go");
            e.Bytes(0xFF, 0x05); e.Abs32((long)dataAddr + 0x6C);   // inc dword [DATA+0x6C] seenForeign++
            e.Bytes(0x31, 0xC0);                               // xor eax, eax
            e.Bytes(0xC2, 0x04, 0x00);                         // ret 4（foreign: CONTINUE_SEARCH）
            e.Label("ours_go");
            e.Bytes(0x8B, 0x44, 0x24, 0x04);                   // mov eax, [esp+4]      EXCEPTION_POINTERS*
            e.Bytes(0x8B, 0x15); e.Abs32((long)dataAddr);      // mov edx, [DATA]       flag（入口快照，x64 版同款诊断）
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x4C);   // mov [DATA+0x4C], edx  flagSeen
            e.Bytes(0x8B, 0x08);                               // mov ecx, [eax]        ExceptionRecord*
            e.Bytes(0x8B, 0x11);                               // mov edx, [ecx]        ExceptionCode
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x38);   // mov [DATA+0x38], edx  recCode
            e.Bytes(0x8B, 0x51, 0x18);                         // mov edx, [ecx+0x18]   ER.ExceptionInformation[1] 访问地址
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x28);   // mov [DATA+0x28], edx  excAddr
            e.Bytes(0x8B, 0x51, 0x14);                         // mov edx, [ecx+0x14]   ER.ExceptionInformation[0]（0=读 1=写）
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x64);   // mov [DATA+0x64], edx  excRW
            e.Bytes(0x8B, 0x50, 0x04);                         // mov edx, [eax+4]      CONTEXT*（x86 EP={ER*+0,CTX*+4}，+8 是 x64 布局！）
            e.Bytes(0x8B, 0x8A, 0xB8, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xB8]   ctx.Eip（x86 CONTEXT.Eip=0xB8）
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x40);   // mov [DATA+0x40], ecx  recRip
            e.Bytes(0x8B, 0x8A, 0xC4, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xC4]   ctx.Esp
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x0C);   // mov [DATA+0x0C], ecx  faultEsp
            e.Bytes(0x8B, 0x8A, 0xB0, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xB0]   ctx.Eax（0xB0，非 Eip！）
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x30);   // mov [DATA+0x30], ecx  faultEax
            e.Bytes(0x8B, 0x8A, 0xAC, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xAC]   ctx.Ecx
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x34);   // mov [DATA+0x34], ecx  faultEcx
            e.Bytes(0x8B, 0x8A, 0xA4, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xA4]   ctx.Ebx
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x3C);   // mov [DATA+0x3C], ecx  faultEbx
            e.Bytes(0x8B, 0x8A, 0xA8, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xA8]   ctx.Edx
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x50);   // mov [DATA+0x50], ecx  faultEdx
            e.Bytes(0x8B, 0x8A, 0xA0, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xA0]   ctx.Esi
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x54);   // mov [DATA+0x54], ecx  faultEsi
            e.Bytes(0x8B, 0x8A, 0x9C, 0x00, 0x00, 0x00);       // mov ecx, [edx+0x9C]   ctx.Edi
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x58);   // mov [DATA+0x58], ecx  faultEdi
            e.Bytes(0x8B, 0x8A, 0xB4, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xB4]   ctx.Ebp
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x5C);   // mov [DATA+0x5C], ecx  faultEbp
            e.Bytes(0x8B, 0x8A, 0xC0, 0x00, 0x00, 0x00);       // mov ecx, [edx+0xC0]   ctx.EFlags
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x60);   // mov [DATA+0x60], ecx  faultEflags
            e.Bytes(0x8B, 0x0D); e.Abs32((long)dataAddr);      // mov ecx, [DATA]       flagSeen
            e.Bytes(0x89, 0x0D); e.Abs32((long)dataAddr + 0x4C);   // mov [DATA+0x4C], ecx
            e.Bytes(0xFF, 0x05); e.Abs32((long)dataAddr + 0x48);   // inc dword [DATA+0x48] seenCount
            e.Bytes(0x8B, 0x08);                               // mov ecx, [eax]        恢复 ER*
            e.Bytes(0x8B, 0x11);                               // mov edx, [ecx]        恢复 ExceptionCode（供过滤）
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
            e.Bytes(0x8B, 0x51, 0x18);                         // mov edx, [ecx+0x18]   ER->Information[1]（x86 ER 布局，AV 时=访问地址）
            e.Bytes(0x89, 0x15); e.Abs32((long)dataAddr + 0x28);   // mov [DATA+0x28], edx  excAddr（x64 版同款）
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
