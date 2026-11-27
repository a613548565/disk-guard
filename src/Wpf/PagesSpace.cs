// DiskGuard v9.0 - 原生 WPF 空间瘦身 3 页（概览与一键清 / User 文件夹专清 / 空间透视+重复大文件+微信QQ大附件）
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
    public partial class MainWindow
    {
        // ==================== PAGE 1: 概览与一键清 ====================
        UniformGrid drivesGrid;
        Border cleanRingHost;
        TextBlock heroTitleTxt, cleanMetaTxt, cleanProgTxt;
        Border cleanProgPanel;
        ProgressBar cleanProgBar;
        Button btnHeroClean, btnRescan, btnFullAudit;
        UniformGrid auditGrid;
        StackPanel cleanGroupsPanel, advGroupPanel;
        bool advExpanded = false;
        bool cleanBusy = false;
        List<CleanItem> cleanItems = new List<CleanItem>();
        HashSet<string> cleanChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> cleanGroupExpanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UIElement BuildCleanPage()
        {
            var sp = new StackPanel();

            drivesGrid = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(drivesGrid);

            // Hero Card
            var heroStack = new StackPanel();
            var heroTop = new Grid();
            heroTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heroTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heroTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            cleanRingHost = new Border { Margin = new Thickness(0, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cleanRingHost, 0);

            var heroCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heroTitleTxt = WpfUI.Txt("正在快速体检您的磁盘…", 17.5, true, WpfUI.Pal.Text);
            heroCopy.Children.Add(heroTitleTxt);
            var hSub = WpfUI.TxtWrap("L1 纯缓存放心一键清；L2 建议迁移/审清；L3 重要项强制反复确认；L4 系统与个人文档永久锁定", 12, WpfUI.Pal.Text2);
            hSub.Margin = new Thickness(0, 4, 12, 0);
            heroCopy.Children.Add(hSub);
            Grid.SetColumn(heroCopy, 1);

            var heroActs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnFullAudit = WpfUI.Btn("全盘四级体检", "secondary", StartFullAuditAsync);
            btnFullAudit.Margin = new Thickness(0, 0, 7, 0);
            btnRescan = WpfUI.Btn("快扫缓存", "secondary", () => StartCleanScanAsync(true));
            btnRescan.Margin = new Thickness(0, 0, 7, 0);
            btnHeroClean = WpfUI.Btn("一键清理", "primary", AskExecClean);
            heroActs.Children.Add(btnFullAudit);
            heroActs.Children.Add(btnRescan);
            heroActs.Children.Add(btnHeroClean);
            Grid.SetColumn(heroActs, 2);

            heroTop.Children.Add(cleanRingHost);
            heroTop.Children.Add(heroCopy);
            heroTop.Children.Add(heroActs);
            heroStack.Children.Add(heroTop);

            cleanProgBar = new ProgressBar { Height = 6, Maximum = 100, Foreground = WpfUI.Pal.Accent, Background = WpfUI.Pal.Track, BorderThickness = new Thickness(0) };
            cleanProgTxt = WpfUI.Txt("扫描中…", 11.8, false, WpfUI.Pal.Text2);
            cleanProgTxt.Margin = new Thickness(0, 5, 0, 0);
            var progSp = new StackPanel();
            progSp.Children.Add(cleanProgBar);
            progSp.Children.Add(cleanProgTxt);
            cleanProgPanel = new Border { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, Child = progSp };
            heroStack.Children.Add(cleanProgPanel);

            auditGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            heroStack.Children.Add(auditGrid);

            var heroCard = WpfUI.HeroCard(heroStack);
            heroCard.Margin = new Thickness(0, 0, 0, 14);
            sp.Children.Add(heroCard);

            var secHead = new Grid { Margin = new Thickness(2, 0, 2, 8) };
            secHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            secHead.Children.Add(WpfUI.Txt("L1 放心安全清理清单（绝不动登录态、聊天记录与项目源码）", 12.2, true, WpfUI.Pal.Text2));
            cleanMetaTxt = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text3, true);
            Grid.SetColumn(cleanMetaTxt, 1);
            secHead.Children.Add(cleanMetaTxt);
            sp.Children.Add(secHead);

            cleanGroupsPanel = new StackPanel();
            sp.Children.Add(cleanGroupsPanel);

            var advBtn = new Border
            {
                Background = WpfUI.Pal.Surface, BorderBrush = WpfUI.Pal.BorderStrong, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 8, 0, 8), Cursor = System.Windows.Input.Cursors.Hand
            };
            var advRow = new Grid();
            advRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            advRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var advLeft = new StackPanel { Orientation = Orientation.Horizontal };
            var l3Tag = WpfUI.Badge("L3 重要项", WpfUI.Pal.CritSoft, WpfUI.Pal.Crit);
            l3Tag.Margin = new Thickness(0, 0, 8, 0);
            advLeft.Children.Add(l3Tag);
            advLeft.Children.Add(WpfUI.Txt("进阶按需清理（回收站、Docker 缓存、Go/NuGet 仓库，清理需多重确认）", 12.4, false, WpfUI.Pal.Text2));
            var advChv = WpfUI.Txt("展开 ⌄", 12, false, WpfUI.Pal.Text2);
            Grid.SetColumn(advChv, 1);
            advRow.Children.Add(advLeft);
            advRow.Children.Add(advChv);
            advBtn.Child = advRow;
            advBtn.MouseLeftButtonUp += (s, e) =>
            {
                advExpanded = !advExpanded;
                advChv.Text = advExpanded ? "收起 ⌃" : "展开 ⌄";
                advGroupPanel.Visibility = advExpanded ? Visibility.Visible : Visibility.Collapsed;
                if (advExpanded) { cleanGroupExpanded.Add("advanced"); RefreshOverviewCleanUI(); }
            };
            sp.Children.Add(advBtn);

            advGroupPanel = new StackPanel { Visibility = Visibility.Collapsed };
            sp.Children.Add(advGroupPanel);

            RenderOverviewDrives();
            RefreshOverviewCleanUI();
            return sp;
        }

        void RenderOverviewDrives()
        {
            if (drivesGrid == null) return;
            drivesGrid.Children.Clear();
            var list = App.Stats.Where(d => d.Ready).ToList();
            drivesGrid.Columns = Math.Max(1, Math.Min(4, list.Count));

            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                int lv = d.Level(App.Cfg);
                var col = WpfUI.LevelBrush(lv);
                var sp = new StackPanel();

                var head = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                tl.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = col, Margin = new Thickness(0, 0, 6, 0) });
                var dn = WpfUI.Txt(d.Name + " 盘", 13.2, true, WpfUI.Pal.Text);
                dn.Margin = new Thickness(0, 0, 6, 0);
                tl.Children.Add(dn);
                tl.Children.Add(WpfUI.PillBadge(d.HealthLabel(App.Cfg), WpfUI.LevelSoftBrush(lv), col));
                Grid.SetColumn(tl, 0);

                var fr = WpfUI.Txt("剩 " + Util.FmtCompact(d.Free), 13.2, true, col, true);
                Grid.SetColumn(fr, 1);
                head.Children.Add(tl); head.Children.Add(fr);
                sp.Children.Add(head);

                var bar = WpfUI.ProgressTrack(d.UsedRatio * 100.0, col, 6.5);
                bar.Margin = new Thickness(0, 0, 0, 7);
                sp.Children.Add(bar);

                var foot = new Grid();
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                string chg = History.ChangeText(d);
                var ftLeft = WpfUI.Txt(string.Format("{0:0}% · {1} {2}", d.UsedRatio * 100.0, Util.FmtCompact(d.Total), chg), 11, false, WpfUI.Pal.Text2, true);
                Grid.SetColumn(ftLeft, 0);
                var ftActs = new StackPanel { Orientation = Orientation.Horizontal };
                string drvName = d.Name;
                ftActs.Children.Add(WpfUI.Btn("大文件", "link", () => { GoTab("space"); RunSpaceScan("files", drvName + ":\\"); }, true));
                ftActs.Children.Add(WpfUI.Btn("目录", "link", () => { GoTab("space"); RunSpaceScan("dirs", drvName + ":\\"); }, true));
                Grid.SetColumn(ftActs, 1);
                foot.Children.Add(ftLeft); foot.Children.Add(ftActs);
                sp.Children.Add(foot);

                var card = WpfUI.Card(sp, new Thickness(13, 11, 13, 11), 13);
                card.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == list.Count - 1 ? 0 : 5, 0);
                drivesGrid.Children.Add(card);
            }
        }

        static string GroupKey(CleanItem it)
        {
            if (!it.DefaultOn) return "advanced";
            if (it.Category == CleanCategory.BrowserApp) return "apps";
            if (it.Category == CleanCategory.DevTools) return "dev";
            return "system";
        }

        void StartCleanScanAsync(bool force)
        {
            if (cleanBusy) return;
            cleanItems = CleanItemsBuilder.Build(App.Cfg);
            bool allCached = !force;
            foreach (var it in cleanItems)
            {
                long v;
                if (!force && App.CacheGet(it.Id, out v)) it.LastSize = v;
                else allCached = false;
            }
            if (cleanChecked.Count == 0)
            {
                foreach (var it in cleanItems.Where(x => x.DefaultOn)) cleanChecked.Add(it.Id);
            }
            if (allCached)
            {
                OnUI(() => { if (cleanMetaTxt != null) cleanMetaTxt.Text = "5 分钟内缓存结果"; RefreshOverviewCleanUI(); });
                return;
            }

            cleanBusy = true;
            OnUI(() =>
            {
                if (cleanProgPanel != null) cleanProgPanel.Visibility = Visibility.Visible;
                if (cleanProgBar != null) cleanProgBar.Value = 8;
                if (cleanProgTxt != null) cleanProgTxt.Text = "正在 Win32 并行快扫安全缓存…";
                RefreshOverviewCleanUI();
            });

            RunBg(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int done = 0, total = cleanItems.Count;
                Parallel.ForEach(cleanItems, it =>
                {
                    long sz;
                    try { sz = it.Scan(CancellationToken.None); } catch { sz = -1; }
                    it.LastSize = sz;
                    App.CacheSet(it.Id, sz);
                    int n = Interlocked.Increment(ref done);
                    OnUI(() =>
                    {
                        if (cleanProgBar != null) cleanProgBar.Value = n * 100.0 / Math.Max(1, total);
                        if (cleanProgTxt != null) cleanProgTxt.Text = string.Format("已完成 {0}/{1} 项快扫…", n, total);
                    });
                });
                sw.Stop();
                cleanBusy = false;
                OnUI(() =>
                {
                    if (cleanProgPanel != null) cleanProgPanel.Visibility = Visibility.Collapsed;
                    if (cleanMetaTxt != null) cleanMetaTxt.Text = string.Format("快扫耗时 {0:0.0} 秒", sw.Elapsed.TotalSeconds);
                    RefreshOverviewCleanUI();
                });
            });
        }

        void StartFullAuditAsync()
        {
            if (cleanBusy) return;
            cleanBusy = true;
            cleanProgPanel.Visibility = Visibility.Visible;
            cleanProgBar.Value = 10;
            cleanProgTxt.Text = "正在执行全盘四级深度体检…";
            RefreshOverviewCleanUI();

            RunBg(() =>
            {
                var snap = CleanItemsBuilder.Build(App.Cfg);
                var r = SpaceAnalyzer.RunFullAudit(App.Cfg, snap, CancellationToken.None, (msg, pct) =>
                {
                    OnUI(() => { cleanProgBar.Value = pct; cleanProgTxt.Text = msg; });
                });
                cleanItems = snap;
                foreach (var it in snap) App.CacheSet(it.Id, it.LastSize);
                cleanBusy = false;
                OnUI(() =>
                {
                    cleanProgPanel.Visibility = Visibility.Collapsed;
                    cleanMetaTxt.Text = string.Format("全盘四级体检完成 ({0:0.0}s)", r.ElapsedMs / 1000.0);
                    auditGrid.Children.Clear();
                    auditGrid.Children.Add(MakeAuditTile("L1 放心一键清", Util.Fmt(r.L1Bytes), "纯临时缓存", WpfUI.Pal.Ok, () => AskExecClean()));
                    auditGrid.Children.Add(MakeAuditTile("L2 闲置项目依赖", Util.Fmt(r.L2ArtifactBytes), r.ArtifactCount + " 个可重装 ›", WpfUI.Pal.Warn, () => { GoTab("space"); RunSpaceScan("artifacts", Directory.Exists(@"E:\code") ? @"E:\code" : Util.UserProfile); }));
                    auditGrid.Children.Add(MakeAuditTile("L3 Docker 潜能", Util.Fmt(r.L3DockerBytes), "镜像+VHDX ›", WpfUI.Pal.Crit, () => GoTab("docker")));
                    auditGrid.Children.Add(MakeAuditTile("L2 全盘大文件", Util.Fmt(r.L2LargeFileBytes), r.LargeFileCount + " 个大块头 ›", WpfUI.Pal.Accent, () => { GoTab("space"); RunSpaceScan("files", "ALL"); }));
                    auditGrid.Visibility = Visibility.Visible;
                    RefreshOverviewCleanUI();
                });
            });
        }

        Border MakeAuditTile(string t, string v, string sub, Brush col, Action onClick)
        {
            var c = WpfUI.KpiCard(t, v, sub, col);
            c.Margin = new Thickness(0, 0, 8, 0);
            c.Cursor = System.Windows.Input.Cursors.Hand;
            c.MouseLeftButtonUp += (s, e) => { if (onClick != null) onClick(); };
            return c;
        }

        public void RefreshOverviewCleanUI()
        {
            if (cleanGroupsPanel == null) return;
            long selBytes = cleanItems.Where(i => cleanChecked.Contains(i.Id) && i.LastSize > 0).Sum(i => i.LastSize);
            long safeBytes = cleanItems.Where(i => i.DefaultOn && i.LastSize > 0).Sum(i => i.LastSize);

            if (navReclaimBadge != null)
            {
                navReclaimBadge.Visibility = safeBytes >= 1048576 ? Visibility.Visible : Visibility.Collapsed;
                navReclaimBadgeTxt.Text = Util.FmtCompact(safeBytes);
            }

            double pct = Math.Min(100, Math.Max(6, selBytes * 100.0 / (10.0 * 1073741824)));
            var ringCol = selBytes >= 5L * 1073741824 ? WpfUI.Pal.Warn : (selBytes >= 1048576 ? WpfUI.Pal.Accent : WpfUI.Pal.Ok);
            cleanRingHost.Child = WpfUI.RingGauge(62, 6, pct, ringCol, selBytes >= 1048576 ? Util.FmtCompact(selBytes) : "0M", "可清理");

            btnRescan.IsEnabled = !cleanBusy;
            btnFullAudit.IsEnabled = !cleanBusy;
            btnHeroClean.IsEnabled = !cleanBusy && cleanChecked.Count > 0;
            if (cleanBusy) { heroTitleTxt.Text = "正在扫描/执行中，请稍候…"; btnHeroClean.Content = "执行中…"; }
            else if (selBytes >= 1048576) { heroTitleTxt.Text = "发现 " + Util.Fmt(selBytes) + " 可安全清理"; btnHeroClean.Content = "一键清理 " + Util.Fmt(selBytes); }
            else { heroTitleTxt.Text = "安全缓存很干净，可去「User 文件夹专清」或「大文件搬家」继续减负"; btnHeroClean.Content = cleanChecked.Count > 0 ? "清理所选项" : "无需清理"; }

            cleanGroupsPanel.Children.Clear();
            cleanGroupsPanel.Children.Add(BuildCleanGroupCard("apps", "浏览器与应用缓存", "Chrome、Edge、VS Code、Cursor 等网页与渲染缓存，删后不影响登录与聊天记录", WpfUI.Pal.AccentSoft, WpfUI.Pal.Accent));
            cleanGroupsPanel.Children.Add(BuildCleanGroupCard("system", "软件残留与系统临时垃圾", "软件更新残留安装包、过期临时文件、GPU 着色器缓存与崩溃日志", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn));
            cleanGroupsPanel.Children.Add(BuildCleanGroupCard("dev", "开发工具临时缓存", "npx、pip、npm、Yarn、Go 编译缓存与 Playwright 浏览器，自动重建", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));

            advGroupPanel.Children.Clear();
            advGroupPanel.Children.Add(BuildCleanGroupCard("advanced", "L3 进阶重要项（勾选后强制多重确认）", "系统回收站清空不可恢复；Go/NuGet 仓库与 Docker 缓存按需清理", WpfUI.Pal.CritSoft, WpfUI.Pal.Crit));
        }

        UIElement BuildCleanGroupCard(string gKey, string title, string desc, Brush icoBg, Brush icoFg)
        {
            var items = cleanItems.Where(i => GroupKey(i) == gKey).OrderByDescending(i => i.LastSize).ToList();
            if (items.Count == 0) return new Border();
            long total = items.Where(i => i.LastSize > 0).Sum(i => i.LastSize);
            int chkCnt = items.Count(i => cleanChecked.Contains(i.Id));
            bool isOpen = cleanGroupExpanded.Contains(gKey);

            var cardSp = new StackPanel();
            var head = new Grid { Margin = new Thickness(15, 12, 15, 12), Cursor = System.Windows.Input.Cursors.Hand };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var gChk = new CheckBox { IsChecked = chkCnt == items.Count ? (bool?)true : (chkCnt == 0 ? (bool?)false : null), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            gChk.Click += (s, e) =>
            {
                bool targetOn = chkCnt < items.Count;
                foreach (var it in items) { if (targetOn) cleanChecked.Add(it.Id); else cleanChecked.Remove(it.Id); }
                RefreshOverviewCleanUI();
                e.Handled = true;
            };
            Grid.SetColumn(gChk, 0);

            var icoBox = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = icoBg, Margin = new Thickness(0, 0, 12, 0), Child = WpfUI.Icon("M21 16V8l-9-5-9 5v8l9 5 9-5z", 16, icoFg) };
            Grid.SetColumn(icoBox, 1);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(WpfUI.Txt(title, 13.8, true, WpfUI.Pal.Text));
            info.Children.Add(WpfUI.Txt(desc, 11.8, false, WpfUI.Pal.Text2));
            Grid.SetColumn(info, 2);

            var sz = WpfUI.Txt(Util.Fmt(total), 14, true, total >= 500L * 1048576 ? WpfUI.Pal.Warn : (total > 0 ? WpfUI.Pal.Text : WpfUI.Pal.Text3), true);
            sz.Margin = new Thickness(10, 0, 12, 0);
            Grid.SetColumn(sz, 3);

            var expBtn = WpfUI.Btn(string.Format("明细 ({0}) {1}", items.Count, isOpen ? "⌃" : "⌄"), "soft", () =>
            {
                if (cleanGroupExpanded.Contains(gKey)) cleanGroupExpanded.Remove(gKey); else cleanGroupExpanded.Add(gKey);
                RefreshOverviewCleanUI();
            }, true);
            Grid.SetColumn(expBtn, 4);

            head.Children.Add(gChk); head.Children.Add(icoBox); head.Children.Add(info); head.Children.Add(sz); head.Children.Add(expBtn);
            head.MouseLeftButtonUp += (s, e) => { if (cleanGroupExpanded.Contains(gKey)) cleanGroupExpanded.Remove(gKey); else cleanGroupExpanded.Add(gKey); RefreshOverviewCleanUI(); };
            cardSp.Children.Add(head);

            if (isOpen)
            {
                var subSp = new StackPanel { Background = WpfUI.Pal.Surface2 };
                foreach (var it in items)
                {
                    var item = it;
                    var cb = new CheckBox { IsChecked = cleanChecked.Contains(item.Id), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) cleanChecked.Add(item.Id); else cleanChecked.Remove(item.Id); RefreshOverviewCleanUI(); };
                    var tags = item.IsCaution ? new UIElement[] { WpfUI.Badge("L3 重要项", WpfUI.Pal.CritSoft, WpfUI.Pal.Crit) } : null;
                    var acts = (item.InspectPaths != null && item.InspectPaths.Length > 0)
                        ? new UIElement[] { WpfUI.Btn("打开目录", "soft", () => Util.OpenInExplorer(item.InspectPaths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p)) ?? item.InspectPaths[0]), true) }
                        : null;
                    subSp.Children.Add(WpfUI.MakeRow(cb, item.Name, item.Note, item.LastSize == -2 ? "未运行" : Util.Fmt(item.LastSize), tags, acts));
                }
                cardSp.Children.Add(new Border { BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = subSp });
            }

            var c = WpfUI.Card(cardSp, new Thickness(0), 13);
            c.Margin = new Thickness(0, 0, 0, 9);
            return c;
        }

        void AskExecClean()
        {
            var targets = cleanItems.Where(i => cleanChecked.Contains(i.Id)).ToList();
            if (targets.Count == 0) return;
            long sum = targets.Where(i => i.LastSize > 0).Sum(i => i.LastSize);
            bool hasL3 = targets.Any(i => i.IsCaution);
            bool ok = WpfUI.ShowGuardDialog(this, hasL3 ? "L3" : "L2",
                hasL3 ? "包含 L3 重要清理项，请反复核对" : "确认执行 L1 安全清理",
                hasL3 ? "清单中包含回收站或重要依赖仓库（清空回收站后不可恢复），请仔细核对：" : "即将安全清理所选纯临时缓存，被占用文件自动跳过，绝不影响个人文档与登录态。",
                targets.Select(i => (i.IsCaution ? "[L3] " : "[L1] ") + i.Name + " (" + Util.Fmt(i.LastSize) + ")").ToList(),
                hasL3 ? "我已确认回收站/重要缓存内无需要找回的数据" : "确认清理所选 L1 纯缓存",
                hasL3 ? "确认清理" : "",
                "确认清理 " + Util.Fmt(sum));
            if (!ok) return;

            cleanBusy = true;
            cleanProgPanel.Visibility = Visibility.Visible;
            cleanProgBar.Value = 10;
            RefreshOverviewCleanUI();

            RunBg(() =>
            {
                var log = new StringBuilder();
                var before = DriveStat.Read(App.Cfg);
                log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 开始清理 ====");
                long freed = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    var it = targets[i];
                    OnUI(() => { cleanProgBar.Value = (i + 1) * 100.0 / targets.Count; cleanProgTxt.Text = "正在清理：" + it.Name; });
                    log.AppendLine("▶ " + it.Name);
                    try { long f = it.Clean(log); freed += f; log.AppendLine("  删除 " + Util.Fmt(f)); } catch (Exception ex) { log.AppendLine("  失败：" + ex.Message); }
                    App.CacheRemove(it.Id);
                    it.LastSize = 0;
                }
                var after = DriveStat.Read(App.Cfg);
                long delta = DriveStat.TotalFree(after) - DriveStat.TotalFree(before);
                log.AppendLine("合计：删除文件 " + Util.Fmt(freed) + "，磁盘可用空间变化 " + Util.FmtDelta(delta));
                log.AppendLine();
                try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }
                cleanBusy = false;
                OnUI(() =>
                {
                    cleanProgPanel.Visibility = Visibility.Collapsed;
                    cleanChecked.Clear();
                    App.RefreshStats();
                    RefreshDrives();
                    RefreshOverviewCleanUI();
                    ShowBanner(true, "瘦身完成！删除 " + Util.Fmt(freed) + "，可用空间变化 " + Util.FmtDelta(delta));
                });
            });
        }

        // ==================== PAGE 2: User 文件夹专清清单 ====================
        bool userLoaded = false;
        UserAuditResult userData;
        string userFilter = "all";
        TextBlock userHeroSubTxt, userMetaTxt;
        UniformGrid userKpiGrid;
        WrapPanel userFilterRow;
        StackPanel userListPanel;

        UIElement BuildUserPage()
        {
            var sp = new StackPanel();
            var hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hLeft = new StackPanel();
            hLeft.Children.Add(WpfUI.Txt("C 盘重点治理 · User 用户目录透视清单", 17, true, WpfUI.Pal.Text));
            userHeroSubTxt = WpfUI.TxtWrap("深度体检 C:\\Users 用户目录（含 AppData、开发点目录与下载），清晰标出 L1 可清、L2 建议搬家与 L4 核心锁定项", 12, WpfUI.Pal.Text2);
            userHeroSubTxt.Margin = new Thickness(0, 4, 12, 0);
            hLeft.Children.Add(userHeroSubTxt);
            var hActs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bGoCache = WpfUI.Btn("去改软件默认缓存位置", "secondary", () => { GoTab("apps"); SwitchAppsTab("caches"); });
            bGoCache.Margin = new Thickness(0, 0, 8, 0);
            hActs.Children.Add(bGoCache);
            hActs.Children.Add(WpfUI.Btn("重新体检 User 目录", "primary", ScanUserFolderAsync));
            Grid.SetColumn(hActs, 1);
            hGrid.Children.Add(hLeft); hGrid.Children.Add(hActs);
            var hc = WpfUI.HeroCard(hGrid); hc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(hc);

            userKpiGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 13) };
            sp.Children.Add(userKpiGrid);

            var panelSp = new StackPanel();
            var tb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            userFilterRow = new WrapPanel();
            userMetaTxt = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text3, true);
            Grid.SetColumn(userMetaTxt, 1);
            tb.Children.Add(userFilterRow); tb.Children.Add(userMetaTxt);
            panelSp.Children.Add(tb);
            panelSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            userListPanel = new StackPanel();
            userListPanel.Children.Add(new Border { Padding = new Thickness(20, 30, 20, 30), Child = WpfUI.Txt("正在并发扫描 User 文件夹占用…", 13, false, WpfUI.Pal.Text2) });
            panelSp.Children.Add(userListPanel);
            sp.Children.Add(WpfUI.Card(panelSp, new Thickness(0), 13));

            RenderUserAuditUI();
            return sp;
        }

        void ScanUserFolderAsync()
        {
            userLoaded = true;
            if (userListPanel != null)
            {
                userListPanel.Children.Clear();
                userListPanel.Children.Add(new Border { Padding = new Thickness(20, 30, 20, 30), Child = WpfUI.Txt("正在 Win32 并发扫描 User 文件夹（含 AppData 与开发点目录）…", 13, false, WpfUI.Pal.Text2) });
            }
            RunBg(() =>
            {
                var r = UserFolderAudit.Scan(CancellationToken.None);
                userData = r;
                OnUI(RenderUserAuditUI);
            });
        }

        void RenderUserAuditUI()
        {
            if (userFilterRow == null) return;
            userFilterRow.Children.Clear();
            var filters = new[] { new[] { "all", "全部目录" }, new[] { "L1", "仅看 L1 可安全清" }, new[] { "L2", "仅看 L2 建议搬家" }, new[] { "L4", "仅看 L4 核心锁定" } };
            foreach (var f in filters)
            {
                string fk = f[0];
                userFilterRow.Children.Add(WpfUI.PillBtn(f[1], userFilter == fk, () => { userFilter = fk; RenderUserAuditUI(); }));
            }

            userKpiGrid.Children.Clear();
            var k1 = WpfUI.KpiCard("纯缓存与临时文件", userData != null ? Util.Fmt(userData.L1TotalBytes) : "—", "pip / npm / Temp / 着色器缓存，删后自动重建", WpfUI.Pal.Ok, WpfUI.Badge("L1 可安全清空", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));
            k1.Margin = new Thickness(0, 0, 8, 0);
            var k2 = WpfUI.KpiCard("开发库、AI 模型与下载", userData != null ? Util.Fmt(userData.L2TotalBytes) : "—", "点「安全搬家」可无损软链迁移到数据盘", WpfUI.Pal.Warn, WpfUI.Badge("L2 建议搬家", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn));
            k2.Margin = new Thickness(0, 0, 8, 0);
            var k3 = WpfUI.KpiCard("个人文档与账号配置", userData != null ? Util.Fmt(userData.L4TotalBytes) : "—", "Desktop / Documents / .ssh / Roaming 永不动", WpfUI.Pal.Text, WpfUI.Badge("L4 核心锁定", WpfUI.Pal.SurfaceHover, WpfUI.Pal.Text2));
            userKpiGrid.Children.Add(k1); userKpiGrid.Children.Add(k2); userKpiGrid.Children.Add(k3);

            if (userData == null) return;
            userHeroSubTxt.Text = "当前主目录：" + userData.UserRoot + " · 已按 L1/L2/L4 分级标注处理建议";
            userMetaTxt.Text = string.Format("耗时 {0:0.0}s · 共 {1} 项", userData.ElapsedMs / 1000.0, userData.Items.Count);

            userListPanel.Children.Clear();
            var rows = userData.Items.Where(x => userFilter == "all" || x.Safety == userFilter).ToList();
            if (rows.Count == 0) { userListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("当前筛选条件下暂无占用项", 13, false, WpfUI.Pal.Text2) }); return; }

            long maxSz = Math.Max(1, rows.Max(r => r.SizeBytes));
            foreach (var item in rows)
            {
                var it = item;
                double pct = it.SizeBytes > 0 ? Math.Max(2, it.SizeBytes * 100.0 / maxSz) : 0;
                var bBg = it.Safety == "L1" ? WpfUI.Pal.OkSoft : (it.Safety == "L2" ? WpfUI.Pal.WarnSoft : WpfUI.Pal.SurfaceHover);
                var bFg = it.Safety == "L1" ? WpfUI.Pal.Ok : (it.Safety == "L2" ? WpfUI.Pal.Warn : WpfUI.Pal.Text2);
                var tags = new UIElement[] { WpfUI.Badge(it.Safety + " " + it.SafetyText, bBg, bFg), WpfUI.Badge(it.Category, WpfUI.Pal.Track, WpfUI.Pal.Text2) };

                var acts = new List<UIElement>();
                if (it.IsJunction) acts.Add(WpfUI.Badge("已安全搬家", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));
                else if (it.ActionType == "clean" && it.SizeBytes > 0)
                    acts.Add(WpfUI.Btn("安全清空", "primary", () =>
                    {
                        if (WpfUI.ShowGuardDialog(this, "L2", "安全清空 " + it.Name, "该目录属于白名单 L1 纯缓存，清空后不影响任何登录态或文档。", new[] { it.FullPath + " (" + Util.Fmt(it.SizeBytes) + ")" }, "确认清空此缓存目录", "", "确认清空"))
                            RunBg(() => { var r = UserFolderAudit.CleanSafeUserItem(it.RelPath); OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(r.Ok, r.Summary); ScanUserFolderAsync(); }); });
                    }, true));
                else if (it.ActionType == "migrate" && it.SizeBytes > 0)
                    acts.Add(WpfUI.Btn("安全搬家 ›", "secondary", () => PrefillMigrate(it.FullPath), true));
                acts.Add(WpfUI.Btn("打开", "soft", () => Util.OpenInExplorer(it.FullPath), true));

                string sub = it.Desc + (string.IsNullOrEmpty(it.JunctionTarget) ? (" · " + it.FullPath) : (" → " + it.JunctionTarget));
                userListPanel.Children.Add(WpfUI.MakeRow(null, it.Name, sub, it.IsJunction ? "0 B (软链)" : Util.Fmt(it.SizeBytes), tags, acts.ToArray(), pct, it.Safety == "L2" ? WpfUI.Pal.WarnSoft : WpfUI.Pal.AccentSoft));
            }
        }

        // ==================== PAGE 3: 空间透视、重复大文件与微信/QQ大附件 ====================
        string spaceMode = "";
        string spaceRootPath = "";
        int spaceRecentHours = 1;
        int dupMinMB = 20, chatMinMB = 5;
        SpaceScanResult spaceRes;
        DuplicateScanResult dupRes;
        ChatScanResult chatRes;
        HashSet<string> spaceSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TextBlock spaceTitleTxt, spaceProgTxt;
        WrapPanel spaceFiltersRow;
        StackPanel spaceListPanel, spaceToolbarActs;
        Border spaceProgWrap;

        UIElement BuildSpacePage()
        {
            var sp = new StackPanel();
            var taskGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 13) };
            taskGrid.Children.Add(MakeTaskCard("闲置项目依赖", "L2 可重建", "扫代码目录里长期未动的 node_modules / target / dist / .venv", () => RunSpaceScan("artifacts", Directory.Exists(@"E:\code") ? @"E:\code" : Util.UserProfile)));
            taskGrid.Children.Add(MakeTaskCard("全盘超大文件", "≥100MB", "扫全部分区超大安装包、视频、压缩包与虚拟磁盘", () => RunSpaceScan("files", "ALL")));
            taskGrid.Children.Add(MakeTaskCard("分区目录占用", "逐层钻取", "按文件夹体积排行层层钻取并标注 L4 系统锁定项", () => RunSpaceScan("dirs", (App.Stats.LastOrDefault() != null ? App.Stats.Last().Name : "E") + ":\\")));
            taskGrid.Children.Add(MakeTaskCard("骤降抓凶手", "刚写了啥", "揪出最近 1~24 小时新写入的大文件，抓出狂写日志程序", () => RunSpaceScan("recent", "")));
            taskGrid.Children.Add(MakeTaskCard("全盘重复大文件", "SHA-256", "三段精验同内容大文件，自动标出「保留原件」与多余副本", () => RunDupScan(dupMinMB)));
            taskGrid.Children.Add(MakeTaskCard("微信 / QQ 大附件", "不动聊天库", "专清微信、企业微信、QQ 接收的大文件与视频，严禁触碰 .db", () => RunChatScan(chatMinMB)));
            sp.Children.Add(taskGrid);

            var panelSp = new StackPanel();
            var tb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leftSp = new StackPanel();
            spaceTitleTxt = WpfUI.Txt("点击上方任意一张场景卡立即开始只读扫描", 13, true, WpfUI.Pal.Text);
            spaceFiltersRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            leftSp.Children.Add(spaceTitleTxt);
            leftSp.Children.Add(spaceFiltersRow);
            spaceToolbarActs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(spaceToolbarActs, 1);
            tb.Children.Add(leftSp); tb.Children.Add(spaceToolbarActs);
            panelSp.Children.Add(tb);
            panelSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });

            spaceProgTxt = WpfUI.Txt("正在 Win32 高速扫描…", 12, false, WpfUI.Pal.Accent);
            spaceProgWrap = new Border { Padding = new Thickness(14, 9, 14, 9), Background = WpfUI.Pal.Surface2, Visibility = Visibility.Collapsed, Child = spaceProgTxt };
            panelSp.Children.Add(spaceProgWrap);

            spaceListPanel = new StackPanel();
            spaceListPanel.Children.Add(new Border { Padding = new Thickness(20, 34, 20, 34), Child = WpfUI.Txt("选择上方卡片开始排查：所有大文件与重复副本均走 Windows 回收站，随时可还原", 12.8, false, WpfUI.Pal.Text2) });
            panelSp.Children.Add(spaceListPanel);

            sp.Children.Add(WpfUI.Card(panelSp, new Thickness(0), 13));
            return sp;
        }

        Border MakeTaskCard(string title, string badge, string desc, Action onClick)
        {
            var sp = new StackPanel();
            var top = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(WpfUI.Txt(title, 13.2, true, WpfUI.Pal.Text));
            var bd = WpfUI.Badge(badge, WpfUI.Pal.AccentSoft, WpfUI.Pal.Accent);
            Grid.SetColumn(bd, 1); top.Children.Add(bd);
            sp.Children.Add(top);
            sp.Children.Add(WpfUI.TxtWrap(desc, 11.4, WpfUI.Pal.Text2));
            var c = WpfUI.Card(sp, new Thickness(13, 11, 13, 11), 12);
            c.Margin = new Thickness(0, 0, 8, 8);
            c.Cursor = System.Windows.Input.Cursors.Hand;
            c.MouseLeftButtonUp += (s, e) => onClick();
            return c;
        }

        void ResetSpaceScan(string mode, string title)
        {
            if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            spaceScanCts = new CancellationTokenSource();
            spaceMode = mode;
            spaceSelected.Clear();
            spaceTitleTxt.Text = title;
            spaceProgWrap.Visibility = Visibility.Visible;
            spaceProgTxt.Text = "正在高速扫描…";
            spaceToolbarActs.Children.Clear();
            spaceToolbarActs.Children.Add(WpfUI.Btn("停止扫描", "danger", () => { if (spaceScanCts != null) try { spaceScanCts.Cancel(); } catch { } }, true));
            spaceListPanel.Children.Clear();
        }

        void RunSpaceScan(string mode, string path)
        {
            spaceRootPath = path ?? "";
            var eff = App.Cfg.EffectiveDrives();
            string title = mode == "recent" ? ("最近 " + spaceRecentHours + " 小时大写入") : (path == "ALL" ? "全盘超大文件（" + string.Join("/", eff) + "）" : path);
            ResetSpaceScan(mode, title);
            var ct = spaceScanCts.Token;

            RunBg(() =>
            {
                Action<SpaceScanProgress> onP = pr => OnUI(() => spaceProgTxt.Text = string.Format("已扫 {0:N0} 文件 / {1:N0} 目录… {2}", pr.ScannedFiles, pr.ScannedDirs, pr.CurrentHint));
                SpaceScanResult r;
                if (mode == "recent") r = SpaceAnalyzer.ScanRecentWriters(eff, TimeSpan.FromHours(spaceRecentHours), 10L * 1024 * 1024, ct, onP);
                else if (mode == "files" && path == "ALL") r = SpaceAnalyzer.ScanLargeFilesMultiDrive(eff, 100L * 1024 * 1024, ct, onP);
                else if (mode == "artifacts") r = SpaceAnalyzer.ScanBuildArtifacts(path, 7, ct, onP);
                else r = SpaceAnalyzer.ScanLargeItems(path, 100L * 1024 * 1024, ct, onP);
                spaceRes = r;
                OnUI(RenderSpaceResults);
            });
        }

        void RunDupScan(int minMB)
        {
            dupMinMB = minMB;
            var roots = App.Cfg.EffectiveDrives().Select(d => d + ":\\").ToArray();
            ResetSpaceScan("dups", "全盘重复大文件（≥" + minMB + "MB · 三段式 SHA-256 精验）");
            var ct = spaceScanCts.Token;
            RunBg(() =>
            {
                var r = ExtraScanners.FindDuplicates(roots, minMB * 1048576L, ct, pr => OnUI(() => spaceProgTxt.Text = pr.CurrentHint ?? "精验中…"));
                dupRes = r;
                OnUI(RenderDupResults);
            });
        }

        void RunChatScan(int minMB)
        {
            chatMinMB = minMB;
            ResetSpaceScan("chat", "微信 / 企业微信 / QQ 接收大附件与视频（≥" + minMB + "MB · 严禁触碰 .db）");
            var ct = spaceScanCts.Token;
            RunBg(() =>
            {
                var r = ExtraScanners.ScanChatAttachments(minMB * 1048576L, ct);
                chatRes = r;
                OnUI(RenderChatResults);
            });
        }

        void RenderSpaceResults()
        {
            spaceProgWrap.Visibility = Visibility.Collapsed;
            spaceFiltersRow.Children.Clear();
            spaceToolbarActs.Children.Clear();
            spaceListPanel.Children.Clear();
            if (spaceRes == null) return;

            if (spaceMode == "dirs" || spaceMode == "files")
            {
                if (spaceMode == "files") spaceFiltersRow.Children.Add(WpfUI.PillBtn("全部盘", spaceRootPath == "ALL", () => RunSpaceScan("files", "ALL")));
                foreach (var d in App.Stats.Where(x => x.Ready))
                {
                    string drv = d.Name + ":\\";
                    spaceFiltersRow.Children.Add(WpfUI.PillBtn(d.Name + ": 盘", spaceRootPath.StartsWith(drv, StringComparison.OrdinalIgnoreCase), () => RunSpaceScan(spaceMode, drv)));
                }
            }
            else if (spaceMode == "recent")
            {
                foreach (var h in new[] { 1, 6, 24 })
                {
                    int hrs = h;
                    spaceFiltersRow.Children.Add(WpfUI.PillBtn("最近 " + h + " 小时", spaceRecentHours == h, () => { spaceRecentHours = hrs; RunSpaceScan("recent", ""); }));
                }
            }
            else if (spaceMode == "artifacts")
            {
                spaceToolbarActs.Children.Add(WpfUI.Btn("清理选中依赖 (" + spaceSelected.Count + ")", "danger", () =>
                {
                    if (spaceSelected.Count == 0) { ShowBanner(false, "请先勾选要清理的项目依赖目录"); return; }
                    var list = spaceSelected.ToList();
                    if (WpfUI.ShowGuardDialog(this, "L2", "确认删除 " + list.Count + " 个项目构建产物", "仅删除可重建的依赖/产物目录，绝不动源码。", list, "确认删除所选目录", "", "确认删除"))
                    {
                        RunBg(() =>
                        {
                            long freed = 0; var sb = new StringBuilder();
                            foreach (var p in list) freed += Fs.DeleteDirSafe(p, sb);
                            OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(true, "已安全清理项目依赖，释放 " + Util.Fmt(freed)); RunSpaceScan("artifacts", spaceRootPath); });
                        });
                    }
                }, true));
            }

            List<SpaceEntry> rows = spaceMode == "dirs" ? spaceRes.Directories : (spaceMode == "artifacts" ? spaceRes.BuildArtifacts : spaceRes.LargeFiles);
            if (rows == null || rows.Count == 0) { spaceListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("未发现符合条件的大文件或目录", 13, false, WpfUI.Pal.Text2) }); return; }

            long maxSz = Math.Max(1, rows[0].Size);
            foreach (var entry in rows.Take(100))
            {
                var it = entry;
                double pct = Math.Max(2, it.Size * 100.0 / maxSz);
                UIElement left = null;
                if (spaceMode == "artifacts")
                {
                    var cb = new CheckBox { IsChecked = spaceSelected.Contains(it.FullPath), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) spaceSelected.Add(it.FullPath); else spaceSelected.Remove(it.FullPath); RenderSpaceResults(); };
                    left = cb;
                }
                var tags = new List<UIElement>();
                if (it.IsProtected) tags.Add(WpfUI.Badge("L4 锁定", WpfUI.Pal.SurfaceHover, WpfUI.Pal.Text2));
                else if (!string.IsNullOrEmpty(it.Tag)) tags.Add(WpfUI.Badge(it.Tag, WpfUI.Pal.Track, WpfUI.Pal.Text2));

                var acts = new List<UIElement>();
                if (spaceMode == "dirs") acts.Add(WpfUI.Btn("进入", "soft", () => RunSpaceScan("dirs", it.FullPath), true));
                if (!it.IsProtected) acts.Add(WpfUI.Btn("搬家", "soft", () => PrefillMigrate(it.FullPath), true));
                acts.Add(WpfUI.Btn("打开", "secondary", () => Util.OpenInExplorer(it.FullPath), true));
                if ((spaceMode == "files" || spaceMode == "recent") && !it.IsProtected)
                    acts.Add(WpfUI.Btn("回收站", "danger", () => AskTrashSingle(it.FullPath, it.DisplayName, Util.Fmt(it.Size), () => RunSpaceScan(spaceMode, spaceRootPath)), true));

                spaceListPanel.Children.Add(WpfUI.MakeRow(left, it.DisplayName, it.ParentLabel + (string.IsNullOrEmpty(it.Advice) ? "" : (" · " + it.Advice)), Util.Fmt(it.Size), tags.ToArray(), acts.ToArray(), pct, it.IsProtected ? WpfUI.Pal.Track : WpfUI.Pal.AccentSoft));
            }
        }

        void RenderDupResults()
        {
            spaceProgWrap.Visibility = Visibility.Collapsed;
            spaceFiltersRow.Children.Clear();
            spaceToolbarActs.Children.Clear();
            spaceListPanel.Children.Clear();
            foreach (var mb in new[] { 20, 50, 100 })
            {
                int m = mb;
                spaceFiltersRow.Children.Add(WpfUI.PillBtn("≥" + mb + "M", dupMinMB == mb, () => RunDupScan(m)));
            }
            if (dupRes == null || dupRes.Groups.Count == 0)
            {
                spaceListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("未发现 ≥" + dupMinMB + " MB 的同内容重复大文件", 13, false, WpfUI.Pal.Text2) });
                return;
            }
            var bSel = WpfUI.Btn("一键勾选全部副本", "secondary", () =>
            {
                spaceSelected.Clear();
                foreach (var g in dupRes.Groups) for (int i = 1; i < g.Files.Count; i++) spaceSelected.Add(g.Files[i].FullPath);
                RenderDupResults();
            }, true);
            bSel.Margin = new Thickness(0, 0, 6, 0);
            spaceToolbarActs.Children.Add(bSel);
            spaceToolbarActs.Children.Add(WpfUI.Btn("移入回收站 (" + spaceSelected.Count + ")", "danger", () => AskTrashBatch(() => RunDupScan(dupMinMB)), true));

            int gi = 0;
            foreach (var grp in dupRes.Groups.Take(50))
            {
                gi++;
                var gh = new Border { Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(15, 7, 15, 7), Child = WpfUI.Txt(string.Format("重复组 #{0} · 单份 {1} · 共 {2} 份 · SHA-256 {3} · 可省 {4}", gi, Util.Fmt(grp.FileSize), grp.Files.Count, grp.HashShort, Util.Fmt(grp.WastedBytes)), 11.8, true, WpfUI.Pal.Warn, true) };
                spaceListPanel.Children.Add(gh);
                for (int idx = 0; idx < grp.Files.Count; idx++)
                {
                    var f = grp.Files[idx];
                    bool orig = idx == 0;
                    var cb = new CheckBox { IsChecked = spaceSelected.Contains(f.FullPath), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) spaceSelected.Add(f.FullPath); else spaceSelected.Remove(f.FullPath); RenderDupResults(); };
                    var tags = new UIElement[] { orig ? WpfUI.Badge("保留原件", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok) : WpfUI.Badge("重复副本", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn) };
                    var acts = new UIElement[] { WpfUI.Btn("打开", "secondary", () => Util.OpenInExplorer(f.FullPath), true) };
                    spaceListPanel.Children.Add(WpfUI.MakeRow(cb, f.DisplayName, f.ParentLabel, Util.Fmt(f.Size), tags, acts));
                }
            }
        }

        void RenderChatResults()
        {
            spaceProgWrap.Visibility = Visibility.Collapsed;
            spaceFiltersRow.Children.Clear();
            spaceToolbarActs.Children.Clear();
            spaceListPanel.Children.Clear();
            foreach (var mb in new[] { 5, 20, 50 })
            {
                int m = mb;
                spaceFiltersRow.Children.Add(WpfUI.PillBtn("≥" + mb + "M", chatMinMB == mb, () => RunChatScan(m)));
            }
            spaceToolbarActs.Children.Add(WpfUI.Btn("移入回收站 (" + spaceSelected.Count + ")", "danger", () => AskTrashBatch(() => RunChatScan(chatMinMB)), true));
            if (chatRes == null || chatRes.Files.Count == 0)
            {
                spaceListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("未发现 ≥" + chatMinMB + " MB 的微信/企业微信/QQ大附件（已自动保护排除聊天数据库）", 13, false, WpfUI.Pal.Text2) });
                return;
            }
            long maxSz = Math.Max(1, chatRes.Files[0].Size);
            foreach (var f in chatRes.Files.Take(100))
            {
                var file = f;
                var cb = new CheckBox { IsChecked = spaceSelected.Contains(file.FullPath), VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (s, e) => { if (cb.IsChecked == true) spaceSelected.Add(file.FullPath); else spaceSelected.Remove(file.FullPath); RenderChatResults(); };
                var tags = new UIElement[] { WpfUI.Badge(file.Tag, WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn) };
                var acts = new UIElement[] { WpfUI.Btn("打开", "secondary", () => Util.OpenInExplorer(file.FullPath), true) };
                spaceListPanel.Children.Add(WpfUI.MakeRow(cb, file.DisplayName, file.ParentLabel, Util.Fmt(file.Size), tags, acts, Math.Max(2, file.Size * 100.0 / maxSz)));
            }
        }

        void AskTrashSingle(string path, string name, string sz, Action after)
        {
            if (!WpfUI.ShowGuardDialog(this, "L2", "移入回收站：" + name, "文件将移入 Windows 回收站，随时可完整还原。", new[] { path + " (" + sz + ")" }, "确认移入回收站", "", "移入回收站")) return;
            RunBg(() =>
            {
                bool ok = Win32Native.SendToRecycleBin(path);
                OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(ok, ok ? ("已移入回收站：" + name) : "文件正被占用或受系统保护"); if (ok && after != null) after(); });
            });
        }

        void AskTrashBatch(Action after)
        {
            if (spaceSelected.Count == 0) { ShowBanner(false, "请先勾选要移入回收站的文件"); return; }
            var list = spaceSelected.ToList();
            if (!WpfUI.ShowGuardDialog(this, "L2", "批量移入回收站 (" + list.Count + " 个文件)", "所选文件将安全移入 Windows 回收站（随时可完整还原）。", list, "确认批量移入回收站", "", "移入回收站")) return;
            RunBg(() =>
            {
                int okCnt = 0; long sum = 0;
                foreach (var p in list) { long len = Util.FileLen(p); if (Win32Native.SendToRecycleBin(p)) { okCnt++; sum += len; } }
                OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(true, string.Format("已移入回收站 {0}/{1} 项（共 {2}，可随时还原）", okCnt, list.Count, Util.Fmt(sum))); if (after != null) after(); });
            });
        }
    }
}
