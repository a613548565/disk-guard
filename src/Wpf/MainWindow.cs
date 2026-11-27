// DiskGuard v9.0 - macOS / iOS 18 极简 5 入口主工作台（智能体检首页 + 深度瘦身 + 软件与搬家 + 加速与修复 + 设置与更新）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DiskGuard
{
    public partial class MainWindow : Window
    {
        public readonly TrayApp App;
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, UIElement> pages = new Dictionary<string, UIElement>();

        Grid rootShell;
        Border sidebarBorder, topbarBorder, miniDrivesBorder, globalBanner, updateBanner;
        StackPanel navRoot, miniDrivesPanel;
        TextBlock topTitle, topSub, globalBannerTxt, updateBannerTxt, navReclaimBadgeTxt;
        Border navReclaimBadge;
        Button btnThemeQuick, btnTopUpdate;
        ScrollViewer mainScroll;
        ContentPresenter pageHost;
        string currentTab = "home";
        UpdateInfo latestUpdate;

        // 公共共享状态
        CancellationTokenSource spaceScanCts;
        DispatcherTimer perfTimer;
        bool perfManualPaused = false;
        bool winActive = true;

        // 分段子页状态
        string cleanSeg = "cache";  // cache | user | dups | chat | space
        string appsSeg = "caches";  // caches | migrate | uninstall | docker
        string sysSeg = "perf";     // perf | net | env | tools

        static readonly Dictionary<string, string[]> PageMeta = new Dictionary<string, string[]>
        {
            { "home",     new[] { "智能体检", "一键全面诊断磁盘、内存、网络与环境变量，安全项一次修完" } },
            { "clean",    new[] { "深度瘦身", "L1 安全缓存 · User 专清 · SHA-256 重复大文件 · 微信/QQ 附件" } },
            { "apps",     new[] { "软件与搬家", "默认缓存改位 · 零丢失大文件搬家 · 软件卸载 · Docker 与 WSL2" } },
            { "sys",      new[] { "加速与修复", "低耗性能监控 · DNS 网络测速 · 环境变量整理 · PowerShell 7 与修复" } },
            { "settings", new[] { "设置与更新", "空间变化趋势 · L1 智能护航策略 · 自动更新与更新日志" } }
        };

        public MainWindow(TrayApp app, int initialTab = 0)
        {
            this.App = app;
            WpfUI.SyncPalette(Theme.Current != null && Theme.Current.IsDark);

            Title = "DiskGuard v" + Updater.CurrentVersion;
            Width = 1120;
            Height = 740;
            MinWidth = 920;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = WpfUI.FontSans;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            BuildShell();
            SourceInitialized += (s, e) => SyncDwmTheme();
            Activated += (s, e) => { winActive = true; SyncPerfTimerState(); };
            Deactivated += (s, e) => { winActive = false; SyncPerfTimerState(); };
            StateChanged += (s, e) => SyncPerfTimerState();
            Closing += (s, e) =>
            {
                StopPerfTimer();
                if (spaceScanCts != null) { try { spaceScanCts.Cancel(); } catch { } }
            };

            string initKey = TabKeyFromIndex(initialTab);
            GoTab(initKey);
            RefreshDrives();
            StartSmartCheckupAsync();
            StartSilentUpdateCheckAsync();
        }

        public static string TabKeyFromIndex(int idx)
        {
            if (idx == 1) return "sys";
            if (idx == 2) return "clean";
            if (idx == 3) return "apps";
            if (idx == 4) return "settings";
            return "home";
        }

        public void SyncDwmTheme()
        {
            try
            {
                bool dark = WpfUI.Pal.IsDark;
                var h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;
                var bg = dark ? System.Drawing.Color.FromArgb(28, 28, 32) : System.Drawing.Color.FromArgb(245, 245, 247);
                var br = dark ? System.Drawing.Color.FromArgb(58, 58, 66) : System.Drawing.Color.FromArgb(220, 220, 226);
                Win32Native.ApplyDwmTheme(h, dark, bg, br);
            }
            catch { }
        }

        void BuildShell()
        {
            Background = WpfUI.Pal.Bg;
            rootShell = new Grid { Background = WpfUI.Pal.Bg };
            rootShell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            rootShell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ===== 左侧 macOS 极简 5 入口导航栏 =====
            var sideDock = new DockPanel { LastChildFill = true };

            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            miniDrivesPanel = new StackPanel();
            miniDrivesBorder = new Border
            {
                Background = WpfUI.Pal.Surface, BorderBrush = WpfUI.Pal.Border,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(11, 9, 11, 9), Margin = new Thickness(0, 0, 0, 8),
                Effect = WpfUI.SoftShadow(10, 2, 0.04), Child = miniDrivesPanel
            };
            footer.Children.Add(miniDrivesBorder);
            footer.Children.Add(MakeAppleNavItem("settings", "设置与更新", "版本与自动守护", WpfUI.Pal.Text3, "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 0 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 0 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 0 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 0 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"));
            DockPanel.SetDock(footer, Dock.Bottom);
            sideDock.Children.Add(footer);

            navRoot = new StackPanel();
            // Brand Header
            var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 16) };
            var mark = WpfUI.SquircleIcon("M12 2L20 5.5V12.5C20 17.5 16.5 20.8 12 22C7.5 20.8 4 17.5 4 12.5V5.5L12 2Z", WpfUI.Pal.Accent, 36, 18);
            mark.Margin = new Thickness(0, 0, 10, 0);
            mark.Effect = WpfUI.SoftShadow(10, 2, 0.14);
            var bTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var bTop = new StackPanel { Orientation = Orientation.Horizontal };
            var bName = WpfUI.Txt("DiskGuard", 15.5, true, WpfUI.Pal.Text); bName.Margin = new Thickness(0, 0, 6, 0);
            bTop.Children.Add(bName);
            bTop.Children.Add(WpfUI.PillBadge("v" + Updater.CurrentVersion, WpfUI.Pal.AccentSoft, WpfUI.Pal.Accent));
            bTxt.Children.Add(bTop);
            bTxt.Children.Add(WpfUI.Txt("原生 DirectX 空间管家", 10.6, false, WpfUI.Pal.Text3));
            brandRow.Children.Add(mark); brandRow.Children.Add(bTxt);
            navRoot.Children.Add(brandRow);

            // 4 个核心彩色 Squircle 主入口（极简、清晰、零学习成本）
            navRoot.Children.Add(MakeAppleNavItem("home", "智能体检", "一键诊断与修复", WpfUI.Pal.Accent, "M12 2L20 5.5V12.5C20 17.5 16.5 20.8 12 22C7.5 20.8 4 17.5 4 12.5V5.5L12 2Z M9 12l2 2 4-4"));
            navRoot.Children.Add(MakeAppleNavItem("clean", "深度瘦身", "缓存 · User · 去重", WpfUI.Pal.Ok, "M12 3v18M3 12h18M5.6 5.6l12.8 12.8M18.4 5.6L5.6 18.4", true));
            navRoot.Children.Add(MakeAppleNavItem("apps", "软件与搬家", "缓存改位 · 搬家 · Docker", WpfUI.Pal.Warn, "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"));
            navRoot.Children.Add(MakeAppleNavItem("sys", "加速与修复", "性能 · 网络 · 环境变量", WpfUI.Pal.Indigo, "M22 12h-4l-3 9L9 3l-3 9H2"));

            sideDock.Children.Add(navRoot);
            sidebarBorder = new Border
            {
                Background = WpfUI.Pal.Sidebar, BorderBrush = WpfUI.Pal.Border,
                BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 12, 12, 12),
                Child = sideDock
            };
            Grid.SetColumn(sidebarBorder, 0);
            rootShell.Children.Add(sidebarBorder);

            // ===== 右侧工作台 =====
            var rightDock = new DockPanel { LastChildFill = true };

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            topTitle = WpfUI.Txt("智能体检", 19, true, WpfUI.Pal.Text);
            topSub = WpfUI.Txt("", 11.8, false, WpfUI.Pal.Text3);
            heading.Children.Add(topTitle); heading.Children.Add(topSub);
            Grid.SetColumn(heading, 0); topGrid.Children.Add(heading);

            var topActs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnTopUpdate = WpfUI.PillBtn("有新版本", true, () => { if (latestUpdate != null) WpfUI.ShowUpdateDialog(this, latestUpdate); else GoTab("settings"); });
            btnTopUpdate.Visibility = Visibility.Collapsed;
            btnTopUpdate.Margin = new Thickness(0, 0, 8, 0);
            btnThemeQuick = WpfUI.Btn(WpfUI.Pal.IsDark ? "☀ 浅色" : "☾ 深色", "secondary", ToggleThemeQuick, true);
            btnThemeQuick.Margin = new Thickness(0, 0, 7, 0);
            var btnRef = WpfUI.Btn("↻ 刷新", "secondary", () => { App.RefreshStats(); RefreshDrives(); ShowBanner(true, "已刷新全部分区状态"); }, true);
            topActs.Children.Add(btnTopUpdate); topActs.Children.Add(btnThemeQuick); topActs.Children.Add(btnRef);
            Grid.SetColumn(topActs, 1); topGrid.Children.Add(topActs);

            topbarBorder = new Border
            {
                Height = 64, Background = WpfUI.Pal.Bg, BorderBrush = WpfUI.Pal.Border,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(26, 0, 26, 0),
                Child = topGrid
            };
            DockPanel.SetDock(topbarBorder, Dock.Top);
            rightDock.Children.Add(topbarBorder);

            var bodyStack = new StackPanel { MaxWidth = 960, Margin = new Thickness(26, 18, 26, 28) };

            // 自动更新顶部醒目提示条（检测到新版本时自动弹出）
            var updGrid = new Grid();
            updGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            updateBannerTxt = WpfUI.TxtWrap("", 12.6, WpfUI.Pal.Text);
            updateBannerTxt.VerticalAlignment = VerticalAlignment.Center;
            var uActs = new StackPanel { Orientation = Orientation.Horizontal };
            var bViewUpd = WpfUI.Btn("查看更新内容并升级", "primary", () => { if (latestUpdate != null) WpfUI.ShowUpdateDialog(this, latestUpdate); }, true);
            bViewUpd.Margin = new Thickness(0, 0, 6, 0);
            var bDisUpd = WpfUI.Btn("稍后", "soft", () => updateBanner.Visibility = Visibility.Collapsed, true);
            uActs.Children.Add(bViewUpd); uActs.Children.Add(bDisUpd);
            Grid.SetColumn(uActs, 1);
            updGrid.Children.Add(updateBannerTxt); updGrid.Children.Add(uActs);
            updateBanner = new Border
            {
                Background = WpfUI.Pal.AccentSoft, BorderBrush = WpfUI.Pal.Accent,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(15, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14),
                Visibility = Visibility.Collapsed, Child = updGrid
            };
            bodyStack.Children.Add(updateBanner);

            // 全局结果通知条
            var banGrid = new Grid();
            banGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            banGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            globalBannerTxt = WpfUI.TxtWrap("", 12.5, WpfUI.Pal.Text);
            globalBannerTxt.VerticalAlignment = VerticalAlignment.Center;
            var banClose = WpfUI.Btn("关闭", "soft", () => globalBanner.Visibility = Visibility.Collapsed, true);
            Grid.SetColumn(banClose, 1);
            banGrid.Children.Add(globalBannerTxt); banGrid.Children.Add(banClose);
            globalBanner = new Border
            {
                Background = WpfUI.Pal.OkSoft, BorderBrush = WpfUI.Pal.Ok,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 9, 11, 9), Margin = new Thickness(0, 0, 0, 14),
                Visibility = Visibility.Collapsed, Child = banGrid
            };
            bodyStack.Children.Add(globalBanner);

            pageHost = new ContentPresenter();
            bodyStack.Children.Add(pageHost);

            mainScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = bodyStack
            };
            rightDock.Children.Add(mainScroll);

            Grid.SetColumn(rightDock, 1);
            rootShell.Children.Add(rightDock);
            Content = rootShell;
            RenderMiniDrives();
        }

        Button MakeAppleNavItem(string key, string title, string sub, Brush iconBg, string iconPath, bool withBadge = false)
        {
            var g = new Grid { Margin = new Thickness(2, 4, 2, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ico = WpfUI.SquircleIcon(iconPath, iconBg, 28, 14);
            ico.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(ico, 0);

            var txtSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txtSp.Children.Add(WpfUI.Txt(title, 13.2, true, WpfUI.Pal.Text));
            txtSp.Children.Add(WpfUI.Txt(sub, 10.6, false, WpfUI.Pal.Text3));
            Grid.SetColumn(txtSp, 1);

            g.Children.Add(ico); g.Children.Add(txtSp);
            if (withBadge)
            {
                navReclaimBadgeTxt = WpfUI.Txt("", 10, true, WpfUI.Pal.Ok, true);
                navReclaimBadge = new Border
                {
                    Background = WpfUI.Pal.OkSoft, CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 1.5, 6, 1.5), Visibility = Visibility.Collapsed,
                    VerticalAlignment = VerticalAlignment.Center, Child = navReclaimBadgeTxt
                };
                Grid.SetColumn(navReclaimBadge, 2); g.Children.Add(navReclaimBadge);
            }

            var btn = new Button
            {
                Content = g, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 2, 0, 2), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "nbd"; bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            tpl.VisualTree = bd;
            var hv = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hv.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9, "nbd"));
            tpl.Triggers.Add(hv);
            btn.Template = tpl;
            btn.Click += (s, e) => GoTab(key);
            navButtons[key] = btn;
            return btn;
        }

        public void SwitchPage(int idx) { GoTab(TabKeyFromIndex(idx)); }

        // 兼容旧代码中的页面跳转调用
        public void GoTab(string key)
        {
            if (key == "user") { cleanSeg = "user"; key = "clean"; }
            else if (key == "space") { cleanSeg = "space"; key = "clean"; }
            else if (key == "migrate") { appsSeg = "migrate"; key = "apps"; }
            else if (key == "docker") { appsSeg = "docker"; key = "apps"; }
            else if (key == "perf") { sysSeg = "perf"; key = "sys"; }
            else if (key == "sysfix") { sysSeg = "env"; key = "sys"; }

            currentTab = key;
            foreach (var kv in navButtons)
            {
                bool active = kv.Key == key;
                kv.Value.Background = active ? WpfUI.Pal.Surface : Brushes.Transparent;
                kv.Value.BorderBrush = active ? WpfUI.Pal.Border : Brushes.Transparent;
                kv.Value.Effect = active ? WpfUI.SoftShadow(12, 2, 0.06) : null;
            }
            if (PageMeta.ContainsKey(key)) { topTitle.Text = PageMeta[key][0]; topSub.Text = PageMeta[key][1]; }

            pages[key] = CreateUnifiedPage(key);
            pageHost.Content = pages[key];
            mainScroll.ScrollToTop();
            SyncPerfTimerState();
        }

        UIElement CreateUnifiedPage(string key)
        {
            if (key == "home") return BuildSmartHomePage();
            if (key == "clean") return BuildUnifiedCleanHub();
            if (key == "apps") return BuildUnifiedAppsHub();
            if (key == "sys") return BuildUnifiedSysHub();
            return BuildSettingsPage();
        }

        UIElement BuildUnifiedCleanHub()
        {
            var sp = new StackPanel();
            var segs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("cache", "安全缓存清理"),
                new KeyValuePair<string, string>("user", "User 专清清单"),
                new KeyValuePair<string, string>("space", "大文件 / 重复文件 / 微信 QQ")
            };
            var bar = WpfUI.SegmentedBar(segs, cleanSeg, k => { cleanSeg = k; GoTab("clean"); });
            bar.Margin = new Thickness(0, 0, 0, 15);
            sp.Children.Add(bar);

            if (cleanSeg == "user") { sp.Children.Add(BuildUserPage()); if (!userLoaded) ScanUserFolderAsync(); }
            else if (cleanSeg == "space") sp.Children.Add(BuildSpacePage());
            else { sp.Children.Add(BuildCleanPage()); if (cleanItems.Count == 0) StartCleanScanAsync(false); }
            return sp;
        }

        UIElement BuildUnifiedAppsHub()
        {
            var sp = new StackPanel();
            var segs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("caches", "默认缓存改位"),
                new KeyValuePair<string, string>("migrate", "大文件安全搬家"),
                new KeyValuePair<string, string>("uninstall", "软件卸载与残留"),
                new KeyValuePair<string, string>("docker", "Docker 与 WSL2")
            };
            var bar = WpfUI.SegmentedBar(segs, appsSeg, k => { appsSeg = k; GoTab("apps"); });
            bar.Margin = new Thickness(0, 0, 0, 15);
            sp.Children.Add(bar);

            if (appsSeg == "migrate") sp.Children.Add(BuildMigratePage());
            else if (appsSeg == "docker") { sp.Children.Add(BuildDockerPage()); if (!dockerLoaded) LoadDockerAsync(false); if (!wslLoaded) LoadWslAsync(); }
            else if (appsSeg == "uninstall") { appsSubTab = "uninstall"; sp.Children.Add(BuildAppsPage()); }
            else { appsSubTab = "caches"; sp.Children.Add(BuildAppsPage()); }
            return sp;
        }

        UIElement BuildUnifiedSysHub()
        {
            var sp = new StackPanel();
            var segs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("perf", "CPU / 内存监控"),
                new KeyValuePair<string, string>("net", "网络 DNS 优化"),
                new KeyValuePair<string, string>("env", "环境变量整理"),
                new KeyValuePair<string, string>("tools", "PS7 升级与系统修复")
            };
            var bar = WpfUI.SegmentedBar(segs, sysSeg, k => { sysSeg = k; GoTab("sys"); });
            bar.Margin = new Thickness(0, 0, 0, 15);
            sp.Children.Add(bar);

            if (sysSeg == "net") { perfSubMode = "network"; sp.Children.Add(BuildPerfPage()); }
            else if (sysSeg == "env") { fixSubMode = "env"; sp.Children.Add(BuildSysFixPage()); }
            else if (sysSeg == "tools") { fixSubMode = "ps7"; sp.Children.Add(BuildSysFixPage()); }
            else { perfSubMode = "monitor"; sp.Children.Add(BuildPerfPage()); }
            return sp;
        }

        void StartSilentUpdateCheckAsync()
        {
            if (!App.Cfg.AutoCheckUpdate || string.IsNullOrWhiteSpace(App.Cfg.UpdateUrl)) return;
            RunBg(() =>
            {
                var u = Updater.Check(App.Cfg.UpdateUrl);
                if (u.Ok && u.HasUpdate)
                {
                    latestUpdate = u;
                    OnUI(() =>
                    {
                        btnTopUpdate.Content = "发现新版本 v" + u.LatestVersion;
                        btnTopUpdate.Visibility = Visibility.Visible;
                        string shortNotes = string.IsNullOrWhiteSpace(u.Notes) ? "" : (" · " + u.Notes.Replace("\r", " ").Replace("\n", " ").Trim());
                        if (shortNotes.Length > 90) shortNotes = shortNotes.Substring(0, 90) + "…";
                        updateBannerTxt.Text = "发现 DiskGuard 新版本 v" + u.LatestVersion + "（当前 v" + u.CurrentVersion + "）" + shortNotes;
                        updateBanner.Visibility = Visibility.Visible;
                    });
                }
            });
        }

        void ToggleThemeQuick() { ApplyTheme(WpfUI.Pal.IsDark ? ThemeMode.Light : ThemeMode.Dark); }

        public void ApplyTheme(ThemeMode mode)
        {
            App.Cfg.Theme = mode; App.Cfg.Save();
            Theme.ApplyMode(mode); App.RebuildMenuTheme();
            WpfUI.SyncPalette(Theme.Current.IsDark);
            SyncDwmTheme();
            pages.Clear(); navButtons.Clear();
            BuildShell();
            RefreshDrives();
            GoTab(currentTab);
        }

        public void ShowBanner(bool ok, string msg)
        {
            OnUI(() =>
            {
                globalBanner.Background = ok ? WpfUI.Pal.OkSoft : WpfUI.Pal.WarnSoft;
                globalBanner.BorderBrush = ok ? WpfUI.Pal.Ok : WpfUI.Pal.Warn;
                globalBannerTxt.Text = (ok ? "✓ " : "⚠ ") + msg;
                globalBanner.Visibility = Visibility.Visible;
            });
        }

        public void RefreshDrives()
        {
            OnUI(() => { RenderMiniDrives(); RenderOverviewDrives(); RenderHomeDrives(); });
        }

        void RenderMiniDrives()
        {
            if (miniDrivesPanel == null) return;
            miniDrivesPanel.Children.Clear();
            foreach (var d in App.Stats.Where(x => x.Ready))
            {
                int lv = d.Level(App.Cfg);
                var col = WpfUI.LevelBrush(lv);
                var row = new Grid { Margin = new Thickness(0, 1, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(WpfUI.Txt(d.Name + ": 盘", 11.2, true, WpfUI.Pal.Text));
                var r = WpfUI.Txt("剩 " + Util.FmtCompact(d.Free), 11.2, true, col, true); Grid.SetColumn(r, 1);
                row.Children.Add(r);
                miniDrivesPanel.Children.Add(row);
                var bar = WpfUI.ProgressTrack(d.UsedRatio * 100.0, col, 4); bar.Margin = new Thickness(0, 0, 0, 5);
                miniDrivesPanel.Children.Add(bar);
            }
        }

        void RunBg(Action work) { ThreadPool.QueueUserWorkItem(_ => { try { work(); } catch { } }); }
        void OnUI(Action act) { try { Dispatcher.BeginInvoke(act); } catch { } }

        public void CaptureSelfTestShots(string outDir)
        {
            Directory.CreateDirectory(outDir);
            // 同步跑完一次智能体检，让首页截图展示真实健康分与问题列表
            RunSmartCheckupSyncForShot();
            GoTab("home");
            WpfUI.RenderElementToPng(rootShell, 1120, 740, Path.Combine(outDir, "v9_home_light.png"));

            cleanSeg = "user";
            userData = UserFolderAudit.Scan(CancellationToken.None); userLoaded = true;
            GoTab("clean");
            WpfUI.RenderElementToPng(rootShell, 1120, 740, Path.Combine(outDir, "v9_user_light.png"));

            appsSeg = "migrate";
            GoTab("apps");
            WpfUI.RenderElementToPng(rootShell, 1120, 740, Path.Combine(outDir, "v9_migrate_light.png"));

            ApplyTheme(ThemeMode.Dark);
            sysSeg = "env";
            envData = NetworkEnvManager.InspectEnvVars(); envLoaded = true;
            GoTab("sys");
            WpfUI.RenderElementToPng(rootShell, 1120, 740, Path.Combine(outDir, "v9_sys_dark.png"));
        }
    }
}
