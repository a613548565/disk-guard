// DiskGuard v6.0 单文件图形化安装程序与标准卸载程序（零外部依赖，支持沙箱静默自测）
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskGuardSetup
{
    static class SetupProgram
    {
        public const string AppName = "DiskGuard";
        public const string AppVersion = "9.0.4";
        public const string PayloadResource = "DiskGuard.payload.zip";
        const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskGuard";
        const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int a);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDpiAwareness(1); } catch { try { SetProcessDPIAware(); } catch { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 自动化沙箱测试模式（不触碰真实注册表与用户桌面）
            if (args.Length >= 2 && args[0] == "--sandbox-install")
            {
                try
                {
                    ExtractPayload(args[1], true);
                    File.Copy(Application.ExecutablePath, Path.Combine(args[1], "Uninstall.exe"), true);
                    return 0;
                }
                catch { return 1; }
            }
            if (args.Length >= 2 && args[0] == "--sandbox-uninstall")
            {
                try { if (Directory.Exists(args[1])) Directory.Delete(args[1], true); return 0; }
                catch { return 1; }
            }

            // 自动更新无缝静默升级模式：停旧进程 → 解压覆盖（保留 DiskGuard.ini）→ 更新卸载入口 → 重启新版
            if (args.Length >= 2 && args[0] == "--silent-upgrade")
            {
                try
                {
                    string targetDir = args[1].Trim('"');
                    Thread.Sleep(500);
                    StopRunningApp();
                    ExtractPayload(targetDir, false);
                    try
                    {
                        string uninst = Path.Combine(targetDir, "Uninstall.exe");
                        if (!string.Equals(Application.ExecutablePath, uninst, StringComparison.OrdinalIgnoreCase))
                            File.Copy(Application.ExecutablePath, uninst, true);
                        using (var k = Registry.CurrentUser.OpenSubKey(UninstallRegKey, true))
                            if (k != null) k.SetValue("DisplayVersion", AppVersion);
                    }
                    catch { }
                    string exe = Path.Combine(targetDir, "DiskGuard.exe");
                    if (File.Exists(exe))
                    {
                        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = targetDir, UseShellExecute = true });
                        Thread.Sleep(1200);
                        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = targetDir, UseShellExecute = true });
                    }
                    return 0;
                }
                catch { return 1; }
            }

            if (args.Length > 0 && (args[0] == "--uninstall" || args[0] == "/u"))
            {
                Application.Run(new UninstallForm());
                return 0;
            }

            Application.Run(new InstallForm());
            return 0;
        }

        public static string DefaultInstallDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        public static void ApplyDwm(IntPtr hwnd, bool dark)
        {
            try
            {
                int d = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref d, sizeof(int));
                int corner = 2;
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
            }
            catch { }
        }

        public static void StopRunningApp()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("DiskGuard"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
            Thread.Sleep(300);
        }

        public static void ExtractPayload(string targetDir, bool overwriteAll)
        {
            Directory.CreateDirectory(targetDir);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (stream == null) throw new Exception("安装包内嵌资源缺失：" + PayloadResource);
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string dest = Path.Combine(targetDir, entry.FullName.Replace('/', '\\'));
                        string dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        // 覆盖升级时保留用户已有的 DiskGuard.ini 配置
                        if (!overwriteAll && string.Equals(entry.Name, "DiskGuard.ini", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                            continue;
                        entry.ExtractToFile(dest, true);
                    }
                }
            }
        }

        public static void CreateShortcut(string lnkPath, string targetExe, string args, string desc)
        {
            try
            {
                string dir = Path.GetDirectoryName(lnkPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = targetExe;
                if (!string.IsNullOrEmpty(args)) sc.Arguments = args;
                sc.WorkingDirectory = Path.GetDirectoryName(targetExe);
                sc.IconLocation = targetExe + ",0";
                sc.Description = desc;
                sc.Save();
            }
            catch { }
        }

        public static void RegisterApp(string installDir, bool desktopLnk, bool startMenuLnk, bool autoStart)
        {
            string exe = Path.Combine(installDir, "DiskGuard.exe");
            string uninst = Path.Combine(installDir, "Uninstall.exe");
            try
            {
                if (!string.Equals(Application.ExecutablePath, uninst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, uninst, true);
            }
            catch { }

            if (desktopLnk)
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desk, "DiskGuard.lnk"), exe, "", "DiskGuard 磁盘与系统健康守护管家");
            }
            if (startMenuLnk)
            {
                string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                CreateShortcut(Path.Combine(sm, "DiskGuard.lnk"), exe, "", "启动 DiskGuard");
                CreateShortcut(Path.Combine(sm, "卸载 DiskGuard.lnk"), uninst, "--uninstall", "卸载 DiskGuard");
            }

            if (autoStart)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunRegKey))
                    k.SetValue(AppName, "\"" + exe + "\"");
            }

            long sizeKB = 1500;
            try
            {
                long b = 0;
                foreach (var f in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    b += new FileInfo(f).Length;
                sizeKB = Math.Max(100, b / 1024);
            }
            catch { }

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallRegKey))
            {
                k.SetValue("DisplayName", "DiskGuard 磁盘与系统守护管家");
                k.SetValue("DisplayVersion", AppVersion);
                k.SetValue("Publisher", "DiskGuard");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", installDir);
                k.SetValue("UninstallString", "\"" + uninst + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" --uninstall");
                k.SetValue("EstimatedSize", (int)sizeKB, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void UnregisterAndClean(string installDir, bool removeData)
        {
            StopRunningApp();
            try
            {
                string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DiskGuard.lnk");
                if (File.Exists(desk)) File.Delete(desk);
            }
            catch { }
            try
            {
                string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                if (Directory.Exists(sm)) Directory.Delete(sm, true);
            }
            catch { }
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunRegKey, true)) if (k != null) k.DeleteValue(AppName, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKey, false); } catch { }

            if (removeData)
            {
                try
                {
                    string wv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskGuard");
                    if (Directory.Exists(wv)) Directory.Delete(wv, true);
                }
                catch { }
            }

            // 删除除自身外的所有文件，再通过延时 cmd 自删目录
            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                string self = Application.ExecutablePath;
                foreach (var f in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!removeData && (f.EndsWith("DiskGuard.ini", StringComparison.OrdinalIgnoreCase) || f.IndexOf("\\logs\\", StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                    try { File.Delete(f); } catch { }
                }
                if (removeData)
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul & rd /s /q \"" + installDir + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    try { Process.Start(psi); } catch { }
                }
                else
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul & del /f /q \"" + self + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    try { Process.Start(psi); } catch { }
                }
            }
        }
    }

    class InstallForm : Form
    {
        readonly TextBox txtDir;
        readonly CheckBox chkDesktop, chkStartMenu, chkAutoStart, chkLaunch;
        readonly Button btnBrowse, btnInstall, btnClose;
        readonly ProgressBar prog;
        readonly Label lblStatus;
        bool installed;

        public InstallForm()
        {
            Text = "安装 DiskGuard " + SetupProgram.AppVersion;
            Font = new Font("Microsoft YaHei UI", 9.25F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(520, 392);
            BackColor = Color.FromArgb(248, 250, 252);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 顶部品牌横幅
            var banner = new Panel { Dock = DockStyle.Top, Height = 92 };
            banner.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lg = new LinearGradientBrush(banner.ClientRectangle, Color.FromArgb(14, 165, 233), Color.FromArgb(2, 105, 161), 25f))
                    g.FillRectangle(lg, banner.ClientRectangle);
                using (var f1 = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold))
                using (var f2 = new Font("Microsoft YaHei UI", 9.25F))
                {
                    TextRenderer.DrawText(g, "DiskGuard " + SetupProgram.AppVersion, f1, new Point(24, 18), Color.White);
                    TextRenderer.DrawText(g, "磁盘清理 · 内存/CPU 卡顿急救 · Docker 专治 · 系统修复", f2, new Point(26, 54), Color.FromArgb(224, 242, 254));
                }
            };

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 14) };

            var lblDir = new Label { Text = "安装位置（免管理员权限，升级时自动保留原有配置）：", Location = new Point(24, 16), AutoSize = true, ForeColor = Color.FromArgb(51, 65, 85) };
            txtDir = new TextBox { Text = SetupProgram.DefaultInstallDir(), Location = new Point(24, 40), Width = 382, Font = new Font("Consolas", 9.5F) };
            btnBrowse = new Button { Text = "浏览…", Location = new Point(414, 38), Width = 82, Height = 27, FlatStyle = FlatStyle.System };
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "选择 DiskGuard 安装目录", SelectedPath = txtDir.Text })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        string p = dlg.SelectedPath;
                        if (!p.EndsWith("DiskGuard", StringComparison.OrdinalIgnoreCase)) p = Path.Combine(p, "DiskGuard");
                        txtDir.Text = p;
                    }
                }
            };

            chkDesktop = new CheckBox { Text = "创建桌面快捷方式", Checked = true, Location = new Point(24, 84), AutoSize = true };
            chkStartMenu = new CheckBox { Text = "创建开始菜单快捷方式与卸载入口", Checked = true, Location = new Point(24, 112), AutoSize = true };
            chkAutoStart = new CheckBox { Text = "登录 Windows 时开机自动启动（后台静默守护）", Checked = true, Location = new Point(24, 140), AutoSize = true };
            chkLaunch = new CheckBox { Text = "安装完成后立即启动 DiskGuard", Checked = true, Location = new Point(24, 168), AutoSize = true };

            prog = new ProgressBar { Location = new Point(24, 208), Width = 472, Height = 10, Visible = false };
            lblStatus = new Label { Text = "点击“立即安装”只需 2 秒即可完成部署（约 1.1 MB）", Location = new Point(24, 224), Width = 472, Height = 22, ForeColor = Color.FromArgb(71, 85, 105) };

            btnInstall = new Button
            {
                Text = "立即安装",
                Location = new Point(292, 254),
                Width = 112,
                Height = 34,
                BackColor = Color.FromArgb(2, 132, 199),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += (s, e) => DoInstall();

            btnClose = new Button { Text = "取消", Location = new Point(414, 254), Width = 82, Height = 34, FlatStyle = FlatStyle.System };
            btnClose.Click += (s, e) => Close();

            body.Controls.AddRange(new Control[] { lblDir, txtDir, btnBrowse, chkDesktop, chkStartMenu, chkAutoStart, chkLaunch, prog, lblStatus, btnInstall, btnClose });
            Controls.Add(body);
            Controls.Add(banner);

            Shown += (s, e) => SetupProgram.ApplyDwm(Handle, false);
        }

        void DoInstall()
        {
            if (installed) { Close(); return; }
            string dir = (txtDir.Text ?? "").Trim();
            if (string.IsNullOrEmpty(dir)) { MessageBox.Show(this, "请指定有效的安装目录。", "提示"); return; }

            btnInstall.Enabled = btnBrowse.Enabled = txtDir.Enabled = false;
            prog.Visible = true;
            prog.Value = 15;
            lblStatus.Text = "正在停止旧版进程并释放程序文件…";

            bool desk = chkDesktop.Checked, sm = chkStartMenu.Checked, auto = chkAutoStart.Checked, launch = chkLaunch.Checked;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    SetupProgram.StopRunningApp();
                    BeginInvoke(new Action(() => { prog.Value = 45; lblStatus.Text = "正在解压程序与 WebView2 运行组件…"; }));
                    SetupProgram.ExtractPayload(dir, false);
                    BeginInvoke(new Action(() => { prog.Value = 80; lblStatus.Text = "正在创建快捷方式并注册到系统应用列表…"; }));
                    SetupProgram.RegisterApp(dir, desk, sm, auto);
                    if (launch)
                    {
                        string exe = Path.Combine(dir, "DiskGuard.exe");
                        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
                        Thread.Sleep(1200);
                        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
                    }
                    BeginInvoke(new Action(() =>
                    {
                        installed = true;
                        prog.Value = 100;
                        lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                        lblStatus.Text = "✓ 安装成功！已注册到 Windows「已安装的应用」";
                        btnInstall.Text = "完成";
                        btnInstall.Enabled = true;
                        btnClose.Text = "关闭";
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        btnInstall.Enabled = btnBrowse.Enabled = txtDir.Enabled = true;
                        prog.Visible = false;
                        lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                        lblStatus.Text = "安装失败：" + ex.Message;
                        MessageBox.Show(this, "安装失败：\n" + ex.Message, "DiskGuard 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }
    }

    class UninstallForm : Form
    {
        public UninstallForm()
        {
            Text = "卸载 DiskGuard";
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(420, 195);
            BackColor = Color.FromArgb(248, 250, 252);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            string installDir = Path.GetDirectoryName(Application.ExecutablePath);
            var lbl = new Label
            {
                Text = "确定要卸载 DiskGuard 吗？\n安装目录：" + installDir,
                Location = new Point(22, 22),
                Width = 376,
                Height = 52
            };
            var chkData = new CheckBox
            {
                Text = "同时删除配置文件、清理日志与 WebView2 缓存",
                Checked = false,
                Location = new Point(22, 84),
                AutoSize = true
            };
            var btnUn = new Button
            {
                Text = "确认卸载",
                Location = new Point(208, 134),
                Width = 96,
                Height = 34,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnUn.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button { Text = "取消", Location = new Point(314, 134), Width = 84, Height = 34, FlatStyle = FlatStyle.System };
            btnCancel.Click += (s, e) => Close();
            btnUn.Click += (s, e) =>
            {
                btnUn.Enabled = false;
                SetupProgram.UnregisterAndClean(installDir, chkData.Checked);
                MessageBox.Show(this, "DiskGuard 已完成卸载。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            };

            Controls.AddRange(new Control[] { lbl, chkData, btnUn, btnCancel });
            Shown += (s, e) => SetupProgram.ApplyDwm(Handle, false);
        }
    }
}
