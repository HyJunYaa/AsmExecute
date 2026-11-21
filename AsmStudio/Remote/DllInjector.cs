// DllInjector.cs - 可选：把 AsmLibrary.dll 注入目标进程（LoadLibraryW 远程线程）
// 限同位数注入（跨位数时 32 位 kernel32 基址需解析目标 PEB，此处不实现）。
// 目标程序本身已自加载 AsmLibrary，本注入器仅作为"目标未自加载"的补救手段。
// 仅用于自有/已授权进程。
using System;
using System.Text;

namespace AsmStudio.Remote
{
    internal static class DllInjector
    {
        /// <summary>注入 DLL 到目标进程。返回错误消息（null=成功）。</summary>
        public static string Inject(ProcessInfo target, string dllPath)
        {
            bool target64 = target.Is64Bit;
            bool self64 = IntPtr.Size == 8;
            if (target64 != self64)
                return "跨位数注入不支持（主程序 " + (self64 ? "x64" : "x86") +
                       " → 目标 " + (target64 ? "x64" : "x86") +
                       "）。请以目标位数启动主程序，或让目标自加载 AsmLibrary。";

            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, target.Pid);
            if (proc == IntPtr.Zero)
                return "OpenProcess 失败";

            try
            {
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                IntPtr remote = NativeInterop.VirtualAllocEx(proc, IntPtr.Zero,
                    (UIntPtr)pathBytes.Length,
                    NativeInterop.MEM_COMMIT | NativeInterop.MEM_RESERVE,
                    NativeInterop.PAGE_READWRITE);
                if (remote == IntPtr.Zero)
                    return "VirtualAllocEx 失败";

                UIntPtr written;
                if (!NativeInterop.WriteProcessMemory(proc, remote, pathBytes,
                    (UIntPtr)pathBytes.Length, out written))
                    return "WriteProcessMemory 失败";

                IntPtr loadLibraryW = NativeInterop.GetProcAddress(
                    NativeInterop.GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (loadLibraryW == IntPtr.Zero)
                    return "GetProcAddress(LoadLibraryW) 失败";

                IntPtr tid;
                IntPtr thread = NativeInterop.CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero,
                    loadLibraryW, remote, 0, out tid);
                if (thread == IntPtr.Zero)
                    return "CreateRemoteThread 失败";

                NativeInterop.WaitForSingleObject(thread, 5000);
                NativeInterop.CloseHandle(thread);
                NativeInterop.VirtualFreeEx(proc, remote, UIntPtr.Zero, NativeInterop.MEM_RELEASE);
                return null;
            }
            finally
            {
                NativeInterop.CloseHandle(proc);
            }
        }
    }
}
