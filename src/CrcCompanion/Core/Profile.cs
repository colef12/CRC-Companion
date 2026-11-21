using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcCompanion.Core
{
    /// <summary>Settings for one ERAM list view (ALTIM SET or WX). Mirrors the view's M menu.</summary>
    public sealed class ViewSettings
    {
        public bool Visible { get; set; }
        /// <summary>Screen position in physical pixels. Null = default placement.</summary>
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Opaque { get; set; } = true;
        public bool Border { get; set; } = true;
        public bool Tearoff { get; set; } = true;
        public int Lines { get; set; } = 10;
        public int Cols { get; set; } = 1;
        public int Font { get; set; } = 2;
        public int Bright { get; set; } = 80;
        public List<string> Stations { get; set; } = new List<string>();
    }

    public sealed class SelectorSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Open { get; set; } = true;
    }

    public sealed class Profile
    {
        public string Name { get; set; } = "DEFAULT";
        /// <summary>Extra scale on top of Windows DPI scaling, to match CRC's on-screen size.</summary>
        public double UiScale { get; set; } = 1.0;
        public SelectorSettings Selector { get; set; } = new SelectorSettings();
        public ViewSettings Altim { get; set; } = new ViewSettings { Lines = 24, Cols = 1 };
        public ViewSettings Wx { get; set; } = new ViewSettings { Lines = 5, Cols = 1 };

        public Profile Clone(string newName)
        {
            var json = JsonSerializer.Serialize(this, ProfileStore.JsonOptions);
            var copy = JsonSerializer.Deserialize<Profile>(json, ProfileStore.JsonOptions)!;
            copy.Name = newName;
            return copy;
        }
    }

    public sealed class AppSettings
    {
        public string ActiveProfile { get; set; } = "DEFAULT";
        /// <summary>Primary METAR source: "REAL" (aviationweather.gov) or "VATSIM". The other is the fallback.</summary>
        public string WeatherSource { get; set; } = WeatherSources.Real;
    }

    public static class WeatherSources
    {
        public const string Real = "REAL";
        public const string Vatsim = "VATSIM";
        public static string Normalize(string? s) => s == Vatsim ? Vatsim : Real;
    }

    /// <summary>Profiles live in %APPDATA%\CRCCompanion\profiles\NAME.json.</summary>
    public sealed class ProfileStore
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public string Root { get; }
        public string ProfilesDir => Path.Combine(Root, "profiles");
        private string SettingsPath => Path.Combine(Root, "settings.json");

        public ProfileStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(ProfilesDir);
        }

        public static string DefaultRoot() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CRCCompanion");

        /// <summary>Profile names are uppercase letters, digits, '-' and '_' (max 24).</summary>
        public static string? CleanName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = new string(name.Trim().ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            if (n.Length == 0) return null;
            return n.Length > 24 ? n.Substring(0, 24) : n;
        }

        private string PathFor(string name) => Path.Combine(ProfilesDir, name + ".json");

        public List<string> List() =>
            Directory.GetFiles(ProfilesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        public bool Exists(string name) => File.Exists(PathFor(name));

        public Profile Load(string name)
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(PathFor(name)), JsonOptions);
                if (p != null) { p.Name = name; Sanitize(p); return p; }
            }
            catch (Exception) { /* corrupt or missing: fall through to a fresh profile */ }
            return new Profile { Name = name };
        }

        public void Save(Profile p)
        {
            var path = PathFor(p.Name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(p, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }

        public void Delete(string name)
        {
            if (Exists(name)) File.Delete(PathFor(name));
        }

        public void Rename(string oldName, string newName)
        {
            var p = Load(oldName);
            p.Name = newName;
            Save(p);
            Delete(oldName);
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            }
            catch (Exception) { }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings s) =>
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));

        public static void Sanitize(Profile p)
        {
            p.UiScale = Math.Clamp(p.UiScale <= 0 ? 1.0 : p.UiScale, 0.5, 3.0);
            p.Selector ??= new SelectorSettings();
            p.Altim ??= new ViewSettings { Lines = 24 };
            p.Wx ??= new ViewSettings { Lines = 5 };
            foreach (var v in new[] { p.Altim, p.Wx })
            {
                v.Lines = Math.Clamp(v.Lines, Limits.LinesMin, Limits.LinesMax);
                v.Cols = Math.Clamp(v.Cols, Limits.ColsMin, Limits.ColsMax);
                v.Font = Math.Clamp(v.Font, Limits.FontMin, Limits.FontMax);
                v.Bright = Math.Clamp(v.Bright, Limits.BrightMin, Limits.BrightMax);
                v.Stations = (v.Stations ?? new List<string>())
                    .Select(StationIds.Normalize).Where(s => s != null).Select(s => s!)
                    .Distinct().ToList();
            }
        }
    }

    public static class Limits
    {
        public const int LinesMin = 1, LinesMax = 40;
        public const int ColsMin = 1, ColsMax = 6;
        public const int FontMin = 1, FontMax = 5;
        public const int BrightMin = 0, BrightMax = 100, BrightStep = 2;
    }
}
