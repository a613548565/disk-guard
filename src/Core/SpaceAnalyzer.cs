// DiskGuard v3 - 高速空间透视引擎（大目录钻取 / 大文件排行 / 项目构建产物智能识别）
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGuard
{
    public enum SpaceEntryKind
    {
        Directory,
        LargeFile,
        BuildArtifact
    }

    public class SpaceEntry
    {
        public string FullPath;
        public string DisplayName;
        public string ParentLabel;
        public string Tag;             // 如 "目录" / "VHDX" / "node_modules" / "target"
        public SpaceEntryKind Kind;
        public long Size;
        public DateTime LastWrite;
        public bool IsProtected;
        public string Advice;          // 给用户的人话处理建议（如 docker_data.vhdx / pagefile.sys / WinSxS）
        public bool Checked;
        public string StatusOverride;
    }

    public class SpaceScanProgress
    {
        public long ScannedFiles;
        public long ScannedDirs;
        public long ScannedBytes;
        public string CurrentHint;
    }

    public class SpaceScanResult
    {
        public string RootPath;
        public long TotalScannedBytes;
        public long ElapsedMs;
        public bool Cancelled;
        public List<SpaceEntry> Directories = new List<SpaceEntry>();
        public List<SpaceEntry> LargeFiles = new List<SpaceEntry>();
        public List<SpaceEntry> BuildArtifacts = new List<SpaceEntry>();
    }

    public static class SpaceAnalyzer
    {
        static readonly HashSet<string> SkipSystemDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Recovery", "Config.Msi", "$WinREAgent"
        };

        // 软件安装目录关键字：扫描构建产物时强制排除，避免误伤软件自带 node_modules
        static readonly string[] ExcludedArtifactPathTokens = new[]
        {
            @"\windows\", @"\program files\", @"\program files (x86)\", @"\programdata\",
            @"\appdata\", @"\node_gobal\", @"\node_global\", @"\hbuilder", @"\clawx\",
            @"\tracememo\", @"\.git\", @"\$recycle.bin\", @"\system volume information\"
        };

        static readonly Dictionary<string, string[]> ArtifactMarkers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "node_modules", new[] { "package.json" } },
            { "target", new[] { "Cargo.toml", "pom.xml" } },
            { "dist", new[] { "package.json", "tsconfig.json", "vite.config.ts", "vite.config.js", "pyproject.toml" } },
            { "build", new[] { "package.json", "build.gradle", "build.gradle.kts", "CMakeLists.txt" } },
            { ".next", new[] { "package.json", "next.config.js", "next.config.mjs", "next.config.ts" } },
            { ".nuxt", new[] { "package.json", "nuxt.config.ts", "nuxt.config.js" } },
            { ".venv", new[] { "pyproject.toml", "requirements.txt", "setup.py", "Pipfile" } },
            { "venv", new[] { "pyproject.toml", "requirements.txt", "setup.py", "Pipfile" } }
        };

        // 模式 A：大目录 & 大文件并行扫描（支持目录钻取）
        public static SpaceScanResult ScanLargeItems(string rootPath, long minFileBytes, CancellationToken ct, Action<SpaceScanProgress> onProgress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new SpaceScanResult { RootPath = rootPath };
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return res;

            string normRoot = rootPath.EndsWith("\\") ? rootPath : rootPath + "\\";
            var topDirs = new List<string>();
            var largeFiles = new ConcurrentBag<SpaceEntry>();
            long directFileBytes = 0;
            long scannedFiles = 0;
            long scannedDirs = 1;
            long lastReportTick = Environment.TickCount;

            // 先枚举根目录一层
            EnumDirRaw(normRoot, (name, isDir, isReparse, size, ftWrite) =>
            {
                if (isReparse) return;
                string full = normRoot + name;
                if (isDir)
                {
                    if (!SkipSystemDirs.Contains(name)) topDirs.Add(full);
                }
                else
                {
                    Interlocked.Increment(ref scannedFiles);
                    directFileBytes += size;
                    if (size >= minFileBytes)
                    {
                        largeFiles.Add(MakeFileEntry(full, name, normRoot, size, ftWrite));
                    }
                }
            });

            var dirResults = new ConcurrentBag<SpaceEntry>();
            int maxDop = Math.Min(Math.Max(2, Environment.ProcessorCount), 6);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct };

            try
            {
                Parallel.ForEach(topDirs, opts, dir =>
                {
                    if (ct.IsCancellationRequested) return;
                    long dirSize = 0;
                    DateTime latest = DateTime.MinValue;
                    var stack = new Stack<string>(64);
                    stack.Push(dir);

                    while (stack.Count > 0)
                    {
                        if (ct.IsCancellationRequested) return;
                        string cur = stack.Pop();
                        Interlocked.Increment(ref scannedDirs);
                        string prefix = cur.EndsWith("\\") ? cur : cur + "\\";

                        EnumDirRaw(prefix, (name, isDir, isReparse, size, ftWrite) =>
                        {
                            if (isReparse) return;
                            string full = prefix + name;
                            if (isDir)
                            {
                                if (!SkipSystemDirs.Contains(name)) stack.Push(full);
                            }
                            else
                            {
                                Interlocked.Increment(ref scannedFiles);
                                dirSize += size;
                                if (size >= minFileBytes)
                                {
                                    largeFiles.Add(MakeFileEntry(full, name, prefix, size, ftWrite));
                                }
                            }
                        });

                        int nowTick = Environment.TickCount;
                        if (onProgress != null && Math.Abs(nowTick - lastReportTick) > 120)
                        {
                            Interlocked.Exchange(ref lastReportTick, nowTick);
                            onProgress(new SpaceScanProgress
                            {
                                ScannedFiles = Interlocked.Read(ref scannedFiles),
                                ScannedDirs = Interlocked.Read(ref scannedDirs),
                                CurrentHint = Path.GetFileName(dir)
                            });
                        }
                    }

                    try { latest = Directory.GetLastWriteTime(dir); } catch { }
                    dirResults.Add(new SpaceEntry
                    {
                        FullPath = dir,
                        DisplayName = Path.GetFileName(dir),
                        ParentLabel = normRoot,
                        Tag = "目录",
                        Kind = SpaceEntryKind.Directory,
                        Size = dirSize,
                        LastWrite = latest,
                        IsProtected = Fs.IsProtected(dir) || Fs.IsSystemCriticalPath(dir),
                        Advice = BuildAdvice(dir, Path.GetFileName(dir))
                    });
                });
            }
            catch (OperationCanceledException) { res.Cancelled = true; }

            res.Directories = dirResults.OrderByDescending(x => x.Size).Take(60).ToList();
            res.LargeFiles = largeFiles.OrderByDescending(x => x.Size).Take(100).ToList();
            res.TotalScannedBytes = directFileBytes + dirResults.Sum(x => x.Size);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        // 全盘多分区并行扫超大文件（如 C + D + E 一起扫）
        public static SpaceScanResult ScanLargeFilesMultiDrive(string[] drives, long minFileBytes, CancellationToken ct, Action<SpaceScanProgress> onProgress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new SpaceScanResult { RootPath = "ALL" };
            var allFiles = new List<SpaceEntry>();
            long total = 0;
            foreach (var d in drives)
            {
                if (ct.IsCancellationRequested) { res.Cancelled = true; break; }
                string root = d.TrimEnd(':', '\\') + ":\\";
                if (!Directory.Exists(root)) continue;
                var part = ScanLargeItems(root, minFileBytes, ct, onProgress);
                allFiles.AddRange(part.LargeFiles);
                total += part.TotalScannedBytes;
            }
            res.LargeFiles = allFiles.OrderByDescending(x => x.Size).Take(120).ToList();
            res.TotalScannedBytes = res.LargeFiles.Sum(x => x.Size);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        public static List<string> FindCodeRoots(string[] drives)
        {
            var roots = new List<string>();
            var names = new[] { "code", "projects", "workspace", "repos" };
            foreach (var d in drives)
            {
                string r = d.TrimEnd(':', '\\') + ":\\";
                foreach (var n in names)
                {
                    string p = Path.Combine(r, n);
                    if (Directory.Exists(p)) roots.Add(p);
                }
            }
            if (roots.Count == 0 && Directory.Exists(Util.UserProfile)) roots.Add(Util.UserProfile);
            return roots;
        }

        // 模式 B：项目构建产物智能扫描（仅识别有项目标志文件的可再生成目录）
        public static SpaceScanResult ScanBuildArtifacts(string rootPath, int maxDepth, CancellationToken ct, Action<SpaceScanProgress> onProgress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new SpaceScanResult { RootPath = rootPath };
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return res;

            var matchedPaths = new ConcurrentBag<Tuple<string, string>>(); // fullPath, artifactType
            long scannedDirs = 0;
            int lastReportTick = Environment.TickCount;

            string normRoot = rootPath.TrimEnd('\\');
            var queue = new Queue<Tuple<string, int>>();
            queue.Enqueue(Tuple.Create(normRoot, 0));

            while (queue.Count > 0 && !ct.IsCancellationRequested)
            {
                var item = queue.Dequeue();
                string dir = item.Item1;
                int depth = item.Item2;
                if (depth > maxDepth) continue;
                scannedDirs++;

                string relDir = dir.Length > normRoot.Length ? dir.Substring(normRoot.Length) : "";
                if (relDir.Length > 0 && IsExcludedArtifactPath(relDir)) continue;

                string prefix = dir.EndsWith("\\") ? dir : dir + "\\";
                var children = new List<string>();
                var siblingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                EnumDirRaw(prefix, (name, isDir, isReparse, size, ftWrite) =>
                {
                    if (isReparse) return;
                    if (isDir)
                    {
                        if (!SkipSystemDirs.Contains(name) && !string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                            children.Add(name);
                    }
                    else
                    {
                        siblingFiles.Add(name);
                    }
                });

                foreach (var childName in children)
                {
                    string childFull = prefix + childName;
                    string childRel = childFull.Length > normRoot.Length ? childFull.Substring(normRoot.Length) : ("\\" + childName);
                    if (IsExcludedArtifactPath(childRel)) continue;

                    string[] markers;
                    if (ArtifactMarkers.TryGetValue(childName, out markers))
                    {
                        bool validProject = false;
                        foreach (var m in markers)
                        {
                            if (siblingFiles.Contains(m)) { validProject = true; break; }
                        }
                        if (!validProject && (string.Equals(childName, ".venv", StringComparison.OrdinalIgnoreCase) || string.Equals(childName, "venv", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (File.Exists(Path.Combine(childFull, "pyvenv.cfg"))) validProject = true;
                        }
                        if (validProject)
                        {
                            matchedPaths.Add(Tuple.Create(childFull, childName));
                            continue; // 命中后剪枝，不再深入内部
                        }
                    }

                    if (depth + 1 <= maxDepth)
                        queue.Enqueue(Tuple.Create(childFull, depth + 1));
                }

                int nowTick = Environment.TickCount;
                if (onProgress != null && Math.Abs(nowTick - lastReportTick) > 120)
                {
                    lastReportTick = nowTick;
                    onProgress(new SpaceScanProgress
                    {
                        ScannedDirs = scannedDirs,
                        ScannedFiles = matchedPaths.Count,
                        CurrentHint = Path.GetFileName(dir)
                    });
                }
            }

            if (ct.IsCancellationRequested) { res.Cancelled = true; res.ElapsedMs = sw.ElapsedMilliseconds; return res; }

            // 并行计算每个构建产物的真实体积
            var list = new ConcurrentBag<SpaceEntry>();
            int measured = 0;
            var matches = matchedPaths.ToArray();
            int maxDop = Math.Min(Math.Max(2, Environment.ProcessorCount), 6);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct };

            try
            {
                Parallel.ForEach(matches, opts, m =>
                {
                    if (ct.IsCancellationRequested) return;
                    long sz = Fs.DirSizeSingleThread(m.Item1, long.MaxValue, ct);
                    if (sz < 1024 * 1024) return; // 小于 1 MB 忽略，保持列表精炼
                    DateTime wt = DateTime.Now;
                    try { wt = Directory.GetLastWriteTime(m.Item1); } catch { }
                    string parentDir = Path.GetDirectoryName(m.Item1) ?? "";
                    string projName = Path.GetFileName(parentDir);
                    list.Add(new SpaceEntry
                    {
                        FullPath = m.Item1,
                        DisplayName = string.IsNullOrEmpty(projName) ? m.Item2 : (projName + " / " + m.Item2),
                        ParentLabel = parentDir,
                        Tag = m.Item2,
                        Kind = SpaceEntryKind.BuildArtifact,
                        Size = sz,
                        LastWrite = wt,
                        IsProtected = false,
                        Checked = false
                    });
                    int done = Interlocked.Increment(ref measured);
                    if (onProgress != null && (done % 3 == 0 || done == matches.Length))
                    {
                        onProgress(new SpaceScanProgress
                        {
                            ScannedDirs = scannedDirs,
                            ScannedFiles = done,
                            CurrentHint = string.Format("正在计算体积 {0}/{1}", done, matches.Length)
                        });
                    }
                });
            }
            catch (OperationCanceledException) { res.Cancelled = true; }

            res.BuildArtifacts = list.OrderByDescending(x => x.Size).ToList();
            res.TotalScannedBytes = res.BuildArtifacts.Sum(x => x.Size);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        // 模式 C：空间骤降抓凶手 — 扫描指定盘符最近 N 小时内新建/修改的大文件（默认 >10MB）
        public static SpaceScanResult ScanRecentWriters(string[] drives, TimeSpan window, long minFileBytes, CancellationToken ct, Action<SpaceScanProgress> onProgress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new SpaceScanResult { RootPath = string.Join(", ", drives.Select(d => d + ":\\").ToArray()) };
            long cutoffFt = (DateTime.Now - window).ToFileTime();
            var hits = new ConcurrentBag<SpaceEntry>();
            long scannedFiles = 0, scannedDirs = 0;
            int lastTick = Environment.TickCount;

            var topDirs = new List<string>();
            foreach (var d in drives)
            {
                string root = d.TrimEnd(':', '\\') + ":\\";
                if (!Directory.Exists(root)) continue;
                EnumDirRaw(root, (name, isDir, isReparse, size, ftWrite) =>
                {
                    if (isReparse) return;
                    string full = root + name;
                    if (isDir)
                    {
                        if (!SkipSystemDirs.Contains(name)) topDirs.Add(full);
                    }
                    else
                    {
                        Interlocked.Increment(ref scannedFiles);
                        if (size >= minFileBytes && Win32Native.FileTimeToTicks(ftWrite) >= cutoffFt)
                            hits.Add(MakeFileEntry(full, name, root, size, ftWrite));
                    }
                });
            }

            int maxDop = Math.Min(Math.Max(2, Environment.ProcessorCount), 6);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct };
            try
            {
                Parallel.ForEach(topDirs, opts, dir =>
                {
                    if (ct.IsCancellationRequested) return;
                    string lowTop = dir.ToLowerInvariant();
                    if (lowTop.EndsWith(@"\windows\winsxs") || lowTop.EndsWith(@"\windows\installer")) return;
                    var stack = new Stack<string>(64);
                    stack.Push(dir);
                    while (stack.Count > 0)
                    {
                        if (ct.IsCancellationRequested) return;
                        string cur = stack.Pop();
                        Interlocked.Increment(ref scannedDirs);
                        string prefix = cur.EndsWith("\\") ? cur : cur + "\\";
                        EnumDirRaw(prefix, (name, isDir, isReparse, size, ftWrite) =>
                        {
                            if (isReparse) return;
                            string full = prefix + name;
                            if (isDir)
                            {
                                if (!SkipSystemDirs.Contains(name) && !string.Equals(name, "WinSxS", StringComparison.OrdinalIgnoreCase))
                                    stack.Push(full);
                            }
                            else
                            {
                                Interlocked.Increment(ref scannedFiles);
                                if (size >= minFileBytes && Win32Native.FileTimeToTicks(ftWrite) >= cutoffFt)
                                    hits.Add(MakeFileEntry(full, name, prefix, size, ftWrite));
                            }
                        });
                        int nowTick = Environment.TickCount;
                        if (onProgress != null && Math.Abs(nowTick - lastTick) > 140)
                        {
                            Interlocked.Exchange(ref lastTick, nowTick);
                            onProgress(new SpaceScanProgress
                            {
                                ScannedFiles = Interlocked.Read(ref scannedFiles),
                                ScannedDirs = Interlocked.Read(ref scannedDirs),
                                CurrentHint = Path.GetFileName(dir)
                            });
                        }
                    }
                });
            }
            catch (OperationCanceledException) { res.Cancelled = true; }

            res.LargeFiles = hits.OrderByDescending(x => x.Size).Take(60).ToList();
            res.TotalScannedBytes = res.LargeFiles.Sum(x => x.Size);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        // 全盘四级风险体检报告（L1 安全缓存 + L2 构建产物/大文件 + L3 Docker/回收站 + L4 系统红线）
        public class FullAuditResult
        {
            public long ElapsedMs;
            public long L1Bytes;
            public long L2ArtifactBytes;
            public long L2LargeFileBytes;
            public long L3DockerBytes;
            public long L3RecycleBytes;
            public int ArtifactCount;
            public int LargeFileCount;
            public List<SpaceEntry> TopArtifacts = new List<SpaceEntry>();
            public List<SpaceEntry> TopLargeFiles = new List<SpaceEntry>();
            public List<SpaceEntry> L4LockedItems = new List<SpaceEntry>();
        }

        public static FullAuditResult RunFullAudit(Config cfg, List<CleanItem> cleanItems, CancellationToken ct, Action<string, int> onStep)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new FullAuditResult();

            if (onStep != null) onStep("1/4 扫描 L1 安全缓存与系统临时垃圾…", 15);
            Parallel.ForEach(cleanItems, it =>
            {
                if (ct.IsCancellationRequested) return;
                try { it.LastSize = it.Scan(ct); } catch { it.LastSize = -1; }
            });
            res.L1Bytes = cleanItems.Where(i => i.DefaultOn && i.LastSize > 0).Sum(i => i.LastSize);
            var rb = cleanItems.FirstOrDefault(i => i.Id == "recycle-bin");
            res.L3RecycleBytes = (rb != null && rb.LastSize > 0) ? rb.LastSize : 0;

            if (ct.IsCancellationRequested) { res.ElapsedMs = sw.ElapsedMilliseconds; return res; }

            if (onStep != null) onStep("2/4 扫描 L2 全盘代码目录项目构建产物（node_modules / target / dist / .venv）…", 42);
            var codeRoots = FindCodeRoots(cfg.EffectiveDrives());
            var allArts = new List<SpaceEntry>();
            foreach (var cr in codeRoots)
            {
                if (ct.IsCancellationRequested) break;
                var artRes = ScanBuildArtifacts(cr, 6, ct, null);
                allArts.AddRange(artRes.BuildArtifacts);
            }
            allArts = allArts.OrderByDescending(x => x.Size).ToList();
            res.TopArtifacts = allArts.Take(25).ToList();
            res.ArtifactCount = allArts.Count;
            res.L2ArtifactBytes = allArts.Sum(x => x.Size);

            if (ct.IsCancellationRequested) { res.ElapsedMs = sw.ElapsedMilliseconds; return res; }

            if (onStep != null) onStep("3/4 扫描 L2 全盘超大文件（≥200 MB）与 L4 受保护大件…", 68);
            var allBig = new List<SpaceEntry>();
            foreach (var d in cfg.EffectiveDrives())
            {
                if (ct.IsCancellationRequested) break;
                string root = d + ":\\";
                if (!Directory.Exists(root)) continue;
                var scan = ScanLargeItems(root, 200L * 1024 * 1024, ct, null);
                allBig.AddRange(scan.LargeFiles);
            }
            var personalBig = allBig.Where(f => !f.IsProtected).OrderByDescending(f => f.Size).ToList();
            var lockedBig = allBig.Where(f => f.IsProtected).OrderByDescending(f => f.Size).Take(12).ToList();
            res.TopLargeFiles = personalBig.Take(25).ToList();
            res.LargeFileCount = personalBig.Count;
            res.L2LargeFileBytes = personalBig.Sum(f => f.Size);
            res.L4LockedItems = lockedBig;

            if (ct.IsCancellationRequested) { res.ElapsedMs = sw.ElapsedMilliseconds; return res; }

            if (onStep != null) onStep("4/4 汇总 L3 Docker 镜像、构建缓存与虚拟磁盘空间…", 88);
            var dok = DockerManager.Inspect(cfg, false);
            res.L3DockerBytes = Math.Max(0, dok.BuildCacheBytes) + Math.Max(0, dok.CompactReclaimableBytes) +
                                dok.UnusedImages.Sum(i => i.SizeBytes);

            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        public static bool IsExcludedArtifactPath(string path)
        {
            string low = (path ?? "").ToLowerInvariant() + "\\";
            foreach (var tok in ExcludedArtifactPathTokens)
            {
                if (low.Contains(tok)) return true;
            }
            return false;
        }

        static SpaceEntry MakeFileEntry(string full, string name, string parent, long size, System.Runtime.InteropServices.ComTypes.FILETIME ftWrite)
        {
            DateTime wt = DateTime.MinValue;
            try { wt = DateTime.FromFileTime(Win32Native.FileTimeToTicks(ftWrite)); } catch { }
            string ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrEmpty(ext)) ext = "文件";
            if (ext.Length > 6) ext = ext.Substring(0, 6);
            return new SpaceEntry
            {
                FullPath = full,
                DisplayName = name,
                ParentLabel = parent.TrimEnd('\\'),
                Tag = ext,
                Kind = SpaceEntryKind.LargeFile,
                Size = size,
                LastWrite = wt,
                IsProtected = Fs.IsProtected(full) || Fs.IsSystemCriticalPath(full),
                Advice = BuildAdvice(full, name)
            };
        }

        public static string BuildAdvice(string fullPath, string name)
        {
            string low = (fullPath ?? "").ToLowerInvariant();
            string fn = (name ?? "").ToLowerInvariant();
            if (fn == "docker_data.vhdx")
                return "Docker 虚拟磁盘（严禁直接删除）：清理镜像后需在 WSL 执行 fstrim 并压缩 vhdx 才会释放宿主机空间";
            if (fn == "pagefile.sys" || fn == "swapfile.sys")
                return "Windows 虚拟内存（禁止手删）：可在“系统设置 → 高级 → 性能 → 虚拟内存”中调小";
            if (fn == "hiberfil.sys")
                return "系统休眠文件（禁止手删）：若不用休眠功能，管理员运行 powercfg /h off 可安全关闭释放";
            if (low.Contains(@"\windows\winsxs"))
                return "Windows 组件存储（禁止手删）：管理员运行 DISM /Online /Cleanup-Image /StartComponentCleanup 安全瘦身";
            if (low.Contains(@"\windows\installer"))
                return "系统安装卸载缓存（禁止手删）：删除会导致软件无法正常卸载或更新";
            if (low.Contains(@"\tencent\") || low.Contains(@"\wechat files\") || low.Contains(@"\wxwork\"))
                return "聊天软件数据：建议在微信/QQ/企业微信“设置 → 存储空间”中清理或迁移位置";
            return "";
        }

        static void EnumDirRaw(string dirWithSlash, Action<string, bool, bool, long, System.Runtime.InteropServices.ComTypes.FILETIME> onItem)
        {
            string pattern = Fs.ToLongPath(dirWithSlash) + "*";
            Win32Native.WIN32_FIND_DATAW fd;
            IntPtr h = Win32Native.FindFirstFileExW(
                pattern,
                Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic,
                out fd,
                Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                Win32Native.FIND_FIRST_EX_LARGE_FETCH);
            if (h == Win32Native.INVALID_HANDLE_VALUE) return;
            try
            {
                do
                {
                    string name = fd.cFileName;
                    if (name == "." || name == "..") continue;
                    bool isReparse = (fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                    bool isDir = (fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    long sz = isDir ? 0 : ((((long)fd.nFileSizeHigh) << 32) | fd.nFileSizeLow);
                    onItem(name, isDir, isReparse, sz, fd.ftLastWriteTime);
                }
                while (Win32Native.FindNextFileW(h, out fd));
            }
            finally { Win32Native.FindClose(h); }
        }
    }
}
