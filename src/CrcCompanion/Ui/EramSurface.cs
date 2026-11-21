using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Draws an ERAM element in code. The same Paint function runs for measuring (no DrawingContext)
    /// and rendering, so the window size, the drawing and the click areas always agree.
    /// </summary>
    public sealed class EramSurface : FrameworkElement
    {
        private readonly List<(Rect Rect, Action<MouseButton> Action)> _hits = new List<(Rect, Action<MouseButton>)>();

        public Func<Painter, Size>? Paint { get; set; }
        public Func<bool>? ClicksSuppressed { get; set; }
        public Action<int, Point>? Wheel { get; set; }

        public EramSurface()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        private readonly List<(double Y, double H, object Tag)> _tags = new List<(double, double, object)>();

        internal void AddHit(Rect r, Action<MouseButton> a) => _hits.Add((r, a));
        internal void AddTag(double y, double h, object tag) => _tags.Add((y, h, tag));

        /// <summary>Returns the object tagged at a vertical position (used for wheel input on rows).</summary>
        public object? RowAt(double y)
        {
            foreach (var t in _tags)
                if (y >= t.Y && y < t.Y + t.H) return t.Tag;
            return null;
        }

        public void Refresh()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        private double Ppd => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        protected override Size MeasureOverride(Size availableSize) =>
            Paint?.Invoke(new Painter(null, this, Ppd)) ?? new Size(1, 1);

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        protected override void OnRender(DrawingContext dc)
        {
            _hits.Clear();
            _tags.Clear();
            Paint?.Invoke(new Painter(dc, this, Ppd));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            e.Handled = true;
            if (ClicksSuppressed?.Invoke() == true) return;
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;
            var p = e.GetPosition(this);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Rect.Contains(p))
                {
                    _hits[i].Action(e.ChangedButton);
                    return;
                }
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            e.Handled = true;
            Wheel?.Invoke(e.Delta, e.GetPosition(this));
        }
    }

    /// <summary>Thin drawing helper. Every call is a no-op during the measure pass.</summary>
    public sealed class Painter
    {
        private readonly DrawingContext? _dc;
        private readonly EramSurface _surface;
        public double Ppd { get; }
        public bool Drawing => _dc != null;

        internal Painter(DrawingContext? dc, EramSurface surface, double ppd)
        {
            _dc = dc;
            _surface = surface;
            Ppd = ppd;
        }

        /// <summary>A painter with the same metrics that draws nothing (for sizing sub-areas).</summary>
        public Painter MeasureOnly() => new Painter(null, _surface, Ppd);

        public double CharW(double size) => Eram.CharWidth(size, Ppd);
        public double LineH(double size) => Eram.LineHeight(size);
        public double TextW(string s, double size) => s.Length * CharW(size);

        public void Fill(Brush b, double x, double y, double w, double h)
        {
            if (_dc != null && w > 0 && h > 0) _dc.DrawRectangle(b, null, new Rect(x, y, w, h));
        }

        /// <summary>Rectangle outline drawn as filled strips, which keeps 1 px lines crisp.</summary>
        public void Frame(Brush b, double x, double y, double w, double h, double t = 1)
        {
            Fill(b, x, y, w, t);
            Fill(b, x, y + h - t, w, t);
            Fill(b, x, y, t, h);
            Fill(b, x + w - t, y, t, h);
        }

        /// <summary>Filled triangle centered in a box, pointing up or down.</summary>
        public void Triangle(Brush b, double x, double y, double w, double h, bool up)
        {
            if (_dc == null) return;
            double s = Math.Min(w, h) * 0.5, cx = x + w / 2, cy = y + h / 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (up)
                {
                    ctx.BeginFigure(new Point(cx, cy - s / 2), true, true);
                    ctx.LineTo(new Point(cx + s / 2, cy + s / 2), true, false);
                    ctx.LineTo(new Point(cx - s / 2, cy + s / 2), true, false);
                }
                else
                {
                    ctx.BeginFigure(new Point(cx, cy + s / 2), true, true);
                    ctx.LineTo(new Point(cx + s / 2, cy - s / 2), true, false);
                    ctx.LineTo(new Point(cx - s / 2, cy - s / 2), true, false);
                }
            }
            g.Freeze();
            _dc.DrawGeometry(b, null, g);
        }

        /// <summary>Draws text left-aligned at x, vertically centered in a row of height rowH starting at y.</summary>
        public void Text(string s, double x, double y, double size, Brush b, double rowH)
        {
            if (_dc == null || string.IsNullOrEmpty(s)) return;
            var ft = Eram.Format(s, size, b, Ppd);
            _dc.DrawText(ft, new Point(Math.Round(x), Math.Round(y + (rowH - ft.Height) / 2)));
        }

        public void TextCentered(string s, double x, double y, double w, double size, Brush b, double rowH)
        {
            var tw = TextW(s, size);
            Text(s, x + (w - tw) / 2, y, size, b, rowH);
        }

        /// <summary>Underlines n characters starting at character column col of text drawn at x.</summary>
        public void Underline(double x, double y, double rowH, int col, int n, double size, Brush b)
        {
            var cw = CharW(size);
            var baseY = Math.Round(y + rowH - Math.Max(2, rowH * 0.12));
            Fill(b, Math.Round(x + col * cw), baseY, Math.Round(n * cw), 1);
        }

        public void Hit(double x, double y, double w, double h, Action<MouseButton> a)
        {
            if (_dc != null) _surface.AddHit(new Rect(x, y, w, h), a);
        }

        public void Tag(object tag, double y, double h)
        {
            if (_dc != null) _surface.AddTag(y, h, tag);
        }
    }
}
