// GameSharp - 冒烟测试目标（模拟未知环境的托管"游戏"）
//
// 定位：给 AsmStudio 提供未知目标的冒烟/暴力测试环境：
//   - 与主程序零共享，无注入、无管道
//   - 内部方法不导出、不输出任何地址（真实程序不会把内部地址给你）
//   - 控制台只显示游戏状态本身
// 托管程序的特殊性：方法地址是 JIT 运行时产生的，每次启动都可能不同——
// 这正是"未知目标"里最恶劣的情况，适合冒烟测试。
using System;

namespace GameSharp
{
    internal static class Program
    {
        // ---- 游戏状态 ----
        private static int _score = 0;
        private static int _hp = 100;
        private static readonly int[] _pos = { 10, 20, 30 };

        // ---- 内部游戏方法（不导出；JIT 地址运行时才确定）----
        private static int GameAddScore(int points)
        {
            _score += points;
            if (_score < 0) _score = 0;
            return _score;
        }
        private static int GameGetScore() { return _score; }
        private static int GameHeal(int amount)
        {
            _hp += amount;
            if (_hp > 100) _hp = 100;
            return _hp;
        }
        private static int GameGetHealth() { return _hp; }
        private static int GameDamage(int amount)
        {
            _hp -= amount;
            if (_hp < 0) _hp = 0;
            return _hp;
        }
        private static int GameSlowCall()
        {
            // 约 2 秒 busy loop
            int s = 0;
            for (int i = 0; i < 150000000; i++) s += i;
            return s;
        }
        private static int GameCrashNow()
        {
            // 空指针读取 → 走 Marshal 原生层产生真正的硬件 AV（而非托管 NullReferenceException）
            return System.Runtime.InteropServices.Marshal.ReadInt32(IntPtr.Zero);
        }

        private static void PrintState()
        {
            Console.WriteLine("  HP={0}  Score={1}  Pos=({2},{3},{4})",
                _hp, _score, _pos[0], _pos[1], _pos[2]);
        }

        private static int Main(string[] args)
        {
            Console.WriteLine("=== GameSharp 迷你游戏目标 (arch={0} 位) ===",
                IntPtr.Size * 8);
            Console.WriteLine("[i] 这是一个模拟未知目标的冒烟测试环境：内部符号不公开，地址自行定位。");
            PrintState();

            Console.WriteLine("[i] 命令: state=状态  hp=血量  score=得分  exit=退出");
            Console.WriteLine("[i] 游戏循环中，血量每秒 -1...\n");

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
                    if (string.Equals(line, "state", StringComparison.OrdinalIgnoreCase))
                        PrintState();
                    else if (string.Equals(line, "hp", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine("HP = {0}", _hp);
                    else if (string.Equals(line, "score", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine("Score = {0}", _score);
                    else if (line.Length > 0)
                        Console.WriteLine("[?] 未知命令: {0}", line);
                    Console.Write("> ");
                }

                if ((DateTime.UtcNow - lastTick).TotalMilliseconds >= 1000)
                {
                    lastTick = DateTime.UtcNow;
                    if (_hp > 0) _hp--;
                    if (++cnt % 3 == 0) _score++;
                    Console.Write("\r[游戏] HP={0}  Score={1}  ", _hp, _score);
                }
                System.Threading.Thread.Sleep(50);
            }
            Console.WriteLine("\n退出。");
            return 0;
        }
    }
}
