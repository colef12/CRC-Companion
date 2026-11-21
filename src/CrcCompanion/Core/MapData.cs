using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CrcCompanion.Core
{
    public sealed class MapAirport
    {
        public string Icao { get; init; } = "";
        /// <summary>FAA/IATA identifier if present (IAD), else the ICAO code.</summary>
        public string Display { get; init; } = "";
        public LatLon Pos { get; init; }
        public string Fir { get; init; } = "";
    }

    /// <summary>
    /// VATSIM map data (vatsimnetwork/vatspy-data-project): which callsign prefixes belong to which
    /// FIR/ARTCC, ARTCC boundaries, and airports by ARTCC.
    /// </summary>
    public sealed class MapData
    {
        /// <summary>Callsign prefix (e.g. "DC", "WAS") → FIR id (e.g. "KZDC").</summary>
        private readonly Dictionary<string, string> _prefixToFir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>FIR id → boundary id (usually the same).</summary>
        private readonly Dictionary<string, string> _firToBoundary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Boundary> _boundaries = new Dictionary<string, Boundary>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MapAirport> _airports = new Dictionary<string, MapAirport>(StringComparer.OrdinalIgnoreCase);

        public int AirportCount => _airports.Count;
        public int BoundaryCount => _boundaries.Count;

        public static MapData Parse(string vatspyDat, string boundariesGeoJson)
        {
            var m = new MapData();
            m.ParseDat(vatspyDat);
            m.ParseBoundaries(boundariesGeoJson);
            return m;
        }

        private void ParseDat(string dat)
        {
            string section = "";
            foreach (var raw in dat.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal)) { section = line; continue; }
                var f = line.Split('|');
                if (section == "[FIRs]" && f.Length >= 4)
                {
                    // ICAO|Name|CallsignPrefix|FIRBoundary
                    var icao = f[0].Trim();
                    var prefix = f[2].Trim();
                    var boundary = f[3].Trim().Length > 0 ? f[3].Trim() : icao;
                    if (prefix.Length > 0 && !_prefixToFir.ContainsKey(prefix)) _prefixToFir[prefix] = icao;
                    if (!_prefixToFir.ContainsKey(icao)) _prefixToFir[icao] = icao;
                    if (!_firToBoundary.ContainsKey(icao)) _firToBoundary[icao] = boundary;
                }
                else if (section == "[Airports]" && f.Length >= 6)
                {
                    // ICAO|Name|Lat|Lon|IATA/LID|FIR|IsPseudo
                    if (f.Length >= 7 && f[6].Trim() == "1") continue;
                    if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
                    if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
                    var icao = f[0].Trim();
                    var lid = f[4].Trim();
                    if (_airports.ContainsKey(icao)) continue;
                    _airports[icao] = new MapAirport
                    {
                        Icao = icao,
                        Display = lid.Length > 0 ? lid : (icao.Length == 4 && icao[0] == 'K' ? icao.Substring(1) : icao),
                        Pos = new LatLon(lat, lon),
                        Fir = f[5].Trim(),
                    };
                }
            }
        }

        private void ParseBoundaries(string geojson)
        {
            using var doc = JsonDocument.Parse(geojson);
            foreach (var feat in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var props = feat.GetProperty("properties");
                var id = props.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                bool oceanic = props.TryGetProperty("oceanic", out var oc) && oc.ValueKind == JsonValueKind.String && oc.GetString() == "1";
                if (id.Length == 0 || oceanic || _boundaries.ContainsKey(id)) continue;

                var geom = feat.GetProperty("geometry");
                var type = geom.GetProperty("type").GetString();
                var rings = new List<IReadOnlyList<LatLon>>();
                var coords = geom.GetProperty("coordinates");
                if (type == "Polygon") rings.Add(ReadRing(coords[0]));
                else if (type == "MultiPolygon")
                    foreach (var poly in coords.EnumerateArray()) rings.Add(ReadRing(poly[0]));
                if (rings.Count > 0) _boundaries[id] = new Boundary(id, rings);
            }
        }

        private static IReadOnlyList<LatLon> ReadRing(JsonElement ring) =>
            ring.EnumerateArray().Select(pt => new LatLon(pt[1].GetDouble(), pt[0].GetDouble())).ToList();

        /// <summary>FIR id for a controller callsign: "DC_12_CTR" → "KZDC". Null if unknown.</summary>
        public string? FirForCallsign(string callsign)
        {
            var parts = callsign.ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            if (_prefixToFir.TryGetValue(parts[0], out var fir)) return fir;
            if (parts.Length > 1 && _prefixToFir.TryGetValue(parts[0] + "_" + parts[1], out fir)) return fir;
            return null;
        }

        /// <summary>FIR id for a facility name typed by the user or taken from a profile: "ZDC" or "KZDC".</summary>
        public string? FirForFacility(string name)
        {
            var n = name.Trim().ToUpperInvariant();
            if (_firToBoundary.ContainsKey(n)) return n;
            if (_firToBoundary.ContainsKey("K" + n)) return "K" + n;
            return null;
        }

        public Boundary? BoundaryForFir(string fir) =>
            _firToBoundary.TryGetValue(fir, out var b) && _boundaries.TryGetValue(b, out var bd) ? bd
            : _boundaries.TryGetValue(fir, out bd) ? bd : null;

        public MapAirport? Airport(string icao) => _airports.TryGetValue(icao.Trim(), out var a) ? a : null;

        public IEnumerable<MapAirport> AirportsInFir(string fir) => _airports.Values.Where(a => string.Equals(a.Fir, fir, StringComparison.OrdinalIgnoreCase));

        /// <summary>"KZDC" → "ZDC" for display and profile names.</summary>
        public static string ShortFacility(string fir) => fir.Length == 4 && fir[0] == 'K' ? fir.Substring(1) : fir;
    }
}
