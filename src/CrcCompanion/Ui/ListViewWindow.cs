using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;
using CrcCompanion.Services;

namespace CrcCompanion.Ui
{
    public interface IViewHost
    {
        MetarService Metar { get; }
        double UiScale { get; }
        /// <summary>A setting or position changed: schedule a profile save.</summary>
        void MarkDirty();
        void StationsChanged(ListViewWindow view);
        void SetViewVisible(ListViewWindow view, bool visible);
    }

    /// <summary>
    /// Shared ERAM list-view chrome, measured from CRC at FONT 2 (screen pixels):
    /// 1 px border, 17 px title bar with 23 px "M" and "-" cells, 1 px rule, 8 px body padding,
    /// 21 px rows. Also owns the M menu, the station entry box and the delete popup.
    /// </summary>
    public abstract class ListViewWindow : EramWindow
    {
        protected readonly IViewHost Host;
        public ViewSettings Settings { get; }
        public string ViewTitle { get; }
        protected abstract string MenuTitle { get; }

        private MenuWindow? _menu;
        private MenuWindow? _popup;
        private StationEntryWindow? _entry;

        /// <summary>Gap between the left border and the first marker.</summary>
        protected const int LeftPad = 6;
        /// <summary>Gap between a marker and its text, and after each entry's text.</summary>
        protected const int EntryGap = 11;
        /// <summary>Extra space after the last column, before the right border.</summary>
        protected const int RightPad = 13;
        /// <summary>Body padding above the first row and below the last.</summary>
        protected const int BodyPad = 8;

        protected ListViewWindow(IViewHost host, ViewSettings settings, string title)
        {
            Host = host;
            Settings = settings;
            ViewTitle = title;
            Surface.Paint = Paint;
            Surface.Wheel = (d, _) => OnBodyWheel(d);
            Repositioned += _ => SavePosition();
            Closed += (_, _) => CloseChildren();
        }

        public void SavePosition()
        {
            var (x, y) = ScreenPosition;
            Settings.X = x;
            Settings.Y = y;
            Host.MarkDirty();
        }

        public void Refresh()
        {
            Surface.Refresh();
            _menu?.Refresh();
        }

        public void CloseChildren()
        {
            _menu?.Close();
            _popup?.Close();
            _entry?.Close();
        }

        protected EramMetrics M => new EramMetrics(Settings.Font);
        protected System.Windows.Media.Brush TextBrush => Eram.TextGray(Settings.Bright);

        /// <summary>
        /// Paints the body (rows only, not its padding) with its top-left content corner at (x, y),
        /// where x is the inner left edge (just inside the border). Returns the content size,
        /// width including its own right padding.
        /// </summary>
        protected abstract Size PaintBody(Painter p, double x, double y);

        protected virtual void OnBodyWheel(int delta) { }

        protected abstract IReadOnlyList<MenuRow> BuildMenuRows();

        /// <summary>Draws one outlined tear-off marker with its top-left at (x, y).</summary>
        protected void DrawMarker(Painter p, double x, double y)
        {
            var m = M;
            p.Fill(Eram.MarkerOutline, x, y, m.MarkerW, m.MarkerH);
            p.Fill(Eram.Marker, x + 1, y + 1, m.MarkerW - 2, m.MarkerH - 2);
        }

        private Size Paint(Painter p)
        {
            var m = M;
            int th = m.TitleH, box = m.BoxW;
            double bodyTop = 2 + th;                 // border + title + rule

            var body = PaintBody(p.MeasureOnly(), 1, bodyTop + BodyPad);
            double minW = 2 + box * 2 + 2 + m.TextWidth(ViewTitle.Length) + 2 * m.Cell;
            double w = Math.Max(minW, 1 + body.Width + 1);
            double h = bodyTop + BodyPad + body.Height + BodyPad + 1;

            p.Fill(Settings.Opaque ? Eram.Black : Eram.HalfBlack, 0, 0, w, h);

            // Title bar: [M][   TITLE   ][-]
            var tb = TextBrush;
            double gt = 1 + m.GlyphTopInTitle;
            p.TextCentered("M", 1, box, gt, m, tb);
            p.Fill(Eram.ViewBorder, 1 + box, 1, 1, th);
            double titleX = 2 + box, titleW = w - 4 - box * 2;
            p.TextCentered(ViewTitle, titleX, titleW, gt, m, tb);
            p.Fill(Eram.ViewBorder, w - 2 - box, 1, 1, th);
            p.TextCentered("-", w - 1 - box, box, gt, m, tb);
            p.Fill(Eram.ViewBorder, 1, 1 + th, w - 2, 1);

            p.Hit(1, 1, box, th, _ => ToggleMenu());
            p.Hit(titleX, 1, titleW, th, _ => BeginReposition());
            p.Hit(w - 1 - box, 1, box, th, _ => Host.SetViewVisible(this, false));

            if (p.Drawing) PaintBody(p, 1, bodyTop + BodyPad);

            if (Settings.Border) p.Frame(Eram.ViewBorder, 0, 0, w, h, 1);
            return new Size(w, h);
        }

