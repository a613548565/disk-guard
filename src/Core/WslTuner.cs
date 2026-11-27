// DiskGuard v7 - WSL2 内存限流 (.wslconfig 可视化调优) 与休眠/虚拟内存诊断助手
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskGuard
{
    public class WslTunerState
    {
        public string ConfigPath;
        public bool ConfigExists;
        public int PhysicalRamGB;
        public int LogicalCores;
        public int MemoryGB;
        public int Processors;
        public int SwapGB;
        public string AutoMemoryReclaim = "gradual"; // gradual | dropcache | disabled
        public bool SparseVhd = true;
        public long HiberfilBytes;
        public long PagefileTotalBytes;
        public List<string> PagefilePaths = new List<string>();
    }

    public static class WslTuner
    {
        public static string ConfigPath { get { return Path.Combine(Util.UserProfile, ".wslconfig"); } }

        public static WslTunerState Read()
        {
            long tot, avail; double pct;
            SystemHealth.ReadMemory(out tot, out avail, out pct);
            int ramGB = Math.Max(4, (int)Math.Round(tot / 1073741824.0));
            int cores = Math.Max(2, Environment.ProcessorCount);

            var st = new WslTunerState
            {
                ConfigPath = ConfigPath,
                ConfigExists = File.Exists(ConfigPath),
                PhysicalRamGB = ramGB,
                LogicalCores = cores,
                // 默认推荐值：物理内存约 35%（8~16GB），核心数一半
                MemoryGB = Math.Max(4, Math.Min(16, (int)Math.Round(ramGB * 0.35))),
                Processors = Math.Max(2, cores / 2),
                SwapGB = 4,
                AutoMemoryReclaim = "gradual",
                SparseVhd = true
            };

            if (st.ConfigExists)
            {
                try
                {
                    foreach (var raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        int n;
                        if (k == "memory")
                        {
                            var m = Regex.Match(v, @"(\d+)");
                            if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n >= 2) st.MemoryGB = n;
                        }
                        else if (k == "processors")
                        {
                            if (int.TryParse(v, out n) && n >= 1) st.Processors = n;
                        }
                        else if (k == "swap")
                        {
                            var m = Regex.Match(v, @"(\d+)");
                            if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n >= 0) st.SwapGB = n;
                        }
                        else if (k == "automemoryreclaim")
                        {
                            string lv = v.ToLowerInvariant();
                            if (lv == "gradual" || lv == "dropcache" || lv == "disabled") st.AutoMemoryReclaim = lv;
                        }
                        else if (k == "sparsevhd")
                        {
                            st.SparseVhd = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }
            }

            st.HiberfilBytes = Util.FileLen(@"C:\hiberfil.sys");
            foreach (var d in DriveStat.AllFixedDrives())
            {
                string pf = d + @":\pagefile.sys";
                long len = Util.FileLen(pf);
                if (len > 0)
                {
                    st.PagefileTotalBytes += len;
                    st.PagefilePaths.Add(pf + " (" + Util.Fmt(len) + ")");
                }
            }
            return st;
        }

        public static FixResult SaveConfig(int memoryGB, int processors, int swapGB, string autoReclaim, bool sparseVhd)
        {
            memoryGB = Math.Max(2, Math.Min(128, memoryGB));
            processors = Math.Max(1, Math.Min(Environment.ProcessorCount, processors));
            swapGB = Math.Max(0, Math.Min(64, swapGB));
            if (autoReclaim != "gradual" && autoReclaim != "dropcache" && autoReclaim != "disabled") autoReclaim = "gradual";

            try
            {
                // 保留用户原有的非托管行，安全覆盖 [wsl2] / [experimental] 对应项
                var sb = new StringBuilder();
                sb.AppendLine("# 由 DiskGuard v7 可视化生成的 WSL2 / Docker 内存与性能限流配置");
                sb.AppendLine("[wsl2]");
                sb.AppendLine("memory=" + memoryGB + "GB");
                sb.AppendLine("processors=" + processors);
                sb.AppendLine("swap=" + swapGB + "GB");
                sb.AppendLine();
                sb.AppendLine("[experimental]");
                sb.AppendLine("autoMemoryReclaim=" + autoReclaim);
                sb.AppendLine("sparseVhd=" + (sparseVhd ? "true" : "false"));

                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
                return new FixResult
                {
                    Ok = true,
                    Title = ".wslconfig 已保存",
                    Summary = string.Format("已写入 {0}（内存上限 {1}GB · {2}核 · 自动回收={3} · 稀疏磁盘={4}），下次重启 Docker / WSL 生效",
                        ConfigPath, memoryGB, processors, autoReclaim, sparseVhd ? "开启" : "关闭")
                };
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "保存失败", Summary = ex.Message }; }
        }

        public static FixResult ToggleHibernation(bool enable)
        {
            int ex;
            string arg = enable ? "/h on" : "/h off";
            string outStr = SystemHealth.RunMaybeElevated("powercfg.exe", arg, out ex);
            return new FixResult
            {
                Ok = ex == 0,
                Title = enable ? "休眠功能已开启" : "休眠功能已关闭",
                Summary = ex == 0 ? (enable ? "已重新启用系统休眠" : "已关闭系统休眠，C:\\hiberfil.sys 空间已立即归还给 C 盘") : "未完成（需管理员权限）",
                Detail = outStr
            };
        }

        public static void OpenVirtualMemoryDialog()
        {
            try { Process.Start("SystemPropertiesPerformance.exe"); } catch { }
        }
    }
}
