using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CrcCompanion.Core;
using CrcCompanion.Services;
using CrcCompanion.Tray;
using CrcCompanion.Ui;

namespace CrcCompanion
{
    /// <summary>
    /// Owns the active profile, the windows, the METAR and VATSIM data services and the tray icon.
    /// Finds the facility you're controlling from your CID and keeps the strip lists current.
    /// </summary>
    public sealed class AppController : IViewHost, ITrayHost, IStripSource, IDisposable
    {
        private readonly Application _app;
        private readonly ProfileStore _store;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly LiveDataService _live;
        private readonly TrayIcon _tray;

        private Profile _profile = new Profile();
        private ViewsSelectorWindow? _selector;
        private AltimViewWindow? _altim;
        private WxViewWindow? _wx;
        private StripViewWindow? _inbound;
        private StripViewWindow? _inSector;
        private StripViewWindow? _dept;
        private StatusViewWindow? _status;

        /// <summary>Callsign seen on the last poll, to auto-switch only when you log on or change position.</summary>
        private string _lastSessionCallsign = "";

        public MetarService Metar { get; }
        public double UiScale => _profile.UiScale;
        public string ActiveProfile => _profile.Name;

        // IStripSource
        public TrafficResult? Traffic { get; private set; }
        public FacilityContext? Facility { get; private set; }
        public bool Connected { get; private set; }
        public string StatusText { get; private set; } = "LOADING";

        public AppController(Application app)
        {
            _app = app;
            _store = new ProfileStore(ProfileStore.DefaultRoot());
            _settings = _store.LoadSettings();

            Metar = new MetarService(app.Dispatcher, _settings.WeatherSource);
            Metar.Updated += RefreshViews;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

            // Re-evaluate stale/expired marks as time passes.
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick += (_, _) => RefreshViews();
            _clockTimer.Start();

            if (_store.List().Count == 0) _store.Save(new Profile { Name = "DEFAULT" });
            var start = _store.Exists(_settings.ActiveProfile) ? _settings.ActiveProfile : _store.List()[0];
            LoadProfile(start);

            _live = new LiveDataService(app.Dispatcher, ProfileStore.DefaultRoot());
            _live.MapReady += OnLiveData;
            _live.FeedUpdated += OnLiveData;

            _tray = new TrayIcon(this);
        }

        // ---------------- Profiles ----------------

        private static System.Drawing.Rectangle WorkArea =>
            System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        private void LoadProfile(string name)
        {
            CloseWindows();
            _profile = _store.Load(name);
            _settings.ActiveProfile = name;
            _store.SaveSettings(_settings);

            var wa = WorkArea;
            _selector = new ViewsSelectorWindow(_profile.Selector, IsViewOn, ToggleView);
            _selector.Changed += MarkDirty;
            _selector.SetScale(UiScale);
            var (sx, sy) = Place(_profile.Selector.X, _profile.Selector.Y, wa.Left + 40, wa.Top + 40);
            _selector.ShowAt(sx, sy);

            _altim = new AltimViewWindow(this, _profile.Altim);
            _wx = new WxViewWindow(this, _profile.Wx);
            _inbound = new StripViewWindow(this, _profile.Inbound, StripKind.Inbound, this);
            _inSector = new StripViewWindow(this, _profile.InSector, StripKind.InSector, this);
            _dept = new StripViewWindow(this, _profile.Dept, StripKind.Departures, this);
            _status = new StatusViewWindow(this, _profile.Status);
            foreach (var v in Views) { v.SetScale(UiScale); if (v.Settings.Visible) ShowView(v); }

            UpdateMetarStations();
            Recompute();
            _tray?.UpdateText();
        }

        private IEnumerable<ListViewWindow> Views =>
            new ListViewWindow?[] { _altim, _wx, _inbound, _inSector, _dept, _status }.Where(v => v != null).Select(v => v!);

        private static (int X, int Y) Place(int? x, int? y, int defX, int defY)
        {
            if (x == null || y == null) return (defX, defY);
            var pt = new System.Drawing.Point(x.Value + 10, y.Value + 10);
            bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(pt));
            return onScreen ? (x.Value, y.Value) : (defX, defY);
        }

        private (int X, int Y) DefaultPosition(ListViewWindow v)
        {
            var wa = WorkArea;
            if (v == _altim) return (wa.Left + 40, wa.Top + 160);
            if (v == _wx) return (wa.Left + 360, wa.Top + 160);
            if (v == _inbound) return (wa.Left + 760, wa.Top + 160);
            if (v == _inSector) return (wa.Left + 760, wa.Top + 520);
            if (v == _status) return (wa.Right - 360, wa.Top + 160);
            return (wa.Left + 40, wa.Top + 520);
        }

        private void ShowView(ListViewWindow v)
        {
            if (v.IsVisible) return;
            var (defX, defY) = DefaultPosition(v);
            var (x, y) = Place(v.Settings.X, v.Settings.Y, defX, defY);
            v.ShowAt(x, y);
            if (v.Settings.X == null) v.SavePosition();
        }

