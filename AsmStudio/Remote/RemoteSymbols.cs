// RemoteSymbols.cs - 无管道符号源：远程模块枚举 + PE 导出表解析
// 用途：RemoteThreadVeh / RemoteThread 通道（目标无 AsmLibrary）下，
//       从目标进程内存直接解析各模块的导出表（EAT），获得 API 名称 → 远程地址，
//       供"调用目标/参数/符号表"功能使用，全程 OpenProcess + ReadProcessMemory，
//       不注入任何内容。
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    internal static class RemoteSymbols
    {
        public sealed class ModuleInfo
        {
            public string Name;
            public ulong Base;
            public uint Size;

            public override string ToString()
            {
                return Name + "  (0x" + Base.ToString("X") + ", " + Size + " 字节)";
            }
        }

        /// <summary>枚举目标进程全部模块（Toolhelp32 模块快照）</summary>
        public static List<ModuleInfo> EnumModules(int pid)
        {
            List<ModuleInfo> result = new List<ModuleInfo>();
            IntPtr snap = NativeInterop.CreateToolhelp32Snapshot(
                NativeInterop.TH32CS_SNAPMODULE | NativeInterop.TH32CS_SNAPMODULE32, (uint)pid);
            if (snap == IntPtr.Zero || snap.ToInt64() == -1)
                return result;
            try
            {
                NativeInterop.MODULEENTRY32W e = new NativeInterop.MODULEENTRY32W();
                e.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(NativeInterop.MODULEENTRY32W));
                if (NativeInterop.Module32FirstW(snap, ref e))
                {
                    do
                    {
                        ModuleInfo m = new ModuleInfo();
                        m.Name = e.szModule;
                        m.Base = (ulong)e.modBaseAddr.ToInt64();
                        m.Size = e.modBaseSize;
                        result.Add(m);
                    }
                    while (NativeInterop.Module32NextW(snap, ref e));
                }
            }
            finally
            {
                NativeInterop.CloseHandle(snap);
            }
            return result;
        }

        private const int MaxExportNames = 30000;   // 防御性上限
        private const int MaxNameLen = 512;

        /// <summary>解析模块 PE 导出表（读目标内存，不注入）。
        /// 返回符号：Name = "模块名!API"，Address = VA（转发出口为 0 且 Name 带转发目标）。</summary>
        public static List<SymbolInfo> ReadExports(int pid, ModuleInfo module, bool is64)
        {
            List<SymbolInfo> result = new List<SymbolInfo>();
            if (module.Base == 0) return result;

            IntPtr proc = NativeInterop.OpenProcess(NativeInterop.PROCESS_ALL_NEEDED, false, pid);
            if (proc == IntPtr.Zero) return result;
            try
            {
                // DOS 头 → e_lfanew
                byte[] dos = ReadBytes(proc, module.Base, 0x40);
                if (dos == null) return result;
                if (dos[0] != 'M' || dos[1] != 'Z') return result;
                uint peOff = BitConverter.ToUInt32(dos, 0x3C);
                if (peOff == 0 || peOff > 0x400) return result;

                // NT 头：签名 + 文件头 + OptionalHeader
                byte[] nt = ReadBytes(proc, module.Base + peOff, 0x200);
                if (nt == null) return result;
                if (nt[0] != 'P' || nt[1] != 'E' || nt[2] != 0 || nt[3] != 0) return result;
                ushort magic = BitConverter.ToUInt16(nt, 24);
                int ddOffset;   // DataDirectory 在 NT 头内的偏移
                if (magic == 0x20B) ddOffset = 112;       // PE32+（x64）
                else if (magic == 0x10B) ddOffset = 96;   // PE32（x86）
                else return result;

                uint expRva = BitConverter.ToUInt32(nt, 24 + ddOffset);
                uint expSize = BitConverter.ToUInt32(nt, 24 + ddOffset + 4);
                if (expRva == 0 || expSize == 0) return result;   // 无导出表

                // 导出目录
                ulong dir = module.Base + expRva;
                byte[] ed = ReadBytes(proc, dir, 40);
                if (ed == null) return result;
                uint numberOfFunctions = BitConverter.ToUInt32(ed, 0x14);
                uint numberOfNames = BitConverter.ToUInt32(ed, 0x18);
                uint addrFunctions = BitConverter.ToUInt32(ed, 0x1C);
                uint addrNames = BitConverter.ToUInt32(ed, 0x20);
                uint addrOrdinals = BitConverter.ToUInt32(ed, 0x24);
                if (numberOfNames == 0 || numberOfNames > MaxExportNames) return result;

                // 名称/序号/函数地址三表整块读入
                byte[] nameTable = ReadBytes(proc, module.Base + addrNames,
                    numberOfNames * 4);
                byte[] ordTable = ReadBytes(proc, module.Base + addrOrdinals,
                    numberOfNames * 2);
                byte[] funcTable = ReadBytes(proc, module.Base + addrFunctions,
                    numberOfFunctions * 4);
                if (nameTable == null || ordTable == null || funcTable == null) return result;

                string modName = module.Name.ToLowerInvariant();
                for (uint i = 0; i < numberOfNames; i++)
                {
                    uint nameRva = BitConverter.ToUInt32(nameTable, (int)(i * 4));
                    string api = ReadCString(proc, module.Base + nameRva, MaxNameLen);
                    if (api == null || api.Length == 0) continue;

                    uint ord = BitConverter.ToUInt16(ordTable, (int)(i * 2));
                    if (ord >= numberOfFunctions) continue;
                    uint funcRva = BitConverter.ToUInt32(funcTable, (int)(ord * 4));

                    SymbolInfo sym = new SymbolInfo();
                    sym.Name = modName + "!" + api;
                    if (funcRva >= expRva && funcRva < expRva + expSize)
                    {
                        // 转发出口（forwarder）：RVA 指向导出目录内的 "DLL.API" 字符串
                        string fwd = ReadCString(proc, module.Base + funcRva, MaxNameLen);
                        sym.Name = modName + "!" + api + " -> " + fwd;
                        sym.Address = 0;   // 转发不可直接调用
                    }
                    else
                    {
                        sym.Address = module.Base + funcRva;
                    }
                    result.Add(sym);
                }
            }
            finally
            {
                NativeInterop.CloseHandle(proc);
            }
            return result;
        }

        private static byte[] ReadBytes(IntPtr proc, ulong addr, uint size)
        {
            byte[] buf = new byte[size];
            UIntPtr read;
            if (!NativeInterop.ReadProcessMemory(proc, new IntPtr(unchecked((long)addr)),
                    buf, (UIntPtr)size, out read) || read.ToUInt64() < size)
                return null;
            return buf;
        }

        private static string ReadCString(IntPtr proc, ulong addr, uint maxLen)
        {
            byte[] buf = ReadBytes(proc, addr, maxLen);
            if (buf == null) return null;
            int end = Array.IndexOf(buf, (byte)0);
            if (end < 0) end = buf.Length;
            if (end == 0) return null;
            return System.Text.Encoding.ASCII.GetString(buf, 0, end);
        }
    }
}
