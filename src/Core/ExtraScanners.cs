// DiskGuard v7 - 全盘重复大文件三段式极速哈希排查 & 微信/企业微信/QQ 大附件专清（绝不碰聊天数据库）
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGuard
{
    public class DuplicateGroup
    {
        public string HashShort;
        public long FileSize;
        public long WastedBytes; // (count - 1) * FileSize
        public List<SpaceEntry> Files = new List<SpaceEntry>();
    }

    public class DuplicateScanResult
    {
        public long ElapsedMs;
        public bool Cancelled;
        public long ScannedFiles;
        public long TotalWastedBytes;
        public List<DuplicateGroup> Groups = new List<DuplicateGroup>();
    }

    public class ChatScanResult
    {
        public long ElapsedMs;
        public bool Cancelled;
        public long TotalBytes;
        public List<string> ScannedRoots = new List<string>();
        public List<SpaceEntry> Files = new List<SpaceEntry>();
    }

    public static class ExtraScanners
    {
        static readonly HashSet<string> SkipDupDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Windows", "Program Files", "Program Files (x86)",
            "ProgramData", "Recovery", "Config.Msi", "node_modules", ".git", ".venv", "venv", "target", "WinSxS"
        };

        // 三段式极速重复大文件排查：1.按精确体积分桶 → 2.首尾 8KB 快速哈希 → 3.全文件 SHA-256 精验
        public static DuplicateScanResult FindDuplicates(string[] roots, long minFileBytes, CancellationToken ct, Action<SpaceScanProgress> onProgress)
        {
            var sw = Stopwatch.StartNew();
            var res = new DuplicateScanResult();
            var bySize = new ConcurrentDictionary<long, ConcurrentBag<Tuple<string, DateTime>>>();
            long scanned = 0;
            int lastTick = Environment.TickCount;

            var topDirs = new List<string>();
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r) || !Directory.Exists(r)) continue;
                string norm = r.EndsWith("\\") ? r : r + "\\";
                EnumRaw(norm, (name, isDir, isReparse, size, ftWrite) =>
                {
                    if (isReparse) return;
                    string full = norm + name;
                    if (isDir)
                    {
                        if (!SkipDupDirs.Contains(name)) topDirs.Add(full);
                    }
                    else
                    {
                        Interlocked.Increment(ref scanned);
                        if (size >= minFileBytes && !Fs.IsSystemCriticalPath(full))
                            bySize.GetOrAdd(size, _ => new ConcurrentBag<Tuple<string, DateTime>>()).Add(Tuple.Create(full, FromFt(ftWrite)));
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
                    string low = dir.ToLowerInvariant();
                    if (low.Contains(@"\appdata\local\packages") || low.Contains(@"\appdata\local\microsoft")) return;

                    var stack = new Stack<string>(64);
                    stack.Push(dir);
                    while (stack.Count > 0)
                    {
                        if (ct.IsCancellationRequested) return;
                        string cur = stack.Pop();
                        string prefix = cur.EndsWith("\\") ? cur : cur + "\\";
                        EnumRaw(prefix, (name, isDir, isReparse, size, ftWrite) =>
                        {
                            if (isReparse) return;
                            string full = prefix + name;
                            if (isDir)
                            {
                                if (!SkipDupDirs.Contains(name)) stack.Push(full);
                            }
                            else
                            {
                                Interlocked.Increment(ref scanned);
                                if (size >= minFileBytes && !Fs.IsSystemCriticalPath(full))
                                    bySize.GetOrAdd(size, _ => new ConcurrentBag<Tuple<string, DateTime>>()).Add(Tuple.Create(full, FromFt(ftWrite)));
                            }
                        });

                        int nowTick = Environment.TickCount;
                        if (onProgress != null && Math.Abs(nowTick - lastTick) > 150)
                        {
                            Interlocked.Exchange(ref lastTick, nowTick);
                            onProgress(new SpaceScanProgress { ScannedFiles = Interlocked.Read(ref scanned), CurrentHint = "阶段 1/3 扫描大文件：" + Path.GetFileName(dir) });
                        }
                    }
                });
            }
            catch (OperationCanceledException) { res.Cancelled = true; }

            res.ScannedFiles = scanned;
            if (ct.IsCancellationRequested) { res.ElapsedMs = sw.ElapsedMilliseconds; return res; }

            // Stage 2: 首尾 8KB 快速采样哈希
            var sizeCandidates = bySize.Where(kv => kv.Value.Count >= 2).OrderByDescending(kv => kv.Key * (kv.Value.Count - 1)).ToList();
            if (onProgress != null) onProgress(new SpaceScanProgress { ScannedFiles = scanned, CurrentHint = "阶段 2/3 快速比对 " + sizeCandidates.Count + " 组同体积候选…" });

            var fullHashGroups = new ConcurrentBag<DuplicateGroup>();
            try
            {
                Parallel.ForEach(sizeCandidates, opts, kv =>
                {
                    if (ct.IsCancellationRequested) return;
                    long sz = kv.Key;
                    var partialBuckets = new Dictionary<string, List<Tuple<string, DateTime>>>();
                    foreach (var f in kv.Value)
                    {
                        if (ct.IsCancellationRequested) return;
                        string ph = ComputeHeadTailHash(f.Item1, sz);
                        if (ph == null) continue;
                        List<Tuple<string, DateTime>> b;
                        if (!partialBuckets.TryGetValue(ph, out b)) { b = new List<Tuple<string, DateTime>>(); partialBuckets[ph] = b; }
                        b.Add(f);
                    }

                    // Stage 3: 仅对首尾一致的文件做全文件 SHA-256 精验（零误判！）
                    foreach (var pb in partialBuckets.Values)
                    {
                        if (pb.Count < 2 || ct.IsCancellationRequested) continue;
                        var shaBuckets = new Dictionary<string, List<Tuple<string, DateTime>>>();
                        foreach (var f in pb)
                        {
                            if (ct.IsCancellationRequested) return;
                            string sh = ComputeFullSha256(f.Item1);
                            if (sh == null) continue;
                            List<Tuple<string, DateTime>> sb;
                            if (!shaBuckets.TryGetValue(sh, out sb)) { sb = new List<Tuple<string, DateTime>>(); shaBuckets[sh] = sb; }
                            sb.Add(f);
                        }
                        foreach (var skv in shaBuckets)
                        {
                            if (skv.Value.Count < 2) continue;
                            var ordered = skv.Value.OrderBy(x => x.Item2).ToList(); // 最早修改的作为“保留原件”
                            var grp = new DuplicateGroup
                            {
                                HashShort = skv.Key.Substring(0, 10),
                                FileSize = sz,
                                WastedBytes = sz * (ordered.Count - 1)
                            };
                            for (int i = 0; i < ordered.Count; i++)
                            {
                                var item = ordered[i];
                                string name = Path.GetFileName(item.Item1);
                                grp.Files.Add(new SpaceEntry
                                {
                                    FullPath = item.Item1,
                                    DisplayName = name,
                                    ParentLabel = Path.GetDirectoryName(item.Item1) ?? "",
                                    Tag = i == 0 ? "保留原件" : "重复副本",
                                    Kind = SpaceEntryKind.LargeFile,
                                    Size = sz,
                                    LastWrite = item.Item2,
                                    IsProtected = false
                                });
                            }
                            fullHashGroups.Add(grp);
                        }
                    }
                });
            }
            catch (OperationCanceledException) { res.Cancelled = true; }

            res.Groups = fullHashGroups.OrderByDescending(g => g.WastedBytes).Take(60).ToList();
            res.TotalWastedBytes = res.Groups.Sum(g => g.WastedBytes);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        static string ComputeHeadTailHash(string path, long size)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096))
                using (var sha = SHA256.Create())
                {
                    byte[] buf = new byte[Math.Min(8192, (int)Math.Min(size, 8192))];
                    int n1 = fs.Read(buf, 0, Math.Min(4096, buf.Length));
                    if (size > 8192)
                    {
                        fs.Seek(-4096, SeekOrigin.End);
                        int n2 = fs.Read(buf, n1, Math.Min(4096, buf.Length - n1));
                        n1 += n2;
                    }
                    var hash = sha.ComputeHash(buf, 0, n1);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
            catch { return null; }
        }

        static string ComputeFullSha256(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(fs);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
            catch { return null; }
        }

        // 微信 / 企业微信 / QQ 接收大文件与视频专清（严格限定附件目录，绝不碰 .db 聊天数据库！）
        static readonly HashSet<string> ForbiddenChatExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".db", ".db-wal", ".db-shm", ".sqlite", ".sqlite3", ".ini", ".cfg", ".dat", ".key"
        };

        public static ChatScanResult ScanChatAttachments(long minFileBytes, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var res = new ChatScanResult();
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string roaming = Util.RoamingAppData;

            var targetDirs = new List<Tuple<string, string>>(); // dir, appTag

            // 1. 微信 (WeChat Files / xwechat_files)
            foreach (var baseDir in new[] { Path.Combine(docs, "WeChat Files"), Path.Combine(docs, "xwechat_files"), Path.Combine(roaming, "xwechat") })
            {
                if (!Directory.Exists(baseDir)) continue;
                try
                {
                    foreach (var acct in Directory.GetDirectories(baseDir))
                    {
                        foreach (var sub in new[] { @"FileStorage\File", @"FileStorage\Video", @"FileStorage\Cache", @"msg\file", @"msg\video", @"cache" })
                        {
                            string p = Path.Combine(acct, sub);
                            if (Directory.Exists(p)) targetDirs.Add(Tuple.Create(p, "微信"));
                        }
                    }
                }
                catch { }
            }

            // 2. 企业微信 (Documents\WXWork)
            string wxw = Path.Combine(docs, "WXWork");
            if (Directory.Exists(wxw))
            {
                try
                {
                    foreach (var acct in Directory.GetDirectories(wxw))
                    {
                        foreach (var sub in new[] { @"Cache\File", @"Cache\Video", @"Cache\Image", @"Data\File" })
                        {
                            string p = Path.Combine(acct, sub);
                            if (Directory.Exists(p)) targetDirs.Add(Tuple.Create(p, "企业微信"));
                        }
                    }
                }
                catch { }
            }

            // 3. QQ (Documents\Tencent Files / AppData\Roaming\Tencent)
            string qqDocs = Path.Combine(docs, "Tencent Files");
            if (Directory.Exists(qqDocs))
            {
                try
                {
                    foreach (var acct in Directory.GetDirectories(qqDocs))
                    {
                        foreach (var sub in new[] { "FileRecv", @"nt_qq\nt_data\File", @"nt_qq\nt_data\Video" })
                        {
                            string p = Path.Combine(acct, sub);
                            if (Directory.Exists(p)) targetDirs.Add(Tuple.Create(p, "QQ"));
                        }
                    }
                }
                catch { }
            }
            string tencentR = Path.Combine(roaming, "Tencent");
            if (Directory.Exists(tencentR))
            {
                foreach (var sub in new[] { @"WeChat\XPlugin", @"QQ\Temp", @"WXWork\Temp" })
                {
                    string p = Path.Combine(tencentR, sub);
                    if (Directory.Exists(p)) targetDirs.Add(Tuple.Create(p, "腾讯缓存"));
                }
            }

            res.ScannedRoots = targetDirs.Select(t => t.Item1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var hits = new ConcurrentBag<SpaceEntry>();
            Parallel.ForEach(targetDirs, t =>
            {
                if (ct.IsCancellationRequested) return;
                var stack = new Stack<string>(32);
                stack.Push(t.Item1);
                while (stack.Count > 0 && !ct.IsCancellationRequested)
                {
                    string cur = stack.Pop();
                    string prefix = cur.EndsWith("\\") ? cur : cur + "\\";
                    EnumRaw(prefix, (name, isDir, isReparse, size, ftWrite) =>
                    {
                        if (isReparse) return;
                        string full = prefix + name;
                        if (isDir) stack.Push(full);
                        else
                        {
                            if (size < minFileBytes) return;
                            string ext = Path.GetExtension(name);
                            if (ForbiddenChatExts.Contains(ext)) return;
                            hits.Add(new SpaceEntry
                            {
                                FullPath = full,
                                DisplayName = name,
                                ParentLabel = prefix.TrimEnd('\\'),
                                Tag = t.Item2,
                                Kind = SpaceEntryKind.LargeFile,
                                Size = size,
                                LastWrite = FromFt(ftWrite),
                                IsProtected = false
                            });
                        }
                    });
                }
            });

            res.Files = hits.OrderByDescending(x => x.Size).Take(120).ToList();
            res.TotalBytes = res.Files.Sum(x => x.Size);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        static DateTime FromFt(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            try { return DateTime.FromFileTime(Win32Native.FileTimeToTicks(ft)); } catch { return DateTime.MinValue; }
        }

        static void EnumRaw(string dirWithSlash, Action<string, bool, bool, long, System.Runtime.InteropServices.ComTypes.FILETIME> onItem)
        {
            string pattern = Fs.ToLongPath(dirWithSlash) + "*";
            Win32Native.WIN32_FIND_DATAW fd;
            IntPtr h = Win32Native.FindFirstFileExW(pattern, Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic, out fd,
                Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Native.FIND_FIRST_EX_LARGE_FETCH);
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
