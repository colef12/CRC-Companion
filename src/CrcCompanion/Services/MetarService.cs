using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// Keeps METARs current for the stations in use.
    /// Real world (default): aviationweather.gov, all stations in one request; VATSIM's METAR
    /// service fills in any station it doesn't return. VATSIM mode is the reverse.
    /// Refreshes every 2 minutes and immediately when stations are added or the source changes.
    /// Raises Updated on the UI thread.
    /// </summary>
    public sealed class MetarService : IDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);
        private const int BatchSize = 40;

        private readonly HttpClient _http;
        private readonly Dispatcher _ui;
        private readonly ConcurrentDictionary<string, MetarReport?> _reports = new ConcurrentDictionary<string, MetarReport?>();
        private readonly ConcurrentDictionary<string, string> _resolvedIcao = new ConcurrentDictionary<string, string>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Timer _timer;
        private HashSet<string> _stations = new HashSet<string>();
        private volatile string _source;
        private bool _disposed;

        public event Action? Updated;

        public MetarService(Dispatcher ui, string source)
        {
            _ui = ui;
            _source = WeatherSources.Normalize(source);
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.1");
            _timer = new Timer(_ => _ = RefreshAllAsync(), null, RefreshInterval, RefreshInterval);
        }

        public string Source => _source;

        /// <summary>Switches the primary source and refetches everything.</summary>
        public void SetSource(string source)
        {
            var s = WeatherSources.Normalize(source);
            if (s == _source) return;
            _source = s;
            _ = RefreshAllAsync();
        }

        public bool HasFetched(string id) => _reports.ContainsKey(id);

        public MetarReport? Get(string id) => _reports.TryGetValue(id, out var r) ? r : null;

        /// <summary>Sets the stations to keep current. New stations are fetched right away.</summary>
        public void SetStations(IEnumerable<string> stations)
        {
            var next = new HashSet<string>(stations);
            var added = next.Where(s => !_stations.Contains(s)).ToList();
            _stations = next;
            if (added.Count > 0) _ = FetchManyAsync(added);
        }

        public Task RefreshAllAsync() => FetchManyAsync(_stations.ToList());

        private IReadOnlyList<string> Candidates(string id) =>
            _resolvedIcao.TryGetValue(id, out var known) ? new[] { known } : StationIds.IcaoCandidates(id);

        private async Task FetchManyAsync(List<string> ids)
        {
            if (_disposed || ids.Count == 0) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var results = new Dictionary<string, MetarReport?>();

                if (_source == WeatherSources.Real)
                {
                    var awc = await GetAwcBatchAsync(ids.SelectMany(Candidates).Distinct().ToList()).ConfigureAwait(false);
                    foreach (var id in ids) results[id] = Pick(id, awc, now);
                    // Fill gaps from VATSIM, one station at a time.
                    var missing = ids.Where(id => results[id] == null).ToList();
                    await Task.WhenAll(missing.Select(async id =>
                    {
                        var r = await FetchSingleAsync(id, GetVatsimAsync, now).ConfigureAwait(false);
                        lock (results) results[id] = r;
                    })).ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAll(ids.Select(async id =>
                    {
                        var r = await FetchSingleAsync(id, GetVatsimAsync, now).ConfigureAwait(false);
                        lock (results) results[id] = r;
                    })).ConfigureAwait(false);
                    var missing = ids.Where(id => results[id] == null).ToList();
                    if (missing.Count > 0)
                    {
                        var awc = await GetAwcBatchAsync(missing.SelectMany(Candidates).Distinct().ToList()).ConfigureAwait(false);
                        foreach (var id in missing) results[id] = Pick(id, awc, now);
                    }
                }

                foreach (var kv in results)
                {
                    // Keep the last good report if a refresh fails.
                    if (kv.Value != null || !_reports.ContainsKey(kv.Key)) _reports[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
            finally
            {
                _gate.Release();
            }
            if (!_disposed) _ = _ui.BeginInvoke(new Action(() => Updated?.Invoke()));
        }

        private MetarReport? Pick(string id, Dictionary<string, string> rawByIcao, DateTime now)
        {
            foreach (var icao in Candidates(id))
            {
                if (!rawByIcao.TryGetValue(icao, out var raw)) continue;
                var rep = Metar.Parse(raw, now);
                if (rep != null && rep.Station == icao)
                {
                    _resolvedIcao[id] = icao;
                    return rep;
                }
            }
            return null;
        }

        private async Task<MetarReport?> FetchSingleAsync(string id, Func<string, Task<string?>> getter, DateTime now)
        {
            foreach (var icao in Candidates(id))
            {
                var raw = await getter(icao).ConfigureAwait(false);
                if (raw == null) continue;
                var rep = Metar.Parse(raw, now);
                if (rep != null && rep.Station == icao)
                {
                    _resolvedIcao[id] = icao;
                    return rep;
                }
            }
            return null;
        }

        /// <summary>aviationweather.gov raw METARs for many stations; newest report per station.</summary>
        private async Task<Dictionary<string, string>> GetAwcBatchAsync(List<string> icaos)
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < icaos.Count; i += BatchSize)
            {
                var chunk = icaos.Skip(i).Take(BatchSize);
                try
                {
                    var url = "https://aviationweather.gov/api/data/metar?format=raw&ids=" +
                              Uri.EscapeDataString(string.Join(",", chunk));
                    var text = await _http.GetStringAsync(url).ConfigureAwait(false);
                    foreach (var (icao, line) in ParseRawLines(text))
                        if (!result.ContainsKey(icao)) result[icao] = line; // first = newest
                }
                catch (Exception) { /* leave these to the fallback */ }
            }
            return result;
        }

        private async Task<string?> GetVatsimAsync(string icao)
        {
            try
            {
                var text = await _http.GetStringAsync("https://metar.vatsim.net/metar.php?id=" + Uri.EscapeDataString(icao)).ConfigureAwait(false);
                foreach (var (station, line) in ParseRawLines(text))
                    if (station == icao) return line;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Splits a raw METAR response into (station, line), skipping METAR/SPECI prefixes.</summary>
        public static IEnumerable<(string Icao, string Line)> ParseRawLines(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0) continue;
                var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int k = parts[0] == "METAR" || parts[0] == "SPECI" ? 1 : 0;
                if (parts.Length > k + 1 && parts[k].Length == 4) yield return (parts[k], l);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _http.Dispose();
        }
    }
}
