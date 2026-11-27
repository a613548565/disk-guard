// DiskGuard v3 - 任务栏托盘磁盘空间监控、分析与安全清理
// 编译：build.cmd（系统自带 .NET Framework 4 csc，C# 5 语法）
// 自检：DiskGuard.exe --selftest（只扫描不清理；删除逻辑只在临时沙箱里测；结果写入 logs\selftest.txt）
// 再次运行 DiskGuard.exe：唤起已运行实例的主窗口
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskGuard
{
    static class Program
    {
        public const string ShowEventName = @"Local\DiskGuardShow";
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run();

            int wi = Array.IndexOf(args, "--wait-pid");
            if (wi >= 0 && wi + 1 < args.Length)
            {
                int pid;
                if (int.TryParse(args[wi + 1], out pid))
                {
                    try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
                }
            }

            Mutex m = null;
            bool created = false;
            try { m = new Mutex(true, @"Local\DiskGuardTray", out created); }
            catch (UnauthorizedAccessException) { created = false; }

            if (!created)
            {
                try { AllowSetForegroundWindow(-1); EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                catch { MessageBox.Show("DiskGuard 已在运行，见任务栏右下角托盘。", "DiskGuard"); }
                if (m != null) m.Dispose();
                return 0;
            }
            try
            {
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
            finally { m.Dispose(); }
            return 0;
        }
    }

    // ======================= 配置 =======================
    class Config
    {
        public string[] Drives = new[] { "C", "E" };
        public double WarnGB = 15, CritGB = 8, DropAlertGB = 5;
        public int IntervalSec = 60, InactiveDays = 30, AutoCleanDays = 0, AutoCleanHour = 3;
        public string DockerExe = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe";
        public string DockerDesktopExe = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
        public string DockerVhdx = @"E:\develop\docker\DockerDesktopWSL\disk\docker_data.vhdx";
        public string DockerDistro = "docker-desktop", DockerMount = "/mnt/docker-desktop-disk";
        public List<string> ExtraDirs = new List<string>(), ProjectRoots = new List<string>(), WatchDirs = new List<string>();

        public static string FilePath { get { return Path.Combine(Util.AppDir, "DiskGuard.ini"); } }

        const string Template =
            "# DiskGuard 配置。修改后在托盘菜单点“重新加载配置”\r\n" +
            "# 监控的盘符，逗号分隔\r\n" +
            "drives=C,E\r\n" +
            "# 可用空间低于 warnGB 显示橙色并提醒，低于 critGB 显示红色并每 2 小时提醒\r\n" +
            "warnGB=15\r\n" +
            "critGB=8\r\n" +
            "# 1 小时内可用空间减少超过 dropAlertGB 时提醒，并指出是哪里涨了\r\n" +
            "dropAlertGB=5\r\n" +
            "intervalSec=60\r\n" +
            "# 项目产物页扫描的根目录（可写多行），超过 inactiveDays 天没改动的项目默认勾选\r\n" +
            "projectRoot=E:\\code\r\n" +
            "inactiveDays=30\r\n" +
            "# 定时自动清理：每 autoCleanDays 天在 autoCleanHour 点清一次“放心清”的项，0 表示关闭\r\n" +
            "autoCleanDays=0\r\n" +
            "autoCleanHour=3\r\n" +
            "dockerExe=C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe\r\n" +
            "dockerDesktopExe=C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe\r\n" +
            "dockerVhdx=E:\\develop\\docker\\DockerDesktopWSL\\disk\\docker_data.vhdx\r\n" +
            "# 额外的可清理缓存目录（清空目录内容，目录本身保留），可写多行\r\n" +
            "# extraDir=D:\\some\\cache\r\n" +
            "# 空间暴涨时额外检查的目录，可写多行\r\n" +
            "# watchDir=E:\\data\r\n";

        public static Config Load()
        {
            var c = new Config();
            if (!File.Exists(FilePath)) { try { File.WriteAllText(FilePath, Template, new UTF8Encoding(false)); } catch { } }
            string[] lines;
            try { lines = File.ReadAllLines(FilePath, Encoding.UTF8); } catch { lines = new string[0]; }
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                double d; int n;
                switch (k)
                {
                    case "drives":
                        c.Drives = v.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim().TrimEnd(':', '\\').ToUpperInvariant()).Where(s => s.Length > 0).ToArray();
                        break;
                    case "warngb": if (D(v, out d)) c.WarnGB = d; break;
                    case "critgb": if (D(v, out d)) c.CritGB = d; break;
                    case "dropalertgb": if (D(v, out d)) c.DropAlertGB = d; break;
                    case "intervalsec": if (int.TryParse(v, out n) && n >= 10) c.IntervalSec = n; break;
                    case "inactivedays": if (int.TryParse(v, out n) && n >= 0) c.InactiveDays = n; break;
                    case "autocleandays": if (int.TryParse(v, out n) && n >= 0) c.AutoCleanDays = n; break;
                    case "autocleanhour": if (int.TryParse(v, out n) && n >= 0 && n <= 23) c.AutoCleanHour = n; break;
                    case "dockerexe": c.DockerExe = v; break;
                    case "dockerdesktopexe": c.DockerDesktopExe = v; break;
                    case "dockervhdx": c.DockerVhdx = v; break;
                    case "extradir": if (v.Length > 0) c.ExtraDirs.Add(v); break;
                    case "projectroot": if (v.Length > 0) c.ProjectRoots.Add(v); break;
                    case "watchdir": if (v.Length > 0) c.WatchDirs.Add(v); break;
                }
            }
            if (c.ProjectRoots.Count == 0 && Directory.Exists(@"E:\code")) c.ProjectRoots.Add(@"E:\code");
            return c;
        }

        static bool D(string v, out double d) { return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d); }

        public static void SetValue(string key, string value)
        {
            var lines = new List<string>();
            try { if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath, Encoding.UTF8)); } catch { }
            bool done = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                int eq = t.IndexOf('=');
                if (eq > 0 && !t.StartsWith("#") && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                { lines[i] = key + "=" + value; done = true; break; }
            }
            if (!done) lines.Add(key + "=" + value);
            File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
        }
    }

    // ======================= 工具 =======================
    static class Util
    {
        public const long GB = 1073741824L;
        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string LogDir { get { var d = Path.Combine(AppDir, "logs"); Directory.CreateDirectory(d); return d; } }
        public static string LogFile { get { return Path.Combine(LogDir, "clean.log"); } }
        public static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string UserProfile { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        public static string ProgramData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }

        public static bool IsAdmin
        {
            get { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        }

        public static Encoding Oem
        {
            get { try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.Default; } }
        }

        // -1 = 无法统计，-2 = 运行时计算
        public static string Fmt(long b)
        {
            if (b == -2) return "运行时计算";
            if (b < 0) return "—";
            if (b >= GB) return (b / (double)GB).ToString("0.0") + " G";
            if (b >= (1L << 20)) return (b / 1048576.0).ToString("0") + " M";
            return (b / 1024.0).ToString("0") + " K";
        }

        public static string FmtDelta(long b) { return (b >= 0 ? "+" : "-") + Fmt(Math.Abs(b)); }
        public static string Ts() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

        public static string Run(string exe, string args, int timeoutMs, out int exit) { return Run(exe, args, timeoutMs, out exit, Encoding.UTF8); }

        public static string Run(string exe, string args, int timeoutMs, out int exit, Encoding enc)
        {
            var sb = new StringBuilder();
            exit = -1;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = enc, StandardErrorEncoding = enc
                };
                using (var p = new Process { StartInfo = psi })
                {
                    DataReceivedEventHandler h = (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data.Replace("\0", "")); };
                    p.OutputDataReceived += h; p.ErrorDataReceived += h;
                    p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } lock (sb) sb.AppendLine("[timeout]"); return sb.ToString(); }
                    p.WaitForExit();
                    exit = p.ExitCode;
                }
            }
            catch (Exception ex) { lock (sb) sb.AppendLine("[error] " + ex.Message); }
            lock (sb) return sb.ToString();
        }

        public static long ParseDockerSize(string s)
        {
            var m = Regex.Match(s ?? "", @"([\d.]+)\s*(kB|KB|MB|GB|TB|B)", RegexOptions.IgnoreCase);
            if (!m.Success) return -1;
            double v;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1;
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "KB": v *= 1e3; break;
                case "MB": v *= 1e6; break;
                case "GB": v *= 1e9; break;
                case "TB": v *= 1e12; break;
            }
            return (long)v;
        }

        public static List<string> Lines(string s)
        {
            return (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("[timeout]") && !x.StartsWith("[error]")).ToList();
        }

        public static string LastLine(string s) { var l = Lines(s); return l.Count == 0 ? "" : l[l.Count - 1]; }

        public static long FileLen(string f)
        {
            try { var fi = new FileInfo(f); if (fi.Exists) return fi.Length; } catch { }
            try
            {
                var m = new DirectoryInfo(Path.GetDirectoryName(f)).GetFiles(Path.GetFileName(f));
                if (m.Length > 0) return m[0].Length;
            }
            catch { }
            return 0;
        }

        public static long DriveFree(string anyPath)
        {
            try { return new DriveInfo(Path.GetPathRoot(anyPath)).AvailableFreeSpace; } catch { return -1; }
        }

        public static Color LevelColor(int level)
        {
            return level == 2 ? Color.FromArgb(255, 59, 48) : level == 1 ? Color.FromArgb(255, 149, 0) : Color.FromArgb(52, 199, 89);
        }

        public static void AppendLog(string name, string text)
        {
            try { File.AppendAllText(Path.Combine(LogDir, name), text, Encoding.UTF8); } catch { }
        }

        public static void Reveal(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        public static string[] SubDirs(string parent)
        {
            try { return Directory.Exists(parent) ? Directory.GetDirectories(parent) : new string[0]; } catch { return new string[0]; }
        }
    }

    // ======================= 文件系统（不跟随联接/符号链接） =======================
    static class Fs
    {
        public static bool IsReparse(FileSystemInfo i)
        {
            try { return (i.Attributes & FileAttributes.ReparsePoint) != 0; } catch { return true; }
        }

        // 盘符根目录和用户/系统关键目录本身永远不允许被清空或删除
        public static bool IsProtected(string path)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return true; }
            string root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
            if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;
            var deny = new[] {
                Util.UserProfile, Util.LocalAppData, Util.Roaming, Util.WinDir, Util.ProgramData,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Util.UserProfile, "Downloads")
            };
            foreach (var d in deny)
                if (!string.IsNullOrEmpty(d) && string.Equals(full, d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // 转成 \\?\ 扩展路径，绕过 260 字符限制（深层 node_modules 的大小统计和删除都要用）
        public static string Ext(string p)
        {
            if (string.IsNullOrEmpty(p) || p.StartsWith(@"\\?\")) return p;
            string f;
            try { f = Path.GetFullPath(p); } catch { return p; }
            return f.StartsWith(@"\\") ? @"\\?\UNC\" + f.Substring(2) : @"\\?\" + f;
        }

        public static long DirSize(string path, TimeSpan? olderThan) { return DirSize(path, olderThan, CancellationToken.None); }

        public static long DirSize(string path, TimeSpan? olderThan, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(Ext(path))) return 0;
            DateTime cutoff = olderThan.HasValue ? DateTime.Now - olderThan.Value : DateTime.MaxValue;
            long total = 0;
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(Ext(path)));
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) break;
                var d = stack.Pop();
                try
                {
                    foreach (var e in d.EnumerateFileSystemInfos())
                    {
                        if (IsReparse(e)) continue;
                        var f = e as FileInfo;
                        if (f != null) { if (f.LastWriteTime < cutoff) { try { total += f.Length; } catch { } } }
                        else stack.Push((DirectoryInfo)e);
                    }
                }
                catch { }
            }
            return total;
        }

        // 清空目录内容，保留目录本身；被占用或无权限的项跳过
        public static long EmptyDir(string path, TimeSpan? olderThan, StringBuilder log)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(Ext(path))) return 0;
            if (IsProtected(path)) { log.AppendLine("  拒绝：受保护目录 " + path); return 0; }
            var root = new DirectoryInfo(Ext(path));
            if (IsReparse(root)) { log.AppendLine("  跳过：根目录是链接 " + path); return 0; }
            int failed = 0;
            DateTime? cutoff = olderThan.HasValue ? (DateTime?)(DateTime.Now - olderThan.Value) : null;
            long freed = DeleteChildren(root, cutoff, ref failed);
            if (failed > 0) log.AppendLine("  " + failed + " 项被占用或无权限，已跳过");
            return freed;
        }

        // 删除整个目录（项目产物用）；遇到联接只删链接本身
        public static long DeleteTree(string path, StringBuilder log)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(Ext(path))) return 0;
            if (IsProtected(path)) { log.AppendLine("  拒绝：受保护目录 " + path); return 0; }
            var root = new DirectoryInfo(Ext(path));
            if (IsReparse(root))
            {
                try { Directory.Delete(Ext(path), false); log.AppendLine("  删除链接 " + path); }
                catch (Exception ex) { log.AppendLine("  跳过链接 " + path + "：" + ex.Message); }
                return 0;
            }
            int failed = 0;
            long freed = DeleteChildren(root, null, ref failed);
            try { root.Attributes = FileAttributes.Normal; root.Delete(); } catch { failed++; }
            if (failed > 0) log.AppendLine("  " + path + "：" + failed + " 项被占用或无权限，已跳过");
            return freed;
        }

        static long DeleteChildren(DirectoryInfo d, DateTime? cutoff, ref int failed)
        {
            long freed = 0;
            FileSystemInfo[] entries;
            try { entries = d.GetFileSystemInfos(); } catch { failed++; return 0; }
            foreach (var e in entries)
            {
                try
                {
                    if (IsReparse(e))
                    {
                        if (!cutoff.HasValue)
                        {
                            if (e is DirectoryInfo) Directory.Delete(e.FullName, false);
                            else { e.Attributes = FileAttributes.Normal; e.Delete(); }
                        }
                        continue;
                    }
                    var f = e as FileInfo;
                    if (f != null)
                    {
                        if (cutoff.HasValue && f.LastWriteTime >= cutoff.Value) continue;
                        long len = f.Length;
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                        f.Delete();
                        freed += len;
                    }
                    else
                    {
                        var sub = (DirectoryInfo)e;
                        DateTime subTime = sub.LastWriteTime;
                        freed += DeleteChildren(sub, cutoff, ref failed);
                        try
                        {
                            if (sub.GetFileSystemInfos().Length == 0 && (!cutoff.HasValue || subTime < cutoff.Value))
                            {
                                sub.Attributes = FileAttributes.Normal;
                                sub.Delete();
                            }
                        }
                        catch { }
                    }
                }
                catch { failed++; }
            }
            return freed;
        }
    }

    // ======================= 磁盘状态 =======================
    class DriveStat
    {
        public string Name; public bool Ready; public long Free, Total;
        public double FreeGB { get { return Free / (double)Util.GB; } }

        public int Level(Config c)
        {
            if (!Ready) return 0;
            if (FreeGB < c.CritGB) return 2;
            if (FreeGB < c.WarnGB) return 1;
            return 0;
        }

        public static List<DriveStat> Read(Config c)
        {
            var list = new List<DriveStat>();
            foreach (var n in c.Drives)
            {
                var s = new DriveStat { Name = n };
                try
                {
                    var d = new DriveInfo(n + ":\\");
                    if (d.IsReady) { s.Ready = true; s.Free = d.AvailableFreeSpace; s.Total = d.TotalSize; }
                }
                catch { }
                list.Add(s);
            }
            return list;
        }

        public static long TotalFree(List<DriveStat> l) { return l.Where(x => x.Ready).Sum(x => x.Free); }
    }

    // ======================= 空间历史（logs\history.csv，每 10 分钟一点，保留 30 天） =======================
    class Sample { public DateTime T; public string D; public long Free; }

    static class History
    {
        static readonly object L = new object();
        static readonly List<Sample> samples = new List<Sample>();
        static DateTime lastWrite = DateTime.MinValue;
        const string TimeFmt = "yyyy-MM-dd HH:mm:ss";
        static string FilePath { get { return Path.Combine(Util.LogDir, "history.csv"); } }

        public static int Count { get { lock (L) return samples.Count; } }
        public static List<Sample> Snapshot() { lock (L) return samples.ToList(); }

        public static void Load()
        {
            lock (L)
            {
                samples.Clear();
                try
                {
                    if (!File.Exists(FilePath)) return;
                    var cutoff = DateTime.Now.AddDays(-30);
                    var keep = new List<string>();
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var p = line.Split(',');
                        if (p.Length != 3) continue;
                        DateTime t; long f;
                        if (!DateTime.TryParseExact(p[0], TimeFmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (!long.TryParse(p[2], out f) || t < cutoff) continue;
                        samples.Add(new Sample { T = t, D = p[1], Free = f });
                        keep.Add(line);
                    }
                    File.WriteAllLines(FilePath, keep);
                    if (samples.Count > 0) lastWrite = samples.Max(x => x.T);
                }
                catch { }
            }
        }

        public static void Record(List<DriveStat> stats)
        {
            lock (L)
            {
                var now = DateTime.Now;
                if (now - lastWrite < TimeSpan.FromMinutes(10)) return;
                lastWrite = now;
                var sb = new StringBuilder();
                foreach (var d in stats)
                {
                    if (!d.Ready) continue;
                    samples.Add(new Sample { T = now, D = d.Name, Free = d.Free });
                    sb.AppendLine(now.ToString(TimeFmt, CultureInfo.InvariantCulture) + "," + d.Name + "," + d.Free);
                }
                try { File.AppendAllText(FilePath, sb.ToString()); } catch { }
            }
        }

        public static bool Change(string drive, TimeSpan window, long nowFree, out long delta, out TimeSpan span)
        {
            delta = 0; span = TimeSpan.Zero;
            lock (L)
            {
                var from = DateTime.Now - window;
                Sample oldest = null;
                foreach (var s in samples)
                    if (s.D == drive && s.T >= from && (oldest == null || s.T < oldest.T)) oldest = s;
                if (oldest == null) return false;
                delta = nowFree - oldest.Free;
                span = DateTime.Now - oldest.T;
                return true;
            }
        }

        public static string ChangeText(DriveStat d)
        {
            long delta; TimeSpan span;
            if (!d.Ready || !Change(d.Name, TimeSpan.FromHours(24), d.Free, out delta, out span) || span.TotalMinutes < 30) return "";
            string label = span.TotalHours >= 23 ? "24h" : "近" + Math.Max(1, (int)Math.Round(span.TotalHours)) + "h";
            return "  " + label + " " + Util.FmtDelta(delta);
        }

        // 最小二乘拟合每天变化量，估算多久写满
        public static string Forecast(string drive, TimeSpan window, long nowFree)
        {
            List<Sample> pts;
            var from = DateTime.Now - window;
            lock (L) pts = samples.Where(s => s.D == drive && s.T >= from).OrderBy(s => s.T).ToList();
            if (pts.Count < 6 || (pts[pts.Count - 1].T - pts[0].T).TotalHours < 6) return "数据不足（至少要 6 小时的记录）";
            var t0 = pts[0].T;
            double n = pts.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var p in pts)
            {
                double x = (p.T - t0).TotalDays, y = p.Free / (double)Util.GB;
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double den = n * sxx - sx * sx;
            if (Math.Abs(den) < 1e-12) return "数据不足";
            double slope = (n * sxy - sx * sy) / den;
            string trend = (slope >= 0 ? "+" : "") + slope.ToString("0.00") + " G/天";
            if (slope > -0.05) return trend + "，基本持平";
            double days = nowFree / (double)Util.GB / -slope;
            return trend + "，按这个速度约" + (days < 1 ? "不到 1 天" : " " + days.ToString("0") + " 天") + "后用完";
        }
    }

    // ======================= 小状态文件 =======================
    static class State
    {
        static string FilePath { get { return Path.Combine(Util.LogDir, "state.ini"); } }

        public static string Get(string k)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                foreach (var l in File.ReadAllLines(FilePath))
                {
                    int i = l.IndexOf('=');
                    if (i > 0 && l.Substring(0, i) == k) return l.Substring(i + 1);
                }
            }
            catch { }
            return null;
        }

        public static void Set(string k, string v)
        {
            var lines = new List<string>();
            try { if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath)); } catch { }
            lines.RemoveAll(l => l.StartsWith(k + "="));
            lines.Add(k + "=" + v);
            try { File.WriteAllLines(FilePath, lines); } catch { }
        }

        public static DateTime GetTime(string k)
        {
            DateTime t;
            return DateTime.TryParseExact(Get(k) ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.MinValue;
        }

        public static void SetTime(string k, DateTime t) { Set(k, t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); }
    }

    // ======================= 托盘图标常驻（Windows 11） =======================
    static class TrayPin
    {
        const string Root = @"Control Panel\NotifyIconSettings";

        static string FindKey()
        {
            try
            {
                using (var r = Registry.CurrentUser.OpenSubKey(Root))
                {
                    if (r == null) return null;
                    foreach (var n in r.GetSubKeyNames())
                    {
                        using (var k = r.OpenSubKey(n))
                        {
                            if (k == null) continue;
                            var p = k.GetValue("ExecutablePath") as string;
                            if (p != null && string.Equals(p, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return Root + "\\" + n;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // -1：Windows 还没登记；0：隐藏区；1：常驻任务栏
        public static int State()
        {
            var k = FindKey();
            if (k == null) return -1;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(k))
                {
                    var v = key == null ? null : key.GetValue("IsPromoted");
                    return (v is int && (int)v == 1) ? 1 : 0;
                }
            }
            catch { return -1; }
        }

        public static bool Set(bool on)
        {
            var k = FindKey();
            if (k == null) return false;
            using (var key = Registry.CurrentUser.OpenSubKey(k, true))
            {
                if (key == null) return false;
                key.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord);
            }
            return true;
        }
    }

    static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "DiskGuard";

        public static bool Enabled
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue(Name) != null; }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }

    // ======================= 清理项 =======================
    enum Tier { Safe = 0, Careful = 1, Advice = 2 }

    class CleanItem
    {
        public string Name, Note;
        public Tier Tier;
        public string[] Paths = new string[0];
        public Func<long> Scan;
        public Func<StringBuilder, long> Clean;   // 只建议的项为 null
        public Action Open;                       // 可选：自定义“打开”动作
        public long LastSize = -1;
        public bool Scanned, Scanning;
        public bool DefaultOn { get { return Tier == Tier.Safe && Clean != null; } }
    }

    static class Items
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        static CleanItem Dir(Tier tier, string name, string note, TimeSpan? older, params string[] paths)
        {
            var ps = paths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return new CleanItem
            {
                Tier = tier, Name = name, Note = note, Paths = ps,
                Scan = () => ps.Sum(p => Fs.DirSize(p, older)),
                Clean = log =>
                {
                    long t = 0;
                    foreach (var p in ps) { if (Directory.Exists(p)) { log.AppendLine("  清理 " + p); t += Fs.EmptyDir(p, older, log); } }
                    return t;
                }
            };
        }

        static CleanItem Advice(string name, string note, params string[] paths)
        {
            var ps = paths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return new CleanItem { Tier = Tier.Advice, Name = name, Note = note, Paths = ps, Scan = () => ps.Sum(p => Fs.DirSize(p, null)) };
        }

        static string[] Children(string root, string sub)
        {
            return Util.SubDirs(root).Select(a => Path.Combine(a, sub)).Where(Directory.Exists).ToArray();
        }

        // Electron / Chromium 类桌面应用：用户数据目录里有 Local Storage / Preferences 等标记才算
        public static string[] ElectronCaches(out int apps)
        {
            var names = new[] { "Cache", "Code Cache", "GPUCache", "CachedData", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", @"Crashpad\completed" };
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Google", "Microsoft", "Mozilla", "Packages", "Temp", "JetBrains", "Docker" };
            var list = new List<string>();
            apps = 0;
            foreach (var root in new[] { Util.Roaming, Util.LocalAppData })
            {
                foreach (var app in Util.SubDirs(root))
                {
                    if (skip.Contains(Path.GetFileName(app))) continue;
                    bool chromium = Directory.Exists(Path.Combine(app, "Local Storage")) || File.Exists(Path.Combine(app, "Preferences"))
                                    || Directory.Exists(Path.Combine(app, "Session Storage")) || Directory.Exists(Path.Combine(app, "Network"));
                    if (!chromium) continue;
                    bool any = false;
                    foreach (var n in names)
                    {
                        var p = Path.Combine(app, n);
                        if (Directory.Exists(p)) { list.Add(p); any = true; }
                    }
                    if (any) apps++;
                }
            }
            return list.ToArray();
        }

        static string[] BrowserCaches()
        {
            var list = new List<string>();
            foreach (var root in new[] { Path.Combine(Util.LocalAppData, "Google", "Chrome", "User Data"), Path.Combine(Util.LocalAppData, "Microsoft", "Edge", "User Data") })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var prof in Util.SubDirs(root))
                {
                    var n = Path.GetFileName(prof);
                    if (n != "Default" && !n.StartsWith("Profile ")) continue;
                    foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" }) list.Add(Path.Combine(prof, c));
                }
                foreach (var c in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" }) list.Add(Path.Combine(root, c));
            }
            return list.Where(Directory.Exists).ToArray();
        }

        // ---------- npm 缓存位置：环境变量 > 用户 .npmrc > 全局 npmrc > 内置 npmrc > 默认 ----------
        static string npmFast, npmFull;

        static string RcValue(string file, string key)
        {
            try
            {
                if (!File.Exists(file)) return null;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("#") || l.StartsWith(";")) continue;
                    int eq = l.IndexOf('=');
                    if (eq > 0 && l.Substring(0, eq).Trim() == key) return l.Substring(eq + 1).Trim().Trim('"');
                }
            }
            catch { }
            return null;
        }

        static string FindOnPath(string exe)
        {
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (d.Length > 0 && File.Exists(Path.Combine(d.Trim(), exe))) return d.Trim(); } catch { }
            }
            return null;
        }

        public static string NpmCacheFast()
        {
            if (npmFast != null) return npmFast;
            string found = Environment.GetEnvironmentVariable("npm_config_cache");
            var userRc = Path.Combine(Util.UserProfile, ".npmrc");
            if (string.IsNullOrEmpty(found)) found = RcValue(userRc, "cache");
            string nodeDir = FindOnPath("node.exe");
            string prefix = RcValue(userRc, "prefix");
            if (string.IsNullOrEmpty(found) && !string.IsNullOrEmpty(prefix)) found = RcValue(Path.Combine(prefix, "etc", "npmrc"), "cache");
            if (string.IsNullOrEmpty(found) && nodeDir != null) found = RcValue(Path.Combine(nodeDir, "etc", "npmrc"), "cache");
            if (string.IsNullOrEmpty(found) && nodeDir != null) found = RcValue(Path.Combine(nodeDir, "node_modules", "npm", "npmrc"), "cache");
            if (string.IsNullOrEmpty(found)) found = Path.Combine(Util.LocalAppData, "npm-cache");
            npmFast = Environment.ExpandEnvironmentVariables(found);
            return npmFast;
        }

        // 文件里没找到时，后台线程里问一次 npm
        public static string NpmCache()
        {
            if (npmFull != null) return npmFull;
            var fast = NpmCacheFast();
            if (Directory.Exists(fast)) { npmFull = fast; return npmFull; }
            int ex;
            var o = Util.LastLine(Util.Run("cmd.exe", "/c npm config get cache", 20000, out ex));
            npmFull = (ex == 0 && o.Length > 2 && Directory.Exists(o)) ? o : fast;
            return npmFull;
        }

        static CleanItem Npm()
        {
            var it = new CleanItem
            {
                Tier = Tier.Safe, Name = "npm / npx 缓存",
                Note = "npm 下载过的包和 npx 临时安装（" + NpmCacheFast() + "），需要时自动重新下载",
                Paths = new[] { NpmCacheFast() }
            };
            it.Scan = () => Fs.DirSize(NpmCache(), null);
            it.Clean = log =>
            {
                var p = NpmCache();
                bool looksLikeCache = Directory.Exists(Path.Combine(p, "_cacache")) || Directory.Exists(Path.Combine(p, "_npx"))
                                      || Path.GetFileName(p.TrimEnd('\\')).IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeCache) { log.AppendLine("  跳过：" + p + " 看起来不像 npm 缓存目录"); return 0; }
                log.AppendLine("  清理 " + p);
                return Fs.EmptyDir(p, null, log);
            };
            return it;
        }

        public static List<CleanItem> Build(Config c)
        {
            string la = Util.LocalAppData, ro = Util.Roaming, up = Util.UserProfile, win = Util.WinDir, pd = Util.ProgramData;
            string goCache = Env("GOCACHE", Path.Combine(la, "go-build"));
            string goMod = Env("GOMODCACHE", Path.Combine(up, "go", "pkg", "mod"));
            var day = TimeSpan.FromDays(1);
            var L = new List<CleanItem>();

            // ---------- 放心清 ----------
            L.Add(Dir(Tier.Safe, "Go 编译缓存", "下次编译自动重建", null, goCache));
            L.Add(Dir(Tier.Safe, "用户临时文件（1 天前）", "被占用的文件自动跳过", day, Path.GetTempPath()));
            L.Add(Dir(Tier.Safe, "系统临时文件（1 天前）", "Windows\\Temp，没有管理员权限的部分会跳过", day, Path.Combine(win, "Temp")));
            L.Add(Dir(Tier.Safe, "Windows 更新下载缓存", "已下载的更新安装包，需要管理员权限", null, Path.Combine(win, "SoftwareDistribution", "Download")));
            L.Add(Updaters());
            L.Add(Npm());
            var vsix = Children(ro, "CachedExtensionVSIXs");
            L.Add(Dir(Tier.Safe, "编辑器插件安装包（" + vsix.Length + " 个编辑器）", "VS Code / Cursor 等下载过的 .vsix，插件已经装好，不影响使用", null, vsix));
            int apps;
            var el = ElectronCaches(out apps);
            L.Add(Dir(Tier.Safe, "桌面应用缓存（" + apps + " 个应用）", "Electron 类软件的 Cache / Code Cache / GPUCache，正在运行的软件文件会跳过，先关掉再清更彻底", null, el));
            L.Add(Dir(Tier.Safe, "开发工具下载缓存", "uv、node-gyp 的下载缓存，需要时自动重新下载", null, Path.Combine(la, "uv", "cache"), Path.Combine(la, "node-gyp", "Cache")));
            L.Add(Dir(Tier.Safe, "显卡着色器缓存", "游戏和浏览器启动时自动重建", null,
                Path.Combine(la, "D3DSCache"), Path.Combine(la, "NVIDIA", "DXCache"), Path.Combine(la, "NVIDIA", "GLCache"),
                Path.Combine(la, "AMD", "DxCache"), Path.Combine(la, "AMD", "GLCache")));
            L.Add(Dir(Tier.Safe, "崩溃转储和错误报告", "程序崩溃留下的 dump 和 Windows 错误报告", null,
                Path.Combine(la, "CrashDumps"), Path.Combine(la, "Microsoft", "Windows", "WER"),
                Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportQueue")));

            var known = L.SelectMany(i => i.Paths).Concat(new[] { NpmCacheFast() }).Select(p => p.TrimEnd('\\')).ToList();
            foreach (var p in c.ExtraDirs)
            {
                var t = p.TrimEnd('\\');
                if (known.Any(k => string.Equals(t, k, StringComparison.OrdinalIgnoreCase) || t.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                L.Add(Dir(Tier.Safe, "缓存目录：" + Path.GetFileName(t), p, null, p));
            }

            // ---------- 谨慎 ----------
            L.Add(Dir(Tier.Careful, "Go 模块缓存", "需要时重新下载，首次编译会变慢", null, goMod));
            L.Add(Dir(Tier.Careful, "测试用浏览器", "Playwright / Cypress / Puppeteer 下载的浏览器，删后要重新运行 npx playwright install 等命令", null,
                Path.Combine(la, "ms-playwright"), Path.Combine(la, "ms-playwright-mcp"), Path.Combine(la, "Cypress", "Cache"), Path.Combine(up, ".cache", "puppeteer")));
            L.Add(Dir(Tier.Careful, "pnpm 包仓库", "已安装的项目不受影响，新装依赖要重新下载", null, Path.Combine(la, "pnpm", "store"), Path.Combine(la, "pnpm-cache")));
            L.Add(Dir(Tier.Careful, "Maven / Gradle 本地仓库", "构建时重新下载依赖；mvn install 装的内部包和私服包可能下不回来", null, Path.Combine(up, ".m2", "repository"), Path.Combine(up, ".gradle", "caches")));
            L.Add(Dir(Tier.Careful, "浏览器缓存（Chrome / Edge）", "只删网页缓存，不动书签、密码、Cookie；浏览器开着时大部分会跳过", null, BrowserCaches()));
            L.Add(Dir(Tier.Careful, "Windows 日志（7 天前）", "Windows\\Logs，需要管理员；排查系统问题时可能用得上", TimeSpan.FromDays(7), Path.Combine(win, "Logs")));
            L.Add(Dism());
            L.Add(DockerPrune(c));
            L.Add(RecycleBin(c));

            // ---------- 只建议 ----------
            L.Add(Advice("微信 / QQ 聊天文件", "在微信「设置 → 存储空间」里清理，或把文件存储位置改到 E 盘", Path.Combine(ro, "Tencent"), Path.Combine(up, "Documents", "WeChat Files"), Path.Combine(ro, "xwechat")));
            L.Add(Advice("企业微信文件", "在企业微信「设置 → 通用」里清理缓存，或改文件存储位置", Path.Combine(up, "Documents", "WXWork")));
            L.Add(Advice("编辑器工作区历史", "Cursor / VS Code 的 workspaceStorage，含 AI 对话和本地历史，删了找不回", Path.Combine(ro, "Cursor", "User", "workspaceStorage"), Path.Combine(ro, "Code", "User", "workspaceStorage")));
            L.Add(Advice("JetBrains 数据", "当前在用的 IDE 数据；在 IDE 里用「File → Invalidate Caches」，或卸载旧版本后再删对应目录", Path.Combine(ro, "JetBrains"), Path.Combine(la, "JetBrains")));
            L.Add(Advice("~/.cache 工具缓存", "codex-runtimes、模型下载等，确认不再用后手动删除", Path.Combine(up, ".cache")));
            L.Add(Advice("下载文件夹", "自己挑着删，双击打开", Path.Combine(up, "Downloads")));
            L.Add(Pagefile(c));
            return L;
        }

        static void UpdaterTargets(List<string> files, List<string> pendings)
        {
            foreach (var d in Util.SubDirs(Util.LocalAppData).Where(x => x.EndsWith("-updater", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(d))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".exe" || ext == ".zip" || ext == ".7z" || ext == ".nupkg" || ext == ".msi") files.Add(f);
                    }
                    var pdir = Path.Combine(d, "pending");
                    if (Directory.Exists(pdir)) pendings.Add(pdir);
                }
                catch { }
            }
        }

        static CleanItem Updaters()
        {
            return new CleanItem
            {
                Tier = Tier.Safe, Name = "软件更新器残留安装包", Note = "AppData\\Local\\*-updater 里已下载的旧安装包",
                Paths = Util.SubDirs(Util.LocalAppData).Where(x => x.EndsWith("-updater", StringComparison.OrdinalIgnoreCase)).ToArray(),
                Scan = () =>
                {
                    var fs = new List<string>(); var ps = new List<string>();
                    UpdaterTargets(fs, ps);
                    return fs.Sum(f => Util.FileLen(f)) + ps.Sum(p => Fs.DirSize(p, null));
                },
                Clean = log =>
                {
                    var fs = new List<string>(); var ps = new List<string>();
                    UpdaterTargets(fs, ps);
                    long t = 0;
                    foreach (var f in fs)
                    {
                        try { long l = Util.FileLen(f); File.Delete(f); t += l; log.AppendLine("  删除 " + f); }
                        catch (Exception ex) { log.AppendLine("  跳过 " + f + "：" + ex.Message); }
                    }
                    foreach (var p in ps) t += Fs.EmptyDir(p, null, log);
                    return t;
                }
            };
        }

        static CleanItem Dism()
        {
            return new CleanItem
            {
                Tier = Tier.Careful, Name = "Windows 组件存储瘦身（DISM）",
                Note = "系统自带的 WinSxS 清理（StartComponentCleanup），删掉已被替代的旧组件；需要管理员，可能要十几分钟",
                Paths = new[] { Path.Combine(Util.WinDir, "WinSxS") },
                Scan = () => -2,
                Clean = log =>
                {
                    if (!Util.IsAdmin) { log.AppendLine("  需要管理员权限，已跳过"); return 0; }
                    long before = Util.DriveFree(Util.WinDir);
                    int ex;
                    Util.Run(Path.Combine(Util.WinDir, "System32", "Dism.exe"), "/Online /Cleanup-Image /StartComponentCleanup", 3600000, out ex, Util.Oem);
                    log.AppendLine("  DISM exit=" + ex);
                    return Math.Max(0, Util.DriveFree(Util.WinDir) - before);
                }
            };
        }

        static CleanItem DockerPrune(Config c)
        {
            return new CleanItem
            {
                Tier = Tier.Careful, Name = "Docker 构建缓存 + 悬空镜像",
                Note = "不动容器和卷。释放的空间留在 docker_data.vhdx 里，到 Docker 页做“一键回收”才还给磁盘",
                Paths = new[] { c.DockerVhdx },
                Scan = () =>
                {
                    if (!File.Exists(c.DockerExe)) return -1;
                    int ex;
                    var o = Util.Run(c.DockerExe, "builder du", 30000, out ex);
                    if (ex != 0) return -1;
                    var m = Regex.Match(o, @"Reclaimable:\s*(\S+)");
                    return m.Success ? Util.ParseDockerSize(m.Groups[1].Value) : -1;
                },
                Clean = log => DockerOps.Prune(c, s => log.AppendLine("  " + s))
            };
        }

        static CleanItem RecycleBin(Config c)
        {
            Func<long> query = () =>
            {
                long t = 0;
                foreach (var d in c.Drives)
                {
                    var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                    try { if (SHQueryRecycleBin(d + ":\\", ref info) == 0) t += info.i64Size; } catch { }
                }
                return t;
            };
            return new CleanItem
            {
                Tier = Tier.Careful, Name = "回收站", Note = "清空后无法恢复，请确认里面没有要找回的文件",
                Scan = query,
                Clean = log =>
                {
                    long before = query();
                    foreach (var d in c.Drives) { try { SHEmptyRecycleBin(IntPtr.Zero, d + ":\\", 7); } catch { } }
                    return Math.Max(0, before - query());
                },
                Open = () => { try { Process.Start("explorer.exe", "shell:RecycleBinFolder"); } catch { } }
            };
        }

        static CleanItem Pagefile(Config c)
        {
            var files = c.Drives.Select(d => d + @":\pagefile.sys").ToArray();
            return new CleanItem
            {
                Tier = Tier.Advice, Name = "虚拟内存文件 pagefile.sys",
                Note = "不能直接删；在「性能选项 → 高级 → 虚拟内存」里改小或换盘（双击打开）",
                Paths = files, Scan = () => files.Sum(f => Util.FileLen(f)),
                Open = () => { try { Process.Start("SystemPropertiesPerformance.exe"); } catch { } }
            };
        }
    }

    // ======================= 执行清理并写日志 =======================
    static class Cleaner
    {
        public static long Run(List<CleanItem> items, Config cfg, string title, Action<string> onStart, Action<int, long> onDone, out string summary)
        {
            var log = new StringBuilder();
            var before = DriveStat.Read(cfg);
            log.AppendLine("==== " + Util.Ts() + " " + title + " ====");
            long sum = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (onStart != null) onStart(it.Name);
                log.AppendLine("▶ " + it.Name);
                long f = 0;
                try { f = it.Clean(log); sum += f; log.AppendLine("  删除 " + Util.Fmt(f)); }
                catch (Exception ex) { log.AppendLine("  失败：" + ex.Message); }
                if (onDone != null) onDone(i, f);
            }
            var after = DriveStat.Read(cfg);
            foreach (var a in after)
            {
                var b = before.FirstOrDefault(x => x.Name == a.Name);
                if (b != null && a.Ready && b.Ready) log.AppendLine(a.Name + ": 可用 " + Util.Fmt(b.Free) + " → " + Util.Fmt(a.Free));
            }
            long delta = DriveStat.TotalFree(after) - DriveStat.TotalFree(before);
            summary = "删除 " + Util.Fmt(sum) + "，可用空间 " + Util.FmtDelta(delta);
            log.AppendLine("合计：" + summary);
            log.AppendLine();
            Util.AppendLog("clean.log", log.ToString());
            return sum;
        }
    }

    // ======================= 项目构建产物 =======================
    class Artifact
    {
        public string Path, Kind, Project, Root;
        public long Size = -1;
        public DateTime LastActive;
        public bool DefaultKind;
        public int InactiveDays { get { return LastActive == DateTime.MinValue ? 9999 : Math.Max(0, (int)(DateTime.Now - LastActive).TotalDays); } }
    }

    static class Projects
    {
        static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".svn", ".hg", ".idea", ".vscode", ".history", "$RECYCLE.BIN", "System Volume Information" };
        static readonly HashSet<string> ArtifactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "target", "dist", "build", ".next", ".nuxt", ".turbo", ".parcel-cache", ".angular", ".svelte-kit", ".gradle", "venv", ".venv", "env", "__pycache__", ".pytest_cache", ".mypy_cache" };

        static bool Has(string dir, params string[] files)
        {
            foreach (var f in files) { try { if (File.Exists(System.IO.Path.Combine(dir, f))) return true; } catch { } }
            return false;
        }

        // 只有同级存在对应的项目标记文件才认定为构建产物
        public static string Classify(string parent, string name, string full)
        {
            switch (name.ToLowerInvariant())
            {
                case "node_modules": return Has(parent, "package.json") ? "node_modules" : null;
                case "target": return Has(parent, "pom.xml", "Cargo.toml", "build.gradle", "build.gradle.kts", "build.sbt") ? "target" : null;
                case ".next": case ".nuxt": case ".turbo": case ".parcel-cache": case ".angular": case ".svelte-kit":
                    return Has(parent, "package.json") ? name.ToLowerInvariant() : null;
                case "dist": return Has(parent, "package.json") ? "dist" : null;
                case "build": return Has(parent, "build.gradle", "build.gradle.kts") ? "build" : null;
                case ".gradle": return Has(parent, "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts") ? ".gradle" : null;
                case "venv": case ".venv": case "env": return File.Exists(System.IO.Path.Combine(full, "pyvenv.cfg")) ? name.ToLowerInvariant() : null;
            }
            return null;
        }

        // dist 可能是有人手动提交的发布物，默认不勾
        public static bool DefaultKind(string kind) { return kind != "dist"; }

        public static List<Artifact> Find(IEnumerable<string> roots, int maxDepth, CancellationToken ct)
        {
            var list = new List<Artifact>();
            foreach (var r in roots)
                if (Directory.Exists(r)) Walk(new DirectoryInfo(r), 0, maxDepth, r, list, ct);
            return list;
        }

        static void Walk(DirectoryInfo d, int depth, int max, string root, List<Artifact> list, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            DirectoryInfo[] subs;
            try { subs = d.GetDirectories(); } catch { return; }
            foreach (var s in subs)
            {
                if (Fs.IsReparse(s) || SkipNames.Contains(s.Name)) continue;
                var kind = Classify(d.FullName, s.Name, s.FullName);
                if (kind != null)
                {
                    list.Add(new Artifact { Path = s.FullName, Kind = kind, Project = d.FullName, Root = root, DefaultKind = DefaultKind(kind) });
                    continue;
                }
                if (ArtifactNames.Contains(s.Name)) continue;
                if (depth < max) Walk(s, depth + 1, max, root, list, ct);
            }
        }

        // 删除前再校验一次：仍在配置的根目录下、仍满足项目标记规则
        public static bool StillArtifact(string path, Config cfg)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(path).TrimEnd('\\');
                if (!cfg.ProjectRoots.Any(r => full.StartsWith(System.IO.Path.GetFullPath(r).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) return false;
                var parent = System.IO.Path.GetDirectoryName(full);
                return Classify(parent, System.IO.Path.GetFileName(full), full) != null && !Fs.IsProtected(full);
            }
            catch { return false; }
        }

        // 项目最近一次改动：源码文件（深 3 层、最多 4000 项）和 git 记录里最新的修改时间
        public static DateTime LastActive(string project)
        {
            DateTime best = DateTime.MinValue;
            int budget = 4000;
            ScanActive(new DirectoryInfo(project), 0, ref best, ref budget);
            foreach (var g in new[] { @".git\index", @".git\HEAD", @".git\FETCH_HEAD", @".git\logs\HEAD" })
            {
                try { var f = new FileInfo(System.IO.Path.Combine(project, g)); if (f.Exists && f.LastWriteTime > best) best = f.LastWriteTime; } catch { }
            }
            return best;
        }

        static void ScanActive(DirectoryInfo d, int depth, ref DateTime best, ref int budget)
        {
            if (budget <= 0) return;
            try
            {
                foreach (var e in d.EnumerateFileSystemInfos())
                {
                    if (--budget <= 0) return;
                    if (Fs.IsReparse(e)) continue;
                    var f = e as FileInfo;
                    if (f != null)
                    {
                        if (f.LastWriteTime > best && f.LastWriteTime <= DateTime.Now.AddDays(1)) best = f.LastWriteTime;
                    }
                    else if (depth < 3 && !SkipNames.Contains(e.Name) && !ArtifactNames.Contains(e.Name))
                        ScanActive((DirectoryInfo)e, depth + 1, ref best, ref budget);
                }
            }
            catch { }
        }
    }

    // ======================= Docker =======================
    class DockerInfo
    {
        public long VhdxSize = -1, Used = -1, BuildCache = -1;
        public int Images = -1, Dangling = -1, Volumes = -1, DanglingVolumes = -1;
        public List<string> Running = new List<string>();
        public bool EngineUp;
        public string Error;
    }

    static class DockerOps
    {
        public static bool EngineUp(Config c)
        {
            if (!File.Exists(c.DockerExe)) return false;
            int ex;
            Util.Run(c.DockerExe, "version --format \"{{.Server.Version}}\"", 15000, out ex);
            return ex == 0;
        }

        static List<string> List(Config c, string args, out bool ok)
        {
            int ex;
            var o = Util.Run(c.DockerExe, args, 60000, out ex);
            ok = ex == 0;
            return ok ? Util.Lines(o) : new List<string>();
        }

        public static List<string> Running(Config c)
        {
            bool ok;
            return List(c, "ps --format \"{{.Names}}\"", out ok);
        }

        public static long InternalUsed(Config c)
        {
            int ex;
            var df = Util.Run("wsl.exe", "-d " + c.DockerDistro + " -u root df -B1 " + c.DockerMount, 30000, out ex);
            var m = Regex.Match(df, @"^\S+\s+(\d+)\s+(\d+)\s+(\d+)\s+\d+%\s+" + Regex.Escape(c.DockerMount) + @"\s*$", RegexOptions.Multiline);
            return m.Success ? long.Parse(m.Groups[2].Value) : -1;
        }

        public static DockerInfo Read(Config c)
        {
            var i = new DockerInfo();
            try { if (File.Exists(c.DockerVhdx)) i.VhdxSize = new FileInfo(c.DockerVhdx).Length; } catch { }
            if (!File.Exists(c.DockerExe)) { i.Error = "没找到 docker.exe（配置项 dockerExe）"; return i; }
            i.EngineUp = EngineUp(c);
            if (!i.EngineUp) { i.Error = "Docker 引擎没有运行或没有响应"; return i; }
            i.Used = InternalUsed(c);
            int ex;
            var o = Util.Run(c.DockerExe, "builder du", 60000, out ex);
            var mm = Regex.Match(o, @"Reclaimable:\s*(\S+)");
            if (ex == 0 && mm.Success) i.BuildCache = Util.ParseDockerSize(mm.Groups[1].Value);
            bool ok;
            var l = List(c, "images -q", out ok); if (ok) i.Images = l.Distinct().Count();
            l = List(c, "images -q -f dangling=true", out ok); if (ok) i.Dangling = l.Distinct().Count();
            l = List(c, "volume ls -q", out ok); if (ok) i.Volumes = l.Count;
            l = List(c, "volume ls -q -f dangling=true", out ok); if (ok) i.DanglingVolumes = l.Count;
            i.Running = Running(c);
            return i;
        }

        public static List<string> DanglingVolumes(Config c)
        {
            bool ok;
            return List(c, "volume ls -q -f dangling=true", out ok);
        }

        public static long Prune(Config c, Action<string> log)
        {
            if (!EngineUp(c)) { log("Docker 引擎没有运行，跳过"); return 0; }
            int ex;
            var o1 = Util.Run(c.DockerExe, "builder prune -a -f", 1800000, out ex);
            log("builder prune exit=" + ex + "  " + Util.LastLine(o1));
            var o2 = Util.Run(c.DockerExe, "image prune -f", 600000, out ex);
            log("image prune exit=" + ex + "  " + Util.LastLine(o2));
            long t = 0;
            var m = Regex.Match(o1, @"Total:\s*(\S+)");
            if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
            m = Regex.Match(o2, @"Total reclaimed space:\s*(\S+)");
            if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
            return t;
        }

        // 在线 TRIM：让 ext4 把已删除的块告诉虚拟磁盘，之后压缩才能真正变小；不停容器
        public static bool Trim(Config c, Action<string> log)
        {
            if (!EngineUp(c)) { log("Docker 引擎没有运行，跳过 TRIM"); return false; }
            int ex;
            var o = Util.Run("wsl.exe", "-d " + c.DockerDistro + " -u root fstrim -v " + c.DockerMount, 1800000, out ex);
            log("fstrim exit=" + ex + "  " + Util.LastLine(o));
            return ex == 0;
        }

        static bool WaitVmGone(int seconds)
        {
            for (int i = 0; i < seconds / 2; i++)
            {
                if (Process.GetProcessesByName("vmmemWSL").Length == 0) return true;
                Thread.Sleep(2000);
            }
            return Process.GetProcessesByName("vmmemWSL").Length == 0;
        }

        // 一键回收：TRIM → 停 Docker 和 WSL → diskpart 压缩 → 重启 Docker → 把原来运行中的容器全部拉起来
        public static void Reclaim(Config c, Action<string> log)
        {
            if (!Util.IsAdmin) { log("需要管理员权限：请在托盘菜单点“以管理员身份重新启动”后再试。"); return; }
            if (!File.Exists(c.DockerVhdx)) { log("没找到虚拟磁盘文件：" + c.DockerVhdx); return; }
            long vBefore = Util.FileLen(c.DockerVhdx), fBefore = Util.DriveFree(c.DockerVhdx);
            log("开始：docker_data.vhdx " + Util.Fmt(vBefore) + "，所在盘可用 " + Util.Fmt(fBefore));

            var before = new List<string>();
            if (EngineUp(c))
            {
                before = Running(c);
                log("运行中的容器（" + before.Count + "）：" + (before.Count == 0 ? "无" : string.Join(", ", before)));
                File.WriteAllLines(Path.Combine(Util.LogDir, "docker_running_before.txt"), before);
                log("1/4 在线 TRIM…");
                Trim(c, log);
            }
            else log("Docker 引擎没有运行，跳过 TRIM（不 TRIM 的话压缩效果有限）");

            try
            {
                log("2/4 停止 Docker Desktop…");
                int ex;
                Util.Run(c.DockerExe, "desktop stop", 240000, out ex);
                if (ex != 0)
                {
                    log("docker desktop stop 失败（exit=" + ex + "），改为结束进程");
                    foreach (var n in new[] { "Docker Desktop", "com.docker.backend", "com.docker.build" })
                        foreach (var p in Process.GetProcessesByName(n)) { try { p.Kill(); } catch { } }
                }
                Thread.Sleep(5000);
                Util.Run("wsl.exe", "--shutdown", 120000, out ex);
                if (!WaitVmGone(90)) { log("WSL 虚拟机还没完全退出，为了安全放弃压缩。"); return; }

                log("3/4 压缩虚拟磁盘（可能要几十分钟，期间不要启动 Docker）…");
                var script = Path.Combine(Path.GetTempPath(), "diskguard_compact.txt");
                File.WriteAllText(script, "select vdisk file=\"" + c.DockerVhdx + "\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\nexit\r\n", Encoding.ASCII);
                var sw = Stopwatch.StartNew();
                Util.Run(Path.Combine(Util.WinDir, "System32", "diskpart.exe"), "/s \"" + script + "\"", 7200000, out ex, Util.Oem);
                long vAfter = Util.FileLen(c.DockerVhdx);
                log("diskpart exit=" + ex + "，用时 " + (int)sw.Elapsed.TotalMinutes + " 分钟，vhdx " + Util.Fmt(vBefore) + " → " + Util.Fmt(vAfter));
            }
            finally
            {
                log("4/4 启动 Docker Desktop 并恢复容器…");
                try { Process.Start(new ProcessStartInfo(c.DockerDesktopExe) { UseShellExecute = true }); }
                catch (Exception e) { log("启动 Docker Desktop 失败：" + e.Message); }
                bool up = false;
                for (int i = 0; i < 72 && !up; i++) { if (EngineUp(c)) up = true; else Thread.Sleep(5000); }
                if (!up) log("6 分钟内 Docker 引擎没有就绪，请手动打开 Docker Desktop 检查。原来运行的容器见 logs\\docker_running_before.txt");
                else
                {
                    Thread.Sleep(20000);
                    var now = Running(c);
                    int ex;
                    foreach (var n in before.Where(x => !now.Contains(x)))
                        log("启动 " + n + "：" + Util.LastLine(Util.Run(c.DockerExe, "start " + n, 120000, out ex)) + " (exit=" + ex + ")");
                    Thread.Sleep(5000);
                    now = Running(c);
                    var down = before.Where(x => !now.Contains(x)).ToList();
                    log(down.Count == 0 ? "原来运行中的 " + before.Count + " 个容器都已恢复" : "没能恢复：" + string.Join(", ", down));
                }
                log("完成：所在盘可用 " + Util.Fmt(fBefore) + " → " + Util.Fmt(Util.DriveFree(c.DockerVhdx)));
            }
        }
    }

    // ======================= 空间暴涨定位 =======================
    static class Watch
    {
        static Dictionary<string, long> last;
        static DateTime lastAt = DateTime.MinValue;
        static int running;

        static List<KeyValuePair<string, string>> Targets(Config c)
        {
            var up = Util.UserProfile;
            var t = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("临时文件", Path.GetTempPath()),
                new KeyValuePair<string, string>("下载文件夹", Path.Combine(up, "Downloads")),
                new KeyValuePair<string, string>("桌面", Path.Combine(up, "Desktop")),
                new KeyValuePair<string, string>("npm 缓存", Items.NpmCacheFast()),
                new KeyValuePair<string, string>("Go 编译缓存", Path.Combine(Util.LocalAppData, "go-build")),
                new KeyValuePair<string, string>("Docker 数据盘", c.DockerVhdx)
            };
            foreach (var d in c.Drives) t.Add(new KeyValuePair<string, string>("pagefile(" + d + ":)", d + @":\pagefile.sys"));
            foreach (var w in c.WatchDirs) t.Add(new KeyValuePair<string, string>(Path.GetFileName(w.TrimEnd('\\')), w));
            return t;
        }

        static Dictionary<string, long> Measure(Config c)
        {
            var r = new Dictionary<string, long>();
            foreach (var t in Targets(c))
            {
                long v = Directory.Exists(t.Value) ? Fs.DirSize(t.Value, null) : Util.FileLen(t.Value);
                r[t.Key + "|" + t.Value] = v;
            }
            return r;
        }

        public static void MaybeSnapshot(Config c)
        {
            if (DateTime.Now - lastAt < TimeSpan.FromMinutes(30)) return;
            if (Interlocked.Exchange(ref running, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { last = Measure(c); lastAt = DateTime.Now; }
                catch { }
                finally { Interlocked.Exchange(ref running, 0); }
            });
        }

        public static void Explain(Config c, Action<string> done)
        {
            var prev = last;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text;
                try
                {
                    var now = Measure(c);
                    if (prev == null) text = "还没有对比基准，可以打开「空间分析」看看。";
                    else
                    {
                        var grow = now.Where(kv => prev.ContainsKey(kv.Key))
                            .Select(kv => new KeyValuePair<string, long>(kv.Key.Split('|')[0], kv.Value - prev[kv.Key]))
                            .Where(kv => kv.Value > 200L * 1048576).OrderByDescending(kv => kv.Value).Take(3).ToList();
                        text = grow.Count == 0 ? "常见位置没有明显增长，可以打开「空间分析」查找。"
                                               : "主要增长：" + string.Join("、", grow.Select(g => g.Key + " " + Util.FmtDelta(g.Value)));
                    }
                }
                catch (Exception e) { text = "定位失败：" + e.Message; }
                Util.AppendLog("alerts.log", Util.Ts() + " " + text + Environment.NewLine);
                done(text);
            });
        }
    }

    // ======================= 界面公共部分 =======================
    class ListViewEx : ListView
    {
        public ListViewEx() { DoubleBuffered = true; View = View.Details; FullRowSelect = true; HideSelection = false; }
    }

    class PageBase : UserControl
    {
        bool shown;
        protected void UI(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || shown) return;
            shown = true;
            if (IsHandleCreated) BeginInvoke(new Action(FirstShown));
            else HandleCreated += (s, a) => BeginInvoke(new Action(FirstShown));
        }

        protected virtual void FirstShown() { }

        protected static Button Btn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
            b.Click += h;
            return b;
        }

        // 提示文字区：按当前宽度自动算行数调整高度，避免长说明被截断（height 只作最小高度）
        protected static Label Hint(string text, int height)
        {
            var l = new Label { Dock = DockStyle.Top, Height = height, ForeColor = Color.DimGray, Padding = new Padding(2, 4, 2, 0), Text = text };
            EventHandler fit = (s, e) =>
            {
                int w = l.ClientSize.Width - l.Padding.Horizontal;
                if (w <= 20) return;
                var sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                int h = Math.Max(height, sz.Height + l.Padding.Vertical + 4);
                if (l.Height != h) l.Height = h;
            };
            l.Resize += fit;
            l.TextChanged += fit;
            l.FontChanged += fit;
            return l;
        }
    }

    class DrivePanel : Panel
    {
        public Func<List<DriveStat>> Source;
        public Func<Config> Cfg;
        public const int RowH = 28;

        public DrivePanel() { DoubleBuffered = true; ResizeRedraw = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var list = Source == null ? null : Source();
            if (list == null) return;
            var g = e.Graphics;
            int y = 8, labelW = 34, textW = 360;
            foreach (var d in list)
            {
                var bar = new Rectangle(12 + labelW, y + 6, Math.Max(60, Width - 24 - labelW - textW), 14);
                TextRenderer.DrawText(g, d.Name + ":", Font, new Point(12, y + 3), ForeColor);
                using (var bg = new SolidBrush(Color.FromArgb(229, 229, 234))) g.FillRectangle(bg, bar);
                string t;
                if (d.Ready && d.Total > 0)
                {
                    double used = (double)(d.Total - d.Free) / d.Total;
                    using (var b = new SolidBrush(Util.LevelColor(d.Level(Cfg()))))
                        g.FillRectangle(b, new Rectangle(bar.X, bar.Y, (int)(bar.Width * used), bar.Height));
                    t = string.Format("可用 {0} / {1}（已用 {2:0}%）{3}", Util.Fmt(d.Free), Util.Fmt(d.Total), used * 100, History.ChangeText(d));
                }
                else t = "不可用";
                TextRenderer.DrawText(g, t, Font, new Point(bar.Right + 10, y + 3), ForeColor);
                y += RowH;
            }
        }
    }

    // ======================= 页 1：安全清理 =======================
    class CleanPage : PageBase
    {
        readonly TrayApp app;
        readonly ListViewEx lv;
        readonly Label status;
        readonly ProgressBar progress;
        readonly CheckBox hideZero;
        // 扫描完且不足 1M 的项视为“没有可清的”，显示为 0 K 的几百字节也一起隐藏；“运行时计算”(-1) 的项不隐藏
        static bool Negligible(CleanItem i) { return i.Scanned && i.LastSize >= 0 && i.LastSize < 1024L * 1024; }
        readonly Button btnScan, btnClean, btnOpen;
        readonly ListViewGroup[] groups;
        List<CleanItem> items;
        readonly Dictionary<string, bool> chk = new Dictionary<string, bool>();
        bool busy, populating;

        public CleanPage(TrayApp app)
        {
            this.app = app;
            items = Items.Build(app.Cfg);

            var tip = Hint("分三级：「放心清」删了会自动重建；「谨慎」默认不勾，删了要重新下载或有副作用；「只建议」不提供删除，双击打开位置或设置自己处理。" +
                           "不会碰项目代码、Docker 卷和容器、系统组件文件。被占用的文件自动跳过，每次清理都写入日志。鼠标停在某一项上可以看到具体路径。", 62);

            lv = new ListViewEx { CheckBoxes = true, Dock = DockStyle.Fill, ShowItemToolTips = true, ShowGroups = true };
            lv.Columns.Add("项目", 290);
            lv.Columns.Add("可释放", 100, HorizontalAlignment.Right);
            lv.Columns.Add("说明", 600);
            groups = new[]
            {
                new ListViewGroup("放心清（默认勾选，删了会自动重建）"),
                new ListViewGroup("谨慎（默认不勾，删了要重新下载或有副作用）"),
                new ListViewGroup("只建议（不提供删除，双击打开位置或设置）")
            };
            lv.Groups.AddRange(groups);
            lv.ItemCheck += (s, e) =>
            {
                var it = (CleanItem)lv.Items[e.Index].Tag;
                if (it.Clean == null && e.NewValue == CheckState.Checked) e.NewValue = CheckState.Unchecked;
            };
            lv.ItemChecked += (s, e) =>
            {
                if (populating) return;
                chk[((CleanItem)e.Item.Tag).Name] = e.Item.Checked;
                if (!busy) UpdateTotal();
            };
            lv.DoubleClick += (s, e) => OpenSelected();

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            btnClean = Btn("清理所选", (s, e) => DoClean());
            btnScan = Btn("重新扫描", (s, e) => Scan());
            btnOpen = Btn("打开位置", (s, e) => OpenSelected());
            hideZero = new CheckBox { Text = "隐藏为 0 的项", AutoSize = true, Checked = true, Margin = new Padding(3, 9, 12, 3) };
            hideZero.CheckedChanged += (s, e) => Populate();
            progress = new ProgressBar { Width = 160, Height = 18, Margin = new Padding(3, 10, 12, 3), Visible = false };
            status = new Label { AutoSize = true, Padding = new Padding(0, 9, 8, 0) };
            bottom.Controls.AddRange(new Control[] { btnClean, btnScan, btnOpen, hideZero, progress, status });

            Controls.Add(lv);
            Controls.Add(tip);
            Controls.Add(bottom);
            Populate();
        }

        protected override void FirstShown()
        {
            bool all = true;
            foreach (var it in items)
            {
                long v;
                if (app.CacheGet(it.Name, out v)) { it.LastSize = v; it.Scanned = true; } else all = false;
            }
            if (all) { Populate(); status.Text += "（5 分钟内的扫描结果）"; }
            else Scan();
        }

        bool Checked(CleanItem it)
        {
            bool v;
            return it.Clean != null && (chk.TryGetValue(it.Name, out v) ? v : it.DefaultOn);
        }

        static string SizeText(CleanItem it)
        {
            if (it.Scanning) return "扫描中…";
            if (!it.Scanned) return "…";
            return Util.Fmt(it.LastSize);
        }

        static string Tooltip(CleanItem it)
        {
            var ex = it.Paths.Where(p => Directory.Exists(p) || File.Exists(p)).Take(10).ToList();
            var s = it.Note;
            if (ex.Count > 0) s += "\n" + string.Join("\n", ex);
            int more = it.Paths.Count(p => Directory.Exists(p) || File.Exists(p)) - ex.Count;
            if (more > 0) s += "\n…另外 " + more + " 个位置";
            return s;
        }

        void Populate()
        {
            populating = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var it in items)
            {
                if (hideZero.Checked && Negligible(it)) continue;
                var row = new ListViewItem(new[] { it.Name, SizeText(it), it.Note }) { Tag = it, Group = groups[(int)it.Tier], ToolTipText = Tooltip(it) };
                row.Checked = Checked(it);
                if (it.Clean == null) row.ForeColor = Color.DimGray;
                lv.Items.Add(row);
            }
            lv.EndUpdate();
            populating = false;
            UpdateTotal();
        }

        void UpdateRow(CleanItem it)
        {
            foreach (ListViewItem r in lv.Items) if (r.Tag == it) { r.SubItems[1].Text = SizeText(it); break; }
        }

        void UpdateTotal()
        {
            var sel = items.Where(i => Checked(i) && !(hideZero.Checked && Negligible(i))).ToList();
            status.Text = "已勾选 " + sel.Count + " 项，预计可释放约 " + Util.Fmt(sel.Where(i => i.LastSize > 0).Sum(i => i.LastSize));
        }

        void SetBusy(bool b, string text, int max)
        {
            busy = b;
            btnScan.Enabled = btnClean.Enabled = !b;
            UseWaitCursor = b;
            progress.Visible = b;
            if (b) { progress.Maximum = Math.Max(1, max); progress.Value = 0; }
            if (text != null) status.Text = text;
        }

        void OpenSelected()
        {
            if (lv.SelectedItems.Count == 0) return;
            var it = (CleanItem)lv.SelectedItems[0].Tag;
            if (it.Open != null) { it.Open(); return; }
            var p = it.Paths.FirstOrDefault(x => Directory.Exists(x) || File.Exists(x));
            if (p != null) Util.Reveal(p);
        }

        public void Scan()
        {
            if (busy) return;
            items = Items.Build(app.Cfg);
            var list = items.ToList();
            foreach (var it in list) it.Scanning = true;
            Populate();
            SetBusy(true, "正在扫描…", list.Count);
            var sw = Stopwatch.StartNew();
            int done = 0, total = list.Count;
            foreach (var it0 in list)
            {
                var it = it0;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    long sz;
                    try { sz = it.Scan(); } catch { sz = -1; }
                    it.LastSize = sz; it.Scanned = true; it.Scanning = false;
                    app.CacheSet(it.Name, sz);
                    int n = Interlocked.Increment(ref done);
                    UI(() =>
                    {
                        UpdateRow(it);
                        progress.Value = Math.Min(progress.Maximum, n);
                        status.Text = "正在扫描… " + n + "/" + total;
                        if (n == total)
                        {
                            SetBusy(false, null, 0);
                            Populate();
                            status.Text += "（扫描用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒）";
                        }
                    });
                });
            }
        }

        void DoClean()
        {
            var sel = items.Where(i => Checked(i) && !(hideZero.Checked && Negligible(i))).ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "请先勾选要清理的项目。", "DiskGuard"); return; }
            long est = sel.Where(i => i.LastSize > 0).Sum(i => i.LastSize);
            var careful = sel.Where(i => i.Tier == Tier.Careful).ToList();
            var msg = "将清理以下 " + sel.Count + " 项，预计释放约 " + Util.Fmt(est) + "：\n\n" +
                      string.Join("\n", sel.Select(i => (i.Tier == Tier.Careful ? "⚠ " : "• ") + i.Name)) +
                      (careful.Count > 0 ? "\n\n带 ⚠ 的是谨慎项，删后需要重新下载或有副作用。" : "") + "\n\n确认清理？";
            if (MessageBox.Show(this, msg, "确认清理", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true, "正在清理…", sel.Count);
            var cfg = app.Cfg;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string summary;
                Cleaner.Run(sel, cfg, "手动清理",
                    name => UI(() => status.Text = "正在清理：" + name),
                    (i, f) =>
                    {
                        var it = sel[i];
                        app.CacheRemove(it.Name);
                        UI(() =>
                        {
                            foreach (ListViewItem r in lv.Items) if (r.Tag == it) { r.SubItems[1].Text = "已清理 " + Util.Fmt(f); r.Checked = false; }
                            progress.Value = Math.Min(progress.Maximum, i + 1);
                        });
                    }, out summary);
                UI(() =>
                {
                    SetBusy(false, "完成：" + summary + "（详见清理日志）", 0);
                    app.RefreshStats();
                    app.Notify("清理完成", summary, ToolTipIcon.Info, 0);
                });
            });
        }
    }

    // ======================= 页 2：项目产物 =======================
    class ProjectsPage : PageBase
    {
        readonly TrayApp app;
        readonly ListViewEx lv;
        readonly Label status, header;
        readonly ProgressBar progress;
        readonly CheckBox onlyInactive;
        readonly Button btnScan, btnClean;
        List<Artifact> arts = new List<Artifact>();
        readonly Dictionary<string, bool> chk = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool busy, populating;
        CancellationTokenSource cts;

        public ProjectsPage(TrayApp app)
        {
            this.app = app;
            header = Hint("", 42);
            lv = new ListViewEx { CheckBoxes = true, Dock = DockStyle.Fill, ShowItemToolTips = true };
            lv.Columns.Add("项目", 300);
            lv.Columns.Add("类型", 110);
            lv.Columns.Add("大小", 90, HorizontalAlignment.Right);
            lv.Columns.Add("未改动", 80, HorizontalAlignment.Right);
            lv.Columns.Add("路径", 520);
            lv.ItemChecked += (s, e) => { if (populating) return; chk[((Artifact)e.Item.Tag).Path] = e.Item.Checked; if (!busy) UpdateTotal(); };
            lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count > 0) Util.Reveal(((Artifact)lv.SelectedItems[0].Tag).Path); };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            btnClean = Btn("删除所选", (s, e) => DoClean());
            btnScan = Btn("重新扫描", (s, e) => Scan());
            var btnNone = Btn("全不选", (s, e) => SetAll(false));
            var btnInactive = Btn("勾选不活跃项", (s, e) => SetAll(true));
            onlyInactive = new CheckBox { Text = "只看不活跃项目", AutoSize = true, Margin = new Padding(3, 9, 12, 3) };
            onlyInactive.CheckedChanged += (s, e) => Populate();
            progress = new ProgressBar { Width = 160, Height = 18, Margin = new Padding(3, 10, 12, 3), Visible = false };
            status = new Label { AutoSize = true, Padding = new Padding(0, 9, 8, 0) };
            bottom.Controls.AddRange(new Control[] { btnClean, btnScan, btnNone, btnInactive, onlyInactive, progress, status });

            Controls.Add(lv);
            Controls.Add(header);
            Controls.Add(bottom);
            UpdateHeader();
        }

        void UpdateHeader()
        {
            header.Text = "扫描目录：" + string.Join("；", app.Cfg.ProjectRoots) + "（配置项 projectRoot）。只认有项目标记的目录：node_modules 要有 package.json，target 要有 pom.xml / Cargo.toml，venv 要有 pyvenv.cfg 等。" +
                          "超过 " + app.Cfg.InactiveDays + " 天没改动的项目默认勾选；dist 可能是提交的发布物，默认不勾。删除前会再校验一次。";
        }

        protected override void FirstShown() { Scan(); }

        bool IsChecked(Artifact a)
        {
            bool v;
            return chk.TryGetValue(a.Path, out v) ? v : (a.DefaultKind && a.InactiveDays >= app.Cfg.InactiveDays);
        }

        void SetAll(bool inactiveOnly)
        {
            foreach (var a in arts) chk[a.Path] = inactiveOnly && a.DefaultKind && a.InactiveDays >= app.Cfg.InactiveDays;
            Populate();
        }

        static string Rel(Artifact a)
        {
            var r = a.Root.TrimEnd('\\');
            return a.Project.Length > r.Length ? a.Project.Substring(r.Length).TrimStart('\\') : Path.GetFileName(a.Project);
        }

        void Populate()
        {
            populating = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var a in arts)
            {
                bool inactive = a.InactiveDays >= app.Cfg.InactiveDays;
                if (onlyInactive.Checked && !inactive) continue;
                var days = a.InactiveDays >= 9999 ? "未知" : a.InactiveDays == 0 ? "今天" : a.InactiveDays + " 天";
                var row = new ListViewItem(new[] { Rel(a), a.Kind, a.Size < 0 ? "计算中…" : Util.Fmt(a.Size), days, a.Path })
                {
                    Tag = a, ToolTipText = a.Path + "\n最近改动：" + (a.LastActive == DateTime.MinValue ? "未知" : a.LastActive.ToString("yyyy-MM-dd HH:mm"))
                };
                row.Checked = IsChecked(a);
                if (!inactive) row.ForeColor = Color.DimGray;
                lv.Items.Add(row);
            }
            lv.EndUpdate();
            populating = false;
            UpdateTotal();
        }

        void UpdateTotal()
        {
            var sel = arts.Where(IsChecked).ToList();
            status.Text = "共 " + arts.Count + " 个，合计 " + Util.Fmt(arts.Where(a => a.Size > 0).Sum(a => a.Size)) +
                          "；已勾选 " + sel.Count + " 个，约 " + Util.Fmt(sel.Where(a => a.Size > 0).Sum(a => a.Size));
        }

        void SetBusy(bool b, string text, int max)
        {
            busy = b;
            btnScan.Enabled = btnClean.Enabled = !b;
            progress.Visible = b;
            if (b) { progress.Maximum = Math.Max(1, max); progress.Value = 0; }
            if (text != null) status.Text = text;
        }

        void Scan()
        {
            if (busy) return;
            UpdateHeader();
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var cfg = app.Cfg;
            SetBusy(true, "正在查找项目产物…", 1);
            Task.Factory.StartNew(() =>
            {
                var sw = Stopwatch.StartNew();
                var found = Projects.Find(cfg.ProjectRoots, 8, ct);
                var act = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in found)
                {
                    DateTime t;
                    if (!act.TryGetValue(a.Project, out t)) { t = Projects.LastActive(a.Project); act[a.Project] = t; }
                    a.LastActive = t;
                }
                UI(() => { arts = found; progress.Maximum = Math.Max(1, found.Count); Populate(); status.Text = "找到 " + found.Count + " 个，正在计算大小…"; });
                int n = 0;
                Parallel.ForEach(found, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, a =>
                {
                    a.Size = Fs.DirSize(a.Path, null, ct);
                    int k = Interlocked.Increment(ref n);
                    if (k % 4 == 0 || k == found.Count)
                        UI(() => { progress.Value = Math.Min(progress.Maximum, k); status.Text = "正在计算大小… " + k + "/" + found.Count; });
                });
                var secs = sw.Elapsed.TotalSeconds;
                UI(() =>
                {
                    arts = arts.OrderByDescending(x => x.Size).ToList();
                    SetBusy(false, null, 0);
                    Populate();
                    status.Text += "（扫描用时 " + secs.ToString("0") + " 秒）";
                });
            }, ct).ContinueWith(t =>
            {
                if (t.IsFaulted || t.IsCanceled)
                {
                    var msg = t.Exception != null ? t.Exception.GetBaseException().Message : "已取消";
                    UI(() => SetBusy(false, "扫描中断：" + msg, 0));
                }
            });
        }

        void DoClean()
        {
            var sel = arts.Where(IsChecked).ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "请先勾选要删除的目录。", "DiskGuard"); return; }
            long est = sel.Where(a => a.Size > 0).Sum(a => a.Size);
            var kinds = string.Join("、", sel.GroupBy(a => a.Kind).Select(g => g.Key + " × " + g.Count()));
            var venv = sel.Any(a => a.Kind.Contains("env"));
            var msg = "将删除 " + sel.Count + " 个目录，共约 " + Util.Fmt(est) + "（" + kinds + "）。\n\n" +
                      "这些都是可以重新生成的构建产物，需要时在项目里重新执行 npm install、mvn package、cargo build 等即可恢复。" +
                      (venv ? "\nPython 虚拟环境要用 requirements.txt 之类的依赖清单重建，没有清单的请先取消勾选。" : "") +
                      "\n\n确认删除？";
            if (MessageBox.Show(this, msg, "确认删除项目产物", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            var cfg = app.Cfg;
            var items = sel.Select(a => new CleanItem
            {
                Name = a.Kind + "  " + a.Path,
                Clean = log =>
                {
                    if (!Projects.StillArtifact(a.Path, cfg)) { log.AppendLine("  拒绝：复核不通过（不在项目根目录下或项目标记已不存在）"); return 0; }
                    return Fs.DeleteTree(a.Path, log);
                }
            }).ToList();
            SetBusy(true, "正在删除…", items.Count);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string summary;
                Cleaner.Run(items, cfg, "项目产物清理", name => UI(() => status.Text = "正在删除：" + name),
                    (i, f) => UI(() => { progress.Value = Math.Min(progress.Maximum, i + 1); }), out summary);
                UI(() =>
                {
                    arts = arts.Where(a => Directory.Exists(a.Path)).ToList();
                    SetBusy(false, null, 0);
                    Populate();
                    status.Text = "完成：" + summary + "（详见清理日志）";
                    app.RefreshStats();
                    app.Notify("项目产物清理完成", summary, ToolTipIcon.Info, 1);
                });
            });
        }
    }

    // ======================= 页 3：空间分析 =======================
    class Entry { public string Name, Path; public bool IsDir, IsLink; public long Size = -1; public DateTime Modified; }

    class AnalyzePage : PageBase
    {
        readonly TrayApp app;
        readonly ListViewEx lv;
        readonly Label pathLabel, status;
        readonly Button btnUp;
        string current;
        bool bigMode;
        List<Entry> entries = new List<Entry>();
        CancellationTokenSource cts;
        readonly Dictionary<string, KeyValuePair<DateTime, List<Entry>>> cache = new Dictionary<string, KeyValuePair<DateTime, List<Entry>>>(StringComparer.OrdinalIgnoreCase);

        public AnalyzePage(TrayApp app)
        {
            this.app = app;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 2, 0, 0) };
            foreach (var d in app.Cfg.Drives) { var dd = d; bar.Controls.Add(Btn(d + ": 盘", (s, e) => Navigate(dd + ":\\", false))); }
            btnUp = Btn("上一级", (s, e) => { var p = ParentDir(current); if (p != null) Navigate(p, false); });
            bar.Controls.Add(btnUp);
            bar.Controls.Add(Btn("重新计算", (s, e) => { if (current != null) Navigate(current, true); }));
            bar.Controls.Add(Btn("查找大文件（≥1G）", (s, e) => FindBig()));
            bar.Controls.Add(Btn("在资源管理器中显示", (s, e) => { var en = Sel(); Util.Reveal(en != null ? en.Path : current); }));
            pathLabel = new Label { AutoSize = true, Padding = new Padding(8, 9, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            bar.Controls.Add(pathLabel);

            lv = new ListViewEx { Dock = DockStyle.Fill, ShowItemToolTips = true };
            lv.Columns.Add("名称", 420);
            lv.Columns.Add("大小", 100, HorizontalAlignment.Right);
            lv.Columns.Add("占比", 70, HorizontalAlignment.Right);
            lv.Columns.Add("类型", 70);
            lv.Columns.Add("修改时间", 140);
            lv.DoubleClick += (s, e) =>
            {
                var en = Sel();
                if (en == null) return;
                if (en.IsDir && !en.IsLink && !bigMode) Navigate(en.Path, false); else Util.Reveal(en.Path);
            };

            status = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(4, 6, 0, 0) };
            var tip = Hint("双击文件夹往下钻，找到占空间的地方。这里只看不删，要删请在资源管理器里自己处理，或者用前两页的清理功能。", 26);

            Controls.Add(lv);
            Controls.Add(tip);
            Controls.Add(bar);
            Controls.Add(status);
        }

        protected override void FirstShown() { if (current == null && app.Cfg.Drives.Length > 0) Navigate(app.Cfg.Drives[0] + ":\\", false); }

        static string ParentDir(string p) { try { var d = Directory.GetParent(p); return d == null ? null : d.FullName; } catch { return null; } }
        Entry Sel() { return lv.SelectedItems.Count == 0 ? null : (Entry)lv.SelectedItems[0].Tag; }
        static long Len(FileInfo f) { try { return f.Length; } catch { return 0; } }

        void Populate()
        {
            long total = entries.Where(x => x.Size > 0).Sum(x => x.Size);
            // 还有文件夹在计算时，分母不完整，占比会误导（例如单个文件显示 99.9%），先不显示
            bool pending = entries.Any(x => x.Size < 0);
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var en in entries)
            {
                string pct = !pending && en.Size >= 0 && total > 0 ? (en.Size * 100.0 / total).ToString("0.0") + "%" : "";
                var row = new ListViewItem(new[]
                {
                    en.Name, en.Size < 0 ? "计算中…" : Util.Fmt(en.Size), pct,
                    en.IsLink ? "链接" : en.IsDir ? "文件夹" : "文件", en.Modified == DateTime.MinValue ? "" : en.Modified.ToString("yyyy-MM-dd HH:mm")
                }) { Tag = en, ToolTipText = en.Path };
                if (en.IsLink) row.ForeColor = Color.DimGray;
                lv.Items.Add(row);
            }
            lv.EndUpdate();
        }

        void UpdateRow(Entry en)
        {
            foreach (ListViewItem r in lv.Items) if (r.Tag == en) { r.SubItems[1].Text = Util.Fmt(en.Size); break; }
        }

        public void Navigate(string path, bool force)
        {
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            current = path; bigMode = false;
            pathLabel.Text = path;
            btnUp.Enabled = ParentDir(path) != null;

            KeyValuePair<DateTime, List<Entry>> kv;
            if (!force && cache.TryGetValue(path, out kv) && DateTime.Now - kv.Key < TimeSpan.FromMinutes(10))
            {
                entries = kv.Value; Populate();
                status.Text = "合计 " + Util.Fmt(entries.Where(x => x.Size > 0).Sum(x => x.Size)) + "（" + (int)(DateTime.Now - kv.Key).TotalMinutes + " 分钟前的结果，点“重新计算”刷新）";
                return;
            }

            var list = new List<Entry>();
            try
            {
                foreach (var e in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    var f = e as FileInfo;
                    bool link = Fs.IsReparse(e);
                    if (f != null) list.Add(new Entry { Name = f.Name, Path = f.FullName, Size = Len(f), Modified = f.LastWriteTime });
                    else list.Add(new Entry { Name = e.Name, Path = e.FullName, IsDir = true, IsLink = link, Size = link ? 0 : -1, Modified = e.LastWriteTime });
                }
            }
            catch (Exception ex) { status.Text = "无法读取：" + ex.Message; }
            entries = list.OrderByDescending(x => x.Size).ToList();
            Populate();

            var dirs = entries.Where(x => x.IsDir && x.Size < 0).ToList();
            status.Text = "正在计算 " + dirs.Count + " 个文件夹…";
            var mine = entries;
            var sw = Stopwatch.StartNew();
            Task.Factory.StartNew(() =>
            {
                int n = 0;
                Parallel.ForEach(dirs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, d =>
                {
                    d.Size = Fs.DirSize(d.Path, null, ct);
                    int k = Interlocked.Increment(ref n);
                    UI(() => { if (ct.IsCancellationRequested) return; UpdateRow(d); status.Text = "正在计算… " + k + "/" + dirs.Count; });
                });
                UI(() =>
                {
                    if (ct.IsCancellationRequested || !ReferenceEquals(mine, entries)) return;
                    entries = entries.OrderByDescending(x => x.Size).ToList();
                    cache[path] = new KeyValuePair<DateTime, List<Entry>>(DateTime.Now, entries);
                    Populate();
                    status.Text = "合计 " + Util.Fmt(entries.Where(x => x.Size > 0).Sum(x => x.Size)) + "，用时 " + sw.Elapsed.TotalSeconds.ToString("0") + " 秒" +
                                  "（不进入链接；WinSxS 等硬链接会重复计算，系统盘显示偏大属正常；无权限的目录按 0 计）";
                });
            }, ct).ContinueWith(t => { var ignore = t.Exception; });
        }

        void FindBig()
        {
            if (current == null) return;
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            bigMode = true;
            var root = current;
            pathLabel.Text = root + "  里 ≥1G 的文件";
            entries = new List<Entry>();
            Populate();
            status.Text = "正在查找…";
            Task.Factory.StartNew(() =>
            {
                var found = new List<Entry>();
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(root));
                int dirs = 0;
                while (stack.Count > 0 && !ct.IsCancellationRequested)
                {
                    var d = stack.Pop();
                    dirs++;
                    try
                    {
                        foreach (var e in d.EnumerateFileSystemInfos())
                        {
                            if (Fs.IsReparse(e)) continue;
                            var f = e as FileInfo;
                            if (f != null) { long l = Len(f); if (l >= Util.GB) found.Add(new Entry { Name = f.FullName, Path = f.FullName, Size = l, Modified = f.LastWriteTime }); }
                            else stack.Push((DirectoryInfo)e);
                        }
                    }
                    catch { }
                    if (dirs % 2000 == 0) { int dd = dirs, ff = found.Count; UI(() => { if (!ct.IsCancellationRequested) status.Text = "正在查找… 已扫 " + dd + " 个文件夹，找到 " + ff + " 个"; }); }
                }
                int total = dirs;
                UI(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    entries = found.OrderByDescending(x => x.Size).Take(300).ToList();
                    Populate();
                    status.Text = "扫了 " + total + " 个文件夹，找到 " + found.Count + " 个 ≥1G 的文件，合计 " + Util.Fmt(found.Sum(x => x.Size)) + "。双击在资源管理器中定位。";
                });
            }, ct).ContinueWith(t => { var ignore = t.Exception; });
        }
    }

    // ======================= 页 4：Docker =======================
    class DockerPage : PageBase
    {
        readonly TrayApp app;
        readonly Label info;
        readonly TextBox log;
        readonly List<Button> actions = new List<Button>();
        bool busy;

        public DockerPage(TrayApp app)
        {
            this.app = app;
            info = new Label { Dock = DockStyle.Top, Height = 150, Padding = new Padding(4, 6, 4, 0), Text = "正在读取…" };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
            actions.Add(Btn("刷新", (s, e) => RefreshInfo()));
            actions.Add(Btn("清理构建缓存 + 悬空镜像", (s, e) => RunOp("清理构建缓存和悬空镜像", "会删除所有构建缓存和没有标签的镜像，不动容器和卷，不用停 Docker。", c => { DockerOps.Prune(c, Log); })));
            actions.Add(Btn("在线 TRIM", (s, e) => RunOp("在线 TRIM", "让 Docker 磁盘里的文件系统把已删除的空间标记出来，不停容器。做完后还要“一键回收”压缩才会还给磁盘。", c => { DockerOps.Trim(c, Log); })));
            actions.Add(Btn("一键回收（会停 Docker）", (s, e) => Reclaim()));
            actions.Add(Btn("列出未用卷（只读）", (s, e) => ListVolumes()));
            foreach (var b in actions) bar.Controls.Add(b);
            bar.Controls.Add(Btn("打开 Docker 日志", (s, e) => { var f = Path.Combine(Util.LogDir, "docker.log"); Util.Reveal(File.Exists(f) ? f : Util.LogDir); }));

            var tip = Hint("在 Docker 里删东西只是在虚拟磁盘内部腾出空间，docker_data.vhdx 文件不会自己变小。“一键回收”会依次：在线 TRIM → 停止 Docker 和 WSL → 压缩虚拟磁盘 → 重新启动 Docker，并把原来运行中的容器全部拉起来（包括 restart=no 的）。" +
                           "期间所有容器会停一段时间。卷里可能有数据库数据，这里只列出，不提供删除。", 44);
            log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.White };

            Controls.Add(log);
            Controls.Add(tip);
            Controls.Add(bar);
            Controls.Add(info);
        }

        protected override void FirstShown() { RefreshInfo(); }

        void Log(string s)
        {
            var line = Util.Ts() + "  " + s;
            Util.AppendLog("docker.log", line + Environment.NewLine);
            UI(() => { log.AppendText(line + Environment.NewLine); });
        }

        void SetBusy(bool b) { busy = b; foreach (var a in actions) a.Enabled = !b; UseWaitCursor = b; }

        public void RefreshInfo()
        {
            if (busy) return;
            SetBusy(true);
            info.Text = "正在读取 Docker 信息…";
            var cfg = app.Cfg;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                DockerInfo i;
                try { i = DockerOps.Read(cfg); } catch (Exception e) { i = new DockerInfo { Error = e.Message }; }
                UI(() => { info.Text = Format(i, cfg); SetBusy(false); });
            });
        }

        static string N(int v) { return v < 0 ? "—" : v.ToString(); }

        static string Format(DockerInfo i, Config c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("虚拟磁盘文件：" + c.DockerVhdx + "    " + Util.Fmt(i.VhdxSize));
            if (i.Error != null) { sb.AppendLine(i.Error); return sb.ToString(); }
            sb.Append("磁盘里实际使用：" + Util.Fmt(i.Used));
            if (i.Used > 0 && i.VhdxSize > 0) sb.Append("    → 一键回收最多能还给 " + Path.GetPathRoot(c.DockerVhdx) + " 约 " + Util.Fmt(Math.Max(0, i.VhdxSize - i.Used)));
            sb.AppendLine();
            sb.AppendLine("构建缓存（可清）：" + Util.Fmt(i.BuildCache) + "    镜像 " + N(i.Images) + " 个（悬空 " + N(i.Dangling) + " 个）    卷 " + N(i.Volumes) + " 个（没有容器在用 " + N(i.DanglingVolumes) + " 个）");
            sb.AppendLine("运行中的容器（" + i.Running.Count + "）：" + (i.Running.Count == 0 ? "无" : string.Join(", ", i.Running)));
            if (!Util.IsAdmin) sb.AppendLine("当前不是管理员：一键回收需要先在托盘菜单点“以管理员身份重新启动”。");
            return sb.ToString();
        }

        void RunOp(string title, string desc, Action<Config> op)
        {
            if (busy) return;
            if (MessageBox.Show(this, desc + "\n\n确认执行“" + title + "”？", "DiskGuard", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            var cfg = app.Cfg;
            Log("==== " + title + " ====");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { op(cfg); } catch (Exception e) { Log("出错：" + e.Message); }
                UI(() => { SetBusy(false); RefreshInfo(); app.RefreshStats(); });
            });
        }

        void Reclaim()
        {
            if (busy) return;
            if (!Util.IsAdmin)
            {
                MessageBox.Show(this, "压缩虚拟磁盘需要管理员权限。请在托盘图标右键菜单点“以管理员身份重新启动”，再回到这里操作。", "DiskGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var cfg = app.Cfg;
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var running = DockerOps.EngineUp(cfg) ? DockerOps.Running(cfg) : new List<string>();
                UI(() =>
                {
                    var msg = "将按顺序执行：\n1. 在线 TRIM\n2. 停止 Docker Desktop 和 WSL —— 下面这些容器会暂时停止：\n   " +
                              (running.Count == 0 ? "（当前没有运行中的容器）" : string.Join("\n   ", running)) +
                              "\n3. 用 diskpart 压缩 docker_data.vhdx（可能要几十分钟）\n4. 重新启动 Docker，并把上面这些容器全部拉起来\n\n期间 Docker 不可用，也不要手动启动 Docker Desktop。确认开始？";
                    if (MessageBox.Show(this, msg, "一键回收 Docker 磁盘空间", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) { SetBusy(false); return; }
                    Log("==== 一键回收 ====");
                    ThreadPool.QueueUserWorkItem(__ =>
                    {
                        try { DockerOps.Reclaim(cfg, Log); } catch (Exception e) { Log("出错：" + e.Message); }
                        UI(() => { SetBusy(false); RefreshInfo(); app.RefreshStats(); app.Notify("Docker 回收完成", "详情见 DiskGuard 的 Docker 页", ToolTipIcon.Info, 3); });
                    });
                });
            });
        }

        void ListVolumes()
        {
            if (busy) return;
            SetBusy(true);
            var cfg = app.Cfg;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var v = DockerOps.EngineUp(cfg) ? DockerOps.DanglingVolumes(cfg) : null;
                UI(() =>
                {
                    SetBusy(false);
                    if (v == null) { Log("Docker 引擎没有运行"); return; }
                    Log("没有容器在用的卷共 " + v.Count + " 个（只列出，不删除；确认不要后可以用 docker volume rm <名字> 删除）：");
                    foreach (var x in v) Log("  " + x);
                });
            });
        }
    }

    // ======================= 页 5：空间趋势 =======================
    class TrendPanel : Panel
    {
        public Func<Config> Cfg;
        public TimeSpan Range = TimeSpan.FromDays(7);
        static readonly Color[] Pal = { Color.FromArgb(0, 122, 255), Color.FromArgb(175, 82, 222), Color.FromArgb(255, 149, 0), Color.FromArgb(48, 176, 199) };

        public TrendPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cfg = Cfg();
            var t0 = DateTime.Now - Range;
            var data = History.Snapshot().Where(s => s.T >= t0).ToList();
            var plot = new Rectangle(64, 30, Width - 90, Height - 66);
            if (plot.Width < 80 || plot.Height < 60) return;
            if (data.Count < 2)
            {
                TextRenderer.DrawText(g, "还没有足够的数据：每 10 分钟记录一次，程序运行一段时间后这里会出现曲线。", Font, plot, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            double lo = data.Min(s => s.Free) / (double)Util.GB, hi = data.Max(s => s.Free) / (double)Util.GB;
            double pad = Math.Max(1, (hi - lo) * 0.15);
            lo = Math.Max(0, lo - pad); hi = hi + pad;
            var rangeSec = Range.TotalSeconds;
            Func<DateTime, float> X = t => plot.Left + (float)((t - t0).TotalSeconds / rangeSec * plot.Width);
            Func<double, float> Y = v => plot.Bottom - (float)((v - lo) / (hi - lo) * plot.Height);

            using (var grid = new Pen(Color.FromArgb(229, 229, 234)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    double v = lo + (hi - lo) * i / 4;
                    float y = Y(v);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, v.ToString("0") + " G", Font, new Rectangle(0, (int)y - 9, plot.Left - 6, 18), Color.Gray, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
                for (int i = 0; i <= 4; i++)
                {
                    var t = t0 + TimeSpan.FromTicks(Range.Ticks * i / 4);
                    float x = X(t);
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                    var lab = Range.TotalDays <= 1.01 ? t.ToString("HH:mm") : t.ToString("MM-dd HH:mm");
                    // 两端的刻度标签收进绘图区内，避免最右侧的日期被窗口边缘截断
                    int lw = TextRenderer.MeasureText(g, lab, Font).Width;
                    int lx0 = Math.Max(0, Math.Min((int)x - lw / 2, Width - lw - 4));
                    TextRenderer.DrawText(g, lab, Font, new Point(lx0, plot.Bottom + 6), Color.Gray);
                }
            }
            foreach (var th in new[] { new KeyValuePair<double, Color>(cfg.WarnGB, Util.LevelColor(1)), new KeyValuePair<double, Color>(cfg.CritGB, Util.LevelColor(2)) })
            {
                if (th.Key <= lo || th.Key >= hi) continue;
                using (var p = new Pen(th.Value, 1f) { DashStyle = DashStyle.Dash }) g.DrawLine(p, plot.Left, Y(th.Key), plot.Right, Y(th.Key));
            }
            int ci = 0, lx = plot.Left;
            foreach (var d in cfg.Drives)
            {
                var col = Pal[ci++ % Pal.Length];
                var pts = data.Where(s => s.D == d).OrderBy(s => s.T).Select(s => new PointF(X(s.T), Y(s.Free / (double)Util.GB))).ToArray();
                if (pts.Length >= 2) using (var p = new Pen(col, 2f)) g.DrawLines(p, pts);
                else if (pts.Length == 1) using (var b = new SolidBrush(col)) g.FillEllipse(b, pts[0].X - 3, pts[0].Y - 3, 6, 6);
                using (var b = new SolidBrush(col)) g.FillRectangle(b, lx, 10, 12, 12);
                TextRenderer.DrawText(g, d + ": 可用空间", Font, new Point(lx + 16, 8), ForeColor);
                lx += 110;
            }
        }
    }

    class TrendPage : PageBase
    {
        readonly TrayApp app;
        readonly TrendPanel chart;
        readonly Label forecast;

        public TrendPage(TrayApp app)
        {
            this.app = app;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            chart = new TrendPanel { Dock = DockStyle.Fill, Cfg = () => app.Cfg };
            foreach (var r in new[] { new KeyValuePair<string, double>("24 小时", 1), new KeyValuePair<string, double>("7 天", 7), new KeyValuePair<string, double>("30 天", 30) })
            {
                var days = r.Value;
                var rb = new RadioButton { Text = r.Key, AutoSize = true, Checked = days == 7, Margin = new Padding(6, 8, 6, 3) };
                rb.CheckedChanged += (s, e) => { if (rb.Checked) { chart.Range = TimeSpan.FromDays(days); RefreshView(); } };
                bar.Controls.Add(rb);
            }
            forecast = new Label { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(6, 6, 0, 0) };
            // 行数随盘符数量变化：按文字实际高度调整，避免最后一行被截断
            forecast.TextChanged += (s, e) =>
            {
                int w = Math.Max(100, forecast.ClientSize.Width - forecast.Padding.Horizontal);
                var sz = TextRenderer.MeasureText(forecast.Text, forecast.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                forecast.Height = Math.Max(60, sz.Height + forecast.Padding.Vertical + 8);
            };
            Controls.Add(chart);
            Controls.Add(bar);
            Controls.Add(forecast);
        }

        protected override void FirstShown() { RefreshView(); }

        public void RefreshView()
        {
            chart.Invalidate();
            var sb = new StringBuilder();
            foreach (var d in app.Stats.Where(x => x.Ready))
                sb.AppendLine(d.Name + ": 现在可用 " + Util.Fmt(d.Free) + "，所选区间 " + History.Forecast(d.Name, chart.Range, d.Free));
            sb.Append("（每 10 分钟记录一次，保存在 logs\\history.csv，保留 30 天）");
            forecast.Text = sb.ToString();
        }
    }

    // ======================= 主窗口 =======================
    class MainForm : Form
    {
        readonly TabControl tabs;
        readonly DrivePanel drives;
        public readonly TrendPage Trend;
        public bool AllowClose;

        public MainForm(TrayApp app)
        {
            Text = "DiskGuard" + (Util.IsAdmin ? "（管理员）" : "");
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1080, 700);
            MinimumSize = new Size(780, 500);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            drives = new DrivePanel { Dock = DockStyle.Top, Source = () => app.Stats, Cfg = () => app.Cfg };
            drives.Height = 12 + DrivePanel.RowH * Math.Max(1, app.Cfg.Drives.Length);
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            Trend = new TrendPage(app);
            tabs.TabPages.Add(Wrap("安全清理", new CleanPage(app)));
            tabs.TabPages.Add(Wrap("项目产物", new ProjectsPage(app)));
            tabs.TabPages.Add(Wrap("空间分析", new AnalyzePage(app)));
            tabs.TabPages.Add(Wrap("Docker", new DockerPage(app)));
            tabs.TabPages.Add(Wrap("空间趋势", Trend));
            Controls.Add(tabs);
            Controls.Add(drives);
        }

        static TabPage Wrap(string title, Control c)
        {
            var p = new TabPage(title) { Padding = new Padding(6) };
            c.Dock = DockStyle.Fill;
            p.Controls.Add(c);
            return p;
        }

        public void ShowTab(int i) { if (i >= 0 && i < tabs.TabCount) tabs.SelectedIndex = i; }
        public void RefreshDrives() { drives.Invalidate(); if (Trend.Visible) Trend.RefreshView(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 关闭只是隐藏，保留扫描结果；托盘菜单“退出”才真正关闭
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }
    }

    // ======================= 托盘 =======================
    class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public Config Cfg;
        readonly NotifyIcon ni;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly Control invoker;
        readonly EventWaitHandle showEvt;
        readonly Dictionary<string, KeyValuePair<DateTime, long>> cache = new Dictionary<string, KeyValuePair<DateTime, long>>();
        List<DriveStat> stats = new List<DriveStat>();
        int lastLevel, balloonTab;
        DateTime lastCritNotify = DateTime.MinValue, lastDropNotify = DateTime.MinValue;
        IntPtr lastHicon = IntPtr.Zero;
        MainForm form;
        bool autoRunning;

        public List<DriveStat> Stats { get { return stats; } }

        public TrayApp()
        {
            Cfg = Config.Load();
            History.Load();
            invoker = new Control();
            invoker.CreateControl();
            var unused = invoker.Handle;

            menu = new ContextMenuStrip();
            menu.Opening += (s, e) => BuildMenu();
            ni = new NotifyIcon { ContextMenuStrip = menu, Visible = false };
            ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenMain(0); };
            ni.BalloonTipClicked += (s, e) => OpenMain(balloonTab);
            RefreshStats();
            BuildMenu();
            ni.Visible = true;

            timer = new System.Windows.Forms.Timer { Interval = Cfg.IntervalSec * 1000 };
            timer.Tick += (s, e) => RefreshStats();
            timer.Start();

            try
            {
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
                ThreadPool.RegisterWaitForSingleObject(showEvt, (st, to) => Post(() => OpenMain(0)), null, -1, false);
            }
            catch { }
        }

        public void Post(Action a) { try { invoker.BeginInvoke(a); } catch { } }

        public bool CacheGet(string name, out long v)
        {
            lock (cache)
            {
                KeyValuePair<DateTime, long> e;
                if (cache.TryGetValue(name, out e) && DateTime.Now - e.Key < TimeSpan.FromMinutes(5)) { v = e.Value; return true; }
            }
            v = -1;
            return false;
        }

        public void CacheSet(string name, long v) { lock (cache) cache[name] = new KeyValuePair<DateTime, long>(DateTime.Now, v); }
        public void CacheRemove(string name) { lock (cache) cache.Remove(name); }

        public void Notify(string title, string text, ToolTipIcon icon, int tab)
        {
            balloonTab = tab;
            try { ni.ShowBalloonTip(8000, title, text.Length > 250 ? text.Substring(0, 250) : text, icon); } catch { }
        }

        public void RefreshStats()
        {
            stats = DriveStat.Read(Cfg);
            History.Record(stats);
            Watch.MaybeSnapshot(Cfg);

            int level = 0; DriveStat worst = null;
            foreach (var s in stats)
            {
                if (!s.Ready) continue;
                int l = s.Level(Cfg);
                if (worst == null || l > level || (l == level && s.Free < worst.Free)) { worst = s; level = l; }
            }
            SetIcon(level, worst == null ? "?" : Math.Min(999, (int)Math.Floor(worst.FreeGB)).ToString());
            var tip = "DiskGuard  " + string.Join("  ", stats.Where(s => s.Ready).Select(s => s.Name + ":" + s.FreeGB.ToString("0.0") + "G")) + " 可用";
            ni.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (worst != null && level > 0)
            {
                bool rose = level > lastLevel;
                bool repeat = level == 2 && DateTime.Now - lastCritNotify > TimeSpan.FromHours(2);
                if (rose || repeat)
                {
                    Notify(level == 2 ? "磁盘空间严重不足" : "磁盘空间偏低", worst.Name + ": 盘只剩 " + Util.Fmt(worst.Free) + "，点这里打开安全清理",
                           level == 2 ? ToolTipIcon.Error : ToolTipIcon.Warning, 0);
                    if (level == 2) lastCritNotify = DateTime.Now;
                }
            }
            lastLevel = level;

            if (DateTime.Now - lastDropNotify > TimeSpan.FromHours(1))
            {
                foreach (var s in stats)
                {
                    long delta; TimeSpan span;
                    if (s.Ready && History.Change(s.Name, TimeSpan.FromHours(1), s.Free, out delta, out span)
                        && span.TotalMinutes >= 20 && delta < -(long)(Cfg.DropAlertGB * Util.GB))
                    {
                        lastDropNotify = DateTime.Now;
                        string name = s.Name; int mins = (int)span.TotalMinutes; long drop = -delta;
                        Watch.Explain(Cfg, text => Post(() =>
                            Notify("磁盘空间下降很快", name + ": 盘 " + mins + " 分钟内少了 " + Util.Fmt(drop) + "。" + text, ToolTipIcon.Warning, 2)));
                        break;
                    }
                }
            }

            CheckAutoClean();
            if (form != null && !form.IsDisposed && form.Visible) form.RefreshDrives();
        }

        DateTime NextAutoClean()
        {
            var last = State.GetTime("lastAutoClean");
            if (last == DateTime.MinValue) last = DateTime.Now;
            return last.Date.AddDays(Cfg.AutoCleanDays).AddHours(Cfg.AutoCleanHour);
        }

        void CheckAutoClean()
        {
            if (Cfg.AutoCleanDays <= 0 || autoRunning) return;
            if (State.GetTime("lastAutoClean") == DateTime.MinValue) { State.SetTime("lastAutoClean", DateTime.Now); return; }
            if (DateTime.Now < NextAutoClean()) return;
            autoRunning = true;
            State.SetTime("lastAutoClean", DateTime.Now);
            var cfg = Cfg;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string summary = "";
                try
                {
                    var items = Items.Build(cfg).Where(i => i.DefaultOn).ToList();
                    Cleaner.Run(items, cfg, "定时自动清理（放心清）", null, null, out summary);
                }
                catch (Exception e) { summary = "出错：" + e.Message; }
                Post(() => { autoRunning = false; RefreshStats(); Notify("定时自动清理完成", summary, ToolTipIcon.Info, 0); });
            });
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void SetIcon(int level, string text)
        {
            var sz = SystemInformation.SmallIconSize;
            using (var bmp = new Bitmap(sz.Width, sz.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (var path = Rounded(new RectangleF(0, 0, sz.Width - 1, sz.Height - 1), sz.Width / 4f))
                    using (var b = new SolidBrush(Util.LevelColor(level)))
                        g.FillPath(b, path);
                    float fs = sz.Height * (text.Length >= 3 ? 0.44f : 0.6f);
                    using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, f, Brushes.White, new RectangleF(0, 0, sz.Width, sz.Height + 1), sf);
                }
                IntPtr h = bmp.GetHicon();
                ni.Icon = Icon.FromHandle(h);
                if (lastHicon != IntPtr.Zero) DestroyIcon(lastHicon);
                lastHicon = h;
            }
        }

        ToolStripMenuItem Add(string text, EventHandler onClick, bool bold)
        {
            var it = new ToolStripMenuItem(text);
            it.Click += onClick;
            if (bold) it.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(it);
            return it;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            foreach (var s in stats)
            {
                string t = s.Ready && s.Total > 0
                    ? string.Format("{0}:  可用 {1} / {2}（已用 {3:0}%）{4}", s.Name, Util.Fmt(s.Free), Util.Fmt(s.Total), 100.0 * (s.Total - s.Free) / s.Total, History.ChangeText(s))
                    : s.Name + ":  不可用";
                menu.Items.Add(new ToolStripMenuItem(t) { Enabled = false });
            }
            menu.Items.Add(new ToolStripSeparator());
            Add("安全清理…", (s, e) => OpenMain(0), true);
            Add("项目产物…", (s, e) => OpenMain(1), false);
            Add("空间分析…", (s, e) => OpenMain(2), false);
            Add("Docker…", (s, e) => OpenMain(3), false);
            Add("空间趋势…", (s, e) => OpenMain(4), false);
            menu.Items.Add(new ToolStripSeparator());
            Add("立即刷新", (s, e) => RefreshStats(), false);
            Add("打开日志目录", (s, e) => Util.Reveal(File.Exists(Util.LogFile) ? Util.LogFile : Util.LogDir), false);
            Add("编辑配置", (s, e) => { try { Process.Start("notepad.exe", "\"" + Config.FilePath + "\""); } catch { } }, false);
            Add("重新加载配置", (s, e) => { Cfg = Config.Load(); timer.Interval = Cfg.IntervalSec * 1000; lastLevel = 0; RefreshStats(); }, false);
            menu.Items.Add(new ToolStripSeparator());

            var autoText = Cfg.AutoCleanDays > 0
                ? "定时自动清理：每 " + Cfg.AutoCleanDays + " 天 " + Cfg.AutoCleanHour + " 点（下次 " + NextAutoClean().ToString("MM-dd HH:mm") + "）"
                : "定时自动清理（每 7 天，只清“放心清”）";
            var auto = Add(autoText, (s, e) =>
            {
                try
                {
                    Config.SetValue("autoCleanDays", Cfg.AutoCleanDays > 0 ? "0" : "7");
                    State.SetTime("lastAutoClean", DateTime.Now);
                    Cfg = Config.Load();
                }
                catch (Exception ex) { MessageBox.Show("保存配置失败：" + ex.Message, "DiskGuard"); }
            }, false);
            auto.Checked = Cfg.AutoCleanDays > 0;

            var pin = Add("图标常驻任务栏", (s, e) =>
            {
                int st = TrayPin.State();
                if (st < 0) { MessageBox.Show("Windows 还没登记这个图标，过一会再试；也可以在 设置 → 个性化 → 任务栏 → 其他系统托盘图标 里打开 DiskGuard。", "DiskGuard"); return; }
                try { TrayPin.Set(st != 1); } catch (Exception ex) { MessageBox.Show("设置失败：" + ex.Message, "DiskGuard"); }
            }, false);
            pin.Checked = TrayPin.State() == 1;

            var boot = Add("开机自启", (s, e) =>
            {
                try { AutoStart.Set(!AutoStart.Enabled); }
                catch (Exception ex) { MessageBox.Show("设置开机自启失败：" + ex.Message, "DiskGuard"); }
            }, false);
            boot.Checked = AutoStart.Enabled;

            if (!Util.IsAdmin) Add("以管理员身份重新启动", (s, e) => RestartAsAdmin(), false);
            menu.Items.Add(new ToolStripSeparator());
            Add("退出", (s, e) => Quit(), false);
        }

        void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--wait-pid " + Process.GetCurrentProcess().Id) { UseShellExecute = true, Verb = "runas" });
                Quit();
            }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void OpenMain(int tab)
        {
            if (form == null || form.IsDisposed) form = new MainForm(this);
            form.ShowTab(tab);
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.TopMost = true; form.TopMost = false;
            form.Activate();
            form.RefreshDrives();
        }

        void Quit()
        {
            timer.Stop();
            ni.Visible = false;
            ni.Dispose();
            if (form != null && !form.IsDisposed) { form.AllowClose = true; form.Close(); }
            if (lastHicon != IntPtr.Zero) DestroyIcon(lastHicon);
            try { if (showEvt != null) showEvt.Dispose(); } catch { }
            ExitThread();
        }
    }

    // ======================= 自检（只扫描；删除逻辑只在临时沙箱里测） =======================
    static class SelfTest
    {
        static bool Check(StringBuilder sb, bool cond, string text)
        {
            sb.AppendLine((cond ? "OK   " : "FAIL ") + text);
            return cond;
        }

        public static int Run()
        {
            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                var cfg = Config.Load();
                History.Load();
                sb.AppendLine("version=v3 admin=" + Util.IsAdmin + " time=" + Util.Ts());
                sb.AppendLine("== drives ==");
                foreach (var d in DriveStat.Read(cfg))
                    sb.AppendLine(d.Name + ": free=" + Util.Fmt(d.Free) + " total=" + Util.Fmt(d.Total) + " level=" + d.Level(cfg) + History.ChangeText(d) + " | 7天趋势：" + History.Forecast(d.Name, TimeSpan.FromDays(7), d.Free));
                sb.AppendLine("history samples=" + History.Count + "  trayPin=" + TrayPin.State() + "  npmCache=" + Items.NpmCache() + "  projectRoots=" + string.Join(";", cfg.ProjectRoots));

                sb.AppendLine("== clean items（并行扫描，只扫描不清理）==");
                var items = Items.Build(cfg);
                var res = new string[items.Count];
                var wall = Stopwatch.StartNew();
                Parallel.For(0, items.Count, i =>
                {
                    var sw = Stopwatch.StartNew();
                    long s;
                    try { s = items[i].Scan(); } catch { s = -1; }
                    res[i] = string.Format("[{0}] {1,-10} {2}  ({3} ms, {4}, 位置 {5} 个)", items[i].Tier, Util.Fmt(s), items[i].Name, sw.ElapsedMilliseconds,
                        items[i].Clean == null ? "只建议" : items[i].DefaultOn ? "默认勾" : "默认不勾", items[i].Paths.Count(p => Directory.Exists(p) || File.Exists(p)));
                });
                foreach (var r in res) sb.AppendLine(r);
                sb.AppendLine("并行扫描总用时 " + wall.ElapsedMilliseconds + " ms");
                ok &= Check(sb, items.Where(i => i.Tier == Tier.Advice).All(i => i.Clean == null), "只建议的项都没有删除动作");

                sb.AppendLine("== delete safety（临时沙箱）==");
                ok &= DeleteSafety(sb);
                sb.AppendLine("== project classify（临时沙箱，正反例）==");
                ok &= ClassifyTests(sb, cfg);

                sb.AppendLine("== protected paths ==");
                foreach (var p in new[] { "C:\\", "E:\\", Util.UserProfile, Util.LocalAppData, Util.Roaming, Util.WinDir, Path.Combine(Util.UserProfile, "Downloads") })
                    ok &= Check(sb, Fs.IsProtected(p), "拒绝清空/删除 " + p);

                sb.AppendLine("== projects（只查找，不算大小）==");
                var psw = Stopwatch.StartNew();
                var arts = Projects.Find(cfg.ProjectRoots, 8, CancellationToken.None);
                sb.AppendLine("找到 " + arts.Count + " 个，用时 " + psw.ElapsedMilliseconds + " ms：" + string.Join("、", arts.GroupBy(a => a.Kind).OrderByDescending(g => g.Count()).Select(g => g.Key + "×" + g.Count())));
                ok &= Check(sb, arts.Count > 0, "项目产物扫描结果非空");
                ok &= Check(sb, arts.All(a => Projects.StillArtifact(a.Path, cfg)), "每个结果都能通过删除前复核");
                ok &= Check(sb, !arts.Any(a => a.Path.StartsWith(@"E:\develop", StringComparison.OrdinalIgnoreCase)), "不包含软件安装目录（E:\\develop）");
                foreach (var a in arts.Take(3))
                    sb.AppendLine("  样例 " + a.Kind + "  " + a.Path + "  最近改动 " + Projects.LastActive(a.Project).ToString("yyyy-MM-dd"));

                sb.AppendLine("== docker（只读）==");
                var di = DockerOps.Read(cfg);
                sb.AppendLine("engine=" + di.EngineUp + " vhdx=" + Util.Fmt(di.VhdxSize) + " used=" + Util.Fmt(di.Used) + " buildCache=" + Util.Fmt(di.BuildCache) +
                              " images=" + di.Images + " dangling=" + di.Dangling + " volumes=" + di.Volumes + " danglingVolumes=" + di.DanglingVolumes +
                              " running=" + di.Running.Count + " " + (di.Error ?? ""));
            }
            catch (Exception ex) { ok = false; sb.AppendLine("EXCEPTION " + ex); }
            sb.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            try { File.WriteAllText(Path.Combine(Util.LogDir, "selftest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch { }
            return ok ? 0 : 1;
        }

        static string LegacySwitch()
        {
            bool v;
            return AppContext.TryGetSwitch("Switch.System.IO.UseLegacyPathHandling", out v) ? v.ToString() : "unset";
        }

        static bool Junction(string link, string target)
        {
            int ex;
            Util.Run("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"", 15000, out ex);
            return Directory.Exists(link);
        }

        static bool DeleteSafety(StringBuilder sb)
        {
            bool ok = true;
            string root = Path.Combine(Path.GetTempPath(), "dg_selftest_" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
            var log = new StringBuilder();

            // 1) EmptyDir：联接目标保留、只读文件、按时间过滤
            string work = Path.Combine(root, "work");
            Directory.CreateDirectory(Path.Combine(work, "sub"));
            File.WriteAllText(Path.Combine(work, "a.txt"), "a");
            File.WriteAllText(Path.Combine(work, "sub", "b.txt"), "b");
            var ro = Path.Combine(work, "ro.txt");
            File.WriteAllText(ro, "ro");
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            ok &= Check(sb, Junction(Path.Combine(work, "link"), outside), "创建测试联接 work\\link -> outside");
            Fs.EmptyDir(work, null, log);
            ok &= Check(sb, File.Exists(Path.Combine(outside, "keep.txt")), "清空目录：联接目标里的文件保留");
            ok &= Check(sb, Directory.GetFileSystemEntries(work).Length == 0, "清空目录：内容已清空（含只读文件和联接本身）");
            string oldF = Path.Combine(work, "old.txt"), newF = Path.Combine(work, "new.txt");
            File.WriteAllText(oldF, "o"); File.WriteAllText(newF, "n");
            File.SetLastWriteTime(oldF, DateTime.Now.AddDays(-2));
            Fs.EmptyDir(work, TimeSpan.FromDays(1), log);
            ok &= Check(sb, !File.Exists(oldF) && File.Exists(newF), "按时间过滤：2 天前的删除，新的保留");

            // 2) DeleteTree：整个 node_modules 删除，里面的联接不跟随；超长路径
            string nm = Path.Combine(root, "proj", "node_modules");
            Directory.CreateDirectory(Path.Combine(nm, "pkg"));
            File.WriteAllText(Path.Combine(nm, "pkg", "index.js"), "x");
            ok &= Check(sb, Junction(Path.Combine(nm, ".pnpm-link"), outside), "创建 node_modules 内的测试联接");
            string deep = nm;
            for (int i = 0; i < 25; i++) deep = Path.Combine(deep, "deep_folder_" + i.ToString("00"));
            bool longOk = false;
            try
            {
                var xd = Fs.Ext(deep);
                Directory.CreateDirectory(xd);
                File.WriteAllText(Path.Combine(xd, "long.txt"), "x");
                bool ex1 = File.Exists(Path.Combine(xd, "long.txt"));
                bool ex2 = Directory.Exists(xd);
                long sz = Fs.DirSize(deep, null);
                long szRoot = Fs.DirSize(nm, null);
                sb.AppendLine("  诊断 ext=" + xd.Substring(0, 12) + "… fileExists=" + ex1 + " dirExists=" + ex2 + " sizeDeep=" + sz + " sizeNm=" + szRoot +
                              " legacyPath=" + LegacySwitch());
                longOk = ex1 && sz == 1;
            }
            catch (Exception e) { sb.AppendLine("WARN 创建长路径失败：" + e.Message); }
            ok &= Check(sb, longOk, "创建超长路径并统计大小（" + Path.Combine(deep, "long.txt").Length + " 字符）");
            Fs.DeleteTree(nm, log);
            ok &= Check(sb, !Directory.Exists(nm), "删除整个目录：node_modules 已不存在" + (longOk ? "（含超长路径）" : ""));
            ok &= Check(sb, File.Exists(Path.Combine(outside, "keep.txt")), "删除整个目录：联接目标里的文件保留");
            var log2 = new StringBuilder();
            Fs.DeleteTree("C:\\", log2);
            ok &= Check(sb, log2.ToString().Contains("拒绝"), "删除整个目录：拒绝 C:\\");

            try { Directory.Delete(root, true); } catch (Exception e) { sb.AppendLine("WARN 沙箱清理失败：" + e.Message); }
            return ok;
        }

        static bool ClassifyTests(StringBuilder sb, Config cfg)
        {
            bool ok = true;
            string root = Path.Combine(Path.GetTempPath(), "dg_classify_" + Guid.NewGuid().ToString("N"));
            Func<string, string[], string[], string> mk = (name, files, dirs) =>
            {
                var p = Path.Combine(root, name);
                Directory.CreateDirectory(p);
                foreach (var f in files) File.WriteAllText(Path.Combine(p, f), "{}");
                foreach (var d in dirs) Directory.CreateDirectory(Path.Combine(p, d));
                return p;
            };
            var a = mk("js", new[] { "package.json" }, new[] { "node_modules", "build", "dist" });
            var b = mk("nomarker", new string[0], new[] { "node_modules", "target", "venv" });
            var c = mk("mvn", new[] { "pom.xml" }, new[] { "target" });
            var d2 = mk("gradle", new[] { "build.gradle" }, new[] { "build", ".gradle" });
            var e = mk("py", new string[0], new[] { "venv", "env" });
            File.WriteAllText(Path.Combine(e, "venv", "pyvenv.cfg"), "home=x");

            ok &= Check(sb, Projects.Classify(a, "node_modules", Path.Combine(a, "node_modules")) == "node_modules", "package.json + node_modules → 认定");
            ok &= Check(sb, Projects.Classify(a, "build", Path.Combine(a, "build")) == null, "package.json + build → 不认定（可能是 vue-cli 构建脚本）");
            ok &= Check(sb, Projects.Classify(a, "dist", Path.Combine(a, "dist")) == "dist" && !Projects.DefaultKind("dist"), "dist → 认定但默认不勾");
            ok &= Check(sb, Projects.Classify(b, "node_modules", Path.Combine(b, "node_modules")) == null, "没有 package.json 的 node_modules → 不认定");
            ok &= Check(sb, Projects.Classify(b, "target", Path.Combine(b, "target")) == null, "没有 pom.xml 的 target → 不认定");
            ok &= Check(sb, Projects.Classify(b, "venv", Path.Combine(b, "venv")) == null, "没有 pyvenv.cfg 的 venv → 不认定");
            ok &= Check(sb, Projects.Classify(c, "target", Path.Combine(c, "target")) == "target", "pom.xml + target → 认定");
            ok &= Check(sb, Projects.Classify(d2, "build", Path.Combine(d2, "build")) == "build", "build.gradle + build → 认定");
            ok &= Check(sb, Projects.Classify(e, "venv", Path.Combine(e, "venv")) == "venv", "有 pyvenv.cfg 的 venv → 认定");
            ok &= Check(sb, Projects.Classify(e, "env", Path.Combine(e, "env")) == null, "没有 pyvenv.cfg 的 env → 不认定");
            ok &= Check(sb, !Projects.StillArtifact(Path.Combine(a, "node_modules"), cfg), "不在 projectRoot 下的目录 → 删除前复核拒绝");
            try { Directory.Delete(root, true); } catch { }
            return ok;
        }
    }
}