        // ---------- Menu ----------

        public void ToggleMenu()
        {
            if (_menu != null) { _menu.Close(); return; }
            _menu = new MenuWindow(MenuTitle, BuildMenuRows());
            _menu.SetScale(Host.UiScale);
            _menu.Closed += (_, _) => _menu = null;
            var r = ScreenRect;
            // CRC opens the menu against the view's left edge, top-aligned.
            _menu.ShowAt(r.Left, r.Top);
            var mr = _menu.ScreenRect;
            _menu.MoveTo(r.Left - (mr.Right - mr.Left), r.Top);
        }

        protected List<MenuRow> CommonRowsHead() => new List<MenuRow>
        {
            new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
            new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
            new ToggleRow("TEAROFF", () => Settings.Tearoff, () => { Settings.Tearoff = !Settings.Tearoff; Changed(); }),
        };

        protected StepRow LinesRow() => new StepRow("LINES", () => Settings.Lines, v => { Settings.Lines = v; Changed(); }, Limits.LinesMin, Limits.LinesMax);
        protected StepRow FontRow() => new StepRow("FONT", () => Settings.Font, v => { Settings.Font = v; Changed(); }, Limits.FontMin, Limits.FontMax);
        protected StepRow BrightRow() => new StepRow("BRIGHT", () => Settings.Bright, v => { Settings.Bright = v; Changed(); }, Limits.BrightMin, Limits.BrightMax, Limits.BrightStep);
        protected CommandRow StationsRow() => new CommandRow("STATIONS", _ => OpenStationEntry());

        protected void Changed()
        {
            Surface.Refresh();
            Host.MarkDirty();
        }

        // ---------- Stations ----------

        public void OpenStationEntry()
        {
            if (_entry != null) { _entry.Activate(); return; }
            var previous = Native.GetForegroundWindow();
            _entry = new StationEntryWindow(
                MenuTitle + " STATIONS",
                () => Settings.Stations,
                text =>
                {
                    var result = StationIds.Apply(Settings.Stations, text);
                    if (result.Added.Count > 0 || result.Removed.Count > 0) Host.StationsChanged(this);
                    return StationIds.Feedback(result);
                },
                previous,
                Host.UiScale);
            _entry.Closed += (_, _) => _entry = null;
            var r = ScreenRect;
            _entry.ShowNear(r.Left, r.Bottom + 4);
        }

        /// <summary>Popup with a DELETE row for one station, as in CRC.</summary>
        protected void OpenDeletePopup(string station)
        {
            _popup?.Close();
            var popup = new MenuWindow(station, new List<MenuRow>
            {
                new CommandRow("DELETE", _ =>
                {
                    Settings.Stations.Remove(station);
                    Host.StationsChanged(this);
                    _popup?.Close();
                }),
            });
            _popup = popup;
            popup.SetScale(Host.UiScale);
            popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; };
            Native.GetCursorPos(out var c);
            popup.ShowAt(c.X + 8, c.Y + 8);
        }

        /// <summary>Station state shared by both views.</summary>
        protected (string Time, MetarReport? Report, bool Fetched, bool Stale, bool Expired) StationState(string id, DateTime now)
        {
            var fetched = Host.Metar.HasFetched(id);
            var rep = Host.Metar.Get(id);
            if (rep == null) return ("    ", null, fetched, false, false);
            return (Metar.FormatTime(rep.ObservedUtc), rep, true, Metar.IsStale(rep, now), Metar.IsExpired(rep, now));
        }
    }
}
