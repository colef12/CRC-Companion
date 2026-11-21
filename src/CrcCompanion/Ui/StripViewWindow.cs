using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    public enum StripKind { Inbound, InSector, Departures }

    /// <summary>What the strip views need from the app.</summary>
    public interface IStripSource
    {
        TrafficResult? Traffic { get; }
        FacilityContext? Facility { get; }
        /// <summary>True when you're logged on as a controller (frequency marks are meaningful).</summary>
        bool Connected { get; }
        /// <summary>Short status shown when there is nothing to list (e.g. "NO FACILITY").</summary>
        string StatusText { get; }
        /// <summary>Recompute the lists now (e.g. after the Inbound look-ahead changes).</summary>
        void Recompute();
    }

    /// <summary>
    /// One-line strips in ERAM view chrome (eATS-style columns):
    /// mark, callsign, type, entry time or destination, altitude, beacon, route.
    /// Lines are 17 px apart at FONT 2, like the WX view; DEPT LIST adds an airport heading per group.
    /// </summary>
    public sealed class StripViewWindow : ListViewWindow
    {
        private readonly StripKind _kind;
        private readonly IStripSource _source;
        private int _scroll;
        /// <summary>Callsigns highlighted by clicking; kept by callsign so the highlight follows the flight as lists re-sort.</summary>
        private readonly HashSet<string> _highlighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public StripViewWindow(IViewHost host, ViewSettings settings, StripKind kind, IStripSource source)
            : base(host, settings, kind switch { StripKind.Inbound => "INBND LIST", StripKind.InSector => "IN SECTOR", _ => "DEPT LIST" })
        {
            _kind = kind;
            _source = source;
        }

        protected override string MenuTitle => _kind switch { StripKind.Inbound => "IL", StripKind.InSector => "IS", _ => "DL" };

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = new List<MenuRow>
            {
                new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
                new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
                LinesRow(),
                new StepRow("ROUTE", () => Settings.RouteChars, v => { Settings.RouteChars = v; Changed(); }, Limits.RouteMin, Limits.RouteMax, Limits.RouteStep),
                FontRow(),
                BrightRow(),
            };
            switch (_kind)
            {
                case StripKind.Inbound:
                    rows.Add(new StepRow("MIN", () => Settings.Minutes, v => { Settings.Minutes = v; _source.Recompute(); Changed(); },
                        Limits.MinutesMin, Limits.MinutesMax, Limits.MinutesStep));
                    break;
                case StripKind.InSector:
                    rows.Add(new ToggleRow("OTHERS", () => Settings.ShowOthers, () => { Settings.ShowOthers = !Settings.ShowOthers; Changed(); }));
                    break;
                case StripKind.Departures:
                    rows.Add(new ToggleRow("PREFILE", () => Settings.ShowPrefiles, () => { Settings.ShowPrefiles = !Settings.ShowPrefiles; Changed(); }));
                    break;
            }
            return rows;
        }

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -3 : 3;
            Surface.Refresh();
        }

        /// <summary>
        /// Text lines for the body. Headings are marked so they can be spaced like WX report breaks;
        /// Key is the strip's callsign (empty for headings and status lines).
        /// </summary>
        private List<(string Text, bool Heading, string Key)> BuildLines()
        {
            var lines = new List<(string, bool, string)>();
            var t = _source.Traffic;
            var f = _source.Facility;
            if (t == null || f == null)
            {
                lines.Add((_source.StatusText, false, ""));
                return lines;
            }

            int route = Settings.RouteChars;
            bool connected = _source.Connected;
            switch (_kind)
            {
                case StripKind.Inbound:
                    foreach (var i in t.Inbound) lines.Add((StripFormat.Inbound(i, route, connected), false, i.Callsign));
                    break;
                case StripKind.InSector:
                    foreach (var i in Core.Traffic.VisibleInSector(t.InSector, connected, Settings.ShowOthers))
                        lines.Add((StripFormat.InSector(i, route, connected, f.DisplayAirport), false, i.Callsign));
                    break;
                case StripKind.Departures:
                    string? group = null;
                    foreach (var i in t.Departures)
                    {
                        if (i.IsPrefile && !Settings.ShowPrefiles) continue;
                        var apt = f.DisplayAirport(i.Departure);
                        if (apt != group) { lines.Add((apt, true, "")); group = apt; }
                        lines.Add((StripFormat.Departure(i, route, f.DisplayAirport), false, i.Callsign));
                    }
                    break;
            }
            if (lines.Count == 0) lines.Add(("NONE", false, ""));
            return lines;
        }

        private void ToggleHighlight(string callsign)
        {
            if (!_highlighted.Remove(callsign)) _highlighted.Add(callsign);
            Surface.Refresh();
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int pitch = m.TitleH;           // 17 at FONT 2
            int groupGap = m.TitleH - 1;    // 16 at FONT 2, as between WX reports
            int chars = StripFormat.Width(Settings.RouteChars);
            double width = LeftPad + m.TextWidth(chars) + EntryGap;

            var lines = BuildLines();
            int max = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - max);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            int count = Math.Min(max, lines.Count - _scroll);

            double y = 0;
            var tops = new double[count];
            for (int v = 0; v < count; v++)
            {
                if (v > 0) y += pitch + (lines[_scroll + v].Heading ? groupGap : 0);
                tops[v] = y;
            }
            double contentH = count == 0 ? m.GlyphTopInRow + m.Cap : tops[count - 1] + m.GlyphTopInRow + m.Cap;

            if (p.Drawing)
            {
                var tb = TextBrush;
                for (int v = 0; v < count; v++)
                {
                    var (text, _, key) = lines[_scroll + v];
                    // Line box: the glyphs plus 3 px above and below (17 px at FONT 2, so boxes touch).
                    double boxY = y0 + tops[v] + m.GlyphTopInRow - 3, boxH = pitch;
                    if (key.Length > 0)
                    {
                        if (_highlighted.Contains(key)) p.Fill(Eram.Highlight, x0 + 1, boxY, width - 2, boxH);
                        p.Hit(x0, boxY, width, boxH, _ => ToggleHighlight(key));
                    }
                    if (v == count - 1 && _scroll + count < lines.Count) text = text.PadRight(chars - 1).Substring(0, chars - 1) + ">";
                    p.Text(text, x0 + LeftPad, y0 + tops[v] + m.GlyphTopInRow, m, tb);
                }
            }
            return new Size(width, contentH);
        }
    }
}
