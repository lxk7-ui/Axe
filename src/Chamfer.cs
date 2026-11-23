using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoMixApp
{
    [Flags]
    internal enum Cuts
    {
        None = 0,
        TopLeft = 1,
        TopRight = 2,
        BottomRight = 4,
        BottomLeft = 8,
        Diagonal = TopLeft | BottomRight,
        All = 15
    }

    // Border with cut (chamfered) corners instead of round ones: the angular "tech" frame used for panels,
    // buttons, chips and inputs. Optional accent brackets on the uncut corners give it the HUD look.
    // Works like Border: one child, Padding, hit-testable wherever Background is set (Transparent counts).
    internal sealed class Chamfer : Decorator
    {
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            "Background", typeof(Brush), typeof(Chamfer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
            "BorderBrush", typeof(Brush), typeof(Chamfer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
            "BorderThickness", typeof(double), typeof(Chamfer), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CutProperty = DependencyProperty.Register(
            "Cut", typeof(double), typeof(Chamfer), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CornersProperty = DependencyProperty.Register(
            "Corners", typeof(Cuts), typeof(Chamfer), new FrameworkPropertyMetadata(Cuts.Diagonal, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
            "Padding", typeof(Thickness), typeof(Chamfer), new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            "Accent", typeof(Brush), typeof(Chamfer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty AccentSizeProperty = DependencyProperty.Register(
            "AccentSize", typeof(double), typeof(Chamfer), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Background { get { return (Brush)GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public Brush BorderBrush { get { return (Brush)GetValue(BorderBrushProperty); } set { SetValue(BorderBrushProperty, value); } }
        public double BorderThickness { get { return (double)GetValue(BorderThicknessProperty); } set { SetValue(BorderThicknessProperty, value); } }
        public double Cut { get { return (double)GetValue(CutProperty); } set { SetValue(CutProperty, value); } }
        public Cuts Corners { get { return (Cuts)GetValue(CornersProperty); } set { SetValue(CornersProperty, value); } }
        public Thickness Padding { get { return (Thickness)GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public Brush Accent { get { return (Brush)GetValue(AccentProperty); } set { SetValue(AccentProperty, value); } }
        public double AccentSize { get { return (double)GetValue(AccentSizeProperty); } set { SetValue(AccentSizeProperty, value); } }

        public Chamfer()
        {
            SnapsToDevicePixels = true;
        }

        protected override Size MeasureOverride(Size avail)
        {
            Thickness pad = Padding;
            double b = BorderThickness;
            double w = pad.Left + pad.Right + 2 * b, h = pad.Top + pad.Bottom + 2 * b;
            if (Child == null) return new Size(w, h);
            Child.Measure(new Size(Math.Max(0, avail.Width - w), Math.Max(0, avail.Height - h)));
            return new Size(Child.DesiredSize.Width + w, Child.DesiredSize.Height + h);
        }

        protected override Size ArrangeOverride(Size size)
        {
            if (Child != null)
            {
                Thickness pad = Padding;
                double b = BorderThickness;
                Child.Arrange(new Rect(pad.Left + b, pad.Top + b,
                    Math.Max(0, size.Width - pad.Left - pad.Right - 2 * b), Math.Max(0, size.Height - pad.Top - pad.Bottom - 2 * b)));
            }
            return size;
        }

        // The outline, inset by half the pen so the stroke stays inside the element.
        public static Geometry Outline(double w, double h, double cut, Cuts corners, double inset)
        {
            double c = Math.Max(0, Math.Min(cut, Math.Min(w, h) / 2));
            double l = inset, t = inset, r = Math.Max(l, w - inset), b = Math.Max(t, h - inset);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool tl = (corners & Cuts.TopLeft) != 0, tr = (corners & Cuts.TopRight) != 0;
                bool br = (corners & Cuts.BottomRight) != 0, bl = (corners & Cuts.BottomLeft) != 0;
                ctx.BeginFigure(new Point(tl ? l + c : l, t), true, true);
                ctx.LineTo(new Point(tr ? r - c : r, t), true, false);
                if (tr) ctx.LineTo(new Point(r, t + c), true, false);
                ctx.LineTo(new Point(r, br ? b - c : b), true, false);
                if (br) ctx.LineTo(new Point(r - c, b), true, false);
                ctx.LineTo(new Point(bl ? l + c : l, b), true, false);
                if (bl) ctx.LineTo(new Point(l, b - c), true, false);
                ctx.LineTo(new Point(l, tl ? t + c : t), true, false);
            }
            g.Freeze();
            return g;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double bt = BorderBrush != null ? BorderThickness : 0;
            Pen pen = bt > 0 ? new Pen(BorderBrush, bt) : null;
            dc.DrawGeometry(Background, pen, Outline(w, h, Cut, Corners, bt / 2));
            Brush accent = Accent;
            if (accent == null) return;
            // Brackets on the corners that are not cut, a short bar along each cut.
            double a = Math.Min(AccentSize, Math.Min(w, h) / 3), k = 2.0;
            var ap = new Pen(accent, k);
            double o = k / 2;
            Cuts cs = Corners;
            if ((cs & Cuts.TopLeft) == 0) Bracket(dc, ap, new Point(o, o), a, 1, 1);
            if ((cs & Cuts.TopRight) == 0) Bracket(dc, ap, new Point(w - o, o), a, -1, 1);
            if ((cs & Cuts.BottomRight) == 0) Bracket(dc, ap, new Point(w - o, h - o), a, -1, -1);
            if ((cs & Cuts.BottomLeft) == 0) Bracket(dc, ap, new Point(o, h - o), a, 1, -1);
            double c = Math.Max(0, Math.Min(Cut, Math.Min(w, h) / 2));
            if (c >= 6)
            {
                double m = c * 0.32;
                if ((cs & Cuts.TopLeft) != 0) dc.DrawLine(ap, new Point(m, c - m + o), new Point(c - m + o, m));
                if ((cs & Cuts.BottomRight) != 0) dc.DrawLine(ap, new Point(w - c + m - o, h - m), new Point(w - m, h - c + m - o));
                if ((cs & Cuts.TopRight) != 0) dc.DrawLine(ap, new Point(w - c + m - o, m), new Point(w - m, c - m + o));
                if ((cs & Cuts.BottomLeft) != 0) dc.DrawLine(ap, new Point(m, h - c + m - o), new Point(c - m + o, h - m));
            }
        }

        private static void Bracket(DrawingContext dc, Pen pen, Point p, double len, int dx, int dy)
        {
            dc.DrawLine(pen, p, new Point(p.X + dx * len, p.Y));
            dc.DrawLine(pen, p, new Point(p.X, p.Y + dy * len));
        }
    }
}
