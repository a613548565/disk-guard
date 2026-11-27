// DiskGuard v8 - 网络诊断与优选优化（DNS 实测延迟/系统代理残留修复/TCP 参数调优）+ 环境变量体检清理与自动备份回滚
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DiskGuard
{
    public class DnsProbeItem
    {
        public string Name;
        public string Primary;
        public string Secondary;
        public string Note;
        public long LatencyMs; // -1 表示超时
    }

    public class NetworkStatus
    {
        public string AdapterName;
        public string AdapterDesc;
        public string IpAddress;
        public List<string> CurrentDns = new List<string>();
        public bool ProxyEnabled;
        public string ProxyServer;
        public string AutoConfigUrl;
        public string TcpAutoTuning;
        public List<DnsProbeItem> DnsBench = new List<DnsProbeItem>();
    }

    public class PathEntryItem
    {
        public int Index;
        public string RawValue;
        public string ExpandedValue;
        public bool Exists;
        public bool IsDuplicate;
        public string Scope; // User / Machine
    }

    public class EnvVarItem
    {
        public string Name;
        public string Value;
        public string Scope; // User / Machine
        public bool IsExpandable;
    }

    public class EnvBackupMeta
    {
        public string FileName;
        public string Time;
        public string Reason;
    }

    public class EnvInspection
    {
        public List<PathEntryItem> UserPath = new List<PathEntryItem>();
        public List<PathEntryItem> MachinePath = new List<PathEntryItem>();
        public List<EnvVarItem> UserVars = new List<EnvVarItem>();
        public List<EnvVarItem> MachineVars = new List<EnvVarItem>();
        public int UserMissingCount;
        public int UserDupCount;
        public int MachineMissingCount;
        public int MachineDupCount;
        public int UserPathLen;
        public int MachinePathLen;
        public List<EnvBackupMeta> Backups = new List<EnvBackupMeta>();
    }

    public static class NetworkEnvManager
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;

        static string EnvBackupDir
        {
            get
            {
                string d = Path.Combine(Util.LocalAppData, "DiskGuard", "env_backups");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static void BroadcastEnvChange()
        {
            try
            {
                UIntPtr res;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 2000, out res);
            }
            catch { }
        }

        // ==================== 1. 网络诊断、DNS 测速与安全优化 ====================
        public static NetworkStatus InspectNetwork()
        {
            var st = new NetworkStatus { AdapterName = "未检测到活动网卡", AdapterDesc = "", IpAddress = "—", TcpAutoTuning = "未知" };

            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0 ? 1 : 0)
                    .ThenBy(n => (n.Description ?? "").IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (n.Description ?? "").IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (n.Description ?? "").IndexOf("WSL", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                    .ToList();

                var nic = nics.FirstOrDefault();
                if (nic != null)
                {
                    st.AdapterName = nic.Name;
                    st.AdapterDesc = nic.Description;
                    var props = nic.GetIPProperties();
                    var v4 = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (v4 != null) st.IpAddress = v4.Address.ToString();
                    foreach (var dns in props.DnsAddresses)
                    {
                        if (dns.AddressFamily == AddressFamily.InterNetwork) st.CurrentDns.Add(dns.ToString());
                    }
                }
            }
            catch { }

            // 检查 Windows 系统代理残留
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    if (k != null)
                    {
                        object en = k.GetValue("ProxyEnable");
                        st.ProxyEnabled = en != null && en.ToString() == "1";
                        st.ProxyServer = (k.GetValue("ProxyServer") as string ?? "").Trim();
                        st.AutoConfigUrl = (k.GetValue("AutoConfigURL") as string ?? "").Trim();
                    }
                }
            }
            catch { }

            // 检查 TCP Auto-Tuning
            try
            {
                int ex;
                string o = Util.Run("netsh.exe", "interface tcp show global", 3000, out ex);
                foreach (var line in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf("Auto-Tuning", StringComparison.OrdinalIgnoreCase) >= 0 || line.Contains("自动调谐"))
                    {
                        int col = line.IndexOf(':');
                        if (col > 0) { st.TcpAutoTuning = line.Substring(col + 1).Trim(); break; }
                    }
                }
            }
            catch { }

            // 并发测速 5 组主流公共 DNS
            var presets = new[]
            {
                new DnsProbeItem { Name = "阿里 DNS (国内极速)", Primary = "223.5.5.5", Secondary = "223.6.6.6", Note = "国内网站与开发镜像解析快且稳" },
                new DnsProbeItem { Name = "腾讯 DNSPod (国内稳定)", Primary = "119.29.29.29", Secondary = "182.254.116.116", Note = "腾讯系与国内主流服务低延迟" },
                new DnsProbeItem { Name = "114 公共 DNS", Primary = "114.114.114.114", Secondary = "114.114.115.115", Note = "国内三大运营商广泛兼容" },
                new DnsProbeItem { Name = "Cloudflare DNS", Primary = "1.1.1.1", Secondary = "1.0.0.1", Note = "全球隐私 DNS，海外访问友好" },
                new DnsProbeItem { Name = "Google Public DNS", Primary = "8.8.8.8", Secondary = "8.8.4.4", Note = "国际通用标准 DNS" }
            };

            Parallel.ForEach(presets, item =>
            {
                item.LatencyMs = ProbeLatency(item.Primary);
            });
            st.DnsBench = presets.OrderBy(x => x.LatencyMs < 0 ? 99999 : x.LatencyMs).ToList();
            return st;
        }

        static long ProbeLatency(string ip)
        {
            try
            {
                using (var ping = new Ping())
                {
                    long best = long.MaxValue;
                    for (int i = 0; i < 2; i++)
                    {
                        var reply = ping.Send(ip, 1200);
                        if (reply != null && reply.Status == IPStatus.Success && reply.RoundtripTime < best)
                            best = reply.RoundtripTime;
                    }
                    return best == long.MaxValue ? -1 : best;
                }
            }
            catch { return -1; }
        }

        public static FixResult SetAdapterDns(string adapterName, string primary, string secondary)
        {
            if (string.IsNullOrWhiteSpace(adapterName) || adapterName == "未检测到活动网卡")
                return new FixResult { Ok = false, Title = "未识别到活动网卡", Summary = "请先确认网络连接正常" };

            try
            {
                string safeName = adapterName.Replace("\"", "");
                string script;
                if (string.Equals(primary, "dhcp", StringComparison.OrdinalIgnoreCase))
                {
                    script = string.Format("netsh interface ip set dns name=\"{0}\" source=dhcp && ipconfig /flushdns", safeName);
                }
                else
                {
                    script = string.Format("netsh interface ip set dns name=\"{0}\" static {1} primary", safeName, primary);
                    if (!string.IsNullOrWhiteSpace(secondary))
                        script += string.Format(" && netsh interface ip add dns name=\"{0}\" {1} index=2", safeName, secondary);
                    script += " && ipconfig /flushdns";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + script,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi)) { p.WaitForExit(15000); }
                return new FixResult
                {
                    Ok = true,
                    Title = primary == "dhcp" ? "已恢复自动获取 DNS" : "DNS 优选切换完成",
                    Summary = primary == "dhcp"
                        ? ("网卡「" + safeName + "」已恢复 DHCP 自动 DNS 并刷新缓存")
                        : ("网卡「" + safeName + "」已切换至 " + primary + (string.IsNullOrEmpty(secondary) ? "" : (" / " + secondary)) + " 并刷新 DNS 缓存")
                };
            }
            catch (Exception ex)
            {
                return new FixResult { Ok = false, Title = "修改 DNS 未完成", Summary = "已取消 UAC 授权或执行失败：" + ex.Message };
            }
        }

        public static FixResult ClearSystemProxy()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (k == null) return new FixResult { Ok = false, Title = "无法打开代理设置", Summary = "注册表键不存在" };
                    k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    try { k.DeleteValue("AutoConfigURL", false); } catch { }
                }
                return new FixResult
                {
                    Ok = true,
                    Title = "系统残留代理已清理",
                    Summary = "已关闭 Windows 系统代理开关与 PAC 自动脚本，网页/终端直连将立即恢复"
                };
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "清理代理失败", Summary = ex.Message }; }
        }

        public static FixResult OptimizeTcpAndFlush()
        {
            try
            {
                string script = "ipconfig /flushdns && arp -d * && netsh int tcp set global autotuninglevel=normal";
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + script,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi)) { p.WaitForExit(15000); }
                return new FixResult
                {
                    Ok = true,
                    Title = "网络栈优化完成",
                    Summary = "已刷新 DNS 与 ARP 缓存，并确保 TCP 接收窗口自动调谐为微软推荐的 normal 模式"
                };
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "网络栈优化已取消", Summary = ex.Message }; }
        }

        // ==================== 2. 环境变量体检、PATH 去重清洗与自动备份回滚 ====================
        public static EnvInspection InspectEnvVars()
        {
            var res = new EnvInspection();
            ReadScopeEnv(Registry.CurrentUser, @"Environment", "User", res.UserVars, res.UserPath, out res.UserPathLen, out res.UserMissingCount, out res.UserDupCount);
            ReadScopeEnv(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Machine", res.MachineVars, res.MachinePath, out res.MachinePathLen, out res.MachineMissingCount, out res.MachineDupCount);
            res.Backups = ListBackups();
            return res;
        }

        static void ReadScopeEnv(RegistryKey root, string subKey, string scope, List<EnvVarItem> vars, List<PathEntryItem> pathItems, out int pathLen, out int missingCnt, out int dupCnt)
        {
            pathLen = 0; missingCnt = 0; dupCnt = 0;
            try
            {
                using (var k = root.OpenSubKey(subKey, false))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    {
                        var kind = k.GetValueKind(name);
                        string val = k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                        vars.Add(new EnvVarItem { Name = name, Value = val, Scope = scope, IsExpandable = (kind == RegistryValueKind.ExpandString) });

                        if (string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase))
                        {
                            pathLen = val.Length;
                            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var parts = val.Split(';');
                            int idx = 0;
                            foreach (var raw in parts)
                            {
                                string trimmed = raw.Trim();
                                if (string.IsNullOrEmpty(trimmed)) continue;
                                string exp = Environment.ExpandEnvironmentVariables(trimmed).Trim('"');
                                string norm = exp.TrimEnd('\\');
                                bool dup = !seen.Add(norm);
                                bool exists = Directory.Exists(exp);
                                if (dup) dupCnt++;
                                if (!exists) missingCnt++;
                                pathItems.Add(new PathEntryItem
                                {
                                    Index = idx++,
                                    RawValue = trimmed,
                                    ExpandedValue = exp,
                                    Exists = exists,
                                    IsDuplicate = dup,
                                    Scope = scope
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static string BackupEnvVars(string reason)
        {
            try
            {
                var snap = new Dictionary<string, object>
                {
                    { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "reason", reason ?? "手动备份" },
                    { "user", DumpHive(Registry.CurrentUser, @"Environment") },
                    { "machine", DumpHive(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment") }
                };
                string name = "env_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                string full = Path.Combine(EnvBackupDir, name);
                var ser = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                File.WriteAllText(full, ser.Serialize(snap), new UTF8Encoding(false));
                return name;
            }
            catch { return ""; }
        }

        static Dictionary<string, object> DumpHive(RegistryKey root, string subKey)
        {
            var dict = new Dictionary<string, object>();
            try
            {
                using (var k = root.OpenSubKey(subKey, false))
                {
                    if (k == null) return dict;
                    foreach (var n in k.GetValueNames())
                    {
                        var kind = k.GetValueKind(n);
                        string val = k.GetValue(n, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                        dict[n] = new Dictionary<string, object> { { "v", val }, { "expand", kind == RegistryValueKind.ExpandString } };
                    }
                }
            }
            catch { }
            return dict;
        }

        public static List<EnvBackupMeta> ListBackups()
        {
            var res = new List<EnvBackupMeta>();
            try
            {
                var ser = new JavaScriptSerializer();
                foreach (var f in Directory.GetFiles(EnvBackupDir, "env_*.json").OrderByDescending(x => x).Take(15))
                {
                    try
                    {
                        var d = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                        res.Add(new EnvBackupMeta
                        {
                            FileName = Path.GetFileName(f),
                            Time = d.ContainsKey("time") ? d["time"].ToString() : "",
                            Reason = d.ContainsKey("reason") ? d["reason"].ToString() : ""
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return res;
        }

        public static void SetUserEnvRaw(string name, string value)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Environment", true))
            {
                if (k == null) return;
                var kind = (value != null && value.Contains("%")) ? RegistryValueKind.ExpandString : RegistryValueKind.String;
                if (string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase)) kind = RegistryValueKind.ExpandString;
                k.SetValue(name, value ?? "", kind);
            }
        }

        public static FixResult CleanPathScope(string scope, bool removeMissing, bool removeDuplicates)
        {
            var insp = InspectEnvVars();
            var list = scope == "Machine" ? insp.MachinePath : insp.UserPath;
            if (list.Count == 0) return new FixResult { Ok = false, Title = "PATH 为空", Summary = "当前作用域没有可整理的 PATH 项" };

            var kept = new List<string>();
            int removedMiss = 0, removedDup = 0;
            foreach (var p in list)
            {
                if (removeDuplicates && p.IsDuplicate) { removedDup++; continue; }
                if (removeMissing && !p.Exists) { removedMiss++; continue; }
                kept.Add(p.RawValue);
            }

            if (removedMiss == 0 && removedDup == 0)
                return new FixResult { Ok = true, Title = "PATH 非常整洁", Summary = "未发现失效路径或重复项，无需改动" };

            string bak = BackupEnvVars("整理 " + scope + " PATH");
            string newPath = string.Join(";", kept.ToArray());

            if (scope == "User")
            {
                try
                {
                    SetUserEnvRaw("Path", newPath);
                    BroadcastEnvChange();
                    return new FixResult
                    {
                        Ok = true,
                        Title = "用户 PATH 整理完成",
                        Summary = string.Format("已移除 {0} 个失效路径、{1} 个重复项（已自动备份 {2}，随时可一键回滚）", removedMiss, removedDup, bak)
                    };
                }
                catch (Exception ex) { return new FixResult { Ok = false, Title = "保存用户 PATH 失败", Summary = ex.Message }; }
            }
            else
            {
                return WriteMachineEnvElevated("Path", newPath, true,
                    string.Format("系统 PATH 已移除 {0} 个失效路径、{1} 个重复项（已自动备份 {2}）", removedMiss, removedDup, bak));
            }
        }

        public static FixResult SaveEnvVar(string scope, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new FixResult { Ok = false, Title = "变量名不能为空", Summary = "请输入合法的环境变量名" };
            name = name.Trim();
            string bak = BackupEnvVars("修改变量 " + scope + ":" + name);

            if (scope == "User")
            {
                try
                {
                    SetUserEnvRaw(name, value ?? "");
                    BroadcastEnvChange();
                    return new FixResult { Ok = true, Title = "用户环境变量已保存", Summary = "已更新 " + name + "（已备份 " + bak + "，新终端立即生效）" };
                }
                catch (Exception ex) { return new FixResult { Ok = false, Title = "保存失败", Summary = ex.Message }; }
            }
            else
            {
                bool expand = string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase) || (value != null && value.Contains("%"));
                return WriteMachineEnvElevated(name, value ?? "", expand, "系统环境变量 " + name + " 已保存（已备份 " + bak + "）");
            }
        }

        public static FixResult DeleteEnvVar(string scope, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new FixResult { Ok = false, Title = "参数无效", Summary = "变量名为空" };
            if (string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "SystemRoot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "ComSpec", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TEMP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TMP", StringComparison.OrdinalIgnoreCase))
            {
                return new FixResult { Ok = false, Title = "安全红线拦截", Summary = "禁止直接删除核心系统变量：" + name };
            }

            string bak = BackupEnvVars("删除变量 " + scope + ":" + name);
            if (scope == "User")
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Environment", true)) { if (k != null) k.DeleteValue(name, false); }
                    BroadcastEnvChange();
                    return new FixResult { Ok = true, Title = "已删除用户变量", Summary = "已移除 " + name + "（已备份 " + bak + "，随时可恢复）" };
                }
                catch (Exception ex) { return new FixResult { Ok = false, Title = "删除失败", Summary = ex.Message }; }
            }
            else
            {
                return WriteMachineEnvElevated(name, null, false, "已删除系统变量 " + name + "（已备份 " + bak + "）");
            }
        }

        public static FixResult RestoreUserEnvBackup(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains("\\") || fileName.Contains("/"))
                return new FixResult { Ok = false, Title = "备份文件名无效", Summary = "非法路径" };
            string full = Path.Combine(EnvBackupDir, fileName);
            if (!File.Exists(full)) return new FixResult { Ok = false, Title = "备份不存在", Summary = fileName };

            try
            {
                BackupEnvVars("回滚前自动快照");
                var ser = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                var root = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(full, Encoding.UTF8));
                var userDict = root["user"] as Dictionary<string, object>;
                if (userDict != null)
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Environment", true))
                    {
                        if (k != null)
                        {
                            foreach (var kv in userDict)
                            {
                                var entry = kv.Value as Dictionary<string, object>;
                                if (entry == null) continue;
                                string v = entry.ContainsKey("v") && entry["v"] != null ? entry["v"].ToString() : "";
                                bool exp = entry.ContainsKey("expand") && entry["expand"] != null && bool.Parse(entry["expand"].ToString());
                                k.SetValue(kv.Key, v, exp ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                            }
                        }
                    }
                    BroadcastEnvChange();
                }
                return new FixResult { Ok = true, Title = "环境变量已回滚恢复", Summary = "已从快照 " + fileName + " 恢复所有用户环境变量（含 PATH）" };
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "回滚失败", Summary = ex.Message }; }
        }

        static FixResult WriteMachineEnvElevated(string name, string valueOrNullToDelete, bool expand, string okMsg)
        {
            string tmpPs1 = Path.Combine(Path.GetTempPath(), "dg_env_" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                string regPath = @"HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
                var sb = new StringBuilder();
                if (valueOrNullToDelete == null)
                {
                    sb.AppendLine("Remove-ItemProperty -Path '" + regPath + "' -Name '" + name.Replace("'", "''") + "' -ErrorAction SilentlyContinue");
                }
                else
                {
                    string kind = expand ? "ExpandString" : "String";
                    sb.AppendLine("Set-ItemProperty -Path '" + regPath + "' -Name '" + name.Replace("'", "''") + "' -Value '" + valueOrNullToDelete.Replace("'", "''") + "' -Type " + kind);
                }
                File.WriteAllText(tmpPs1, sb.ToString(), new UTF8Encoding(true));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + tmpPs1 + "\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    if (p.ExitCode != 0) return new FixResult { Ok = false, Title = "系统环境变量写入失败", Summary = "退出码 " + p.ExitCode };
                }
                BroadcastEnvChange();
                return new FixResult { Ok = true, Title = "系统环境变量已更新", Summary = okMsg };
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "修改系统变量已取消", Summary = ex.Message }; }
            finally { try { File.Delete(tmpPs1); } catch { } }
        }
    }
}
