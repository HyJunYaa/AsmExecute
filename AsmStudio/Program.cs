// Program.cs - AsmStudio 入口（纯窗体，日志在主窗口"执行日志"页）
// 全局异常钩子：未处理异常落 %TEMP%\AsmStudio-crash.log（此前扫描闪退无声无息无法定位）
using System;
using System.IO;
using System.Windows.Forms;

namespace AsmStudio
{
    internal static class Program
    {
        private static readonly string CrashLog =
            Path.Combine(Path.GetTempPath(), "AsmStudio-crash.log");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            WriteCrash(e.Exception);
            MessageBox.Show(e.Exception.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) WriteCrash(ex);
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                File.AppendAllText(CrashLog,
                    "==== " + DateTime.Now + " ====\r\n" + ex + "\r\n\r\n");
            }
            catch { }
        }
    }
}
