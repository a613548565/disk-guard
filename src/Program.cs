// DiskGuard v3 - 程序入口（高 DPI 适配、单实例唤起、自检与截图验收模式）
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DiskGuard
{
    static class Program
    {
        public const string ShowEventName = @"Local\DiskGuardShow";

        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwareness(1); return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--selftest")
                return SelfTest.Run(args.Length > 1 ? args[1] : null);

            bool created;
            using (var m = new Mutex(true, @"Local\DiskGuardTray", out created))
            {
                if (!created)
                {
                    try { AllowSetForegroundWindow(-1); EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                    catch { MessageBox.Show("DiskGuard 已在运行，请查看任务栏右下角托盘图标。", "DiskGuard"); }
                    return 0;
                }
                Application.Run(new TrayApp());
            }
            return 0;
        }
    }
}
