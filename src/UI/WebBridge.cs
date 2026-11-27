// DiskGuard v5 - WebView2 JSON 双向通信桥接（全盘四级体检 / Docker 专治与 VHDX 瘦身 / 骤降抓凶手 / 智能自动清理）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiskGuard
{
    public class WebBridge
    {
        readonly TrayApp app;
        readonly MainForm host;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        List<CleanItem> cleanItems = new List<CleanItem>();
        CancellationTokenSource cleanScanCts;
        CancellationTokenSource spaceScanCts;
        CancellationTokenSource auditCts;
        bool cleanBusy;
        bool spaceBusy;
        bool dockerBusy;

        public WebBridge(TrayApp app, MainForm host)
        {
            this.app = app;
            this.host = host;
            cleanItems = CleanItemsBuilder.Build(app.Cfg);
        }

        public void CancelAll()
        {
            if (cleanScanCts != null) { try { cleanScanCts.Cancel(); } catch { } }
            if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            if (auditCts != null) { try { auditCts.Cancel(); } catch { } }
        }

        public void Push(string ev, object payload)
        {
            var msg = new Dictionary<string, object> { { "event", ev }, { "data", payload } };
            host.PostJson(json.Serialize(msg));
        }

        public void PushDrives() { Push("drives", BuildDrivesPayload()); }

        public void HandleMessage(string rawJson)
        {
            Dictionary<string, object> req;
            try { req = json.Deserialize<Dictionary<string, object>>(rawJson); }
            catch { return; }
            if (req == null || !req.ContainsKey("cmd")) return;
            string cmd = Str(req, "cmd");

            switch (cmd)
            {
                case "init": SendInitState(); break;
                case "refreshDrives": app.RefreshStats(); PushDrives(); break;
                case "scanClean": StartCleanScan(true); break;
                case "execClean": ExecuteClean(StrList(req, "ids")); break;
                case "inspectCleanItem": InspectCleanItem(Str(req, "id")); break;
                case "runFullAudit": StartFullAudit(); break;
                case "cancelFullAudit": if (auditCts != null) { try { auditCts.Cancel(); } catch { } } break;
                case "startSpaceScan": StartSpaceScan(Str(req, "mode"), Str(req, "path"), Int(req, "hours", 1)); break;
                case "cancelSpaceScan": if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } } break;
                case "browseFolder": BrowseFolder(Str(req, "initial"), Str(req, "purpose")); break;
                case "openExplorer": Util.OpenInExplorer(Str(req, "path")); break;
                case "trashFile": TrashFile(Str(req, "path")); break;
                case "cleanArtifacts": CleanArtifacts(StrList(req, "paths")); break;
                case "inspectDocker": InspectDockerAsync(Bool(req, "withVolumes")); break;
                case "dockerPruneSafe": RunDockerAction("pruneSafe", null); break;
                case "dockerRmContainers": RunDockerAction("rmContainers", StrList(req, "ids")); break;
                case "dockerRmImages": RunDockerAction("rmImages", StrList(req, "ids")); break;
                case "dockerRmVolumes": RunDockerAction("rmVolumes", StrList(req, "names")); break;
                case "dockerCompactVhdx": RunDockerCompactAsync(); break;
                case "samplePerf": SamplePerfAsync(); break;
                case "cleanMemory": RunMemoryCleanAsync(req.ContainsKey("pid") ? (int?)Int(req, "pid", 0) : null); break;
                case "throttleProcess": RunProcActionAsync("throttle", Int(req, "pid", 0)); break;
                case "killProcess": RunProcActionAsync("kill", Int(req, "pid", 0)); break;
                case "runBugFix": RunBugFixAsync(Str(req, "fixId")); break;
                case "scanPorts": ScanPortsAsync(Str(req, "filter")); break;
                case "listStartups": ListStartupsAsync(); break;
                case "toggleStartup": ToggleStartupAsync(Str(req, "id"), Bool(req, "enable")); break;
                case "checkUpdate": CheckUpdateAsync(Str(req, "url")); break;
                case "downloadUpdate": DownloadUpdateAsync(Str(req, "url"), Str(req, "sha256")); break;
                case "getWslTuner": SendWslTunerAsync(); break;
                case "saveWslConfig": SaveWslConfigAsync(req); break;
                case "toggleHibernation": ToggleHibernationAsync(Bool(req, "enable")); break;
                case "openVirtualMemory": WslTuner.OpenVirtualMemoryDialog(); break;
                case "scanDuplicates": ScanDuplicatesAsync(Str(req, "scope"), Int(req, "minMB", 20)); break;
                case "scanChatFiles": ScanChatFilesAsync(Int(req, "minMB", 5)); break;
                case "trashBatch": TrashBatchAsync(StrList(req, "paths")); break;
                case "scanUserFolder": ScanUserFolderAsync(); break;
                case "cleanUserItem": CleanUserItemAsync(Str(req, "relPath")); break;
                case "inspectAppCaches": InspectAppCachesAsync(); break;
                case "relocateAppCache": RelocateAppCacheAsync(Str(req, "id"), Str(req, "targetBase"), Bool(req, "moveExisting")); break;
                case "listMigrations": PushMigrations(); break;
                case "migratePath": MigratePathAsync(Str(req, "sourcePath"), Str(req, "targetPath"), Str(req, "mode")); break;
                case "restoreMigration": RestoreMigrationAsync(Str(req, "id")); break;
                case "browseFile": BrowseFile(Str(req, "purpose")); break;
                case "listSoftware": ListSoftwareAsync(); break;
                case "uninstallApp": UninstallAppAsync(Str(req, "id")); break;
                case "scanLeftovers": ScanLeftoversAsync(Str(req, "name"), Str(req, "publisher"), Str(req, "installLocation")); break;
                case "inspectPowerShell": InspectPowerShellAsync(); break;
                case "upgradePowerShell7": UpgradePowerShell7Async(); break;
                case "inspectNetwork": InspectNetworkAsync(); break;
                case "setDns": SetDnsAsync(Str(req, "adapter"), Str(req, "primary"), Str(req, "secondary")); break;
                case "clearSystemProxy": ClearSystemProxyAsync(); break;
                case "optimizeTcp": OptimizeTcpAsync(); break;
                case "inspectEnv": InspectEnvAsync(); break;
                case "cleanPath": CleanPathAsync(Str(req, "scope"), Bool(req, "removeMissing"), Bool(req, "removeDuplicates")); break;
                case "saveEnvVar": SaveEnvVarAsync(Str(req, "scope"), Str(req, "name"), Str(req, "value")); break;
                case "deleteEnvVar": DeleteEnvVarAsync(Str(req, "scope"), Str(req, "name")); break;
                case "backupEnv": BackupEnvAsync(); break;
                case "restoreEnvBackup": RestoreEnvBackupAsync(Str(req, "fileName")); break;
                case "getTrend": SendTrend(Str(req, "drive"), Int(req, "days", 1)); break;
                case "setTheme": ApplyTheme(Str(req, "mode")); break;
                case "saveSettings": SaveSettings(req); break;
                case "runAutoCleanNow":
                    app.RunAutoCleanAsync("手动试跑自动安全清理策略", (freed, delta) =>
                    {
                        Push("autoCleanDone", new Dictionary<string, object>
                        {
                            { "freedText", Util.Fmt(freed) },
                            { "deltaText", Util.FmtDelta(delta) },
                            { "drives", BuildDrivesPayload() },
                            { "logSummary", BuildLogSummaryPayload() },
                            { "config", BuildConfigPayload() }
                        });
                    });
                    break;
                case "setAutoStart":
                    try { AutoStart.Set(Bool(req, "on")); } catch { }
                    Push("toggles", BuildTogglesPayload());
                    break;
                case "setTrayPin":
                    if (TrayPin.State() >= 0) { try { TrayPin.Set(Bool(req, "on")); } catch { } }
                    Push("toggles", BuildTogglesPayload());
                    break;
                case "removeExtraDir": RemoveExtraDir(Str(req, "path")); break;
                case "openLogFile": Util.OpenPath(File.Exists(Util.LogFile) ? Util.LogFile : Util.LogDir); break;
                case "openConfigDir": Util.OpenInExplorer(Config.FilePath); break;
            }
        }

        void SendInitState()
        {
            cleanItems = CleanItemsBuilder.Build(app.Cfg);
            bool allCached = true;
            foreach (var it in cleanItems)
            {
                long v;
                if (app.CacheGet(it.Id, out v)) { it.LastSize = v; it.StatusOverride = null; }
                else allCached = false;
            }

            var payload = new Dictionary<string, object>
            {
                { "initialTab", host.InitialTab },
                { "isDark", Theme.Current.IsDark },
                { "themeMode", app.Cfg.Theme.ToString().ToLowerInvariant() },
                { "drives", BuildDrivesPayload() },
                { "allFixedDrives", DriveStat.AllFixedDrives() },
                { "config", BuildConfigPayload() },
                { "toggles", BuildTogglesPayload() },
                { "cleanItems", BuildCleanItemsPayload() },
                { "cleanCached", allCached },
                { "codeDir", Directory.Exists(@"E:\code") ? @"E:\code" : Util.UserProfile },
                { "logSummary", BuildLogSummaryPayload() }
            };
            Push("init", payload);
            if (!allCached) StartCleanScan(false);
        }

        List<Dictionary<string, object>> BuildDrivesPayload()
        {
            var list = new List<Dictionary<string, object>>();
            var cfg = app.Cfg;
            foreach (var d in app.Stats)
            {
                long delta = 0; TimeSpan span = TimeSpan.Zero;
                bool hasChg = d.Ready && History.Change(d.Name, TimeSpan.FromHours(24), d.Free, out delta, out span) && span.TotalMinutes >= 20;
                list.Add(new Dictionary<string, object>
                {
                    { "name", d.Name },
                    { "label", string.IsNullOrEmpty(d.Label) ? (d.Name == "C" ? "系统盘" : "数据盘") : d.Label },
                    { "ready", d.Ready },
                    { "free", d.Free },
                    { "total", d.Total },
                    { "freeText", Util.Fmt(d.Free) },
                    { "totalText", Util.Fmt(d.Total) },
                    { "usedPct", Math.Round(d.UsedRatio * 100.0, 1) },
                    { "level", d.Level(cfg) },
                    { "healthLabel", d.HealthLabel(cfg) },
                    { "changeText", History.ChangeText(d) },
                    { "changeDrop", hasChg && delta < 0 },
                    { "forecast", History.ForecastText(d) }
                });
            }
            return list;
        }

        Dictionary<string, object> BuildConfigPayload()
        {
            var c = app.Cfg;
            return new Dictionary<string, object>
            {
                { "drives", c.EffectiveDrives() },
                { "autoDiscoverDrives", c.AutoDiscoverDrives },
                { "warnGB", c.WarnGB },
                { "critGB", c.CritGB },
                { "dropAlertGB", c.DropAlertGB },
                { "intervalSec", c.IntervalSec },
                { "theme", c.Theme.ToString().ToLowerInvariant() },
                { "autoCleanSchedule", c.AutoCleanSchedule },
                { "autoCleanOnLowSpace", c.AutoCleanOnLowSpace },
                { "autoCleanLowSpaceGB", c.AutoCleanLowSpaceGB },
                { "autoCleanTempAgeDays", c.AutoCleanTempAgeDays },
                { "autoCleanDisabledIds", c.AutoCleanDisabledIds },
                { "lagWatchdog", c.LagWatchdog },
                { "autoCleanMemOnHigh", c.AutoCleanMemOnHigh },
                { "autoCleanMemPct", c.AutoCleanMemPct },
                { "autoCheckUpdate", c.AutoCheckUpdate },
                { "updateUrl", c.UpdateUrl ?? "" },
                { "currentVersion", Updater.CurrentVersion },
                { "lastAutoClean", string.IsNullOrEmpty(c.LastAutoClean) ? "尚未执行" : c.LastAutoClean },
                { "extraDirs", c.ExtraDirs }
            };
        }

        Dictionary<string, object> BuildTogglesPayload()
        {
            return new Dictionary<string, object>
            {
                { "autoStart", AutoStart.Enabled },
                { "trayPin", TrayPin.State() == 1 },
                { "trayPinAvailable", TrayPin.State() >= 0 }
            };
        }

        static string GroupKeyOf(CleanItem it)
        {
            if (!it.DefaultOn) return "advanced";
            if (it.Category == CleanCategory.BrowserApp) return "apps";
            if (it.Category == CleanCategory.DevTools) return "dev";
            return "system";
        }

        List<Dictionary<string, object>> BuildCleanItemsPayload()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var it in cleanItems)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "id", it.Id },
                    { "name", it.Name },
                    { "note", it.Note },
                    { "group", GroupKeyOf(it) },
                    { "category", it.CategoryLabel },
                    { "defaultOn", it.DefaultOn },
                    { "isCaution", it.IsCaution },
                    { "riskLevel", it.DefaultOn ? "L1" : "L3" },
                    { "size", it.LastSize },
                    { "sizeText", it.LastSize == -2 ? "未运行" : Util.Fmt(it.LastSize) },
                    { "scanMs", it.LastScanMs },
                    { "canOpen", it.InspectPaths != null && it.InspectPaths.Length > 0 }
                });
            }
            return list;
        }

        Dictionary<string, object> BuildLogSummaryPayload()
        {
            var sum = LogSummary.Read();
            return new Dictionary<string, object>
            {
                { "runCount", sum.RunCount },
                { "totalFreed", sum.TotalFreedBytes },
                { "totalFreedText", Util.Fmt(sum.TotalFreedBytes) },
                { "lastRun", sum.LastRunTime },
                { "recentText", sum.RecentText }
            };
        }

        void StartCleanScan(bool force)
        {
            if (cleanBusy) return;
            if (cleanScanCts != null) { try { cleanScanCts.Cancel(); } catch { } }
            cleanScanCts = new CancellationTokenSource();
            var ct = cleanScanCts.Token;
            cleanBusy = true;

            if (force) cleanItems = CleanItemsBuilder.Build(app.Cfg);
            var snapshot = cleanItems.ToList();
            int total = snapshot.Count, done = 0;
            var sw = Stopwatch.StartNew();

            Push("cleanScanStart", new Dictionary<string, object> { { "total", total } });

            foreach (var item in snapshot)
            {
                var it = item;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var isw = Stopwatch.StartNew();
                    long sz;
                    try { sz = it.Scan(ct); } catch { sz = -1; }
                    isw.Stop();
                    if (ct.IsCancellationRequested) return;
                    it.LastSize = sz;
                    it.LastScanMs = isw.ElapsedMilliseconds;
                    app.CacheSet(it.Id, sz);
                    int n = Interlocked.Increment(ref done);
                    Push("cleanScanProgress", new Dictionary<string, object>
                    {
                        { "id", it.Id }, { "size", sz }, { "sizeText", sz == -2 ? "未运行" : Util.Fmt(sz) },
                        { "scanMs", it.LastScanMs }, { "done", n }, { "total", total }
                    });
                    if (n == total)
                    {
                        cleanBusy = false;
                        Push("cleanScanDone", new Dictionary<string, object>
                        {
                            { "elapsedSec", Math.Round(sw.Elapsed.TotalSeconds, 2) },
                            { "items", BuildCleanItemsPayload() }
                        });
                    }
                });
            }
        }

        void StartFullAudit()
        {
            if (auditCts != null) { try { auditCts.Cancel(); } catch { } }
            auditCts = new CancellationTokenSource();
            var ct = auditCts.Token;
            Push("fullAuditStart", new Dictionary<string, object>());

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var itemsSnap = CleanItemsBuilder.Build(app.Cfg);
                var r = SpaceAnalyzer.RunFullAudit(app.Cfg, itemsSnap, ct, (msg, pct) =>
                {
                    Push("fullAuditProgress", new Dictionary<string, object> { { "text", msg }, { "pct", pct } });
                });
                cleanItems = itemsSnap;
                foreach (var it in itemsSnap) app.CacheSet(it.Id, it.LastSize);

                Push("fullAuditDone", new Dictionary<string, object>
                {
                    { "elapsedSec", Math.Round(r.ElapsedMs / 1000.0, 1) },
                    { "l1Bytes", r.L1Bytes },
                    { "l1Text", Util.Fmt(r.L1Bytes) },
                    { "l2ArtifactBytes", r.L2ArtifactBytes },
                    { "l2ArtifactText", Util.Fmt(r.L2ArtifactBytes) },
                    { "artifactCount", r.ArtifactCount },
                    { "l2LargeFileBytes", r.L2LargeFileBytes },
                    { "l2LargeFileText", Util.Fmt(r.L2LargeFileBytes) },
                    { "largeFileCount", r.LargeFileCount },
                    { "l3DockerBytes", r.L3DockerBytes },
                    { "l3DockerText", Util.Fmt(r.L3DockerBytes) },
                    { "l3RecycleBytes", r.L3RecycleBytes },
                    { "l3RecycleText", Util.Fmt(r.L3RecycleBytes) },
                    { "l4Locked", SerializeEntries(r.L4LockedItems) },
                    { "items", BuildCleanItemsPayload() }
                });
            });
        }

        void ExecuteClean(List<string> ids)
        {
            if (cleanBusy || ids == null || ids.Count == 0) return;
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            var targets = cleanItems.Where(i => idSet.Contains(i.Id)).ToList();
            if (targets.Count == 0) return;

            cleanBusy = true;
            var cfg = app.Cfg;
            Push("cleanExecStart", new Dictionary<string, object> { { "total", targets.Count } });

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var log = new StringBuilder();
                var before = DriveStat.Read(cfg);
                log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 开始清理 ====");
                long totalFreed = 0;

                for (int i = 0; i < targets.Count; i++)
                {
                    var it = targets[i];
                    Push("cleanExecProgress", new Dictionary<string, object> { { "id", it.Id }, { "name", it.Name }, { "done", i }, { "total", targets.Count } });
                    log.AppendLine("▶ " + it.Name);
                    long f = 0;
                    try { f = it.Clean(log); totalFreed += f; log.AppendLine("  删除 " + Util.Fmt(f)); }
                    catch (Exception ex) { log.AppendLine("  失败：" + ex.Message); }
                    app.CacheRemove(it.Id);
                    it.LastSize = 0;
                }

                var after = DriveStat.Read(cfg);
                var driveDiffs = new List<string>();
                foreach (var a in after)
                {
                    var b = before.FirstOrDefault(x => x.Name == a.Name);
                    if (b != null && a.Ready && b.Ready)
                    {
                        string line = string.Format("{0}: 可用 {1} → {2}", a.Name, Util.Fmt(b.Free), Util.Fmt(a.Free));
                        log.AppendLine(line);
                        if (Math.Abs(a.Free - b.Free) >= 5 * 1024 * 1024) driveDiffs.Add(line);
                    }
                }
                long delta = DriveStat.TotalFree(after) - DriveStat.TotalFree(before);
                log.AppendLine("合计：删除文件 " + Util.Fmt(totalFreed) + "，磁盘可用空间变化 " + Util.FmtDelta(delta));
                log.AppendLine();
                try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

                cleanBusy = false;
                host.InvokeOnUI(() =>
                {
                    app.RefreshStats();
                    app.Notify("DiskGuard 瘦身完成", "已安全释放 " + Util.Fmt(totalFreed) + "，可用空间 " + Util.FmtDelta(delta), ToolTipIcon.Info);
                });

                Push("cleanExecDone", new Dictionary<string, object>
                {
                    { "freed", totalFreed }, { "freedText", Util.Fmt(totalFreed) }, { "deltaText", Util.FmtDelta(delta) },
                    { "driveDiffs", driveDiffs }, { "drives", BuildDrivesPayload() },
                    { "items", BuildCleanItemsPayload() }, { "logSummary", BuildLogSummaryPayload() }
                });
            });
        }

        void InspectCleanItem(string id)
        {
            var it = cleanItems.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (it == null || it.InspectPaths == null || it.InspectPaths.Length == 0) return;
            var existing = it.InspectPaths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
            Util.OpenInExplorer(existing ?? it.InspectPaths[0]);
        }

        void StartSpaceScan(string mode, string path, int recentHours)
        {
            bool isAllFiles = (mode == "files" && string.Equals(path, "ALL", StringComparison.OrdinalIgnoreCase));
            if (mode != "recent" && !isAllFiles && (string.IsNullOrEmpty(path) || !Directory.Exists(path)))
            {
                Push("toast", new Dictionary<string, object> { { "type", "warn" }, { "text", "目录不存在：" + path } });
                return;
            }
            if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            spaceScanCts = new CancellationTokenSource();
            var ct = spaceScanCts.Token;
            spaceBusy = true;

            var effDrives = app.Cfg.EffectiveDrives();
            string drivesLabel = string.Join(" / ", effDrives.Select(d => d + ":").ToArray());
            string displayTarget = mode == "recent"
                ? ("最近 " + recentHours + " 小时大写入（" + drivesLabel + "）")
                : (isAllFiles ? ("全盘超大文件（" + drivesLabel + "）") : path);
            Push("spaceScanStart", new Dictionary<string, object> { { "mode", mode }, { "path", displayTarget } });

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Action<SpaceScanProgress> onProg = pr =>
                {
                    Push("spaceScanProgress", new Dictionary<string, object> { { "files", pr.ScannedFiles }, { "dirs", pr.ScannedDirs }, { "hint", pr.CurrentHint ?? "" } });
                };

                SpaceScanResult res;
                if (mode == "recent")
                    res = SpaceAnalyzer.ScanRecentWriters(effDrives, TimeSpan.FromHours(Math.Max(1, recentHours)), 10L * 1024 * 1024, ct, onProg);
                else if (isAllFiles)
                    res = SpaceAnalyzer.ScanLargeFilesMultiDrive(effDrives, 100L * 1024 * 1024, ct, onProg);
                else if (mode == "artifacts")
                    res = SpaceAnalyzer.ScanBuildArtifacts(path, 7, ct, onProg);
                else
                    res = SpaceAnalyzer.ScanLargeItems(path, 100L * 1024 * 1024, ct, onProg);

                spaceBusy = false;
                Push("spaceScanDone", new Dictionary<string, object>
                {
                    { "mode", mode }, { "rootPath", res.RootPath }, { "elapsedSec", Math.Round(res.ElapsedMs / 1000.0, 2) },
                    { "cancelled", res.Cancelled }, { "totalBytes", res.TotalScannedBytes }, { "totalText", Util.Fmt(res.TotalScannedBytes) },
                    { "directories", SerializeEntries(res.Directories) },
                    { "largeFiles", SerializeEntries(res.LargeFiles) },
                    { "artifacts", SerializeEntries(res.BuildArtifacts) }
                });
            });
        }

        // ---------------- Docker 专治中心 ----------------
        void InspectDockerAsync(bool withVolumes)
        {
            if (dockerBusy) return;
            dockerBusy = true;
            Push("dockerLoading", new Dictionary<string, object> { { "text", withVolumes ? "正在通过 WSL 统计 200+ 孤立卷体积与容器镜像…" : "正在读取 Docker 引擎与虚拟磁盘状态…" } });
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var ov = DockerManager.Inspect(app.Cfg, withVolumes);
                dockerBusy = false;
                Push("dockerOverview", SerializeDocker(ov));
            });
        }

        Dictionary<string, object> SerializeDocker(DockerOverview ov)
        {
            return new Dictionary<string, object>
            {
                { "installed", ov.Installed },
                { "running", ov.Running },
                { "isAdmin", ov.IsAdmin },
                { "error", ov.Error },
                { "vhdxPath", ov.VhdxPath },
                { "vhdxPhysicalBytes", ov.VhdxPhysicalBytes },
                { "vhdxPhysicalText", Util.Fmt(ov.VhdxPhysicalBytes) },
                { "vhdxInternalUsedBytes", ov.VhdxInternalUsedBytes },
                { "vhdxInternalUsedText", Util.Fmt(ov.VhdxInternalUsedBytes) },
                { "compactReclaimableBytes", ov.CompactReclaimableBytes },
                { "compactReclaimableText", Util.Fmt(ov.CompactReclaimableBytes) },
                { "buildCacheBytes", ov.BuildCacheBytes },
                { "buildCacheText", Util.Fmt(ov.BuildCacheBytes) },
                { "runningContainers", ov.RunningContainers.Select(c => new Dictionary<string, object> { { "id", c.Id }, { "name", c.Name }, { "image", c.Image }, { "status", c.Status } }).ToList() },
                { "stoppedContainers", ov.StoppedContainers.Select(c => new Dictionary<string, object> { { "id", c.Id }, { "name", c.Name }, { "image", c.Image }, { "status", c.Status } }).ToList() },
                { "unusedImages", ov.UnusedImages.Select(i => new Dictionary<string, object> { { "id", i.Id }, { "repoTag", i.RepoTag }, { "sizeBytes", i.SizeBytes }, { "sizeText", Util.Fmt(i.SizeBytes) }, { "created", i.CreatedSince }, { "isDangling", i.IsDangling } }).ToList() },
                { "danglingVolumes", ov.DanglingVolumes.Select(v => new Dictionary<string, object> { { "name", v.Name }, { "project", v.ProjectHint }, { "sizeBytes", v.SizeBytes }, { "sizeText", Util.Fmt(v.SizeBytes) } }).ToList() }
            };
        }

        void RunDockerAction(string action, List<string> targets)
        {
            if (dockerBusy) return;
            dockerBusy = true;
            Push("dockerLoading", new Dictionary<string, object> { { "text", "正在执行 Docker 清理操作…" } });
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var log = new StringBuilder();
                log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Docker 专治：" + action + " ====");
                string msg = "";
                if (action == "pruneSafe")
                {
                    long freed = DockerManager.PruneSafeCache(app.Cfg, log);
                    msg = "已清理 Docker 构建缓存与悬空镜像（内部释放 " + Util.Fmt(freed) + "，执行 VHDX 压缩后可归还给 E 盘）";
                    log.AppendLine("合计：删除文件 " + Util.Fmt(freed));
                }
                else if (action == "rmContainers")
                {
                    int n = DockerManager.RemoveStoppedContainers(app.Cfg, targets, log);
                    msg = "已删除 " + n + " 个已停止容器（运行中容器未受影响）";
                }
                else if (action == "rmImages")
                {
                    int n = DockerManager.RemoveUnusedImages(app.Cfg, targets, log);
                    msg = "已删除 " + n + " 个未使用镜像（执行 VHDX 压缩后空间归还给宿主机）";
                }
                else if (action == "rmVolumes")
                {
                    int n = DockerManager.RemoveDanglingVolumes(app.Cfg, targets, log);
                    msg = "已永久删除 " + n + " 个孤立数据卷（执行 VHDX 压缩后空间归还给宿主机）";
                }
                log.AppendLine();
                try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

                var ov = DockerManager.Inspect(app.Cfg, true);
                dockerBusy = false;
                Push("dockerOverview", SerializeDocker(ov));
                Push("toast", new Dictionary<string, object> { { "type", "ok" }, { "text", msg } });
                Push("logSummaryUpdated", BuildLogSummaryPayload());
            });
        }

        void RunDockerCompactAsync()
        {
            if (dockerBusy) return;
            dockerBusy = true;
            Push("dockerCompactStart", new Dictionary<string, object>());
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var log = new StringBuilder();
                var res = DockerManager.CompactVhdx(app.Cfg, (txt, pct) =>
                {
                    Push("dockerCompactProgress", new Dictionary<string, object> { { "text", txt }, { "pct", pct } });
                }, log);
                try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

                host.InvokeOnUI(() => app.RefreshStats());
                var ov = DockerManager.Inspect(app.Cfg, false);
                dockerBusy = false;

                Push("dockerCompactDone", new Dictionary<string, object>
                {
                    { "success", res.Success },
                    { "message", res.Message },
                    { "restored", res.RestoredContainers },
                    { "failed", res.FailedContainers },
                    { "drives", BuildDrivesPayload() },
                    { "overview", SerializeDocker(ov) },
                    { "logSummary", BuildLogSummaryPayload() }
                });
            });
        }

        List<Dictionary<string, object>> SerializeEntries(List<SpaceEntry> list)
        {
            var outList = new List<Dictionary<string, object>>();
            if (list == null) return outList;
            foreach (var e in list)
            {
                double idleDays = e.LastWrite > DateTime.MinValue.AddYears(10) ? Math.Max(0, (DateTime.Now - e.LastWrite).TotalDays) : 0;
                outList.Add(new Dictionary<string, object>
                {
                    { "path", e.FullPath }, { "name", e.DisplayName }, { "parent", e.ParentLabel },
                    { "tag", e.Tag }, { "kind", e.Kind.ToString().ToLowerInvariant() },
                    { "size", e.Size }, { "sizeText", Util.Fmt(e.Size) },
                    { "ageText", e.LastWrite > DateTime.MinValue.AddYears(10) ? Util.FmtAge(e.LastWrite) : "" },
                    { "idleDays", Math.Round(idleDays, 1) },
                    { "isProtected", e.IsProtected }, { "advice", e.Advice ?? "" }
                });
            }
            return outList;
        }

        void BrowseFolder(string initial, string purpose)
        {
            host.InvokeOnUI(() =>
            {
                using (var dlg = new FolderBrowserDialog
                {
                    Description = purpose == "extraDir" ? "选择要加入安全清理的缓存目录" : "选择要透视分析的目录",
                    SelectedPath = (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) ? initial : ""
                })
                {
                    if (dlg.ShowDialog(host) != DialogResult.OK) return;
                    if (purpose == "extraDir")
                    {
                        if (Fs.IsProtected(dlg.SelectedPath) || Fs.IsSystemCriticalPath(dlg.SelectedPath))
                        {
                            Push("toast", new Dictionary<string, object> { { "type", "warn" }, { "text", "安全拦截：禁止添加受保护的系统目录或根目录" } });
                            return;
                        }
                        if (!app.Cfg.ExtraDirs.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase))
                        {
                            app.Cfg.ExtraDirs.Add(dlg.SelectedPath);
                            app.Cfg.Save();
                            cleanItems = CleanItemsBuilder.Build(app.Cfg);
                            Push("configUpdated", BuildConfigPayload());
                            Push("toast", new Dictionary<string, object> { { "type", "ok" }, { "text", "已添加自定义缓存目录" } });
                        }
                    }
                    else Push("folderPicked", new Dictionary<string, object> { { "path", dlg.SelectedPath }, { "purpose", purpose ?? "analyze" } });
                }
            });
        }

        void BrowseFile(string purpose)
        {
            host.InvokeOnUI(() =>
            {
                using (var dlg = new OpenFileDialog { Title = "选择要安全搬家的大文件", Filter = "所有文件 (*.*)|*.*" })
                {
                    if (dlg.ShowDialog(host) != DialogResult.OK) return;
                    Push("folderPicked", new Dictionary<string, object> { { "path", dlg.FileName }, { "purpose", purpose ?? "migrateSrc" } });
                }
            });
        }

        void TrashFile(string path)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = Win32Native.SendToRecycleBin(path);
                if (ok) host.InvokeOnUI(() => app.RefreshStats());
                Push("trashResult", new Dictionary<string, object> { { "path", path }, { "ok", ok }, { "drives", BuildDrivesPayload() } });
            });
        }

        void CleanArtifacts(List<string> paths)
        {
            if (spaceBusy || paths == null || paths.Count == 0) return;
            spaceBusy = true;
            Push("artifactCleanStart", new Dictionary<string, object> { { "total", paths.Count } });
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var log = new StringBuilder();
                var before = DriveStat.Read(app.Cfg);
                log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 清理项目构建产物 ====");
                long totalFreed = 0;
                var cleaned = new List<string>();
                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    Push("artifactCleanProgress", new Dictionary<string, object> { { "path", p }, { "done", i }, { "total", paths.Count } });
                    log.AppendLine("▶ " + p);
                    long f = Fs.DeleteDirSafe(p, log);
                    totalFreed += f;
                    cleaned.Add(p);
                    log.AppendLine("  删除 " + Util.Fmt(f));
                }
                var after = DriveStat.Read(app.Cfg);
                long delta = DriveStat.TotalFree(after) - DriveStat.TotalFree(before);
                log.AppendLine("合计：删除文件 " + Util.Fmt(totalFreed) + "，磁盘可用空间变化 " + Util.FmtDelta(delta));
                log.AppendLine();
                try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

                spaceBusy = false;
                host.InvokeOnUI(() => app.RefreshStats());
                Push("artifactCleanDone", new Dictionary<string, object>
                {
                    { "freed", totalFreed }, { "freedText", Util.Fmt(totalFreed) }, { "deltaText", Util.FmtDelta(delta) },
                    { "cleanedPaths", cleaned }, { "drives", BuildDrivesPayload() }, { "logSummary", BuildLogSummaryPayload() }
                });
            });
        }

        void SendTrend(string drive, int days)
        {
            if (string.IsNullOrEmpty(drive) && app.Cfg.Drives.Length > 0) drive = app.Cfg.Drives[0];
            TimeSpan win = days <= 1 ? TimeSpan.FromHours(24) : TimeSpan.FromDays(days);
            var series = History.GetSeries(drive, win);
            var stat = app.Stats.FirstOrDefault(s => s.Name == drive);
            if (stat != null && stat.Ready)
            {
                if (series.Count == 0 || (DateTime.Now - series[series.Count - 1].T).TotalMinutes >= 1)
                    series.Add(new Sample { T = DateTime.Now, D = drive, Free = stat.Free });
            }
            var pts = new List<Dictionary<string, object>>();
            string fmt = days > 1 ? "MM-dd HH:mm" : "HH:mm";
            foreach (var s in series)
            {
                pts.Add(new Dictionary<string, object>
                {
                    { "t", s.T.ToString(fmt) }, { "fullT", s.T.ToString("MM-dd HH:mm") },
                    { "gb", Math.Round(s.Free / 1073741824.0, 2) }, { "text", Util.Fmt(s.Free) }
                });
            }
            Push("trendData", new Dictionary<string, object>
            {
                { "drive", drive }, { "days", days }, { "warnGB", app.Cfg.WarnGB }, { "critGB", app.Cfg.CritGB },
                { "forecast", stat != null ? History.ForecastText(stat) : "暂无数据" },
                { "points", pts }, { "logSummary", BuildLogSummaryPayload() }
            });
        }

        void ApplyTheme(string mode)
        {
            var m = mode == "dark" ? ThemeMode.Dark : (mode == "light" ? ThemeMode.Light : ThemeMode.Auto);
            app.Cfg.Theme = m;
            app.Cfg.Save();
            Theme.ApplyMode(m);
            host.InvokeOnUI(() => { host.SyncDwmTheme(); app.RebuildMenuTheme(); });
            Push("themeChanged", new Dictionary<string, object> { { "mode", m.ToString().ToLowerInvariant() }, { "isDark", Theme.Current.IsDark } });
        }

        void SaveSettings(Dictionary<string, object> req)
        {
            var drives = StrList(req, "drives");
            if (drives.Count == 0) { Push("toast", new Dictionary<string, object> { { "type", "warn" }, { "text", "请至少选择一个监控盘符" } }); return; }
            double warn = Dbl(req, "warnGB", app.Cfg.WarnGB);
            double crit = Dbl(req, "critGB", app.Cfg.CritGB);
            if (crit >= warn) { Push("toast", new Dictionary<string, object> { { "type", "warn" }, { "text", "严重阈值必须小于预警阈值" } }); return; }

            var allFixed = DriveStat.AllFixedDrives();
            app.Cfg.AutoDiscoverDrives = allFixed.Count > 0 && drives.Count == allFixed.Count && allFixed.All(x => drives.Contains(x));
            app.Cfg.Drives = drives.ToArray();
            app.Cfg.WarnGB = warn;
            app.Cfg.CritGB = crit;
            app.Cfg.DropAlertGB = Dbl(req, "dropAlertGB", app.Cfg.DropAlertGB);
            app.Cfg.IntervalSec = Math.Max(10, Math.Min(600, Int(req, "intervalSec", app.Cfg.IntervalSec)));
            string sched = Str(req, "autoCleanSchedule");
            if (sched == "off" || sched == "daily" || sched == "weekly") app.Cfg.AutoCleanSchedule = sched;
            app.Cfg.AutoCleanOnLowSpace = Bool(req, "autoCleanOnLowSpace");
            app.Cfg.AutoCleanLowSpaceGB = Math.Max(2, Dbl(req, "autoCleanLowSpaceGB", app.Cfg.AutoCleanLowSpaceGB));
            app.Cfg.AutoCleanTempAgeDays = Math.Max(1, Math.Min(90, Int(req, "autoCleanTempAgeDays", app.Cfg.AutoCleanTempAgeDays)));
            if (req.ContainsKey("autoCleanDisabledIds")) app.Cfg.AutoCleanDisabledIds = StrList(req, "autoCleanDisabledIds");
            if (req.ContainsKey("lagWatchdog")) app.Cfg.LagWatchdog = Bool(req, "lagWatchdog");
            if (req.ContainsKey("autoCleanMemOnHigh")) app.Cfg.AutoCleanMemOnHigh = Bool(req, "autoCleanMemOnHigh");
            if (req.ContainsKey("autoCleanMemPct")) app.Cfg.AutoCleanMemPct = Math.Max(70, Math.Min(96, Int(req, "autoCleanMemPct", app.Cfg.AutoCleanMemPct)));
            if (req.ContainsKey("autoCheckUpdate")) app.Cfg.AutoCheckUpdate = Bool(req, "autoCheckUpdate");
            if (req.ContainsKey("updateUrl")) app.Cfg.UpdateUrl = Str(req, "updateUrl").Trim();
            app.Cfg.Save();

            host.InvokeOnUI(() => app.ApplyConfigChanged());
            Push("configUpdated", BuildConfigPayload());
            PushDrives();
            Push("toast", new Dictionary<string, object> { { "type", "ok" }, { "text", "设置与自动清理策略已保存生效" } });
        }

        void RemoveExtraDir(string path)
        {
            app.Cfg.ExtraDirs.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            app.Cfg.Save();
            cleanItems = CleanItemsBuilder.Build(app.Cfg);
            Push("configUpdated", BuildConfigPayload());
            Push("toast", new Dictionary<string, object> { { "type", "ok" }, { "text", "已移除自定义目录" } });
        }

        // ---------------- 性能监控 / 卡顿急救 / 系统 Bug 修复 ----------------
        void SamplePerfAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var snap = SystemHealth.SampleAll();
                Push("perfData", new Dictionary<string, object>
                {
                    { "cpuPct", snap.CpuPct },
                    { "memPct", snap.MemPct },
                    { "memTotalText", Util.Fmt(snap.MemTotalBytes) },
                    { "memUsedText", Util.Fmt(snap.MemUsedBytes) },
                    { "memAvailText", Util.Fmt(snap.MemAvailBytes) },
                    { "procCount", snap.ProcessCount },
                    { "cores", snap.LogicalCores },
                    { "isLagging", snap.IsLagging },
                    { "lagReason", snap.LagReason },
                    { "topMem", SerializeProcs(snap.TopMem) },
                    { "topCpu", SerializeProcs(snap.TopCpu) },
                    { "hungApps", SerializeProcs(snap.HungApps) }
                });
            });
        }

        List<Dictionary<string, object>> SerializeProcs(List<ProcMetric> list)
        {
            return list.Select(p => new Dictionary<string, object>
            {
                { "pid", p.Pid }, { "name", p.Name }, { "memBytes", p.WorkingSetBytes },
                { "memText", Util.Fmt(p.WorkingSetBytes) }, { "cpuPct", p.CpuPct },
                { "priority", p.Priority }, { "isProtected", p.IsProtected },
                { "responding", p.Responding }, { "title", p.WindowTitle ?? "" }
            }).ToList();
        }

        void RunMemoryCleanAsync(int? pid)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SystemHealth.CleanMemory(pid.HasValue && pid.Value > 0 ? pid : null);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", r.Detail ?? "" } });
                SamplePerfAsync();
            });
        }

        void RunProcActionAsync(string act, int pid)
        {
            if (pid <= 4) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = act == "kill" ? SystemHealth.KillUserProcess(pid) : SystemHealth.ThrottleProcess(pid);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", r.Detail ?? "" } });
                SamplePerfAsync();
            });
        }

        void RunBugFixAsync(string fixId)
        {
            Push("fixRunning", new Dictionary<string, object> { { "fixId", fixId } });
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SystemHealth.RunBugFix(fixId);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", r.Detail ?? "" } });
                if (fixId == "lag-rescue") SamplePerfAsync();
            });
        }

        void ScanPortsAsync(string filter)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = SystemHealth.ListListeningPorts(filter);
                Push("portsData", list.Select(p => new Dictionary<string, object>
                {
                    { "port", p.Port }, { "local", p.LocalAddr }, { "pid", p.Pid },
                    { "name", p.ProcName }, { "isProtected", p.IsProtected }
                }).ToList());
            });
        }

        void ListStartupsAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = SystemHealth.ListStartups();
                Push("startupsData", list.Select(s => new Dictionary<string, object>
                {
                    { "id", s.Id }, { "scope", s.Scope }, { "name", s.Name },
                    { "cmd", s.Command }, { "enabled", s.Enabled }
                }).ToList());
            });
        }

        void ToggleStartupAsync(string id, bool enable)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SystemHealth.ToggleStartup(id, enable);
                Push("toast", new Dictionary<string, object> { { "type", r.Ok ? "ok" : "warn" }, { "text", r.Summary } });
                ListStartupsAsync();
            });
        }

        // ---------------- 自动更新 / WSL2 调优 / 重复大文件 / 聊天大附件 ----------------
        void CheckUpdateAsync(string overrideUrl)
        {
            string url = !string.IsNullOrWhiteSpace(overrideUrl) ? overrideUrl.Trim() : app.Cfg.UpdateUrl;
            if (!string.IsNullOrWhiteSpace(overrideUrl) && !string.Equals(overrideUrl.Trim(), app.Cfg.UpdateUrl, StringComparison.OrdinalIgnoreCase))
            {
                app.Cfg.UpdateUrl = overrideUrl.Trim();
                app.Cfg.Save();
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var u = Updater.Check(url);
                Push("updateInfo", new Dictionary<string, object>
                {
                    { "ok", u.Ok }, { "hasUpdate", u.HasUpdate },
                    { "currentVersion", u.CurrentVersion }, { "latestVersion", u.LatestVersion },
                    { "url", u.DownloadUrl }, { "sha256", u.Sha256 },
                    { "notes", u.Notes }, { "error", u.Error }
                });
            });
        }

        void DownloadUpdateAsync(string url, string sha256)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = Updater.DownloadAndLaunchUpgrade(url, sha256, (pct, msg) =>
                {
                    Push("updateProgress", new Dictionary<string, object> { { "pct", pct }, { "text", msg } });
                });
                Push("updateResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary } });
            });
        }

        void SendWslTunerAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var st = WslTuner.Read();
                Push("wslTunerData", new Dictionary<string, object>
                {
                    { "configPath", st.ConfigPath }, { "configExists", st.ConfigExists },
                    { "physicalRamGB", st.PhysicalRamGB }, { "logicalCores", st.LogicalCores },
                    { "memoryGB", st.MemoryGB }, { "processors", st.Processors }, { "swapGB", st.SwapGB },
                    { "autoMemoryReclaim", st.AutoMemoryReclaim }, { "sparseVhd", st.SparseVhd },
                    { "hiberfilBytes", st.HiberfilBytes }, { "hiberfilText", Util.Fmt(st.HiberfilBytes) },
                    { "pagefileTotalBytes", st.PagefileTotalBytes }, { "pagefileTotalText", Util.Fmt(st.PagefileTotalBytes) },
                    { "pagefilePaths", st.PagefilePaths }
                });
            });
        }

        void SaveWslConfigAsync(Dictionary<string, object> req)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = WslTuner.SaveConfig(
                    Int(req, "memoryGB", 8),
                    Int(req, "processors", 4),
                    Int(req, "swapGB", 4),
                    Str(req, "autoMemoryReclaim"),
                    Bool(req, "sparseVhd"));
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                SendWslTunerAsync();
            });
        }

        void ToggleHibernationAsync(bool enable)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = WslTuner.ToggleHibernation(enable);
                host.InvokeOnUI(() => app.RefreshStats());
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", r.Detail ?? "" } });
                SendWslTunerAsync();
            });
        }

        void ScanDuplicatesAsync(string scope, int minMB)
        {
            if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            spaceScanCts = new CancellationTokenSource();
            var ct = spaceScanCts.Token;
            spaceBusy = true;

            string[] roots = string.IsNullOrEmpty(scope) || scope == "ALL"
                ? app.Cfg.EffectiveDrives().Select(d => d + ":\\").ToArray()
                : new[] { scope };
            Push("spaceScanStart", new Dictionary<string, object> { { "mode", "dups" }, { "path", string.Join(" / ", roots) + "（≥" + minMB + "MB 重复大文件）" } });

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = ExtraScanners.FindDuplicates(roots, Math.Max(5, minMB) * 1048576L, ct, pr =>
                {
                    Push("spaceScanProgress", new Dictionary<string, object> { { "files", pr.ScannedFiles }, { "dirs", 0 }, { "hint", pr.CurrentHint ?? "" } });
                });
                spaceBusy = false;
                Push("duplicatesDone", new Dictionary<string, object>
                {
                    { "elapsedSec", Math.Round(r.ElapsedMs / 1000.0, 2) },
                    { "cancelled", r.Cancelled },
                    { "scannedFiles", r.ScannedFiles },
                    { "wastedBytes", r.TotalWastedBytes },
                    { "wastedText", Util.Fmt(r.TotalWastedBytes) },
                    { "groups", r.Groups.Select(g => new Dictionary<string, object>
                        {
                            { "hash", g.HashShort }, { "size", g.FileSize }, { "sizeText", Util.Fmt(g.FileSize) },
                            { "wastedText", Util.Fmt(g.WastedBytes) }, { "files", SerializeEntries(g.Files) }
                        }).ToList() }
                });
            });
        }

        void ScanChatFilesAsync(int minMB)
        {
            if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            spaceScanCts = new CancellationTokenSource();
            var ct = spaceScanCts.Token;
            spaceBusy = true;
            Push("spaceScanStart", new Dictionary<string, object> { { "mode", "chat" }, { "path", "微信 / 企业微信 / QQ 接收大附件与视频（≥" + minMB + "MB）" } });

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = ExtraScanners.ScanChatAttachments(Math.Max(1, minMB) * 1048576L, ct);
                spaceBusy = false;
                Push("chatFilesDone", new Dictionary<string, object>
                {
                    { "elapsedSec", Math.Round(r.ElapsedMs / 1000.0, 2) },
                    { "totalBytes", r.TotalBytes }, { "totalText", Util.Fmt(r.TotalBytes) },
                    { "rootsCount", r.ScannedRoots.Count },
                    { "files", SerializeEntries(r.Files) }
                });
            });
        }

        void TrashBatchAsync(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int okCnt = 0;
                long freed = 0;
                var trashed = new List<string>();
                foreach (var p in paths)
                {
                    long len = Util.FileLen(p);
                    if (Win32Native.SendToRecycleBin(p)) { okCnt++; freed += len; trashed.Add(p); }
                }
                host.InvokeOnUI(() => app.RefreshStats());
                Push("trashBatchDone", new Dictionary<string, object>
                {
                    { "okCount", okCnt }, { "totalCount", paths.Count },
                    { "freedText", Util.Fmt(freed) }, { "trashedPaths", trashed },
                    { "drives", BuildDrivesPayload() }
                });
            });
        }

        // ---------------- v8.0: User 专清 / 缓存改位 / 安全搬家 / 软件卸载 / PS7 / 网络 / 环境变量 ----------------
        void ScanUserFolderAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = UserFolderAudit.Scan(CancellationToken.None);
                Push("userFolderData", new Dictionary<string, object>
                {
                    { "userRoot", r.UserRoot },
                    { "elapsedSec", Math.Round(r.ElapsedMs / 1000.0, 2) },
                    { "l1Bytes", r.L1TotalBytes }, { "l1Text", Util.Fmt(r.L1TotalBytes) },
                    { "l2Bytes", r.L2TotalBytes }, { "l2Text", Util.Fmt(r.L2TotalBytes) },
                    { "l4Bytes", r.L4TotalBytes }, { "l4Text", Util.Fmt(r.L4TotalBytes) },
                    { "items", r.Items.Select(x => new Dictionary<string, object>
                        {
                            { "id", x.Id }, { "name", x.Name }, { "fullPath", x.FullPath }, { "relPath", x.RelPath },
                            { "category", x.Category }, { "safety", x.Safety }, { "safetyText", x.SafetyText },
                            { "actionType", x.ActionType }, { "desc", x.Desc },
                            { "size", x.SizeBytes }, { "sizeText", Util.Fmt(x.SizeBytes) },
                            { "isJunction", x.IsJunction }, { "junctionTarget", x.JunctionTarget ?? "" }
                        }).ToList() }
                });
            });
        }

        void CleanUserItemAsync(string relPath)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = UserFolderAudit.CleanSafeUserItem(relPath);
                host.InvokeOnUI(() => app.RefreshStats());
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                PushDrives();
                Push("logSummaryUpdated", BuildLogSummaryPayload());
                ScanUserFolderAsync();
            });
        }

        void InspectAppCachesAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = SafeRelocator.InspectAppCaches(CancellationToken.None);
                Push("appCachesData", list.Select(x => new Dictionary<string, object>
                {
                    { "id", x.Id }, { "appName", x.AppName }, { "category", x.Category },
                    { "currentPath", x.CurrentPath }, { "drive", x.Drive },
                    { "size", x.SizeBytes }, { "sizeText", x.Exists ? Util.Fmt(x.SizeBytes) : "尚未产生" },
                    { "exists", x.Exists }, { "isOnSystemDrive", x.IsOnSystemDrive }, { "isJunction", x.IsJunction },
                    { "method", x.Method }, { "envKey", x.EnvKey ?? "" },
                    { "advice", x.Advice ?? "" }, { "canRelocate", x.CanRelocate }
                }).ToList());
            });
        }

        void RelocateAppCacheAsync(string id, string targetBase, bool moveExisting)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SafeRelocator.RelocateAppCache(id, targetBase, moveExisting, (txt, pct) =>
                {
                    Push("migrateProgress", new Dictionary<string, object> { { "text", txt }, { "pct", pct } });
                });
                host.InvokeOnUI(() => app.RefreshStats());
                Push("migrateDone", new Dictionary<string, object>
                {
                    { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "drives", BuildDrivesPayload() }
                });
                InspectAppCachesAsync();
                PushMigrations();
            });
        }

        void PushMigrations()
        {
            var list = SafeRelocator.ListMigrations();
            Push("migrationsData", list.Select(m => new Dictionary<string, object>
            {
                { "id", m.Id }, { "time", m.Time }, { "sourcePath", m.SourcePath }, { "targetPath", m.TargetPath },
                { "mode", m.Mode }, { "bytes", m.Bytes }, { "sizeText", Util.Fmt(m.Bytes) },
                { "fileCount", m.FileCount }, { "isDir", m.IsDir }, { "active", m.Active }
            }).ToList());
        }

        void MigratePathAsync(string src, string dst, string mode)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SafeRelocator.MigratePath(src, dst, mode, CancellationToken.None, (txt, pct) =>
                {
                    Push("migrateProgress", new Dictionary<string, object> { { "text", txt }, { "pct", pct } });
                });
                host.InvokeOnUI(() => app.RefreshStats());
                Push("migrateDone", new Dictionary<string, object>
                {
                    { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "drives", BuildDrivesPayload() }
                });
                PushMigrations();
            });
        }

        void RestoreMigrationAsync(string id)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SafeRelocator.RestoreMigration(id, (txt, pct) =>
                {
                    Push("migrateProgress", new Dictionary<string, object> { { "text", txt }, { "pct", pct } });
                });
                host.InvokeOnUI(() => app.RefreshStats());
                Push("migrateDone", new Dictionary<string, object>
                {
                    { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "drives", BuildDrivesPayload() }
                });
                PushMigrations();
            });
        }

        void ListSoftwareAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = SoftwareManager.ListInstalledApps();
                Push("softwareData", list.Select(a => new Dictionary<string, object>
                {
                    { "id", a.Id }, { "name", a.Name }, { "publisher", a.Publisher },
                    { "version", a.Version }, { "installDate", a.InstallDate },
                    { "installLocation", a.InstallLocation }, { "size", a.EstimatedBytes },
                    { "sizeText", a.EstimatedBytes > 0 ? Util.Fmt(a.EstimatedBytes) : "未知" },
                    { "scope", a.Scope }
                }).ToList());
            });
        }

        void UninstallAppAsync(string id)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SoftwareManager.LaunchUninstall(id);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
            });
        }

        void ScanLeftoversAsync(string name, string publisher, string installLoc)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = SoftwareManager.ScanLeftovers(name, publisher, installLoc);
                Push("leftoversData", new Dictionary<string, object>
                {
                    { "appName", name },
                    { "items", list.Select(x => new Dictionary<string, object>
                        {
                            { "path", x.Path }, { "area", x.Area }, { "size", x.SizeBytes }, { "sizeText", Util.Fmt(x.SizeBytes) }
                        }).ToList() }
                });
            });
        }

        void InspectPowerShellAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var st = SoftwareManager.InspectPowerShell();
                Push("psStatusData", new Dictionary<string, object>
                {
                    { "winPs", st.WindowsPsVersion }, { "hasPwsh7", st.HasPwsh7 },
                    { "pwsh7Ver", st.Pwsh7Version ?? "" }, { "pwsh7Path", st.Pwsh7Path ?? "" },
                    { "hasWinget", st.HasWinget }, { "wingetVer", st.WingetVersion ?? "" }
                });
            });
        }

        void UpgradePowerShell7Async()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SoftwareManager.UpgradePowerShell7();
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
            });
        }

        void InspectNetworkAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var st = NetworkEnvManager.InspectNetwork();
                Push("networkData", new Dictionary<string, object>
                {
                    { "adapterName", st.AdapterName }, { "adapterDesc", st.AdapterDesc },
                    { "ip", st.IpAddress }, { "currentDns", st.CurrentDns },
                    { "proxyEnabled", st.ProxyEnabled }, { "proxyServer", st.ProxyServer ?? "" },
                    { "autoConfigUrl", st.AutoConfigUrl ?? "" }, { "tcpAutoTuning", st.TcpAutoTuning },
                    { "dnsBench", st.DnsBench.Select(d => new Dictionary<string, object>
                        {
                            { "name", d.Name }, { "primary", d.Primary }, { "secondary", d.Secondary },
                            { "note", d.Note }, { "latencyMs", d.LatencyMs }
                        }).ToList() }
                });
            });
        }

        void SetDnsAsync(string adapter, string primary, string secondary)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.SetAdapterDns(adapter, primary, secondary);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectNetworkAsync();
            });
        }

        void ClearSystemProxyAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.ClearSystemProxy();
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectNetworkAsync();
            });
        }

        void OptimizeTcpAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.OptimizeTcpAndFlush();
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectNetworkAsync();
            });
        }

        void InspectEnvAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var e = NetworkEnvManager.InspectEnvVars();
                Push("envData", new Dictionary<string, object>
                {
                    { "userPathLen", e.UserPathLen }, { "machinePathLen", e.MachinePathLen },
                    { "userMissing", e.UserMissingCount }, { "userDup", e.UserDupCount },
                    { "machineMissing", e.MachineMissingCount }, { "machineDup", e.MachineDupCount },
                    { "userPath", e.UserPath.Select(p => new Dictionary<string, object> { { "idx", p.Index }, { "raw", p.RawValue }, { "exp", p.ExpandedValue }, { "exists", p.Exists }, { "dup", p.IsDuplicate } }).ToList() },
                    { "machinePath", e.MachinePath.Select(p => new Dictionary<string, object> { { "idx", p.Index }, { "raw", p.RawValue }, { "exp", p.ExpandedValue }, { "exists", p.Exists }, { "dup", p.IsDuplicate } }).ToList() },
                    { "userVars", e.UserVars.Select(v => new Dictionary<string, object> { { "name", v.Name }, { "value", v.Value }, { "expand", v.IsExpandable } }).ToList() },
                    { "machineVars", e.MachineVars.Select(v => new Dictionary<string, object> { { "name", v.Name }, { "value", v.Value }, { "expand", v.IsExpandable } }).ToList() },
                    { "backups", e.Backups.Select(b => new Dictionary<string, object> { { "file", b.FileName }, { "time", b.Time }, { "reason", b.Reason } }).ToList() }
                });
            });
        }

        void CleanPathAsync(string scope, bool removeMissing, bool removeDuplicates)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.CleanPathScope(scope, removeMissing, removeDuplicates);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectEnvAsync();
            });
        }

        void SaveEnvVarAsync(string scope, string name, string value)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.SaveEnvVar(scope, name, value);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectEnvAsync();
            });
        }

        void DeleteEnvVarAsync(string scope, string name)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.DeleteEnvVar(scope, name);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectEnvAsync();
            });
        }

        void BackupEnvAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string f = NetworkEnvManager.BackupEnvVars("手动完整快照");
                Push("fixResult", new Dictionary<string, object> { { "ok", !string.IsNullOrEmpty(f) }, { "title", "环境变量备份成功" }, { "summary", "已保存完整快照：" + f }, { "detail", "" } });
                InspectEnvAsync();
            });
        }

        void RestoreEnvBackupAsync(string fileName)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = NetworkEnvManager.RestoreUserEnvBackup(fileName);
                Push("fixResult", new Dictionary<string, object> { { "ok", r.Ok }, { "title", r.Title }, { "summary", r.Summary }, { "detail", "" } });
                InspectEnvAsync();
            });
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static int Int(Dictionary<string, object> d, string k, int def) { object v; int r; return (d.TryGetValue(k, out v) && v != null && int.TryParse(v.ToString(), out r)) ? r : def; }
        static double Dbl(Dictionary<string, object> d, string k, double def) { object v; double r; return (d.TryGetValue(k, out v) && v != null && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r)) ? r : def; }
        static bool Bool(Dictionary<string, object> d, string k) { object v; bool r; return d.TryGetValue(k, out v) && v != null && bool.TryParse(v.ToString(), out r) && r; }
        static List<string> StrList(Dictionary<string, object> d, string k)
        {
            var res = new List<string>();
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return res;
            var arr = v as System.Collections.IEnumerable;
            if (arr != null && !(v is string)) foreach (var x in arr) if (x != null) res.Add(x.ToString());
            return res;
        }
    }
}
