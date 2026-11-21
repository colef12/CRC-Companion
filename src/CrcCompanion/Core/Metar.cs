using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrcCompanion.Core
{
    public sealed class MetarReport
    {
        public string Raw { get; init; } = "";
        public string Station { get; init; } = "";
        public DateTime? ObservedUtc { get; init; }
        /// <summary>Altimeter in hundredths of inHg (29.92 -> 2992). Null if not reported.</summary>
        public int? AltimeterHundredths { get; init; }
        /// <summary>Report text after the observation time token.</summary>
        public string Body { get; init; } = "";
    }

    public static class Metar
    {
        private static readonly Regex TimeRx = new Regex(@"^(\d{2})(\d{2})(\d{2})Z$", RegexOptions.Compiled);
        private static readonly Regex AltARx = new Regex(@"^A(\d{4})$", RegexOptions.Compiled);
        private static readonly Regex AltQRx = new Regex(@"^Q(\d{4})$", RegexOptions.Compiled);

        /// <summary>ERAM marks an observation stale after this many minutes (time underlined).</summary>
        public const int StaleMinutes = 65;
        /// <summary>ERAM treats an observation as unusable after this many minutes (-M-).</summary>
        public const int ExpiredMinutes = 120;
        /// <summary>Standard pressure in hundredths of inHg.</summary>
        public const int StandardHundredths = 2992;

        public static MetarReport? Parse(string raw, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = Regex.Replace(raw.Trim(), @"\s+", " ");
            var tokens = text.Split(' ').ToList();
            if (tokens.Count > 0 && (tokens[0] == "METAR" || tokens[0] == "SPECI")) tokens.RemoveAt(0);
            if (tokens.Count < 2) return null;

            var station = tokens[0];
            DateTime? observed = null;
            int bodyStart = 1;
            var tm = TimeRx.Match(tokens[1]);
            if (tm.Success)
            {
                observed = ResolveObservation(
                    int.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(tm.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(tm.Groups[3].Value, CultureInfo.InvariantCulture),
                    nowUtc);
                bodyStart = 2;
            }

            int? alt = null;
            foreach (var tok in tokens.Skip(bodyStart))
            {
                if (tok == "RMK") break;
                var a = AltARx.Match(tok);
                if (a.Success) { alt = int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture); break; }
                var q = AltQRx.Match(tok);
                if (q.Success)
                {
                    var hpa = int.Parse(q.Groups[1].Value, CultureInfo.InvariantCulture);
                    alt = (int)Math.Round(hpa * 2.9529983);
                    break;
                }
            }

            return new MetarReport
            {
                Raw = text,
                Station = station,
                ObservedUtc = observed,
                AltimeterHundredths = alt,
                Body = string.Join(" ", tokens.Skip(bodyStart)),
            };
        }

        /// <summary>Builds the observation time from day/hour/minute, rolling back a month if the day is in the future.</summary>
        public static DateTime? ResolveObservation(int day, int hour, int minute, DateTime nowUtc)
        {
            if (hour > 23 || minute > 59 || day < 1 || day > 31) return null;
            for (int back = 0; back < 3; back++)
            {
                var month = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-back);
                if (day > DateTime.DaysInMonth(month.Year, month.Month)) continue;
                var candidate = new DateTime(month.Year, month.Month, day, hour, minute, 0, DateTimeKind.Utc);
                if (candidate <= nowUtc.AddMinutes(10)) return candidate;
            }
            return null;
        }

        /// <summary>29.92 -> "992", 30.12 -> "012" (first digit and decimal point removed).</summary>
        public static string FormatAltimeter(int hundredths)
        {
            var s = Math.Clamp(hundredths, 0, 9999).ToString("0000", CultureInfo.InvariantCulture);
            return s.Substring(1);
        }

        public static string FormatTime(DateTime? observedUtc)
            => observedUtc.HasValue ? observedUtc.Value.ToString("HHmm", CultureInfo.InvariantCulture) : "    ";

        public static double AgeMinutes(MetarReport r, DateTime nowUtc)
            => r.ObservedUtc.HasValue ? (nowUtc - r.ObservedUtc.Value).TotalMinutes : double.MaxValue;

        public static bool IsStale(MetarReport r, DateTime nowUtc) => AgeMinutes(r, nowUtc) > StaleMinutes;
        public static bool IsExpired(MetarReport r, DateTime nowUtc) => AgeMinutes(r, nowUtc) > ExpiredMinutes;

        /// <summary>
        /// Wraps a report whose first line starts with a fixed-column prefix such as "ALB  1451 ".
        /// The prefix spacing is preserved; continuation lines start at column 0, as in ERAM's WX view.
        /// </summary>
        public static List<string> WrapReport(string prefix, string body, int width)
        {
            var lines = new List<string>();
            if (width < 1) width = 1;
            var cur = prefix;
            foreach (var word in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (true)
                {
                    var sep = cur.Length == 0 || cur.EndsWith(" ", StringComparison.Ordinal) ? "" : " ";
                    if (cur.Length + sep.Length + w.Length <= width) { cur += sep + w; break; }
                    if (cur.Trim().Length > 0) { lines.Add(cur.TrimEnd()); cur = ""; continue; }
                    // Word longer than a whole line: hard split.
                    lines.Add(w.Substring(0, width));
                    w = w.Substring(width);
                    if (w.Length == 0) break;
                }
            }
            if (cur.Trim().Length > 0) lines.Add(cur.TrimEnd());
            return lines;
        }

        /// <summary>
        /// Word-wraps text to a width in characters. Tokens longer than the width are split.
        /// </summary>
        public static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            if (width < 1) width = 1;
            var current = "";
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (w.Length > width)
                {
                    if (current.Length > 0) { lines.Add(current); current = ""; }
                    lines.Add(w.Substring(0, width));
                    w = w.Substring(width);
                }
                if (current.Length == 0) current = w;
                else if (current.Length + 1 + w.Length <= width) current += " " + w;
                else { lines.Add(current); current = w; }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }
    }
}
