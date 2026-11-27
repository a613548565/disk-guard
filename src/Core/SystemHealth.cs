// DiskGuard v7 - 极速低耗系统监控（ntdll NtQuerySystemInformation 单次内核调用 + 独立平滑 CPU 时钟）、卡顿急救、系统修复、端口与启动项
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DiskGuard
{
    public class ProcMetric
    {
        public int Pid;
        public string Name;
        public long WorkingSetBytes;
        public double CpuPct;
        public string Priority;
        public bool IsProtected;
        public bool Responding;
        public string WindowTitle;
    }

    public class SystemSnapshot
    {
        public double CpuPct;
        public double MemPct;
        public long MemTotalBytes;
        public long MemAvailBytes;
        public long MemUsedBytes;
        public int ProcessCount;
        public int LogicalCores;
        public bool IsLagging;
        public string LagReason = "";
        public List<ProcMetric> TopMem = new List<ProcMetric>();
        public List<ProcMetric> TopCpu = new List<ProcMetric>();
        public List<ProcMetric> HungApps = new List<ProcMetric>();
    }

    public class PortEntry
    {
        public string Proto;
        public int Port;
        public string LocalAddr;
        public int Pid;
        public string ProcName;
        public bool IsProtected;
    }

    public class StartupEntry
    {
        public string Id;
        public string Scope;
        public string Name;
        public string Command;
        public bool Enabled;
    }

    public class FixResult
    {
        public bool Ok;
        public string Title;
        public string Summary;
        public string Detail;
    }

    public static class SystemHealth
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        // 单次内核调用获取全机所有进程 CPU/内存（替代 Process.GetProcesses 550 次句柄打开，开销降 95%）
        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        // 快速枚举可见顶层窗口检测未响应（避免对 550 个后台进程逐个查 MainWindowHandle）
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);

        const int SystemProcessInformation = 5;
        const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        const uint PROCESS_QUERY_INFORMATION = 0x0400;
        const uint PROCESS_SET_QUOTA = 0x0100;

        static readonly HashSet<string> ProtectedProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm",
            "Memory Compression", "sihost", "taskhostw", "explorer", "ctfmon",
            "SecurityHealthService", "MsMpEng", "NisSrv", "audiodg", "spoolsv",
            "WmiPrvSE", "conhost", "RuntimeBroker", "ShellExperienceHost",
            "StartMenuExperienceHost", "SearchHost", "DiskGuard"
        };

        // 独立 CPU 采样器（托盘哨兵与前端面板各自独立时钟 + 最小 800ms 防抖窗口，彻底消除假 98% 尖峰）
        class CpuClock
        {
            readonly object lk = new object();
            long prevIdle, prevKernel, prevUser;
            DateTime prevTime = DateTime.MinValue;
            double lastVal = 0;

            public double Sample()
            {
                long idle, kernel, user;
                if (!GetSystemTimes(out idle, out kernel, out user)) return lastVal;
                lock (lk)
                {
                    var now = DateTime.Now;
                    if (prevTime == DateTime.MinValue)
                    {
                        prevIdle = idle; prevKernel = kernel; prevUser = user; prevTime = now;
                        return 0;
                    }
                    if ((now - prevTime).TotalMilliseconds < 700) return lastVal;

                    long dIdle = idle - prevIdle;
                    long dKernel = kernel - prevKernel;
                    long dUser = user - prevUser;
                    prevIdle = idle; prevKernel = kernel; prevUser = user; prevTime = now;
                    long dTotal = dKernel + dUser;
                    if (dTotal <= 0) return lastVal;
                    double pct = (double)(dTotal - dIdle) * 100.0 / dTotal;
                    lastVal = Math.Round(Math.Max(0, Math.Min(100, pct)), 1);
                    return lastVal;
                }
            }
        }

        static readonly CpuClock uiCpuClock = new CpuClock();
        static readonly CpuClock watchdogCpuClock = new CpuClock();
        static readonly object procLock = new object();
        static DateTime prevProcSample = DateTime.MinValue;
        static Dictionary<int, long> prevProcTicks = new Dictionary<int, long>();
        static int ntBufSize = 512 * 1024;

        public static bool IsProtectedProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return ProtectedProcs.Contains(name.Trim());
        }

        public static void ReadMemory(out long total, out long avail, out double pct)
        {
            total = 0; avail = 0; pct = 0;
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref ms))
            {
                total = (long)ms.ullTotalPhys;
                avail = (long)ms.ullAvailPhys;
                pct = total > 0 ? Math.Round(100.0 * (total - avail) / total, 1) : ms.dwMemoryLoad;
            }
        }

        public static double ReadCpuPct() { return uiCpuClock.Sample(); }
        public static double ReadWatchdogCpuPct() { return watchdogCpuClock.Sample(); }

        struct RawProc
        {
            public int Pid;
            public string Name;
            public long WorkingSet;
            public long TotalTicks; // 100ns units (KernelTime + UserTime)
            public int BasePriority;
        }

        // x64 SYSTEM_PROCESS_INFORMATION 精确偏移（无需 OpenProcess，单次内核调用 < 1 ms）
        static List<RawProc> QueryAllProcessesFast()
        {
            var list = new List<RawProc>(256);
            int size = ntBufSize;
            IntPtr buf = IntPtr.Zero;
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                    buf = Marshal.AllocHGlobal(size);
                    int retLen;
                    int status = NtQuerySystemInformation(SystemProcessInformation, buf, size, out retLen);
                    if (status == STATUS_INFO_LENGTH_MISMATCH)
                    {
                        size = Math.Max(size * 2, retLen + 65536);
                        ntBufSize = size;
                        continue;
                    }
                    if (status != 0) return list;

                    long baseAddr = buf.ToInt64();
                    long offset = 0;
                    while (true)
                    {
                        IntPtr p = new IntPtr(baseAddr + offset);
                        int nextOffset = Marshal.ReadInt32(p, 0);
                        long userTime = Marshal.ReadInt64(p, 40);
                        long kernelTime = Marshal.ReadInt64(p, 48);
                        ushort nameLen = (ushort)Marshal.ReadInt16(p, 56);
                        IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                        int basePrio = Marshal.ReadInt32(p, 72);
                        int pid = (int)Marshal.ReadIntPtr(p, 80).ToInt64();
                        long workingSet = Marshal.ReadInt64(p, 152); // WorkingSetSize on x64

                        if (pid > 4 && nameLen > 0 && namePtr != IntPtr.Zero)
                        {
                            string nm = Marshal.PtrToStringUni(namePtr, nameLen / 2);
                            if (nm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                nm = nm.Substring(0, nm.Length - 4);
                            list.Add(new RawProc
                            {
                                Pid = pid,
                                Name = nm,
                                WorkingSet = workingSet,
                                TotalTicks = userTime + kernelTime,
                                BasePriority = basePrio
                            });
                        }
                        if (nextOffset == 0) break;
                        offset += nextOffset;
                    }
                    break;
                }
            }
            catch { }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
            return list;
        }

        static Dictionary<int, Tuple<bool, string>> QueryVisibleWindowsFast()
        {
            var map = new Dictionary<int, Tuple<bool, string>>();
            try
            {
                EnumWindows((hWnd, lp) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    int len = GetWindowTextLength(hWnd);
                    if (len <= 0) return true;
                    int pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid <= 4) return true;
                    bool hung = IsHungAppWindow(hWnd);
                    Tuple<bool, string> existing;
                    if (map.TryGetValue(pid, out existing) && !existing.Item1 && !hung) return true;
                    var sb = new StringBuilder(Math.Min(120, len + 1));
                    GetWindowText(hWnd, sb, sb.Capacity);
                    map[pid] = Tuple.Create(!hung, sb.ToString());
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return map;
        }

        public static SystemSnapshot SampleAll()
        {
            var snap = new SystemSnapshot { LogicalCores = Math.Max(1, Environment.ProcessorCount) };
            long total, avail; double memPct;
            ReadMemory(out total, out avail, out memPct);
            snap.MemTotalBytes = total;
            snap.MemAvailBytes = avail;
            snap.MemUsedBytes = Math.Max(0, total - avail);
            snap.MemPct = memPct;
            snap.CpuPct = ReadCpuPct();

            var raw = QueryAllProcessesFast();
            var winMap = QueryVisibleWindowsFast();
            var now = DateTime.Now;
            double wallTicks100ns;
            Dictionary<int, long> oldMap;
            lock (procLock)
            {
                wallTicks100ns = prevProcSample == DateTime.MinValue ? 0 : (now - prevProcSample).TotalMilliseconds * 10000.0;
                oldMap = prevProcTicks;
            }

            var newMap = new Dictionary<int, long>(raw.Count);
            var metrics = new List<ProcMetric>(raw.Count);
            snap.ProcessCount = raw.Count;

            foreach (var r in raw)
            {
                newMap[r.Pid] = r.TotalTicks;
                double cpu = 0;
                long prevT;
                if (wallTicks100ns >= 3000000 && oldMap.TryGetValue(r.Pid, out prevT))
                {
                    long dt = r.TotalTicks - prevT;
                    if (dt > 0)
                        cpu = Math.Round(Math.Min(100.0, dt / (wallTicks100ns * snap.LogicalCores) * 100.0), 1);
                }

                bool responding = true;
                string title = "";
                Tuple<bool, string> winInfo;
                if (winMap.TryGetValue(r.Pid, out winInfo))
                {
                    responding = winInfo.Item1;
                    title = winInfo.Item2;
                }

                string prio = r.BasePriority <= 6 ? "低" : (r.BasePriority >= 10 ? "高" : "正常");
                metrics.Add(new ProcMetric
                {
                    Pid = r.Pid,
                    Name = r.Name,
                    WorkingSetBytes = r.WorkingSet,
                    CpuPct = cpu,
                    Priority = prio,
                    IsProtected = IsProtectedProcess(r.Name),
                    Responding = responding,
                    WindowTitle = title
                });
            }

            lock (procLock)
            {
                prevProcTicks = newMap;
                prevProcSample = now;
            }

            snap.TopMem = metrics.OrderByDescending(x => x.WorkingSetBytes).Take(12).ToList();
            snap.TopCpu = metrics.OrderByDescending(x => x.CpuPct).ThenByDescending(x => x.WorkingSetBytes).Take(10).ToList();
            snap.HungApps = metrics.Where(x => !x.Responding).ToList();

            var reasons = new List<string>();
            if (snap.MemPct >= 88) reasons.Add("内存占用过高 (" + snap.MemPct.ToString("0.0") + "%)");
            if (snap.CpuPct >= 88) reasons.Add("CPU 持续满载 (" + snap.CpuPct.ToString("0.0") + "%)");
            if (snap.HungApps.Count > 0) reasons.Add("发现 " + snap.HungApps.Count + " 个无响应窗口");
            snap.IsLagging = reasons.Count > 0;
            snap.LagReason = string.Join(" · ", reasons.ToArray());
            return snap;
        }

        public static FixResult CleanMemory(int? singlePid = null)
        {
            long total, availBefore, availAfter;
            double pctBefore, pctAfter;
            ReadMemory(out total, out availBefore, out pctBefore);

            int trimmed = 0;
            if (singlePid.HasValue)
            {
                IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, singlePid.Value);
                if (h != IntPtr.Zero)
                {
                    try { if (EmptyWorkingSet(h)) trimmed = 1; }
                    finally { CloseHandle(h); }
                }
            }
            else
            {
                int myPid = Process.GetCurrentProcess().Id;
                foreach (var r in QueryAllProcessesFast())
                {
                    if (r.Pid == myPid || IsProtectedProcess(r.Name) || r.WorkingSet < 10 * 1024 * 1024) continue;
                    IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, r.Pid);
                    if (h != IntPtr.Zero)
                    {
                        try { if (EmptyWorkingSet(h)) trimmed++; }
                        finally { CloseHandle(h); }
                    }
                }
            }

            Thread.Sleep(250);
            ReadMemory(out total, out availAfter, out pctAfter);
            long gained = Math.Max(0, availAfter - availBefore);
            return new FixResult
            {
                Ok = true,
                Title = singlePid.HasValue ? "进程工作集已修剪" : "安全内存释放完成",
                Summary = string.Format("修剪了 {0} 个进程空闲页，可用内存 {1} → {2}（净释放 {3}，占用率 {4:0.0}% → {5:0.0}%）",
                    trimmed, Util.Fmt(availBefore), Util.Fmt(availAfter), Util.Fmt(gained), pctBefore, pctAfter),
                Detail = "未关闭任何软件或浏览器标签页，工作数据完全安全"
            };
        }

        public static FixResult ThrottleProcess(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (IsProtectedProcess(p.ProcessName))
                        return new FixResult { Ok = false, Title = "安全拦截", Summary = "系统关键进程禁止调整优先级：" + p.ProcessName };
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    return new FixResult { Ok = true, Title = "已限制 CPU 抢占", Summary = "已将 " + p.ProcessName + " (PID " + pid + ") 降为「低于正常」优先级，前台操作立刻变流畅" };
                }
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "操作失败", Summary = ex.Message }; }
        }

        public static FixResult KillUserProcess(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (IsProtectedProcess(p.ProcessName))
                        return new FixResult { Ok = false, Title = "安全拦截", Summary = "禁止结束系统核心进程：" + p.ProcessName };
                    string name = p.ProcessName;
                    p.Kill();
                    return new FixResult { Ok = true, Title = "已结束进程", Summary = "已结束 " + name + " (PID " + pid + ")" };
                }
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "结束失败", Summary = ex.Message }; }
        }

        public static FixResult RunBugFix(string fixId)
        {
            int ex;
            switch (fixId)
            {
                case "restart-explorer":
                    try
                    {
                        foreach (var p in Process.GetProcessesByName("explorer"))
                        {
                            try { p.Kill(); p.WaitForExit(3000); } catch { }
                        }
                        Thread.Sleep(400);
                        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
                        return new FixResult { Ok = true, Title = "桌面与任务栏已重启", Summary = "已安全重启 Windows 资源管理器，任务栏与桌面已恢复响应" };
                    }
                    catch (Exception e) { return new FixResult { Ok = false, Title = "重启失败", Summary = e.Message }; }

                case "flush-dns":
                    string dnsOut = Util.Run("ipconfig.exe", "/flushdns", 15000, out ex);
                    return new FixResult { Ok = ex == 0, Title = "DNS 网络缓存已刷新", Summary = ex == 0 ? "已清空系统 DNS 解析缓存" : "执行返回码 " + ex, Detail = dnsOut.Trim() };

                case "reset-winsock":
                    string wsOut = RunMaybeElevated("netsh.exe", "winsock reset", out ex);
                    return new FixResult { Ok = ex == 0, Title = "网络协议栈 (Winsock) 已重置", Summary = ex == 0 ? "重置成功，建议稍后重启电脑完全生效" : "未完成（可能取消了管理员授权）", Detail = wsOut };

                case "fix-icon-cache":
                    return FixDesktopIconsDeep();

                case "dism-check":
                    string ckOut = RunMaybeElevated("dism.exe", "/Online /Cleanup-Image /CheckHealth", out ex);
                    return new FixResult
                    {
                        Ok = ex == 0, Title = "Windows 系统镜像健康检测完成",
                        Summary = ex == 0 ? (ckOut.Contains("未检测到组件存储损坏") || ckOut.IndexOf("No component store corruption", StringComparison.OrdinalIgnoreCase) >= 0 ? "系统组件存储健康，未发现损坏" : "检测已完成，详见输出") : "检测未完成（需管理员权限）",
                        Detail = ckOut
                    };

                case "sfc-scan":
                    string sfcOut = RunMaybeElevated("sfc.exe", "/scannow", out ex);
                    return new FixResult { Ok = ex == 0, Title = "系统文件完整性修复 (SFC) 已完成", Summary = ex == 0 ? "SFC 扫描修复已执行完毕" : "未完成（需管理员权限）", Detail = sfcOut };

                case "fix-wu":
                    string wuCmd = "/c net stop wuauserv & net stop bits & rd /s /q \"%WINDIR%\\SoftwareDistribution\\Download\" & mkdir \"%WINDIR%\\SoftwareDistribution\\Download\" & net start bits & net start wuauserv";
                    string wuOut = RunMaybeElevated("cmd.exe", wuCmd, out ex);
                    return new FixResult { Ok = ex == 0, Title = "Windows 更新服务已重置", Summary = ex == 0 ? "已重启更新服务并清空损坏的更新下载队列" : "执行失败（需管理员权限）", Detail = wuOut };

                case "lag-rescue":
                    var memRes = CleanMemory(null);
                    Util.Run("ipconfig.exe", "/flushdns", 8000, out ex);
                    return new FixResult { Ok = true, Title = "一键卡顿急救已完成", Summary = memRes.Summary + "；同时已刷新 DNS 缓存" };

                default:
                    return new FixResult { Ok = false, Title = "未知修复项", Summary = fixId };
            }
        }

        public static string RunMaybeElevated(string exe, string args, out int exit)
        {
            if (Util.IsAdmin()) return Util.Run(exe, args, 300000, out exit);
            exit = -1;
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Normal };
                using (var p = Process.Start(psi))
                {
                    if (p != null) { p.WaitForExit(300000); exit = p.ExitCode; }
                }
                return "已通过管理员 UAC 窗口执行完毕（退出码 " + exit + "）";
            }
            catch (Exception e) { return "管理员授权取消或失败：" + e.Message; }
        }

        public static List<PortEntry> ListListeningPorts(string filter)
        {
            var res = new List<PortEntry>();
            int ex;
            string outStr = Util.Run("netstat.exe", "-ano -p tcp", 10000, out ex);
            if (ex != 0) return res;

            var pidNames = new Dictionary<int, string>();
            foreach (var r in QueryAllProcessesFast()) pidNames[r.Pid] = r.Name;

            var seen = new HashSet<string>();
            foreach (var raw in outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) || line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                string local = parts[1];
                int colon = local.LastIndexOf(':');
                if (colon < 0) continue;
                int port, pid;
                if (!int.TryParse(local.Substring(colon + 1), out port)) continue;
                if (!int.TryParse(parts[parts.Length - 1], out pid)) continue;
                if (!seen.Add(port + ":" + pid)) continue;
                string pname;
                if (!pidNames.TryGetValue(pid, out pname)) pname = "PID " + pid;
                if (!string.IsNullOrEmpty(filter))
                {
                    string f = filter.Trim();
                    if (!port.ToString().Contains(f) && pname.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                res.Add(new PortEntry { Proto = "TCP", Port = port, LocalAddr = local, Pid = pid, ProcName = pname, IsProtected = IsProtectedProcess(pname) || pid <= 4 });
            }
            return res.OrderBy(x => x.Port).Take(60).ToList();
        }

        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string DisabledRunPath = @"Software\DiskGuard\DisabledStartup";

        public static List<StartupEntry> ListStartups()
        {
            var list = new List<StartupEntry>();
            ReadStartupKey(Registry.CurrentUser, RunPath, "HKCU", true, list);
            ReadStartupKey(Registry.CurrentUser, DisabledRunPath, "HKCU", false, list);
            ReadStartupKey(Registry.LocalMachine, RunPath, "HKLM", true, list);
            return list.OrderByDescending(x => x.Enabled).ThenBy(x => x.Name).ToList();
        }

        static void ReadStartupKey(RegistryKey root, string sub, string scope, bool enabled, List<StartupEntry> list)
        {
            try
            {
                using (var k = root.OpenSubKey(sub))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        var val = k.GetValue(name) as string;
                        if (string.IsNullOrEmpty(val)) continue;
                        list.Add(new StartupEntry { Id = scope + "|" + name, Scope = scope, Name = name, Command = val, Enabled = enabled });
                    }
                }
            }
            catch { }
        }

        public class ShortcutRepairReport
        {
            public int ScannedCount;
            public int RepairedCount;
            public List<string> RepairedNames = new List<string>();
            public List<string> BrokenTargets = new List<string>();
            public List<string> NoIconExeNames = new List<string>();
        }

        static bool HasIconAt(string filePath, int iconIndex)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            try
            {
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                uint n = ExtractIconEx(filePath, iconIndex, large, small, 1);
                if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
                return n > 0;
            }
            catch { return false; }
        }

        static void ParseIconLocation(string raw, out string path, out int index)
        {
            path = "";
            index = 0;
            if (string.IsNullOrWhiteSpace(raw)) return;
            string s = raw.Trim();
            int comma = s.LastIndexOf(',');
            if (comma >= 0)
            {
                string idxStr = s.Substring(comma + 1).Trim();
                int parsed;
                if (int.TryParse(idxStr, out parsed))
                {
                    index = parsed;
                    s = s.Substring(0, comma).Trim();
                }
            }
            s = s.Trim('"', ' ');
            if (s.Length > 0)
            {
                try { s = Environment.ExpandEnvironmentVariables(s); } catch { }
            }
            path = s;
        }

        static string FindNearbyIco(string targetExe)
        {
            try
            {
                string dir = Path.GetDirectoryName(targetExe);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                foreach (var ico in Directory.GetFiles(dir, "*.ico"))
                    if (HasIconAt(ico, 0)) return ico;
                foreach (var sub in new[] { "res", "resources", "icons", "icon", "assets" })
                {
                    string sd = Path.Combine(dir, sub);
                    if (!Directory.Exists(sd)) continue;
                    foreach (var ico in Directory.GetFiles(sd, "*.ico"))
                        if (HasIconAt(ico, 0)) return ico;
                }
            }
            catch { }
            return null;
        }

        public static ShortcutRepairReport InspectAndRepairDesktopShortcuts(string[] dirs)
        {
            var rep = new ShortcutRepairReport();
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return rep;
            object shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in dirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || !seen.Add(dir)) continue;
                    string[] lnks;
                    try { lnks = Directory.GetFiles(dir, "*.lnk"); } catch { continue; }
                    foreach (var lnk in lnks)
                    {
                        rep.ScannedCount++;
                        object sc = null;
                        try
                        {
                            sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                            if (sc == null) continue;
                            Type scType = sc.GetType();
                            string target = Convert.ToString(scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null)) ?? "";
                            string iconLoc = Convert.ToString(scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.GetProperty, null, sc, null)) ?? "";
                            string name = Path.GetFileNameWithoutExtension(lnk);

                            string expTarget = target.Trim('"', ' ');
                            if (expTarget.Length > 0) { try { expTarget = Environment.ExpandEnvironmentVariables(expTarget); } catch { } }

                            // 仅校验本地盘符目标路径（避免误判 UWP/Shell 虚拟快捷方式）
                            bool isLocalTarget = expTarget.Length >= 3 && char.IsLetter(expTarget[0]) && expTarget[1] == ':' && expTarget[2] == '\\';
                            bool targetExists = !isLocalTarget || File.Exists(expTarget) || Directory.Exists(expTarget);

                            if (isLocalTarget && !targetExists)
                            {
                                rep.BrokenTargets.Add(name + " → " + expTarget);
                                continue;
                            }

                            string iconFile; int iconIdx;
                            ParseIconLocation(iconLoc, out iconFile, out iconIdx);
                            bool hasExplicitIconFile = !string.IsNullOrEmpty(iconFile);
                            bool iconValid = true;
                            if (hasExplicitIconFile)
                            {
                                bool isLocalIcon = iconFile.Length >= 3 && char.IsLetter(iconFile[0]) && iconFile[1] == ':';
                                if (isLocalIcon)
                                {
                                    string ext = Path.GetExtension(iconFile).ToLowerInvariant();
                                    bool checkExtract = ext == ".exe" || ext == ".dll" || ext == ".ico" || ext == ".icl";
                                    iconValid = File.Exists(iconFile) && (!checkExtract || HasIconAt(iconFile, iconIdx));
                                }
                            }

                            // 情况 1：IconLocation 指向丢失/无效图标，而目标程序存在 -> 自动修回目标程序或同目录 .ico
                            if (!iconValid && isLocalTarget && File.Exists(expTarget))
                            {
                                string fixLoc = null;
                                if (HasIconAt(expTarget, 0)) fixLoc = target + ",0";
                                else
                                {
                                    string nearIco = FindNearbyIco(expTarget);
                                    if (!string.IsNullOrEmpty(nearIco)) fixLoc = nearIco + ",0";
                                }
                                if (!string.IsNullOrEmpty(fixLoc))
                                {
                                    scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { fixLoc });
                                    scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                                    try { File.SetLastWriteTime(lnk, DateTime.Now); } catch { }
                                    rep.RepairedCount++;
                                    rep.RepairedNames.Add(name);
                                }
                                else
                                {
                                    rep.NoIconExeNames.Add(name);
                                }
                            }
                            // 情况 2：IconLocation 未设且目标为 .exe，但该 .exe 本身不含任何嵌入图标资源 -> 尝试同目录 .ico
                            else if (!hasExplicitIconFile && isLocalTarget && File.Exists(expTarget) && expTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !HasIconAt(expTarget, 0))
                            {
                                string nearIco = FindNearbyIco(expTarget);
                                if (!string.IsNullOrEmpty(nearIco))
                                {
                                    scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { nearIco + ",0" });
                                    scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                                    try { File.SetLastWriteTime(lnk, DateTime.Now); } catch { }
                                    rep.RepairedCount++;
                                    rep.RepairedNames.Add(name);
                                }
                                else
                                {
                                    rep.NoIconExeNames.Add(name);
                                }
                            }
                        }
                        catch { }
                        finally { if (sc != null) { try { Marshal.FinalReleaseComObject(sc); } catch { } } }
                    }

                    // 同时排查桌面 .url 快捷方式（IconFile 丢失）
                    string[] urls;
                    try { urls = Directory.GetFiles(dir, "*.url"); } catch { continue; }
                    foreach (var uf in urls)
                    {
                        try
                        {
                            foreach (var line in File.ReadAllLines(uf))
                            {
                                if (!line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) continue;
                                string icf = line.Substring(9).Trim('"', ' ');
                                if (icf.Length >= 3 && char.IsLetter(icf[0]) && icf[1] == ':' && !File.Exists(icf))
                                    rep.BrokenTargets.Add(Path.GetFileNameWithoutExtension(uf) + "(URL图标丢失) → " + icf);
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            finally { if (shell != null) { try { Marshal.FinalReleaseComObject(shell); } catch { } } }
            return rep;
        }

        static FixResult FixDesktopIconsDeep()
        {
            try
            {
                // 1. 扫描并自动修复桌面（当前用户 + 公共桌面）异常快捷方式
                var desktopDirs = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                };
                var lnkRep = InspectAndRepairDesktopShortcuts(desktopDirs);

                // 2. 抗 Win11 explorer 秒级自动重启锁定：最多 3 轮快速结束宿主并强删全部图标/缩略图缓存库
                string expDir = Path.Combine(Util.LocalAppData, "Microsoft", "Windows", "Explorer");
                string oldIcon = Path.Combine(Util.LocalAppData, "IconCache.db");
                int delCnt = 0;
                int lockedCnt = 0;
                string[] lockProcs = new[] { "explorer", "StartMenuExperienceHost", "ShellExperienceHost" };

                for (int round = 0; round < 3; round++)
                {
                    foreach (var pn in lockProcs)
                    {
                        foreach (var p in Process.GetProcessesByName(pn))
                        {
                            try { p.Kill(); p.WaitForExit(1200); } catch { }
                        }
                    }
                    Thread.Sleep(120);

                    var targets = new List<string>();
                    if (File.Exists(oldIcon)) targets.Add(oldIcon);
                    if (Directory.Exists(expDir))
                    {
                        try { targets.AddRange(Directory.GetFiles(expDir, "iconcache*.db")); } catch { }
                        try { targets.AddRange(Directory.GetFiles(expDir, "thumbcache*.db")); } catch { }
                        try { targets.AddRange(Directory.GetFiles(expDir, "*.dg_old")); } catch { }
                    }

                    lockedCnt = 0;
                    foreach (var f in targets)
                    {
                        try
                        {
                            File.SetAttributes(f, FileAttributes.Normal);
                            File.Delete(f);
                            delCnt++;
                        }
                        catch
                        {
                            // 若文件允许 FILE_SHARE_DELETE 重命名，移走后可让新 explorer 立即重建干净缓存库
                            try
                            {
                                string moved = f + ".dg_old";
                                if (File.Exists(moved)) { try { File.Delete(moved); } catch { } }
                                File.Move(f, moved);
                                delCnt++;
                                try { File.Delete(moved); } catch { }
                            }
                            catch { lockedCnt++; }
                        }
                    }
                    if (lockedCnt == 0) break;
                }

                // 3. 确保 explorer.exe 正常拉起
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    try { Process.Start("explorer.exe"); } catch { }
                }
                Thread.Sleep(900);

                // 4. 调用 Win10/11 官方 ie4uinit 刷新命令 + Win32 SHChangeNotify 强制重绘桌面全部图标
                int ex;
                string ie4 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
                if (File.Exists(ie4))
                {
                    Util.Run(ie4, "-show", 8000, out ex);
                    Util.Run(ie4, "-ClearIconCache", 8000, out ex);
                }
                try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); } catch { }

                // 5. 检查注册表 Shell Icons\29 快捷方式覆盖是否指向了不存在的 .ico
                string badShellIconNote = "";
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons"))
                    {
                        string v29 = k != null ? (k.GetValue("29") as string) : null;
                        if (!string.IsNullOrWhiteSpace(v29))
                        {
                            string p29; int idx29;
                            ParseIconLocation(v29, out p29, out idx29);
                            if (!string.IsNullOrEmpty(p29) && p29.Length >= 3 && p29[1] == ':' && !File.Exists(p29))
                                badShellIconNote = "检测到注册表 Shell Icons\\29 指向缺失文件(" + p29 + ")";
                        }
                    }
                }
                catch { }

                var sumParts = new List<string>();
                sumParts.Add("已清理/重建 " + delCnt + " 个图标缓存库并触发系统强制刷新");
                if (lnkRep.RepairedCount > 0)
                    sumParts.Add("自动修复 " + lnkRep.RepairedCount + " 个异常快捷方式(" + string.Join("、", lnkRep.RepairedNames.Take(3).ToArray()) + ")");
                if (lockedCnt > 0)
                    sumParts.Add("另有 " + lockedCnt + " 个缓存库被系统占用（注销或重启电脑后将彻底生效）");

                var detailParts = new List<string>();
                if (lnkRep.BrokenTargets.Count > 0)
                    detailParts.Add("发现 " + lnkRep.BrokenTargets.Count + " 个快捷方式目标/图标文件已丢失（清缓存无法恢复，请重装或重建快捷方式）：" + string.Join("；", lnkRep.BrokenTargets.Take(3).ToArray()));
                if (lnkRep.NoIconExeNames.Count > 0)
                    detailParts.Add("以下程序本身未内嵌图标：" + string.Join("、", lnkRep.NoIconExeNames.Take(3).ToArray()));
                if (!string.IsNullOrEmpty(badShellIconNote))
                    detailParts.Add(badShellIconNote);

                return new FixResult
                {
                    Ok = true,
                    Title = "桌面白纸图标深层修复完成",
                    Summary = string.Join("；", sumParts.ToArray()),
                    Detail = string.Join(" | ", detailParts.ToArray())
                };
            }
            catch (Exception e) { return new FixResult { Ok = false, Title = "修复失败", Summary = e.Message }; }
        }

        public static FixResult ToggleStartup(string id, bool enable)
        {
            var parts = (id ?? "").Split(new[] { '|' }, 2);
            if (parts.Length != 2) return new FixResult { Ok = false, Title = "参数无效", Summary = id };
            string scope = parts[0], name = parts[1];
            if (string.Equals(name, "DiskGuard", StringComparison.OrdinalIgnoreCase))
                return new FixResult { Ok = false, Title = "提示", Summary = "请在下方「开机自启」开关中切换 DiskGuard" };
            if (scope != "HKCU")
                return new FixResult { Ok = false, Title = "系统级启动项", Summary = "HKLM 系统级启动项为安全起见仅做展示" };
            try
            {
                using (var runK = Registry.CurrentUser.CreateSubKey(RunPath))
                using (var disK = Registry.CurrentUser.CreateSubKey(DisabledRunPath))
                {
                    if (enable)
                    {
                        var val = disK.GetValue(name) as string;
                        if (val != null) { runK.SetValue(name, val); disK.DeleteValue(name, false); }
                        return new FixResult { Ok = true, Title = "已恢复启动项", Summary = "已恢复 " + name + " 开机自启" };
                    }
                    else
                    {
                        var val = runK.GetValue(name) as string;
                        if (val != null) { disK.SetValue(name, val); runK.DeleteValue(name, false); }
                        return new FixResult { Ok = true, Title = "已禁用启动项", Summary = "已安全禁用 " + name + "（已备份，随时可一键恢复）" };
                    }
                }
            }
            catch (Exception e) { return new FixResult { Ok = false, Title = "修改失败", Summary = e.Message }; }
        }
    }
}
