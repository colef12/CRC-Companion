using System;
using System.Collections.Generic;
using System.IO;
using CrcCompanion.Core;

internal static class Program
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail == "" ? "" : "  -> " + detail)}");
    }

    private static void Eq<T>(string name, T expected, T actual) =>
        Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected [{expected}] got [{actual}]");

    private static int Main()
    {
        var now = new DateTime(2026, 11, 21, 18, 30, 0, DateTimeKind.Utc);

        // --- Station normalization ---
        Eq("KIAD -> IAD", "IAD", StationIds.Normalize("kiad"));
        Eq("IAD -> IAD", "IAD", StationIds.Normalize("IAD"));
        Eq("CYYZ kept", "CYYZ", StationIds.Normalize("CYYZ"));
        Eq("PANC kept", "PANC", StationIds.Normalize("PANC"));
        Eq("2 chars invalid", null, StationIds.Normalize("IA"));
        Eq("5 chars invalid", null, StationIds.Normalize("KIADX"));
        Eq("symbols invalid", null, StationIds.Normalize("I*D"));
        Eq("3-letter candidates", "KIAD,PIAD", string.Join(",", StationIds.IcaoCandidates("IAD")));

        // --- Batch entry ---
        var list = new List<string>();
        var r = StationIds.Apply(list, "IAD DCA BWI RIC");
        Eq("batch adds 4", "IAD,DCA,BWI,RIC", string.Join(",", list));
        r = StationIds.Apply(list, "kiad -RIC ORF, -XYZ 12");
        Eq("mixed add/remove", "IAD,DCA,BWI,ORF", string.Join(",", list));
        Eq("dup reported", "IAD", string.Join(",", r.AlreadyPresent));
        Eq("removed reported", "RIC", string.Join(",", r.Removed));
        Eq("not found reported", "XYZ", string.Join(",", r.NotFound));
        Eq("invalid reported", "12", string.Join(",", r.Invalid));
        Eq("feedback", "ADD ORF  DEL RIC  DUP IAD  NOT IN LIST XYZ  INVALID 12", StationIds.Feedback(r));

        // --- METAR parsing ---
        var m = Metar.Parse("KBOS 211754Z 26008G18KT 10SM FEW050 05/M06 A3012 RMK AO2 SLP201 T00501061", now)!;
        Eq("station", "KBOS", m.Station);
        Eq("obs time", new DateTime(2026, 11, 21, 17, 54, 0, DateTimeKind.Utc), m.ObservedUtc);
        Eq("altimeter A3012", 3012, m.AltimeterHundredths);
        Eq("format 30.12 -> 012", "012", Metar.FormatAltimeter(3012));
        Eq("format 29.92 -> 992", "992", Metar.FormatAltimeter(2992));
        Eq("format 29.07 -> 907", "907", Metar.FormatAltimeter(2907));
        Eq("time HHMM", "1754", Metar.FormatTime(m.ObservedUtc));
        Eq("body excludes station/time", "26008G18KT 10SM FEW050 05/M06 A3012 RMK AO2 SLP201 T00501061", m.Body);
        Check("36 min old not stale", !Metar.IsStale(m, now));

        var s = Metar.Parse("METAR KALB 211651Z 18009KT 10SM A2985", now)!;
        Eq("METAR prefix stripped", "KALB", s.Station);
        Check("99 min old is stale", Metar.IsStale(s, now));
        Check("99 min old not expired", !Metar.IsExpired(s, now));
        Check("below standard", s.AltimeterHundredths < Metar.StandardHundredths);

        var old = Metar.Parse("KPVD 211600Z 00000KT A2990", now)!;
        Check("150 min old expired", Metar.IsExpired(old, now));

        var q = Metar.Parse("EGLL 211750Z 24012KT 9999 Q1013", now)!;
        Eq("Q1013 -> 2991", 2991, q.AltimeterHundredths);

        var rmkOnly = Metar.Parse("KXYZ 211750Z AUTO RMK A3001", now)!;
        Eq("altimeter after RMK ignored", null, rmkOnly.AltimeterHundredths);

        // Day rollover: report from the 30th when today is the 1st.
        var dec1 = new DateTime(2026, 12, 1, 0, 20, 0, DateTimeKind.Utc);
        var roll = Metar.Parse("KJFK 302351Z 31010KT A3001", dec1)!;
        Eq("month rollback", new DateTime(2026, 11, 30, 23, 51, 0, DateTimeKind.Utc), roll.ObservedUtc);

        Eq("empty -> null", null, Metar.Parse("   ", now));

        // --- Wrap ---
        Eq("wrap", "ALB 1451 18009KT|10SM FEW030", string.Join("|", Metar.Wrap("ALB 1451 18009KT 10SM FEW030", 16)));
        Eq("wrap long token", "ABCDE|FGH", string.Join("|", Metar.Wrap("ABCDEFGH", 5)));

        Eq("wrap report keeps prefix spacing",
            "ALB  1451 18009KT 10SM|FEW030 BKN049 03/M06|A3033 RMK AO2 SLP273|T00281056 53019",
            string.Join("|", Metar.WrapReport("ALB  1451 ", "18009KT 10SM FEW030 BKN049 03/M06 A3033 RMK AO2 SLP273 T00281056 53019", 25)));
        Eq("wrap report -M-", "BOS       -M-", string.Join("|", Metar.WrapReport("BOS       ", "-M-", 25)));
        Eq("wrap report empty body", "BOS", string.Join("|", Metar.WrapReport("BOS  ", "", 25)));

        // --- Profiles ---
        var dir = Path.Combine(Path.GetTempPath(), "crc-companion-test-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(dir);
        var p = new Profile { Name = "ZBW" };
        p.Altim.Stations.AddRange(new[] { "BOS", "PVD" });
        p.Altim.X = 120; p.Altim.Y = 340; p.Altim.Font = 9; p.Wx.Lines = 0;
        store.Save(p);
        var loaded = store.Load("ZBW");
        Eq("profile stations round-trip", "BOS,PVD", string.Join(",", loaded.Altim.Stations));
        Eq("profile position round-trip", 340, loaded.Altim.Y);
        Eq("font clamped", 5, loaded.Altim.Font);
        Eq("lines clamped", 1, loaded.Wx.Lines);
        store.Save(loaded.Clone("ZDC"));
        Eq("list", "ZBW,ZDC", string.Join(",", store.List()));
        store.Rename("ZDC", "ZID");
        Eq("rename", "ZBW,ZID", string.Join(",", store.List()));
        Eq("rename keeps data", "BOS,PVD", string.Join(",", store.Load("ZID").Altim.Stations));
        store.Delete("ZBW");
        Eq("delete", "ZID", string.Join(",", store.List()));
        Eq("clean name", "Z-BW_2", ProfileStore.CleanName(" z-bw_2! "));
        store.SaveSettings(new AppSettings { ActiveProfile = "ZID" });
        Eq("settings round-trip", "ZID", store.LoadSettings().ActiveProfile);
        Directory.Delete(dir, true);

        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}
