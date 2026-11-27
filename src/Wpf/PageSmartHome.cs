// DiskGuard v9.0 - Apple macOS / iOS 风格「智能体检 & 一键修复」旗舰首页
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DiskGuard
{
    public class SmartCheckIssue
    {
        public string Id;
        public string Title;
        public string Desc;
        public string Metric;
        public string Severity; // ok | info | warn | crit
        public bool AutoFixable;
        public bool Checked;
        public string ActionLabel;
        public Action JumpAction;
    }

    public partial class MainWindow
    {
        bool checkupRunning = false, checkupDone = false;
        int healthScore = 100;
        List<SmartCheckIssue> checkIssues = new List<SmartCheckIssue>();
        Border homeRingHost, homeProgWrap;
        TextBlock homeTitleTxt, homeSubTxt, homeProgTxt;
        ProgressBar homeProgBar;
        Button btnRunCheckup, btnOneClickFix;
        UniformGrid homeDrivesGrid;
        StackPanel homeIssuesPanel;

        UIElement BuildSmartHomePage()
        {
            var sp = new StackPanel();

            // ===== Apple Hero 健康评分卡 =====
            var heroSp = new StackPanel();
            var hTop = new Grid();
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            homeRingHost = new Border { Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(homeRingHost, 0);

            var copySp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            homeTitleTxt = WpfUI.Txt("正在为您的电脑做全维度健康体检…", 20, true, WpfUI.Pal.Text);
            homeSubTxt = WpfUI.TxtWrap("一次性检查全盘缓存、内存负载、网络代理、环境变量 PATH 与 Docker/C 盘空间，发现的安全问题支持一键修复", 12.6, WpfUI.Pal.Text2);
            homeSubTxt.Margin = new Thickness(0, 5, 16, 0);
            copySp.Children.Add(homeTitleTxt); copySp.Children.Add(homeSubTxt);
            Grid.SetColumn(copySp, 1);

            var actSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnRunCheckup = WpfUI.Btn("重新体检", "secondary", StartSmartCheckupAsync);
            btnRunCheckup.Margin = new Thickness(0, 0, 8, 0);
            btnOneClickFix = WpfUI.Btn("一键智能修复", "primary", RunOneClickSmartFixAsync);
            actSp.Children.Add(btnRunCheckup); actSp.Children.Add(btnOneClickFix);
            Grid.SetColumn(actSp, 2);

            hTop.Children.Add(homeRingHost); hTop.Children.Add(copySp); hTop.Children.Add(actSp);
            heroSp.Children.Add(hTop);

            homeProgBar = new ProgressBar { Height = 6, Maximum = 100, Foreground = WpfUI.Pal.Accent, Background = WpfUI.Pal.Track, BorderThickness = new Thickness(0) };
            homeProgTxt = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text2); homeProgTxt.Margin = new Thickness(0, 5, 0, 0);
            var hpSp = new StackPanel(); hpSp.Children.Add(homeProgBar); hpSp.Children.Add(homeProgTxt);
            homeProgWrap = new Border { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed, Child = hpSp };
            heroSp.Children.Add(homeProgWrap);

            var hero = WpfUI.HeroCard(heroSp);
            hero.Margin = new Thickness(0, 0, 0, 16);
            sp.Children.Add(hero);

            // ===== 全部分区健康卡片 =====
            homeDrivesGrid = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(homeDrivesGrid);

            // ===== 体检发现与一键修复清单 =====
            var listSp = new StackPanel();
            var listHead = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            listHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            listHead.Children.Add(WpfUI.Txt("全维度体检报告（勾选的安全项可一键自动修复，高影响项提供直达入口）", 13.2, true, WpfUI.Pal.Text));
            var badge = WpfUI.PillBadge("严守零误删红线", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok);
            Grid.SetColumn(badge, 1); listHead.Children.Add(badge);
            listSp.Children.Add(listHead);
            listSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });

            homeIssuesPanel = new StackPanel();
            listSp.Children.Add(homeIssuesPanel);
            sp.Children.Add(WpfUI.Card(listSp, new Thickness(0), 16));

            RenderHomeDrives();
            RenderSmartHomeUI();
            return sp;
        }

        void RenderHomeDrives()
        {
            if (homeDrivesGrid == null) return;
            homeDrivesGrid.Children.Clear();
            var list = App.Stats.Where(d => d.Ready).ToList();
            homeDrivesGrid.Columns = Math.Max(1, Math.Min(4, list.Count));
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                int lv = d.Level(App.Cfg);
                var col = WpfUI.LevelBrush(lv);
                var sp = new StackPanel();
                var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tl = new StackPanel { Orientation = Orientation.Horizontal };
                tl.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = col, Margin = new Thickness(0, 0, 7, 0) });
                var dn = WpfUI.Txt(d.Name + " 盘", 13.8, true, WpfUI.Pal.Text); dn.Margin = new Thickness(0, 0, 7, 0);
                tl.Children.Add(dn);
                tl.Children.Add(WpfUI.PillBadge(d.HealthLabel(App.Cfg), WpfUI.LevelSoftBrush(lv), col));
                var fr = WpfUI.Txt("剩 " + Util.FmtCompact(d.Free), 13.8, true, col, true);
                Grid.SetColumn(fr, 1);
                head.Children.Add(tl); head.Children.Add(fr);
                sp.Children.Add(head);

                var bar = WpfUI.ProgressTrack(d.UsedRatio * 100.0, col, 6.5); bar.Margin = new Thickness(0, 0, 0, 7);
                sp.Children.Add(bar);

                var foot = new Grid();
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                foot.Children.Add(WpfUI.Txt(string.Format("已用 {0:0}% · 总计 {1}", d.UsedRatio * 100.0, Util.FmtCompact(d.Total)), 11.2, false, WpfUI.Pal.Text2, true));
                string drv = d.Name;
                var acts = new StackPanel { Orientation = Orientation.Horizontal };
                acts.Children.Add(WpfUI.Btn("大文件", "link", () => { cleanSeg = "space"; GoTab("clean"); RunSpaceScan("files", drv + ":\\"); }, true));
                acts.Children.Add(WpfUI.Btn("目录", "link", () => { cleanSeg = "space"; GoTab("clean"); RunSpaceScan("dirs", drv + ":\\"); }, true));
                Grid.SetColumn(acts, 1);
                foot.Children.Add(acts);
                sp.Children.Add(foot);

                var card = WpfUI.Card(sp, new Thickness(15, 13, 15, 13), 15);
                card.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == list.Count - 1 ? 0 : 6, 0);
                homeDrivesGrid.Children.Add(card);
            }
        }

        void StartSmartCheckupAsync()
        {
            if (checkupRunning) return;
            checkupRunning = true;
            OnUI(() =>
            {
                if (homeProgWrap != null) { homeProgWrap.Visibility = Visibility.Visible; homeProgBar.Value = 12; homeProgTxt.Text = "正在体检磁盘安全缓存、内存负载、网络代理与环境变量…"; }
                RenderSmartHomeUI();
            });
            RunBg(() =>
            {
                ComputeSmartCheckup((txt, pct) => OnUI(() => { if (homeProgBar != null) { homeProgBar.Value = pct; homeProgTxt.Text = txt; } }));
                checkupRunning = false;
                checkupDone = true;
                OnUI(() =>
                {
                    if (homeProgWrap != null) homeProgWrap.Visibility = Visibility.Collapsed;
                    RenderSmartHomeUI();
                    RefreshOverviewCleanUI();
                    Win32Native.TrimMemory();
                });
            });
        }

        void RunSmartCheckupSyncForShot()
        {
            ComputeSmartCheckup(null);
            checkupDone = true;
        }

        void ComputeSmartCheckup(Action<string, int> onProg)
        {
            int score = 100;
            var found = new List<SmartCheckIssue>();

            // 1. L1 安全缓存
            if (onProg != null) onProg("第 1/5 项：并行扫描全盘 L1 安全缓存…", 20);
            cleanItems = CleanItemsBuilder.Build(App.Cfg);
            Parallel.ForEach(cleanItems, it =>
            {
                long v;
                if (App.CacheGet(it.Id, out v)) it.LastSize = v;
                else { try { it.LastSize = it.Scan(CancellationToken.None); } catch { it.LastSize = 0; } App.CacheSet(it.Id, it.LastSize); }
            });
            if (cleanChecked.Count == 0) foreach (var it in cleanItems.Where(x => x.DefaultOn)) cleanChecked.Add(it.Id);
            long l1Bytes = cleanItems.Where(x => x.DefaultOn && x.LastSize > 0).Sum(x => x.LastSize);
            if (l1Bytes >= 50L * 1048576) score -= Math.Min(18, (int)(l1Bytes / (250L * 1048576)) + 5);
            found.Add(new SmartCheckIssue
            {
                Id = "fix_l1_cache",
                Title = "系统与应用安全缓存（L1 零风险）",
                Desc = l1Bytes >= 5L * 1048576 ? "发现可立即清理的临时文件、浏览器/开发/GPU 缓存，绝不动文档与登录态" : "L1 临时缓存非常干净，无需额外清理",
                Metric = Util.Fmt(l1Bytes),
                Severity = l1Bytes >= 500L * 1048576 ? "warn" : (l1Bytes >= 5L * 1048576 ? "info" : "ok"),
                AutoFixable = l1Bytes >= 1048576,
                Checked = l1Bytes >= 1048576,
                ActionLabel = "查看明细",
                JumpAction = () => { cleanSeg = "cache"; GoTab("clean"); }
            });

            // 2. 内存与 CPU 实时健康
            if (onProg != null) onProg("第 2/5 项：检测内存与 CPU 实时负载…", 42);
            long mTot, mAvail; double mPct;
            SystemHealth.ReadMemory(out mTot, out mAvail, out mPct);
            if (mPct >= 85) score -= 14; else if (mPct >= 72) score -= 6;
            found.Add(new SmartCheckIssue
            {
                Id = "fix_mem",
                Title = "物理内存与后台占用健康度",
                Desc = mPct >= 70 ? string.Format("当前内存占用 {0:0}%（剩余 {1}），可安全修剪闲置物理页加速系统，绝不关闭软件", mPct, Util.Fmt(mAvail)) : string.Format("内存充裕（已用 {0:0}%，剩余 {1}）", mPct, Util.Fmt(mAvail)),
                Metric = string.Format("{0:0}%", mPct),
                Severity = mPct >= 85 ? "crit" : (mPct >= 70 ? "warn" : "ok"),
                AutoFixable = mPct >= 65,
                Checked = mPct >= 70,
                ActionLabel = "进程监控",
                JumpAction = () => { sysSeg = "perf"; GoTab("sys"); }
            });

            // 3. 网络代理与 DNS 缓存
            if (onProg != null) onProg("第 3/5 项：检测系统代理残留与网络状态…", 62);
            bool proxyOn = false; string proxySrv = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    if (k != null) { object en = k.GetValue("ProxyEnable"); proxyOn = en != null && en.ToString() == "1"; proxySrv = (k.GetValue("ProxyServer") as string ?? "").Trim(); }
                }
            }
            catch { }
            if (proxyOn) score -= 6;
            found.Add(new SmartCheckIssue
            {
                Id = "fix_net_dns",
                Title = "网络 DNS 缓存与系统代理检测",
                Desc = proxyOn ? ("检测到系统代理已开启（" + proxySrv + "）；如已关闭代理软件却无法上网，可去网络页一键清除") : "无异常代理残留；一键修复可自动刷新 DNS 缓存保持解析畅通",
                Metric = proxyOn ? "代理开启" : "正常",
                Severity = proxyOn ? "warn" : "ok",
                AutoFixable = true,
                Checked = true,
                ActionLabel = "DNS 测速",
                JumpAction = () => { sysSeg = "net"; GoTab("sys"); }
            });

            // 4. 环境变量 PATH 健康度
            if (onProg != null) onProg("第 4/5 项：体检用户与系统 PATH 环境变量…", 80);
            var env = NetworkEnvManager.InspectEnvVars();
            envData = env; envLoaded = true;
            int userBad = env.UserMissingCount + env.UserDupCount;
            int sysBad = env.MachineMissingCount + env.MachineDupCount;
            if (userBad + sysBad > 0) score -= Math.Min(12, (userBad + sysBad));
            found.Add(new SmartCheckIssue
            {
                Id = "fix_user_path",
                Title = "环境变量 PATH 失效与重复路径体检",
                Desc = (userBad + sysBad) > 0
                    ? string.Format("检出用户 PATH 异常 {0} 项、系统 PATH 失效 {1} 项 / 重复 {2} 项（整理前自动快照备份，随时回滚）", userBad, env.MachineMissingCount, env.MachineDupCount)
                    : "用户与系统 PATH 均整洁无冗余",
                Metric = (userBad + sysBad) > 0 ? ((userBad + sysBad) + " 项异常") : "整洁",
                Severity = (userBad + sysBad) >= 5 ? "warn" : ((userBad + sysBad) > 0 ? "info" : "ok"),
                AutoFixable = userBad > 0,
                Checked = userBad > 0,
                ActionLabel = "一键整理",
                JumpAction = () => { sysSeg = "env"; GoTab("sys"); }
            });

            // 5. 低空间分区与 Docker VHDX 潜能
            if (onProg != null) onProg("第 5/5 项：检查分区余量与 Docker / User 空间潜能…", 95);
            var lowDrv = App.Stats.Where(d => d.Ready && d.Level(App.Cfg) > 0).ToList();
            if (lowDrv.Count > 0) score -= lowDrv.Any(d => d.Level(App.Cfg) == 2) ? 16 : 8;
            found.Add(new SmartCheckIssue
            {
                Id = "check_space_pot",
                Title = "分区剩余空间与大文件搬家建议",
                Desc = lowDrv.Count > 0
                    ? (string.Join("、", lowDrv.Select(d => d.Name + "盘仅剩 " + Util.FmtCompact(d.Free)).ToArray()) + "；建议使用「User 专清」「缓存改位」或「Docker VHDX 瘦身」释放数十 GB")
                    : "全部硬盘分区可用空间充裕，状态极佳",
                Metric = lowDrv.Count > 0 ? (lowDrv.Count + " 个盘偏紧") : "充裕",
                Severity = lowDrv.Any(d => d.Level(App.Cfg) == 2) ? "crit" : (lowDrv.Count > 0 ? "warn" : "ok"),
                AutoFixable = false,
                Checked = false,
                ActionLabel = "去瘦身/搬家",
                JumpAction = () => { cleanSeg = "user"; GoTab("clean"); }
            });

            healthScore = Math.Max(42, Math.Min(100, score));
            checkIssues = found;
        }

        void RenderSmartHomeUI()
        {
            if (homeRingHost == null) return;
            var col = healthScore >= 85 ? WpfUI.Pal.Ok : (healthScore >= 68 ? WpfUI.Pal.Warn : WpfUI.Pal.Crit);
            homeRingHost.Child = WpfUI.RingGauge(98, 8.5, checkupDone ? healthScore : 25, col, checkupDone ? healthScore.ToString() : "…", checkupDone ? "健康分" : "体检中");

            int fixCnt = checkIssues.Count(i => i.AutoFixable && i.Checked);
            int warnCnt = checkIssues.Count(i => i.Severity == "warn" || i.Severity == "crit");
            btnRunCheckup.IsEnabled = !checkupRunning;
            btnOneClickFix.IsEnabled = !checkupRunning && fixCnt > 0;
            btnOneClickFix.Content = fixCnt > 0 ? ("一键智能修复 (" + fixCnt + " 项)") : "当前无需修复";

            if (checkupRunning) homeTitleTxt.Text = "正在全面体检您的电脑…";
            else if (warnCnt > 0) homeTitleTxt.Text = string.Format("体检得分 {0} 分 · 发现 {1} 项可优化问题", healthScore, warnCnt);
            else homeTitleTxt.Text = string.Format("电脑状态优秀（{0} 分）· 各项核心指标健康", healthScore);

            homeIssuesPanel.Children.Clear();
            if (checkIssues.Count == 0)
            {
                homeIssuesPanel.Children.Add(new Border { Padding = new Thickness(20, 28, 20, 28), Child = WpfUI.Txt("正在汇总体检报告…", 13, false, WpfUI.Pal.Text2) });
                return;
            }
            foreach (var item in checkIssues)
            {
                var it = item;
                UIElement left;
                if (it.AutoFixable)
                {
                    var cb = new CheckBox { IsChecked = it.Checked, VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { it.Checked = cb.IsChecked == true; RenderSmartHomeUI(); };
                    left = cb;
                }
                else
                {
                    var dotCol = it.Severity == "ok" ? WpfUI.Pal.Ok : (it.Severity == "crit" ? WpfUI.Pal.Crit : WpfUI.Pal.Warn);
                    left = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = dotCol, Margin = new Thickness(4, 0, 4, 0) };
                }

                var bBg = it.Severity == "ok" ? WpfUI.Pal.OkSoft : (it.Severity == "crit" ? WpfUI.Pal.CritSoft : (it.Severity == "warn" ? WpfUI.Pal.WarnSoft : WpfUI.Pal.AccentSoft));
                var bFg = it.Severity == "ok" ? WpfUI.Pal.Ok : (it.Severity == "crit" ? WpfUI.Pal.Crit : (it.Severity == "warn" ? WpfUI.Pal.Warn : WpfUI.Pal.Accent));
                var tags = new List<UIElement> { WpfUI.PillBadge(it.Metric, bBg, bFg) };
                if (it.AutoFixable) tags.Add(WpfUI.Badge("支持一键修", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));
                var acts = it.JumpAction != null ? new UIElement[] { WpfUI.Btn(it.ActionLabel + " ›", "secondary", it.JumpAction, true) } : null;
                homeIssuesPanel.Children.Add(WpfUI.MakeRow(left, it.Title, it.Desc, "", tags.ToArray(), acts));
            }
        }

        void RunOneClickSmartFixAsync()
        {
            var targets = checkIssues.Where(i => i.AutoFixable && i.Checked).ToList();
            if (targets.Count == 0) return;

            checkupRunning = true;
            homeProgWrap.Visibility = Visibility.Visible;
            homeProgBar.Value = 10;
            RenderSmartHomeUI();

            RunBg(() =>
            {
                var summary = new List<string>();
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    OnUI(() => { homeProgBar.Value = (i + 1) * 90.0 / targets.Count; homeProgTxt.Text = "正在修复：" + t.Title + "…"; });
                    if (t.Id == "fix_l1_cache")
                    {
                        long freed = 0; var log = new StringBuilder();
                        foreach (var ci in cleanItems.Where(x => x.DefaultOn && !x.IsCaution))
                        {
                            try { freed += ci.Clean(log); ci.LastSize = 0; App.CacheRemove(ci.Id); } catch { }
                        }
                        summary.Add("清理安全缓存 " + Util.Fmt(freed));
                    }
                    else if (t.Id == "fix_mem")
                    {
                        var r = SystemHealth.CleanMemory(null);
                        summary.Add(r.Summary);
                    }
                    else if (t.Id == "fix_net_dns")
                    {
                        SystemHealth.RunBugFix("flush-dns");
                        summary.Add("已刷新系统 DNS 缓存");
                    }
                    else if (t.Id == "fix_user_path")
                    {
                        var r = NetworkEnvManager.CleanPathScope("User", true, true);
                        summary.Add(r.Summary);
                    }
                }
                checkupRunning = false;
                OnUI(() =>
                {
                    App.RefreshStats(); RefreshDrives();
                    ShowBanner(true, "一键智能修复完成：" + string.Join("；", summary.ToArray()));
                    StartSmartCheckupAsync();
                });
            });
        }
    }
}
