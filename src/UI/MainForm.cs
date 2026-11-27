// DiskGuard v4 - 轻量级原生 WebView2 宿主窗口（关闭后彻底释放 WebView2 内存，托盘常驻仅 ~8 MB）
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DiskGuard
{
    public class MainForm : Form
    {
        readonly TrayApp app;
        readonly WebView2 webView;
        readonly WebBridge bridge;
        bool webReady;
        public int InitialTab { get; private set; }

        public MainForm(TrayApp app, int initialTab = 0)
        {
            this.app = app;
            InitialTab = initialTab;
            Text = "DiskGuard - 磁盘管家";
            try { Icon = Theme.CreateBrandWindowIcon(); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1160, 760);
            MinimumSize = new Size(940, 620);
            BackColor = Theme.Current.IsDark ? Color.FromArgb(15, 18, 24) : Color.FromArgb(245, 247, 250);

            bridge = new WebBridge(app, this);
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = BackColor
            };
            Controls.Add(webView);

            Load += async (s, e) =>
            {
                SyncDwmTheme();
                await InitWebViewAsync();
            };

            FormClosing += (s, e) =>
            {
                bridge.CancelAll();
            };

            FormClosed += (s, e) =>
            {
                try { webView.Dispose(); } catch { }
            };
        }

        async Task InitWebViewAsync()
        {
            try
            {
                string userDataDir = Path.Combine(Util.LocalAppData, "DiskGuard", "WebView2");
                Directory.CreateDirectory(userDataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir, null);
                await webView.EnsureCoreWebView2Async(env);

                var core = webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.IsBuiltInErrorPageEnabled = false;

                string uiDir = Path.Combine(Util.AppDir, "ui");
                core.SetVirtualHostNameToFolderMapping("app.diskguard.local", uiDir, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += (s, e) =>
                {
                    try { bridge.HandleMessage(e.WebMessageAsJson); } catch { }
                };

                webReady = true;
                core.Navigate("https://app.diskguard.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法初始化 WebView2 界面引擎：" + ex.Message, "DiskGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void SwitchPage(int tabIndex)
        {
            InitialTab = tabIndex;
            if (webReady) bridge.Push("switchTab", tabIndex);
        }

        public void RefreshDrives()
        {
            if (webReady && !IsDisposed) bridge.PushDrives();
        }

        public void SyncDwmTheme()
        {
            bool dark = Theme.Current.IsDark;
            Color canvas = dark ? Color.FromArgb(15, 18, 24) : Color.FromArgb(245, 247, 250);
            Color border = dark ? Color.FromArgb(38, 45, 58) : Color.FromArgb(218, 225, 236);
            BackColor = canvas;
            try { webView.DefaultBackgroundColor = canvas; } catch { }
            if (IsHandleCreated) Win32Native.ApplyDwmTheme(Handle, dark, canvas, border);
        }

        public void InvokeOnUI(Action a)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    if (InvokeRequired) BeginInvoke(a);
                    else a();
                }
            }
            catch { }
        }

        public void PostJson(string rawJson)
        {
            InvokeOnUI(() =>
            {
                try
                {
                    if (webReady && webView != null && !webView.IsDisposed && webView.CoreWebView2 != null)
                        webView.CoreWebView2.PostWebMessageAsJson(rawJson);
                }
                catch { }
            });
        }

        // 供自检验收导出真实 WebView2 渲染截图
        public async Task<bool> WaitReadyAndCaptureAsync(string outDir)
        {
            int waited = 0;
            while (!webReady && waited < 8000) { await Task.Delay(100); waited += 100; }
            if (!webReady || webView.CoreWebView2 == null) return false;

            await Task.Delay(1600);
            await SavePreviewAsync(Path.Combine(outDir, "v8_overview_light.png"));

            await webView.CoreWebView2.ExecuteScriptAsync("window.__dgTest && window.__dgTest.go('user')");
            await Task.Delay(1600);
            await SavePreviewAsync(Path.Combine(outDir, "v8_user_light.png"));

            await webView.CoreWebView2.ExecuteScriptAsync("window.__dgTest && window.__dgTest.go('apps')");
            await Task.Delay(1200);
            await SavePreviewAsync(Path.Combine(outDir, "v8_apps_light.png"));

            await webView.CoreWebView2.ExecuteScriptAsync("window.__dgTest && window.__dgTest.theme('dark')");
            await webView.CoreWebView2.ExecuteScriptAsync("window.__dgTest && window.__dgTest.go('sysfix')");
            await Task.Delay(1200);
            await SavePreviewAsync(Path.Combine(outDir, "v8_sysfix_dark.png"));

            return true;
        }

        async Task SavePreviewAsync(string filePath)
        {
            using (var fs = File.Create(filePath))
            {
                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            }
        }
    }
}
