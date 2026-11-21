// TargetSession.cs - 统一目标会话：四种执行通道的公共门面
//   PipeDll        目标内 AsmLibrary.dll + 命名管道（VEH 全保护，主模式）
//   RemoteThreadVeh 远程线程 + 汇编 VEH（无 DLL 注入，handler 由 AsmVeh 远程注册）
//   RemoteThread   远程线程直调（兜底，无 VEH）
//   InProcess      主程序本进程执行（直接 P/Invoke 自身架构的 AsmLibrary）
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AsmStudio.Remote
{
    internal sealed class TargetSession : IDisposable
    {
        public ProcessInfo Target;
        public ExecChannel Channel;
        public List<SymbolInfo> Symbols = new List<SymbolInfo>();
        public string LastError;

        private PipeClient _pipe;
        private AsmVehProtector _veh;   // RemoteThreadVeh 通道的汇编 VEH

        // ---- 本进程 AsmLibrary 绑定 ----
        private static bool _localInit;
        private static LocalExec _localExec;

        private delegate int LocalExec(byte[] code, UIntPtr codeLen, ulong[] args, int nargs,
            IntPtr callTarget, uint timeout, IntPtr resultPtr);

        private delegate int DllInstallVeh();
        private delegate int DllRegSym([MarshalAs(UnmanagedType.LPStr)] string name, IntPtr addr);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr h, string name);

        private static string LocalDllName
        {
            get { return "AsmLibrary.dll"; }   // x86/x64 同名分目录
        }

        /// <summary>ret 重定向网关地址（管道模式有效，0=无）</summary>
        public ulong GateAddress
        {
            get { return _pipe != null ? _pipe.GateAddress : 0; }
        }

        /// <summary>连接目标</summary>
        public bool Connect(ProcessInfo target, ExecChannel channel)
        {
            Target = target;
            Channel = channel;
            LastError = null;

            if (channel == ExecChannel.InProcess)
            {
                if (!EnsureLocal())
                {
                    LastError = "本进程模式需要 " + LocalDllName +
                        "（与主程序同目录，或加入 PATH）";
                    return false;
                }
                Symbols.Clear();
                return true;
            }

            if (channel == ExecChannel.PipeDll)
            {
                _pipe = new PipeClient();
                if (_pipe.Connect(target.Pid, 1500))
                {
                    RefreshSymbols();
                    return true;
                }
                _pipe.Dispose();
                _pipe = null;
                LastError = "目标未加载 AsmLibrary（管道 asmexec_" + target.Pid + " 不存在）。" +
                    "可改用远程线程模式，或重新启动目标程序。";
                return false;
            }

            // RemoteThread / RemoteThreadVeh: 做一次权限探测；VEH 通道还需远程安装汇编 handler
            IntPtr h = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, target.Pid);
            if (h == IntPtr.Zero)
            {
                LastError = "OpenProcess 失败（权限不足或进程已退出）";
                return false;
            }
            NativeInterop.CloseHandle(h);
            if (channel == ExecChannel.RemoteThreadVeh)
            {
                if (_veh == null) _veh = new AsmVehProtector();
                string vehErr;
                if (!_veh.Install(target.Pid, target.Is64Bit, out vehErr))
                {
                    LastError = "汇编 VEH 安装失败: " + vehErr;
                    _veh.Dispose();
                    _veh = null;
                    return false;
                }
            }
            Symbols.Clear();
            return true;
        }

        /// <summary>探测目标是否有 AsmLibrary 管道</summary>
        public static bool HasPipe(int pid)
        {
            PipeClient probe = new PipeClient();
            try
            {
                return probe.Connect(pid, 400);
            }
            finally
            {
                probe.Dispose();
            }
        }

        public void RefreshSymbols()
        {
            if (_pipe != null && _pipe.Connected)
                Symbols = _pipe.GetSymbols();
            else
                Symbols.Clear();
        }

        /// <summary>执行机器码（可选 call 目标）</summary>
        public ExecutionResult Execute(byte[] code, ulong[] args, int nargs,
            ulong callTarget, bool modeCall, int timeoutMs)
        {
            if (Channel == ExecChannel.PipeDll && _pipe != null)
            {
                try
                {
                    return _pipe.Execute(code, args, nargs, callTarget, modeCall, timeoutMs);
                }
                catch (Exception ex)
                {
                    ExecutionResult r = new ExecutionResult();
                    r.Status = ExecStatus.NoConnection;
                    r.Message = "管道通信异常: " + ex.Message;
                    return r;
                }
            }
            if (Channel == ExecChannel.RemoteThread || Channel == ExecChannel.RemoteThreadVeh)
            {
                ExecutionResult r = RemoteThreadExecutor.Execute(Target, code, args, nargs,
                    modeCall ? callTarget : 0, timeoutMs,
                    Channel == ExecChannel.RemoteThreadVeh && _veh != null ? _veh.DataAddress : 0);
                if (Channel == ExecChannel.RemoteThreadVeh && _veh != null)
                    MergeVehOutcome(r, timeoutMs);
                return r;
            }
            ExecutionResult nr = new ExecutionResult();
            nr.Status = ExecStatus.NoConnection;
            nr.Message = "未连接";
            return nr;
        }

        /// <summary>汇编 VEH 通道：执行后读回数据块，把拦截结果并入执行结果</summary>
        private void MergeVehOutcome(ExecutionResult r, int timeoutMs)
        {
            if (r.Status == ExecStatus.Timeout)
            {
                _veh.ForceIdle();   // 线程仍在跑，解除保护标志避免 handler 长期误吞目标异常
                return;
            }
            uint faultCount, excCode;
            ulong excAddr, excRip;
            if (!_veh.Query(out faultCount, out excCode, out excAddr, out excRip))
                return;
            if (faultCount > 0)
            {
                r.Status = ExecStatus.Exception;
                r.ExcCode = excCode;
                r.ExcAddr = excAddr;
                r.ExcRip = excRip;
                r.ExcWasAccessViolation = excCode == 0xC0000005u || excCode == 0xC0000006u;
                r.Message = "汇编 VEH 拦截异常 " + r.ExceptionName +
                    " (rip=0x" + excRip.ToString("X") +
                    (excAddr != 0 ? ", addr=0x" + excAddr.ToString("X") : "") + ")";
            }
        }

        /// <summary>诊断: 读 handler 入口记录仪（死亡前最后快照用）</summary>
        public bool VehRecorder(out uint seenCount, out uint seenCode, out ulong seenRip)
        {
            if (_veh == null) { seenCount = 0; seenCode = 0; seenRip = 0; return false; }
            return _veh.QueryRecorder(out seenCount, out seenCode, out seenRip);
        }

        /// <summary>诊断: 读回 VEH 数据块原始状态（fault/code/rip/addr + 入口记录仪）</summary>
        public string VehDiag()
        {
            if (_veh == null) return "veh=null";
            uint fc, code;
            ulong addr, rip;
            if (!_veh.Query(out fc, out code, out addr, out rip))
                return "veh=query-fail";
            string s = "fault=" + fc + " code=0x" + code.ToString("X") +
                " rip=0x" + rip.ToString("X") + " addr=0x" + addr.ToString("X");
            uint sc, scode;
            ulong srip;
            if (_veh.QueryRecorder(out sc, out scode, out srip))
                s += " | seen=" + sc + " lastCode=0x" + scode.ToString("X") +
                    " lastRip=0x" + srip.ToString("X") + " flagAtSeen=0x" +
                    _veh.LastFlagSeen.ToString("X");
            return s;
        }

        /// <summary>InProcess 模式执行（走本地 asmhost_exec，完整 VEH 保护）</summary>
        public ExecutionResult ExecuteLocal(byte[] code, ulong[] args, int nargs,
            ulong callTarget, int timeoutMs)
        {
            ExecutionResult r = new ExecutionResult();
            if (_localExec == null)
            {
                r.Status = ExecStatus.NoConnection;
                r.Message = "本进程 AsmLibrary 未加载";
                return r;
            }
            int size = Marshal.SizeOf(typeof(AsmHostResultFull));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                int status = _localExec(code, (UIntPtr)(code != null ? code.Length : 0),
                    args, nargs,
                    callTarget != 0 ? (IntPtr)(unchecked((long)callTarget)) : IntPtr.Zero,
                    (uint)timeoutMs, mem);
                r.Status = MapStatus(status);
                AsmHostResultFull res = (AsmHostResultFull)Marshal.PtrToStructure(
                    mem, typeof(AsmHostResultFull));
                res.CopyRegs(r);
                r.Message = "本进程模式";
                return r;
            }
            catch (Exception ex)
            {
                r.Status = ExecStatus.Internal;
                r.Message = "本进程执行异常: " + ex.Message;
                return r;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        private static bool EnsureLocal()
        {
            if (_localInit) return _localExec != null;
            _localInit = true;

            IntPtr h = LoadLibrary(LocalDllName);
            if (h == IntPtr.Zero) return false;
            IntPtr pExec = GetProcAddress(h, "asmhost_exec");
            if (pExec == IntPtr.Zero) return false;
            _localExec = (LocalExec)Marshal.GetDelegateForFunctionPointer(
                pExec, typeof(LocalExec));
            return true;
        }

        /// <summary>本进程安装 VEH（InProcess 模式建议调用一次）</summary>
        public static bool InstallLocalVeh()
        {
            IntPtr h = LoadLibrary(LocalDllName);
            if (h == IntPtr.Zero) return false;
            IntPtr p = GetProcAddress(h, "asmhost_install_veh");
            if (p == IntPtr.Zero) return false;
            DllInstallVeh f = (DllInstallVeh)Marshal.GetDelegateForFunctionPointer(
                p, typeof(DllInstallVeh));
            return f() >= 0;
        }

        /// <summary>本进程注册符号（InProcess 模式使用）</summary>
        public static bool RegisterLocalSymbol(string name, IntPtr addr)
        {
            IntPtr h = LoadLibrary(LocalDllName);
            if (h == IntPtr.Zero) return false;
            IntPtr p = GetProcAddress(h, "asmhost_register_symbol");
            if (p == IntPtr.Zero) return false;
            DllRegSym f = (DllRegSym)Marshal.GetDelegateForFunctionPointer(p, typeof(DllRegSym));
            return f(name, addr) >= 0;
        }

        /// <summary>本进程演示符号（InProcess 模式的快速试验目标）</summary>
        public void LoadInProcessDemoSymbols()
        {
            _demoAdd = DemoAdd;
            _demoEcho = DemoEcho;
            RegisterLocalSymbol("DemoAdd",
                Marshal.GetFunctionPointerForDelegate(_demoAdd));
            RegisterLocalSymbol("DemoEcho",
                Marshal.GetFunctionPointerForDelegate(_demoEcho));
        }

        private static DemoAddDel _demoAdd;
        private static DemoEchoDel _demoEcho;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DemoAddDel(int a, int b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DemoEchoDel(IntPtr p);
        private static int DemoAdd(int a, int b) { return a + b; }
        private static IntPtr DemoEcho(IntPtr p) { return p; }

        private static ExecStatus MapStatus(int s)
        {
            switch (s)
            {
                case 0: return ExecStatus.Ok;
                case 1: return ExecStatus.Exception;
                case 2: return ExecStatus.Timeout;
                case 3: return ExecStatus.BadArg;
                case 4: return ExecStatus.Busy;
                default: return ExecStatus.Internal;
            }
        }

        public void Dispose()
        {
            if (_pipe != null)
            {
                _pipe.Dispose();
                _pipe = null;
            }
            if (_veh != null)
            {
                _veh.Dispose();
                _veh = null;
            }
        }
    }

    /// <summary>asmhost_result 的完整 C# 镜像（布局与 asmhost.h 一致）</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AsmHostResultFull
    {
        public int status;
        public ulong ret_value;
        public ulong regs_mid0, regs_mid1, regs_mid2, regs_mid3,
                     regs_mid4, regs_mid5, regs_mid6, regs_mid7,
                     regs_mid8, regs_mid9, regs_mid10, regs_mid11,
                     regs_mid12, regs_mid13, regs_mid14, regs_mid15;
        public ulong regs_final0, regs_final1, regs_final2, regs_final3,
                     regs_final4, regs_final5, regs_final6, regs_final7,
                     regs_final8, regs_final9, regs_final10, regs_final11,
                     regs_final12, regs_final13, regs_final14, regs_final15;
        public ulong flags_mid;
        public ulong flags_final;
        public ulong exc0, exc1, exc2, exc3, exc4, exc5, exc6, exc7,
                     exc8, exc9, exc10, exc11, exc12, exc13, exc14, exc15;
        public ulong exc_flags;
        public uint exc_code;
        public ulong exc_addr;
        public int exc_was_av;
        public int exc_rw;
        public ulong exc_rip;

        public void CopyRegs(ExecutionResult r)
        {
            ulong[] mid = new ulong[] { regs_mid0, regs_mid1, regs_mid2, regs_mid3,
                regs_mid4, regs_mid5, regs_mid6, regs_mid7, regs_mid8, regs_mid9,
                regs_mid10, regs_mid11, regs_mid12, regs_mid13, regs_mid14, regs_mid15 };
            ulong[] fin = new ulong[] { regs_final0, regs_final1, regs_final2, regs_final3,
                regs_final4, regs_final5, regs_final6, regs_final7, regs_final8,
                regs_final9, regs_final10, regs_final11, regs_final12, regs_final13,
                regs_final14, regs_final15 };
            ulong[] exc = new ulong[] { exc0, exc1, exc2, exc3, exc4, exc5, exc6, exc7,
                exc8, exc9, exc10, exc11, exc12, exc13, exc14, exc15 };
            Array.Copy(mid, r.RegsMid, RegIdx.Count);
            Array.Copy(fin, r.RegsFinal, RegIdx.Count);
            Array.Copy(exc, r.RegsExc, RegIdx.Count);
            r.FlagsMid = flags_mid;
            r.FlagsFinal = flags_final;
            r.FlagsExc = exc_flags;
            r.ExcCode = exc_code;
            r.ExcAddr = exc_addr;
            r.ExcWasAccessViolation = exc_was_av != 0;
            r.ExcReadOrWrite = exc_rw;
            r.ExcRip = exc_rip;
        }
    }
}