        private void CloseWindows()
        {
            foreach (var v in Views) v.CloseChildren();
            foreach (var w in new Window?[] { _altim, _wx, _inbound, _inSector, _dept, _status, _selector }) w?.Close();
            _altim = null; _wx = null; _inbound = null; _inSector = null; _dept = null; _status = null; _selector = null;
        }

        public IReadOnlyList<string> Profiles() => _store.List();

        public void SwitchProfile(string name)
        {
            if (name == _profile.Name) return;
            SaveNow();
            LoadProfile(name);
        }

        public void NewProfile()
        {
            var name = AskName("New profile", "Facility name (e.g. ZBW):", "");
            if (name == null) return;
            SaveNow();
            _store.Save(new Profile { Name = name, UiScale = _profile.UiScale });
            LoadProfile(name);
        }

        public void DuplicateProfile()
        {
            var name = AskName("Duplicate profile", $"Copy {_profile.Name} as:", _profile.Name + "-2");
            if (name == null) return;
            SaveNow();
            _store.Save(_profile.Clone(name));
            LoadProfile(name);
        }

        public void RenameProfile()
        {
            var name = AskName("Rename profile", $"Rename {_profile.Name} to:", _profile.Name);
            if (name == null || name == _profile.Name) return;
            SaveNow();
            _store.Rename(_profile.Name, name);
            LoadProfile(name);
        }

