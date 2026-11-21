// Program.cs - AsmStudio 入口
// Debug 构建为控制台程序（可查看日志），Release 为 WinExe（纯窗体，csproj 条件 OutputType）
using System;
using System.Windows.Forms;

namespace AsmStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
