// DiskGuard v9.0 - 原生 WPF 加速排障（CPU/内存监控 + 网络 DNS 优化 + 环境变量整理 + PS7 升级与 6 大修复）& 设置与自动更新
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DiskGuard
{
    public partial class MainWindow
    {
        // ==================== PAGE: 性能监控与网络优化 ====================
        string perfSubMode = "monitor";
        string procTab = "mem";
        SystemSnapshot lastPerf;
        readonly List<double> memHist = new List<double>(), cpuHist = new List<double>();
        Border memGaugeHost, cpuGaugeHost, hungBanner;
        TextBlock perfTitleTxt, memMetaTxt, cpuMetaTxt, hungTxt;
        Canvas memSparkCanvas, cpuSparkCanvas;
        Button btnPerfPause, btnKillHung;
        StackPanel procListPanel, netHostPanel;
        NetworkStatus netData;
        bool netLoaded = false;

        UIElement BuildPerfPage()
        {
            if (perfSubMode == "network")
            {
                netHostPanel = new StackPanel();
                if (!netLoaded) LoadNetworkAsync(); else RenderNetworkUI();
                return netHostPanel;
            }

            var sp = new StackPanel();
            var hSp = new StackPanel();
            var hTop = new Grid();
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hL = new StackPanel();
            perfTitleTxt = WpfUI.Txt("正在采样内存与 CPU 实时负载…", 17.5, true, WpfUI.Pal.Text);
            var hSub = WpfUI.TxtWrap("单次内核调用 NtQuerySystemInformation（失焦自动暂停零占用），安全内存释放绝不关闭文档或软件", 12, WpfUI.Pal.Text2);
            hSub.Margin = new Thickness(0, 4, 12, 0);
            hL.Children.Add(perfTitleTxt); hL.Children.Add(hSub);

            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bExp = WpfUI.Btn("重启任务栏", "secondary", AskRestartExplorer); bExp.Margin = new Thickness(0, 0, 6, 0);
            var bLag = WpfUI.Btn("卡顿急救", "secondary", () => RunBugFixAndNotify("lag-rescue")); bLag.Margin = new Thickness(0, 0, 6, 0);
            var bMem = WpfUI.Btn("一键释放内存", "primary", () => RunBg(() => { var r = SystemHealth.CleanMemory(null); OnUI(() => { ShowBanner(r.Ok, r.Title + "：" + r.Summary); SamplePerfOnceAsync(); }); }));
            acts.Children.Add(bExp); acts.Children.Add(bLag); acts.Children.Add(bMem);
            Grid.SetColumn(acts, 1);
            hTop.Children.Add(hL); hTop.Children.Add(acts);
            hSp.Children.Add(hTop);

            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hungTxt = WpfUI.Txt("", 12.2, true, WpfUI.Pal.Warn);
            btnKillHung = WpfUI.Btn("结束假死窗口", "danger", null, true); Grid.SetColumn(btnKillHung, 1);
            hg.Children.Add(hungTxt); hg.Children.Add(btnKillHung);
            hungBanner = new Border { Background = WpfUI.Pal.WarnSoft, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 10, 8), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = hg };
            hSp.Children.Add(hungBanner);
            var hc = WpfUI.HeroCard(hSp); hc.Margin = new Thickness(0, 0, 0, 13);
            sp.Children.Add(hc);

            // 双仪表 + 波形图
            var gGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 13) };
            memGaugeHost = new Border { Margin = new Thickness(0, 0, 14, 0) };
            memMetaTxt = WpfUI.Txt("读取中…", 12, false, WpfUI.Pal.Text2);
            memSparkCanvas = new Canvas { Height = 54, ClipToBounds = true };
            var mc = BuildGaugeCard("内存实时占用", memGaugeHost, memMetaTxt, memSparkCanvas);
            mc.Margin = new Thickness(0, 0, 7, 0);

            cpuGaugeHost = new Border { Margin = new Thickness(0, 0, 14, 0) };
            cpuMetaTxt = WpfUI.Txt("读取中…", 12, false, WpfUI.Pal.Text2);
            cpuSparkCanvas = new Canvas { Height = 54, ClipToBounds = true };
            var cc = BuildGaugeCard("CPU 实时负载", cpuGaugeHost, cpuMetaTxt, cpuSparkCanvas);
            cc.Margin = new Thickness(7, 0, 0, 0);
            gGrid.Children.Add(mc); gGrid.Children.Add(cc);
            sp.Children.Add(gGrid);

            var pPanel = new StackPanel();
            var ptb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            ptb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ptb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ptabs = new WrapPanel();
            ptabs.Children.Add(WpfUI.PillBtn("内存占用 Top 12", procTab == "mem", () => { procTab = "mem"; RenderProcListUI(); }));
            ptabs.Children.Add(WpfUI.PillBtn("CPU 占用 Top 10", procTab == "cpu", () => { procTab = "cpu"; RenderProcListUI(); }));
            var pActs = new StackPanel { Orientation = Orientation.Horizontal };
            btnPerfPause = WpfUI.Btn(perfManualPaused ? "恢复刷新" : "暂停刷新", "secondary", () => { perfManualPaused = !perfManualPaused; btnPerfPause.Content = perfManualPaused ? "恢复刷新" : "暂停刷新"; SyncPerfTimerState(); }, true);
            btnPerfPause.Margin = new Thickness(0, 0, 6, 0);
            pActs.Children.Add(btnPerfPause);
            pActs.Children.Add(WpfUI.Btn("立即采样", "soft", SamplePerfOnceAsync, true));
            Grid.SetColumn(pActs, 1);
            ptb.Children.Add(ptabs); ptb.Children.Add(pActs);
            pPanel.Children.Add(ptb);
            pPanel.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            procListPanel = new StackPanel();
            pPanel.Children.Add(procListPanel);
            sp.Children.Add(WpfUI.Card(pPanel, new Thickness(0), 15));

            if (lastPerf != null) RenderPerfUI();
            SamplePerfOnceAsync();
            return sp;
        }

        Border BuildGaugeCard(string title, Border gaugeHost, TextBlock metaTxt, Canvas spark)
        {
            var sp = new StackPanel();
            var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.Children.Add(gaugeHost);
            var r = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            r.Children.Add(WpfUI.Txt(title, 13.8, true, WpfUI.Pal.Text));
            metaTxt.Margin = new Thickness(0, 4, 0, 0);
            r.Children.Add(metaTxt);
            Grid.SetColumn(r, 1); top.Children.Add(r);
            sp.Children.Add(top);
            sp.Children.Add(new Border { Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = spark });
            return WpfUI.Card(sp, new Thickness(16, 14, 16, 14), 15);
        }

        void SyncPerfTimerState()
        {
            bool shouldRun = currentTab == "sys" && sysSeg == "perf" && !perfManualPaused && winActive && WindowState != WindowState.Minimized;
            if (shouldRun)
            {
                if (perfTimer == null)
                {
                    perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    perfTimer.Tick += (s, e) => SamplePerfOnceAsync();
                }
                if (!perfTimer.IsEnabled) perfTimer.Start();
            }
            else StopPerfTimer();
        }

        void StopPerfTimer() { if (perfTimer != null && perfTimer.IsEnabled) perfTimer.Stop(); }

        void SamplePerfOnceAsync()
        {
            RunBg(() =>
            {
                var snap = SystemHealth.SampleAll();
                lastPerf = snap;
                OnUI(RenderPerfUI);
            });
        }

        void RenderPerfUI()
        {
            if (lastPerf == null || memGaugeHost == null) return;
            memHist.Add(lastPerf.MemPct); if (memHist.Count > 28) memHist.RemoveAt(0);
            cpuHist.Add(lastPerf.CpuPct); if (cpuHist.Count > 28) cpuHist.RemoveAt(0);

            var mCol = lastPerf.MemPct >= 88 ? WpfUI.Pal.Crit : (lastPerf.MemPct >= 75 ? WpfUI.Pal.Warn : WpfUI.Pal.Accent);
            var cCol = lastPerf.CpuPct >= 85 ? WpfUI.Pal.Crit : (lastPerf.CpuPct >= 65 ? WpfUI.Pal.Warn : WpfUI.Pal.Ok);

            memGaugeHost.Child = WpfUI.RingGauge(74, 7, lastPerf.MemPct, mCol, Math.Round(lastPerf.MemPct) + "%", "内存");
            cpuGaugeHost.Child = WpfUI.RingGauge(74, 7, lastPerf.CpuPct, cCol, Math.Round(lastPerf.CpuPct) + "%", "CPU");
            memMetaTxt.Text = string.Format("已用 {0} / {1} · 剩余 {2}", Util.Fmt(lastPerf.MemUsedBytes), Util.Fmt(lastPerf.MemTotalBytes), Util.Fmt(lastPerf.MemAvailBytes));
            cpuMetaTxt.Text = string.Format("{0} 逻辑核心 · 运行 {1} 个进程", lastPerf.LogicalCores, lastPerf.ProcessCount);
            perfTitleTxt.Text = lastPerf.IsLagging
                ? ("检测到高负载卡顿 · " + lastPerf.LagReason)
                : string.Format("系统流畅 · 内存 {0:0}% · CPU {1:0}%", lastPerf.MemPct, lastPerf.CpuPct);

            DrawSparkline(memSparkCanvas, memHist, mCol);
            DrawSparkline(cpuSparkCanvas, cpuHist, cCol);

            if (lastPerf.HungApps.Count > 0)
            {
                var h = lastPerf.HungApps[0];
                hungTxt.Text = string.Format("检测到 {0} 个未响应假死窗口：{1}", lastPerf.HungApps.Count, h.Name);
                btnKillHung.Click += (s, e) => AskKillProc(h.Pid, h.Name, Util.Fmt(h.WorkingSetBytes));
                hungBanner.Visibility = Visibility.Visible;
            }
            else hungBanner.Visibility = Visibility.Collapsed;

            RenderProcListUI();
        }

        void DrawSparkline(Canvas cv, List<double> vals, Brush stroke)
        {
            cv.Children.Clear();
            double w = cv.ActualWidth > 40 ? cv.ActualWidth : 360, h = 54;
            if (vals.Count < 2) return;
            var poly = new Polyline { Stroke = stroke, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < vals.Count; i++)
            {
                double x = w * i / Math.Max(1, vals.Count - 1);
                double y = h - 4 - (Math.Min(100, Math.Max(0, vals[i])) / 100.0 * (h - 8));
                poly.Points.Add(new Point(x, y));
            }
            cv.Children.Add(poly);
        }

        void RenderProcListUI()
        {
            if (procListPanel == null || lastPerf == null) return;
            procListPanel.Children.Clear();
            bool isMem = procTab == "mem";
            var rows = isMem ? lastPerf.TopMem : lastPerf.TopCpu;
            double maxV = Math.Max(1, rows.Count > 0 ? rows.Max(r => isMem ? (double)r.WorkingSetBytes : r.CpuPct) : 1);
            foreach (var proc in rows)
            {
                var p = proc;
                double pct = Math.Max(2, (isMem ? (double)p.WorkingSetBytes : p.CpuPct) * 100.0 / maxV);
                var tags = new List<UIElement> { WpfUI.Badge("PID " + p.Pid, WpfUI.Pal.Track, WpfUI.Pal.Text2) };
                if (p.IsProtected) tags.Add(WpfUI.Badge("系统锁定", WpfUI.Pal.SurfaceHover, WpfUI.Pal.Text2));
                var acts = new List<UIElement>();
                if (!p.IsProtected)
                {
                    acts.Add(WpfUI.Btn("释放", "soft", () => RunBg(() => { var r = SystemHealth.CleanMemory(p.Pid); OnUI(() => { ShowBanner(r.Ok, r.Summary); SamplePerfOnceAsync(); }); }), true));
                    acts.Add(WpfUI.Btn("降优", "secondary", () => RunBg(() => { var r = SystemHealth.ThrottleProcess(p.Pid); OnUI(() => { ShowBanner(r.Ok, r.Summary); SamplePerfOnceAsync(); }); }), true));
                    acts.Add(WpfUI.Btn("结束", "danger", () => AskKillProc(p.Pid, p.Name, Util.Fmt(p.WorkingSetBytes)), true));
                }
                string sub = string.IsNullOrEmpty(p.WindowTitle) ? string.Format("内存 {0} · CPU {1:0.0}%", Util.Fmt(p.WorkingSetBytes), p.CpuPct) : p.WindowTitle;
                procListPanel.Children.Add(WpfUI.MakeRow(null, p.Name, sub, isMem ? Util.Fmt(p.WorkingSetBytes) : string.Format("{0:0.0}%", p.CpuPct), tags.ToArray(), acts.ToArray(), pct, p.IsProtected ? WpfUI.Pal.Track : WpfUI.Pal.AccentSoft));
            }
        }

        void AskKillProc(int pid, string name, string mem)
        {
            if (!WpfUI.ShowGuardDialog(this, "L2", "结束进程 " + name + " (PID " + pid + ")", "请先保存该程序中的未保存文档。", new[] { name + " · 占用 " + mem }, "确认结束此进程", "", "确认结束")) return;
            RunBg(() => { var r = SystemHealth.KillUserProcess(pid); OnUI(() => { ShowBanner(r.Ok, r.Summary); SamplePerfOnceAsync(); }); });
        }

        // ==================== 网络优化 ====================
        void LoadNetworkAsync()
        {
            netLoaded = true;
            if (netHostPanel != null) { netHostPanel.Children.Clear(); netHostPanel.Children.Add(WpfUI.Card(WpfUI.Txt("正在并发测试 5 组主流 DNS 延迟与系统代理状态…", 13, false, WpfUI.Pal.Text2))); }
            RunBg(() => { netData = NetworkEnvManager.InspectNetwork(); OnUI(RenderNetworkUI); });
        }

        void RenderNetworkUI()
        {
            if (netHostPanel == null || netData == null) return;
            netHostPanel.Children.Clear();
            var kGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 13) };
            var k1 = WpfUI.KpiCard("当前活动网卡与 IP", netData.AdapterName, "IP " + netData.IpAddress + " · DNS " + (netData.CurrentDns.Count > 0 ? string.Join(", ", netData.CurrentDns.ToArray()) : "自动")); k1.Margin = new Thickness(0, 0, 8, 0);
            var k2 = WpfUI.KpiCard("Windows 系统代理状态", netData.ProxyEnabled ? "已开启代理" : "直连无残留", netData.ProxyEnabled ? netData.ProxyServer : "未发现异常代理残留", netData.ProxyEnabled ? WpfUI.Pal.Warn : WpfUI.Pal.Ok); k2.Margin = new Thickness(0, 0, 8, 0);
            var k3 = WpfUI.KpiCard("TCP 自动调谐级别", netData.TcpAutoTuning, "微软推荐 normal 发挥宽带吞吐", WpfUI.Pal.Accent);
            kGrid.Children.Add(k1); kGrid.Children.Add(k2); kGrid.Children.Add(k3);
            netHostPanel.Children.Add(kGrid);

            var actBar = new WrapPanel();
            var b1 = WpfUI.Btn("重新测速 DNS", "secondary", LoadNetworkAsync, true); b1.Margin = new Thickness(0, 0, 7, 0);
            var b2 = WpfUI.Btn("清除残留系统代理", "secondary", () => RunBg(() => { var r = NetworkEnvManager.ClearSystemProxy(); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadNetworkAsync(); }); }), true); b2.Margin = new Thickness(0, 0, 7, 0);
            var b3 = WpfUI.Btn("恢复自动 DNS (DHCP)", "secondary", () => RunBg(() => { var r = NetworkEnvManager.SetAdapterDns(netData.AdapterName, "dhcp", ""); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadNetworkAsync(); }); }), true); b3.Margin = new Thickness(0, 0, 7, 0);
            var b4 = WpfUI.Btn("刷新 DNS/ARP + TCP 优选", "primary", () => RunBg(() => { var r = NetworkEnvManager.OptimizeTcpAndFlush(); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadNetworkAsync(); }); }), true);
            actBar.Children.Add(b1); actBar.Children.Add(b2); actBar.Children.Add(b3); actBar.Children.Add(b4);
            var ac = WpfUI.Card(actBar, new Thickness(15, 11, 15, 11), 14); ac.Margin = new Thickness(0, 0, 0, 13);
            netHostPanel.Children.Add(ac);

            var dnsSp = new StackPanel();
            dnsSp.Children.Add(new Border { Padding = new Thickness(16, 11, 16, 11), BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = WpfUI.Txt("公共 DNS 实时 Ping 测速排行（按延迟从低到高）", 13.5, true, WpfUI.Pal.Text) });
            for (int i = 0; i < netData.DnsBench.Count; i++)
            {
                var d = netData.DnsBench[i];
                bool ok = d.LatencyMs >= 0;
                var tags = new List<UIElement> { WpfUI.Badge(d.Primary + " / " + d.Secondary, WpfUI.Pal.Track, WpfUI.Pal.Text2) };
                if (i == 0 && ok) tags.Add(WpfUI.PillBadge("推荐最快", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok));
                var acts = new UIElement[] { WpfUI.Btn("一键切换", "secondary", () =>
                {
                    if (!WpfUI.ShowGuardDialog(this, "L2", "切换 DNS 至 " + d.Name, "将通过 UAC 授权把网卡「" + netData.AdapterName + "」DNS 设为 " + d.Primary + " / " + d.Secondary + "。", new[] { d.Primary, d.Secondary }, "确认切换 DNS", "", "立即切换")) return;
                    RunBg(() => { var r = NetworkEnvManager.SetAdapterDns(netData.AdapterName, d.Primary, d.Secondary); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadNetworkAsync(); }); });
                }, true) };
                dnsSp.Children.Add(WpfUI.MakeRow(null, d.Name, d.Note, ok ? (d.LatencyMs + " ms") : "超时", tags.ToArray(), acts));
            }
            netHostPanel.Children.Add(WpfUI.Card(dnsSp, new Thickness(0), 15));
        }

        // ==================== PAGE: 环境变量整理 / PS7 升级 / 系统修复 ====================
        string fixSubMode = "env";
        string envScope = "User";
        bool envLoaded = false, psLoaded = false;
        EnvInspection envData;
        PowerShellStatus psData;
        StackPanel envSectionHost, psHostPanel, portsListPanel, startupsListPanel;

        UIElement BuildSysFixPage()
        {
            if (fixSubMode == "env")
            {
                envSectionHost = new StackPanel();
                if (!envLoaded) LoadEnvAsync(); else RenderEnvSection();
                return envSectionHost;
            }

            // PS7 升级 + 6 大故障一键修 + 端口/启动项 集成工具页
            var sp = new StackPanel();
            psHostPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(psHostPanel);
            if (!psLoaded) LoadPsAsync(); else RenderPsUI();

            var bugCard = new StackPanel();
            bugCard.Children.Add(WpfUI.Txt("常见系统故障一键诊断与修复", 14.5, true, WpfUI.Pal.Text));
            var fGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 11, 0, 0) };
            fGrid.Children.Add(MakeBugTile("任务栏 / 桌面假死", "秒修", "安全重启 explorer.exe，专治任务栏点不动", WpfUI.Pal.Warn, AskRestartExplorer));
            fGrid.Children.Add(MakeBugTile("网页打不开 / DNS", "零风险", "立即刷新系统 DNS 缓存修复域名解析异常", WpfUI.Pal.Accent, () => RunBugFixAndNotify("flush-dns")));
            fGrid.Children.Add(MakeBugTile("桌面图标变白纸", "L2", "清理损坏图标与缩略图缓存并自动重建", WpfUI.Pal.Violet, () => AskBugFix("fix-icon-cache", "重建图标缓存", "将短暂重启桌面资源管理器并重建缓存。")));
            fGrid.Children.Add(MakeBugTile("代理关掉后断网", "需重启", "重置 Windows 网络协议栈 (Winsock)", WpfUI.Pal.Crit, () => AskBugFix("reset-winsock", "重置 Winsock 协议栈", "需管理员权限，执行后建议重启电脑。")));
            fGrid.Children.Add(MakeBugTile("系统更新卡死 0%", "L2", "重启 Windows Update 服务并清空损坏下载队列", WpfUI.Pal.Ok, () => AskBugFix("fix-wu", "重置更新组件", "不影响已安装的系统补丁。")));
            fGrid.Children.Add(MakeBugTile("系统文件损坏修复", "SFC", "调用官方 sfc /scannow 扫描并修复系统文件", WpfUI.Pal.Indigo, () => AskBugFix("sfc-scan", "执行 SFC 系统文件修复", "调用官方 sfc /scannow（需管理员，约 2~5 分钟）。")));
            bugCard.Children.Add(fGrid);
            var bc = WpfUI.Card(bugCard); bc.Margin = new Thickness(0, 0, 0, 14);
            sp.Children.Add(bc);

            // 端口占用 + 启动项双列
            var bottomGrid = new UniformGrid { Columns = 2 };
            var pSp = new StackPanel();
            var pH = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            pH.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pH.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var portIn = WpfUI.InputBox("", 150, true);
            var bPort = WpfUI.Btn("查端口", "primary", () => RunBg(() => { var l = SystemHealth.ListListeningPorts(portIn.Text.Trim()); OnUI(() => RenderPortsUI(l)); }), true);
            Grid.SetColumn(bPort, 1);
            var pl = new StackPanel { Orientation = Orientation.Horizontal };
            var plbl = WpfUI.Txt("端口占用 ", 13, true, WpfUI.Pal.Text); plbl.Margin = new Thickness(0, 0, 6, 0);
            pl.Children.Add(plbl); pl.Children.Add(portIn);
            pH.Children.Add(pl); pH.Children.Add(bPort);
            pSp.Children.Add(pH);
            pSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            portsListPanel = new StackPanel();
            pSp.Children.Add(new ScrollViewer { MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = portsListPanel });
            var pCard = WpfUI.Card(pSp, new Thickness(0), 14); pCard.Margin = new Thickness(0, 0, 7, 0);

            var sSp = new StackPanel();
            var sH = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            sH.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sH.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sH.Children.Add(WpfUI.Txt("开机启动项体检（禁用自动备份）", 13, true, WpfUI.Pal.Text));
            var bSt = WpfUI.Btn("刷新", "soft", () => RunBg(() => { var l = SystemHealth.ListStartups(); OnUI(() => RenderStartupsUI(l)); }), true);
            Grid.SetColumn(bSt, 1); sH.Children.Add(bSt);
            sSp.Children.Add(sH);
            sSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });
            startupsListPanel = new StackPanel();
            sSp.Children.Add(new ScrollViewer { MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = startupsListPanel });
            var sCard = WpfUI.Card(sSp, new Thickness(0), 14); sCard.Margin = new Thickness(7, 0, 0, 0);

            bottomGrid.Children.Add(pCard); bottomGrid.Children.Add(sCard);
            sp.Children.Add(bottomGrid);

            RunBg(() => { var plst = SystemHealth.ListListeningPorts(""); var slst = SystemHealth.ListStartups(); OnUI(() => { RenderPortsUI(plst); RenderStartupsUI(slst); }); });
            return sp;
        }

        Border MakeBugTile(string title, string tag, string desc, Brush col, Action onClick)
        {
            var sp = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var ico = WpfUI.SquircleIcon("M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z", col, 26, 13);
            ico.Margin = new Thickness(0, 0, 8, 0);
            var t = WpfUI.Txt(title, 13, true, WpfUI.Pal.Text); t.Margin = new Thickness(0, 0, 6, 0);
            top.Children.Add(ico); top.Children.Add(t); top.Children.Add(WpfUI.Badge(tag, WpfUI.Pal.SurfaceHover, WpfUI.Pal.Text2));
            sp.Children.Add(top);
            var d = WpfUI.TxtWrap(desc, 11.4, WpfUI.Pal.Text2); d.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(d);
            var b = WpfUI.Btn("立即执行", "secondary", onClick, true); b.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(b);
            var c = new Border { Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 8), Child = sp };
            return c;
        }

        void AskRestartExplorer() { AskBugFix("restart-explorer", "重启桌面与任务栏 (explorer.exe)", "桌面图标与任务栏将闪黑 1~2 秒后自动恢复，不会关闭浏览器或开发软件。"); }
        void AskBugFix(string id, string title, string desc)
        {
            if (!WpfUI.ShowGuardDialog(this, "L2", title, desc, new[] { title }, "确认执行此修复项", "", "确认执行")) return;
            RunBugFixAndNotify(id);
        }
        void RunBugFixAndNotify(string id) { RunBg(() => { var r = SystemHealth.RunBugFix(id); OnUI(() => ShowBanner(r.Ok, r.Title + "：" + r.Summary)); }); }

        void RenderPortsUI(List<PortEntry> list)
        {
            if (portsListPanel == null) return;
            portsListPanel.Children.Clear();
            foreach (var p in list.Take(40))
            {
                var x = p;
                var acts = !x.IsProtected ? new UIElement[] { WpfUI.Btn("释放", "danger", () => AskKillProc(x.Pid, x.ProcName, "端口 :" + x.Port), true) } : null;
                portsListPanel.Children.Add(WpfUI.MakeRow(null, ":" + x.Port + "  " + x.ProcName, x.LocalAddr + " · PID " + x.Pid, "", null, acts));
            }
        }

        void RenderStartupsUI(List<StartupEntry> list)
        {
            if (startupsListPanel == null) return;
            startupsListPanel.Children.Clear();
            foreach (var s in list)
            {
                var x = s;
                var acts = (x.Scope == "HKCU" && x.Name != "DiskGuard")
                    ? new UIElement[] { WpfUI.Btn(x.Enabled ? "安全禁用" : "恢复启用", "secondary", () => RunBg(() => { var r = SystemHealth.ToggleStartup(x.Id, !x.Enabled); var nl = SystemHealth.ListStartups(); OnUI(() => { ShowBanner(r.Ok, r.Summary); RenderStartupsUI(nl); }); }), true) }
                    : null;
                startupsListPanel.Children.Add(WpfUI.MakeRow(null, x.Name, x.Command, "", new UIElement[] { WpfUI.Badge(x.Enabled ? "已启用" : "已禁用", x.Enabled ? WpfUI.Pal.OkSoft : WpfUI.Pal.WarnSoft, x.Enabled ? WpfUI.Pal.Ok : WpfUI.Pal.Warn) }, acts));
            }
        }

        void LoadPsAsync() { psLoaded = true; RunBg(() => { psData = SoftwareManager.InspectPowerShell(); OnUI(RenderPsUI); }); }
        void RenderPsUI()
        {
            if (psHostPanel == null || psData == null) return;
            psHostPanel.Children.Clear();
            var h = new Grid();
            h.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            h.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hl = new StackPanel();
            hl.Children.Add(WpfUI.Txt("PowerShell 7 (pwsh) 现代终端检测与一键升级", 15.5, true, WpfUI.Pal.Text));
            hl.Children.Add(WpfUI.TxtWrap(string.Format("内置 Windows PowerShell v{0} · PowerShell 7：{1} · winget：{2}（与 5.1 并存互不冲突）",
                psData.WindowsPsVersion, psData.HasPwsh7 ? ("已安装 v" + psData.Pwsh7Version) : "尚未安装", psData.HasWinget ? ("可用 " + psData.WingetVersion) : "未安装"), 12, WpfUI.Pal.Text2));
            var bUp = WpfUI.Btn(psData.HasPwsh7 ? "检查升级至最新 PS 7" : "通过 winget 一键安装 PS 7", "primary", () => RunBg(() => { var r = SoftwareManager.UpgradePowerShell7(); OnUI(() => ShowBanner(r.Ok, r.Title + "：" + r.Summary)); }));
            Grid.SetColumn(bUp, 1);
            h.Children.Add(hl); h.Children.Add(bUp);
            psHostPanel.Children.Add(WpfUI.HeroCard(h, new Thickness(18, 15, 18, 15)));
        }

        void LoadEnvAsync() { envLoaded = true; RunBg(() => { envData = NetworkEnvManager.InspectEnvVars(); OnUI(RenderEnvSection); }); }

        void RenderEnvSection()
        {
            if (envSectionHost == null || envData == null) return;
            envSectionHost.Children.Clear();

            var kGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 13) };
            var k1 = WpfUI.KpiCard("用户 PATH 条目", envData.UserPath.Count + " 条", "长度 " + envData.UserPathLen + " 字符"); k1.Margin = new Thickness(0, 0, 8, 0);
            var k2 = WpfUI.KpiCard("用户失效 / 重复", string.Format("失效 {0} / 重复 {1}", envData.UserMissingCount, envData.UserDupCount), "改动前自动快照", WpfUI.Pal.Warn); k2.Margin = new Thickness(0, 0, 8, 0);
            var k3 = WpfUI.KpiCard("系统 PATH 条目", envData.MachinePath.Count + " 条", "长度 " + envData.MachinePathLen + " 字符"); k3.Margin = new Thickness(0, 0, 8, 0);
            var k4 = WpfUI.KpiCard("系统失效 / 重复", string.Format("失效 {0} / 重复 {1}", envData.MachineMissingCount, envData.MachineDupCount), "保留 %SystemRoot% 宏", WpfUI.Pal.Crit);
            kGrid.Children.Add(k1); kGrid.Children.Add(k2); kGrid.Children.Add(k3); kGrid.Children.Add(k4);
            envSectionHost.Children.Add(kGrid);

            var pSp = new StackPanel();
            var tb = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var scopes = new WrapPanel();
            scopes.Children.Add(WpfUI.PillBtn("用户 PATH", envScope == "User", () => { envScope = "User"; RenderEnvSection(); }));
            scopes.Children.Add(WpfUI.PillBtn("系统 PATH (需 UAC)", envScope == "Machine", () => { envScope = "Machine"; RenderEnvSection(); }));
            scopes.Children.Add(WpfUI.PillBtn("自定义变量", envScope == "Vars", () => { envScope = "Vars"; RenderEnvSection(); }));
            scopes.Children.Add(WpfUI.PillBtn("历史快照回滚 (" + envData.Backups.Count + ")", envScope == "Backups", () => { envScope = "Backups"; RenderEnvSection(); }));

            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            var bBak = WpfUI.Btn("立即快照", "soft", () => RunBg(() => { string f = NetworkEnvManager.BackupEnvVars("手动完整快照"); OnUI(() => { ShowBanner(true, "已保存环境变量快照：" + f); LoadEnvAsync(); }); }), true);
            bBak.Margin = new Thickness(0, 0, 6, 0);
            acts.Children.Add(bBak);
            if (envScope == "User" || envScope == "Machine")
            {
                acts.Children.Add(WpfUI.Btn("一键去重并清理失效项", "primary", () =>
                {
                    int miss = envScope == "User" ? envData.UserMissingCount : envData.MachineMissingCount;
                    int dup = envScope == "User" ? envData.UserDupCount : envData.MachineDupCount;
                    if (miss + dup == 0) { ShowBanner(true, "当前 PATH 非常整洁，没有失效路径或重复项"); return; }
                    if (!WpfUI.ShowGuardDialog(this, "L2", "智能整理 " + (envScope == "User" ? "用户" : "系统") + " PATH",
                        string.Format("将移除 {0} 个失效路径与 {1} 个重复项，同时完整保留 %SystemRoot% 等宏变量，执行前自动备份 JSON 快照。", miss, dup),
                        new[] { "作用域：" + envScope, "失效路径：" + miss + " 项", "重复路径：" + dup + " 项" }, "确认自动备份并整理 PATH", "", "备份并整理")) return;
                    RunBg(() => { var r = NetworkEnvManager.CleanPathScope(envScope, true, true); OnUI(() => { ShowBanner(r.Ok, r.Title + "：" + r.Summary); LoadEnvAsync(); }); });
                }, true));
            }
            Grid.SetColumn(acts, 1);
            tb.Children.Add(scopes); tb.Children.Add(acts);
            pSp.Children.Add(tb);
            pSp.Children.Add(new Border { Height = 1, Background = WpfUI.Pal.Border });

            var listSp = new StackPanel();
            if (envScope == "User" || envScope == "Machine")
            {
                var arr = envScope == "User" ? envData.UserPath : envData.MachinePath;
                foreach (var item in arr)
                {
                    var p = item;
                    var tags = new List<UIElement> { p.Exists ? WpfUI.Badge("有效", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok) : WpfUI.Badge("路径不存在", WpfUI.Pal.CritSoft, WpfUI.Pal.Crit) };
                    if (p.IsDuplicate) tags.Add(WpfUI.Badge("重复项", WpfUI.Pal.WarnSoft, WpfUI.Pal.Warn));
                    var rowActs = p.Exists ? new UIElement[] { WpfUI.Btn("打开", "soft", () => Util.OpenInExplorer(p.ExpandedValue), true) } : null;
                    listSp.Children.Add(WpfUI.MakeRow(null, p.RawValue, p.ExpandedValue != p.RawValue ? ("展开为：" + p.ExpandedValue) : "", "", tags.ToArray(), rowActs));
                }
            }
            else if (envScope == "Vars")
            {
                var addBar = new WrapPanel { Margin = new Thickness(14, 10, 14, 10) };
                var nIn = WpfUI.InputBox("", 170, true); nIn.Margin = new Thickness(0, 0, 6, 0);
                var vIn = WpfUI.InputBox("", 280, true); vIn.Margin = new Thickness(0, 0, 6, 0);
                addBar.Children.Add(WpfUI.Txt("新增用户变量 ", 12, true, WpfUI.Pal.Text2));
                addBar.Children.Add(nIn); addBar.Children.Add(vIn);
                addBar.Children.Add(WpfUI.Btn("保存", "primary", () => RunBg(() => { var r = NetworkEnvManager.SaveEnvVar("User", nIn.Text, vIn.Text); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadEnvAsync(); }); }), true));
                listSp.Children.Add(addBar);
                foreach (var v in envData.UserVars.Where(x => !x.Name.Equals("Path", StringComparison.OrdinalIgnoreCase)))
                {
                    var ev = v;
                    listSp.Children.Add(WpfUI.MakeRow(null, ev.Name, ev.Value, "", new UIElement[] { WpfUI.Badge("用户", WpfUI.Pal.OkSoft, WpfUI.Pal.Ok) },
                        new UIElement[] { WpfUI.Btn("删除", "soft", () => { if (WpfUI.ShowGuardDialog(this, "L2", "删除用户变量 " + ev.Name, "删除前自动备份快照。", new[] { ev.Name + "=" + ev.Value }, "确认删除", "", "删除")) RunBg(() => { var r = NetworkEnvManager.DeleteEnvVar("User", ev.Name); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadEnvAsync(); }); }); }, true) }));
                }
            }
            else
            {
                if (envData.Backups.Count == 0) listSp.Children.Add(new Border { Padding = new Thickness(20), Child = WpfUI.Txt("暂无快照，点击「立即快照」或执行整理时自动创建", 12.5, false, WpfUI.Pal.Text2) });
                foreach (var b in envData.Backups)
                {
                    var bak = b;
                    listSp.Children.Add(WpfUI.MakeRow(null, bak.FileName, bak.Time + " · " + bak.Reason, "", null,
                        new UIElement[] { WpfUI.Btn("一键回滚恢复", "secondary", () =>
                        {
                            if (!WpfUI.ShowGuardDialog(this, "L2", "从快照回滚用户环境变量", "将从 " + bak.FileName + " 恢复全部用户环境变量与 PATH。", new[] { bak.FileName }, "确认回滚", "", "确认回滚")) return;
                            RunBg(() => { var r = NetworkEnvManager.RestoreUserEnvBackup(bak.FileName); OnUI(() => { ShowBanner(r.Ok, r.Summary); LoadEnvAsync(); }); });
                        }, true) }));
                }
            }
            pSp.Children.Add(new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listSp });
            envSectionHost.Children.Add(WpfUI.Card(pSp, new Thickness(0), 15));
        }

        // ==================== PAGE: 设置、空间趋势与自动更新 ====================
        StackPanel settingsHost;
        string trendDrive = "C";
        int trendDays = 1;

        UIElement BuildSettingsPage()
        {
            settingsHost = new StackPanel();
            RefreshSettingsPage();
            return settingsHost;
        }

        void RefreshSettingsPage()
        {
            if (settingsHost == null) return;
            settingsHost.Children.Clear();
            var cfg = App.Cfg;
            if (string.IsNullOrEmpty(trendDrive) && cfg.EffectiveDrives().Length > 0) trendDrive = cfg.EffectiveDrives()[0];

            // 1. 版本管理、默认更新源与自动更新卡（置顶醒目展示！）
            var updSp = new StackPanel();
            var uHead = new Grid();
            uHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            uHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var uTitleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var uTitle = WpfUI.Txt("软件更新与版本日志", 15, true, WpfUI.Pal.Text); uTitle.Margin = new Thickness(0, 0, 8, 0);
            uTitleRow.Children.Add(uTitle);
            uTitleRow.Children.Add(WpfUI.PillBadge("当前 v" + Updater.CurrentVersion, WpfUI.Pal.AccentSoft, WpfUI.Pal.Accent));
            var uAutoSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var uAutoLbl = WpfUI.Txt("启动自动检查更新", 12.2, false, WpfUI.Pal.Text2); uAutoLbl.Margin = new Thickness(0, 0, 8, 0);
            uAutoSp.Children.Add(uAutoLbl);
            uAutoSp.Children.Add(WpfUI.SwitchToggle(cfg.AutoCheckUpdate, v => { cfg.AutoCheckUpdate = v; cfg.Save(); }));
            Grid.SetColumn(uAutoSp, 1);
            uHead.Children.Add(uTitleRow); uHead.Children.Add(uAutoSp);
            updSp.Children.Add(uHead);

            string defUrl = string.IsNullOrWhiteSpace(cfg.UpdateUrl) ? Config.DefaultUpdateUrl : cfg.UpdateUrl;
            var uInputRow = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            uInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            uInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            uInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var uBox = WpfUI.InputBox(defUrl, 0, true); uBox.Margin = new Thickness(0, 0, 8, 0);
            var bLocal = WpfUI.Btn("填本地源", "soft", () => { string p = System.IO.Path.Combine(Directory.GetParent(Util.AppDir).FullName, "release"); uBox.Text = Directory.Exists(p) ? p : Util.AppDir; }, true);
            bLocal.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(bLocal, 1);
            var bCheck = WpfUI.Btn("立即检查更新", "primary", () =>
            {
                cfg.UpdateUrl = uBox.Text.Trim(); cfg.Save();
                ShowBanner(true, "正在连接更新源检查最新版本…");
                RunBg(() =>
                {
                    var u = Updater.Check(cfg.UpdateUrl);
                    OnUI(() =>
                    {
                        if (!u.Ok) ShowBanner(false, "检查更新失败：" + u.Error);
                        else if (u.HasUpdate) { latestUpdate = u; WpfUI.ShowUpdateDialog(this, u); }
                        else ShowBanner(true, "当前已是最新版本 v" + u.CurrentVersion + "（无需升级）");
                    });
                });
            }, true);
            Grid.SetColumn(bCheck, 2);
            uInputRow.Children.Add(uBox); uInputRow.Children.Add(bLocal); uInputRow.Children.Add(bCheck);
            updSp.Children.Add(uInputRow);

            // 当前版本更新亮点
            updSp.Children.Add(new Border
            {
                Background = WpfUI.Pal.Surface2, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(13, 10, 13, 10),
                Child = WpfUI.TxtWrap("v" + Updater.CurrentVersion + " 本次更新亮点：\n" + Updater.CurrentReleaseNotes, 11.8, WpfUI.Pal.Text2)
            });
            var uc = WpfUI.HeroCard(updSp, new Thickness(18, 16, 18, 16)); uc.Margin = new Thickness(0, 0, 0, 14);
            settingsHost.Children.Add(uc);

            // 2. 空间趋势折线图与历史统计
            var trSp = new StackPanel();
            var trHead = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            trHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            trHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var curStat = App.Stats.FirstOrDefault(x => x.Name == trendDrive);
            var trL = new StackPanel();
            trL.Children.Add(WpfUI.Txt("可用空间变化趋势", 14.5, true, WpfUI.Pal.Text));
            trL.Children.Add(WpfUI.Txt(trendDrive + " 盘 — " + (curStat != null ? History.ForecastText(curStat) : "采样积累中"), 12, false, WpfUI.Pal.Text2));
            var trPills = new WrapPanel();
            foreach (var d in cfg.EffectiveDrives()) { string drv = d; trPills.Children.Add(WpfUI.PillBtn(d + " 盘", trendDrive == d, () => { trendDrive = drv; RefreshSettingsPage(); })); }
            foreach (var dy in new[] { 1, 7, 30 }) { int days = dy; trPills.Children.Add(WpfUI.PillBtn(dy == 1 ? "24h" : (dy + "天"), trendDays == dy, () => { trendDays = days; RefreshSettingsPage(); })); }
            Grid.SetColumn(trPills, 1);
            trHead.Children.Add(trL); trHead.Children.Add(trPills);
            trSp.Children.Add(trHead);

            var chartCv = new Canvas { Height = 120, Background = WpfUI.Pal.Surface2 };
            var series = History.GetSeries(trendDrive, trendDays <= 1 ? TimeSpan.FromHours(24) : TimeSpan.FromDays(trendDays));
            chartCv.SizeChanged += (s, e) => DrawTrendCanvas(chartCv, series);
            trSp.Children.Add(new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, BorderBrush = WpfUI.Pal.Border, BorderThickness = new Thickness(1), Child = chartCv });

            var sum = LogSummary.Read();
            var kpi3 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 11, 0, 0) };
            var sk1 = WpfUI.KpiCard("累计安全释放", Util.Fmt(sum.TotalFreedBytes), "", WpfUI.Pal.Accent); sk1.Margin = new Thickness(0, 0, 8, 0);
            var sk2 = WpfUI.KpiCard("历史清理次数", sum.RunCount + " 次", ""); sk2.Margin = new Thickness(0, 0, 8, 0);
            var sk3 = WpfUI.KpiCard("最近一次清理", sum.LastRunTime, "");
            kpi3.Children.Add(sk1); kpi3.Children.Add(sk2); kpi3.Children.Add(sk3);
            trSp.Children.Add(kpi3);
            var tc = WpfUI.Card(trSp); tc.Margin = new Thickness(0, 0, 0, 14);
            settingsHost.Children.Add(tc);

            // 3. 左右双列：智能自动清理策略 + 监控与外观设置
            var twoCol = new UniformGrid { Columns = 2 };
            var leftSp = new StackPanel();
            leftSp.Children.Add(WpfUI.Txt("智能自动清理护航（严守 L1 安全白名单）", 14, true, WpfUI.Pal.Text));
            leftSp.Children.Add(MakeSettingLine("低空间自动护航清理", WpfUI.SwitchToggle(cfg.AutoCleanOnLowSpace, v => { cfg.AutoCleanOnLowSpace = v; cfg.Save(); })));
            leftSp.Children.Add(MakeSettingLine("低空间触发阈值", WpfUI.Stepper((int)cfg.AutoCleanLowSpaceGB, 1, 3, 100, " GB", v => { cfg.AutoCleanLowSpaceGB = v; cfg.Save(); })));
            leftSp.Children.Add(MakeSettingLine("后台卡顿自动提醒", WpfUI.SwitchToggle(cfg.LagWatchdog, v => { cfg.LagWatchdog = v; cfg.Save(); })));
            leftSp.Children.Add(MakeSettingLine("高内存自动温和释放", WpfUI.SwitchToggle(cfg.AutoCleanMemOnHigh, v => { cfg.AutoCleanMemOnHigh = v; cfg.Save(); })));
            var bRunAuto = WpfUI.Btn("立即试跑一次 L1 自动清理", "secondary", () => App.RunAutoCleanAsync("手动试跑自动清理", (f, d) => OnUI(() => ShowBanner(true, "自动清理完成：释放 " + Util.Fmt(f) + "（可用空间 " + Util.FmtDelta(d) + "）"))), true);
            bRunAuto.HorizontalAlignment = HorizontalAlignment.Left; bRunAuto.Margin = new Thickness(0, 10, 0, 0);
            leftSp.Children.Add(bRunAuto);
            var lc = WpfUI.Card(leftSp); lc.Margin = new Thickness(0, 0, 7, 0);

            var rightSp = new StackPanel();
            rightSp.Children.Add(WpfUI.Txt("系统集成、阈值与日志", 14, true, WpfUI.Pal.Text));
            rightSp.Children.Add(MakeSettingLine("偏低空间预警阈值", WpfUI.Stepper((int)cfg.WarnGB, 1, 2, 500, " GB", v => { cfg.WarnGB = v; cfg.Save(); App.ApplyConfigChanged(); })));
            rightSp.Children.Add(MakeSettingLine("严重不足预警阈值", WpfUI.Stepper((int)cfg.CritGB, 1, 1, 200, " GB", v => { cfg.CritGB = v; cfg.Save(); App.ApplyConfigChanged(); })));
            rightSp.Children.Add(MakeSettingLine("开机自动启动", WpfUI.SwitchToggle(AutoStart.Enabled, v => { try { AutoStart.Set(v); } catch { } })));
            rightSp.Children.Add(MakeSettingLine("任务栏图标常驻", WpfUI.SwitchToggle(TrayPin.State() == 1, v => { try { TrayPin.Set(v); } catch { } })));
            var logActs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var bCfg = WpfUI.Btn("打开配置目录", "soft", () => Util.OpenInExplorer(Config.FilePath), true); bCfg.Margin = new Thickness(0, 0, 6, 0);
            var bLog = WpfUI.Btn("打开完整日志", "secondary", () => Util.OpenPath(File.Exists(Util.LogFile) ? Util.LogFile : Util.LogDir), true);
            logActs.Children.Add(bCfg); logActs.Children.Add(bLog);
            rightSp.Children.Add(logActs);
            var rc = WpfUI.Card(rightSp); rc.Margin = new Thickness(7, 0, 0, 0);

            twoCol.Children.Add(lc); twoCol.Children.Add(rc);
            settingsHost.Children.Add(twoCol);
        }

        void DrawTrendCanvas(Canvas cv, List<Sample> pts)
        {
            cv.Children.Clear();
            double w = cv.ActualWidth > 40 ? cv.ActualWidth : 800, h = 120;
            if (pts == null || pts.Count < 2)
            {
                var t = WpfUI.Txt("空间历史趋势采样积累中（每 10 分钟自动记录）", 12, false, WpfUI.Pal.Text3);
                Canvas.SetLeft(t, 20); Canvas.SetTop(t, 50); cv.Children.Add(t);
                return;
            }
            double minG = Math.Max(0, pts.Min(p => p.Free / 1073741824.0) - 2);
            double maxG = pts.Max(p => p.Free / 1073741824.0) + 2;
            if (maxG - minG < 4) maxG = minG + 4;
            var poly = new Polyline { Stroke = WpfUI.Pal.Accent, StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < pts.Count; i++)
            {
                double x = 16 + (w - 32) * i / (pts.Count - 1);
                double g = pts[i].Free / 1073741824.0;
                double y = h - 16 - ((g - minG) / (maxG - minG) * (h - 32));
                poly.Points.Add(new Point(x, y));
            }
            cv.Children.Add(poly);
        }
    }
}
