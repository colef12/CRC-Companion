using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM Altimeter Settings View. Each entry: [marker] STN  HHMM ALT
    /// Time underlined when stale (&gt;65 min); setting underlined when below 29.92;
    /// -M- when the report is missing or older than 120 min.
    /// Entries fill LINES rows per column, across COL columns.
    /// </summary>
    public sealed class AltimViewWindow : ListViewWindow
    {
        // "BOS  1754 012": 5 (station) + 4 (time) + 1 + 3 (altimeter)
        private const int TextChars = 13;

        public AltimViewWindow(IViewHost host, ViewSettings settings) : base(host, settings, "ALTIM SET") { }

        protected override string MenuTitle => "AS";

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = CommonRowsHead();
            rows.Add(LinesRow());
            rows.Add(new StepRow("COL", () => Settings.Cols, v => { Settings.Cols = v; Changed(); }, Limits.ColsMin, Limits.ColsMax));
            rows.Add(FontRow());
            rows.Add(BrightRow());
            rows.Add(new CommandRow("TEMPLATE", null)); // not simulated in CRC either
            rows.Add(StationsRow());
            return rows;
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            double fs = FontPx, cw = p.CharW(fs), lh = p.LineH(fs);
            double markerW = Math.Round(cw), markerGap = 6;
            double textX = Pad + (Settings.Tearoff ? markerW + markerGap : 0);
            double entryW = textX + TextChars * cw + Pad + 2;

            var stations = Settings.Stations;
            int lines = Math.Max(1, Settings.Lines);
            int capacity = lines * Math.Max(1, Settings.Cols);
            int shown = Math.Min(stations.Count, capacity);
            int cols = Math.Max(1, (int)Math.Ceiling(shown / (double)lines));
            int rows = shown == 0 ? 1 : Math.Min(lines, shown);

            var now = DateTime.UtcNow;
            var tb = TextBrush;

            for (int i = 0; i < shown; i++)
            {
                int col = i / lines, row = i % lines;
                double ex = x0 + col * entryW, ey = y0 + 2 + row * lh;
                var id = stations[i];
                var st = StationState(id, now);

                string alt;
                bool altUnderline = false;
                if (!st.Fetched) alt = "   ";
                else if (st.Report == null || st.Expired || st.Report.AltimeterHundredths == null) alt = "-M-";
                else
                {
                    var hundredths = st.Report.AltimeterHundredths.Value;
                    alt = Metar.FormatAltimeter(hundredths);
                    altUnderline = hundredths < Metar.StandardHundredths;
                }

                if (Settings.Tearoff) p.Fill(Eram.Marker, ex + Pad, ey + 2, markerW, lh - 4);

                var line = id.PadRight(5) + st.Time + " " + alt;
                double tx = ex + textX;
                p.Text(line, tx, ey, fs, tb, lh);
                if (st.Stale) p.Underline(tx, ey, lh, 5, 4, fs, tb);
                if (altUnderline) p.Underline(tx, ey, lh, 10, 3, fs, tb);

                p.Hit(ex, ey, entryW, lh, _ => OpenDeletePopup(id));
            }

            return new Size(cols * entryW, rows * lh + 4);
        }
    }
}
