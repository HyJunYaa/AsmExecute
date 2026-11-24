// ProcDiag.cs - 进程权限诊断：连不上时把"谁没权限"说清楚，而不是只丢一句 access denied
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AsmStudio.Localization;

namespace AsmStudio.Remote
{
    internal static class ProcDiag
    {
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;
        private const int TokenElevation = 20;

        [DllImport("advapi32", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass,
            IntPtr buffer, int bufferLen, out int returnLen);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint pid, out uint sessionId);

        [DllImport("kernel32")]
        private static extern bool IsWow64Process(IntPtr process, out bool wow64);

        public static string Self()
        {
            return Describe((uint)Process.GetCurrentProcess().Id);
        }

        /// <summary>
        /// 形如 "Medium(RID 0x2000) session=1 x64 elevated=no"。
        /// 提权/沙箱问题一看就知道：自己 Medium 而目标 High(0x3000) 就是没提权；
        /// 自己 RID 比 0x2000 还低，说明本进程被沙箱/受限令牌拉起来了。
        /// </summary>
        public static string Describe(uint pid)
        {
            string integrity = "?";
            bool elevated = false;
            IntPtr h = NativeInterop.OpenProcess(
                NativeInterop.PROCESS_QUERY_INFORMATION, false, (int)pid);
            if (h == IntPtr.Zero)
                h = NativeInterop.OpenProcess(NativeInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);

            string bits = "?";
            uint session = 0;
            if (h != IntPtr.Zero)
            {
                try
                {
                    bool wow;
                    if (IsWow64Process(h, out wow)) bits = wow ? "x86" : "x64";
                    IntPtr token;
                    if (OpenProcessToken(h, TOKEN_QUERY, out token))
                    {
                        try
                        {
                            integrity = ReadIntegrity(token);
                            elevated = ReadElevation(token);
                        }
                        finally { NativeInterop.CloseHandle(token); }
                    }
                }
                finally { NativeInterop.CloseHandle(h); }
            }
            ProcessIdToSessionId(pid, out session);
            return string.Format("{0} session={1} {2} elevated={3}",
                integrity, session, bits, elevated ? "yes" : "no");
        }

        private static string ReadIntegrity(IntPtr token)
        {
            int rid;
            return TryReadIntegrity(token, out rid) ? IntegrityName(rid) : "?";
        }

