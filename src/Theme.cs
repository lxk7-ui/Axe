using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace VideoMixApp
{
    // One colour scheme (背景色, v2.8.0). Every window takes its colours from Application resources keyed
    // by K.*, through DynamicResource, so switching the scheme repaints all open windows at once, also
    // while a task runs. Same schemes as 素材筛选器, so the two tools look alike side by side.
    internal sealed class Palette
    {
        public string Id, Name;
        public bool Dark;
        public Color Page, Grid, Panel, Panel2, Panel3, Line, LineHi;
        public Color Text, TextSub, TextHint;
        public Color Accent, Accent2, OnAccent;
        public Color Keep, Warn, Danger, Running;
        public Color LogBg, LogText, LogDim;
    }

    // Resource keys.
    internal static class K
    {
        public const string Page = "ax.page", PageSolid = "ax.pageSolid", Panel = "ax.panel", Panel2 = "ax.panel2", Panel3 = "ax.panel3";
        public const string Line = "ax.line", LineHi = "ax.lineHi";
        public const string Text = "ax.text", TextSub = "ax.textSub", TextHint = "ax.textHint";
        public const string Accent = "ax.accent", Accent2 = "ax.accent2", OnAccent = "ax.onAccent", AccentSoft = "ax.accentSoft", AccentLine = "ax.accentLine";
        public const string StartBg = "ax.startBg", StartHover = "ax.startHover", StartPress = "ax.startPress";
        public const string Keep = "ax.keep", Warn = "ax.warn", Danger = "ax.danger", Running = "ax.running";
        public const string Folder = "ax.folder", Video = "ax.video";
        public const string LogBg = "ax.logBg", LogText = "ax.logText", LogDim = "ax.logDim";
        public const string Frame = "ax.frame";
        public const string Clear = "ax.clear";

        // Translucent fill / outline of a colour key (chips, selection, warning outlines).
        public static string Soft(string key) { return key + ".soft"; }
        public static string Edge(string key) { return key + ".edge"; }
    }

    internal static class Theme
    {
        public static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI, Microsoft YaHei");
        // DIN-like face shipped with Windows 10/11 for numbers and Latin headings; Chinese falls back per character.
        public static readonly FontFamily TechFont = new FontFamily("Bahnschrift, Microsoft YaHei UI, Microsoft YaHei");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        public static readonly List<Palette> Palettes = new List<Palette>
        {
            new Palette
            {
                Id = "deep", Name = "深空蓝", Dark = true,
                Page = Hex("#0A0F1A"), Grid = Hex("#14213A"), Panel = Hex("#0F1727"), Panel2 = Hex("#152036"), Panel3 = Hex("#1B2A45"),
                Line = Hex("#1F3050"), LineHi = Hex("#2E4D78"), Text = Hex("#E3EBF6"), TextSub = Hex("#93A4BD"), TextHint = Hex("#5E7090"),
                Accent = Hex("#22D3EE"), Accent2 = Hex("#3B82F6"), OnAccent = Hex("#03121A"),
                Keep = Hex("#34D399"), Warn = Hex("#FBBF24"), Danger = Hex("#F43F5E"), Running = Hex("#F59E0B"),
                LogBg = Hex("#060A12"), LogText = Hex("#C8D3E3"), LogDim = Hex("#6B7C96")
            },
            new Palette
            {
                Id = "graphite", Name = "石墨黑", Dark = true,
                Page = Hex("#0D0E10"), Grid = Hex("#1B1E22"), Panel = Hex("#141619"), Panel2 = Hex("#1B1E22"), Panel3 = Hex("#23272D"),
                Line = Hex("#2A2E35"), LineHi = Hex("#414852"), Text = Hex("#ECEEF1"), TextSub = Hex("#A0A6AF"), TextHint = Hex("#6A707A"),
                Accent = Hex("#2EE6A8"), Accent2 = Hex("#14B8A6"), OnAccent = Hex("#03140E"),
                Keep = Hex("#4ADE80"), Warn = Hex("#FACC15"), Danger = Hex("#EF4444"), Running = Hex("#FB923C"),
                LogBg = Hex("#08090A"), LogText = Hex("#D0D4D9"), LogDim = Hex("#6A707A")
            },
            new Palette
            {
                Id = "violet", Name = "暗夜紫", Dark = true,
                Page = Hex("#0C0917"), Grid = Hex("#1C1735"), Panel = Hex("#130F25"), Panel2 = Hex("#1A1532"), Panel3 = Hex("#231C42"),
                Line = Hex("#2A2350"), LineHi = Hex("#463B80"), Text = Hex("#ECE8FA"), TextSub = Hex("#A69DC9"), TextHint = Hex("#6E6595"),
                Accent = Hex("#C084FC"), Accent2 = Hex("#818CF8"), OnAccent = Hex("#14061F"),
                Keep = Hex("#4ADE80"), Warn = Hex("#FBBF24"), Danger = Hex("#F43F5E"), Running = Hex("#F59E0B"),
                LogBg = Hex("#08060F"), LogText = Hex("#D6D0EE"), LogDim = Hex("#6E6595")
            },
            new Palette
            {
                Id = "ember", Name = "熔岩橙", Dark = true,
                Page = Hex("#110C09"), Grid = Hex("#241A13"), Panel = Hex("#19130F"), Panel2 = Hex("#211913"), Panel3 = Hex("#2B2119"),
                Line = Hex("#3A2B20"), LineHi = Hex("#5C4231"), Text = Hex("#F4ECE6"), TextSub = Hex("#BBA797"), TextHint = Hex("#7E6B5D"),
                Accent = Hex("#FF8A3D"), Accent2 = Hex("#F43F5E"), OnAccent = Hex("#1A0A02"),
                Keep = Hex("#4ADE80"), Warn = Hex("#FACC15"), Danger = Hex("#EF4444"), Running = Hex("#FBBF24"),
                LogBg = Hex("#0A0705"), LogText = Hex("#E6DAD0"), LogDim = Hex("#7E6B5D")
            },
            new Palette
            {
                Id = "light", Name = "极光白", Dark = false,
                Page = Hex("#E8EDF3"), Grid = Hex("#D6DEE8"), Panel = Hex("#FFFFFF"), Panel2 = Hex("#F2F5F9"), Panel3 = Hex("#E6ECF3"),
                Line = Hex("#D2DBE6"), LineHi = Hex("#9DB1C9"), Text = Hex("#142031"), TextSub = Hex("#53647A"), TextHint = Hex("#8B9AAD"),
                Accent = Hex("#0284C7"), Accent2 = Hex("#2563EB"), OnAccent = Hex("#FFFFFF"),
                Keep = Hex("#059669"), Warn = Hex("#D97706"), Danger = Hex("#DC2626"), Running = Hex("#EA580C"),
                LogBg = Hex("#0F1724"), LogText = Hex("#CBD5E1"), LogDim = Hex("#64748B")
            },
            new Palette
            {
                Id = "steel", Name = "钢铁灰", Dark = false,
                Page = Hex("#D5DAE0"), Grid = Hex("#C4CBD3"), Panel = Hex("#EEF1F4"), Panel2 = Hex("#E2E6EB"), Panel3 = Hex("#D7DCE2"),
                Line = Hex("#BAC3CD"), LineHi = Hex("#8995A5"), Text = Hex("#18202A"), TextSub = Hex("#4C5867"), TextHint = Hex("#7B8795"),
                Accent = Hex("#0F766E"), Accent2 = Hex("#0E7490"), OnAccent = Hex("#FFFFFF"),
                Keep = Hex("#047857"), Warn = Hex("#B45309"), Danger = Hex("#B91C1C"), Running = Hex("#C2410C"),
                LogBg = Hex("#151B23"), LogText = Hex("#CDD5DF"), LogDim = Hex("#6B7785")
            }
        };

        public static Palette Current { get; private set; }

        // Raised after the colours changed (for the few things that hold a Color, not a brush: glows).
        public static event Action Changed;

        public static Palette Find(string id)
        {
            foreach (var p in Palettes) if (p.Id == id) return p;
            return Palettes[0];
        }

        // Puts the scheme's brushes where every window finds them (the application, or a lone window in tests).
        public static void Apply(string id, ResourceDictionary target = null)
        {
            Palette p = Find(id);
            ResourceDictionary rd = target ?? (Application.Current != null ? Application.Current.Resources : null);
            if (rd == null) return;
            Current = p;
            Put(rd, K.PageSolid, p.Page);
            rd[K.Page] = GridBrush(p);
            Put(rd, K.Panel, p.Panel);
            Put(rd, K.Panel2, p.Panel2);
            Put(rd, K.Panel3, p.Panel3);
            Put(rd, K.Line, p.Line);
            Put(rd, K.LineHi, p.LineHi);
            Put(rd, K.Text, p.Text);
            Put(rd, K.TextSub, p.TextSub);
            Put(rd, K.TextHint, p.TextHint);
            Put(rd, K.Accent2, p.Accent2);
            Put(rd, K.OnAccent, p.OnAccent);
            Put(rd, K.AccentSoft, A(p.Accent, p.Dark ? 0x26 : 0x1C));
            Put(rd, K.AccentLine, A(p.Accent, 0x8C));
            rd[K.StartBg] = LG(p.Accent, Mix(p.Accent, p.Accent2, 0.55), new Point(0, 0), new Point(1, 1));
            rd[K.StartHover] = LG(Mix(p.Accent, Colors.White, 0.18), Mix(p.Accent, p.Accent2, 0.35), new Point(0, 0), new Point(1, 1));
            Put(rd, K.StartPress, Mix(p.Accent, p.Accent2, 0.7));
            foreach (var kv in new[]
            {
                Tuple.Create(K.Keep, p.Keep), Tuple.Create(K.Warn, p.Warn), Tuple.Create(K.Danger, p.Danger),
                Tuple.Create(K.Running, p.Running), Tuple.Create(K.Accent, p.Accent), Tuple.Create(K.TextHint, p.TextHint)
            })
            {
                Put(rd, kv.Item1, kv.Item2);
                Put(rd, K.Soft(kv.Item1), A(kv.Item2, p.Dark ? 0x2E : 0x22));
                Put(rd, K.Edge(kv.Item1), A(kv.Item2, 0x99));
            }
            Put(rd, K.Folder, p.Dark ? Hex("#F5B544") : Hex("#E39A1B"));
            Put(rd, K.Video, Mix(p.Accent, p.TextSub, 0.45));
            Put(rd, K.LogBg, p.LogBg);
            Put(rd, K.LogText, p.LogText);
            Put(rd, K.LogDim, p.LogDim);
            // Behind the 成片效果 preview and the rule window's picture: always dark, like a monitor.
            rd[K.Frame] = LG(Mix(p.LogBg, p.Panel3, 0.5), p.LogBg, new Point(0, 0), new Point(0, 1));
            rd[K.Clear] = Brushes.Transparent;
            if (target == null && Changed != null) Changed();
        }

        // Windows built before the main window applied the saved scheme (or by test tools) still get colours.
        public static void Ensure(FrameworkElement w)
        {
            if (Application.Current != null)
            {
                if (!Application.Current.Resources.Contains(K.Text)) Apply(Current != null ? Current.Id : null);
            }
            else if (!w.Resources.Contains(K.Text))
            {
                Apply(Current != null ? Current.Id : null, w.Resources);
            }
        }

        public static Color ColorOf(string key)
        {
            Palette p = Current ?? Palettes[0];
            switch (key)
            {
                case K.Keep: return p.Keep;
                case K.Warn: return p.Warn;
                case K.Danger: return p.Danger;
                case K.Running: return p.Running;
                case K.TextHint: return p.TextHint;
                case K.TextSub: return p.TextSub;
                case K.Text: return p.Text;
                default: return p.Accent;
            }
        }

        // Faint grid on the page: the "instrument panel" look, tiled every 28 px.
        private static Brush GridBrush(Palette p)
        {
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(28, 0.5)));
            lines.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, 28)));
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(SB(p.Page), null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
            dg.Children.Add(new GeometryDrawing(null, new Pen(SB(A(p.Grid, p.Dark ? 0x9A : 0xB0)), 1.0), lines));
            var b = new DrawingBrush(dg)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 28, 28),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 28, 28),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            b.Freeze();
            return b;
        }

        private static void Put(ResourceDictionary rd, string key, Color c)
        {
            rd[key] = SB(c);
        }

        public static Color Hex(string s)
        {
            return (Color)ColorConverter.ConvertFromString(s);
        }

        public static Color A(Color c, byte alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        public static Color A(Color c, int alpha)
        {
            return A(c, (byte)alpha);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // ---------------------------------------------------------------- dynamic colour helpers
        // el.Fg(K.Text) = el.SetResourceReference(Foreground, K.Text), usable inside expressions.
        public static T Res<T>(this T el, DependencyProperty dp, string key) where T : FrameworkElement
        {
            el.SetResourceReference(dp, key);
            return el;
        }

        public static T Fg<T>(this T el, string key) where T : FrameworkElement
        {
            // TextBlock.Foreground and Control.Foreground are the same property (TextElement.Foreground).
            el.SetResourceReference(TextElement.ForegroundProperty, key);
            return el;
        }

        public static T Bg<T>(this T el, string key) where T : FrameworkElement
        {
            if (el is Border) el.SetResourceReference(Border.BackgroundProperty, key);
            else if (el is Panel) el.SetResourceReference(Panel.BackgroundProperty, key);
            else if (el is Chamfer) el.SetResourceReference(Chamfer.BackgroundProperty, key);
            else if (el is TextBlock) el.SetResourceReference(TextBlock.BackgroundProperty, key);
            else el.SetResourceReference(Control.BackgroundProperty, key);
            return el;
        }

        public static T Bd<T>(this T el, string key) where T : FrameworkElement
        {
            if (el is Border) el.SetResourceReference(Border.BorderBrushProperty, key);
            else if (el is Chamfer) el.SetResourceReference(Chamfer.BorderBrushProperty, key);
            else el.SetResourceReference(Control.BorderBrushProperty, key);
            return el;
        }

        public static T Fill<T>(this T el, string key) where T : Shape
        {
            el.SetResourceReference(Shape.FillProperty, key);
            return el;
        }

        public static T Stroke<T>(this T el, string key) where T : Shape
        {
            el.SetResourceReference(Shape.StrokeProperty, key);
            return el;
        }

        public static Setter DynSetter(DependencyProperty dp, string key, string target = null)
        {
            return target == null ? new Setter(dp, new DynamicResourceExtension(key)) : new Setter(dp, new DynamicResourceExtension(key), target);
        }

        // ---------------------------------------------------------------- icons (24-unit boxes, angular)
        public const string IconMin = "M5,12 H19";
        public const string IconMax = "M5,5 H19 V19 H5 Z";
        public const string IconClose = "M7,7 L17,17 M17,7 L7,17";
        public const string IconRestore = "M7,5 H14 V12 H7 Z M10,10 H19 V18 H10 Z";
        public const string IconPlay = "M7,4 V20 L20,12 Z";
        public const string IconStop = "M6,8 L8,6 H16 L18,8 V16 L16,18 H8 L6,16 Z";
        public const string IconFolder = "M2,5 L4,3 H10 L12,5 H20 L22,7 V19 L20,21 H4 L2,19 Z";
        public const string IconVideo = "M3,6 L5,4 H15 L17,6 V10 L22,6.5 V17.5 L17,14 V18 L15,20 H5 L3,18 Z";
        public const string IconBolt = "M13,2 L4,14 H11 L10,22 L20,9 H13 L13,2 Z";
        public const string IconRefresh = "M17.65,6.35 C16.2,4.9 14.21,4 12,4 C7.58,4 4.01,7.58 4.01,12 C4.01,16.42 7.58,20 12,20 C15.73,20 18.84,17.45 19.73,14 H17.65 C16.83,16.33 14.61,18 12,18 C8.69,18 6.01,15.31 6.01,12 C6.01,8.69 8.69,6 12,6 C13.66,6 15.14,6.69 16.22,7.78 L13,11 H20 V4 L17.65,6.35 Z";
        public const string IconClock = "M12,2 L19,6 V18 L12,22 L5,18 V6 Z M11,7 V13 L15.5,15.6 L16.3,14.3 L12.5,12.1 V7 Z";
        public const string IconAspect = "M7,14 L5,14 L5,19 L10,19 L10,17 L7,17 Z M17,14 L19,14 L19,19 L14,19 L14,17 L17,17 Z M19,5 L14,5 L14,7 L17,7 L17,10 L19,10 Z M7,7 L10,7 L10,5 L5,5 L5,10 L7,10 Z";
        public const string IconSparkle = "M11,2 L12.9,8.1 L19,10 L12.9,11.9 L11,18 L9.1,11.9 L3,10 L9.1,8.1 Z M18.5,14 L19.4,16.6 L22,17.5 L19.4,18.4 L18.5,21 L17.6,18.4 L15,17.5 L17.6,16.6 Z";
        public const string IconLayers = "M12,2 L2,7 L12,12 L22,7 L12,2 Z M2,17 L12,22 L22,17 M2,12 L12,17 L22,12";
        // 任务队列: a list with a play mark.
        public const string IconQueue = "M3,5 H16 V7.2 H3 Z M3,10.4 H16 V12.6 H3 Z M3,15.8 H11 V18 H3 Z M14,13.5 V21.5 L21,17.5 Z";
        public const string IconTag = "M21.4,11.6 L12.4,2.6 C12,2.2 11.5,2 11,2 H4 C2.9,2 2,2.9 2,4 V11 C2,11.5 2.2,12 2.6,12.4 L11.6,21.4 C12,21.8 12.5,22 13,22 C13.5,22 14,21.8 14.4,21.4 L21.4,14.4 C21.8,14 22,13.5 22,13 C22,12.5 21.8,12 21.4,11.6 Z M6.5,8 C5.7,8 5,7.3 5,6.5 C5,5.7 5.7,5 6.5,5 C7.3,5 8,5.7 8,6.5 C8,7.3 7.3,8 6.5,8 Z";
        // Message dialogs: information, question, warning (filled; the marks are holes).
        public const string IconInfo = "M12,2 L21,7 V17 L12,22 L3,17 V7 Z M11,10 V17 H13 V10 Z M11,6.5 V8.5 H13 V6.5 Z";
        public const string IconHelp = "M12,2 L21,7 V17 L12,22 L3,17 V7 Z M11,16.5 V18.5 H13 V16.5 Z M12,5.5 C9.8,5.5 8.4,6.9 8.4,8.9 H10.4 C10.4,8 11,7.4 12,7.4 C13,7.4 13.6,8 13.6,8.8 C13.6,10.4 11,10.3 11,13.2 V14.5 H13 V13.6 C13,11.7 15.6,11.4 15.6,8.7 C15.6,6.8 14.1,5.5 12,5.5 Z";
        public const string IconAlert = "M12,2 L23,21 H1 Z M11,9 V15 H13 V9 Z M11,17 V19 H13 V17 Z";
        public const string IconChevronUp = "M7.4,14 L12,9.4 L16.6,14 L18,12.6 L12,6.6 L6,12.6 Z";
        public const string IconChevronDown = "M7.4,10 L12,14.6 L16.6,10 L18,11.4 L12,17.4 L6,11.4 Z";
        public const string IconTerminal = "M2,6 L4,4 H20 L22,6 V18 L20,20 H4 L2,18 Z M4,8 V18 H20 V8 Z M6,10 L8.5,12.5 L6,15 L7.4,16.4 L11.3,12.5 L7.4,8.6 Z M12,15 H17 V16.8 H12 Z";
        public const string IconPalette = "M4,4 H11 V11 H4 Z M13,4 H20 V11 H13 Z M4,13 H11 V20 H4 Z M13,13 L20,13 L20,20 L13,20 Z";
        // Picker-only icons.
        public const string IconDrive = "M2,8 L4,6 H20 L22,8 V16 L20,18 H4 L2,16 Z M17,11 A1.5,1.5 0 1 0 17,14 A1.5,1.5 0 1 0 17,11 Z";
        public const string IconFile = "M6,2 H14 L20,8 V22 H4 V4 Z M13,3.5 V9 H18.5 Z";
        public const string IconSearch = "M15.5,14 H14.71 L14.43,13.73 C15.41,12.59 16,11.11 16,9.5 C16,5.91 13.09,3 9.5,3 C5.91,3 3,5.91 3,9.5 C3,13.09 5.91,16 9.5,16 C11.11,16 12.59,15.41 13.73,14.43 L14,14.71 V15.5 L19,20.49 L20.49,19 L15.5,14 Z M9.5,14 C7.01,14 5,11.99 5,9.5 C5,7.01 7.01,5 9.5,5 C11.99,5 14,7.01 14,9.5 C14,11.99 11.99,14 9.5,14 Z";
        public const string IconArrowUp = "M4,12 L5.41,13.41 L11,7.83 V20 H13 V7.83 L18.58,13.42 L20,12 L12,4 Z";
        public const string IconComputer = "M2,6 L4,4 H20 L22,6 V16 L20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 L2,16 Z M4,6 V16 H20 V6 Z";
        // Logo: an angular "A" cut out of a chamfered badge (same drawing as Axe.ico, see make-icon.ps1).
        public const string LogoBadge = "M6,0 H24 L30,6 V24 L24,30 H6 L0,24 V6 Z";
        public const string LogoA = "M8,23 L13.2,7 H16.8 L22,23 H18.4 L17.3,19.4 H12.7 L11.6,23 Z M13.3,16.6 H16.7 L15,10.8 Z M2,7.4 L7.4,2 L8.6,3.2 L3.2,8.6 Z";

        public static SolidColorBrush SB(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush SB(byte a, byte r, byte g, byte b)
        {
            return SB(Color.FromArgb(a, r, g, b));
        }

        public static LinearGradientBrush LG(Color a, Color b, Point s, Point e)
        {
            var g = new LinearGradientBrush();
            g.StartPoint = s;
            g.EndPoint = e;
            g.GradientStops.Add(new GradientStop(a, 0.0));
            g.GradientStops.Add(new GradientStop(b, 1.0));
            g.Freeze();
            return g;
        }

        public static Geometry Geo(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        public static Path MakeIcon(string data, string brushKey, double size)
        {
            var p = new Path
            {
                Data = Geo(data),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            p.SetResourceReference(Shape.FillProperty, brushKey);
            return p;
        }

        public static Path MakeIcon(string data, Brush fill, double size)
        {
            return new Path
            {
                Data = Geo(data),
                Fill = fill,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        public static DropShadowEffect Shadow(Color c, double blur, double depth, double opacity)
        {
            return new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = depth, Opacity = opacity };
        }

        // ---------------------------------------------------------------- shared shapes
        // The badge with the cut-out "A" (same drawing as Axe.ico).
        public static FrameworkElement Logo(double size)
        {
            var g = new Grid { Width = 30.0, Height = 30.0 };
            g.Children.Add(new Path { Data = Geo(LogoBadge) }.Fill(K.StartBg));
            g.Children.Add(new Path { Data = Geo(LogoA) }.Fill(K.OnAccent));
            return new Viewbox { Width = size, Height = size, Child = g, VerticalAlignment = VerticalAlignment.Center };
        }

        // A card: panel colour, cut corners, a hairline border and accent brackets on the uncut corners.
        public static Chamfer Card(Cuts corners, double cut = 16.0)
        {
            var c = new Chamfer { Cut = cut, Corners = corners, BorderThickness = 1.0, AccentSize = 14.0 }.Bg(K.Panel).Bd(K.Line);
            c.SetResourceReference(Chamfer.AccentProperty, K.AccentLine);
            return c;
        }

        // Section heading: accent block, title, then a hairline running to the right edge.
        public static Grid SectionHead(string text, double top, double bottom = 9.0)
        {
            var g = new Grid { Margin = new Thickness(0.0, top, 0.0, bottom) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.Children.Add(new Chamfer { Width = 8.0, Height = 8.0, Cut = 3.0, Corners = Cuts.TopRight, BorderThickness = 0.0, VerticalAlignment = VerticalAlignment.Center }.Bg(K.Accent));
            var t = new TextBlock { Text = text, FontSize = 13.0, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8.0, 0.0, 10.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Fg(K.Text);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var line = new Border { Height = 1.0, VerticalAlignment = VerticalAlignment.Center }.Bg(K.Line);
            Grid.SetColumn(line, 2);
            g.Children.Add(line);
            return g;
        }

        // The frame of a dialog window: cut corners, accent brackets, a soft shadow; added to root.
        // v2.8.3: the shadow comes from an empty copy of the frame behind it, kept as a bitmap, so a
        // change inside the dialog (a progress bar, a list) does not make WPF draw and blur it all again.
        public static Chamfer DialogFrame(Grid root)
        {
            var shadow = new Chamfer { Margin = new Thickness(12.0), Cut = 14.0, BorderThickness = 0.0, IsHitTestVisible = false }.Bg(K.Panel);
            shadow.Effect = Shadow(Colors.Black, 22.0, 0.0, 0.45);
            var host = new Grid { IsHitTestVisible = false, CacheMode = new BitmapCache() };
            host.Children.Add(shadow);
            root.Children.Add(host);
            var f = new Chamfer { Margin = new Thickness(12.0), Cut = 14.0, BorderThickness = 1.0, AccentSize = 14.0 }.Bg(K.Panel).Bd(K.LineHi);
            f.SetResourceReference(Chamfer.AccentProperty, K.Accent);
            root.Children.Add(f);
            return f;
        }

        // Thin vertical separator.
        public static Border VLine(double height)
        {
            return new Border { Width = 1.0, Height = height, VerticalAlignment = VerticalAlignment.Center }.Bg(K.Line);
        }
    }
}
