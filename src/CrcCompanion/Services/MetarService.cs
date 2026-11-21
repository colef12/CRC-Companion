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
    /// Fetches METARs for the stations in use. Source: VATSIM's METAR service, with
    /// aviationweather.gov as a fallback. Refreshes every 2 minutes and immediately
    /// when new stations are added. Raises Updated on the UI thread.
    /// </summary>
    public sealed class MetarService : IDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

        private readonly HttpClient _http;
        private readonly Dispatcher _ui;
        private readonly ConcurrentDictionary<string, MetarReport?> _reports = new ConcurrentDictionary<string, MetarReport?>();
        private readonly ConcurrentDictionary<string, string> _resolvedIcao = new ConcurrentDictionary<string, string>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Timer _timer;
        private HashSet<string> _stations = new HashSet<string>();
        private bool _disposed;

        public event Action? Updated;

        public MetarService(Dispatcher ui)
        {
            _ui = ui;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.1");
            _timer = new Timer(_ => _ = RefreshAllAsync(), null, RefreshInterval, RefreshInterval);
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

        private async Task FetchManyAsync(List<string> ids)
        {
            if (_disposed || ids.Count == 0) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var tasks = ids.Select(async id =>
                {
                    var rep = await FetchAsync(id, now).ConfigureAwait(false);
                    // Keep the last good report if a refresh fails.
                    if (rep != null || !_reports.ContainsKey(id)) _reports[id] = rep;
                });
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            if (!_disposed) _ui.BeginInvoke(new Action(() => Updated?.Invoke()));
        }

        private async Task<MetarReport?> FetchAsync(string id, DateTime now)
        {
            var candidates = _resolvedIcao.TryGetValue(id, out var known)
                ? new[] { known }
                : StationIds.IcaoCandidates(id);

            foreach (var icao in candidates)
            {
                var raw = await GetVatsimAsync(icao).ConfigureAwait(false)
                          ?? await GetAwcAsync(icao).ConfigureAwait(false);
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

        private async Task<string?> GetVatsimAsync(string icao)
        {
            try
            {
                var text = await _http.GetStringAsync("https://metar.vatsim.net/metar.php?id=" + Uri.EscapeDataString(icao)).ConfigureAwait(false);
                return FirstLineFor(text, icao);
            }
            catch (Exception) { return null; }
        }

        private async Task<string?> GetAwcAsync(string icao)
        {
            try
            {
                var text = await _http.GetStringAsync("https://aviationweather.gov/api/data/metar?format=raw&ids=" + Uri.EscapeDataString(icao)).ConfigureAwait(false);
                return FirstLineFor(text, icao);
            }
            catch (Exception) { return null; }
        }

        private static string? FirstLineFor(string text, string icao)
        {
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith(icao + " ", StringComparison.Ordinal) ||
                    l.StartsWith("METAR " + icao + " ", StringComparison.Ordinal) ||
                    l.StartsWith("SPECI " + icao + " ", StringComparison.Ordinal))
                    return l;
            }
            return null;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _http.Dispose();
        }
    }
}
