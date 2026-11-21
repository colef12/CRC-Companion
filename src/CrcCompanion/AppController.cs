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
    /// <summary>Owns the active profile, the windows, the METAR service and the tray icon.</summary>
    public sealed class AppController : IViewHost, ITrayHost, IDisposable
    {
        private readonly Application _app;
        private readonly ProfileStore _store;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly TrayIcon _tray;

        private Profile _profile = new Profile();
        private ViewsSelectorWindow? _selector;
        private AltimViewWindow? _altim;
        private WxViewWindow? _wx;

        public MetarService Metar { get; }
        public double UiScale => _profile.UiScale;
        public string ActiveProfile => _profile.Name;

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

            _tray = new TrayIcon(this);
        }

        // ---------------- Profiles ----------------

        private void LoadProfile(string name)
        {
            CloseWindows();
            _profile = _store.Load(name);
            _settings.ActiveProfile = name;
            _store.SaveSettings(_settings);

            var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            _selector = new ViewsSelectorWindow(_profile.Selector, IsViewOn, ToggleView);
            _selector.Changed += MarkDirty;
            _selector.SetScale(UiScale);
            var (sx, sy) = Place(_profile.Selector.X, _profile.Selector.Y, wa.Left + 40, wa.Top + 40);
            _selector.ShowAt(sx, sy);

            _altim = new AltimViewWindow(this, _profile.Altim);
            _wx = new WxViewWindow(this, _profile.Wx);
            foreach (var v in new ListViewWindow[] { _altim, _wx }) v.SetScale(UiScale);

            if (_profile.Altim.Visible) ShowView(_altim);
            if (_profile.Wx.Visible) ShowView(_wx);

            UpdateMetarStations();
            _tray?.UpdateText();
        }

        private static (int X, int Y) Place(int? x, int? y, int defX, int defY)
        {
            if (x == null || y == null) return (defX, defY);
            var pt = new System.Drawing.Point(x.Value + 10, y.Value + 10);
            bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(pt));
            return onScreen ? (x.Value, y.Value) : (defX, defY);
        }

        private void ShowView(ListViewWindow v)
        {
            if (v.IsVisible) return;
            var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            int defX = wa.Left + (v is AltimViewWindow ? 40 : 360);
            int defY = wa.Top + 140;
            var (x, y) = Place(v.Settings.X, v.Settings.Y, defX, defY);
            v.ShowAt(x, y);
            if (v.Settings.X == null) v.SavePosition();
        }

        private void CloseWindows()
        {
            foreach (var w in new Window?[] { _altim, _wx, _selector })
            {
                if (w == null) continue;
                if (w is ListViewWindow lv) lv.CloseChildren();
                w.Close();
            }
            _altim = null;
            _wx = null;
            _selector = null;
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
            var p = new Profile { Name = name, UiScale = _profile.UiScale };
            _store.Save(p);
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
            foreach (var w in new EramWindow?[] { _selector, _altim, _wx }) w?.SetScale(scale);
            MarkDirty();
        }

        public void ResetPositions()
        {
            _profile.Selector.X = _profile.Selector.Y = null;
            _profile.Altim.X = _profile.Altim.Y = null;
            _profile.Wx.X = _profile.Wx.Y = null;
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

        // ---------------- Views ----------------

        private bool IsViewOn(string key) => key switch
        {
            ViewsSelectorWindow.AltimKey => _profile.Altim.Visible,
            ViewsSelectorWindow.WxKey => _profile.Wx.Visible,
            _ => false,
        };

        private void ToggleView(string key)
        {
            ListViewWindow? v = key switch
            {
                ViewsSelectorWindow.AltimKey => _altim,
                ViewsSelectorWindow.WxKey => _wx,
                _ => null,
            };
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
            _tray.Dispose();
        }
    }
}
