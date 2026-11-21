using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM Weather Station Report View. Each entry starts "STN  HHMM " followed by the
    /// METAR body, wrapped to a fixed width, with a blank line between entries.
    /// Shows LINES lines with a scroll bar; '&gt;' marks a report that continues below.
    /// Uses the same row pitch, markers and chrome as the Altimeter view.
    /// </summary>
    public sealed class WxViewWindow : ListViewWindow
    {
        private const int WrapChars = 25;
        private int _scroll;

        private sealed class WxLine
        {
            public int Entry = -1;     // -1 = blank separator
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

                var wrapped = Metar.WrapReport(id.PadRight(5) + st.Time + " ", body, WrapChars);
                if (all.Count > 0) all.Add(new WxLine());
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
            int markerBlock = Settings.Tearoff ? m.MarkerW + EntryGap : 0;
            int textW = m.TextWidth(WrapChars + 1);
            int sbW = m.BoxW;
            int visible = Math.Max(1, Settings.Lines);
            double width = LeftPad + markerBlock + textW + EntryGap + sbW + 2;
            double height = (visible - 1) * m.Row + m.MarkerH;

            var lines = BuildLines();
            int maxScroll = Math.Max(0, lines.Count - visible);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            if (!p.Drawing) return new Size(width, height);

            var tb = TextBrush;
            for (int v = 0; v < visible; v++)
            {
                int idx = _scroll + v;
                if (idx >= lines.Count) break;
                var ln = lines[idx];
                if (ln.Entry < 0) continue;
                double ly = y0 + v * m.Row;

                if (ln.First && Settings.Tearoff) DrawMarker(p, x0 + LeftPad, ly);

                var text = ln.Text;
                bool lastVisible = v == visible - 1;
                if (lastVisible && idx + 1 < lines.Count && lines[idx + 1].Entry == ln.Entry) text = text.PadRight(WrapChars) + ">";

                double tx = x0 + LeftPad + markerBlock, top = ly + m.GlyphTopInRow;
                p.Text(text, tx, top, m, tb);
                if (ln.StaleTime) p.Underline(tx, top, m, 5, 4, tb);

                var station = ln.Station;
                p.Hit(x0, ly, LeftPad + markerBlock + textW, m.Row, _ => OpenDeletePopup(station));
            }

            // Scroll bar: up box, track with thumb, down box. Spans the padded body height.
            double sx = x0 + width - sbW - 2, sy = y0 - BodyPad + 2, sh = height + BodyPad * 2 - 4;
            p.Frame(Eram.ViewBorder, sx, sy, sbW, sh, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sbW, sbW, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sh - sbW - 1, sbW, 1);
            p.Triangle(Eram.ScrollGray, sx, sy, sbW, sbW, up: true);
            p.Triangle(Eram.ScrollGray, sx, sy + sh - sbW, sbW, sbW, up: false);
            p.Hit(sx, sy, sbW, sbW, _ => ScrollBy(-1));
            p.Hit(sx, sy + sh - sbW, sbW, sbW, _ => ScrollBy(1));

            double trackY = sy + sbW + 1, trackH = sh - 2 * sbW - 2;
            if (trackH > 6)
            {
                int total = Math.Max(lines.Count, 1);
                double frac = Math.Min(1.0, visible / (double)total);
                double thumbH = Math.Max(6, Math.Floor((trackH - 4) * frac));
                double thumbY = Math.Floor(trackY + 2 + (trackH - 4 - thumbH) * (maxScroll == 0 ? 0 : _scroll / (double)maxScroll));
                p.Frame(Eram.ScrollGray, sx + 3, thumbY, sbW - 6, thumbH, 1);
                p.Hit(sx, trackY, sbW, thumbY - trackY, _ => ScrollBy(-visible));
                p.Hit(sx, thumbY + thumbH, sbW, trackY + trackH - (thumbY + thumbH), _ => ScrollBy(visible));
            }

            return new Size(width, height);
        }
    }
}
