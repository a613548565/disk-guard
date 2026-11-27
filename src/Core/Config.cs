// DiskGuard v5 - 配置（含分级自动清理策略、白名单）、通用工具、自启、Win11 托盘常驻与日志汇总
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskGuard
{
    public enum ThemeMode { Auto, Dark, Light }

    public class Config
    {
        public bool AutoDiscoverDrives = true;
        public string[] Drives = new[] { "C", "D", "E" };
        public double WarnGB = 15;
        public double CritGB = 8;
        public double DropAlertGB = 5;
        public int IntervalSec = 60;
        public string DockerExe = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe";
        public ThemeMode Theme = ThemeMode.Auto;

        // 智能自动清理策略（严格仅限 L1 安全白名单，绝不碰 L2/L3、Docker 或项目文件）
        public string AutoCleanSchedule = "off"; // off | daily | weekly
        public bool AutoCleanOnLowSpace = false;
        public double AutoCleanLowSpaceGB = 12;
        public int AutoCleanTempAgeDays = 3;
        public string LastAutoClean = "";        // yyyy-MM-dd HH:mm:ss
        public List<string> AutoCleanDisabledIds = new List<string>(); // 用户手动排除出自动清理的 L1 ID

        // 卡顿自动检测与高内存温和自动释放
        public bool LagWatchdog = true;
        public bool AutoCleanMemOnHigh = false;
        public int AutoCleanMemPct = 88;

        // 自动更新配置（默认预填 GitHub Releases API 更新源，同时支持自定义 latest.json / 局域网共享目录）
        public const string DefaultUpdateUrl = "https://api.github.com/repos/a613548565/disk-guard/releases/latest";
        public bool AutoCheckUpdate = true;
        public string UpdateUrl = DefaultUpdateUrl;

        public List<string> ExtraDirs = new List<string>();

        public static string FilePath { get { return Path.Combine(Util.AppDir, "DiskGuard.ini"); } }

        public bool AutoCleanWeekly
        {
            get { return AutoCleanSchedule == "weekly"; }
            set { if (value && AutoCleanSchedule == "off") AutoCleanSchedule = "weekly"; else if (!value && AutoCleanSchedule == "weekly") AutoCleanSchedule = "off"; }
        }

        public static Config Load()
        {
            var c = new Config();
            if (!File.Exists(FilePath))
            {
                if (Directory.Exists(@"E:\develop\nodejs\node_cache\_npx"))
                    c.ExtraDirs.Add(@"E:\develop\nodejs\node_cache\_npx");
                c.Save();
                return c;
            }
            string[] lines;
            try { lines = File.ReadAllLines(FilePath, Encoding.UTF8); } catch { return c; }
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                double d; int n; bool b;
                switch (k)
                {
                    case "drives":
                        if (string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase) || v == "*")
                        {
                            c.AutoDiscoverDrives = true;
                            c.Drives = DriveStat.AllFixedDrives().ToArray();
                        }
                        else
                        {
                            var arr = v.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim().TrimEnd(':', '\\').ToUpperInvariant())
                                       .Where(s => s.Length == 1 && char.IsLetter(s[0]))
                                       .Distinct()
                                       .ToArray();
                            if (arr.Length > 0) { c.AutoDiscoverDrives = false; c.Drives = arr; }
                        }
                        break;
                    case "warngb":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) c.WarnGB = d;
                        break;
                    case "critgb":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) c.CritGB = d;
                        break;
                    case "dropalertgb":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) c.DropAlertGB = d;
                        break;
                    case "intervalsec":
                        if (int.TryParse(v, out n) && n >= 10 && n <= 3600) c.IntervalSec = n;
                        break;
                    case "dockerexe":
                        if (v.Length > 0) c.DockerExe = v;
                        break;
                    case "theme":
                        if (string.Equals(v, "dark", StringComparison.OrdinalIgnoreCase)) c.Theme = ThemeMode.Dark;
                        else if (string.Equals(v, "light", StringComparison.OrdinalIgnoreCase)) c.Theme = ThemeMode.Light;
                        else c.Theme = ThemeMode.Auto;
                        break;
                    case "autocleanweekly":
                        if (bool.TryParse(v, out b) && b && c.AutoCleanSchedule == "off") c.AutoCleanSchedule = "weekly";
                        break;
                    case "autocleanschedule":
                        string low = v.ToLowerInvariant();
                        if (low == "daily" || low == "weekly" || low == "off") c.AutoCleanSchedule = low;
                        break;
                    case "autocleanonlowspace":
                        if (bool.TryParse(v, out b)) c.AutoCleanOnLowSpace = b;
                        break;
                    case "autocleanlowspacegb":
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 2) c.AutoCleanLowSpaceGB = d;
                        break;
                    case "autocleantempagedays":
                        if (int.TryParse(v, out n) && n >= 1 && n <= 90) c.AutoCleanTempAgeDays = n;
                        break;
                    case "autocleandisabled":
                        c.AutoCleanDisabledIds = v.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
                        break;
                    case "lagwatchdog":
                        if (bool.TryParse(v, out b)) c.LagWatchdog = b;
                        break;
                    case "autocleanmemonhigh":
                        if (bool.TryParse(v, out b)) c.AutoCleanMemOnHigh = b;
                        break;
                    case "autocleanmempct":
                        if (int.TryParse(v, out n) && n >= 70 && n <= 96) c.AutoCleanMemPct = n;
                        break;
                    case "autocheckupdate":
                        if (bool.TryParse(v, out b)) c.AutoCheckUpdate = b;
                        break;
                    case "updateurl":
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            // 自动将旧版占位源地址迁移为当前默认仓库地址
                            if (v.IndexOf("repos/wrl90/disk-guard", StringComparison.OrdinalIgnoreCase) >= 0)
                                c.UpdateUrl = DefaultUpdateUrl;
                            else
                                c.UpdateUrl = v;
                        }
                        break;
                    case "lastautoclean":
                        c.LastAutoClean = v;
                        break;
                    case "extradir":
                        if (v.Length > 0 && !c.ExtraDirs.Contains(v, StringComparer.OrdinalIgnoreCase))
                            c.ExtraDirs.Add(v);
                        break;
                }
            }
            if (c.CritGB >= c.WarnGB) c.CritGB = Math.Max(1, c.WarnGB - 2);
            return c;
        }

        public string[] EffectiveDrives()
        {
            if (AutoDiscoverDrives)
            {
                var all = DriveStat.AllFixedDrives();
                if (all.Count > 0) { Drives = all.ToArray(); return Drives; }
            }
            return (Drives != null && Drives.Length > 0) ? Drives : new[] { "C" };
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# DiskGuard v5.1 配置（drives=auto 表示自动监控本机全部固定分区）");
            sb.AppendLine("drives=" + (AutoDiscoverDrives ? "auto" : string.Join(",", Drives)));
            sb.AppendLine("warnGB=" + WarnGB.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("critGB=" + CritGB.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("dropAlertGB=" + DropAlertGB.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("intervalSec=" + IntervalSec);
            sb.AppendLine("theme=" + Theme.ToString().ToLowerInvariant());
            sb.AppendLine("autoCleanSchedule=" + AutoCleanSchedule);
            sb.AppendLine("autoCleanOnLowSpace=" + (AutoCleanOnLowSpace ? "true" : "false"));
            sb.AppendLine("autoCleanLowSpaceGB=" + AutoCleanLowSpaceGB.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("autoCleanTempAgeDays=" + AutoCleanTempAgeDays);
            sb.AppendLine("lagWatchdog=" + (LagWatchdog ? "true" : "false"));
            sb.AppendLine("autoCleanMemOnHigh=" + (AutoCleanMemOnHigh ? "true" : "false"));
            sb.AppendLine("autoCleanMemPct=" + AutoCleanMemPct);
            sb.AppendLine("autoCheckUpdate=" + (AutoCheckUpdate ? "true" : "false"));
            if (!string.IsNullOrWhiteSpace(UpdateUrl)) sb.AppendLine("updateUrl=" + UpdateUrl.Trim());
            if (AutoCleanDisabledIds.Count > 0)
                sb.AppendLine("autoCleanDisabled=" + string.Join(",", AutoCleanDisabledIds));
            if (!string.IsNullOrEmpty(LastAutoClean))
                sb.AppendLine("lastAutoClean=" + LastAutoClean);
            sb.AppendLine("dockerExe=" + DockerExe);
            sb.AppendLine("# 额外的可清理缓存目录（清空目录内容，目录本身保留），可写多行");
            foreach (var dir in ExtraDirs)
            {
                if (!string.IsNullOrWhiteSpace(dir)) sb.AppendLine("extraDir=" + dir.Trim());
            }
            try { File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false)); } catch { }
        }
    }

    public static class Util
    {
        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string LogDir { get { var d = Path.Combine(AppDir, "logs"); try { Directory.CreateDirectory(d); } catch { } return d; } }
        public static string LogFile { get { return Path.Combine(LogDir, "clean.log"); } }
        public static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string RoamingAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string UserProfile { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static string ProgramData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }

        public static string Fmt(long b)
        {
            if (b < 0) return "—";
            if (b >= (1L << 30)) return (b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= (1L << 20)) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        public static string FmtCompact(long b)
        {
            if (b < 0) return "—";
            if (b >= (1L << 30)) return (b / 1073741824.0).ToString("0.0") + "G";
            if (b >= (1L << 20)) return (b / 1048576.0).ToString("0") + "M";
            return (b / 1024.0).ToString("0") + "K";
        }

        public static string FmtDelta(long b)
        {
            return (b >= 0 ? "+" : "-") + FmtCompact(Math.Abs(b));
        }

        public static string FmtAge(DateTime lastWrite)
        {
            var span = DateTime.Now - lastWrite;
            if (span.TotalDays >= 365) return ((int)(span.TotalDays / 365)) + " 年前";
            if (span.TotalDays >= 30) return ((int)(span.TotalDays / 30)) + " 月前";
            if (span.TotalDays >= 1) return ((int)span.TotalDays) + " 天前";
            if (span.TotalHours >= 1) return ((int)span.TotalHours) + " 小时前";
            return "刚刚";
        }

        public static string Run(string exe, string args, int timeoutMs, out int exit)
        {
            var sb = new StringBuilder();
            exit = -1;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = new Process { StartInfo = psi })
                {
                    DataReceivedEventHandler h = (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.OutputDataReceived += h;
                    p.ErrorDataReceived += h;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        lock (sb) sb.AppendLine("[timeout]");
                        return sb.ToString();
                    }
                    p.WaitForExit();
                    exit = p.ExitCode;
                }
            }
            catch (Exception ex) { lock (sb) sb.AppendLine("[error] " + ex.Message); }
            lock (sb) return sb.ToString();
        }

        public static long ParseSizeString(string s)
        {
            var m = Regex.Match(s ?? "", @"([\d.]+)\s*(kB|KB|K|MB|M|GB|G|TB|T|B)\b", RegexOptions.IgnoreCase);
            if (!m.Success) return -1;
            double v;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1;
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "K": case "KB": v *= 1024.0; break;
                case "M": case "MB": v *= 1048576.0; break;
                case "G": case "GB": v *= 1073741824.0; break;
                case "T": case "TB": v *= 1099511627776.0; break;
            }
            return (long)v;
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

        public static string LastLine(string s)
        {
            var ls = (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ls.Length == 0 ? "" : ls[ls.Length - 1].Trim();
        }

        public static long FileLen(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

        public static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static void OpenInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        public static void OpenPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return;
            try { Process.Start(p); } catch { }
        }
    }

    public static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "DiskGuard";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(Key))
                        return k != null && k.GetValue(Name) != null;
                }
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

    public static class TrayPin
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
                            if (p != null && string.Equals(p, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                                return Root + "\\" + n;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

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

    public static class LogSummary
    {
        public class SummaryData
        {
            public int RunCount;
            public long TotalFreedBytes;
            public string LastRunTime = "暂无记录";
            public string RecentText = "";
        }

        public static SummaryData Read()
        {
            var data = new SummaryData();
            if (!File.Exists(Util.LogFile)) return data;
            try
            {
                var lines = File.ReadAllLines(Util.LogFile, Encoding.UTF8);
                foreach (var line in lines)
                {
                    var mRun = Regex.Match(line, @"====\s*(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})");
                    if (mRun.Success)
                    {
                        data.RunCount++;
                        data.LastRunTime = mRun.Groups[1].Value;
                    }
                    var mSum = Regex.Match(line, @"合计：删除文件\s+([\d.]+\s*[KMGTP]?B?)");
                    if (mSum.Success)
                    {
                        long sz = Util.ParseSizeString(mSum.Groups[1].Value);
                        if (sz > 0) data.TotalFreedBytes += sz;
                    }
                }
                int skip = Math.Max(0, lines.Length - 200);
                data.RecentText = string.Join(Environment.NewLine, lines.Skip(skip).ToArray());
            }
            catch { }
            return data;
        }
    }
}
