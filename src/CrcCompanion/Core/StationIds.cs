using System;
using System.Collections.Generic;
using System.Linq;

namespace CrcCompanion.Core
{
    /// <summary>Result of parsing a station entry line such as "IAD DCA -RIC".</summary>
    public sealed class StationEntryResult
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
        public List<string> Invalid { get; } = new List<string>();
        public List<string> NotFound { get; } = new List<string>();
        public List<string> AlreadyPresent { get; } = new List<string>();
    }

    public static class StationIds
    {
        /// <summary>
        /// Normalizes a user token to the ID shown in the view.
        /// KIAD -> IAD, IAD -> IAD, CYYZ -> CYYZ, PANC -> PANC.
        /// Returns null if the token is not a 3-4 character alphanumeric ID.
        /// </summary>
        public static string? Normalize(string token)
        {
            if (token == null) return null;
            var t = token.Trim().ToUpperInvariant();
            if (t.Length < 3 || t.Length > 4) return null;
            if (!t.All(char.IsLetterOrDigit)) return null;
            if (t.Length == 4 && t[0] == 'K') return t.Substring(1);
            return t;
        }

        /// <summary>ICAO IDs to try, in order, when fetching weather for a display ID.</summary>
        public static IReadOnlyList<string> IcaoCandidates(string displayId)
        {
            if (displayId.Length == 3) return new[] { "K" + displayId, "P" + displayId };
            return new[] { displayId };
        }

        /// <summary>
        /// Applies an entry line to a station list in place. Tokens are separated by spaces
        /// or commas; a leading '-' removes a station. New stations are appended in order.
        /// </summary>
        public static StationEntryResult Apply(List<string> stations, string entry)
        {
            var result = new StationEntryResult();
            if (string.IsNullOrWhiteSpace(entry)) return result;

            var tokens = entry.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                bool remove = raw.StartsWith("-", StringComparison.Ordinal);
                var body = remove ? raw.Substring(1) : raw;
                var id = Normalize(body);
                if (id == null)
                {
                    result.Invalid.Add(raw.ToUpperInvariant());
                    continue;
                }

                if (remove)
                {
                    if (stations.Remove(id)) result.Removed.Add(id);
                    else result.NotFound.Add(id);
                }
                else
                {
                    if (stations.Contains(id)) result.AlreadyPresent.Add(id);
                    else
                    {
                        stations.Add(id);
                        result.Added.Add(id);
                    }
                }
            }
            return result;
        }

        /// <summary>One-line ERAM-style feedback for an entry result.</summary>
        public static string Feedback(StationEntryResult r)
        {
            var parts = new List<string>();
            if (r.Added.Count > 0) parts.Add("ADD " + string.Join(" ", r.Added));
            if (r.Removed.Count > 0) parts.Add("DEL " + string.Join(" ", r.Removed));
            if (r.AlreadyPresent.Count > 0) parts.Add("DUP " + string.Join(" ", r.AlreadyPresent));
            if (r.NotFound.Count > 0) parts.Add("NOT IN LIST " + string.Join(" ", r.NotFound));
            if (r.Invalid.Count > 0) parts.Add("INVALID " + string.Join(" ", r.Invalid));
            return parts.Count == 0 ? "NO CHANGE" : string.Join("  ", parts);
        }
    }
}
