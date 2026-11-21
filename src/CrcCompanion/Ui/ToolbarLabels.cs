using System;
using System.Collections.Generic;
using System.Globalization;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Exact VIEWS toolbar label pixels (data in ToolbarLabels.Data.cs).
    /// Returns ink rectangles in button-body pixel coordinates.
    /// </summary>
    public static partial class ToolbarLabels
    {
        private static readonly Dictionary<string, EramFont.Run[]> Cache = new Dictionary<string, EramFont.Run[]>();

        public static bool Has(string label) => Runs.ContainsKey(label);

        public static EramFont.Run[] Get(string label)
        {
            if (Cache.TryGetValue(label, out var c)) return c;
            var list = new List<EramFont.Run>();
            if (Runs.TryGetValue(label, out var rows))
            {
                for (int y = 0; y < rows.Length; y++)
                {
                    if (rows[y].Length == 0) continue;
                    foreach (var part in rows[y].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split(':');
                        list.Add(new EramFont.Run(
                            int.Parse(kv[0], CultureInfo.InvariantCulture), y,
                            int.Parse(kv[1], CultureInfo.InvariantCulture), 1));
                    }
                }
            }
            var arr = list.ToArray();
            Cache[label] = arr;
            return arr;
        }
    }
}
