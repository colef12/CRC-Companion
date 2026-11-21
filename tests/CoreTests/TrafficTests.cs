using System;
using System.IO;
using System.Linq;
using CrcCompanion.Core;

internal static class TrafficTests
{
    // A box standing in for an ARTCC: lat 37..40, lon -80..-75, with two airports.
    private const string Dat = @"[Countries]
United States|K|Center
[Airports]
KIAD|Washington Dulles|38.9445|-77.4558|IAD|KTST|0
KDCA|Reagan National|38.8521|-77.0377|DCA|KTST|0
KJFK|Kennedy|40.6398|-73.7789|JFK|KZNY|0
[FIRs]
KTST|Test Center|TS|KTST
KTST|Test Center|TST|KTST
KZNY|New York|NY|KZNY
[UIRs]
";
    private const string Geo = @"{""type"":""FeatureCollection"",""features"":[
 {""type"":""Feature"",""properties"":{""id"":""KTST"",""oceanic"":""0""},""geometry"":{""type"":""MultiPolygon"",""coordinates"":[[[[-80,37],[-75,37],[-75,40],[-80,40],[-80,37]]]]}},
 {""type"":""Feature"",""properties"":{""id"":""KZNY"",""oceanic"":""0""},""geometry"":{""type"":""MultiPolygon"",""coordinates"":[[[[-75,40],[-72,40],[-72,42],[-75,42],[-75,40]]]]}}
]}";

    private const string Feed = @"{
 ""general"":{""update_timestamp"":""2026-11-21T19:30:00Z""},
 ""pilots"":[
  {""cid"":1,""callsign"":""AAL1"",""latitude"":38.5,""longitude"":-77.5,""altitude"":35000,""groundspeed"":450,""transponder"":""2201"",""heading"":90,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""A321"",""aircraft_faa"":""A321/L"",""departure"":""KATL"",""arrival"":""KJFK"",""altitude"":""35000"",""route"":""J75 LDN"",""assigned_transponder"":""2201""}},
  {""cid"":2,""callsign"":""DAL2"",""latitude"":38.9,""longitude"":-77.3,""altitude"":6000,""groundspeed"":250,""transponder"":""3302"",""heading"":300,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":"""",""aircraft_faa"":""H/B763/L"",""departure"":""KATL"",""arrival"":""KIAD"",""altitude"":""FL330"",""route"":""DIRECT"",""assigned_transponder"":""3302""}},
  {""cid"":3,""callsign"":""UAL3"",""latitude"":38.0,""longitude"":-81.0,""altitude"":37000,""groundspeed"":480,""transponder"":""4403"",""heading"":90,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""B738"",""aircraft_faa"":"""",""departure"":""KORD"",""arrival"":""KDCA"",""altitude"":""37000"",""route"":""OBK J146 GVE"",""assigned_transponder"":""4403""}},
  {""cid"":4,""callsign"":""SWA4"",""latitude"":38.0,""longitude"":-90.0,""altitude"":37000,""groundspeed"":450,""transponder"":""5504"",""heading"":90,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""B737"",""aircraft_faa"":"""",""departure"":""KLAX"",""arrival"":""KBWI"",""altitude"":""37000"",""route"":""X"",""assigned_transponder"":""5504""}},
  {""cid"":5,""callsign"":""JBU5"",""latitude"":38.945,""longitude"":-77.456,""altitude"":300,""groundspeed"":0,""transponder"":""1200"",""heading"":10,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""A320"",""aircraft_faa"":"""",""departure"":""KIAD"",""arrival"":""KBOS"",""altitude"":""34000"",""route"":""JCOBY4 JCOBY"",""assigned_transponder"":""6605""}},
  {""cid"":6,""callsign"":""N123AB"",""latitude"":39.5,""longitude"":-76.0,""altitude"":4500,""groundspeed"":120,""transponder"":""1200"",""heading"":180,""flight_plan"":null},
  {""cid"":7,""callsign"":""EGF7"",""latitude"":38.7,""longitude"":-77.6,""altitude"":9000,""groundspeed"":280,""transponder"":""7707"",""heading"":45,
   ""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""E75L"",""aircraft_faa"":"""",""departure"":""KCLT"",""arrival"":""KDCA"",""altitude"":""24000"",""route"":""Y"",""assigned_transponder"":""7707""}}
 ],
 ""prefiles"":[
  {""cid"":8,""callsign"":""FDX8"",""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""B752"",""aircraft_faa"":"""",""departure"":""KDCA"",""arrival"":""KMEM"",""altitude"":""31000"",""route"":""Z"",""assigned_transponder"":""""}},
  {""cid"":9,""callsign"":""JBU5"",""flight_plan"":{""flight_rules"":""I"",""aircraft_short"":""A320"",""aircraft_faa"":"""",""departure"":""KIAD"",""arrival"":""KBOS"",""altitude"":""34000"",""route"":""DUP"",""assigned_transponder"":""""}}
 ],
 ""controllers"":[
  {""cid"":1243205,""callsign"":""TS_12_CTR"",""frequency"":""133.700"",""facility"":6},
  {""cid"":100,""callsign"":""TST_APP"",""frequency"":""126.650"",""facility"":5},
  {""cid"":101,""callsign"":""NY_CTR"",""frequency"":""128.300"",""facility"":6},
  {""cid"":102,""callsign"":""TS_OBS"",""frequency"":""199.998"",""facility"":0}
 ]
}";

    private const string Radios = @"[
 {""callsign"":""AAL1"",""transceivers"":[{""id"":0,""frequency"":133700004,""latDeg"":38.5,""lonDeg"":-77.5},{""id"":1,""frequency"":121500000,""latDeg"":38.5,""lonDeg"":-77.5}]},
 {""callsign"":""DAL2"",""transceivers"":[{""id"":0,""frequency"":126650000,""latDeg"":38.9,""lonDeg"":-77.3}]},
 {""callsign"":""UAL3"",""transceivers"":[{""id"":0,""frequency"":128300000,""latDeg"":38.0,""lonDeg"":-81.0}]},
 {""callsign"":""EGF7"",""transceivers"":[{""id"":0,""frequency"":122800000,""latDeg"":38.7,""lonDeg"":-77.6}]},
 {""callsign"":""JBU5"",""transceivers"":[{""id"":0,""frequency"":133700000,""latDeg"":38.9,""lonDeg"":-77.4}]},
 {""callsign"":""TST_APP"",""transceivers"":[{""id"":0,""frequency"":126650000,""latDeg"":38.95,""lonDeg"":-77.45}]},
 {""callsign"":""NY_CTR"",""transceivers"":[{""id"":0,""frequency"":128300000,""latDeg"":41.0,""lonDeg"":-73.5}]},
 {""callsign"":""SAA369"",""transceivers"":[]}
]";

    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        var map = MapData.Parse(Dat, Geo);
        eq("map: prefix TS -> KTST", "KTST", map.FirForCallsign("TS_12_CTR"));
        eq("map: prefix TST -> KTST", "KTST", map.FirForCallsign("TST_APP"));
        eq("map: unknown prefix", null, map.FirForCallsign("XX_CTR"));
        eq("map: facility name TST -> KTST", "KTST", map.FirForFacility("TST"));
        eq("map: facility ZNY -> KZNY", "KZNY", map.FirForFacility("ZNY"));
        eq("map: airports in KTST", "DCA,IAD", string.Join(",", map.AirportsInFir("KTST").Select(a => a.Display).OrderBy(x => x)));
        var bnd = map.BoundaryForFir("KTST")!;
        check("geo: inside", bnd.Contains(new LatLon(38.5, -77.5)), "");
        check("geo: outside", !bnd.Contains(new LatLon(38.5, -81)), "");
        eq("geo: DCA-IAD ~20nm", 20, (int)Math.Round(CrcCompanion.Core.Geo.DistanceNm(new LatLon(38.8521, -77.0377), new LatLon(38.9445, -77.4558))));
        var m = CrcCompanion.Core.Geo.MinutesToEnter(bnd, new LatLon(38.0, -81.0), 90, 480, 20)!;
        check("geo: UAL3 enters in ~6 min", m > 5 && m < 7, $"got {m}");

        eq("feed: MHz parse", 133700000L, VatsimFeed.ParseMhz("133.700"));
        eq("feed: observer freq", 0L, VatsimFeed.ParseMhz("199.998"));
        check("feed: tolerant match", VatsimFeed.SameFrequency(122800004, 122800000), "");
        check("feed: 25 kHz apart differs", !VatsimFeed.SameFrequency(122800000, 122825000), "");
        eq("feed: FL330 -> 330", 330, VatsimFeed.ParseAltitudeHundreds("FL330"));
        eq("feed: 35000 -> 350", 350, VatsimFeed.ParseAltitudeHundreds("35000"));
        eq("feed: VFR -> 0", 0, VatsimFeed.ParseAltitudeHundreds("VFR"));
        eq("feed: H/B763/L -> B763", "B763", VatsimFeed.ShortType("", "H/B763/L"));

        var snap = VatsimFeed.ParseMain(Feed);
        VatsimFeed.AddTransceivers(snap, Radios);
        eq("feed: pilots", 7, snap.Pilots.Count);
        eq("feed: controllers", 4, snap.Controllers.Count);
        eq("feed: transceiver stations", 8, snap.Transceivers.Count);
        var me = snap.Controllers.First(c => c.Cid == 1243205);
        eq("session: find CID", "TS_12_CTR", me.Callsign);

        var ctx = new FacilityContext
        {
            Fir = "KTST",
            Boundary = bnd,
            Airports = map.AirportsInFir("KTST").ToDictionary(a => a.Icao, StringComparer.OrdinalIgnoreCase),
            MyCallsign = me.Callsign,
            MyFrequencyHz = me.FrequencyHz,
            FirForCallsign = map.FirForCallsign,
        };
        var now = new DateTime(2026, 11, 21, 19, 30, 0, DateTimeKind.Utc);
        var r = Traffic.Compute(snap, ctx, 20, now);

        eq("local controllers: only TST_APP", "TST_APP", string.Join(",", r.LocalControllers.Select(c => c.Callsign)));
        eq("in sector", "AAL1,DAL2,EGF7,N123AB", string.Join(",", r.InSector.Select(i => i.Callsign)));
        var byCs = r.InSector.ToDictionary(i => i.Callsign);
        eq("AAL1 on my freq (COM1)", FreqStatus.Mine, byCs["AAL1"].Freq);
        eq("DAL2 with approach", FreqStatus.Approach, byCs["DAL2"].Freq);
        eq("DAL2 with TST_APP", "TST_APP", byCs["DAL2"].With);
        eq("EGF7 on UNICOM -> none", FreqStatus.None, byCs["EGF7"].Freq);
        eq("N123AB no radios -> none", FreqStatus.None, byCs["N123AB"].Freq);

        eq("inbound: UAL3 only (SWA4 too far)", "UAL3", string.Join(",", r.Inbound.Select(i => i.Callsign)));
        eq("UAL3 tuned to NY_CTR (not local) -> none", FreqStatus.None, r.Inbound[0].Freq);
        eq("UAL3 entry time", "1936", r.Inbound[0].EntryUtc!.Value.ToString("HHmm"));

        eq("departures: FDX8 prefile, JBU5 on ground (duplicate prefile dropped)", "FDX8,JBU5",
            string.Join(",", r.Departures.Select(i => i.Callsign).OrderBy(x => x)));
        var jbu = r.Departures.First(i => i.Callsign == "JBU5");
        eq("JBU5 filed alt", 340, jbu.AltitudeHundreds);
        eq("JBU5 on my freq", FreqStatus.Mine, jbu.Freq);

        // Observer / offline: everyone inside is listed, even aircraft working other controllers.
        eq("filter: observing shows all", "AAL1,DAL2,EGF7,N123AB",
            string.Join(",", Traffic.VisibleInSector(r.InSector, connected: false, showOthers: false).Select(i => i.Callsign)));
        eq("filter: controlling hides others", "AAL1,EGF7,N123AB",
            string.Join(",", Traffic.VisibleInSector(r.InSector, connected: true, showOthers: false).Select(i => i.Callsign)));
        eq("filter: OTHERS on shows all", "AAL1,DAL2,EGF7,N123AB",
            string.Join(",", Traffic.VisibleInSector(r.InSector, connected: true, showOthers: true).Select(i => i.Callsign)));

        // Observer whose CID matches: own logon excluded from local controllers, no frequency.
        var obsSnap = VatsimFeed.ParseMain(Feed.Replace("\"cid\":1243205,\"callsign\":\"TS_12_CTR\",\"frequency\":\"133.700\",\"facility\":6",
                                                       "\"cid\":1243205,\"callsign\":\"TS_OBS\",\"frequency\":\"199.998\",\"facility\":0"));
        VatsimFeed.AddTransceivers(obsSnap, Radios);
        var obsMe = obsSnap.Controllers.First(c => c.Cid == 1243205);
        eq("observer: facility 0", 0, obsMe.Facility);
        eq("observer: no frequency", 0L, obsMe.FrequencyHz);
        var obsCtx = new FacilityContext { Fir = "KTST", Boundary = bnd, Airports = ctx.Airports, MyCallsign = obsMe.Callsign, MyFrequencyHz = 0, FirForCallsign = map.FirForCallsign };
        var obs = Traffic.Compute(obsSnap, obsCtx, 20, now);
        eq("observer: AAL1 on the (other) controller's freq is not 'mine'", FreqStatus.None, obs.InSector.First(i => i.Callsign == "AAL1").Freq);
        eq("observer: all listed", 4, Traffic.VisibleInSector(obs.InSector, false, false).Count());

        eq("strip: in sector line", "* AAL1     A321 JFK   350 2201 J75 LDN",
            StripFormat.InSector(byCs["AAL1"], 24, true, ctx.DisplayAirport));
        eq("strip: untuned flag", "? EGF7     E75L DCA   090 7707 Y",
            StripFormat.InSector(byCs["EGF7"], 24, true, ctx.DisplayAirport));
        eq("strip: inbound line", "  UAL3     B738 E1936 370 4403 OBK J146 GVE",
            StripFormat.Inbound(r.Inbound[0], 24, true));
        eq("strip: prefile line", "P FDX8     B752 MEM   310      Z",
            StripFormat.Departure(r.Departures.First(i => i.Callsign == "FDX8"), 24, ctx.DisplayAirport));
        eq("strip: route cut", "* AAL1     A321 JFK   350 2201 J7",
            StripFormat.InSector(byCs["AAL1"], 2, true, ctx.DisplayAirport));

        // Real VATSIM map data, when available locally (downloaded during development).
        if (File.Exists("/tmp/vatspy.dat") && File.Exists("/tmp/bnd.geojson"))
        {
            var real = MapData.Parse(File.ReadAllText("/tmp/vatspy.dat"), File.ReadAllText("/tmp/bnd.geojson"));
            eq("real: DC_12_CTR -> KZDC", "KZDC", real.FirForCallsign("DC_12_CTR"));
            eq("real: WAS_CTR -> KZDC", "KZDC", real.FirForCallsign("WAS_CTR"));
            eq("real: IND_CTR -> KZID", "KZID", real.FirForCallsign("IND_CTR"));
            eq("real: profile ZDC -> KZDC", "KZDC", real.FirForFacility("ZDC"));
            var zdc = real.BoundaryForFir("KZDC")!;
            check("real: IAD inside ZDC", zdc.Contains(real.Airport("KIAD")!.Pos), "");
            check("real: ATL outside ZDC", !zdc.Contains(real.Airport("KATL")!.Pos), "");
            var zdcAirports = real.AirportsInFir("KZDC").Select(a => a.Icao).ToHashSet();
            check("real: ZDC airports include IAD DCA BWI RIC ORF", new[] { "KIAD", "KDCA", "KBWI", "KRIC", "KORF" }.All(zdcAirports.Contains), "");
            Console.WriteLine($"      (real map data: {real.AirportCount} airports, {real.BoundaryCount} boundaries, {zdcAirports.Count} in ZDC)");
        }
    }
}
