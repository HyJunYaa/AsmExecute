// TargetSharp - C# 目标对象（控制台迷你游戏）
//
// 演示如何让一个托管程序公开"可被远程汇编调用的函数地址":
//   - 托管静态方法 → Marshal.GetFunctionPointerForDelegate 得到原生可调用指针
//     (GCHandle 保活防 GC 回收，UnmanagedFunctionPointer 指定 cdecl)
//   - 数据槽 → Pinned 数组提供稳定地址
//   - 加载 AsmLibrary.dll（运行目录，x86/x64 同名分目录）→ 注册符号 + 启动管道服务
//
// /nodll 参数: 不加载 AsmLibrary（测试 RemoteThread 兜底模式）
using System;
using System.Runtime.InteropServices;

namespace TargetSharp
{
    internal static class Program
    {
        // ---- 游戏数据槽（Pinned 数组 → 地址稳定） ----
        private static int[] _score = new int[1];
        private static int[] _hp = new int[1];
        private static int[] _pos = new int[3];
        private static GCHandle _pinScore, _pinHp, _pinPos;

        // ---- 委托与原生指针（保活防 GC；UnmanagedFunctionPointer 指定 cdecl） ----
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelAddScore(int points);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelGetScore();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelHeal(int amount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelGetHealth();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelDamage(int amount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelGetPos(IntPtr out3);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelSlowCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DelCrashNow();

        private static DelAddScore _dAddScore;
        private static DelGetScore _dGetScore;
        private static DelHeal _dHeal;
        private static DelGetHealth _dGetHealth;
        private static DelDamage _dDamage;
        private static DelGetPos _dGetPos;
        private static DelSlowCall _dSlowCall;
        private static DelCrashNow _dCrashNow;

        private static IntPtr _fpAddScore, _fpGetScore, _fpHeal, _fpGetHealth,
                              _fpDamage, _fpGetPos, _fpSlowCall, _fpCrashNow;

        // ---- AsmLibrary P/Invoke ----
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr h, string name);

        private delegate int DllVersion();
        private delegate int DllInstallVeh();
        private delegate int DllRegSym([MarshalAs(UnmanagedType.LPStr)] string name, IntPtr addr);
        private delegate int DllPipeStart([MarshalAs(UnmanagedType.LPStr)] string name);

        private static DllVersion _asmVersion;
        private static DllInstallVeh _asmInstall;
        private static DllRegSym _asmReg;
        private static DllPipeStart _asmPipeStart;
        private static bool _asmOk;

        // ---- 游戏逻辑（将被远程调用） ----
        private static int GameAddScore(int points)
        {
            _score[0] += points;
            if (_score[0] < 0) _score[0] = 0;
            return _score[0];
        }
        private static int GameGetScore() { return _score[0]; }
        private static int GameHeal(int amount)
        {
            _hp[0] += amount;
            if (_hp[0] > 100) _hp[0] = 100;
            return _hp[0];
        }
        private static int GameGetHealth() { return _hp[0]; }
        private static int GameDamage(int amount)
        {
            _hp[0] -= amount;
            if (_hp[0] < 0) _hp[0] = 0;
            return _hp[0];
        }
        private static unsafe int GameGetPos(void* out3)
        {
            if (out3 == null) return -1;
            int* p = (int*)out3;
            p[0] = _pos[0]; p[1] = _pos[1]; p[2] = _pos[2];
            return 0;
        }
        private static int GameSlowCall()
        {
            // 约 2 秒 busy loop —— 供补丁演示（打补丁后瞬间返回）
            int s = 0;
            for (int i = 0; i < 150000000; i++) s += i;
            return s;
        }
        private static unsafe int GameCrashNow()
        {
            // 空指针解引用 → AsmLibrary VEH 捕获，目标不崩溃
            int* p = null;
            return *p;
        }

        private static void PrintSymbols()
        {
            Console.WriteLine("---- 符号表 (共 12 项) ----");
            PrintSym("GameAddScore", _fpAddScore);
            PrintSym("GameGetScore", _fpGetScore);
            PrintSym("GameHeal", _fpHeal);
            PrintSym("GameGetHealth", _fpGetHealth);
            PrintSym("GameDamage", _fpDamage);
            PrintSym("GameGetPos", _fpGetPos);
            PrintSym("GameSlowCall", _fpSlowCall);
            PrintSym("GameCrashNow", _fpCrashNow);
            PrintSym("VirtualProtect", GetProcAddress(LoadLibrary("kernel32.dll"), "VirtualProtect"));
            PrintSym("g_score", Marshal.UnsafeAddrOfPinnedArrayElement(_score, 0));
            PrintSym("g_hp", Marshal.UnsafeAddrOfPinnedArrayElement(_hp, 0));
            PrintSym("g_pos", Marshal.UnsafeAddrOfPinnedArrayElement(_pos, 0));
        }

        private static void PrintSym(string name, IntPtr addr)
        {
            Console.WriteLine("  {0,-14} = 0x{1}", name, addr.ToString("X"));
            if (_asmOk)
                _asmReg(name, addr);
        }

        private static bool LoadAsmLibrary()
        {
            // x86/x64 同名分目录: 运行目录下的 AsmLibrary.dll 即本进程架构版本
            IntPtr h = LoadLibrary("AsmLibrary.dll");
            if (h == IntPtr.Zero)
            {
                Console.WriteLine("[!] 加载 AsmLibrary.dll 失败 —— 仅支持远程线程兜底模式");
                return false;
            }
            IntPtr pV = GetProcAddress(h, "asmhost_version");
            IntPtr pI = GetProcAddress(h, "asmhost_install_veh");
            IntPtr pR = GetProcAddress(h, "asmhost_register_symbol");
            IntPtr pP = GetProcAddress(h, "asmhost_start_pipe_server");
            if (pV == IntPtr.Zero || pI == IntPtr.Zero || pR == IntPtr.Zero || pP == IntPtr.Zero)
            {
                Console.WriteLine("[!] AsmLibrary 导出不完整");
                return false;
            }
            _asmVersion = (DllVersion)Marshal.GetDelegateForFunctionPointer(pV, typeof(DllVersion));
            _asmInstall = (DllInstallVeh)Marshal.GetDelegateForFunctionPointer(pI, typeof(DllInstallVeh));
            _asmReg = (DllRegSym)Marshal.GetDelegateForFunctionPointer(pR, typeof(DllRegSym));
            _asmPipeStart = (DllPipeStart)Marshal.GetDelegateForFunctionPointer(pP, typeof(DllPipeStart));
            _asmInstall();
            _asmOk = true;
            Console.WriteLine("[+] AsmLibrary 已加载: version={0:x} arch={1} 位",
                _asmVersion(), IntPtr.Size * 8);
            return true;
        }

        private static int Main(string[] args)
        {
            bool nodll = false;
            foreach (string a in args)
                if (string.Equals(a, "/nodll", StringComparison.OrdinalIgnoreCase))
                    nodll = true;

            Console.WriteLine("=== TargetSharp 迷你游戏目标 (arch={0} 位) ===",
                IntPtr.Size * 8);

            // 固定数据槽
            _pinScore = GCHandle.Alloc(_score, GCHandleType.Pinned);
            _pinHp = GCHandle.Alloc(_hp, GCHandleType.Pinned);
            _pinPos = GCHandle.Alloc(_pos, GCHandleType.Pinned);
            _score[0] = 0; _hp[0] = 100; _pos[0] = 10; _pos[1] = 20; _pos[2] = 30;

            // 生成原生可调用指针（静态字段保活防 GC）
            _dAddScore = new DelAddScore(GameAddScore);
            _dGetScore = new DelGetScore(GameGetScore);
            _dHeal = new DelHeal(GameHeal);
            _dGetHealth = new DelGetHealth(GameGetHealth);
            _dDamage = new DelDamage(GameDamage);
            _dGetPos = new DelGetPos(GameGetPosNative);
            _dSlowCall = new DelSlowCall(GameSlowCall);
            _dCrashNow = new DelCrashNow(GameCrashNow);
            _fpAddScore = Marshal.GetFunctionPointerForDelegate(_dAddScore);
            _fpGetScore = Marshal.GetFunctionPointerForDelegate(_dGetScore);
            _fpHeal = Marshal.GetFunctionPointerForDelegate(_dHeal);
            _fpGetHealth = Marshal.GetFunctionPointerForDelegate(_dGetHealth);
            _fpDamage = Marshal.GetFunctionPointerForDelegate(_dDamage);
            _fpGetPos = Marshal.GetFunctionPointerForDelegate(_dGetPos);
            _fpSlowCall = Marshal.GetFunctionPointerForDelegate(_dSlowCall);
            _fpCrashNow = Marshal.GetFunctionPointerForDelegate(_dCrashNow);

            if (!nodll && LoadAsmLibrary())
            {
                PrintSymbols();
                if (_asmPipeStart(null) == 1)
                    Console.WriteLine("[+] 管道服务已启动: \\\\.\\pipe\\asmexec_{0}",
                        System.Diagnostics.Process.GetCurrentProcess().Id);
            }
            else
            {
                PrintSymbols();
                Console.WriteLine("[i] 未加载 AsmLibrary —— 主程序可用 RemoteThread 兜底模式");
            }

            Console.WriteLine("[i] 命令: list=重印符号  hp=血量  score=得分  exit=退出");
            Console.WriteLine("[i] 游戏循环中，血量每秒 -1（可被远程 GameHeal 恢复）...\n");

            DateTime lastTick = DateTime.UtcNow;
            int cnt = 0;
            while (true)
            {
                string line = null;
                bool hasKey = false;
                try { hasKey = Console.KeyAvailable; }
                catch (InvalidOperationException) { hasKey = false; }   // stdin 重定向 → 非交互模式
                if (hasKey)
                {
                    line = Console.ReadLine();
                    if (line == null) line = string.Empty;
                    line = line.Trim();
                    if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)) break;
                    if (string.Equals(line, "list", StringComparison.OrdinalIgnoreCase))
                        PrintSymbols();
                    else if (string.Equals(line, "hp", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine("g_hp = {0} @ 0x{1}", _hp[0],
                            Marshal.UnsafeAddrOfPinnedArrayElement(_hp, 0).ToString("X"));
                    else if (string.Equals(line, "score", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine("g_score = {0} @ 0x{1}", _score[0],
                            Marshal.UnsafeAddrOfPinnedArrayElement(_score, 0).ToString("X"));
                    else if (line.Length > 0)
                        Console.WriteLine("[?] 未知命令: {0}", line);
                    Console.Write("> ");
                }

                if ((DateTime.UtcNow - lastTick).TotalMilliseconds >= 1000)
                {
                    lastTick = DateTime.UtcNow;
                    if (_hp[0] > 0) _hp[0]--;
                    if (++cnt % 3 == 0) _score[0]++;
                    Console.Write("\r[游戏] HP={0}  Score={1}  ", _hp[0], _score[0]);
                }
                System.Threading.Thread.Sleep(50);
            }
            Console.WriteLine("\n退出。");
            return 0;
        }

        // GetPos 的 IntPtr 包装（避免 delegate 与 void* 直接混用）
        private static int GameGetPosNative(IntPtr out3)
        {
            unsafe
            {
                return GameGetPos((void*)out3);
            }
        }
    }
}
