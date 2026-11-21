using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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
    /// Shared ERAM list view chrome: title bar "M | TITLE | -", body, optional border,
    /// O/T background, M menu, station entry and delete popups.
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

        protected const double Pad = 4;

        protected ListViewWindow(IViewHost host, ViewSettings settings, string title)
        {
            Host = host;
            Settings = settings;
            ViewTitle = title;
            Surface.Paint = Paint;
            Surface.Wheel = (d, pos) => OnBodyWheel(d);
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

        protected double FontPx => Eram.FontSize(Settings.Font);
        protected System.Windows.Media.Brush TextBrush => Eram.TextGray(Settings.Bright);

        /// <summary>Measures (and draws, when painting) the body. Returns its size.</summary>
        protected abstract Size PaintBody(Painter p, double x, double y);

        protected virtual void OnBodyWheel(int delta) { }

        protected abstract IReadOnlyList<MenuRow> BuildMenuRows();

        private Size Paint(Painter p)
        {
            double fs = FontPx, cw = p.CharW(fs), lh = p.LineH(fs);
            double th = lh + 2;            // title bar height
            double cellW = cw + 8;         // M and - cells
            double bodyY = 1 + th + 1;

            var body = PaintBody(p.MeasureOnly(), 1, bodyY);
            double minW = 2 + cellW * 2 + 2 + p.TextW(ViewTitle, fs) + 16;
            double w = Math.Max(minW, body.Width + 2);
            double h = bodyY + body.Height + 1;

            // Background (drawn first, body painted on top)
            p.Fill(Settings.Opaque ? Eram.Black : Eram.HalfBlack, 0, 0, w, h);

            // Title bar
            var tb = TextBrush;
            p.TextCentered("M", 1, 1, cellW, fs, tb, th);
            p.Fill(Eram.ViewBorder, 1 + cellW, 1, 1, th);
            p.TextCentered(ViewTitle, 2 + cellW, 1, w - 4 - cellW * 2, fs, tb, th);
            p.Fill(Eram.ViewBorder, w - 2 - cellW, 1, 1, th);
            p.TextCentered("-", w - 1 - cellW, 1, cellW, fs, tb, th);
            p.Fill(Eram.ViewBorder, 1, 1 + th, w - 2, 1);

            p.Hit(1, 1, cellW, th, _ => ToggleMenu());
            p.Hit(2 + cellW, 1, w - 4 - cellW * 2, th, _ => BeginReposition());
            p.Hit(w - 1 - cellW, 1, cellW, th, _ => Host.SetViewVisible(this, false));

            if (p.Drawing) PaintBody(p, 1, bodyY);

            if (Settings.Border) p.Frame(Eram.ViewBorder, 0, 0, w, h, 1);
            return new Size(w, h);
        }

        // ---------- Menu ----------

        public void ToggleMenu()
        {
            if (_menu != null) { _menu.Close(); return; }
            var rows = BuildMenuRows();
            _menu = new MenuWindow(MenuTitle, rows);
            _menu.SetScale(Host.UiScale);
            _menu.Closed += (_, _) => _menu = null;
            var r = ScreenRect;
            _menu.ShowAt(r.Right + 4, r.Top);
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

        /// <summary>Text for station state shared by both views.</summary>
        protected (string Time, MetarReport? Report, bool Fetched, bool Stale, bool Expired) StationState(string id, DateTime now)
        {
            var fetched = Host.Metar.HasFetched(id);
            var rep = Host.Metar.Get(id);
            if (rep == null) return ("    ", null, fetched, false, false);
            return (Metar.FormatTime(rep.ObservedUtc), rep, true, Metar.IsStale(rep, now), Metar.IsExpired(rep, now));
        }
    }
}
