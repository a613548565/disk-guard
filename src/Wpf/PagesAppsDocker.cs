// DiskGuard v9.0 - 原生 WPF 软件搬家与容器 3 页（软件卸载与默认缓存改位 / 零丢失大文件安全搬家 / Docker 与 WSL2 调优）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DiskGuard
{
    public partial class MainWindow
    {
        // ==================== PAGE 4: 软件卸载与默认缓存改位 ====================
        string appsSubTab = "caches";
        bool cachesLoaded = false, softLoaded = false;
        List<AppCacheLocation> appCaches = new List<AppCacheLocation>();
        List<InstalledAppEntry> installedApps = new List<InstalledAppEntry>();
        WrapPanel appsTabRow;
        StackPanel appsContentHost;
        TextBox cacheTargetBox, softSearchBox;
        CheckBox chkMoveCacheData;
        StackPanel cachesListPanel, softListPanel, leftoverListPanel;
        Border leftoverPanel;
        TextBlock leftoverTitleTxt, softCntTxt;

        UIElement BuildAppsPage()
        {
            var sp = new StackPanel();
            var hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hL = new StackPanel();
            hL.Children.Add(WpfUI.Txt("软件卸载治理 & 默认缓存改位中心", 17, true, WpfUI.Pal.Text));
            var hSub = WpfUI.TxtWrap("查清系统/开发/AI 软件默认缓存位置并一键安全改位到数据盘；同时支持官方卸载向导与只读卸载残留排查", 12, WpfUI.Pal.Text2);
            hSub.Margin = new Thickness(0, 4, 12, 0);
            hL.Children.Add(hSub);
            appsTabRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(appsTabRow, 1);
            hGrid.Children.Add(hL); hGrid.Children.Add(appsTabRow);
            var hc = WpfUI.HeroCard(hGrid); hc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(hc);

            appsContentHost = new StackPanel();
            sp.Children.Add(appsContentHost);
            SwitchAppsTab(appsSubTab);
            return sp;
        }

        void SwitchAppsTab(string tab)
        {
            appsSubTab = tab;
            if (appsTabRow != null)
            {
                appsTabRow.Children.Clear();
                appsTabRow.Children.Add(WpfUI.PillBtn("软件默认缓存改位", tab == "caches", () => SwitchAppsTab("caches")));
                appsTabRow.Children.Add(WpfUI.PillBtn("已安装软件卸载", tab == "uninstall", () => SwitchAppsTab("uninstall")));
            }
            if (appsContentHost == null) return;
            appsContentHost.Children.Clear();

            if (tab == "caches")
            {
                string defDrive = DriveStat.AllFixedDrives().FirstOrDefault(d => d != "C") ?? "E";
                var cardSp = new StackPanel();
                var tb = new WrapPanel { Margin = new Thickness(14, 11, 14, 11), VerticalAlignment = VerticalAlignment.Center };
                var lbl = WpfUI.Txt("目标数据盘根目录：", 12.6, true, WpfUI.Pal.Text);
                lbl.Margin = new Thickness(0, 0, 6, 0);
                cacheTargetBox = WpfUI.InputBox(defDrive + @":\AppCaches", 230, true);
                cacheTargetBox.Margin = new Thickness(0, 0, 6, 0);
                var bBr = WpfUI.Btn("浏览…", "secondary", () => { string p = PickFolderDialog(cacheTargetBox.Text); if (!string.IsNullOrEmpty(p)) cacheTargetBox.Text = p; }, true);
                bBr.Margin = new Thickness(0, 0, 14, 0);
                chkMoveCacheData = new CheckBox { IsChecked = true, Content = WpfUI.Txt("同时校验搬运旧缓存数据", 12, false, WpfUI.Pal.Text2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var bRef = WpfUI.Btn("刷新检测", "soft", LoadAppCachesAsync, true);
                tb.Children.Add(lbl); tb.Children.Add(cacheTargetBox); tb.Children.Add(bBr); tb.Children.Add(chkMoveCacheData); tb.Children.Add(bRef);
                cardSp.Children.Add(tb);
                cardSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
                cachesListPanel = new StackPanel();
                cardSp.Children.Add(cachesListPanel);
                appsContentHost.Children.Add(WpfUI.Card(cardSp, new Thickness(0), 13));
                if (!cachesLoaded) LoadAppCachesAsync(); else RenderAppCachesUI();
            }
            else
            {
                var cardSp = new StackPanel();
                var tb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
                tb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lSp = new StackPanel { Orientation = Orientation.Horizontal };
                softSearchBox = WpfUI.InputBox("", 240, false);
                softSearchBox.TextChanged += (s, e) => RenderSoftwareUI();
                softSearchBox.Margin = new Thickness(0, 0, 10, 0);
                softCntTxt = WpfUI.Txt("读取中…", 12, false, WpfUI.Pal.Text3, true);
                lSp.Children.Add(softSearchBox); lSp.Children.Add(softCntTxt);
                var bRef = WpfUI.Btn("刷新列表", "secondary", LoadSoftwareAsync, true);
                Grid.SetColumn(bRef, 1);
                tb.Children.Add(lSp); tb.Children.Add(bRef);
                cardSp.Children.Add(tb);
                cardSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
                softListPanel = new StackPanel();
                cardSp.Children.Add(new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = softListPanel });
                appsContentHost.Children.Add(WpfUI.Card(cardSp, new Thickness(0), 13));

                var lfSp = new StackPanel();
                var lfHead = new Grid { Margin = new Thickness(14, 10, 14, 10) };
                lfHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                lfHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftoverTitleTxt = WpfUI.Txt("卸载残留检查结果", 13, true, WpfUI.Pal.Text);
                var bCls = WpfUI.Btn("收起", "soft", () => leftoverPanel.Visibility = Visibility.Collapsed, true);
                Grid.SetColumn(bCls, 1);
                lfHead.Children.Add(leftoverTitleTxt); lfHead.Children.Add(bCls);
                lfSp.Children.Add(lfHead);
                lfSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
                leftoverListPanel = new StackPanel();
                lfSp.Children.Add(leftoverListPanel);
                leftoverPanel = WpfUI.Card(lfSp, new Thickness(0), 13);
                leftoverPanel.Margin = new Thickness(0, 12, 0, 0);
                leftoverPanel.Visibility = Visibility.Collapsed;
                appsContentHost.Children.Add(leftoverPanel);

                if (!softLoaded) LoadSoftwareAsync(); else RenderSoftwareUI();
            }
        }

        void LoadAppCachesAsync()
        {
            cachesLoaded = true;
            if (cachesListPanel != null) { cachesListPanel.Children.Clear(); cachesListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("正在扫描各软件默认缓存位置…", 12.8, false, WpfUI.Pal.Text2) }); }
            RunBg(() => { appCaches = SafeRelocator.InspectAppCaches(CancellationToken.None); OnUI(RenderAppCachesUI); });
        }

        void RenderAppCachesUI()
        {
            if (cachesListPanel == null) return;
            cachesListPanel.Children.Clear();
            long maxSz = Math.Max(1, appCaches.Count > 0 ? appCaches.Max(x => x.SizeBytes) : 1);
            foreach (var item in appCaches)
            {
                var c = item;
                double pct = c.SizeBytes > 0 ? Math.Max(2, c.SizeBytes * 100.0 / maxSz) : 0;
                var drvTag = c.IsJunction
                    ? WpfUI.Badge("已软链迁移", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok)
                    : (c.IsOnSystemDrive ? WpfUI.Badge("位于系统 " + c.Drive + " 盘", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn) : WpfUI.Badge("已在数据 " + c.Drive + " 盘", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));
                var tags = new UIElement[] { drvTag, WpfUI.Badge(c.Category, WpfUI.Pal.Track, WpfUI.Pal.Text2) };
                var acts = new List<UIElement>();
                if (c.Exists) acts.Add(WpfUI.Btn("打开", "soft", () => Util.OpenInExplorer(c.CurrentPath), true));
                if (c.CanRelocate)
                    acts.Add(WpfUI.Btn("更改到数据盘", "primary", () =>
                    {
                        string targetBase = (cacheTargetBox.Text ?? "").Trim();
                        bool move = chkMoveCacheData.IsChecked == true;
                        if (string.IsNullOrEmpty(targetBase)) { ShowBanner(false, "请先选择目标数据盘根目录"); return; }
                        if (!Directory.Exists(targetBase)) { try { Directory.CreateDirectory(targetBase); } catch { } }
                        if (!WpfUI.ShowGuardDialog(this, "L2", "更改「" + c.AppName + "」默认缓存位置",
                            "将把默认缓存路径安全切换至 " + Path.Combine(targetBase, "AppCache_" + c.Id) + "（修改环境变量前会自动备份快照）。",
                            new[] { "原位置：" + c.CurrentPath, "新位置：" + Path.Combine(targetBase, "AppCache_" + c.Id) },
                            "确认更改此软件默认缓存路径", "", "确认更改")) return;
                        RunBg(() =>
                        {
                            var r = SafeRelocator.RelocateAppCache(c.Id, targetBase, move, null);
                            OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(r.Ok, r.Title + "：" + r.Summary); LoadAppCachesAsync(); });
                        });
                    }, true));

                cachesListPanel.Children.Add(WpfUI.MakeRow(null, c.AppName, c.CurrentPath + " · " + c.Advice, c.Exists ? Util.Fmt(c.SizeBytes) : "尚未产生", tags, acts.ToArray(), pct, c.IsOnSystemDrive ? WpfUI.Pal.WarnSoft : WpfUI.Pal.AccentSoft));
            }
        }

        void LoadSoftwareAsync()
        {
            softLoaded = true;
            if (softListPanel != null) { softListPanel.Children.Clear(); softListPanel.Children.Add(new Border { Padding = new Thickness(20, 26, 20, 26), Child = WpfUI.Txt("正在读取已安装软件清单…", 12.8, false, WpfUI.Pal.Text2) }); }
            RunBg(() => { installedApps = SoftwareManager.ListInstalledApps(); OnUI(RenderSoftwareUI); });
        }

        void RenderSoftwareUI()
        {
            if (softListPanel == null) return;
            string kw = (softSearchBox != null ? softSearchBox.Text : "").Trim().ToLowerInvariant();
            var rows = installedApps.Where(a => string.IsNullOrEmpty(kw) || a.Name.ToLowerInvariant().Contains(kw) || (a.Publisher ?? "").ToLowerInvariant().Contains(kw)).ToList();
            if (softCntTxt != null) softCntTxt.Text = string.Format("显示 {0} / {1} 款软件", rows.Count, installedApps.Count);
            softListPanel.Children.Clear();
            foreach (var app in rows.Take(150))
            {
                var a = app;
                var tags = new List<UIElement>();
                if (!string.IsNullOrEmpty(a.Version)) tags.Add(WpfUI.Badge(a.Version, WpfUI.Pal.Track, WpfUI.Pal.Text2));
                var acts = new UIElement[]
                {
                    WpfUI.Btn("扫残留", "soft", () => RunBg(() => { var lf = SoftwareManager.ScanLeftovers(a.Name, a.Publisher, a.InstallLocation); OnUI(() => ShowLeftovers(a.Name, lf)); }), true),
                    WpfUI.Btn("官方卸载", "secondary", () =>
                    {
                        if (!WpfUI.ShowGuardDialog(this, "L2", "启动官方卸载向导：" + a.Name, "将调用该软件自带的标准卸载程序（绝不暴力删文件）。卸载完成后可点击「扫残留」清理遗留目录。", new[] { a.Name + " " + a.Version }, "确认启动官方卸载程序", "", "启动卸载")) return;
                        RunBg(() => { var r = SoftwareManager.LaunchUninstall(a.Id); OnUI(() => ShowBanner(r.Ok, r.Title + "：" + r.Summary)); });
                    }, true)
                };
                softListPanel.Children.Add(WpfUI.MakeRow(null, a.Name, (string.IsNullOrEmpty(a.Publisher) ? "未知发布者" : a.Publisher) + (string.IsNullOrEmpty(a.InstallLocation) ? "" : (" · " + a.InstallLocation)), a.EstimatedBytes > 0 ? Util.Fmt(a.EstimatedBytes) : "未知", tags.ToArray(), acts));
            }
        }

        void ShowLeftovers(string appName, List<LeftoverEntry> list)
        {
            leftoverPanel.Visibility = Visibility.Visible;
            leftoverTitleTxt.Text = "「" + appName + "」关联目录与残留检查（共 " + list.Count + " 项）";
            leftoverListPanel.Children.Clear();
            if (list.Count == 0) { leftoverListPanel.Children.Add(new Border { Padding = new Thickness(16), Child = WpfUI.Txt("未发现明显的独立残留目录，很干净", 12.5, false, WpfUI.Pal.Text2) }); return; }
            foreach (var item in list)
            {
                var x = item;
                var tags = new UIElement[] { WpfUI.Badge(x.Area, WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn) };
                var acts = new UIElement[]
                {
                    WpfUI.Btn("查看", "secondary", () => Util.OpenInExplorer(x.Path), true),
                    WpfUI.Btn("移入回收站", "danger", () => AskTrashSingle(x.Path, x.Path, Util.Fmt(x.SizeBytes), () => ShowLeftovers(appName, list.Where(i => i.Path != x.Path).ToList())), true)
                };
                leftoverListPanel.Children.Add(WpfUI.MakeRow(null, x.Path, "确认不再使用后再移入回收站（随时可还原）", Util.Fmt(x.SizeBytes), tags, acts));
            }
        }

        // ==================== PAGE 5: 零丢失大文件 / 大目录安全搬家 ====================
        string migMode = "junction";
        TextBox migSrcBox, migDstBox;
        WrapPanel migModeRow;
        Border migProgWrap;
        ProgressBar migProgBar;
        TextBlock migProgTxt;
        StackPanel migListPanel;
        Button btnStartMig;

        UIElement BuildMigratePage()
        {
            var sp = new StackPanel();
            var hSp = new StackPanel();
            hSp.Children.Add(WpfUI.Txt("零丢失大文件 / 大目录安全搬家引擎", 17, true, WpfUI.Pal.Text));
            var hSub = WpfUI.TxtWrap("先只读拷贝 → 100% 文件数与字节数校验 → 占用检测后原子切换 → 原位置建立透明 NTFS 软链（任何环节异常自动无损回滚）", 12, WpfUI.Pal.Text2);
            hSub.Margin = new Thickness(0, 4, 0, 12);
            hSp.Children.Add(hSub);

            var steps = new UniformGrid { Columns = 4 };
            string[,] sInfo = { { "STEP 01", "源路径只读拷贝", "拷贝期间原文件零改动" }, { "STEP 02", "100% 字节校验", "少一字节立即中止保护" }, { "STEP 03", "占用检测与切换", "软件占用自动拦截回滚" }, { "STEP 04", "透明软链与迁回", "软件无感知且随时迁回" } };
            for (int i = 0; i < 4; i++)
            {
                var bs = new StackPanel();
                bs.Children.Add(WpfUI.Txt(sInfo[i, 0], 10.2, true, WpfUI.Pal.Accent, true));
                bs.Children.Add(WpfUI.Txt(sInfo[i, 1], 12.2, true, WpfUI.Pal.Text));
                bs.Children.Add(WpfUI.Txt(sInfo[i, 2], 11, false, WpfUI.Pal.Text2));
                var bx = new Border { Background = WpfUI.Pal.Surface, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, i < 3 ? 8 : 0, 0), Child = bs };
                steps.Children.Add(bx);
            }
            hSp.Children.Add(steps);
            var hc = WpfUI.HeroCard(hSp); hc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(hc);

            var formSp = new StackPanel();
            formSp.Children.Add(WpfUI.Txt("新建安全搬家任务", 14, true, WpfUI.Pal.Text));

            var r1 = new Grid { Margin = new Thickness(0, 10, 0, 8) };
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r1.Children.Add(WpfUI.Txt("源目录 / 文件", 12.4, true, WpfUI.Pal.Text));
            migSrcBox = WpfUI.InputBox("", 0, true); migSrcBox.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(migSrcBox, 1);
            var bPickDir = WpfUI.Btn("选文件夹…", "secondary", () => { string p = PickFolderDialog(migSrcBox.Text); if (!string.IsNullOrEmpty(p)) SetMigrateSource(p); }, true);
            bPickDir.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(bPickDir, 2);
            var bPickFile = WpfUI.Btn("选大文件…", "soft", () => { string p = PickFileDialog(); if (!string.IsNullOrEmpty(p)) SetMigrateSource(p); }, true);
            Grid.SetColumn(bPickFile, 3);
            r1.Children.Add(migSrcBox); r1.Children.Add(bPickDir); r1.Children.Add(bPickFile);
            formSp.Children.Add(r1);

            var r2 = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            r2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            r2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r2.Children.Add(WpfUI.Txt("目标新位置", 12.4, true, WpfUI.Pal.Text));
            migDstBox = WpfUI.InputBox("", 0, true); migDstBox.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(migDstBox, 1);
            var bPickDst = WpfUI.Btn("选目标盘目录…", "secondary", () =>
            {
                string p = PickFolderDialog("");
                if (!string.IsNullOrEmpty(p))
                {
                    string leaf = string.IsNullOrEmpty(migSrcBox.Text) ? "" : Path.GetFileName(migSrcBox.Text.Trim().TrimEnd('\\'));
                    migDstBox.Text = string.IsNullOrEmpty(leaf) ? p : Path.Combine(p, leaf);
                }
            }, true);
            Grid.SetColumn(bPickDst, 2);
            r2.Children.Add(migDstBox); r2.Children.Add(bPickDst);
            formSp.Children.Add(r2);

            var r3 = new Grid();
            r3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            migModeRow = new WrapPanel();
            btnStartMig = WpfUI.Btn("安全校验并开始搬家", "primary", StartMigrateConfirmed);
            Grid.SetColumn(btnStartMig, 1);
            r3.Children.Add(migModeRow); r3.Children.Add(btnStartMig);
            formSp.Children.Add(r3);

            migProgBar = new ProgressBar { Height = 6, Maximum = 100, Foreground = WpfUI.Pal.Accent, Background = WpfUI.Pal.Track, BorderThickness = new Thickness(0) };
            migProgTxt = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text2); migProgTxt.Margin = new Thickness(0, 4, 0, 0);
            var mpSp = new StackPanel(); mpSp.Children.Add(migProgBar); mpSp.Children.Add(migProgTxt);
            migProgWrap = new Border { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = mpSp };
            formSp.Children.Add(migProgWrap);

            var fCard = WpfUI.Card(formSp); fCard.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(fCard);

            var histSp = new StackPanel();
            var hHead = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            hHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hHead.Children.Add(WpfUI.Txt("已迁移历史记录（支持一键无损迁回原位）", 13.5, true, WpfUI.Pal.Text));
            var bHRef = WpfUI.Btn("刷新记录", "soft", ReloadMigrations, true); Grid.SetColumn(bHRef, 1);
            hHead.Children.Add(bHRef);
            histSp.Children.Add(hHead);
            histSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            migListPanel = new StackPanel();
            histSp.Children.Add(migListPanel);
            sp.Children.Add(WpfUI.Card(histSp, new Thickness(0), 13));

            RenderMigModePills();
            ReloadMigrations();
            return sp;
        }

        void RenderMigModePills()
        {
            if (migModeRow == null) return;
            migModeRow.Children.Clear();
            migModeRow.Children.Add(WpfUI.PillBtn("透明软链接搬家（推荐文件夹 · 软件无感知）", migMode == "junction", () => { migMode = "junction"; RenderMigModePills(); }));
            migModeRow.Children.Add(WpfUI.PillBtn("纯物理移动（适合独立大文件/归档）", migMode == "move", () => { migMode = "move"; RenderMigModePills(); }));
        }

        void SetMigrateSource(string src)
        {
            migSrcBox.Text = src;
            string leaf = Path.GetFileName(src.TrimEnd('\\'));
            string nonSys = DriveStat.AllFixedDrives().FirstOrDefault(d => d != "C") ?? "E";
            if (!string.IsNullOrEmpty(leaf)) migDstBox.Text = Path.Combine(nonSys + @":\DiskGuard_Moved", leaf);
        }

        void PrefillMigrate(string fullPath)
        {
            GoTab("migrate");
            SetMigrateSource(fullPath);
            ShowBanner(true, "已自动填入源路径与建议数据盘目标，核对后即可开始零丢失安全搬家");
        }

        void StartMigrateConfirmed()
        {
            string src = (migSrcBox.Text ?? "").Trim(), dst = (migDstBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) { ShowBanner(false, "请先选择源路径与目标新位置"); return; }
            if (!WpfUI.ShowGuardDialog(this, "L2", "确认启动零丢失安全搬家",
                "搬家期间源数据全程只读，100% 文件数与字节数校验一致后才会切换路径；如被占用将自动中止回滚。",
                new[] { "源路径：" + src, "目标位置：" + dst, "模式：" + (migMode == "junction" ? "透明软链接搬家" : "纯物理移动") },
                "我已关闭正在读写该目录的程序，确认开始搬家", "", "开始校验搬家")) return;

            btnStartMig.IsEnabled = false;
            migProgWrap.Visibility = Visibility.Visible;
            migProgBar.Value = 5;
            RunBg(() =>
            {
                var r = SafeRelocator.MigratePath(src, dst, migMode, CancellationToken.None, (txt, pct) => OnUI(() => { migProgBar.Value = pct; migProgTxt.Text = txt; }));
                OnUI(() =>
                {
                    btnStartMig.IsEnabled = true;
                    migProgWrap.Visibility = Visibility.Collapsed;
                    App.RefreshStats(); RefreshDrives();
                    ShowBanner(r.Ok, r.Title + "：" + r.Summary);
                    ReloadMigrations();
                });
            });
        }

        void ReloadMigrations()
        {
            if (migListPanel == null) return;
            migListPanel.Children.Clear();
            var list = SafeRelocator.ListMigrations();
            if (list.Count == 0) { migListPanel.Children.Add(new Border { Padding = new Thickness(20, 24, 20, 24), Child = WpfUI.Txt("暂无搬家记录，可在上方或「User 文件夹专清」发起安全搬家", 12.5, false, WpfUI.Pal.Text2) }); return; }
            foreach (var rec in list)
            {
                var m = rec;
                var tags = new UIElement[] { m.Active ? WpfUI.Badge(m.Mode == "junction" ? "软链生效中" : "已移动", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok) : WpfUI.Badge("已迁回原位", WpfUI.Pal.Track, WpfUI.Pal.Text2) };
                var acts = new List<UIElement> { WpfUI.Btn("打开", "soft", () => Util.OpenInExplorer(m.Active ? m.TargetPath : m.SourcePath), true) };
                if (m.Active)
                    acts.Add(WpfUI.Btn("一键迁回原位", "secondary", () =>
                    {
                        if (!WpfUI.ShowGuardDialog(this, "L2", "确认一键无损迁回原位", "将先 100% 校验拷贝回原路径 " + m.SourcePath + " 再移除软链接。", new[] { "恢复目标：" + m.SourcePath }, "确认原分区空间充足并迁回", "", "开始迁回")) return;
                        migProgWrap.Visibility = Visibility.Visible;
                        RunBg(() =>
                        {
                            var r = SafeRelocator.RestoreMigration(m.Id, (txt, pct) => OnUI(() => { migProgBar.Value = pct; migProgTxt.Text = txt; }));
                            OnUI(() => { migProgWrap.Visibility = Visibility.Collapsed; App.RefreshStats(); RefreshDrives(); ShowBanner(r.Ok, r.Title + "：" + r.Summary); ReloadMigrations(); });
                        });
                    }, true));
                migListPanel.Children.Add(WpfUI.MakeRow(null, m.SourcePath + " → " + m.TargetPath, m.Time + " · " + m.FileCount + " 个文件", Util.Fmt(m.Bytes), tags, acts.ToArray()));
            }
        }

        // ==================== PAGE 6: Docker 与 WSL2 调优 ====================
        bool dockerLoaded = false, wslLoaded = false;
        DockerOverview dockerOv;
        string dockerSubTab = "images";
        HashSet<string> dockerSel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TextBlock dockerTitleTxt, dockerSubTxt, dockerProgTxt;
        Border dockerProgWrap;
        ProgressBar dockerProgBar;
        WrapPanel dockerRunChips, dockerSubTabRow;
        UniformGrid dockerKpiGrid;
        StackPanel dockerListPanel, wslHostPanel;
        WslTunerState wslSt;

        UIElement BuildDockerPage()
        {
            var sp = new StackPanel();
            var hSp = new StackPanel();
            var hTop = new Grid();
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hL = new StackPanel();
            dockerTitleTxt = WpfUI.Txt("Docker 虚拟磁盘与 WSL2 限流中心", 17, true, WpfUI.Pal.Text);
            dockerSubTxt = WpfUI.TxtWrap("识别 docker_data.vhdx 物理占用与 WSL 内部实占，支持安全清缓存、fstrim 瘦身压缩，并限制 VmmemWSL 内存上限", 12, WpfUI.Pal.Text2);
            dockerSubTxt.Margin = new Thickness(0, 4, 12, 0);
            hL.Children.Add(dockerTitleTxt); hL.Children.Add(dockerSubTxt);
            var hActs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bR = WpfUI.Btn("刷新", "secondary", () => LoadDockerAsync(false)); bR.Margin = new Thickness(0, 0, 6, 0);
            var bP = WpfUI.Btn("清构建缓存", "secondary", AskDockerPrune); bP.Margin = new Thickness(0, 0, 6, 0);
            var bC = WpfUI.Btn("VHDX 瘦身向导", "warn", AskDockerCompact);
            hActs.Children.Add(bR); hActs.Children.Add(bP); hActs.Children.Add(bC);
            Grid.SetColumn(hActs, 1);
            hTop.Children.Add(hL); hTop.Children.Add(hActs);
            hSp.Children.Add(hTop);

            dockerProgBar = new ProgressBar { Height = 6, Maximum = 100, Foreground = WpfUI.Pal.Accent, Background = WpfUI.Pal.Track, BorderThickness = new Thickness(0) };
            dockerProgTxt = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text2); dockerProgTxt.Margin = new Thickness(0, 4, 0, 0);
            var dp = new StackPanel(); dp.Children.Add(dockerProgBar); dp.Children.Add(dockerProgTxt);
            dockerProgWrap = new Border { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = dp };
            hSp.Children.Add(dockerProgWrap);

            dockerRunChips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            hSp.Children.Add(dockerRunChips);
            var hc = WpfUI.HeroCard(hSp); hc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(hc);

            dockerKpiGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 13) };
            sp.Children.Add(dockerKpiGrid);

            var dPanelSp = new StackPanel();
            var dtb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            dtb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dtb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dockerSubTabRow = new WrapPanel();
            var dActs = new StackPanel { Orientation = Orientation.Horizontal };
            var bVol = WpfUI.Btn("查卷大小(WSL)", "soft", () => LoadDockerAsync(true), true); bVol.Margin = new Thickness(0, 0, 6, 0);
            var bDel = WpfUI.Btn("删除所选", "danger", AskDockerDelSelected, true);
            dActs.Children.Add(bVol); dActs.Children.Add(bDel);
            Grid.SetColumn(dActs, 1);
            dtb.Children.Add(dockerSubTabRow); dtb.Children.Add(dActs);
            dPanelSp.Children.Add(dtb);
            dPanelSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            dockerListPanel = new StackPanel();
            dPanelSp.Children.Add(new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = dockerListPanel });
            var dc = WpfUI.Card(dPanelSp, new Thickness(0), 13); dc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(dc);

            wslHostPanel = new StackPanel();
            sp.Children.Add(WpfUI.Card(wslHostPanel));
            return sp;
        }

        void LoadDockerAsync(bool withVols)
        {
            dockerLoaded = true;
            if (dockerProgWrap != null) { dockerProgWrap.Visibility = Visibility.Visible; dockerProgBar.Value = 45; dockerProgTxt.Text = withVols ? "正在统计 Docker 卷体积…" : "正在读取 Docker 引擎状态…"; }
            RunBg(() => { dockerOv = DockerManager.Inspect(App.Cfg, withVols); OnUI(RenderDockerUI); });
        }

        void RenderDockerUI()
        {
            if (dockerKpiGrid == null || dockerOv == null) return;
            dockerProgWrap.Visibility = Visibility.Collapsed;
            dockerRunChips.Children.Clear();
            dockerKpiGrid.Children.Clear();
            dockerSubTabRow.Children.Clear();
            dockerListPanel.Children.Clear();

            if (!dockerOv.Installed || !dockerOv.Running)
            {
                dockerTitleTxt.Text = string.IsNullOrEmpty(dockerOv.Error) ? "Docker 引擎未运行" : dockerOv.Error;
                dockerListPanel.Children.Add(new Border { Padding = new Thickness(20), Child = WpfUI.Txt("请先启动 Docker Desktop 后点击刷新", 12.6, false, WpfUI.Pal.Text2) });
                return;
            }

            dockerTitleTxt.Text = dockerOv.CompactReclaimableBytes > 500L * 1048576
                ? ("VHDX 虚拟磁盘可压缩归还约 " + Util.Fmt(dockerOv.CompactReclaimableBytes))
                : ("Docker 运行正常（VHDX " + Util.Fmt(dockerOv.VhdxPhysicalBytes) + "）");
            dockerSubTxt.Text = string.Format("物理文件 {0} · WSL 内部实占 {1} · {2}", Util.Fmt(dockerOv.VhdxPhysicalBytes), Util.Fmt(dockerOv.VhdxInternalUsedBytes), dockerOv.VhdxPath);

            foreach (var c in dockerOv.RunningContainers)
            {
                var ch = WpfUI.PillBadge("● " + c.Name, WpfUI.Pal.OkSoft, WpfUI.Pal.Ok);
                ch.Margin = new Thickness(0, 0, 6, 4);
                dockerRunChips.Children.Add(ch);
            }

            long imgBytes = dockerOv.UnusedImages.Sum(x => x.SizeBytes);
            long volBytes = dockerOv.DanglingVolumes.Sum(x => x.SizeBytes);
            var dk1 = WpfUI.KpiCard("VHDX 物理 / 内部", Util.Fmt(dockerOv.VhdxPhysicalBytes), "内部 " + Util.Fmt(dockerOv.VhdxInternalUsedBytes)); dk1.Margin = new Thickness(0, 0, 8, 0);
            var dk2 = WpfUI.KpiCard("构建缓存", Util.Fmt(dockerOv.BuildCacheBytes), "随时安全清空", WpfUI.Pal.Accent); dk2.Margin = new Thickness(0, 0, 8, 0);
            var dk3 = WpfUI.KpiCard("未使用镜像 (" + dockerOv.UnusedImages.Count + ")", Util.Fmt(imgBytes), "已排除运行中容器", WpfUI.Pal.Warn); dk3.Margin = new Thickness(0, 0, 8, 0);
            var dk4 = WpfUI.KpiCard("孤立数据卷 (" + dockerOv.DanglingVolumes.Count + ")", volBytes > 0 ? Util.Fmt(volBytes) : (dockerOv.DanglingVolumes.Count + " 个"), "删前须 L3 确认", WpfUI.Pal.Crit);
            dockerKpiGrid.Children.Add(dk1); dockerKpiGrid.Children.Add(dk2); dockerKpiGrid.Children.Add(dk3); dockerKpiGrid.Children.Add(dk4);

            dockerSubTabRow.Children.Add(WpfUI.PillBtn("未使用镜像 (" + dockerOv.UnusedImages.Count + ")", dockerSubTab == "images", () => { dockerSubTab = "images"; dockerSel.Clear(); RenderDockerUI(); }));
            dockerSubTabRow.Children.Add(WpfUI.PillBtn("已停止容器 (" + dockerOv.StoppedContainers.Count + ")", dockerSubTab == "containers", () => { dockerSubTab = "containers"; dockerSel.Clear(); RenderDockerUI(); }));
            dockerSubTabRow.Children.Add(WpfUI.PillBtn("孤立数据卷 (" + dockerOv.DanglingVolumes.Count + ")", dockerSubTab == "volumes", () => { dockerSubTab = "volumes"; dockerSel.Clear(); RenderDockerUI(); }));

            if (dockerSubTab == "images")
            {
                foreach (var img in dockerOv.UnusedImages.Take(80))
                {
                    var x = img;
                    var cb = new CheckBox { IsChecked = dockerSel.Contains(x.Id), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) dockerSel.Add(x.Id); else dockerSel.Remove(x.Id); };
                    dockerListPanel.Children.Add(WpfUI.MakeRow(cb, x.RepoTag, "ID " + x.Id + " · " + x.CreatedSince, Util.Fmt(x.SizeBytes), new UIElement[] { WpfUI.Badge(x.IsDangling ? "悬空" : "未使用", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn) }, null));
                }
            }
            else if (dockerSubTab == "containers")
            {
                foreach (var con in dockerOv.StoppedContainers.Take(80))
                {
                    var x = con;
                    var cb = new CheckBox { IsChecked = dockerSel.Contains(x.Id), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) dockerSel.Add(x.Id); else dockerSel.Remove(x.Id); };
                    dockerListPanel.Children.Add(WpfUI.MakeRow(cb, x.Name, x.Image + " · " + x.Status, "已退出", null, null));
                }
            }
            else
            {
                foreach (var vol in dockerOv.DanglingVolumes.Take(80))
                {
                    var x = vol;
                    var cb = new CheckBox { IsChecked = dockerSel.Contains(x.Name), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { if (cb.IsChecked == true) dockerSel.Add(x.Name); else dockerSel.Remove(x.Name); };
                    dockerListPanel.Children.Add(WpfUI.MakeRow(cb, x.Name, "无容器挂载的孤立卷（删前请核对是否含旧库数据）", x.SizeBytes > 0 ? Util.Fmt(x.SizeBytes) : "未统计", new UIElement[] { WpfUI.Badge(x.ProjectHint, WpfUI.Pal.CritSoft, WpfUI.Pal.Crit) }, null));
                }
            }
        }

        void AskDockerPrune()
        {
            if (!WpfUI.ShowGuardDialog(this, "L2", "清理 Docker 构建缓存与悬空镜像", "绝不停止或删除任何容器与数据卷。", new[] { "Build Cache 与 <none> 悬空镜像" }, "确认清理", "", "确认清理")) return;
            RunBg(() => { var sb = new StringBuilder(); long f = DockerManager.PruneSafeCache(App.Cfg, sb); OnUI(() => { ShowBanner(true, "Docker 内部缓存已释放 " + Util.Fmt(f) + "（执行 VHDX 压缩后归还给宿主机）"); LoadDockerAsync(false); }); });
        }

        void AskDockerDelSelected()
        {
            var keys = dockerSel.ToList();
            if (keys.Count == 0) { ShowBanner(false, "请先勾选要删除的项目"); return; }
            bool isVol = dockerSubTab == "volumes";
            if (!WpfUI.ShowGuardDialog(this, isVol ? "L3" : "L2", isVol ? ("永久删除 " + keys.Count + " 个 Docker 数据卷") : ("删除 " + keys.Count + " 项 Docker 资源"),
                isVol ? "孤立卷可能包含旧数据库数据，删除后彻底无法恢复！" : "不会影响运行中容器。", keys, "我已核对清单并确认删除", isVol ? "确认删除" : "", "确认删除")) return;
            RunBg(() =>
            {
                var sb = new StringBuilder(); int n = 0;
                if (dockerSubTab == "volumes") n = DockerManager.RemoveDanglingVolumes(App.Cfg, keys, sb);
                else if (dockerSubTab == "images") n = DockerManager.RemoveUnusedImages(App.Cfg, keys, sb);
                else n = DockerManager.RemoveStoppedContainers(App.Cfg, keys, sb);
                dockerSel.Clear();
                OnUI(() => { ShowBanner(true, "已删除 " + n + " 项 Docker 资源"); LoadDockerAsync(false); });
            });
        }

        void AskDockerCompact()
        {
            var running = dockerOv != null ? dockerOv.RunningContainers.Select(c => "运行中：" + c.Name).ToList() : new List<string>();
            if (!WpfUI.ShowGuardDialog(this, "L3", "L3 停机操作：fstrim + VHDX 虚拟磁盘压缩",
                "将按顺序执行 WSL fstrim → 停止 Docker & WSL → 管理员 diskpart 压缩 → 重启 Docker 并自动恢复运行中容器。",
                running.Count > 0 ? running : new List<string> { "当前无运行中容器" },
                "允许短暂中断并执行 VHDX 压缩", "开始压缩", "开始瘦身")) return;
            dockerProgWrap.Visibility = Visibility.Visible;
            RunBg(() =>
            {
                var sb = new StringBuilder();
                var r = DockerManager.CompactVhdx(App.Cfg, (txt, pct) => OnUI(() => { dockerProgBar.Value = pct; dockerProgTxt.Text = txt; }), sb);
                OnUI(() => { dockerProgWrap.Visibility = Visibility.Collapsed; App.RefreshStats(); RefreshDrives(); ShowBanner(r.Success, r.Message); LoadDockerAsync(false); });
            });
        }

        void LoadWslAsync()
        {
            wslLoaded = true;
            RunBg(() => { wslSt = WslTuner.Read(); OnUI(RenderWslUI); });
        }

        void RenderWslUI()
        {
            if (wslHostPanel == null || wslSt == null) return;
            wslHostPanel.Children.Clear();
            wslHostPanel.Children.Add(WpfUI.Txt("WSL2 内存限流 (.wslconfig) 与休眠 / 虚拟内存调优", 14, true, WpfUI.Pal.Text));

            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
            int mem = wslSt.MemoryGB, cpu = wslSt.Processors, swap = wslSt.SwapGB;
            string reclaim = wslSt.AutoMemoryReclaim;
            bool sparse = wslSt.SparseVhd;

            var left = new StackPanel();
            left.Children.Add(WpfUI.Txt(string.Format("物理内存 {0} GB / {1} 核 · {2}", wslSt.PhysicalRamGB, wslSt.LogicalCores, wslSt.ConfigExists ? "已配置 .wslconfig" : "尚未限流"), 11.8, false, WpfUI.Pal.Text2));
            left.Children.Add(MakeSettingLine("WSL2 最大内存上限", WpfUI.Stepper(mem, 1, 2, Math.Max(4, wslSt.PhysicalRamGB - 2), " GB", v => mem = v)));
            left.Children.Add(MakeSettingLine("分配逻辑核心数", WpfUI.Stepper(cpu, 1, 1, wslSt.LogicalCores, " 核", v => cpu = v)));
            left.Children.Add(MakeSettingLine("Swap 交换上限", WpfUI.Stepper(swap, 1, 0, 32, " GB", v => swap = v)));
            left.Children.Add(MakeSettingLine("稀疏虚拟磁盘 (sparseVhd)", WpfUI.SwitchToggle(sparse, v => sparse = v)));
            var saveWsl = WpfUI.Btn("保存 .wslconfig", "primary", () => RunBg(() => { var r = WslTuner.SaveConfig(mem, cpu, swap, reclaim, sparse); OnUI(() => { ShowBanner(r.Ok, r.Title + "：" + r.Summary); LoadWslAsync(); }); }), true);
            saveWsl.HorizontalAlignment = HorizontalAlignment.Right; saveWsl.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(saveWsl);
            var lBox = new Border { Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(13), Margin = new Thickness(0, 0, 10, 0), Child = left };

            var right = new StackPanel();
            bool hasHiber = wslSt.HiberfilBytes > 0;
            var hCard = new StackPanel();
            hCard.Children.Add(WpfUI.Txt("休眠文件 (C:\\hiberfil.sys) · " + (hasHiber ? Util.Fmt(wslSt.HiberfilBytes) : "已关闭"), 12.6, true, hasHiber ? WpfUI.Pal.Warn : WpfUI.Pal.Ok));
            hCard.Children.Add(WpfUI.TxtWrap("无需休眠时关闭可立刻释放 C 盘数 GB 空间（不影响睡眠与关机）", 11.4, WpfUI.Pal.Text2));
            var bHib = WpfUI.Btn(hasHiber ? "关闭休眠释放 C 盘" : "重新开启休眠", "secondary", () =>
            {
                if (!WpfUI.ShowGuardDialog(this, "L2", hasHiber ? "关闭 Windows 休眠" : "开启 Windows 休眠", "将通过 UAC 管理员权限执行 powercfg /h " + (hasHiber ? "off" : "on") + "。", new[] { "目标：C:\\hiberfil.sys" }, "确认执行", "", "确认")) return;
                RunBg(() => { var r = WslTuner.ToggleHibernation(!hasHiber); OnUI(() => { App.RefreshStats(); RefreshDrives(); ShowBanner(r.Ok, r.Summary); LoadWslAsync(); }); });
            }, true);
            bHib.HorizontalAlignment = HorizontalAlignment.Right; bHib.Margin = new Thickness(0, 6, 0, 0);
            hCard.Children.Add(bHib);
            right.Children.Add(new Border { Background = WpfUI.Pal.Surface, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Margin = new Thickness(0, 0, 0, 9), Child = hCard });

            var pCard = new StackPanel();
            pCard.Children.Add(WpfUI.Txt("虚拟内存分页文件 (pagefile.sys) · " + Util.Fmt(wslSt.PagefileTotalBytes), 12.6, true, WpfUI.Pal.Accent));
            pCard.Children.Add(WpfUI.TxtWrap(wslSt.PagefilePaths.Count > 0 ? ("位置：" + string.Join(" | ", wslSt.PagefilePaths.ToArray())) : "由系统自动托管", 11.4, WpfUI.Pal.Text2));
            var bPg = WpfUI.Btn("打开 Windows 虚拟内存设置", "secondary", () => WslTuner.OpenVirtualMemoryDialog(), true);
            bPg.HorizontalAlignment = HorizontalAlignment.Right; bPg.Margin = new Thickness(0, 6, 0, 0);
            pCard.Children.Add(bPg);
            right.Children.Add(new Border { Background = WpfUI.Pal.Surface, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Child = pCard });

            var rBox = new Border { Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(13), Child = right };
            grid.Children.Add(lBox); grid.Children.Add(rBox);
            wslHostPanel.Children.Add(grid);
        }

        Grid MakeSettingLine(string label, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(WpfUI.Txt(label, 12.3, false, WpfUI.Pal.Text));
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        string PickFolderDialog(string initial)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) ? initial : "" })
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : "";
        }

        string PickFileDialog()
        {
            using (var dlg = new System.Windows.Forms.OpenFileDialog { Title = "选择要安全搬家的大文件", Filter = "所有文件 (*.*)|*.*" })
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : "";
        }
    }
}
