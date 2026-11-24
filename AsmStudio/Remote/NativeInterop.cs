// NativeInterop.cs - kernel32 P/Invoke（仅 WinXP/Vista 级 API，兼容 Win7+）
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AsmStudio.Remote
{
    internal static class NativeInterop
    {
        public const uint PROCESS_TERMINATE = 0x0001;
        public const uint PROCESS_CREATE_THREAD = 0x0002;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;   // Vista+：受限也能查基本信息的只读权限
        public const uint PROCESS_ALL_NEEDED =
            PROCESS_TERMINATE | PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION |
            PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION;

        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;

        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;

        public const uint INFINITE = 0xFFFFFFFF;

        /* WaitForSingleObject 返回值。旧代码只判 wait != 0，于是 WAIT_FAILED 也走进
         * "超时、线程仍在跑"分支：既给出错误诊断，又按超时策略泄漏页。 */
        public const uint WAIT_OBJECT_0 = 0x00000000;
        public const uint WAIT_TIMEOUT = 0x00000102;

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, UIntPtr size,
            uint allocType, uint protect);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, UIntPtr size, uint freeType);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr proc, IntPtr baseAddr,
            byte[] buffer, UIntPtr size, out UIntPtr written);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr proc, IntPtr baseAddr,
            byte[] buffer, UIntPtr size, out UIntPtr read);

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr proc, IntPtr attr, UIntPtr stackSize,
            IntPtr startAddress, IntPtr parameter, uint flags, out IntPtr threadId);

        [DllImport("kernel32", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool GetExitCodeThread(IntPtr h, out uint exitCode);

        // ---- 进程创建时间（唯一标识一个进程实例；pid 会被系统复用）----
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr process, out FILETIME creation,
            out FILETIME exit, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr h, out bool isWow64);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        public static extern IntPtr GetModuleHandle(string name);

        // ---- Toolhelp32 进程快照 ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W entry);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W entry);

        public const uint TH32CS_SNAPPROCESS = 0x02;
        public const uint TH32CS_SNAPMODULE = 0x08;
        public const uint TH32CS_SNAPMODULE32 = 0x10;
        public const uint INVALID_HANDLE_VALUE = 0xFFFFFFFF;

        // ---- Toolhelp32 模块快照（解析目标进程内 kernel32 基址用）----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Module32FirstW(IntPtr snap, ref MODULEENTRY32W entry);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Module32NextW(IntPtr snap, ref MODULEENTRY32W entry);

        /// <summary>目标进程内某模块基址（跨 ASLR：按目标自身快照解析）；失败返回 IntPtr.Zero</summary>
        public static IntPtr GetRemoteModuleBase(int pid, string moduleName)
        {
            IntPtr snap = CreateToolhelp32Snapshot(
                TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
            if (snap == IntPtr.Zero || snap.ToInt64() == -1)
                return IntPtr.Zero;
            try
            {
                MODULEENTRY32W e = new MODULEENTRY32W();
                e.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                if (Module32FirstW(snap, ref e))
                {
                    do
                    {
                        if (string.Equals(e.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                            return e.modBaseAddr;
                    }
                    while (Module32NextW(snap, ref e));
                }
            }
            finally
            {
                CloseHandle(snap);
            }
            return IntPtr.Zero;
        }

        // ================= 窗口拾取（拖拽取进程用） =================

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        public const uint GA_ROOT = 2;              // GetAncestor: 取顶层（桌面子窗口 → 真窗口）

        [DllImport("user32", SetLastError = true)]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int maxCount);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int maxCount);

        /* 抓鼠标：捕获期间所有鼠标消息都路由到捕获窗口，因此光标也由它决定——
         * 这正是拖拽时能显示自定义准星（而不是被目标窗口覆盖成箭头）的原因。 */
        [DllImport("user32", SetLastError = true)]
        public static extern IntPtr SetCapture(IntPtr hwnd);

        [DllImport("user32", SetLastError = true)]
        public static extern bool ReleaseCapture();

        [DllImport("user32")]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadCursorFromFileW(string fileName);

        /* ---- 光标：内存自造 ----
         * LoadCursorFromFile 必须先落盘，而落盘/读盘在提权、杀软、%TEMP% 被重定向
         * 等情况下都可能失败，失败就静默回退成系统十字 → 两边看到的"图"不一样。
         * 这里改成直接拿 .cur 的 DIB 在内存里合成（CreateDIBSection + CreateIconIndirect），
         * 不碰文件系统，任何权限下产物逐字节相同。 */

        [StructLayout(LayoutKind.Sequential)]
        public struct IconInfo
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfo
        {
            public BitmapInfoHeader bmiHeader;
            public uint bmiColors0;
            public uint bmiColors1;
            public uint bmiColors2;
        }

        [DllImport("user32", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

        [DllImport("gdi32", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage,
            out IntPtr bits, IntPtr hSection, uint offset);

        [DllImport("gdi32", SetLastError = true)]
        public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] data);

        [DllImport("gdi32")]
        public static extern bool DeleteObject(IntPtr obj);
    }
}
