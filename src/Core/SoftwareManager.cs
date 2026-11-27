// DiskGuard v8 - 已安装软件安全卸载与残留排查 + PowerShell 7 检测与一键升级引擎
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DiskGuard
{
    public class InstalledAppEntry
    {
        public string Id;
        public string Name;
        public string Publisher;
        public string Version;
        public string InstallDate;
        public string InstallLocation;
        public string UninstallString;
        public long EstimatedBytes;
        public string Scope; // HKLM64 / HKLM32 / HKCU
    }

    public class LeftoverEntry
    {
        public string Path;
        public string Area; // InstallLocation / LocalAppData / Roaming / ProgramData
        public long SizeBytes;
    }

    public class PowerShellStatus
    {
        public string WindowsPsVersion;
        public bool HasPwsh7;
        public string Pwsh7Version;
        public string Pwsh7Path;
        public bool HasWinget;
        public string WingetVersion;
    }

    public static class SoftwareManager
    {
        static readonly object appLock = new object();
        static Dictionary<string, InstalledAppEntry> cachedApps = new Dictionary<string, InstalledAppEntry>(StringComparer.OrdinalIgnoreCase);

        public static List<InstalledAppEntry> ListInstalledApps()
        {
            var map = new Dictionary<string, InstalledAppEntry>(StringComparer.OrdinalIgnoreCase);
            ReadUninstallHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM64", map);
            ReadUninstallHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM32", map);
            ReadUninstallHive(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU", map);

            var list = map.Values
                .OrderByDescending(x => x.EstimatedBytes)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (appLock)
            {
                cachedApps.Clear();
                foreach (var a in list) cachedApps[a.Id] = a;
            }
            return list;
        }

        static void ReadUninstallHive(RegistryKey root, string subPath, string scope, Dictionary<string, InstalledAppEntry> map)
        {
            try
            {
                using (var parent = root.OpenSubKey(subPath, false))
                {
                    if (parent == null) return;
                    foreach (var keyName in parent.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = parent.OpenSubKey(keyName, false))
                            {
                                if (k == null) continue;
                                string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                                if (string.IsNullOrEmpty(name)) continue;

                                object sysComp = k.GetValue("SystemComponent");
                                if (sysComp != null && sysComp.ToString() == "1") continue;
                                if (!string.IsNullOrEmpty(k.GetValue("ParentKeyName") as string)) continue;
                                string relType = (k.GetValue("ReleaseType") as string ?? "");
                                if (relType.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    relType.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                                string uninst = (k.GetValue("UninstallString") as string ?? "").Trim();
                                if (string.IsNullOrEmpty(uninst)) continue;

                                string ver = (k.GetValue("DisplayVersion") as string ?? "").Trim();
                                string pub = (k.GetValue("Publisher") as string ?? "").Trim();
                                string loc = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"');
                                string dtRaw = (k.GetValue("InstallDate") as string ?? "").Trim();
                                string dt = FormatInstallDate(dtRaw);

                                long bytes = 0;
                                object estObj = k.GetValue("EstimatedSize");
                                if (estObj != null)
                                {
                                    long kb;
                                    if (long.TryParse(estObj.ToString(), out kb) && kb > 0) bytes = kb * 1024L;
                                }

                                string dedupKey = name + "|" + ver;
                                if (map.ContainsKey(dedupKey))
                                {
                                    if (bytes > map[dedupKey].EstimatedBytes) map[dedupKey].EstimatedBytes = bytes;
                                    continue;
                                }

                                map[dedupKey] = new InstalledAppEntry
                                {
                                    Id = scope + ":" + keyName,
                                    Name = name,
                                    Publisher = pub,
                                    Version = ver,
                                    InstallDate = dt,
                                    InstallLocation = loc,
                                    UninstallString = uninst,
                                    EstimatedBytes = bytes,
                                    Scope = scope
                                };
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static string FormatInstallDate(string raw)
        {
            if (raw.Length == 8)
            {
                int y, m, d;
                if (int.TryParse(raw.Substring(0, 4), out y) && int.TryParse(raw.Substring(4, 2), out m) && int.TryParse(raw.Substring(6, 2), out d))
                    return string.Format("{0:0000}-{1:00}-{2:00}", y, m, d);
            }
            return raw;
        }

        public static FixResult LaunchUninstall(string appId)
        {
            InstalledAppEntry entry = null;
            lock (appLock) { cachedApps.TryGetValue(appId ?? "", out entry); }
            if (entry == null)
            {
                ListInstalledApps();
                lock (appLock) { cachedApps.TryGetValue(appId ?? "", out entry); }
            }
            if (entry == null)
                return new FixResult { Ok = false, Title = "未找到该软件", Summary = "请刷新已安装软件列表后重试" };

            try
            {
                string cmd = entry.UninstallString.Trim();
                // 常见 MSI 安装包 UninstallString 写成了 /I，安全转为 /X 卸载模式
                if (cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    cmd.IndexOf("/I", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    cmd.IndexOf("/X", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cmd = cmd.Replace("/I", "/X").Replace("/i", "/X");
                }

                string file, args;
                SplitCommandLine(cmd, out file, out args);

                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return new FixResult
                {
                    Ok = true,
                    Title = "已启动官方卸载程序",
                    Summary = "正在调用「" + entry.Name + "」自带卸载向导；卸载完成后可点击「扫描残留」安全清理遗留文件夹"
                };
            }
            catch (Exception ex)
            {
                return new FixResult { Ok = false, Title = "启动卸载向导失败", Summary = ex.Message };
            }
        }

        static void SplitCommandLine(string cmd, out string file, out string args)
        {
            file = cmd; args = "";
            if (string.IsNullOrWhiteSpace(cmd)) return;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1)
                {
                    file = cmd.Substring(1, end - 1);
                    args = cmd.Substring(end + 1).Trim();
                    return;
                }
            }
            // 无引号但路径含空格时，优先尝试按 .exe 截断
            int exeIdx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                file = cmd.Substring(0, exeIdx + 4).Trim();
                args = cmd.Substring(exeIdx + 4).Trim();
                return;
            }
            int sp = cmd.IndexOf(' ');
            if (sp > 0)
            {
                file = cmd.Substring(0, sp).Trim();
                args = cmd.Substring(sp + 1).Trim();
            }
        }

        // 只读扫描软件卸载后的残留目录（绝不自动删，返回清单由用户核对后送入回收站）
        public static List<LeftoverEntry> ScanLeftovers(string appName, string publisher, string installLocation)
        {
            var res = new List<LeftoverEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 原安装目录
            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
            {
                TryAddLeftover(res, seen, installLocation, "原安装目录");
            }

            // 构造安全匹配词（拒绝过短或泛化词如 Microsoft/Windows/App/Tool）
            var tokens = new List<string>();
            AddSafeToken(tokens, appName);
            if (!string.IsNullOrWhiteSpace(appName))
            {
                string firstWord = appName.Split(new[] { ' ', '-', '(', '（' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                AddSafeToken(tokens, firstWord);
            }

            if (tokens.Count == 0) return res;

            var searchAreas = new[]
            {
                new KeyValuePair<string, string>(Util.LocalAppData, "LocalAppData"),
                new KeyValuePair<string, string>(Util.RoamingAppData, "Roaming"),
                new KeyValuePair<string, string>(Util.ProgramData, "ProgramData")
            };

            foreach (var area in searchAreas)
            {
                if (string.IsNullOrEmpty(area.Key) || !Directory.Exists(area.Key)) continue;
                try
                {
                    foreach (var sub in Directory.GetDirectories(area.Key))
                    {
                        string dirName = Path.GetFileName(sub);
                        if (string.IsNullOrEmpty(dirName) || dirName.Length < 4) continue;
                        if (tokens.Any(t => string.Equals(dirName, t, StringComparison.OrdinalIgnoreCase) ||
                                            (t.Length >= 6 && dirName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            TryAddLeftover(res, seen, sub, area.Value);
                        }
                    }
                }
                catch { }
            }

            return res.OrderByDescending(x => x.SizeBytes).Take(20).ToList();
        }

        static readonly HashSet<string> GenericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "System", "Program", "Programs", "Common", "Temp", "Cache", "Update",
            "Runtime", "Driver", "Setup", "Installer", "Tools", "Software", "App", "Apps", "Data", "Google", "Apple", "Intel", "AMD", "NVIDIA"
        };

        static void AddSafeToken(List<string> tokens, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            string s = raw.Trim();
            if (s.Length < 4 || GenericWords.Contains(s)) return;
            if (!tokens.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase))) tokens.Add(s);
        }

        static void TryAddLeftover(List<LeftoverEntry> list, HashSet<string> seen, string path, string area)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return; }
            if (seen.Contains(full)) return;
            if (Fs.IsProtected(full) || Fs.IsSystemCriticalPath(full)) return;
            string low = full.ToLowerInvariant();
            if (low.EndsWith(@"\microsoft") || low.EndsWith(@"\windows") || low.EndsWith(@"\common files") || low.EndsWith(@"\diskguard")) return;
            seen.Add(full);
            long sz = Fs.DirSize(full, null, CancellationToken.None, false);
            list.Add(new LeftoverEntry { Path = full, Area = area, SizeBytes = sz });
        }

        // ==================== PowerShell 7 检测与一键升级 ====================
        public static PowerShellStatus InspectPowerShell()
        {
            var st = new PowerShellStatus { WindowsPsVersion = "5.1 (系统内置)" };
            try
            {
                int ex;
                string ps5 = Util.Run("powershell.exe", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", 4000, out ex);
                if (ex == 0 && !string.IsNullOrWhiteSpace(ps5)) st.WindowsPsVersion = ps5.Trim();
            }
            catch { }

            string[] candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7-preview", "pwsh.exe"),
                "pwsh.exe"
            };
            foreach (var c in candidates)
            {
                if (c != "pwsh.exe" && !File.Exists(c)) continue;
                try
                {
                    int ex;
                    string ver = Util.Run(c, "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", 4000, out ex);
                    if (ex == 0 && !string.IsNullOrWhiteSpace(ver))
                    {
                        st.HasPwsh7 = true;
                        st.Pwsh7Version = ver.Trim();
                        st.Pwsh7Path = c == "pwsh.exe" ? "PATH (pwsh.exe)" : c;
                        break;
                    }
                }
                catch { }
            }

            try
            {
                int ex;
                string wg = Util.Run("winget.exe", "--version", 3500, out ex);
                if (ex == 0 && !string.IsNullOrWhiteSpace(wg))
                {
                    st.HasWinget = true;
                    st.WingetVersion = wg.Trim();
                }
            }
            catch { }

            return st;
        }

        public static FixResult UpgradePowerShell7()
        {
            var st = InspectPowerShell();
            if (!st.HasWinget)
            {
                try { Process.Start("https://github.com/PowerShell/PowerShell/releases/latest"); } catch { }
                return new FixResult
                {
                    Ok = false,
                    Title = "未检测到 winget 包管理器",
                    Summary = "已为您打开 PowerShell 7 官方 GitHub 发布页，请下载 win-x64.msi 安装包运行"
                };
            }

            try
            {
                // 启动可视终端运行 winget 安装/升级，让用户清楚看到微软官方源的下载与安装进度（不黑盒卡死）
                string cmd = st.HasPwsh7
                    ? "winget upgrade --id Microsoft.PowerShell --source winget --accept-package-agreements --accept-source-agreements"
                    : "winget install --id Microsoft.PowerShell --source winget --accept-package-agreements --accept-source-agreements";
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k \"title DiskGuard - 升级 PowerShell 7 && echo 正在通过微软官方 winget 安装/升级 PowerShell 7 ... && " + cmd + " && echo. && echo 执行完毕，请回到 DiskGuard 点击刷新查看最新版本号。\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return new FixResult
                {
                    Ok = true,
                    Title = "已启动 PowerShell 7 官方升级向导",
                    Summary = "正在通过 winget 拉取微软官方最新 PowerShell 7（与系统内置 5.1 互不冲突，装完后点击刷新检测）"
                };
            }
            catch (Exception ex)
            {
                return new FixResult { Ok = false, Title = "启动 PowerShell 7 升级失败", Summary = ex.Message };
            }
        }
    }
}
