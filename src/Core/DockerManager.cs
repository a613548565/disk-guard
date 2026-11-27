// DiskGuard v5 - Docker 专治中心（镜像/容器/孤立卷高速透视 + fstrim & VHDX 停机瘦身向导与容器自动恢复）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace DiskGuard
{
    public class DockerContainerInfo
    {
        public string Id;
        public string Name;
        public string Image;
        public string Status;
        public bool Running;
        public long SizeBytes;
    }

    public class DockerImageInfo
    {
        public string Id;
        public string RepoTag;
        public long SizeBytes;
        public string CreatedSince;
        public bool InUse;
        public bool IsDangling;
    }

    public class DockerVolumeInfo
    {
        public string Name;
        public string ProjectHint;
        public long SizeBytes;
        public bool Dangling;
    }

    public class DockerOverview
    {
        public bool Installed;
        public bool Running;
        public string VhdxPath = "";
        public long VhdxPhysicalBytes;
        public long VhdxInternalUsedBytes;
        public long CompactReclaimableBytes;
        public long BuildCacheBytes;
        public bool IsAdmin;
        public string Error = "";
        public List<DockerContainerInfo> RunningContainers = new List<DockerContainerInfo>();
        public List<DockerContainerInfo> StoppedContainers = new List<DockerContainerInfo>();
        public List<DockerImageInfo> UnusedImages = new List<DockerImageInfo>();
        public List<DockerVolumeInfo> DanglingVolumes = new List<DockerVolumeInfo>();
    }

    public class VhdxCompactResult
    {
        public bool Success;
        public string Message;
        public long VhdxBeforeBytes;
        public long VhdxAfterBytes;
        public long DriveFreedBytes;
        public List<string> RestoredContainers = new List<string>();
        public List<string> FailedContainers = new List<string>();
    }

    public static class DockerManager
    {
        static string cachedDockerExe = "";

        // 6 级全自动探测 docker.exe（兼容任意盘符自定义安装路径、PATH、运行进程与注册表）
        public static string ResolveDockerExe(Config c)
        {
            if (c != null && !string.IsNullOrWhiteSpace(c.DockerExe) && File.Exists(c.DockerExe))
                return c.DockerExe;
            if (!string.IsNullOrEmpty(cachedDockerExe) && File.Exists(cachedDockerExe))
                return cachedDockerExe;

            var candidates = new List<string>();

            // 1. 从 PATH 环境变量查找 docker.exe
            try
            {
                string envPath = (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "") + ";" +
                                 (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "") + ";" +
                                 (Environment.GetEnvironmentVariable("PATH") ?? "");
                foreach (var dir in envPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string d = dir.Trim().Trim('"');
                    if (string.IsNullOrEmpty(d)) continue;
                    try { candidates.Add(Path.Combine(d, "docker.exe")); } catch { }
                }
            }
            catch { }

            // 2. 从正在运行的 Docker Desktop / com.docker.backend 进程推导安装目录
            try
            {
                foreach (var pname in new[] { "Docker Desktop", "com.docker.backend" })
                {
                    foreach (var p in Process.GetProcessesByName(pname))
                    {
                        try
                        {
                            string fn = p.MainModule != null ? p.MainModule.FileName : "";
                            if (string.IsNullOrEmpty(fn)) continue;
                            string dir = Path.GetDirectoryName(fn);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                candidates.Add(Path.Combine(dir, "resources", "bin", "docker.exe"));
                                candidates.Add(Path.Combine(dir, "bin", "docker.exe"));
                                string parent = Path.GetDirectoryName(dir);
                                if (!string.IsNullOrEmpty(parent))
                                    candidates.Add(Path.Combine(parent, "resources", "bin", "docker.exe"));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 3. 从注册表读取 Docker Desktop 安装路径
            TryAddRegistryDockerPaths(Registry.LocalMachine, @"SOFTWARE\Docker Inc.\Docker\1.0", candidates);
            TryAddRegistryDockerPaths(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Docker Desktop", candidates);
            TryAddRegistryDockerPaths(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Docker Desktop", candidates);

            // 4. 扫描全部本地固定磁盘的常见 Docker 安装位置
            foreach (var drv in DriveStat.AllFixedDrives())
            {
                string r = drv + @":\";
                candidates.Add(Path.Combine(r, @"Program Files\Docker\Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"Docker\Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"Program Files\Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"develop\Docker\Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"Software\Docker\Docker\resources\bin\docker.exe"));
                candidates.Add(Path.Combine(r, @"Tools\Docker\Docker\resources\bin\docker.exe"));
            }

            foreach (var p in candidates)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    cachedDockerExe = p;
                    if (c != null) c.DockerExe = p;
                    return p;
                }
            }

            // 5. 兜底使用 where.exe docker
            try
            {
                int ex;
                string w = Util.Run("where.exe", "docker", 2500, out ex);
                if (ex == 0 && !string.IsNullOrWhiteSpace(w))
                {
                    foreach (var line in w.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = line.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(t))
                        {
                            cachedDockerExe = t;
                            if (c != null) c.DockerExe = t;
                            return t;
                        }
                    }
                }
            }
            catch { }

            return "";
        }

        static void TryAddRegistryDockerPaths(RegistryKey root, string subKey, List<string> candidates)
        {
            try
            {
                using (var k = root.OpenSubKey(subKey, false))
                {
                    if (k == null) return;
                    foreach (var vn in new[] { "BinPath", "AppPath", "InstallLocation" })
                    {
                        string v = (k.GetValue(vn) as string ?? "").Trim().Trim('"');
                        if (string.IsNullOrEmpty(v)) continue;
                        candidates.Add(Path.Combine(v, "docker.exe"));
                        candidates.Add(Path.Combine(v, "resources", "bin", "docker.exe"));
                        candidates.Add(Path.Combine(v, "Docker", "resources", "bin", "docker.exe"));
                    }
                }
            }
            catch { }
        }

        public static string ResolveDockerDesktopExe(string dockerExe)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(dockerExe))
            {
                try
                {
                    string binDir = Path.GetDirectoryName(dockerExe);
                    string resDir = !string.IsNullOrEmpty(binDir) ? Path.GetDirectoryName(binDir) : "";
                    string appDir = !string.IsNullOrEmpty(resDir) ? Path.GetDirectoryName(resDir) : "";
                    if (!string.IsNullOrEmpty(appDir)) list.Add(Path.Combine(appDir, "Docker Desktop.exe"));
                }
                catch { }
            }
            foreach (var drv in DriveStat.AllFixedDrives())
            {
                list.Add(drv + @":\Program Files\Docker\Docker\Docker Desktop.exe");
                list.Add(drv + @":\Docker\Docker\Docker Desktop.exe");
            }
            return list.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "";
        }

        public static bool IsDockerRunning(string dockerExe)
        {
            if (Win32Native.IsDockerPipeReady()) return true;
            if (string.IsNullOrEmpty(dockerExe) || !File.Exists(dockerExe)) return false;
            try
            {
                int ex;
                Util.Run(dockerExe, "info --format \"{{.ServerVersion}}\"", 4000, out ex);
                return ex == 0;
            }
            catch { return false; }
        }

        public static string FindVhdxPath()
        {
            var candidates = new List<string>();
            // 1. 读取新版 settings-store.json 与旧版 settings.json
            foreach (var cfgFile in new[] {
                Path.Combine(Util.RoamingAppData, "Docker", "settings-store.json"),
                Path.Combine(Util.RoamingAppData, "Docker", "settings.json") })
            {
                try
                {
                    if (!File.Exists(cfgFile)) continue;
                    string txt = File.ReadAllText(cfgFile, Encoding.UTF8);
                    foreach (Match m in Regex.Matches(txt, "\"(?:CustomWslDistroDir|DataFolder|dataFolder)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    {
                        string dir = m.Groups[1].Value.Replace("\\\\", "\\");
                        candidates.Add(Path.Combine(dir, "disk", "docker_data.vhdx"));
                        candidates.Add(Path.Combine(dir, "docker_data.vhdx"));
                        candidates.Add(Path.Combine(dir, "ext4.vhdx"));
                    }
                }
                catch { }
            }

            // 2. 从 Windows WSL 注册表 Lxss 精准读取 docker-desktop / docker-desktop-data 实际 BasePath
            try
            {
                using (var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss", false))
                {
                    if (lxss != null)
                    {
                        foreach (var sub in lxss.GetSubKeyNames())
                        {
                            using (var k = lxss.OpenSubKey(sub, false))
                            {
                                if (k == null) continue;
                                string dn = (k.GetValue("DistributionName") as string ?? "");
                                if (dn.IndexOf("docker", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string bp = (k.GetValue("BasePath") as string ?? "").Replace(@"\\?\", "").Trim();
                                if (string.IsNullOrEmpty(bp)) continue;
                                candidates.Add(Path.Combine(bp, "ext4.vhdx"));
                                candidates.Add(Path.Combine(bp, "docker_data.vhdx"));
                                string parent = Path.GetDirectoryName(bp.TrimEnd('\\'));
                                if (!string.IsNullOrEmpty(parent))
                                    candidates.Add(Path.Combine(parent, "disk", "docker_data.vhdx"));
                            }
                        }
                    }
                }
            }
            catch { }

            candidates.Add(Path.Combine(Util.LocalAppData, "Docker", "wsl", "disk", "docker_data.vhdx"));
            candidates.Add(Path.Combine(Util.LocalAppData, "Docker", "wsl", "data", "ext4.vhdx"));
            candidates.Add(@"E:\develop\docker\DockerDesktopWSL\disk\docker_data.vhdx");

            foreach (var p in candidates)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            return "";
        }

        public static DockerOverview Inspect(Config c, bool includeVolumeSizes)
        {
            string dockerExe = ResolveDockerExe(c);
            var ov = new DockerOverview
            {
                Installed = !string.IsNullOrEmpty(dockerExe),
                IsAdmin = Util.IsAdmin(),
                VhdxPath = FindVhdxPath()
            };
            if (!string.IsNullOrEmpty(ov.VhdxPath))
                ov.VhdxPhysicalBytes = Util.FileLen(ov.VhdxPath);

            if (!ov.Installed) { ov.Error = "未检测到 Docker CLI（如已安装请将其加入 PATH）"; return ov; }
            if (!IsDockerRunning(dockerExe)) { ov.Running = false; ov.Error = "Docker Desktop 未运行"; return ov; }
            ov.Running = true;

            // 并发执行 5 路独立查询（ps / images / builder du / volume ls / wsl df+du），加载提速约 3 倍
            string psOut = "", psImgIds = "", imgOut = "", duOut = "", volOut = "", dfOut = "", vduOut = "";
            int exPs = -1, exPsImg = -1, exImg = -1, exDu = -1, exVol = -1, exDf = -1, exVdu = -1;

            System.Threading.Tasks.Parallel.Invoke(
                () => { psOut = Util.Run(dockerExe, "ps -a --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.State}}\"", 15000, out exPs); },
                () => { psImgIds = Util.Run(dockerExe, "ps -a --no-trunc --format \"{{.Image}}\"", 10000, out exPsImg); },
                () => { imgOut = Util.Run(dockerExe, "images --format \"{{.ID}}|{{.Repository}}:{{.Tag}}|{{.Size}}|{{.CreatedSince}}\"", 15000, out exImg); },
                () => { duOut = Util.Run(dockerExe, "builder du", 15000, out exDu); },
                () => { volOut = Util.Run(dockerExe, "volume ls -q -f dangling=true", 15000, out exVol); },
                () =>
                {
                    dfOut = Util.Run("wsl.exe", "-d docker-desktop -u root sh -c \"df -B1 /mnt/docker-desktop-disk 2>/dev/null | tail -n 1\"", 10000, out exDf);
                    if (includeVolumeSizes)
                        vduOut = Util.Run("wsl.exe", "-d docker-desktop -u root sh -c \"du -sb /mnt/docker-desktop-disk/data/docker/volumes/*/_data 2>/dev/null\"", 25000, out exVdu);
                }
            );

            var usedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exPs == 0)
            {
                foreach (var line in psOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|');
                    if (p.Length < 5) continue;
                    var ci = new DockerContainerInfo
                    {
                        Id = p[0].Trim(),
                        Name = p[1].Trim(),
                        Image = p[2].Trim(),
                        Status = p[3].Trim(),
                        Running = string.Equals(p[4].Trim(), "running", StringComparison.OrdinalIgnoreCase)
                    };
                    if (!string.IsNullOrEmpty(ci.Image)) usedImages.Add(ci.Image);
                    if (ci.Running) ov.RunningContainers.Add(ci);
                    else ov.StoppedContainers.Add(ci);
                }
            }
            if (exPsImg == 0)
            {
                foreach (var l in psImgIds.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    usedImages.Add(l.Trim());
            }

            var seenImg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exImg == 0)
            {
                foreach (var line in imgOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    string id = p[0].Trim();
                    string repoTag = p[1].Trim();
                    if (!seenImg.Add(id + "|" + repoTag)) continue;
                    bool inUse = usedImages.Contains(repoTag) || usedImages.Contains(id) ||
                                 usedImages.Any(u => u.StartsWith("sha256:" + id, StringComparison.OrdinalIgnoreCase));
                    if (inUse) continue;
                    ov.UnusedImages.Add(new DockerImageInfo
                    {
                        Id = id,
                        RepoTag = repoTag,
                        SizeBytes = Math.Max(0, Util.ParseDockerSize(p[2].Trim())),
                        CreatedSince = p[3].Trim(),
                        InUse = false,
                        IsDangling = repoTag.Contains("<none>")
                    });
                }
                ov.UnusedImages = ov.UnusedImages.OrderByDescending(x => x.SizeBytes).ToList();
            }

            if (exDu == 0)
            {
                var m = Regex.Match(duOut, @"Reclaimable:\s*(\S+)");
                if (m.Success) ov.BuildCacheBytes = Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
            }

            var volNames = new List<string>();
            if (exVol == 0)
            {
                foreach (var l in volOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    volNames.Add(l.Trim());
            }

            if (exDf == 0)
            {
                var parts = dfOut.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                long usedB;
                if (parts.Length >= 3 && long.TryParse(parts[2], out usedB))
                    ov.VhdxInternalUsedBytes = usedB;
            }
            if (ov.VhdxPhysicalBytes > 0 && ov.VhdxInternalUsedBytes > 0)
                ov.CompactReclaimableBytes = Math.Max(0, ov.VhdxPhysicalBytes - ov.VhdxInternalUsedBytes);

            var volSizeMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (includeVolumeSizes && !string.IsNullOrEmpty(vduOut))
            {
                foreach (var line in vduOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 2) continue;
                    long sz;
                    if (!long.TryParse(p[0], out sz)) continue;
                    var segs = p[1].Split('/');
                    if (segs.Length >= 2) volSizeMap[segs[segs.Length - 2]] = sz;
                }
            }

            foreach (var vn in volNames)
            {
                long sz = 0;
                volSizeMap.TryGetValue(vn, out sz);
                ov.DanglingVolumes.Add(new DockerVolumeInfo
                {
                    Name = vn,
                    ProjectHint = InferVolumeProject(vn),
                    SizeBytes = sz,
                    Dangling = true
                });
            }
            ov.DanglingVolumes = ov.DanglingVolumes.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.Name).ToList();
            return ov;
        }

        static string InferVolumeProject(string volName)
        {
            if (string.IsNullOrEmpty(volName)) return "匿名卷";
            if (Regex.IsMatch(volName, "^[0-9a-f]{48,64}$")) return "匿名哈希卷";
            int idx = volName.IndexOf('_');
            if (idx > 1) return volName.Substring(0, idx);
            return "具名卷";
        }

        public static long PruneSafeCache(Config c, StringBuilder log)
        {
            string exe = ResolveDockerExe(c);
            if (string.IsNullOrEmpty(exe) || !IsDockerRunning(exe)) return 0;
            int ex;
            var o1 = Util.Run(exe, "builder prune -a -f", 1800000, out ex);
            log.AppendLine("  docker builder prune exit=" + ex + " " + Util.LastLine(o1));
            var o2 = Util.Run(exe, "image prune -f", 600000, out ex);
            log.AppendLine("  docker image prune exit=" + ex + " " + Util.LastLine(o2));
            long t = 0;
            var m = Regex.Match(o1, @"Total:\s*(\S+)");
            if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
            m = Regex.Match(o2, @"Total reclaimed space:\s*(\S+)");
            if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
            return t;
        }

        public static int RemoveStoppedContainers(Config c, List<string> ids, StringBuilder log)
        {
            string exe = ResolveDockerExe(c);
            if (ids == null || ids.Count == 0 || string.IsNullOrEmpty(exe) || !IsDockerRunning(exe)) return 0;
            // 安全核验：只删除非 running 容器，绝不用 -f 强杀运行中容器
            int removed = 0;
            foreach (var id in ids)
            {
                if (!Regex.IsMatch(id, "^[a-zA-Z0-9_.-]+$")) continue;
                int ex;
                var o = Util.Run(exe, "rm " + id, 30000, out ex);
                if (ex == 0) { removed++; log.AppendLine("  删除已停止容器 " + id); }
                else log.AppendLine("  跳过容器 " + id + "：" + Util.LastLine(o));
            }
            return removed;
        }

        public static int RemoveUnusedImages(Config c, List<string> ids, StringBuilder log)
        {
            string exe = ResolveDockerExe(c);
            if (ids == null || ids.Count == 0 || string.IsNullOrEmpty(exe) || !IsDockerRunning(exe)) return 0;
            int removed = 0;
            foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(id, "^[a-zA-Z0-9_:./-]+$")) continue;
                int ex;
                var o = Util.Run(exe, "rmi " + id, 60000, out ex);
                if (ex == 0) { removed++; log.AppendLine("  删除镜像 " + id); }
                else log.AppendLine("  跳过镜像 " + id + "：" + Util.LastLine(o));
            }
            return removed;
        }

        public static int RemoveDanglingVolumes(Config c, List<string> names, StringBuilder log)
        {
            string exe = ResolveDockerExe(c);
            if (names == null || names.Count == 0 || string.IsNullOrEmpty(exe) || !IsDockerRunning(exe)) return 0;
            int removed = 0;
            foreach (var n in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(n, "^[a-zA-Z0-9_.-]+$")) continue;
                int ex;
                var o = Util.Run(exe, "volume rm " + n, 30000, out ex);
                if (ex == 0) { removed++; log.AppendLine("  删除孤立卷 " + n); }
                else log.AppendLine("  跳过卷 " + n + "：" + Util.LastLine(o));
            }
            return removed;
        }

        // VHDX 瘦身向导：记录运行中容器 → WSL fstrim → 停 Docker/WSL → diskpart compact → 重启 Docker → 恢复容器
        public static VhdxCompactResult CompactVhdx(Config c, Action<string, int> onStep, StringBuilder log)
        {
            var res = new VhdxCompactResult();
            string vhdx = FindVhdxPath();
            if (string.IsNullOrEmpty(vhdx) || !File.Exists(vhdx))
            {
                res.Message = "未找到 docker_data.vhdx 文件";
                return res;
            }

            string driveLetter = Path.GetPathRoot(vhdx).Substring(0, 1).ToUpperInvariant();
            long driveFreeBefore = ReadDriveFree(driveLetter);
            res.VhdxBeforeBytes = Util.FileLen(vhdx);
            log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Docker VHDX 瘦身 ====");
            log.AppendLine("  目标文件：" + vhdx + "（压缩前 " + Util.Fmt(res.VhdxBeforeBytes) + "）");

            // Step 1: 记录当前运行中容器
            string dockerExe = ResolveDockerExe(c);
            var runningBefore = new List<string>();
            int ex;
            if (!string.IsNullOrEmpty(dockerExe) && IsDockerRunning(dockerExe))
            {
                if (onStep != null) onStep("1/6 正在记录当前运行中的容器清单…", 10);
                string ps = Util.Run(dockerExe, "ps --format \"{{.Names}}\"", 15000, out ex);
                if (ex == 0)
                {
                    foreach (var l in ps.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        runningBefore.Add(l.Trim());
                }
                log.AppendLine("  停机前运行中容器(" + runningBefore.Count + ")：" + string.Join(", ", runningBefore.ToArray()));

                // Step 2: 在 WSL 内部执行 fstrim 通知虚拟磁盘释放空闲块
                if (onStep != null) onStep("2/6 正在 WSL 内部执行 fstrim 标记空闲块…", 25);
                string trimOut = Util.Run("wsl.exe", "-d docker-desktop -u root fstrim -v /mnt/docker-desktop-disk", 300000, out ex);
                log.AppendLine("  fstrim exit=" + ex + " " + Util.LastLine(trimOut));
            }

            // Step 3: 停止 Docker Desktop & WSL
            if (onStep != null) onStep("3/6 正在停止 Docker Desktop 与 WSL 释放虚拟磁盘锁…", 42);
            try
            {
                foreach (var pname in new[] { "Docker Desktop", "com.docker.backend", "com.docker.build", "vpnkit" })
                {
                    foreach (var p in Process.GetProcessesByName(pname))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { }
            Util.Run("wsl.exe", "--shutdown", 30000, out ex);
            Thread.Sleep(3500);

            // Step 4: 执行 diskpart compact vdisk（无管理员权限时通过 UAC 提权执行）
            if (onStep != null) onStep("4/6 正在压缩 docker_data.vhdx（体积较大，通常需 2~10 分钟）…", 58);
            string scriptPath = Path.Combine(Util.LogDir, "compact_vhdx.dp");
            string script = "select vdisk file=\"" + vhdx + "\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\nexit\r\n";
            File.WriteAllText(scriptPath, script, Encoding.ASCII);

            int dpExit = -1;
            if (Util.IsAdmin())
            {
                string dpOut = Util.Run("diskpart.exe", "/s \"" + scriptPath + "\"", 3600000, out dpExit);
                log.AppendLine("  diskpart exit=" + dpExit + " " + Util.LastLine(dpOut));
            }
            else
            {
                try
                {
                    var psi = new ProcessStartInfo("diskpart.exe", "/s \"" + scriptPath + "\"")
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (p != null) { p.WaitForExit(3600000); dpExit = p.ExitCode; }
                    }
                    log.AppendLine("  diskpart(UAC) exit=" + dpExit);
                }
                catch (Exception uex)
                {
                    log.AppendLine("  diskpart UAC 取消或失败：" + uex.Message);
                }
            }

            res.VhdxAfterBytes = Util.FileLen(vhdx);
            log.AppendLine("  压缩后大小：" + Util.Fmt(res.VhdxAfterBytes));

            // Step 5: 重新启动 Docker Desktop
            if (onStep != null) onStep("5/6 正在重新启动 Docker Desktop 并等待引擎就绪…", 82);
            string desktopExe = ResolveDockerDesktopExe(dockerExe);
            if (!string.IsNullOrEmpty(desktopExe) && File.Exists(desktopExe))
            {
                try { Process.Start(desktopExe); } catch { }
                for (int i = 0; i < 45; i++)
                {
                    Thread.Sleep(2000);
                    if (!string.IsNullOrEmpty(dockerExe) && IsDockerRunning(dockerExe)) break;
                }
            }

            // Step 6: 自动恢复停机前运行中的容器（含 restart=no 的 UAT 容器）
            if (runningBefore.Count > 0 && !string.IsNullOrEmpty(dockerExe) && IsDockerRunning(dockerExe))
            {
                if (onStep != null) onStep("6/6 正在自动恢复之前运行中的 " + runningBefore.Count + " 个容器…", 92);
                var nowRunning = new HashSet<string>(
                    Util.Run(dockerExe, "ps --format \"{{.Names}}\"", 15000, out ex)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var name in runningBefore)
                {
                    if (nowRunning.Contains(name))
                    {
                        res.RestoredContainers.Add(name);
                        continue;
                    }
                    Util.Run(dockerExe, "start " + name, 30000, out ex);
                    if (ex == 0) { res.RestoredContainers.Add(name); log.AppendLine("  已恢复容器 " + name); }
                    else { res.FailedContainers.Add(name); log.AppendLine("  容器恢复失败 " + name); }
                }
            }

            long driveFreeAfter = ReadDriveFree(driveLetter);
            res.DriveFreedBytes = Math.Max(0, driveFreeAfter - driveFreeBefore);
            long vhdxDelta = Math.Max(0, res.VhdxBeforeBytes - res.VhdxAfterBytes);
            res.Success = dpExit == 0;
            res.Message = res.Success
                ? string.Format("压缩完成：vhdx {0} → {1}（缩减 {2}），{3}: 盘可用空间增加 {4}",
                    Util.Fmt(res.VhdxBeforeBytes), Util.Fmt(res.VhdxAfterBytes), Util.Fmt(vhdxDelta), driveLetter, Util.Fmt(res.DriveFreedBytes))
                : "压缩未完成（可能取消了管理员授权或虚拟磁盘仍被占用）";
            log.AppendLine("合计：删除文件 " + Util.Fmt(vhdxDelta) + "，磁盘可用空间变化 " + Util.FmtDelta(driveFreeAfter - driveFreeBefore));
            log.AppendLine();
            return res;
        }

        static long ReadDriveFree(string letter)
        {
            try { var d = new DriveInfo(letter + ":\\"); return d.IsReady ? d.AvailableFreeSpace : 0; }
            catch { return 0; }
        }
    }
}
