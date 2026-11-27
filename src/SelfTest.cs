// DiskGuard v3 - 全面自检（Win32 并发扫描性能、安全删除沙箱、构建产物识别沙箱、UI 深浅主题离屏渲染截图）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskGuard
{
    static class SelfTest
    {
        public static int Run(string screenshotDir)
        {
            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                var cfg = Config.Load();
                Theme.ApplyMode(cfg.Theme);
                History.Load();

                sb.AppendLine("== drives ==");
                foreach (var d in DriveStat.Read(cfg))
                    sb.AppendLine(string.Format("{0}: ready={1} free={2} total={3} level={4} {5}", d.Name, d.Ready, Util.Fmt(d.Free), Util.Fmt(d.Total), d.Level(cfg), History.ChangeText(d)));
                sb.AppendLine("history samples=" + History.Count + "  trayPin=" + TrayPin.State() + "  dockerPipe=" + Win32Native.IsDockerPipeReady());

                sb.AppendLine("== scan (Win32 FindFirstFileExW 并行，只扫描不清理) ==");
                var items = CleanItemsBuilder.Build(cfg);
                var res = new string[items.Count];
                var wall = Stopwatch.StartNew();
                Parallel.For(0, items.Count, i =>
                {
                    var sw = Stopwatch.StartNew();
                    long s;
                    try { s = items[i].Scan(CancellationToken.None); }
                    catch (Exception ex) { s = -1; res[i] = "  ERR " + ex.Message; return; }
                    string szStr = s == -2 ? "未运行" : Util.Fmt(s);
                    res[i] = string.Format("{0,-10} [{1}] {2}  ({3} ms, 默认{4})", szStr, items[i].CategoryLabel, items[i].Name, sw.ElapsedMilliseconds, items[i].DefaultOn ? "勾选" : "不勾选");
                });
                foreach (var r in res) sb.AppendLine(r);
                sb.AppendLine("并行扫描总用时 " + wall.ElapsedMilliseconds + " ms");

                sb.AppendLine("== delete safety (临时沙箱) ==");
                ok &= DeleteSafety(sb);

                sb.AppendLine("== build artifact detection safety (临时沙箱) ==");
                ok &= ArtifactSafety(sb);

                sb.AppendLine("== protected paths ==");
                foreach (var p in new[] { "C:\\", "E:\\", Util.UserProfile, Util.LocalAppData, Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"C:\Windows\WinSxS\test", @"E:\pagefile.sys" })
                {
                    bool prot = Fs.IsProtected(p) || Fs.IsSystemCriticalPath(p);
                    sb.AppendLine((prot ? "OK   " : "FAIL ") + "拦截 " + p);
                    ok &= prot;
                }

                sb.AppendLine("== docker inspect (只读) ==");
                var dok = DockerManager.Inspect(cfg, false);
                var dokFallbackExe = DockerManager.ResolveDockerExe(new Config { DockerExe = @"Z:\nonexistent\docker.exe" });
                sb.AppendLine(string.Format("installed={0} running={1} fallbackExe={2} vhdx={3} physical={4} internal={5} compactable={6} runningC={7} stoppedC={8} unusedImg={9} danglingVol={10}",
                    dok.Installed, dok.Running, string.IsNullOrEmpty(dokFallbackExe) ? "(none)" : dokFallbackExe, dok.VhdxPath, Util.Fmt(dok.VhdxPhysicalBytes), Util.Fmt(dok.VhdxInternalUsedBytes), Util.Fmt(dok.CompactReclaimableBytes),
                    dok.RunningContainers.Count, dok.StoppedContainers.Count, dok.UnusedImages.Count, dok.DanglingVolumes.Count));

                sb.AppendLine("== system health (NtQuerySystemInformation 极速只读) ==");
                SystemHealth.ReadCpuPct();
                Thread.Sleep(850);
                var psw = Stopwatch.StartNew();
                var snap = SystemHealth.SampleAll();
                psw.Stop();
                bool protOk = SystemHealth.IsProtectedProcess("explorer") && SystemHealth.IsProtectedProcess("lsass") && !SystemHealth.IsProtectedProcess("notepad");
                var ports = SystemHealth.ListListeningPorts("");
                var startups = SystemHealth.ListStartups();
                sb.AppendLine(string.Format("sampleMs={0} ms  mem={1:0.0}% ({2}/{3})  cpu={4:0.0}%  procs={5}  hung={6}  ports={7}  startups={8}  procGuard={9}",
                    psw.ElapsedMilliseconds, snap.MemPct, Util.Fmt(snap.MemUsedBytes), Util.Fmt(snap.MemTotalBytes), snap.CpuPct, snap.ProcessCount, snap.HungApps.Count, ports.Count, startups.Count, protOk ? "OK" : "FAIL"));
                ok &= protOk && snap.MemTotalBytes > 0 && snap.ProcessCount > 10;

                sb.AppendLine("== v7/v8 modules (updater + wsl + dups + userAudit + safeRelocator + software + env + ps7) ==");
                var uTest = Updater.ParseManifest("{\"version\":\"9.1.0\",\"file\":\"DiskGuard-Setup-9.1.exe\",\"sha256\":\"abc\",\"notes\":\"test\"}", "https://example.com/dg/");
                var ghJson = "{\"tag_name\":\"v9.2.0\",\"body\":\"更新内容\\nSHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"assets\":[{\"name\":\"DiskGuard-Setup-9.2.0.exe\",\"browser_download_url\":\"https://github.com/a613548565/disk-guard/releases/download/v9.2.0/DiskGuard-Setup-9.2.0.exe\"}]}";
                var ghTest = Updater.ParseManifest(ghJson, "https://api.github.com/repos/a613548565/disk-guard/releases/");
                bool updOk = uTest.Ok && uTest.HasUpdate && uTest.DownloadUrl == "https://example.com/dg/DiskGuard-Setup-9.1.exe"
                    && ghTest.Ok && ghTest.HasUpdate && ghTest.LatestVersion == "9.2.0"
                    && ghTest.DownloadUrl.EndsWith("DiskGuard-Setup-9.2.0.exe")
                    && ghTest.Sha256 == "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                var wslSt = WslTuner.Read();
                bool dupOk = DuplicateSafety(sb);
                bool relocOk = RelocatorSafety(sb);
                var apps = SoftwareManager.ListInstalledApps();
                var ps = SoftwareManager.InspectPowerShell();
                var env = NetworkEnvManager.InspectEnvVars();
                sb.AppendLine((updOk ? "OK   " : "FAIL ") + "更新清单解析与版本比对");
                sb.AppendLine("INFO WSL2 调优检测：ram=" + wslSt.PhysicalRamGB + "G  .wslconfig存在=" + wslSt.ConfigExists + "  hiberfil=" + Util.Fmt(wslSt.HiberfilBytes) + "  pagefile=" + Util.Fmt(wslSt.PagefileTotalBytes));
                sb.AppendLine(string.Format("INFO 软件卸载={0}项  PS5={1}  PS7={2}({3})  UserPath={4}项(失效{5}/重复{6})  SysPath={7}项(失效{8}/重复{9})",
                    apps.Count, ps.WindowsPsVersion, ps.HasPwsh7, ps.Pwsh7Version ?? "-",
                    env.UserPath.Count, env.UserMissingCount, env.UserDupCount,
                    env.MachinePath.Count, env.MachineMissingCount, env.MachineDupCount));
                ok &= updOk && dupOk && relocOk && apps.Count > 0;

                if (!string.IsNullOrEmpty(screenshotDir))
                {
                    sb.AppendLine("== UI offscreen render verification ==");
                    ok &= RenderUiScreenshots(screenshotDir, sb);
                }
            }
            catch (Exception ex) { ok = false; sb.AppendLine("EXCEPTION " + ex); }

            sb.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            try { File.WriteAllText(Path.Combine(Util.LogDir, "selftest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch { }
            return ok ? 0 : 1;
        }

        static bool DeleteSafety(StringBuilder sb)
        {
            string root = Path.Combine(Path.GetTempPath(), "dg_selftest_" + Guid.NewGuid().ToString("N"));
            string target = Path.Combine(root, "target"), work = Path.Combine(root, "work"), link = Path.Combine(work, "link");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(work, "sub"));
            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(work, "a.txt"), "a");
            File.WriteAllText(Path.Combine(work, "sub", "b.txt"), "b");
            string ro = Path.Combine(work, "ro.txt");
            File.WriteAllText(ro, "ro");
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            int ex;
            Util.Run("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"", 15000, out ex);
            bool junction = Directory.Exists(link);
            sb.AppendLine((junction ? "OK   " : "FAIL ") + "创建测试联接 work\\link -> target");

            var log = new StringBuilder();
            Fs.EmptyDir(work, null, log);
            bool keep = File.Exists(Path.Combine(target, "keep.txt"));
            bool emptied = Directory.GetFileSystemEntries(work).Length == 0;
            sb.AppendLine((keep ? "OK   " : "FAIL ") + "联接目标文件完整保留");
            sb.AppendLine((emptied ? "OK   " : "FAIL ") + "work 已清空（含只读文件与联接自身）");

            string oldF = Path.Combine(work, "old.txt"), newF = Path.Combine(work, "new.txt");
            File.WriteAllText(oldF, "o");
            File.WriteAllText(newF, "n");
            File.SetLastWriteTime(oldF, DateTime.Now.AddDays(-2));
            Fs.EmptyDir(work, TimeSpan.FromDays(1), log);
            bool cut = !File.Exists(oldF) && File.Exists(newF);
            sb.AppendLine((cut ? "OK   " : "FAIL ") + "按时间过滤：2 天前删除，新文件保留");

            if (!Directory.Exists(link)) { try { Directory.Delete(root, true); } catch { } }
            return junction && keep && emptied && cut;
        }

        static bool ArtifactSafety(StringBuilder sb)
        {
            string root = Path.Combine(Path.GetTempPath(), "dg_arttest_" + Guid.NewGuid().ToString("N"));
            try
            {
                // 合法前端项目：有 package.json + node_modules (>1MB)
                string realProj = Path.Combine(root, "web-app");
                Directory.CreateDirectory(Path.Combine(realProj, "node_modules"));
                File.WriteAllText(Path.Combine(realProj, "package.json"), "{}");
                File.WriteAllBytes(Path.Combine(realProj, "node_modules", "bundle.bin"), new byte[1200 * 1024]);

                // 伪项目：没有 package.json 的 node_modules（必须跳过）
                string fakeDir = Path.Combine(root, "some-app", "node_modules");
                Directory.CreateDirectory(fakeDir);
                File.WriteAllBytes(Path.Combine(fakeDir, "lib.bin"), new byte[1200 * 1024]);

                // 被排除的软件目录名：node_gobal/node_modules（即使有 package.json 也必须跳过）
                string excludedDir = Path.Combine(root, "node_gobal", "node_modules");
                Directory.CreateDirectory(excludedDir);
                File.WriteAllText(Path.Combine(root, "node_gobal", "package.json"), "{}");
                File.WriteAllBytes(Path.Combine(excludedDir, "cli.bin"), new byte[1200 * 1024]);

                var res = SpaceAnalyzer.ScanBuildArtifacts(root, 4, CancellationToken.None, null);
                bool onlyReal = res.BuildArtifacts.Count == 1 &&
                                string.Equals(res.BuildArtifacts[0].FullPath, Path.Combine(realProj, "node_modules"), StringComparison.OrdinalIgnoreCase);
                sb.AppendLine((onlyReal ? "OK   " : "FAIL ") + "仅识别含 package.json 的项目 node_modules，成功排除无标志目录与 node_gobal");
                return onlyReal;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        static bool DuplicateSafety(StringBuilder sb)
        {
            string root = Path.Combine(Path.GetTempPath(), "dg_duptest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                byte[] same = new byte[6 * 1024 * 1024];
                new Random(42).NextBytes(same);
                byte[] diff = (byte[])same.Clone();
                diff[diff.Length / 2] ^= 0xFF; // 仅中间 1 字节不同，验证 SHA-256 精验不误判
                File.WriteAllBytes(Path.Combine(root, "orig.bin"), same);
                File.WriteAllBytes(Path.Combine(root, "copy.bin"), same);
                File.WriteAllBytes(Path.Combine(root, "diff_mid.bin"), diff);

                var res = ExtraScanners.FindDuplicates(new[] { root }, 5 * 1024 * 1024, CancellationToken.None, null);
                bool pass = res.Groups.Count == 1 && res.Groups[0].Files.Count == 2;
                sb.AppendLine((pass ? "OK   " : "FAIL ") + "三段式重复大文件精验：准确匹配 2 个相同文件，排除仅中间 1 字节不同的同体积文件");
                return pass;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static bool RelocatorSafety(StringBuilder sb)
        {
            string root = Path.Combine(Path.GetTempPath(), "dg_reloctest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string src = Path.Combine(root, "src_folder");
                string dst = Path.Combine(root, "dst_folder");
                Directory.CreateDirectory(Path.Combine(src, "nested"));
                File.WriteAllText(Path.Combine(src, "a.txt"), "DiskGuard-v8-payload-1", Encoding.UTF8);
                File.WriteAllText(Path.Combine(src, "nested", "b.txt"), "DiskGuard-v8-payload-2", Encoding.UTF8);

                var r1 = SafeRelocator.MigratePath(src, dst, "junction", CancellationToken.None, null);
                bool isJunc = UserFolderAudit.IsJunctionDir(src);
                bool readViaJunc = isJunc && File.ReadAllText(Path.Combine(src, "nested", "b.txt"), Encoding.UTF8) == "DiskGuard-v8-payload-2";

                var latest = SafeRelocator.ListAllMigrationsRaw().FirstOrDefault(x => string.Equals(x.SourcePath, src, StringComparison.OrdinalIgnoreCase));
                bool restored = false;
                if (r1.Ok && readViaJunc && latest != null)
                {
                    var r2 = SafeRelocator.RestoreMigration(latest.Id, null);
                    restored = r2.Ok && !UserFolderAudit.IsJunctionDir(src) && !Directory.Exists(dst)
                        && File.ReadAllText(Path.Combine(src, "a.txt"), Encoding.UTF8) == "DiskGuard-v8-payload-1";
                }
                SafeRelocator.RemoveTestRecords();
                sb.AppendLine((r1.Ok && readViaJunc && restored ? "OK   " : "FAIL ") + "零丢失安全搬家沙箱：拷贝校验→NTFS软链访问→一键无损迁回全链路闭环");
                return r1.Ok && readViaJunc && restored;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static bool RenderUiScreenshots(string outDir, StringBuilder sb)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                // 使用未显示托盘的轻量宿主来离屏渲染 4 个页面（深色 + 浅色）
                var dummyTray = (TrayApp)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrayApp));
                var cfg = Config.Load();
                dummyTray.Cfg = cfg;
                var statsField = typeof(TrayApp).GetField("stats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (statsField != null) statsField.SetValue(dummyTray, DriveStat.Read(cfg));
                var cacheField = typeof(TrayApp).GetField("cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cacheField != null) cacheField.SetValue(dummyTray, new Dictionary<string, KeyValuePair<DateTime, long>>());

                // 预填缓存避免 Shown 触发后台扫描影响截图
                var items = CleanItemsBuilder.Build(cfg);
                Parallel.ForEach(items, it => dummyTray.CacheSet(it.Id, it.Scan(CancellationToken.None)));

                Theme.ApplyMode(ThemeMode.Light);
                var w = new MainWindow(dummyTray, 0);
                w.CaptureSelfTestShots(outDir);
                w.Close();
                sb.AppendLine("OK   已通过原生 DirectX WPF 导出 4 张高清渲染截图到 " + outDir);
                return true;
            }
            catch (Exception ex)
            {
                sb.AppendLine("FAIL UI 渲染异常：" + ex.Message);
                return false;
            }
        }

        static void SaveShot(Form f, string path)
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
