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

        // ================= 目标 API 远程地址解析 =================
        //
        // 不能用"本进程 API 地址 + kernel32 基址差值"推算远程地址：
        //   1. 主程序与目标架构不同（x64 主程序 → x86 目标）时两边 kernel32 是不同二进制，
        //      差值算出的地址是垃圾，远程线程一调用目标进程就崩；
        //   2. kernel32 的多数 API 是转发出口（如 ExitThread → ntdll.RtlExitUserThread），
        //      需要追踪转发才能拿到真实实现地址。

        private static readonly Dictionary<string, ulong> _apiCache =
            new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _apiCacheLock = new object();

        /// <summary>
        /// 解析目标进程内某模块导出 API 的远程地址（读目标导出表，追踪一跳转发出口）。
        /// 与主程序位数无关（x64 主程序可解析 x86 目标）。结果按 pid 缓存
        /// （kernel32 基址每次开机随机一次，缓存安全）。返回 0 = 解析失败。
        /// </summary>
        public static ulong ResolveTargetApi(int pid, bool is64, string moduleName, string apiName)
        {
            string key = pid + "|" + is64 + "|" + StripDllSuffix(moduleName) + "!" + apiName;
            lock (_apiCacheLock)
            {
                ulong cached;
                if (_apiCache.TryGetValue(key, out cached)) return cached;
            }
            ulong addr = ResolveTargetApiSlow(pid, is64, moduleName, apiName);
            if (addr != 0)
            {
                lock (_apiCacheLock) { _apiCache[key] = addr; }
            }
            return addr;
        }

        private static ulong ResolveTargetApiSlow(int pid, bool is64, string moduleName, string apiName)
        {
            List<ModuleInfo> modules = EnumModules(pid);
            ModuleInfo mod = FindModule(modules, moduleName, is64);
            if (mod == null) return 0;
            return ResolveFromModule(pid, is64, modules, mod, apiName, 0);
        }

        /// <summary>模块地址是否与目标架构相符：x86 目标忽略 4GB 以上的高位映射
        /// （WOW64 进程里同时映射着 64 位 ntdll，被取错会解析出 64 位地址，x86 代码调用即崩）。</summary>
        private static bool IsPlausibleModule(ModuleInfo m, bool is64)
        {
            if (!is64 && m.Base > 0xFFFFFFFF) return false;
            return true;
        }

        private static ModuleInfo FindModule(List<ModuleInfo> modules, string name, bool is64)
        {
            string want = StripDllSuffix(name);
            foreach (ModuleInfo m in modules)
            {
                if (!IsPlausibleModule(m, is64)) continue;
                if (string.Equals(StripDllSuffix(m.Name), want, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        private static string StripDllSuffix(string name)
        {
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4) : name;
        }

        /// <summary>在单模块导出表中找 API；命中转发出口则到目标模块递归（最多 3 层）。</summary>
        private static ulong ResolveFromModule(int pid, bool is64, List<ModuleInfo> modules,
            ModuleInfo mod, string apiName, int depth)
        {
            if (mod == null || depth > 2) return 0;
            string exact = mod.Name.ToLowerInvariant() + "!" + apiName;   // ReadExports 的名称前缀
            List<SymbolInfo> exports = ReadExports(pid, mod, is64);
            foreach (SymbolInfo s in exports)
            {
                if (!s.Name.StartsWith(exact, StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Name.Length == exact.Length) return s.Address;      // 本模块真实导出
                int arrow = s.Name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow < 0) continue;                                  // 同前缀的更长名称，跳过
                // 转发出口: "mod!api -> TARGET.Api"
                string fwd = s.Name.Substring(arrow + 4);
                int dot = fwd.LastIndexOf('.');
                if (dot <= 0) return 0;
                ModuleInfo fwdMod = FindModule(modules, fwd.Substring(0, dot), is64);
                return ResolveFromModule(pid, is64, modules, fwdMod,
                    fwd.Substring(dot + 1), depth + 1);
            }
            return 0;
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
