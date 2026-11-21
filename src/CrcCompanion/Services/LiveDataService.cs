using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// VATSIM data: map data (callsign prefixes, ARTCC boundaries, airports) from
    /// vatsimnetwork/vatspy-data-project, cached for 7 days; and the live feed plus voice
    /// transceivers, polled every 15 s. Events are raised on the UI thread.
    /// </summary>
    public sealed class LiveDataService : IDisposable
    {
        private const string VatspyDatUrl = "https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/VATSpy.dat";
        private const string BoundariesUrl = "https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/Boundaries.geojson";
        private const string FeedUrl = "https://data.vatsim.net/v3/vatsim-data.json";
        private const string TransceiversUrl = "https://data.vatsim.net/v3/transceivers-data.json";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MapMaxAge = TimeSpan.FromDays(7);

        private readonly Dispatcher _ui;
        private readonly HttpClient _http;
        private readonly string _cacheDir;
        private readonly Timer _timer;
        private readonly SemaphoreSlim _pollGate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public MapData? Map { get; private set; }
        public FeedSnapshot? Latest { get; private set; }
        /// <summary>Last error text for the tray status, or empty.</summary>
        public string LastError { get; private set; } = "";

        public event Action? MapReady;
        public event Action? FeedUpdated;

        public LiveDataService(Dispatcher ui, string appRoot)
        {
            _ui = ui;
            _cacheDir = Path.Combine(appRoot, "cache");
            Directory.CreateDirectory(_cacheDir);
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.2");
            _ = LoadMapAsync();
            _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(1), PollInterval);
        }

        public Task RefreshNow() => PollAsync();

        private async Task LoadMapAsync()
        {
            try
            {
                var dat = await CachedAsync("VATSpy.dat", VatspyDatUrl).ConfigureAwait(false);
                var geo = await CachedAsync("Boundaries.geojson", BoundariesUrl).ConfigureAwait(false);
                if (dat == null || geo == null) { LastError = "map data unavailable"; return; }
                var map = MapData.Parse(dat, geo);
                Map = map;
                Raise(MapReady);
            }
            catch (Exception ex)
            {
                LastError = "map data: " + ex.Message;
                Program.LogError(ex);
            }
        }

        /// <summary>Returns the cached file, refreshing it when older than 7 days. Falls back to a stale copy offline.</summary>
        private async Task<string?> CachedAsync(string name, string url)
        {
            var path = Path.Combine(_cacheDir, name);
            bool fresh = File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MapMaxAge;
            if (!fresh)
            {
                try
                {
                    var text = await _http.GetStringAsync(url).ConfigureAwait(false);
                    if (text.Length > 1000)
                    {
                        var tmp = path + ".tmp";
                        await File.WriteAllTextAsync(tmp, text).ConfigureAwait(false);
                        File.Move(tmp, path, overwrite: true);
                        return text;
                    }
                }
                catch (Exception) { /* use the stale copy below */ }
            }
            return File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : null;
        }

        private async Task PollAsync()
        {
            if (_disposed || !await _pollGate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                var feedTask = _http.GetStringAsync(FeedUrl);
                var radiosTask = _http.GetStringAsync(TransceiversUrl);
                var feed = await feedTask.ConfigureAwait(false);
                var snap = VatsimFeed.ParseMain(feed);
                try { VatsimFeed.AddTransceivers(snap, await radiosTask.ConfigureAwait(false)); }
                catch (Exception) { /* frequencies unknown this cycle */ }
                Latest = snap;
                LastError = "";
                Raise(FeedUpdated);
            }
            catch (Exception ex)
            {
                LastError = "VATSIM data: " + ex.Message;
            }
            finally
            {
                _pollGate.Release();
            }
        }

        private void Raise(Action? evt)
        {
            if (!_disposed && evt != null) _ = _ui.BeginInvoke(evt);
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _http.Dispose();
        }
    }
}