        public void DeleteProfile()
        {
            var all = _store.List();
            if (all.Count <= 1) return;
            var r = MessageBox.Show($"Delete profile {_profile.Name}? This can't be undone.", "CRC Companion",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            var doomed = _profile.Name;
            _saveTimer.Stop();
            CloseWindows();
            _store.Delete(doomed);
            LoadProfile(_store.List()[0]);
        }

        private string? AskName(string title, string prompt, string initial)
        {
            var raw = InputDialog.Ask(title, prompt, initial);
            if (raw == null) return null;
            var name = ProfileStore.CleanName(raw);
            if (name == null)
            {
                MessageBox.Show("Use letters, numbers, '-' or '_'.", "CRC Companion");
                return null;
            }
            if (_store.Exists(name) && name != _profile.Name)
            {
                MessageBox.Show($"A profile named {name} already exists.", "CRC Companion");
                return null;
            }
            return name;
        }

        public string WeatherSource => Metar.Source;

        public void SetWeatherSource(string source)
        {
            _settings.WeatherSource = WeatherSources.Normalize(source);
            _store.SaveSettings(_settings);
            Metar.SetSource(_settings.WeatherSource);
        }

        public void SetScale(double scale)
        {
            _profile.UiScale = scale;
            _selector?.SetScale(scale);
            foreach (var v in Views) v.SetScale(scale);
            MarkDirty();
        }

        public void ResetPositions()
        {
            _profile.Selector.X = _profile.Selector.Y = null;
            foreach (var s in new[] { _profile.Altim, _profile.Wx, _profile.Inbound, _profile.InSector, _profile.Dept, _profile.Status })
                s.X = s.Y = null;
            SaveNow();
            LoadProfile(_profile.Name);
        }

        public void OpenProfilesFolder()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", _store.ProfilesDir) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        public void Exit()
        {
            SaveNow();
            Dispose();
            _app.Shutdown();
        }

        // ---------------- VATSIM session and traffic ----------------

        public string Cid => _settings.Cid;
        public bool AutoSwitchProfile => _settings.AutoSwitchProfile;

        public void SetCid()
        {
            var raw = InputDialog.Ask("VATSIM CID", "Your VATSIM CID (used to find the facility you're controlling):", _settings.Cid);
            if (raw == null) return;
            var cid = new string(raw.Where(char.IsDigit).ToArray());
            _settings.Cid = cid;
            _store.SaveSettings(_settings);
            _lastSessionCallsign = "";
            _ = _live.RefreshNow();
        }

        public void ToggleAutoSwitch()
        {
            _settings.AutoSwitchProfile = !_settings.AutoSwitchProfile;
            _store.SaveSettings(_settings);
        }

        /// <summary>One line for the tray menu describing the session.</summary>
        public string SessionStatus
        {
            get
            {
                if (_live.Map == null) return "Loading VATSIM map data...";
                if (_settings.Cid.Length == 0) return Facility == null ? "Set your VATSIM CID" : $"Facility {ShortFir}: from profile (CID not set)";
                if (Connected && Facility != null)
                    return $"Connected: {Facility.MyCallsign} {Facility.MyFrequencyHz / 1e6:0.000} ({ShortFir})";
                if (Facility != null) return $"Not connected - using {ShortFir} from profile";
                return _live.LastError.Length > 0 ? _live.LastError : "Not connected";
            }
        }

        private string ShortFir => Facility == null ? "" : MapData.ShortFacility(Facility.Fir);

        private void OnLiveData()
        {
            var map = _live.Map;
            var snap = _live.Latest;
            if (map != null && snap != null && int.TryParse(_settings.Cid, out var cid) && cid > 0)
            {
                var me = snap.Controllers.FirstOrDefault(c => c.Cid == cid && c.Facility > VatsimFeed.FacObs);
                var callsign = me?.Callsign ?? "";
                if (callsign != _lastSessionCallsign)
                {
                    _lastSessionCallsign = callsign;
                    var fir = me == null ? null : map.FirForCallsign(me.Callsign);
                    if (fir != null && _settings.AutoSwitchProfile) AutoSwitchTo(fir);
                }
            }
            Recompute();
        }

        /// <summary>Switch to the facility's profile, creating it from the current layout (without stations) if needed.</summary>
        private void AutoSwitchTo(string fir)
        {
            var name = MapData.ShortFacility(fir);
            var current = _live.Map?.FirForFacility(_profile.Name);
            if (string.Equals(current, fir, StringComparison.OrdinalIgnoreCase)) return;
            SaveNow();
            if (!_store.Exists(name)) _store.Save(_profile.CloneLayout(name));
            LoadProfile(name);
        }

        public void Recompute()
        {
            var map = _live?.Map;
            var snap = _live?.Latest;
            Traffic = null;
            Facility = null;
            Connected = false;

            if (map == null) { StatusText = "LOADING MAP DATA"; RefreshStrips(); return; }

            Controller? me = null;
            if (snap != null && int.TryParse(_settings.Cid, out var cid) && cid > 0)
                me = snap.Controllers.FirstOrDefault(c => c.Cid == cid && c.Facility > VatsimFeed.FacObs);

            // Facility: the position you're logged on as, else the profile name (e.g. "ZDC").
            var fir = me != null ? map.FirForCallsign(me.Callsign) : null;
            fir ??= map.FirForFacility(_profile.Name);
            var boundary = fir == null ? null : map.BoundaryForFir(fir);
            if (fir == null || boundary == null)
            {
                StatusText = me != null ? "UNKNOWN FACILITY " + me.Callsign : "NO FACILITY";
                RefreshStrips();
                return;
            }

            Connected = me != null && me.FrequencyHz > 0 && string.Equals(map.FirForCallsign(me.Callsign), fir, StringComparison.OrdinalIgnoreCase);
            Facility = new FacilityContext
            {
                Fir = fir,
                Boundary = boundary,
                Airports = map.AirportsInFir(fir).ToDictionary(a => a.Icao, StringComparer.OrdinalIgnoreCase),
                MyCallsign = Connected ? me!.Callsign : "",
                MyFrequencyHz = Connected ? me!.FrequencyHz : 0,
                FirForCallsign = map.FirForCallsign,
            };

            if (snap == null) { StatusText = "WAITING FOR VATSIM DATA"; RefreshStrips(); return; }
            Traffic = Core.Traffic.Compute(snap, Facility, _profile.Inbound.Minutes, DateTime.UtcNow);
            StatusText = "";
            RefreshStrips();
            _tray?.UpdateText();
        }

        private void RefreshStrips()
        {
            _inbound?.Refresh();
            _inSector?.Refresh();
            _dept?.Refresh();
        }

        // ---------------- Views ----------------

        private bool IsViewOn(string key) => ViewFor(key)?.Settings.Visible ?? false;

        private ListViewWindow? ViewFor(string key) => key switch
        {
            ViewsSelectorWindow.AltimKey => _altim,
            ViewsSelectorWindow.WxKey => _wx,
            ViewsSelectorWindow.InboundKey => _inbound,
            ViewsSelectorWindow.DeptKey => _dept,
            ViewsSelectorWindow.InSectorKey => _inSector,
            ViewsSelectorWindow.StatusKey => _status,
            _ => null,
        };

        private void ToggleView(string key)
        {
            var v = ViewFor(key);
            if (v != null) SetViewVisible(v, !v.Settings.Visible);
        }

        public void SetViewVisible(ListViewWindow view, bool visible)
        {
            view.Settings.Visible = visible;
            if (visible) ShowView(view);
            else
            {
                if (view.IsVisible) view.SavePosition();
                view.CloseChildren();
                view.Hide();
            }
            _selector?.Refresh();
            MarkDirty();
        }

        public void StationsChanged(ListViewWindow view)
        {
            UpdateMetarStations();
            view.Refresh();
            MarkDirty();
        }

        private void UpdateMetarStations() =>
            Metar.SetStations(_profile.Altim.Stations.Concat(_profile.Wx.Stations).Distinct());

        private void RefreshViews()
        {
            _altim?.Refresh();
            _wx?.Refresh();
        }

        // ---------------- Saving ----------------

        public void MarkDirty()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            _saveTimer.Stop();
            try { _store.Save(_profile); }
            catch (Exception ex) { Program.LogError(ex); }
        }

        public void Dispose()
        {
            _clockTimer.Stop();
            _saveTimer.Stop();
            Metar.Dispose();
            _live.Dispose();
            _tray.Dispose();
        }
    }
}
