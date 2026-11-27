// DiskGuard v3 - 扩展安全清理引擎（严格限定可再生成缓存/临时文件，绝不碰用户数据与数据库卷）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DiskGuard
{
    public enum CleanCategory
    {
        All = 0,
        System = 1,
        BrowserApp = 2,
        DevTools = 3,
        ContainerTrash = 4
    }

    public class CleanItem
    {
        public string Id;
        public string Name;
        public string Note;
        public CleanCategory Category;
        public bool DefaultOn;
        public bool IsCaution;
        public string[] InspectPaths;
        public Func<CancellationToken, long> Scan;        // -1: 无法统计 / -2: 服务未运行
        public Func<StringBuilder, long> Clean;
        public long LastSize = -1;
        public long LastScanMs = 0;
        public string StatusOverride;                     // 如 "已清理 1.2 GB" / "扫描中…" / "未运行"

        public string CategoryLabel
        {
            get
            {
                switch (Category)
                {
                    case CleanCategory.System: return "系统缓存";
                    case CleanCategory.BrowserApp: return "浏览器与应用";
                    case CleanCategory.DevTools: return "开发工具";
                    case CleanCategory.ContainerTrash: return "容器与回收站";
                    default: return "其他";
                }
            }
        }
    }

    public static class CleanItemsBuilder
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        static CleanItem DirItem(string id, string name, string note, CleanCategory cat, bool on, bool caution, TimeSpan? older, params string[] paths)
        {
            var ps = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new CleanItem
            {
                Id = id,
                Name = name,
                Note = note,
                Category = cat,
                DefaultOn = on,
                IsCaution = caution,
                InspectPaths = ps,
                Scan = ct =>
                {
                    long sum = 0;
                    foreach (var p in ps)
                    {
                        if (ct.IsCancellationRequested) break;
                        sum += Fs.DirSize(p, older, ct, true);
                    }
                    return sum;
                },
                Clean = log =>
                {
                    long t = 0;
                    foreach (var p in ps)
                    {
                        if (!Directory.Exists(p)) continue;
                        log.AppendLine("  清理 " + p);
                        t += Fs.EmptyDir(p, older, log);
                    }
                    return t;
                }
            };
        }

        public static List<CleanItem> Build(Config c, int? tempAgeDaysOverride = null)
        {
            string la = Util.LocalAppData;
            string ra = Util.RoamingAppData;
            string up = Util.UserProfile;
            string pd = Util.ProgramData;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string goCache = Environment.GetEnvironmentVariable("GOCACHE");
            if (string.IsNullOrEmpty(goCache)) goCache = Path.Combine(la, "go-build");
            string goMod = Environment.GetEnvironmentVariable("GOMODCACHE");
            if (string.IsNullOrEmpty(goMod)) goMod = Path.Combine(up, "go", "pkg", "mod");
            int days = tempAgeDaysOverride.HasValue ? Math.Max(1, tempAgeDaysOverride.Value) : 1;
            var day = TimeSpan.FromDays(days);

            var list = new List<CleanItem>();

            // 1. 系统缓存
            list.Add(DirItem("user-temp", "用户临时文件（1 天前）", "AppData\\Local\\Temp 中 24 小时前的临时文件，被占用自动跳过",
                CleanCategory.System, true, false, day, Path.GetTempPath()));

            list.Add(DirItem("win-temp", "系统临时文件（1 天前）", "Windows\\Temp 中 24 小时前的临时文件",
                CleanCategory.System, true, false, day, Path.Combine(win, "Temp")));

            list.Add(DirItem("wu-download", "Windows 更新下载缓存", "SoftwareDistribution\\Download 已下载的更新包（建议管理员权限）",
                CleanCategory.System, true, false, null, Path.Combine(win, "SoftwareDistribution", "Download")));

            list.Add(DirItem("delivery-opt", "Windows 传递优化缓存", "系统更新 P2P 分发缓存，清理后不影响已安装更新",
                CleanCategory.System, true, false, null,
                Path.Combine(win, "SoftwareDistribution", "DeliveryOptimization"),
                Path.Combine(win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")));

            list.Add(DirItem("crash-wer", "崩溃转储与错误报告", "CrashDumps 与 Windows Error Reporting 历史日志",
                CleanCategory.System, true, false, null,
                Path.Combine(la, "CrashDumps"),
                Path.Combine(la, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(la, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportQueue")));

            list.Add(DirItem("gpu-shader", "GPU 着色器缓存", "DirectX / NVIDIA / AMD 着色器缓存，游戏或软件启动时自动重建",
                CleanCategory.System, true, false, null,
                Path.Combine(la, "D3DSCache"),
                Path.Combine(la, "NVIDIA", "DXCache"),
                Path.Combine(la, "NVIDIA", "GLCache"),
                Path.Combine(la, "AMD", "DxCache")));

            list.Add(UpdatersItem());

            // 2. 浏览器与桌面应用缓存（严格仅限 Cache/Code Cache/GPUCache/CachedData，不动登录态与聊天记录）
            list.Add(BrowserCacheItem(la));
            list.Add(ElectronAppsCacheItem(ra, la));

            // 3. 开发工具缓存
            list.Add(DirItem("go-build", "Go 编译缓存", "go-build 编译中间产物，下次构建自动重建",
                CleanCategory.DevTools, true, false, null, goCache));

            list.Add(DirItem("playwright", "Playwright 浏览器缓存", "ms-playwright 下载的无头浏览器，用到时自动重新下载",
                CleanCategory.DevTools, true, false, null,
                Path.Combine(la, "ms-playwright"),
                Path.Combine(la, "ms-playwright-mcp")));

            var pkgCaches = new List<string>
            {
                Path.Combine(la, "pip", "cache"),
                Path.Combine(la, "uv", "cache"),
                Path.Combine(la, "Yarn", "Cache"),
                Path.Combine(la, "pnpm-cache"),
                Path.Combine(la, "npm-cache"),
                Path.Combine(ra, "npm-cache")
            };
            list.Add(DirItem("pkg-caches", "前端与 Python 下载缓存", "pip / uv / Yarn / npm / pnpm 下载缓存，不影响已安装环境",
                CleanCategory.DevTools, true, false, null, pkgCaches.ToArray()));

            foreach (var p in c.ExtraDirs)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string trimmed = p.Trim();
                string label = Path.GetFileName(trimmed.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(label)) label = trimmed;
                list.Add(DirItem("extra-" + trimmed.ToLowerInvariant(), "自定义缓存：" + label, trimmed,
                    CleanCategory.DevTools, true, false, null, trimmed));
            }

            list.Add(DirItem("go-mod", "Go 模块下载缓存", "go\\pkg\\mod 依赖源码包，清理后首次拉取会重新下载",
                CleanCategory.DevTools, false, true, null, goMod));

            list.Add(DirItem("nuget-gradle-cargo", "NuGet / Gradle / Cargo 缓存", "本地包仓库缓存，清理后重新编译下载较慢，按需勾选",
                CleanCategory.DevTools, false, true, null,
                Path.Combine(la, "NuGet", "v3-cache"),
                Path.Combine(up, ".gradle", "caches", "build-cache-1"),
                Path.Combine(up, ".cargo", "registry", "cache")));

            // 4. 容器与回收站
            list.Add(DockerItem(c));
            list.Add(RecycleBinItem(c));

            return list;
        }

        static List<string> CollectBrowserCacheDirs(string localAppData)
        {
            var targets = new List<string>();
            var roots = new[]
            {
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")
            };
            var subNames = new[] { "Cache", "Code Cache", "GPUCache" };
            var rootCaches = new[] { "ShaderCache", "GrShaderCache" };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var rc in rootCaches)
                {
                    string p = Path.Combine(root, rc);
                    if (Directory.Exists(p)) targets.Add(p);
                }
                try
                {
                    foreach (var prof in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(prof);
                        if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                            continue;
                        foreach (var sub in subNames)
                        {
                            string p = Path.Combine(prof, sub);
                            if (Directory.Exists(p)) targets.Add(p);
                        }
                    }
                }
                catch { }
            }
            return targets;
        }

        static CleanItem BrowserCacheItem(string localAppData)
        {
            var initialDirs = CollectBrowserCacheDirs(localAppData);
            return new CleanItem
            {
                Id = "browser-cache",
                Name = "Chrome / Edge 网页与代码缓存",
                Note = "仅清 Cache / Code Cache / GPUCache，绝不碰书签、密码、Cookie 与扩展",
                Category = CleanCategory.BrowserApp,
                DefaultOn = true,
                IsCaution = false,
                InspectPaths = initialDirs.Count > 0 ? new[] { initialDirs[0] } : new[] { Path.Combine(localAppData, "Google", "Chrome", "User Data") },
                Scan = ct =>
                {
                    var dirs = CollectBrowserCacheDirs(localAppData);
                    long sum = 0;
                    foreach (var d in dirs)
                    {
                        if (ct.IsCancellationRequested) break;
                        sum += Fs.DirSize(d, null, ct, false);
                    }
                    return sum;
                },
                Clean = log =>
                {
                    var dirs = CollectBrowserCacheDirs(localAppData);
                    long t = 0;
                    foreach (var d in dirs)
                    {
                        log.AppendLine("  清理 " + d);
                        t += Fs.EmptyDir(d, null, log);
                    }
                    return t;
                }
            };
        }

        static List<string> CollectElectronCacheDirs(string roaming, string local)
        {
            var targets = new List<string>();
            var cacheSub = new[] { "Cache", "Code Cache", "GPUCache", "CachedData", "CachedExtensionVSIXs" };
            var bases = new[] { roaming, local };
            foreach (var b in bases)
            {
                if (!Directory.Exists(b)) continue;
                string[] apps;
                try { apps = Directory.GetDirectories(b); } catch { continue; }
                foreach (var app in apps)
                {
                    string name = Path.GetFileName(app);
                    if (string.Equals(name, "Microsoft", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Google", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Temp", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Packages", StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var c in cacheSub)
                    {
                        string p = Path.Combine(app, c);
                        if (Directory.Exists(p)) targets.Add(p);
                    }
                    string crash = Path.Combine(app, "Crashpad", "completed");
                    if (Directory.Exists(crash)) targets.Add(crash);
                }
            }
            return targets;
        }

        static CleanItem ElectronAppsCacheItem(string roaming, string local)
        {
            return new CleanItem
            {
                Id = "electron-cache",
                Name = "桌面应用渲染缓存（VS Code / Cursor 等）",
                Note = "扫描 AppData 下应用的 Cache / GPUCache / CachedData / Crashpad，不动工作区与聊天数据",
                Category = CleanCategory.BrowserApp,
                DefaultOn = true,
                IsCaution = false,
                InspectPaths = new[] { roaming },
                Scan = ct =>
                {
                    var dirs = CollectElectronCacheDirs(roaming, local);
                    long sum = 0;
                    var opts = new System.Threading.Tasks.ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Min(Math.Max(2, Environment.ProcessorCount), 6),
                        CancellationToken = ct
                    };
                    try
                    {
                        System.Threading.Tasks.Parallel.ForEach(dirs, opts, d =>
                        {
                            if (ct.IsCancellationRequested) return;
                            long s = Fs.DirSize(d, null, ct, false);
                            Interlocked.Add(ref sum, s);
                        });
                    }
                    catch (OperationCanceledException) { }
                    return sum;
                },
                Clean = log =>
                {
                    var dirs = CollectElectronCacheDirs(roaming, local);
                    long t = 0;
                    foreach (var d in dirs)
                    {
                        long sz = Fs.DirSize(d, null, CancellationToken.None, false);
                        if (sz < 1024 * 1024) continue; // 小于 1M 跳过减少无意义日志
                        log.AppendLine("  清理 " + d);
                        t += Fs.EmptyDir(d, null, log);
                    }
                    return t;
                }
            };
        }

        static void UpdaterTargets(List<string> files, List<string> pendings)
        {
            string[] dirs;
            try { dirs = Directory.GetDirectories(Util.LocalAppData, "*-updater"); } catch { return; }
            foreach (var d in dirs)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(d))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".exe" || ext == ".zip" || ext == ".7z" || ext == ".nupkg" || ext == ".msi")
                            files.Add(f);
                    }
                    var pd = Path.Combine(d, "pending");
                    if (Directory.Exists(pd)) pendings.Add(pd);
                }
                catch { }
            }
        }

        static CleanItem UpdatersItem()
        {
            return new CleanItem
            {
                Id = "updaters",
                Name = "软件更新器残留安装包",
                Note = "AppData\\Local\\*-updater 里已下载的旧安装包与 pending 目录",
                Category = CleanCategory.System,
                DefaultOn = true,
                IsCaution = false,
                InspectPaths = new[] { Util.LocalAppData },
                Scan = ct =>
                {
                    var fs = new List<string>(); var ps = new List<string>();
                    UpdaterTargets(fs, ps);
                    return fs.Sum(f => Util.FileLen(f)) + ps.Sum(p => Fs.DirSize(p, null, ct, false));
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

        static CleanItem DockerItem(Config c)
        {
            return new CleanItem
            {
                Id = "docker",
                Name = "Docker 构建缓存 + 悬空镜像",
                Note = "不动容器与数据卷；释放的空间留在 docker_data.vhdx 内，需后续压缩才归还给宿主机",
                Category = CleanCategory.ContainerTrash,
                DefaultOn = false,
                IsCaution = true,
                InspectPaths = new[] { Path.GetDirectoryName(c.DockerExe) },
                Scan = ct =>
                {
                    string exe = DockerManager.ResolveDockerExe(c);
                    if (string.IsNullOrEmpty(exe)) return -1;
                    if (!DockerManager.IsDockerRunning(exe)) return -2;
                    int ex;
                    var o = Util.Run(exe, "builder du", 15000, out ex);
                    if (ex != 0) return -1;
                    var m = Regex.Match(o, @"Reclaimable:\s*(\S+)");
                    return m.Success ? Util.ParseDockerSize(m.Groups[1].Value) : -1;
                },
                Clean = log =>
                {
                    string exe = DockerManager.ResolveDockerExe(c);
                    if (string.IsNullOrEmpty(exe)) { log.AppendLine("  未找到 docker.exe"); return 0; }
                    if (!DockerManager.IsDockerRunning(exe)) { log.AppendLine("  Docker 引擎未运行，已跳过"); return 0; }
                    int ex;
                    var o1 = Util.Run(exe, "builder prune -a -f", 1800000, out ex);
                    log.AppendLine("  builder prune exit=" + ex + "  " + Util.LastLine(o1));
                    var o2 = Util.Run(exe, "image prune -f", 600000, out ex);
                    log.AppendLine("  image prune exit=" + ex + "  " + Util.LastLine(o2));
                    long t = 0;
                    var m = Regex.Match(o1, @"Total:\s*(\S+)");
                    if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
                    m = Regex.Match(o2, @"Total reclaimed space:\s*(\S+)");
                    if (m.Success) t += Math.Max(0, Util.ParseDockerSize(m.Groups[1].Value));
                    return t;
                }
            };
        }

        static CleanItem RecycleBinItem(Config c)
        {
            Func<long> query = () =>
            {
                long t = 0;
                foreach (var d in c.EffectiveDrives())
                {
                    var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                    try { if (SHQueryRecycleBin(d + ":\\", ref info) == 0) t += info.i64Size; } catch { }
                }
                return t;
            };
            return new CleanItem
            {
                Id = "recycle-bin",
                Name = "系统回收站",
                Note = "清空后无法恢复，请确认里面没有需要找回的文件",
                Category = CleanCategory.ContainerTrash,
                DefaultOn = false,
                IsCaution = true,
                InspectPaths = new string[0],
                Scan = ct => query(),
                Clean = log =>
                {
                    long before = query();
                    foreach (var d in c.EffectiveDrives())
                    {
                        try { SHEmptyRecycleBin(IntPtr.Zero, d + ":\\", 7); } catch { }
                    }
                    return Math.Max(0, before - query());
                }
            };
        }
    }
}
