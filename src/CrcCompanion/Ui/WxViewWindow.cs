using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM Weather Station Report View, laid out to CRC's measured geometry (FONT 2):
    /// "STN   HHMM " + METAR body wrapped at 25 characters, 17 px lines, 16 px extra between
    /// reports, a marker on each report's first line. LINES = most text lines shown; the view
    /// shrinks to its content, and the scroll bar appears only when reports overflow LINES.
    /// '&gt;' marks a report cut off at the bottom.
    /// </summary>
    public sealed class WxViewWindow : ListViewWindow
    {
        public const int WrapChars = 25;
        private const int StationChars = 6;
        /// <summary>Space kept at the right for the scroll bar, as CRC does even when it's hidden.</summary>
        private const int ScrollRightPad = 4;
        private int _scroll; // first visible text line

        private sealed class WxLine
        {
            public int Entry;
            public string Text = "";
            public bool First;
            public bool StaleTime;
            public string Station = "";
        }

        public WxViewWindow(IViewHost host, ViewSettings settings) : base(host, settings, "WX") { }

        protected override string MenuTitle => "WX";

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = CommonRowsHead();
            rows.Add(LinesRow());
            rows.Add(FontRow());
            rows.Add(BrightRow());
            rows.Add(StationsRow());
            return rows;
        }

        private List<WxLine> BuildLines()
        {
            var now = DateTime.UtcNow;
            var all = new List<WxLine>();
            for (int e = 0; e < Settings.Stations.Count; e++)
            {
                var id = Settings.Stations[e];
                var st = StationState(id, now);
                string body;
                if (!st.Fetched) body = "";
                else if (st.Report == null || st.Expired) body = "-M-";
                else body = st.Report.Body;

                var wrapped = Metar.WrapReport(id.PadRight(StationChars) + st.Time + " ", body, WrapChars);
                for (int i = 0; i < wrapped.Count; i++)
                {
                    all.Add(new WxLine
                    {
                        Entry = e,
                        Text = wrapped[i],
                        First = i == 0,
                        StaleTime = i == 0 && st.Stale,
                        Station = id,
                    });
                }
            }
            return all;
        }

        protected override void OnBodyWheel(int delta) => ScrollBy(delta > 0 ? -1 : 1);

        private void ScrollBy(int lines)
        {
            _scroll += lines;
            Surface.Refresh();
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int linePitch = m.TitleH;          // 17 at FONT 2
            int entryGap = m.TitleH - 1;       // 16 at FONT 2
            int markerBlock = Settings.Tearoff ? m.MarkerW + EntryGap : 0;
            int textW = m.TextWidth(WrapChars);
            int sbW = m.BoxW;
            double width = LeftPad + markerBlock + textW + EntryGap + sbW + ScrollRightPad;

            var lines = BuildLines();
            int maxLines = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - maxLines);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            bool overflow = lines.Count > maxLines;
            int count = Math.Min(maxLines, lines.Count - _scroll);

            // Line tops relative to y0.
            var tops = new double[Math.Max(count, 0)];
            double yy = 0;
            for (int v = 0; v < count; v++)
            {
                if (v > 0) yy += linePitch + (lines[_scroll + v].Entry != lines[_scroll + v - 1].Entry ? entryGap : 0);
                tops[v] = yy;
            }
            double contentH = count == 0 ? m.GlyphTopInRow + m.Cap : tops[count - 1] + m.GlyphTopInRow + m.Cap;
            if (overflow) contentH = Math.Max(contentH, 3 * sbW); // room for a usable scroll bar

            if (!p.Drawing) return new Size(width, contentH);

            var tb = TextBrush;
            for (int v = 0; v < count; v++)
            {
                int idx = _scroll + v;
                var ln = lines[idx];
                double ly = y0 + tops[v];

                if (ln.First && Settings.Tearoff) DrawMarker(p, x0 + LeftPad, ly);

                var text = ln.Text;
                if (v == count - 1 && idx + 1 < lines.Count && lines[idx + 1].Entry == ln.Entry)
                    text = text.PadRight(WrapChars) + ">";

                double tx = x0 + LeftPad + markerBlock, top = ly + m.GlyphTopInRow;
                p.Text(text, tx, top, m, tb);
                if (ln.StaleTime) p.Underline(tx, top, m, StationChars, 4, tb);

                var station = ln.Station;
                double hitH = v + 1 < count ? tops[v + 1] - tops[v] : m.Row;
                p.Hit(x0, ly, LeftPad + markerBlock + textW, hitH, _ => OpenDeletePopup(station));
            }

            if (overflow) DrawScrollBar(p, x0 + width - ScrollRightPad - sbW, y0 - BodyPad + 2, sbW,
                contentH + BodyPad * 2 - 4, lines.Count, maxLines, maxScroll);

            return new Size(width, contentH);
        }

        private void DrawScrollBar(Painter p, double sx, double sy, int sbW, double sh, int total, int visible, int maxScroll)
        {
            p.Frame(Eram.ViewBorder, sx, sy, sbW, sh, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sbW, sbW, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sh - sbW - 1, sbW, 1);
            p.Triangle(Eram.ScrollGray, sx, sy, sbW, sbW, up: true);
            p.Triangle(Eram.ScrollGray, sx, sy + sh - sbW, sbW, sbW, up: false);
            p.Hit(sx, sy, sbW, sbW, _ => ScrollBy(-1));
            p.Hit(sx, sy + sh - sbW, sbW, sbW, _ => ScrollBy(1));

            double trackY = sy + sbW + 1, trackH = sh - 2 * sbW - 2;
            if (trackH <= 6) return;
            double frac = Math.Min(1.0, visible / (double)Math.Max(total, 1));
            double thumbH = Math.Max(6, Math.Floor((trackH - 4) * frac));
            double thumbY = Math.Floor(trackY + 2 + (trackH - 4 - thumbH) * (maxScroll == 0 ? 0 : _scroll / (double)maxScroll));
            p.Frame(Eram.ScrollGray, sx + 3, thumbY, sbW - 6, thumbH, 1);
            p.Hit(sx, trackY, sbW, thumbY - trackY, _ => ScrollBy(-visible));
            p.Hit(sx, thumbY + thumbH, sbW, trackY + trackH - (thumbY + thumbH), _ => ScrollBy(visible));
        }
    }
}
