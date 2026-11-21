// ProcessScanner.cs - 进程枚举 + 架构探测
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal static class ProcessScanner
    {
        /// <summary>枚举全部进程（含架构探测）</summary>
        public static List<ProcessInfo> ScanProcesses()
        {
            List<ProcessInfo> result = new List<ProcessInfo>();
            IntPtr snap = NativeInterop.CreateToolhelp32Snapshot(
                NativeInterop.TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap.ToInt64() == -1)
                return result;

            try
            {
                NativeInterop.PROCESSENTRY32W entry = new NativeInterop.PROCESSENTRY32W();
                entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(NativeInterop.PROCESSENTRY32W));
                if (NativeInterop.Process32FirstW(snap, ref entry))
                {
                    do
                    {
                        ProcessInfo info = new ProcessInfo();
                        info.Pid = (int)entry.th32ProcessID;
                        info.Name = entry.szExeFile;
                        info.Is64Bit = DetectBitness(info.Pid);
                        result.Add(info);
                    }
                    while (NativeInterop.Process32NextW(snap, ref entry));
                }
            }
            finally
            {
                NativeInterop.CloseHandle(snap);
            }
            return result;
        }

        /// <summary>探测目标位数：x64 进程=64；WOW64/32 位进程=32；探测失败按系统位数</summary>
        public static bool DetectBitness(int pid)
        {
            bool system64 = IntPtr.Size == 8;
            IntPtr h = NativeInterop.OpenProcess(
                NativeInterop.PROCESS_QUERY_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
                return system64;
            try
            {
                if (system64)
                {
                    bool wow64;
                    if (NativeInterop.IsWow64Process(h, out wow64))
                        return !wow64;   // WOW64 = 32 位目标
                    return system64;
                }
                return false;
            }
            finally
            {
                NativeInterop.CloseHandle(h);
            }
        }
    }
}
