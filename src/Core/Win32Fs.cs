// DiskGuard v3 - Win32 高速文件系统枚举（FindFirstFileExW + LARGE_FETCH）、安全删除、回收站与 DWM 窗口融合
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGuard
{
    public static class Win32Native
    {
        public const int INVALID_HANDLE_VALUE_INT = -1;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        public const int FIND_FIRST_EX_LARGE_FETCH = 0x00000002;

        public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0, FindExInfoBasic = 1 }
        public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATAW lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WaitNamedPipeW(string name, int timeout);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        public static void TrimMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        public static bool IsDockerPipeReady()
        {
            foreach (var pipe in new[] { @"\\.\pipe\docker_engine", @"\\.\pipe\dockerDesktopLinuxEngine" })
            {
                try
                {
                    if (WaitNamedPipeW(pipe, 10)) return true;
                    int err = Marshal.GetLastWin32Error();
                    if (err == 121) return true; // ERROR_SEM_TIMEOUT = pipe exists & busy
                }
                catch { }
            }
            return false;
        }

        // SHFileOperation: 安全移至回收站（可恢复）
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x0004;
        const ushort FOF_NOCONFIRMATION = 0x0010;
        const ushort FOF_ALLOWUNDO = 0x0040;
        const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        public static bool SendToRecycleBin(string path)
        {
            if (string.IsNullOrEmpty(path) || Fs.IsProtected(path) || Fs.IsSystemCriticalPath(path)) return false;
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
            };
            try { return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted; }
            catch { return false; }
        }

        // Windows 11 DWM 标题栏深浅模式、圆角与颜色同步
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;

        public static void ApplyDwmTheme(IntPtr hwnd, bool dark, Color captionColor, Color borderColor)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int useDark = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                int corner = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int cap = captionColor.R | (captionColor.G << 8) | (captionColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
                int brd = borderColor.R | (borderColor.G << 8) | (borderColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref brd, sizeof(int));
            }
            catch { }
        }

        public static long FileTimeToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return (((long)ft.dwHighDateTime) << 32) | (uint)ft.dwLowDateTime;
        }
    }

    public static class Fs
    {
        static readonly string[] DenyExact = new[]
        {
            Util.UserProfile,
            Util.LocalAppData,
            Util.RoamingAppData,
            Util.ProgramData,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        public static string ToLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\")) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        static bool IsReparse(FileSystemInfo i)
        {
            try { return (i.Attributes & FileAttributes.ReparsePoint) != 0; } catch { return true; }
        }

        public static bool IsProtected(string path)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return true; }
            string root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
            if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var d in DenyExact)
            {
                if (!string.IsNullOrEmpty(d) && string.Equals(full, d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsSystemCriticalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string low = path.ToLowerInvariant();
            if (low.Contains(@"\windows\winsxs") || low.Contains(@"\windows\installer") ||
                low.Contains(@"\windows\system32") || low.Contains(@"\windows\syswow64") ||
                low.Contains(@"\system volume information") || low.Contains(@"\$recycle.bin"))
                return true;
            string fn = Path.GetFileName(low);
            if (fn == "pagefile.sys" || fn == "hiberfil.sys" || fn == "swapfile.sys" || fn == "docker_data.vhdx")
                return true;
            return false;
        }

        // 高速 Win32 目录大小统计（无 FileInfo 堆分配，支持超长路径、跳过符号链接、支持并发与取消）
        public static long DirSize(string path, TimeSpan? olderThan)
        {
            return DirSize(path, olderThan, CancellationToken.None, true);
        }

        public static long DirSize(string path, TimeSpan? olderThan, CancellationToken ct, bool parallelTop)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            long cutoffFileTime = olderThan.HasValue
                ? (DateTime.Now - olderThan.Value).ToFileTime()
                : long.MaxValue;

            if (!parallelTop)
                return DirSizeSingleThread(path, cutoffFileTime, ct);

            // 先扫顶层：文件直接累加，子目录分发给有限并发线程
            long total = 0;
            var subDirs = new List<string>();
            EnumerateOneDir(path, cutoffFileTime, ref total, subDirs);
            if (subDirs.Count == 0 || ct.IsCancellationRequested) return total;

            if (subDirs.Count == 1)
            {
                total += DirSizeSingleThread(subDirs[0], cutoffFileTime, ct);
                return total;
            }

            int maxDop = Math.Min(Math.Max(2, Environment.ProcessorCount), 6);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct };
            try
            {
                Parallel.ForEach(subDirs, opts, () => 0L,
                    (sub, state, localSum) =>
                    {
                        if (ct.IsCancellationRequested) return localSum;
                        return localSum + DirSizeSingleThread(sub, cutoffFileTime, ct);
                    },
                    localSum => Interlocked.Add(ref total, localSum));
            }
            catch (OperationCanceledException) { }
            return total;
        }

        static void EnumerateOneDir(string dir, long cutoffFileTime, ref long bytes, List<string> subDirs)
        {
            string pattern = ToLongPath(dir.TrimEnd('\\')) + @"\*";
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
                    if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                    if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        subDirs.Add(dir.TrimEnd('\\') + "\\" + name);
                    }
                    else
                    {
                        if (cutoffFileTime == long.MaxValue || Win32Native.FileTimeToTicks(fd.ftLastWriteTime) < cutoffFileTime)
                        {
                            bytes += (((long)fd.nFileSizeHigh) << 32) | fd.nFileSizeLow;
                        }
                    }
                }
                while (Win32Native.FindNextFileW(h, out fd));
            }
            finally { Win32Native.FindClose(h); }
        }

        public static long DirSizeSingleThread(string rootPath, long cutoffFileTime, CancellationToken ct)
        {
            long total = 0;
            var stack = new Stack<string>(64);
            stack.Push(rootPath);
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) break;
                string cur = stack.Pop();
                string pattern = ToLongPath(cur.TrimEnd('\\')) + @"\*";
                Win32Native.WIN32_FIND_DATAW fd;
                IntPtr h = Win32Native.FindFirstFileExW(
                    pattern,
                    Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic,
                    out fd,
                    Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                    IntPtr.Zero,
                    Win32Native.FIND_FIRST_EX_LARGE_FETCH);
                if (h == Win32Native.INVALID_HANDLE_VALUE) continue;
                try
                {
                    do
                    {
                        string name = fd.cFileName;
                        if (name == "." || name == "..") continue;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                        {
                            stack.Push(cur.TrimEnd('\\') + "\\" + name);
                        }
                        else
                        {
                            if (cutoffFileTime == long.MaxValue || Win32Native.FileTimeToTicks(fd.ftLastWriteTime) < cutoffFileTime)
                            {
                                total += (((long)fd.nFileSizeHigh) << 32) | fd.nFileSizeLow;
                            }
                        }
                    }
                    while (Win32Native.FindNextFileW(h, out fd));
                }
                finally { Win32Native.FindClose(h); }
            }
            return total;
        }

        // 清空目录内容，保留目录本身；绝不跟随联接/符号链接；被占用跳过
        public static long EmptyDir(string path, TimeSpan? olderThan, StringBuilder log)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            if (IsProtected(path)) { log.AppendLine("  拒绝：受保护目录 " + path); return 0; }
            var root = new DirectoryInfo(path);
            if (IsReparse(root)) { log.AppendLine("  跳过：根目录是链接 " + path); return 0; }
            int failed = 0;
            DateTime? cutoff = olderThan.HasValue ? (DateTime?)(DateTime.Now - olderThan.Value) : null;
            long freed = DeleteChildren(root, cutoff, ref failed);
            if (failed > 0) log.AppendLine("  " + failed + " 项被占用或无权限，已跳过");
            return freed;
        }

        // 删除整个目标目录（用于项目构建产物如 node_modules / target / dist），同样不跟随符号链接
        public static long DeleteDirSafe(string path, StringBuilder log)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            if (IsProtected(path) || IsSystemCriticalPath(path))
            {
                log.AppendLine("  拒绝：受保护目录 " + path);
                return 0;
            }
            var root = new DirectoryInfo(path);
            if (IsReparse(root)) { log.AppendLine("  跳过：目标是符号链接 " + path); return 0; }
            long freed = EmptyDir(path, null, log);
            try
            {
                if (root.GetFileSystemInfos().Length == 0)
                {
                    root.Attributes = FileAttributes.Normal;
                    root.Delete();
                }
            }
            catch { }
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
}