        private static bool TryReadIntegrity(IntPtr token, out int rid)
        {
            rid = 0;
            int len;
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out len);
            if (len <= 0) return false;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buf, len, out len)) return false;
                IntPtr sid = Marshal.ReadIntPtr(buf);              // TOKEN_MANDATORY_LABEL.Sid
                int subCount = Marshal.ReadByte(sid + 1);          // SID.SubAuthorityCount
                rid = Marshal.ReadInt32(sid + 8 + (subCount - 1) * 4);
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static string IntegrityName(int rid)
        {
            /* 级别名走本地化：诊断会同时出现在状态栏与日志里，英文界面不该冒中文，
             * 中文界面也不该把 High/Medium/Low 当英文术语硬塞给用户。 */
            string name;
            if (rid >= 0x4000) name = "System";
            else if (rid >= 0x3000) name = LanguageService.T("High/Admin");
            else if (rid >= 0x2000) name = LanguageService.T("Medium/Normal");
            else if (rid >= 0x1000) name = LanguageService.T("Low/Restricted");
            else name = "Untrusted";
            return name + "(0x" + rid.ToString("X") + ")";
        }

        /// <summary>
        /// 只知道"自己 Low"没用，要说清是谁压下来的。
        /// 判据很干净：进程完整性不会自己下降，只可能来自两处 ——
        ///   ① 启动它的父进程本来就 Low（沙箱/受限启动器链）；
        ///   ② 可执行文件（或其所在目录）带 Low 完整性标签，Windows 会强制以 Low 创建进程，
        ///      这时父进程是 explorer（普通权限）也照样降权，与"怎么启动"无关。
        /// 返回 null 表示本进程不受限。
        /// </summary>
        public static string SelfRestrictionHint()
        {
            if (!SelfRestricted) return null;

            int ppid;
            string pname;
            if (!TryGetParent(Process.GetCurrentProcess().Id, out ppid, out pname) || ppid <= 0)
                return LanguageService.T("This app runs in a restricted context (Low integrity): the process list will be incomplete and write access to other processes is denied; its parent process cannot be read either, so the cause is unknown");

            int prid;
            bool known = TryIntegrityRid((uint)ppid, out prid);
            string pdesc = Describe((uint)ppid);

            if (!known)
                return string.Format(LanguageService.T(
                    "This app runs at Low integrity and its parent {0}(pid={1}) cannot be read; the process list will be incomplete and write access to other processes is denied"),
                    pname, ppid);

            if (prid >= 0x2000)
                return string.Format(LanguageService.T(
                    "This app runs at Low integrity, but its parent {0}(pid={1}, {2}) has normal privileges - so AsmStudio.exe itself or its folder carries a Low integrity label and Windows forced the demotion. This has nothing to do with how you launched it (administrator or not, from which folder). Fix: re-run build.bat (it resets bin at the end), or run the local repair tool from the project root, or manually run icacls /setintegritylevel (OI)(CI)M /T on that folder - then restart this app"),
                    pname, ppid, pdesc);

            return string.Format(LanguageService.T(
                "This app runs at Low integrity and its parent {0}(pid={1}, {2}) is restricted too - the demotion came from upstream in the launch chain (sandbox / restricted launcher). Launch the exe directly from Explorer"),
                pname, ppid, pdesc);
        }

        private static bool TryIntegrityRid(uint pid, out int rid)
        {
            rid = 0;
            IntPtr h = NativeInterop.OpenProcess(
                NativeInterop.PROCESS_QUERY_INFORMATION, false, (int)pid);
            if (h == IntPtr.Zero)
                h = NativeInterop.OpenProcess(NativeInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(h, TOKEN_QUERY, out token)) return false;
                try { return TryReadIntegrity(token, out rid); }
                finally { NativeInterop.CloseHandle(token); }
            }
            finally { NativeInterop.CloseHandle(h); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W entry);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W entry);

        private static bool TryGetParent(int pid, out int parentPid, out string parentName)
        {
            parentPid = 0;
            parentName = "?";
            IntPtr snap = CreateToolhelp32Snapshot(0x00000002 /*TH32CS_SNAPPROCESS*/, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return false;
            try
            {
                PROCESSENTRY32W e = new PROCESSENTRY32W();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                if (!Process32FirstW(snap, ref e)) return false;
                do
                {
                    if ((int)e.th32ProcessID == pid)
                    {
                        parentPid = (int)e.th32ParentProcessID;
                        parentName = e.szExeFile;
                        return true;
                    }
                } while (Process32NextW(snap, ref e));
                return false;
            }
            finally { NativeInterop.CloseHandle(snap); }
        }

        /// <summary>本进程是否跑在受限上下文（Low 及以下：沙箱、受限令牌、AppContainer 等）</summary>
        public static bool SelfRestricted
        {
            get
            {
                IntPtr h = NativeInterop.OpenProcess(
                    NativeInterop.PROCESS_QUERY_INFORMATION, false, Process.GetCurrentProcess().Id);
                if (h == IntPtr.Zero) return false;
                try
                {
                    IntPtr token;
                    if (!OpenProcessToken(h, TOKEN_QUERY, out token)) return false;
                    try
                    {
                        int rid;
                        return TryReadIntegrity(token, out rid) && rid < 0x2000;
                    }
                    finally { NativeInterop.CloseHandle(token); }
                }
                finally { NativeInterop.CloseHandle(h); }
            }
        }

        private static bool ReadElevation(IntPtr token)
        {
            int len;
            GetTokenInformation(token, TokenElevation, IntPtr.Zero, 0, out len);
            if (len < 4) return false;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetTokenInformation(token, TokenElevation, buf, len, out len)) return false;
                return Marshal.ReadInt32(buf) != 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>OpenProcess 失败码 → 人话 + 该怎么办</summary>
        public static string ExplainOpenFailure(int win32Error, ProcessInfo target)
        {
            switch (win32Error)
            {
                case 5:   // ERROR_ACCESS_DENIED
                    {
                        string tail = SelfRestricted
                            ? LanguageService.T("; also this app itself runs at Low integrity (see the [!] line in the log) - every normal process will then deny write access, fix the integrity level first")
                            : LanguageService.T("; for an elevated target, run this app as administrator");
                        return string.Format(LanguageService.T("Access denied - target {0}, this app {1}{2}"),
                            Describe((uint)target.Pid), Self(), tail);
                    }
                case 87:  // ERROR_INVALID_PARAMETER
                    return LanguageService.T("pid no longer exists (process exited or pid reused), please rescan");
                case 6:   // ERROR_INVALID_HANDLE
                    return LanguageService.T("invalid system handle (the target may have just exited)");
                default:
                    return string.Format(LanguageService.T("win32={0}, target {1}, this app {2}"),
                        win32Error, Describe((uint)target.Pid), Self());
            }
        }
    }
}
