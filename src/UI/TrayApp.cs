// DiskGuard v5 - 托盘常驻主控（图标缓存 / 主题菜单 / 智能双触发自动清理 / 骤降直达抓凶手 / 内存修剪）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DiskGuard
{
    public class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public Config Cfg;
        readonly NotifyIcon ni;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly System.Windows.Forms.Timer lagTimer;
        readonly Control invoker;
        readonly EventWaitHandle showEvt;
        readonly Dictionary<string, KeyValuePair<DateTime, long>> cache = new Dictionary<string, KeyValuePair<DateTime, long>>();
        List<DriveStat> stats = new List<DriveStat>();
        int lastLevel = 0;
        DateTime lastCritNotify = DateTime.MinValue, lastDropNotify = DateTime.MinValue;
        DateTime lastLagNotify = DateTime.MinValue, lastAutoMemClean = DateTime.MinValue;
        int highMemStreak = 0, highCpuStreak = 0;
        double lastMemPct = 0, lastCpuPct = 0;
        int notifyTargetTab = 0;
        int autoCleanRunning = 0;
        IntPtr lastHicon = IntPtr.Zero;
        string lastIconKey = "";
        MainWindow win;

        public List<DriveStat> Stats { get { return stats; } }

        public TrayApp()
        {
            Cfg = Config.Load();
            Theme.ApplyMode(Cfg.Theme);
            History.Load();

            invoker = new Control();
            invoker.CreateControl();
            var unused = invoker.Handle;

            menu = new ContextMenuStrip { Font = Theme.FontBody, Renderer = new ThemedMenuRenderer() };
            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };

            ni = new NotifyIcon { ContextMenuStrip = menu, Visible = false };
            ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenMain(0); };
            ni.BalloonTipClicked += (s, e) => OpenMain(notifyTargetTab);

            SystemHealth.ReadWatchdogCpuPct(); // 初始化独立哨兵基准点
            RefreshStats();
            BuildMenu();
            ni.Visible = true;

            timer = new System.Windows.Forms.Timer { Interval = Cfg.IntervalSec * 1000 };
            timer.Tick += (s, e) => { RefreshStats(); CheckSmartAutoClean(); };
            timer.Start();

            // 10 秒低开销卡顿检测（独立时钟，绝不与前端面板互相干扰）
            lagTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            lagTimer.Tick += (s, e) => CheckLagWatchdog();
            lagTimer.Start();

            // 启动后延迟 25 秒静默检查一次更新（带更新内容摘要提醒）
            if (Cfg.AutoCheckUpdate && !string.IsNullOrWhiteSpace(Cfg.UpdateUrl))
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(25000);
                    var u = Updater.Check(Cfg.UpdateUrl);
                    if (u.Ok && u.HasUpdate)
                    {
                        string brief = string.IsNullOrWhiteSpace(u.Notes) ? "点击立即查看更新日志并自动升级" : u.Notes.Replace("\r", " ").Replace("\n", " ").Trim();
                        if (brief.Length > 70) brief = brief.Substring(0, 70) + "…";
                        try { invoker.BeginInvoke(new Action(() => Notify("DiskGuard 发现新版本 v" + u.LatestVersion, brief, ToolTipIcon.Info, 4))); } catch { }
                    }
                });
            }

            showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvt, (st, to) =>
            {
                try { invoker.BeginInvoke(new Action(() => OpenMain(0))); } catch { }
            }, null, -1, false);
        }

        public void RebuildMenuTheme()
        {
            if (menu == null) return;
            menu.Renderer = new ThemedMenuRenderer();
            menu.Invalidate();
        }

        public void ApplyConfigChanged()
        {
            timer.Interval = Cfg.IntervalSec * 1000;
            lastLevel = 0;
            lastIconKey = "";
            RebuildMenuTheme();
            RefreshStats();
        }

        public bool CacheGet(string key, out long v)
        {
            lock (cache)
            {
                KeyValuePair<DateTime, long> e;
                if (cache.TryGetValue(key, out e) && DateTime.Now - e.Key < TimeSpan.FromMinutes(5))
                {
                    v = e.Value;
                    return true;
                }
            }
            v = -1;
            return false;
        }

        public void CacheSet(string key, long v) { lock (cache) cache[key] = new KeyValuePair<DateTime, long>(DateTime.Now, v); }
        public void CacheRemove(string key) { lock (cache) cache.Remove(key); }

        public void Notify(string title, string text, ToolTipIcon icon, int targetTab = 0)
        {
            notifyTargetTab = targetTab;
            try { ni.ShowBalloonTip(6000, title, text, icon); } catch { }
        }

        void CheckLagWatchdog()
        {
            long tot, avail; double memPct;
            SystemHealth.ReadMemory(out tot, out avail, out memPct);
            double cpuPct = SystemHealth.ReadWatchdogCpuPct();
            lastMemPct = memPct;
            lastCpuPct = cpuPct;

            if (memPct >= Cfg.AutoCleanMemPct) highMemStreak++; else highMemStreak = 0;
            if (cpuPct >= 90.0) highCpuStreak++; else highCpuStreak = 0;

            if (Cfg.AutoCleanMemOnHigh && highMemStreak >= 2 && (DateTime.Now - lastAutoMemClean).TotalMinutes >= 10)
            {
                lastAutoMemClean = DateTime.Now;
                highMemStreak = 0;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var r = SystemHealth.CleanMemory(null);
                    invoker.BeginInvoke(new Action(() => Notify("高内存自动温和释放完成", r.Summary, ToolTipIcon.Info, 1)));
                });
                return;
            }

            if (Cfg.LagWatchdog && (highMemStreak >= 2 || highCpuStreak >= 2) && (DateTime.Now - lastLagNotify).TotalMinutes >= 15)
            {
                lastLagNotify = DateTime.Now;
                string msg = highMemStreak >= 2
                    ? string.Format("内存占用高达 {0:0}%（剩余 {1}），点击打开「性能与卡顿」一键安全释放", memPct, Util.Fmt(avail))
                    : string.Format("CPU 持续满载 {0:0}%，点击查看高占用进程并一键降优先级恢复流畅", cpuPct);
                Notify("检测到电脑高负载卡顿", msg, ToolTipIcon.Warning, 1);
            }
        }

        public void RefreshStats()
        {
            stats = DriveStat.Read(Cfg);
            History.Record(stats);

            int level = 0;
            DriveStat worst = null;
            foreach (var s in stats)
            {
                if (!s.Ready) continue;
                int l = s.Level(Cfg);
                if (worst == null || l > level || (l == level && s.Free < worst.Free)) { worst = s; level = l; }
            }

            string numText = worst == null ? "?" : Math.Min(999, (int)Math.Floor(worst.FreeGB)).ToString();
            double usedRatio = (worst != null && worst.Total > 0) ? worst.UsedRatio : 0.0;
            SetIconIfChanged(level, numText, usedRatio);

            var tip = "DiskGuard " + string.Join(" ", stats.Where(s => s.Ready).Select(s => s.Name + ":" + s.FreeGB.ToString("0") + "G").ToArray()) +
                      (lastMemPct > 0 ? string.Format(" | RAM {0:0}%", lastMemPct) : "");
            ni.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (worst != null && level > 0)
            {
                bool rose = level > lastLevel;
                bool repeat = level == 2 && DateTime.Now - lastCritNotify > TimeSpan.FromHours(2);
                if (rose || repeat)
                {
                    Notify(level == 2 ? "磁盘空间严重不足" : "磁盘空间偏低",
                           worst.Name + ": 盘仅剩 " + Util.Fmt(worst.Free) + "，点击打开 DiskGuard 安全清理",
                           level == 2 ? ToolTipIcon.Error : ToolTipIcon.Warning, 0);
                    if (level == 2) lastCritNotify = DateTime.Now;
                }
            }
            lastLevel = level;

            if (DateTime.Now - lastDropNotify > TimeSpan.FromHours(1))
            {
                foreach (var s in stats)
                {
                    long delta; TimeSpan span;
                    if (s.Ready && History.Change(s.Name, TimeSpan.FromHours(1), s.Free, out delta, out span)
                        && span.TotalMinutes >= 20 && delta < -(long)(Cfg.DropAlertGB * 1073741824))
                    {
                        Notify("磁盘空间下降很快（点击抓凶手）",
                               s.Name + ": 盘最近 " + (int)span.TotalMinutes + " 分钟减少了 " + Util.Fmt(-delta) + "，点击立即揪出大写入文件",
                               ToolTipIcon.Warning, 2);
                        lastDropNotify = DateTime.Now;
                        break;
                    }
                }
            }

            if (win != null && win.IsLoaded) win.RefreshDrives();
        }

        // 智能双触发自动清理（定时 daily/weekly 或低空间阈值触发；严格只碰 L1 安全白名单！）
        void CheckSmartAutoClean()
        {
            if (Cfg.AutoCleanSchedule == "off" && !Cfg.AutoCleanOnLowSpace) return;
            DateTime last = DateTime.MinValue;
            if (!string.IsNullOrEmpty(Cfg.LastAutoClean))
            {
                if (!DateTime.TryParseExact(Cfg.LastAutoClean, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out last))
                    DateTime.TryParseExact(Cfg.LastAutoClean, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last);
            }

            TimeSpan sinceLast = DateTime.Now - last;
            string reason = null;

            if (Cfg.AutoCleanOnLowSpace && sinceLast.TotalHours >= 6)
            {
                var lowDrive = stats.FirstOrDefault(s => s.Ready && s.FreeGB < Cfg.AutoCleanLowSpaceGB);
                if (lowDrive != null)
                    reason = lowDrive.Name + ": 盘低于 " + Cfg.AutoCleanLowSpaceGB.ToString("0.#") + " GB 低空间自动护航";
            }

            if (reason == null && Cfg.AutoCleanSchedule == "daily" && sinceLast.TotalHours >= 24)
                reason = "每日定时自动安全清理";
            else if (reason == null && Cfg.AutoCleanSchedule == "weekly" && sinceLast.TotalDays >= 7)
                reason = "每周定时自动安全清理";

            if (reason != null) RunAutoCleanAsync(reason, null);
        }

        public void RunAutoCleanAsync(string reason, Action<long, long> onDone)
        {
            if (Interlocked.CompareExchange(ref autoCleanRunning, 1, 0) != 0) return;
            var cfgSnap = Cfg;
            var disabled = new HashSet<string>(cfgSnap.AutoCleanDisabledIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                long total = 0, delta = 0;
                try
                {
                    var before = DriveStat.Read(cfgSnap);
                    var log = new StringBuilder();
                    log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + reason + "（仅 L1 白名单） ====");
                    var safeItems = CleanItemsBuilder.Build(cfgSnap, cfgSnap.AutoCleanTempAgeDays)
                        .Where(i => i.DefaultOn && !i.IsCaution && !disabled.Contains(i.Id))
                        .ToList();

                    foreach (var it in safeItems)
                    {
                        log.AppendLine("▶ " + it.Name);
                        try
                        {
                            long f = it.Clean(log);
                            total += f;
                            if (f > 0) log.AppendLine("  删除 " + Util.Fmt(f));
                        }
                        catch (Exception ex) { log.AppendLine("  跳过：" + ex.Message); }
                        CacheRemove(it.Id);
                    }

                    var after = DriveStat.Read(cfgSnap);
                    delta = DriveStat.TotalFree(after) - DriveStat.TotalFree(before);
                    log.AppendLine("合计：删除文件 " + Util.Fmt(total) + "，磁盘可用空间变化 " + Util.FmtDelta(delta));
                    log.AppendLine();
                    try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

                    cfgSnap.LastAutoClean = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    cfgSnap.Save();

                    invoker.BeginInvoke(new Action(() =>
                    {
                        RefreshStats();
                        if (total >= 5 * 1024 * 1024)
                            Notify("自动安全清理完成", reason + "：已释放 " + Util.Fmt(total) + "（可用空间 " + Util.FmtDelta(delta) + "）", ToolTipIcon.Info, 0);
                    }));
                }
                finally
                {
                    Interlocked.Exchange(ref autoCleanRunning, 0);
                    if (onDone != null) onDone(total, delta);
                }
            });
        }

        void SetIconIfChanged(int level, string text, double usedRatio)
        {
            int bucket = (int)Math.Round(usedRatio * 10);
            string key = level + "|" + text + "|" + bucket;
            if (key == lastIconKey) return;
            lastIconKey = key;

            var sz = SystemInformation.SmallIconSize;
            int w = Math.Max(16, sz.Width), h = Math.Max(16, sz.Height);
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    Color baseCol = level == 2 ? Color.FromArgb(239, 68, 68) : (level == 1 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(22, 163, 74));
                    using (var b = new SolidBrush(baseCol))
                        Theme.FillRounded(g, b, new RectangleF(0, 0, w - 1, h - 1), w * 0.26f);

                    float barY = h - 3.2f;
                    using (var tb = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        g.FillRectangle(tb, 2, barY, w - 4, 2f);
                    using (var fb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                        g.FillRectangle(fb, 2, barY, Math.Max(2f, (w - 4) * (float)Math.Min(1.0, usedRatio)), 2f);

                    float fs = h * (text.Length >= 3 ? 0.42f : 0.56f);
                    using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, f, Brushes.White, new RectangleF(0, -1, w, h - 1), sf);
                }
                IntPtr hIcon = bmp.GetHicon();
                ni.Icon = Icon.FromHandle(hIcon);
                if (lastHicon != IntPtr.Zero) DestroyIcon(lastHicon);
                lastHicon = hIcon;
            }
        }

        void AddItem(string text, EventHandler onClick, bool bold)
        {
            var it = new ToolStripMenuItem(text);
            it.Click += onClick;
            if (bold) it.Font = Theme.FontBodyBold;
            menu.Items.Add(it);
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            foreach (var s in stats)
            {
                string chg = History.ChangeText(s);
                string t = s.Ready && s.Total > 0
                    ? string.Format("{0}:  可用 {1} / {2}（已用 {3:0}%）{4}", s.Name, Util.FmtCompact(s.Free), Util.FmtCompact(s.Total), s.UsedRatio * 100.0, string.IsNullOrEmpty(chg) ? "" : ("  " + chg))
                    : s.Name + ":  不可用";
                menu.Items.Add(new ToolStripMenuItem(t) { Enabled = false });
            }
            long mtot, mavail; double mpct;
            SystemHealth.ReadMemory(out mtot, out mavail, out mpct);
            menu.Items.Add(new ToolStripMenuItem(string.Format("内存:  已用 {0:0}%（剩余 {1}）  ·  CPU {2:0}%", mpct, Util.FmtCompact(mavail), lastCpuPct)) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            AddItem("智能体检与一键修复…", (s, e) => OpenMain(0), true);
            AddItem("深度瘦身（缓存 / User 专清 / 去重）…", (s, e) => OpenMain(2), false);
            AddItem("软件卸载、缓存改位与大文件搬家…", (s, e) => OpenMain(3), false);
            AddItem("加速与修复（性能 / 网络 / 环境变量）…", (s, e) => OpenMain(1), false);
            AddItem("一键安全释放内存", (s, e) => ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = SystemHealth.CleanMemory(null);
                invoker.BeginInvoke(new Action(() => Notify(r.Title, r.Summary, ToolTipIcon.Info, 1)));
            }), false);
            AddItem("设置、空间趋势与自动更新…", (s, e) => OpenMain(4), false);
            menu.Items.Add(new ToolStripSeparator());
            AddItem("立即刷新状态", (s, e) => RefreshStats(), false);
            AddItem("打开清理日志", (s, e) => Util.OpenPath(File.Exists(Util.LogFile) ? Util.LogFile : Util.LogDir), false);

            menu.Items.Add(new ToolStripSeparator());
            int pin = TrayPin.State();
            var pinItem = new ToolStripMenuItem("图标常驻任务栏") { Checked = pin == 1 };
            pinItem.Click += (s, e) =>
            {
                int st = TrayPin.State();
                if (st >= 0) { try { TrayPin.Set(st != 1); } catch { } }
            };
            menu.Items.Add(pinItem);

            var auto = new ToolStripMenuItem("开机自启") { Checked = AutoStart.Enabled };
            auto.Click += (s, e) => { try { AutoStart.Set(!AutoStart.Enabled); } catch { } };
            menu.Items.Add(auto);

            menu.Items.Add(new ToolStripSeparator());
            AddItem("退出 DiskGuard", (s, e) => Quit(), false);
        }

        public void OpenMain(int page)
        {
            if (win == null || !win.IsLoaded)
            {
                win = new MainWindow(this, page);
                System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(win);
                win.Closed += (s, e) =>
                {
                    win = null;
                    Win32Native.TrimMemory();
                };
                win.Show();
            }
            else
            {
                win.SwitchPage(page);
            }
            if (win.WindowState == System.Windows.WindowState.Minimized) win.WindowState = System.Windows.WindowState.Normal;
            win.Topmost = true;
            win.Topmost = false;
            win.Activate();
        }

        void Quit()
        {
            timer.Stop();
            lagTimer.Stop();
            if (win != null && win.IsLoaded) { try { win.Close(); } catch { } }
            ni.Visible = false;
            ni.Dispose();
            if (lastHicon != IntPtr.Zero) DestroyIcon(lastHicon);
            try { showEvt.Dispose(); } catch { }
            ExitThread();
        }
    }
}
