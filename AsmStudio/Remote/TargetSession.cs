// TargetSession.cs - 统一目标会话：远程执行通道的公共门面
//   RemoteThreadVeh 远程线程 + 汇编 VEH（无 DLL 注入，handler 由 AsmVeh 远程注册，主模式）
//   RemoteThread    远程线程直调（兜底，无 VEH）
using System;
using AsmStudio.Localization;
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

        private AsmVehProtector _veh;   // RemoteThreadVeh 通道的汇编 VEH

        /// <summary>Connect目标</summary>
        public bool Connect(ProcessInfo target, ExecChannel channel)
        {
            Target = target;
            Channel = channel;
            LastError = null;
            // 新目标（pid 可能正是刚退出的旧进程用过的）→ 丢掉上一实例解析出的地址
            RemoteSymbols.ClearApiCache();

            // 做一次权限探测；VEH 通道还需远程安装汇编 handler
            IntPtr h = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, target.Pid);
            if (h == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LastError = LanguageService.T("OpenProcess failed: ") + ProcDiag.ExplainOpenFailure(err, target);
                return false;
            }
            NativeInterop.CloseHandle(h);
            if (channel == ExecChannel.RemoteThreadVeh)
            {
                if (_veh == null) _veh = new AsmVehProtector();
                string vehErr;
                if (!_veh.Install(target.Pid, target.Is64Bit, out vehErr))
                {
                    LastError = LanguageService.T("Assembly VEH install failed: ") + vehErr;
                    _veh.Dispose();
                    _veh = null;
                    return false;
                }
            }
            Symbols.Clear();
            return true;
        }

        /// <summary>执行机器码（可选 call 目标）</summary>
        public ExecutionResult Execute(byte[] code, ulong[] args, int nargs,
            ulong callTarget, bool modeCall, int timeoutMs)
        {
            ExecutionResult r = RemoteThreadExecutor.Execute(Target, code, args, nargs,
                modeCall ? callTarget : 0, timeoutMs,
                Channel == ExecChannel.RemoteThreadVeh && _veh != null ? _veh.DataAddress : 0);
            if (Channel == ExecChannel.RemoteThreadVeh && _veh != null)
                MergeVehOutcome(r, timeoutMs);
            return r;
        }

        /// <summary>在目标进程分配缓冲区并写入任意字节，返回远程地址（0=失败）。
        /// 内存页的文本/数值/十六进制与调用设置的字符串参数都经此落库；
        /// 调用方负责用 FreeRemote 释放（超时/泄漏策略除外）。</summary>
        public ulong AllocBytes(byte[] data)
        {
            if (Target == null || data == null || data.Length == 0) return 0;
            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, Target.Pid);
            if (proc == IntPtr.Zero) return 0;
            try
            {
                IntPtr p = NativeInterop.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)data.Length,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_READWRITE);
                if (p == IntPtr.Zero) return 0;
                UIntPtr written;
                if (!NativeInterop.WriteProcessMemory(proc, p, data, (UIntPtr)data.Length, out written))
                    return 0;
                return (ulong)p.ToInt64();
            }
            finally
            {
                NativeInterop.CloseHandle(proc);
            }
        }

        /// <summary>在目标进程分配缓冲区并写入 UTF-16LE 字符串（含终止符），返回远程地址（0=失败）。
        /// 调用设置里的引号字符串参数经此传指针；调用方负责用 FreeRemote 释放（超时泄漏除外）。</summary>
        public ulong AllocUtf16(string s)
        {
            if (Target == null) return 0;
            if (s == null) s = "";
            return AllocBytes(System.Text.Encoding.Unicode.GetBytes(s + "\0"));
        }

        /// <summary>释放目标进程内缓冲区（best effort，失败静默——页可能已被目标持有）。</summary>
        public void FreeRemote(ulong addr)
        {
            if (Target == null || addr == 0) return;
            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, Target.Pid);
            if (proc == IntPtr.Zero) return;
            try
            {
                NativeInterop.VirtualFreeEx(proc, new IntPtr(unchecked((long)addr)),
                    UIntPtr.Zero, NativeInterop.MEM_RELEASE);
            }
            finally
            {
                NativeInterop.CloseHandle(proc);
            }
        }

        /// <summary>汇编 VEH 通道：执行后读回data块，把拦截结果并入执行结果</summary>
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
                // 故障现场寄存器快照（handler 记录仪读回）
                AsmVehProtector.VehSnapshot snap;
                if (_veh.ReadSnapshot(out snap))
                {
                    Array.Copy(snap.Regs, r.RegsExc, RegIdx.Count);
                    if (snap.Eflags >= 0) r.FlagsExc = (ulong)snap.Eflags;
                    if (r.ExcWasAccessViolation && snap.ExcReadWrite >= 0)
                        r.ExcReadOrWrite = snap.ExcReadWrite;
                    if (excAddr == 0 && snap.ExcAddr != 0) r.ExcAddr = snap.ExcAddr;
                }
                r.Message = LanguageService.T("Assembly VEH caught exception ") + r.ExceptionName +
                    " (rip=0x" + excRip.ToString("X") +
                    (r.ExcAddr != 0 ? ", addr=0x" + r.ExcAddr.ToString("X") : "") + ")";
            }
            else if (r.Status == ExecStatus.Ok)
            {
                // 本会话干净完成，但目标自身线程可能发生过异常（已放行）——提示给用户
                AsmVehProtector.VehSnapshot snap;
                if (_veh.ReadSnapshot(out snap) && snap.SeenForeign > 0)
                {
                    r.Message = LanguageService.T("RemoteThread+VEH Protection") +
                        " | target threads raised " + snap.SeenForeign +
                        " non-session exception(s), passed through";
                }
            }
        }

        /// <summary>诊断: 读 handler 入口记录仪（死亡前最后快照用）</summary>
        public bool VehRecorder(out uint seenCount, out uint seenCode, out ulong seenRip)
        {
            if (_veh == null) { seenCount = 0; seenCode = 0; seenRip = 0; return false; }
            return _veh.QueryRecorder(out seenCount, out seenCode, out seenRip);
        }

        /// <summary>诊断: 读回 VEH data块原始状态（fault/code/rip/addr + 入口记录仪）</summary>
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

        public void Dispose()
        {
            if (_veh != null)
            {
                _veh.Dispose();
                _veh = null;
            }
        }
    }
}
