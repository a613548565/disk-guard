// DiskGuard v3 - 双主题调色板（Windows 11 跟随系统深/浅色）、字体缓存、矢量图标绘制与菜单渲染器
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskGuard
{
    public enum IconKind
    {
        None,
        Brand,
        Clean,
        Analyzer,
        Trend,
        Settings,
        Folder,
        File,
        Refresh,
        Trash,
        Sun,
        Moon,
        Lock,
        Check,
        Back,
        Play,
        Stop,
        Log
    }

    public class Palette
    {
        public bool IsDark;
        public Color Canvas;
        public Color Sidebar;
        public Color Surface;
        public Color SurfaceHover;
        public Color SurfaceSelected;
        public Color Border;
        public Color BorderSubtle;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color TextMuted;
        public Color Accent;
        public Color AccentHover;
        public Color AccentSoft;
        public Color AccentText;
        public Color Ok;
        public Color OkSoft;
        public Color Warn;
        public Color WarnSoft;
        public Color Crit;
        public Color CritSoft;
        public Color TrackBg;
        public Color ScrollThumb;
        public Color InputBg;

        public Color LevelColor(int level)
        {
            if (level == 2) return Crit;
            if (level == 1) return Warn;
            return Ok;
        }

        public Color LevelSoft(int level)
        {
            if (level == 2) return CritSoft;
            if (level == 1) return WarnSoft;
            return OkSoft;
        }
    }

    public static class Theme
    {
        public static readonly Palette DarkPalette = new Palette
        {
            IsDark = true,
            Canvas = Color.FromArgb(19, 22, 27),
            Sidebar = Color.FromArgb(24, 28, 35),
            Surface = Color.FromArgb(30, 35, 43),
            SurfaceHover = Color.FromArgb(38, 45, 56),
            SurfaceSelected = Color.FromArgb(27, 46, 66),
            Border = Color.FromArgb(46, 54, 67),
            BorderSubtle = Color.FromArgb(36, 42, 52),
            TextPrimary = Color.FromArgb(241, 245, 249),
            TextSecondary = Color.FromArgb(156, 168, 186),
            TextMuted = Color.FromArgb(107, 119, 138),
            Accent = Color.FromArgb(14, 165, 233),
            AccentHover = Color.FromArgb(56, 189, 248),
            AccentSoft = Color.FromArgb(24, 52, 74),
            AccentText = Color.FromArgb(255, 255, 255),
            Ok = Color.FromArgb(34, 197, 94),
            OkSoft = Color.FromArgb(22, 54, 38),
            Warn = Color.FromArgb(245, 158, 11),
            WarnSoft = Color.FromArgb(62, 45, 19),
            Crit = Color.FromArgb(239, 68, 68),
            CritSoft = Color.FromArgb(64, 28, 32),
            TrackBg = Color.FromArgb(45, 52, 64),
            ScrollThumb = Color.FromArgb(72, 83, 100),
            InputBg = Color.FromArgb(22, 26, 32)
        };

        public static readonly Palette LightPalette = new Palette
        {
            IsDark = false,
            Canvas = Color.FromArgb(244, 246, 250),
            Sidebar = Color.FromArgb(235, 239, 245),
            Surface = Color.FromArgb(255, 255, 255),
            SurfaceHover = Color.FromArgb(241, 245, 250),
            SurfaceSelected = Color.FromArgb(232, 244, 254),
            Border = Color.FromArgb(215, 223, 234),
            BorderSubtle = Color.FromArgb(230, 235, 243),
            TextPrimary = Color.FromArgb(15, 23, 42),
            TextSecondary = Color.FromArgb(71, 85, 105),
            TextMuted = Color.FromArgb(120, 134, 154),
            Accent = Color.FromArgb(2, 132, 199),
            AccentHover = Color.FromArgb(3, 105, 161),
            AccentSoft = Color.FromArgb(224, 242, 254),
            AccentText = Color.FromArgb(255, 255, 255),
            Ok = Color.FromArgb(22, 163, 74),
            OkSoft = Color.FromArgb(220, 252, 231),
            Warn = Color.FromArgb(217, 119, 6),
            WarnSoft = Color.FromArgb(254, 243, 199),
            Crit = Color.FromArgb(220, 38, 38),
            CritSoft = Color.FromArgb(254, 226, 226),
            TrackBg = Color.FromArgb(226, 232, 240),
            ScrollThumb = Color.FromArgb(182, 193, 208),
            InputBg = Color.FromArgb(255, 255, 255)
        };

        public static Palette Current = DarkPalette;

        // 缓存字体，减少重绘期间的 GDI 分配
        public static readonly Font FontBrand = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold);
        public static readonly Font FontSection = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        public static readonly Font FontBodyBold = new Font("Microsoft YaHei UI", 9.25F, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Microsoft YaHei UI", 9.25F, FontStyle.Regular);
        public static readonly Font FontSmall = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular);
        public static readonly Font FontSmallBold = new Font("Microsoft YaHei UI", 8.0F, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9.25F, FontStyle.Regular);
        public static readonly Font FontMonoBold = new Font("Consolas", 9.75F, FontStyle.Bold);
        public static readonly Font FontMonoBig = new Font("Consolas", 13F, FontStyle.Bold);
        public static readonly Font FontMonoSmall = new Font("Consolas", 8.25F, FontStyle.Regular);

        public static bool SystemUsesLightTheme()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        var v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        public static void ApplyMode(ThemeMode mode)
        {
            bool dark;
            if (mode == ThemeMode.Dark) dark = true;
            else if (mode == ThemeMode.Light) dark = false;
            else dark = !SystemUsesLightTheme();
            Current = dark ? DarkPalette : LightPalette;
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
        {
            using (var p = RoundedRect(r, radius)) g.FillPath(b, p);
        }

        public static void DrawRounded(Graphics g, Pen pen, RectangleF r, float radius)
        {
            using (var p = RoundedRect(r, radius)) g.DrawPath(pen, p);
        }

        public static Icon CreateBrandWindowIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var bg = new SolidBrush(Color.FromArgb(14, 165, 233)))
                        FillRounded(g, bg, new RectangleF(1, 1, 30, 30), 8);
                    Icons.Draw(g, IconKind.Brand, new RectangleF(6, 6, 20, 20), Color.White, 2.0f);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // 统一几何矢量图标（1.6px 等宽圆角描边，不使用杂乱 Emoji）
    public static class Icons
    {
        public static void Draw(Graphics g, IconKind kind, RectangleF r, Color color, float stroke = 1.6f)
        {
            if (kind == IconKind.None) return;
            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;

            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(color))
            {
                switch (kind)
                {
                    case IconKind.Brand:
                        // 盾牌外框 + 内部磁盘进度弧
                        using (var p = new GraphicsPath())
                        {
                            p.AddLine(x + w * 0.5f, y + h * 0.06f, x + w * 0.90f, y + h * 0.22f);
                            p.AddLine(x + w * 0.90f, y + h * 0.22f, x + w * 0.84f, y + h * 0.64f);
                            p.AddLine(x + w * 0.84f, y + h * 0.64f, x + w * 0.50f, y + h * 0.94f);
                            p.AddLine(x + w * 0.50f, y + h * 0.94f, x + w * 0.16f, y + h * 0.64f);
                            p.AddLine(x + w * 0.16f, y + h * 0.64f, x + w * 0.10f, y + h * 0.22f);
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        g.DrawArc(pen, x + w * 0.30f, y + h * 0.30f, w * 0.40f, h * 0.40f, -60, 260);
                        g.FillEllipse(br, x + w * 0.45f, y + h * 0.45f, w * 0.10f, h * 0.10f);
                        break;

                    case IconKind.Clean:
                        // 几何星芒 + 清扫弧线
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.12f, x + w * 0.5f, y + h * 0.88f);
                        g.DrawLine(pen, x + w * 0.12f, y + h * 0.5f, x + w * 0.88f, y + h * 0.5f);
                        g.DrawLine(pen, x + w * 0.25f, y + h * 0.25f, x + w * 0.75f, y + h * 0.75f);
                        g.DrawLine(pen, x + w * 0.75f, y + h * 0.25f, x + w * 0.25f, y + h * 0.75f);
                        break;

                    case IconKind.Analyzer:
                        // 磁盘空间比例柱
                        Theme.DrawRounded(g, pen, new RectangleF(x + w * 0.12f, y + h * 0.18f, w * 0.76f, h * 0.18f), 2);
                        Theme.DrawRounded(g, pen, new RectangleF(x + w * 0.12f, y + h * 0.46f, w * 0.54f, h * 0.18f), 2);
                        Theme.DrawRounded(g, pen, new RectangleF(x + w * 0.12f, y + h * 0.74f, w * 0.34f, h * 0.16f), 2);
                        break;

                    case IconKind.Trend:
                        // 折线趋势图
                        g.DrawLine(pen, x + w * 0.12f, y + h * 0.84f, x + w * 0.88f, y + h * 0.84f);
                        g.DrawLines(pen, new[] {
                            new PointF(x + w * 0.14f, y + h * 0.64f),
                            new PointF(x + w * 0.38f, y + h * 0.40f),
                            new PointF(x + w * 0.60f, y + h * 0.54f),
                            new PointF(x + w * 0.86f, y + h * 0.20f)
                        });
                        break;

                    case IconKind.Settings:
                        // 三行调节滑块
                        g.DrawLine(pen, x + w * 0.14f, y + h * 0.25f, x + w * 0.86f, y + h * 0.25f);
                        g.DrawLine(pen, x + w * 0.14f, y + h * 0.50f, x + w * 0.86f, y + h * 0.50f);
                        g.DrawLine(pen, x + w * 0.14f, y + h * 0.75f, x + w * 0.86f, y + h * 0.75f);
                        g.FillEllipse(br, x + w * 0.30f - 2.3f, y + h * 0.25f - 2.3f, 4.6f, 4.6f);
                        g.FillEllipse(br, x + w * 0.68f - 2.3f, y + h * 0.50f - 2.3f, 4.6f, 4.6f);
                        g.FillEllipse(br, x + w * 0.44f - 2.3f, y + h * 0.75f - 2.3f, 4.6f, 4.6f);
                        break;

                    case IconKind.Folder:
                        using (var p = new GraphicsPath())
                        {
                            p.AddLine(x + w * 0.12f, y + h * 0.24f, x + w * 0.42f, y + h * 0.24f);
                            p.AddLine(x + w * 0.42f, y + h * 0.24f, x + w * 0.52f, y + h * 0.34f);
                            p.AddLine(x + w * 0.52f, y + h * 0.34f, x + w * 0.88f, y + h * 0.34f);
                            p.AddLine(x + w * 0.88f, y + h * 0.34f, x + w * 0.88f, y + h * 0.82f);
                            p.AddLine(x + w * 0.88f, y + h * 0.82f, x + w * 0.12f, y + h * 0.82f);
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        break;

                    case IconKind.File:
                        using (var p = new GraphicsPath())
                        {
                            p.AddLine(x + w * 0.22f, y + h * 0.14f, x + w * 0.60f, y + h * 0.14f);
                            p.AddLine(x + w * 0.60f, y + h * 0.14f, x + w * 0.80f, y + h * 0.34f);
                            p.AddLine(x + w * 0.80f, y + h * 0.34f, x + w * 0.80f, y + h * 0.86f);
                            p.AddLine(x + w * 0.80f, y + h * 0.86f, x + w * 0.22f, y + h * 0.86f);
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        break;

                    case IconKind.Refresh:
                        g.DrawArc(pen, x + w * 0.16f, y + h * 0.16f, w * 0.68f, h * 0.68f, -45, 280);
                        g.DrawLine(pen, x + w * 0.78f, y + h * 0.18f, x + w * 0.84f, y + h * 0.36f);
                        g.DrawLine(pen, x + w * 0.64f, y + h * 0.34f, x + w * 0.84f, y + h * 0.36f);
                        break;

                    case IconKind.Trash:
                        g.DrawLine(pen, x + w * 0.18f, y + h * 0.28f, x + w * 0.82f, y + h * 0.28f);
                        g.DrawLine(pen, x + w * 0.38f, y + h * 0.16f, x + w * 0.62f, y + h * 0.16f);
                        g.DrawRectangle(pen, x + w * 0.26f, y + h * 0.28f, w * 0.48f, h * 0.56f);
                        g.DrawLine(pen, x + w * 0.42f, y + h * 0.44f, x + w * 0.42f, y + h * 0.70f);
                        g.DrawLine(pen, x + w * 0.58f, y + h * 0.44f, x + w * 0.58f, y + h * 0.70f);
                        break;

                    case IconKind.Sun:
                        g.DrawEllipse(pen, x + w * 0.30f, y + h * 0.30f, w * 0.40f, h * 0.40f);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.10f, x + w * 0.5f, y + h * 0.20f);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.80f, x + w * 0.5f, y + h * 0.90f);
                        g.DrawLine(pen, x + w * 0.10f, y + h * 0.5f, x + w * 0.20f, y + h * 0.5f);
                        g.DrawLine(pen, x + w * 0.80f, y + h * 0.5f, x + w * 0.90f, y + h * 0.5f);
                        break;

                    case IconKind.Moon:
                        g.DrawArc(pen, x + w * 0.18f, y + h * 0.16f, w * 0.66f, h * 0.66f, 35, 250);
                        g.DrawArc(pen, x + w * 0.36f, y + h * 0.16f, w * 0.52f, h * 0.52f, 80, 175);
                        break;

                    case IconKind.Lock:
                        Theme.DrawRounded(g, pen, new RectangleF(x + w * 0.22f, y + h * 0.46f, w * 0.56f, h * 0.40f), 2);
                        g.DrawArc(pen, x + w * 0.32f, y + h * 0.16f, w * 0.36f, h * 0.40f, 180, 180);
                        break;

                    case IconKind.Check:
                        g.DrawLines(pen, new[] {
                            new PointF(x + w * 0.18f, y + h * 0.52f),
                            new PointF(x + w * 0.42f, y + h * 0.76f),
                            new PointF(x + w * 0.84f, y + h * 0.26f)
                        });
                        break;

                    case IconKind.Back:
                        g.DrawLines(pen, new[] {
                            new PointF(x + w * 0.62f, y + h * 0.20f),
                            new PointF(x + w * 0.30f, y + h * 0.50f),
                            new PointF(x + w * 0.62f, y + h * 0.80f)
                        });
                        break;

                    case IconKind.Play:
                        g.FillPolygon(br, new[] {
                            new PointF(x + w * 0.26f, y + h * 0.18f),
                            new PointF(x + w * 0.82f, y + h * 0.50f),
                            new PointF(x + w * 0.26f, y + h * 0.82f)
                        });
                        break;

                    case IconKind.Stop:
                        Theme.FillRounded(g, br, new RectangleF(x + w * 0.24f, y + h * 0.24f, w * 0.52f, h * 0.52f), 2);
                        break;

                    case IconKind.Log:
                        Theme.DrawRounded(g, pen, new RectangleF(x + w * 0.18f, y + h * 0.14f, w * 0.64f, h * 0.72f), 2);
                        g.DrawLine(pen, x + w * 0.32f, y + h * 0.36f, x + w * 0.68f, y + h * 0.36f);
                        g.DrawLine(pen, x + w * 0.32f, y + h * 0.54f, x + w * 0.68f, y + h * 0.54f);
                        g.DrawLine(pen, x + w * 0.32f, y + h * 0.70f, x + w * 0.54f, y + h * 0.70f);
                        break;
                }
            }
            g.SmoothingMode = oldSmooth;
        }
    }

    // 托盘右键菜单现代化主题渲染器
    public class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Current.Surface))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Current.Border))
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (var b = new SolidBrush(Theme.Current.SurfaceHover))
                    Theme.FillRounded(e.Graphics, b, r, 4);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Current.TextPrimary : Theme.Current.TextSecondary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Current.BorderSubtle))
                e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new RectangleF(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, 13, 13);
            Icons.Draw(e.Graphics, IconKind.Check, r, Theme.Current.Accent, 2f);
        }

        class MenuColors : ProfessionalColorTable
        {
            public override Color ImageMarginGradientBegin { get { return Theme.Current.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Current.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Current.Surface; } }
            public override Color MenuBorder { get { return Theme.Current.Border; } }
        }
    }
}
