// DiskGuard v9.0 - macOS Sequoia / iOS 18 风格原生 DirectX WPF 主题、Segmented Control、圆角阴影与更新/防呆弹窗
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DiskGuard
{
    public class WpfPalette
    {
        public bool IsDark;
        public SolidColorBrush Bg, Sidebar, Surface, Surface2, SurfaceHover, SegBg;
        public SolidColorBrush Border, BorderStrong;
        public SolidColorBrush Text, Text2, Text3;
        public SolidColorBrush Accent, AccentHover, AccentSoft;
        public SolidColorBrush Ok, OkSoft, Warn, WarnSoft, Crit, CritSoft, Indigo, IndigoSoft, Violet, VioletSoft, Teal, TealSoft;
        public SolidColorBrush Track, HeroBorder;
        public Brush HeroBg;
    }

    public static class WpfUI
    {
        public static readonly FontFamily FontSans = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
        public static readonly FontFamily FontMono = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        public static WpfPalette Pal { get; private set; }

        static SolidColorBrush Br(byte r, byte g, byte b, byte a = 255)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public static void SyncPalette(bool dark)
        {
            if (dark)
            {
                var hb = new LinearGradientBrush(
                    Color.FromRgb(28, 30, 38),
                    Color.FromRgb(22, 34, 52),
                    new Point(0, 0), new Point(1, 1));
                hb.Freeze();
                Pal = new WpfPalette
                {
                    IsDark = true,
                    Bg = Br(22, 22, 24),          // Apple macOS Dark Canvas #161618
                    Sidebar = Br(28, 28, 32),     // Apple Sidebar #1C1C20
                    Surface = Br(36, 36, 41),     // Apple Elevated Card #242429
                    Surface2 = Br(44, 44, 50),    // Apple Secondary #2C2C32
                    SurfaceHover = Br(54, 54, 62),
                    SegBg = Br(42, 42, 48),
                    Border = Br(255, 255, 255, 22),
                    BorderStrong = Br(255, 255, 255, 42),
                    Text = Br(245, 245, 247),
                    Text2 = Br(161, 161, 170),
                    Text3 = Br(113, 113, 122),
                    Accent = Br(10, 132, 255),    // Apple System Blue Dark #0A84FF
                    AccentHover = Br(64, 156, 255),
                    AccentSoft = Br(10, 132, 255, 40),
                    Ok = Br(48, 209, 88),         // Apple Green Dark #30D158
                    OkSoft = Br(48, 209, 88, 38),
                    Warn = Br(255, 159, 10),      // Apple Orange Dark #FF9F0A
                    WarnSoft = Br(255, 159, 10, 38),
                    Crit = Br(255, 69, 58),       // Apple Red Dark #FF453A
                    CritSoft = Br(255, 69, 58, 40),
                    Indigo = Br(94, 92, 230),     // Apple Indigo #5E5CE6
                    IndigoSoft = Br(94, 92, 230, 40),
                    Violet = Br(191, 90, 242),    // Apple Purple #BF5AF2
                    VioletSoft = Br(191, 90, 242, 38),
                    Teal = Br(100, 210, 255),
                    TealSoft = Br(100, 210, 255, 36),
                    Track = Br(56, 56, 64),
                    HeroBg = hb,
                    HeroBorder = Br(10, 132, 255, 66)
                };
            }
            else
            {
                var hb = new LinearGradientBrush(
                    Color.FromRgb(255, 255, 255),
                    Color.FromRgb(240, 246, 255),
                    new Point(0, 0), new Point(1, 1));
                hb.Freeze();
                Pal = new WpfPalette
                {
                    IsDark = false,
                    Bg = Br(245, 245, 247),       // Apple Signature Silver #F5F5F7
                    Sidebar = Br(238, 238, 242),  // Apple Translucent-feel Sidebar #EEEEF2
                    Surface = Br(255, 255, 255),  // Pure White Card
                    Surface2 = Br(248, 248, 250),
                    SurfaceHover = Br(238, 240, 245),
                    SegBg = Br(229, 229, 234),    // iOS 18 Segmented Track #E5E5EA
                    Border = Br(0, 0, 0, 16),
                    BorderStrong = Br(0, 0, 0, 30),
                    Text = Br(29, 29, 31),        // Apple Primary Ink #1D1D1F
                    Text2 = Br(110, 110, 115),    // Apple Secondary Ink #6E6E73
                    Text3 = Br(142, 142, 147),    // Apple Tertiary Ink #8E8E93
                    Accent = Br(0, 122, 255),     // Apple System Blue #007AFF
                    AccentHover = Br(0, 102, 224),
                    AccentSoft = Br(0, 122, 255, 26),
                    Ok = Br(52, 199, 89),         // Apple Green #34C759
                    OkSoft = Br(52, 199, 89, 28),
                    Warn = Br(255, 149, 0),       // Apple Orange #FF9500
                    WarnSoft = Br(255, 149, 0, 28),
                    Crit = Br(255, 59, 48),       // Apple Red #FF3B30
                    CritSoft = Br(255, 59, 48, 26),
                    Indigo = Br(88, 86, 214),     // Apple Indigo #5856D6
                    IndigoSoft = Br(88, 86, 214, 26),
                    Violet = Br(175, 82, 222),    // Apple Purple #AF52DE
                    VioletSoft = Br(175, 82, 222, 26),
                    Teal = Br(50, 173, 230),
                    TealSoft = Br(50, 173, 230, 26),
                    Track = Br(229, 229, 234),
                    HeroBg = hb,
                    HeroBorder = Br(0, 122, 255, 48)
                };
            }
        }

        static WpfUI() { SyncPalette(Theme.Current != null && Theme.Current.IsDark); }

        public static SolidColorBrush LevelBrush(int level) { return level == 2 ? Pal.Crit : (level == 1 ? Pal.Warn : Pal.Ok); }
        public static SolidColorBrush LevelSoftBrush(int level) { return level == 2 ? Pal.CritSoft : (level == 1 ? Pal.WarnSoft : Pal.OkSoft); }

        public static DropShadowEffect SoftShadow(double blur = 16, double depth = 3, double opacity = 0.055)
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = blur,
                ShadowDepth = depth,
                Direction = 270,
                Opacity = Pal.IsDark ? opacity * 2.4 : opacity
            };
        }

        public static TextBlock Txt(string text, double size = 13, bool bold = false, Brush fg = null, bool mono = false)
        {
            return new TextBlock
            {
                Text = text ?? "",
                FontFamily = mono ? FontMono : FontSans,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = fg ?? Pal.Text,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock TxtWrap(string text, double size = 12.2, Brush fg = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                FontFamily = FontSans,
                FontSize = size,
                Foreground = fg ?? Pal.Text2,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = size * 1.5
            };
        }

        public static Border Card(UIElement child, Thickness? pad = null, double radius = 16)
        {
            return new Border
            {
                Background = Pal.Surface,
                BorderBrush = Pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                Padding = pad ?? new Thickness(18, 15, 18, 15),
                Effect = SoftShadow(16, 3, 0.05),
                Child = child
            };
        }

        public static Border HeroCard(UIElement child, Thickness? pad = null)
        {
            return new Border
            {
                Background = Pal.HeroBg,
                BorderBrush = Pal.HeroBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = pad ?? new Thickness(22, 20, 22, 20),
                Effect = SoftShadow(22, 5, 0.065),
                Child = child
            };
        }

        // Apple Squircle 彩色圆角图标徽章（macOS/iOS 设置经典辨识度）
        public static Border SquircleIcon(string pathData, Brush bg, double boxSize = 26, double iconSize = 13.5)
        {
            return new Border
            {
                Width = boxSize,
                Height = boxSize,
                CornerRadius = new CornerRadius(boxSize * 0.28),
                Background = bg,
                Child = Icon(pathData, iconSize, Brushes.White, 1.9)
            };
        }

        // iOS 18 原生风格 Segmented Control（替代杂乱的平铺入口）
        public static Border SegmentedBar(IList<KeyValuePair<string, string>> items, string activeKey, Action<string> onSelect)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var kv in items)
            {
                string k = kv.Key;
                bool active = k == activeKey;
                var btn = new Button
                {
                    Content = kv.Value,
                    FontFamily = FontSans,
                    FontSize = 12.6,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground = active ? Pal.Text : Pal.Text2,
                    Background = active ? Pal.Surface : Brushes.Transparent,
                    BorderBrush = active ? Pal.Border : Brushes.Transparent,
                    BorderThickness = new Thickness(active ? 1 : 0),
                    Padding = new Thickness(15, 6.5, 15, 6.5),
                    Cursor = Cursors.Hand,
                    Effect = active ? SoftShadow(8, 1.5, 0.08) : null
                };
                btn.Template = BuildRoundedButtonTemplate(9);
                btn.Click += (s, e) => { if (onSelect != null) onSelect(k); };
                sp.Children.Add(btn);
            }
            return new Border
            {
                Background = Pal.SegBg,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(3.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = sp
            };
        }

        public static Border Badge(string text, Brush bg, Brush fg, double fontSize = 10.5)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontFamily = FontSans, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = fg }
            };
        }

        public static Border PillBadge(string text, Brush bg, Brush fg)
        {
            var b = Badge(text, bg, fg, 10.5);
            b.CornerRadius = new CornerRadius(10);
            b.Padding = new Thickness(8.5, 2.5, 8.5, 2.5);
            return b;
        }

        public static System.Windows.Shapes.Path Icon(string pathData, double size, Brush stroke, double thickness = 1.8)
        {
            var p = new System.Windows.Shapes.Path
            {
                Width = size, Height = size, Stretch = Stretch.Uniform,
                Stroke = stroke, StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            try { p.Data = Geometry.Parse(pathData); } catch { }
            return p;
        }

        public static Button Btn(string text, string kind, Action onClick, bool compact = false)
        {
            Brush bg, fg, brd;
            double radius = compact ? 8 : 10;
            if (kind == "primary") { bg = Pal.Accent; fg = Brushes.White; brd = Pal.Accent; radius = compact ? 8 : 18; }
            else if (kind == "danger") { bg = Pal.Crit; fg = Brushes.White; brd = Pal.Crit; }
            else if (kind == "warn") { bg = Pal.Warn; fg = Brushes.White; brd = Pal.Warn; }
            else if (kind == "soft") { bg = Pal.SurfaceHover; fg = Pal.Text2; brd = Brushes.Transparent; }
            else if (kind == "link") { bg = Brushes.Transparent; fg = Pal.Accent; brd = Brushes.Transparent; }
            else { bg = Pal.Surface; fg = Pal.Text; brd = Pal.BorderStrong; }

            var btn = new Button
            {
                Content = text,
                FontFamily = FontSans,
                FontSize = compact ? 11.8 : 13,
                FontWeight = (kind == "primary" || kind == "danger" || kind == "warn") ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = fg, Background = bg, BorderBrush = brd,
                BorderThickness = new Thickness(kind == "link" || kind == "soft" ? 0 : 1),
                Padding = kind == "link" ? new Thickness(4, 2, 4, 2) : (compact ? new Thickness(11, 4.5, 11, 4.5) : new Thickness(18, 8.5, 18, 8.5)),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
            };
            btn.Template = BuildRoundedButtonTemplate(radius);
            if (onClick != null) btn.Click += (s, e) => onClick();
            return btn;
        }

        public static Button PillBtn(string text, bool active, Action onClick)
        {
            var btn = new Button
            {
                Content = text, FontFamily = FontSans, FontSize = 12,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? Pal.Accent : Pal.Text2,
                Background = active ? Pal.AccentSoft : Pal.Surface2,
                BorderBrush = active ? Pal.Accent : Pal.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 4)
            };
            btn.Template = BuildRoundedButtonTemplate(13);
            if (onClick != null) btn.Click += (s, e) => onClick();
            return btn;
        }

        static ControlTemplate BuildRoundedButtonTemplate(double radius)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var tHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            tHover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.87, "bd"));
            tpl.Triggers.Add(tHover);

            var tDis = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            tDis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42, "bd"));
            tpl.Triggers.Add(tDis);
            return tpl;
        }

        public static TextBox InputBox(string initial = "", double width = 220, bool mono = false)
        {
            var tb = new TextBox
            {
                Text = initial ?? "",
                FontFamily = mono ? FontMono : FontSans,
                FontSize = 12.4, Foreground = Pal.Text, CaretBrush = Pal.Accent,
                Background = Pal.Surface2, BorderBrush = Pal.BorderStrong, BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center
            };
            if (width > 0) tb.Width = width;
            return tb;
        }

        public static Border ProgressTrack(double pct, Brush fill, double height = 6.5)
        {
            var g = new Grid { Height = height };
            g.Children.Add(new Border { Background = Pal.Track, CornerRadius = new CornerRadius(height / 2) });
            var bar = new Border { Background = fill, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
            g.SizeChanged += (s, e) => { bar.Width = Math.Max(0, g.ActualWidth * Math.Max(0, Math.Min(100, pct)) / 100.0); };
            g.Children.Add(bar);
            return new Border { Child = g };
        }

        public static Grid RingGauge(double size, double strokeWidth, double pct, Brush stroke, string bigText, string subText)
        {
            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Pal.Track, StrokeThickness = strokeWidth });

            double frac = Math.Max(0.01, Math.Min(0.999, pct / 100.0));
            double r = (size - strokeWidth) / 2.0, cx = size / 2.0, cy = size / 2.0;
            double angleRad = frac * 2.0 * Math.PI - Math.PI / 2.0;
            var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(angleRad), cy + r * Math.Sin(angleRad)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            grid.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = stroke, StrokeThickness = strokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = bigText, FontFamily = FontMono, FontSize = size >= 90 ? 22 : (size >= 70 ? 15 : 12), FontWeight = FontWeights.Bold, Foreground = stroke, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = subText, FontFamily = FontSans, FontSize = size >= 90 ? 10.5 : 9.5, FontWeight = FontWeights.SemiBold, Foreground = Pal.Text3, HorizontalAlignment = HorizontalAlignment.Center });
            grid.Children.Add(sp);
            return grid;
        }

        public static Border KpiCard(string label, string value, string sub, Brush valColor = null, Border tag = null)
        {
            var sp = new StackPanel();
            if (tag != null)
            {
                var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
                tag.Margin = new Thickness(0, 0, 6, 0);
                top.Children.Add(tag); top.Children.Add(Txt(label, 11.5, false, Pal.Text2));
                sp.Children.Add(top);
            }
            else sp.Children.Add(Txt(label, 11.6, false, Pal.Text2));
            var v = Txt(value, 18, true, valColor ?? Pal.Text, true); v.Margin = new Thickness(0, 3, 0, 2);
            sp.Children.Add(v);
            if (!string.IsNullOrEmpty(sub)) sp.Children.Add(TxtWrap(sub, 11.2, Pal.Text3));
            return Card(sp, new Thickness(15, 13, 15, 13), 15);
        }

        public static Border SwitchToggle(bool isOn, Action<bool> onToggle)
        {
            bool cur = isOn;
            var bd = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), Background = cur ? Pal.Ok : Pal.Track, Cursor = Cursors.Hand, Padding = new Thickness(3) };
            var knob = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = cur ? HorizontalAlignment.Right : HorizontalAlignment.Left, Effect = SoftShadow(4, 1, 0.16) };
            bd.Child = knob;
            bd.MouseLeftButtonUp += (s, e) => { cur = !cur; bd.Background = cur ? Pal.Ok : Pal.Track; knob.HorizontalAlignment = cur ? HorizontalAlignment.Right : HorizontalAlignment.Left; if (onToggle != null) onToggle(cur); };
            return bd;
        }

        public static Border Stepper(int initial, int step, int min, int max, string suffix, Action<int> onChange)
        {
            int val = initial;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var lbl = Txt(val + suffix, 12, true, Pal.Text, true); lbl.MinWidth = 54; lbl.TextAlignment = TextAlignment.Center;
            var bM = Btn("−", "soft", () => { val = Math.Max(min, val - step); lbl.Text = val + suffix; if (onChange != null) onChange(val); }, true);
            var bP = Btn("+", "soft", () => { val = Math.Min(max, val + step); lbl.Text = val + suffix; if (onChange != null) onChange(val); }, true);
            bM.Padding = new Thickness(9, 2.5, 9, 2.5); bP.Padding = new Thickness(9, 2.5, 9, 2.5);
            sp.Children.Add(bM); sp.Children.Add(lbl); sp.Children.Add(bP);
            return new Border { Background = Pal.Surface2, BorderBrush = Pal.BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = sp };
        }

        public static Border MakeRow(UIElement left, string title, string sub, string sizeText, UIElement[] tags, UIElement[] actions, double barPct = 0, Brush barBrush = null)
        {
            var root = new Grid();
            if (barPct > 0.5)
            {
                var bgBar = new Border { Background = barBrush ?? Pal.AccentSoft, Opacity = 0.42, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                root.SizeChanged += (s, e) => { bgBar.Width = root.ActualWidth * Math.Min(100, barPct) / 100.0; };
                root.Children.Add(bgBar);
            }
            var content = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (left != null)
            {
                var lf = new Border { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = left };
                Grid.SetColumn(lf, 0); content.Children.Add(lf);
            }
            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var tBlk = Txt(title, 13.2, true, Pal.Text); tBlk.Margin = new Thickness(0, 0, 7, 0);
            titleRow.Children.Add(tBlk);
            if (tags != null) foreach (var tg in tags) { if (tg == null) continue; titleRow.Children.Add(new Border { Margin = new Thickness(0, 0, 5, 0), Child = tg }); }
            meta.Children.Add(titleRow);
            if (!string.IsNullOrEmpty(sub))
                meta.Children.Add(new TextBlock { Text = sub, FontFamily = FontSans, FontSize = 11.6, Foreground = Pal.Text2, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2.5, 0, 0) });
            Grid.SetColumn(meta, 1); content.Children.Add(meta);

            if (!string.IsNullOrEmpty(sizeText))
            {
                var szBlk = Txt(sizeText, 13, true, Pal.Text, true); szBlk.MinWidth = 76; szBlk.TextAlignment = TextAlignment.Right; szBlk.Margin = new Thickness(10, 0, 10, 0);
                Grid.SetColumn(szBlk, 2); content.Children.Add(szBlk);
            }
            if (actions != null && actions.Length > 0)
            {
                var actSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var a in actions) { if (a == null) continue; actSp.Children.Add(new Border { Margin = new Thickness(6, 0, 0, 0), Child = a }); }
                Grid.SetColumn(actSp, 3); content.Children.Add(actSp);
            }
            root.Children.Add(content);
            return new Border { BorderBrush = Pal.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = root };
        }

        // L2 / L3 防呆确认弹窗
        public static bool ShowGuardDialog(Window owner, string tier, string title, string desc, IList<string> items, string ackText, string keyword, string confirmText)
        {
            var dlg = new Window
            {
                Owner = owner, Title = title, Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 620,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, Background = Pal.Surface, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bool isL3 = tier == "L3";
            var bd = Badge(isL3 ? "L3 高风险反复确认" : "L2 清单二次确认", isL3 ? Pal.CritSoft : Pal.WarnSoft, isL3 ? Pal.Crit : Pal.Warn);
            bd.Margin = new Thickness(0, 0, 9, 0);
            head.Children.Add(bd); head.Children.Add(Txt(title, 15.5, true, Pal.Text));
            root.Children.Add(head);

            var descBlk = TxtWrap(desc, 12.6, Pal.Text2); descBlk.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(descBlk);

            if (items != null && items.Count > 0)
            {
                var listSp = new StackPanel();
                int take = Math.Min(items.Count, 14);
                for (int i = 0; i < take; i++) listSp.Children.Add(Txt("• " + items[i], 11.5, false, Pal.Text, true));
                if (items.Count > take) listSp.Children.Add(Txt("… 等共 " + items.Count + " 项", 11.5, false, Pal.Text3, true));
                root.Children.Add(new Border
                {
                    Background = Pal.Surface2, BorderBrush = Pal.Border, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12),
                    Child = new ScrollViewer { MaxHeight = 135, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listSp }
                });
            }

            var chk = new CheckBox { Content = TxtWrap(ackText ?? "我已逐项核对以上清单并确认执行", 12.2, Pal.Text), Foreground = Pal.Text, VerticalContentAlignment = VerticalAlignment.Center };
            root.Children.Add(new Border { Background = Pal.WarnSoft, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12), Child = chk });

            TextBox kwBox = null;
            if (!string.IsNullOrEmpty(keyword))
            {
                var kwLbl = Txt("高风险二次确认：请在下方输入「" + keyword + "」以解锁执行", 12, true, Pal.Crit); kwLbl.Margin = new Thickness(0, 0, 0, 5);
                root.Children.Add(kwLbl);
                kwBox = InputBox("", 0, false); kwBox.Margin = new Thickness(0, 0, 0, 12);
                root.Children.Add(kwBox);
            }

            bool confirmed = false;
            var btnCancel = Btn("取消", "secondary", () => dlg.Close());
            var btnOk = Btn(confirmText ?? "确认执行", isL3 ? "danger" : "primary", () => { confirmed = true; dlg.Close(); });
            btnOk.IsEnabled = false;
            Action sync = () => { btnOk.IsEnabled = chk.IsChecked == true && (string.IsNullOrEmpty(keyword) || (kwBox != null && kwBox.Text.Trim() == keyword)); };
            chk.Checked += (s, e) => sync(); chk.Unchecked += (s, e) => sync();
            if (kwBox != null) kwBox.TextChanged += (s, e) => sync();

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btnCancel.Margin = new Thickness(0, 0, 9, 0);
            foot.Children.Add(btnCancel); foot.Children.Add(btnOk);
            root.Children.Add(foot);
            dlg.Content = root;
            ApplyDialogDwm(dlg);
            dlg.ShowDialog();
            return confirmed;
        }

        // macOS 风格「软件更新」提醒弹窗（展示版本变更、更新内容日志与一键静默升级进度）
        public static void ShowUpdateDialog(Window owner, UpdateInfo info)
        {
            if (info == null) return;
            var dlg = new Window
            {
                Owner = owner, Title = "DiskGuard 软件更新", Width = 500, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, Background = Pal.Surface, ShowInTaskbar = false
            };

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var ico = SquircleIcon("M12 2L20 5.5V12.5C20 17.5 16.5 20.8 12 22C7.5 20.8 4 17.5 4 12.5V5.5L12 2Z", Pal.Accent, 44, 22);
            ico.Margin = new Thickness(0, 0, 14, 0);
            var hTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hTxt.Children.Add(Txt("发现 DiskGuard 新版本 v" + info.LatestVersion, 16.5, true, Pal.Text));
            hTxt.Children.Add(Txt(string.Format("当前版本 v{0}  →  最新版本 v{1}（静默升级保留全部配置）", info.CurrentVersion, info.LatestVersion), 12, false, Pal.Text2));
            head.Children.Add(ico); head.Children.Add(hTxt);
            root.Children.Add(head);

            var notesTitle = Txt("本次更新内容", 12.5, true, Pal.Text); notesTitle.Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(notesTitle);
            string notesText = string.IsNullOrWhiteSpace(info.Notes) ? "常规性能优化与体验提升。" : info.Notes.Trim();
            root.Children.Add(new Border
            {
                Background = Pal.Surface2, BorderBrush = Pal.Border, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 14),
                Child = new ScrollViewer { MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = TxtWrap(notesText, 12.2, Pal.Text) }
            });

            var progBar = new ProgressBar { Height = 6, Maximum = 100, Foreground = Pal.Accent, Background = Pal.Track, BorderThickness = new Thickness(0) };
            var progTxt = Txt("准备下载更新包…", 11.8, false, Pal.Text2); progTxt.Margin = new Thickness(0, 5, 0, 0);
            var progWrap = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
            progWrap.Children.Add(progBar); progWrap.Children.Add(progTxt);
            root.Children.Add(progWrap);

            Button btnLater = null, btnUp = null;
            btnLater = Btn("稍后提醒", "secondary", () => dlg.Close());
            btnLater.Margin = new Thickness(0, 0, 9, 0);
            btnUp = Btn("立即下载并自动升级", "primary", () =>
            {
                btnUp.IsEnabled = false;
                btnLater.IsEnabled = false;
                progWrap.Visibility = Visibility.Visible;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    var r = Updater.DownloadAndLaunchUpgrade(info.DownloadUrl, info.Sha256, (pct, msg) =>
                    {
                        dlg.Dispatcher.BeginInvoke(new Action(() => { progBar.Value = pct; progTxt.Text = msg; }));
                    });
                    dlg.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        progTxt.Text = r.Summary;
                        if (!r.Ok) { btnUp.IsEnabled = true; btnLater.IsEnabled = true; }
                    }));
                });
            });

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foot.Children.Add(btnLater); foot.Children.Add(btnUp);
            root.Children.Add(foot);
            dlg.Content = root;
            ApplyDialogDwm(dlg);
            dlg.ShowDialog();
        }

        static void ApplyDialogDwm(Window dlg)
        {
            dlg.SourceInitialized += (s, e) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(dlg).Handle;
                var bg = Pal.IsDark ? System.Drawing.Color.FromArgb(36, 36, 41) : System.Drawing.Color.FromArgb(255, 255, 255);
                var br = Pal.IsDark ? System.Drawing.Color.FromArgb(62, 62, 70) : System.Drawing.Color.FromArgb(220, 220, 225);
                Win32Native.ApplyDwmTheme(h, Pal.IsDark, bg, br);
            };
        }

        public static void RenderElementToPng(FrameworkElement el, int width, int height, string filePath)
        {
            el.Measure(new Size(width, height));
            el.Arrange(new Rect(0, 0, width, height));
            el.UpdateLayout();
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(filePath)) enc.Save(fs);
        }
    }
}
