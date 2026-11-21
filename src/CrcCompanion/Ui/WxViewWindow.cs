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
            double fs = FontPx, cw = p.CharW(fs), lh = p.LineH(fs);
            double markerW = Math.Round(cw), markerGap = 6;
            double textX = Pad + (Settings.Tearoff ? markerW + markerGap : 0);
            double sbW = Math.Round(cw + 6);
            double textW = (WrapChars + 1) * cw;
            double w = textX + textW + 4 + sbW + 2;
            int visible = Math.Max(1, Settings.Lines);
            double h = visible * lh + 4;

            var lines = BuildLines();
            int maxScroll = Math.Max(0, lines.Count - visible);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            if (!p.Drawing) return new Size(w, h);

            var tb = TextBrush;
            for (int v = 0; v < visible; v++)
            {
                int idx = _scroll + v;
                if (idx >= lines.Count) break;
                var ln = lines[idx];
                if (ln.Entry < 0) continue;
                double ly = y0 + 2 + v * lh;

                if (ln.First && Settings.Tearoff) p.Fill(Eram.Marker, x0 + Pad, ly + 2, markerW, lh - 4);

                var text = ln.Text;
                bool lastVisible = v == visible - 1;
                if (lastVisible && idx + 1 < lines.Count && lines[idx + 1].Entry == ln.Entry) text = text.PadRight(WrapChars) + ">";

                double tx = x0 + textX;
                p.Text(text, tx, ly, fs, tb, lh);
                if (ln.StaleTime) p.Underline(tx, ly, lh, 5, 4, fs, tb);

                var station = ln.Station;
                p.Hit(x0, ly, textX + textW, lh, _ => OpenDeletePopup(station));
            }

            // Scroll bar: up arrow, track with thumb, down arrow.
            double sx = x0 + w - sbW - 2, sy = y0 + 1, sh = h - 2;
            p.Frame(Eram.ViewBorder, sx, sy, sbW, sh, 1);
            p.Triangle(Eram.ScrollGray, sx, sy, sbW, lh, up: true);
            p.Triangle(Eram.ScrollGray, sx, sy + sh - lh, sbW, lh, up: false);
            p.Hit(sx, sy, sbW, lh, _ => ScrollBy(-1));
            p.Hit(sx, sy + sh - lh, sbW, lh, _ => ScrollBy(1));

            double trackY = sy + lh, trackH = sh - 2 * lh;
            if (trackH > 4)
            {
                p.Fill(Eram.ViewBorder, sx, trackY, sbW, 1);
                p.Fill(Eram.ViewBorder, sx, trackY + trackH - 1, sbW, 1);
                int total = Math.Max(lines.Count, 1);
                double frac = Math.Min(1.0, visible / (double)total);
                double thumbH = Math.Max(6, (trackH - 4) * frac);
                double thumbY = trackY + 2 + (trackH - 4 - thumbH) * (maxScroll == 0 ? 0 : _scroll / (double)maxScroll);
                p.Frame(Eram.ScrollGray, sx + 2, thumbY, sbW - 4, thumbH, 1);
                p.Hit(sx, trackY, sbW, thumbY - trackY, _ => ScrollBy(-visible));
                p.Hit(sx, thumbY + thumbH, sbW, trackY + trackH - (thumbY + thumbH), _ => ScrollBy(visible));
            }

            return new Size(w, h);
        }
    }
}
