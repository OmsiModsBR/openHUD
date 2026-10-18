using System.Collections.Immutable;
using System.Globalization;

namespace OpenHud.Packages.Realmap;

// A loaded openomsi-realmap package (MAP_PACKAGE.md): the manifest, the files that are read whole
// (markers, regions, lines) and an index over surface.oms whose chunks are read on demand.
// Values are untrusted. Each layer degrades alone: a missing or corrupt file switches that layer
// off, a corrupt chunk loses that chunk, and nothing here throws out to the caller.
public sealed class RealmapPackage
{
    public const long MaxCfgBytes = 4L * 1024 * 1024;
    public const int MaxMarkers = 20_000;
    public const int MaxRegions = 2_000;
    public const int MaxPolygonPoints = 20_000;
    public const int MaxLineForms = 5_000;
    public const int MaxChunksPerQuery = 256;
    public const long MaxLinesFileBytes = 32L * 1024 * 1024;

    private SurfaceFile? surface;
    private ImmutableArray<MarkerFeature> markers = ImmutableArray<MarkerFeature>.Empty;
    private ImmutableDictionary<string, RealmapIcon> icons = ImmutableDictionary<string, RealmapIcon>.Empty;
    private ImmutableArray<(RegionFeature Region, double MinX, double MinY, double MaxX, double MaxY)> regions = ImmutableArray<(RegionFeature, double, double, double, double)>.Empty;
    private ImmutableArray<TransitLine> lines = ImmutableArray<TransitLine>.Empty;

    private RealmapPackage(PackageStatus status, IReadOnlyList<PackageDiagnostic> diagnostics) { Status = status; Diagnostics = diagnostics; }

    public PackageStatus Status { get; private set; }
    public IReadOnlyList<PackageDiagnostic> Diagnostics { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public RealmapManifest? Manifest { get; private set; }
    public RealmapLayers Layers { get; private set; }
    public IReadOnlyList<MarkerFeature> Markers => markers;
    public IReadOnlyList<RegionFeature> Regions => regions.Select(x => x.Region).ToList();
    public IReadOnlyList<TransitLine> TransitLines => lines;

    // The author's tile grid, to be used by the frame model so game positions share the package's metres.
    public double? GridMetres => Manifest?.GridMetres;

    public static RealmapPackage None { get; } = new(PackageStatus.Absent, Array.Empty<PackageDiagnostic>());

    // Through the port: no bridge needed in tests.
    public static RealmapPackage Open(IRealmapSource source, string mapIdentity)
    {
        var sink = new DiagnosticSink();
        try
        {
            var located = source.Locate(mapIdentity);
            if (!located.Found || located.Directory is null)
            {
                if (located.ErrorCode is not null) sink.Ignored("openomsi-realmap", located.ErrorCode + ": " + located.Detail);
                return new RealmapPackage(located.ErrorCode is null ? PackageStatus.Absent : PackageStatus.Ignored, sink.ToList());
            }
            return OpenDirectory(located.Directory, located.Fingerprint);
        }
        catch (Exception exception)
        {
            sink.Ignored("openomsi-realmap", "locating the package failed: " + exception.GetType().Name);
            return new RealmapPackage(PackageStatus.Ignored, sink.ToList());
        }
    }

    public static RealmapPackage OpenDirectory(string? directory, string fingerprint = "")
    {
        var sink = new DiagnosticSink();
        var package = new RealmapPackage(PackageStatus.Absent, Array.Empty<PackageDiagnostic>()) { Fingerprint = fingerprint };
        try
        {
            var folder = PackageFolder.Open(directory);
            if (folder is not null) package.Load(folder, sink);
        }
        catch (Exception exception)
        {
            // A reader bug must cost the layer, not the session.
            sink.Ignored("openomsi-realmap", "unexpected failure while reading: " + exception.GetType().Name);
            package = new RealmapPackage(PackageStatus.Ignored, Array.Empty<PackageDiagnostic>()) { Fingerprint = fingerprint };
        }
        package.Diagnostics = sink.ToList();
        return package;
    }

    private void Load(PackageFolder folder, DiagnosticSink sink)
    {
        var bytes = folder.ReadAll("manifest.cfg", 256 * 1024, out var reason);
        if (bytes is null)
        {
            if (reason != "not found") { sink.Ignored("manifest.cfg", reason ?? "unreadable"); Status = PackageStatus.Ignored; }
            return;
        }
        var manifest = ParseManifest(bytes, sink);
        if (manifest is null) { Status = PackageStatus.Ignored; return; }
        Manifest = manifest;
        Status = PackageStatus.Loaded;
        if (Fingerprint.Length == 0) Fingerprint = folder.Fingerprint();

        // surface.oms
        var surfacePath = folder.Resolve("surface.oms", out var surfaceReason);
        if (surfacePath is null) { if (surfaceReason != "not found") sink.Dropped("surface.oms", surfaceReason ?? "unreadable"); }
        else
        {
            surface = SurfaceFile.Open(surfacePath, manifest.TerrainOffsetMetres, sink);
            if (surface is not null) Layers |= RealmapLayers.Surface;
        }

        try { ReadMarkers(folder, sink); } catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException) { sink.Dropped("markers.cfg", "unusable: " + exception.Message); markers = ImmutableArray<MarkerFeature>.Empty; }
        try { ReadRegions(folder, sink); } catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException) { sink.Dropped("regions.cfg", "unusable: " + exception.Message); regions = ImmutableArray<(RegionFeature, double, double, double, double)>.Empty; }
        try { ReadLines(folder, sink); } catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException) { sink.Dropped("lines.lin", "unusable: " + exception.Message); lines = ImmutableArray<TransitLine>.Empty; }
        if (!markers.IsEmpty) Layers |= RealmapLayers.Markers;
        if (!regions.IsEmpty) Layers |= RealmapLayers.Regions;
        if (!lines.IsEmpty) Layers |= RealmapLayers.TransitLines;
    }

    // ---------------------------------------------------------------- manifest.cfg

    private static readonly string[] ColorKeys = { "land", "water", "river", "green", "grass", "forest", "sport", "farm", "built", "rail", "rail_edge", "border", "contour" };

    private static RealmapManifest? ParseManifest(byte[] bytes, DiagnosticSink sink)
    {
        var cfg = CfgReader.Parse(bytes, "manifest.cfg", sink);
        if (cfg is null) { sink.Ignored("openomsi-realmap", "manifest.cfg is unusable"); return null; }
        if (!int.TryParse(cfg.Get("format"), NumberStyles.None, CultureInfo.InvariantCulture, out var format) || format != 1)
        { sink.Ignored("openomsi-realmap", "unknown package format '" + cfg.Get("format") + "'"); return null; }
        if (!TryDouble(cfg.Get("grid"), out var grid) || grid <= 1 || grid > 100_000)
        { sink.Ignored("openomsi-realmap", "manifest grid is missing or not a plausible tile size"); return null; }

        var north = 0.0;
        if (cfg.Get("north") is { } northText)
        {
            if (TryDouble(northText, out var n) && Math.Abs(n) <= 3600) north = ((n % 360) + 360) % 360;
            else sink.Dropped("manifest.cfg north", "not a number of degrees; 0 used");
        }
        var terrain = 0.0;
        if (cfg.Get("terrain_offset") is { } offsetText)
        {
            if (TryDouble(offsetText, out var t) && Math.Abs(t) <= 20_000) terrain = t;
            else sink.Dropped("manifest.cfg terrain_offset", "not a plausible number of metres; 0 used");
        }
        var alignment = Alignment.Unspecified;
        if (cfg.Get("alignment") is { } alignText)
        {
            switch (alignText.Trim().ToLowerInvariant())
            {
                case "exact": alignment = Alignment.Exact; break;
                case "fitted": alignment = Alignment.Fitted; break;
                default: sink.Dropped("manifest.cfg alignment", "must be exact or fitted"); break;
            }
        }

        var dark = ImmutableDictionary.CreateBuilder<string, HudColor>(StringComparer.Ordinal);
        var light = ImmutableDictionary.CreateBuilder<string, HudColor>(StringComparer.Ordinal);
        foreach (var key in ColorKeys)
        {
            Color(key, dark);
            Color("light_" + key, light, key);
        }
        void Color(string key, ImmutableDictionary<string, HudColor>.Builder target, string? alias = null)
        {
            if (cfg.Get(key) is not { } text) return;
            if (HudColor.TryParse(text, out var c)) target[alias ?? key] = c;
            else sink.Dropped("manifest.cfg " + key, "not a #rrggbb colour; the game's own is used");
        }

        HudColor? Player(string key) => cfg.Get(key) is { } t ? (HudColor.TryParse(t, out var c) ? c : Warn(key)) : null;
        HudColor? Warn(string key) { sink.Dropped("manifest.cfg " + key, "not a #rrggbb colour"); return null; }
        var style = new PlayerMarkerStyle(
            string.Equals(cfg.Get("player")?.Trim(), "triangle", StringComparison.OrdinalIgnoreCase),
            Player("player_free"), Player("player_line"), Player("player_brt"), List(cfg.Get("brt_lines"), false));

        return new RealmapManifest(Clip(cfg.Get("name")), grid, Clip(cfg.Get("credit")), north, alignment, terrain,
            dark.ToImmutable(), light.ToImmutable(), style,
            List(cfg.Get("tram_vehicles"), true), List(cfg.Get("metro_vehicles"), true), List(cfg.Get("rail_vehicles"), true));
    }

    private static ImmutableArray<string> List(string? text, bool pathLike)
    {
        if (string.IsNullOrWhiteSpace(text)) return ImmutableArray<string>.Empty;
        return text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x.Length <= 120)
            .Select(x => pathLike ? x.ToLowerInvariant().Replace('\\', '/') : x).Take(64).ToImmutableArray();
    }

    private static string? Clip(string? value) => value is null ? null : value.Length <= 400 ? value : value[..400];

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    // ---------------------------------------------------------------- markers.cfg / icons/

    private void ReadMarkers(PackageFolder folder, DiagnosticSink sink)
    {
        if (!folder.Exists("markers.cfg")) return;
        var bytes = folder.ReadAll("markers.cfg", MaxCfgBytes, out var reason);
        if (bytes is null) { sink.Dropped("markers.cfg", reason ?? "unreadable"); return; }
        var cfg = CfgReader.Parse(bytes, "markers.cfg", sink, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "marker" });
        if (cfg is null) return;
        var list = ImmutableArray.CreateBuilder<MarkerFeature>();
        var iconMap = ImmutableDictionary.CreateBuilder<string, RealmapIcon>(StringComparer.OrdinalIgnoreCase);
        var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cfg.All("marker"))
        {
            var at = "markers.cfg:" + entry.Line;
            if (list.Count >= MaxMarkers) { sink.Dropped("markers.cfg", "more than " + MaxMarkers + " markers; the rest is not read"); break; }
            var parts = entry.Value.Split('|').Select(x => x.Trim()).ToArray();
            if (parts.Length < 4) { sink.Dropped(at, "expected icon | width | x | y | name"); continue; }
            var icon = parts[0];
            if (PackageFolder.UnsafeReason(icon) is { } unsafeReason) { sink.Dropped(at, "unsafe icon path: " + unsafeReason); continue; }
            if (!icon.StartsWith("icons/", StringComparison.OrdinalIgnoreCase) || !icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { sink.Dropped(at, "icons are PNG files under icons/"); continue; }
            if (bad.Contains(icon)) { sink.Dropped(at, "icon unusable (see above)"); continue; }
            if (!iconMap.ContainsKey(icon))
            {
                if (!TryIcon(folder, icon, at, sink, out var info)) { bad.Add(icon); continue; }
                iconMap[icon] = info!;
            }
            if (!TryDouble(parts[1], out var width) || width < 1 || width > 1024) { sink.Dropped(at, "icon width must be 1..1024 pixels"); continue; }
            if (!TryDouble(parts[2], out var x) || !TryDouble(parts[3], out var y) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7) { sink.Dropped(at, "position is not a plausible pair of metres"); continue; }
            list.Add(new MarkerFeature(iconMap[icon].Id, (float)width, (float)x, (float)y, parts.Length > 4 && parts[4].Length > 0 ? Clip(parts[4]) : null));
        }
        markers = list.ToImmutable();
        icons = iconMap.ToImmutable();
    }

    private static bool TryIcon(PackageFolder folder, string icon, string at, DiagnosticSink sink, out RealmapIcon? info)
    {
        info = null;
        var head = folder.ReadPrefix(icon, 33, out var length, out var reason);
        if (head is null) { sink.Dropped(at, "icon " + icon + ": " + reason); return false; }
        if (!AssetProbe.TryProbePng(head, length, out var w, out var h, out reason)) { sink.Dropped(at, "icon " + icon + ": " + reason); return false; }
        if (w > 1024 || h > 1024) { sink.Dropped(at, "icon " + icon + ": larger than 1024x1024"); return false; }
        info = new RealmapIcon(icon.ToLowerInvariant(), w, h, length, folder.Resolve(icon, out _)!);
        return true;
    }

    public RealmapIcon? Icon(string id) => icons.TryGetValue(id, out var icon) ? icon : null;

    // ---------------------------------------------------------------- regions.cfg

    private void ReadRegions(PackageFolder folder, DiagnosticSink sink)
    {
        if (!folder.Exists("regions.cfg")) return;
        var bytes = folder.ReadAll("regions.cfg", MaxCfgBytes, out var reason);
        if (bytes is null) { sink.Dropped("regions.cfg", reason ?? "unreadable"); return; }
        var cfg = CfgReader.Parse(bytes, "regions.cfg", sink, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "region" });
        if (cfg is null) return;
        var list = ImmutableArray.CreateBuilder<(RegionFeature, double, double, double, double)>();
        foreach (var entry in cfg.All("region"))
        {
            var at = "regions.cfg:" + entry.Line;
            if (list.Count >= MaxRegions) { sink.Dropped("regions.cfg", "more than " + MaxRegions + " regions; the rest is not read"); break; }
            var parts = entry.Value.Split('|').Select(x => x.Trim()).ToArray();
            if (parts.Length != 6 || parts[0].Length == 0) { sink.Dropped(at, "expected name | label x | label y | width | height | polygon"); continue; }
            if (!TryDouble(parts[1], out var lx) || !TryDouble(parts[2], out var ly) || !TryDouble(parts[3], out var bw) || !TryDouble(parts[4], out var bh)
                || Math.Abs(lx) > 1e7 || Math.Abs(ly) > 1e7 || bw < 0 || bh < 0 || bw > 1e6 || bh > 1e6) { sink.Dropped(at, "label position or box is not plausible"); continue; }
            var tokens = parts[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3 || tokens.Length > MaxPolygonPoints) { sink.Dropped(at, "polygon needs 3 to " + MaxPolygonPoints + " points"); continue; }
            var polygon = new float[tokens.Length * 2];
            var ok = true;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var i = 0; i < tokens.Length && ok; i++)
            {
                var xy = tokens[i].Split(',');
                if (xy.Length != 2 || !TryDouble(xy[0], out var px) || !TryDouble(xy[1], out var py) || Math.Abs(px) > 1e7 || Math.Abs(py) > 1e7) { ok = false; break; }
                polygon[i * 2] = (float)px; polygon[i * 2 + 1] = (float)py;
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
            }
            if (!ok) { sink.Dropped(at, "polygon has a point that is not an x,y pair of plausible metres"); continue; }
            list.Add((new RegionFeature(Clip(parts[0])!, (float)lx, (float)ly, (float)bw, (float)bh, polygon), minX, minY, maxX, maxY));
        }
        regions = list.ToImmutable();
    }

    // ---------------------------------------------------------------- lines.lin

    private void ReadLines(PackageFolder folder, DiagnosticSink sink)
    {
        if (!folder.Exists("lines.lin")) return;
        var bytes = folder.ReadAll("lines.lin", MaxLinesFileBytes, out var reason);
        if (bytes is null) { sink.Dropped("lines.lin", reason ?? "unreadable"); return; }
        var text = CfgReader.Decode(bytes, "lines.lin", sink);
        if (text is null) return;

        var forms = ImmutableArray.CreateBuilder<TransitLine>();
        double ox = 0, oy = 0;
        var versionOk = false;
        var lineNo = 0;
        // The form being built.
        (string Number, string Group, string Name, TransitKind Kind, int From, int To)? head = null;
        var routes = new List<TransitRoute>();
        (float Km, int Stops, int Minutes, bool Circular, string Days, string Model, string Itinerary)? route = null;
        float[]? way = null, stopPoints = null;

        void FlushRoute()
        {
            if (route is { } r)
            {
                if (routes.Count < 2) routes.Add(new TransitRoute(r.Km, r.Stops, r.Minutes, r.Circular, r.Days, r.Model, r.Itinerary, way ?? Array.Empty<float>(), stopPoints ?? Array.Empty<float>()));
                else sink.Dropped("lines.lin", "a line form has at most two routes; extra ones dropped");
            }
            route = null; way = null; stopPoints = null;
        }
        void FlushForm()
        {
            FlushRoute();
            if (head is { } h)
            {
                if (forms.Count < MaxLineForms) forms.Add(new TransitLine(h.Number, h.Group, h.Name, h.Kind, h.From, h.To, routes.ToImmutableArray()));
                else if (forms.Count == MaxLineForms) sink.Dropped("lines.lin", "more than " + MaxLineForms + " line forms; the rest is not read");
            }
            head = null; routes = new List<TransitRoute>();
        }

        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            var at = "lines.lin:" + lineNo;
            switch (f[0])
            {
                case "RLIN":
                    versionOk = f.Length > 1 && f[1].Trim() == "1";
                    if (!versionOk) { sink.Ignored("lines.lin", "unknown version"); return; }
                    break;
                case "ORIGEM":
                    if (f.Length < 3 || !TryDouble(f[1], out ox) || !TryDouble(f[2], out oy) || Math.Abs(ox) > 1e7 || Math.Abs(oy) > 1e7) { sink.Dropped(at, "bad ORIGEM; 0,0 used"); ox = oy = 0; }
                    break;
                case "L":
                    FlushForm();
                    if (!versionOk) { sink.Ignored("lines.lin", "no RLIN header"); return; }
                    if (f.Length < 4 || f[1].Length == 0) { sink.Dropped(at, "line record needs number, group, name"); break; }
                    var kind = TransitKind.Bus;
                    var dropForm = false;
                    if (f.Length > 4 && f[4].Trim().Length > 0)
                    {
                        switch (f[4].Trim().ToLowerInvariant())
                        {
                            case "bus": kind = TransitKind.Bus; break;
                            case "tram": kind = TransitKind.Tram; break;
                            case "metro": kind = TransitKind.Metro; break;
                            case "rail": kind = TransitKind.Rail; break;
                            default: dropForm = true; sink.Dropped(at, "unknown kind of vehicle '" + f[4].Trim() + "'"); break;
                        }
                    }
                    var from = f.Length > 5 ? Date(f[5]) : 0;
                    var to = f.Length > 6 ? Date(f[6]) : 0;
                    if (from < 0 || to < 0) { sink.Dropped(at, "from/to must be yyyymmdd or 0"); break; }
                    if (!dropForm) head = (Clip(f[1])!, Clip(f[2])!, Clip(f[3])!, kind, from, to);
                    break;
                case "R":
                    FlushRoute();
                    if (head is null) break;
                    if (f.Length < 8 || !TryDouble(f[1], out var km) || !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stops)
                        || !int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || km < 0 || km > 100_000)
                    { sink.Dropped(at, "route record needs km, stops, minutes, circular, days, model, itinerary"); break; }
                    route = ((float)km, stops, minutes, f[4].Trim() == "1", Clip(f[5])!, Clip(f[6])!, Clip(f[7])!);
                    break;
                case "P":
                case "S":
                    if (route is null) break;
                    var points = f.Length > 1 ? Pairs(f[1], ox, oy) : Array.Empty<float>();
                    if (points is null) { sink.Dropped(at, "coordinates are not pairs of plausible metres"); points = Array.Empty<float>(); }
                    if (f[0] == "P") way = points; else stopPoints = points;
                    break;
                default:
                    break; // unknown records are ignored (forward compatibility)
            }
        }
        FlushForm();
        lines = forms.ToImmutable();

        static int Date(string text)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var d)) return -1;
            if (d == 0) return 0;
            var y = d / 10000; var m = d / 100 % 100; var day = d % 100;
            return y is >= 1 and <= 9999 && m is >= 1 and <= 12 && day is >= 1 and <= 31 ? d : -1;
        }
    }

    // "x y x y ..." relative to the origin -> absolute pairs, or null when any value is bad.
    private static float[]? Pairs(string text, double ox, double oy)
    {
        var tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 2 != 0 || tokens.Length > 400_000) return null;
        var result = new float[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryDouble(tokens[i], out var v) || Math.Abs(v) > 1e7) return null;
            result[i] = (float)(v + (i % 2 == 0 ? ox : oy));
        }
        return result;
    }

    // The forms of lines that run on a date (yyyymmdd), optionally of one kind of vehicle.
    public IReadOnlyList<TransitLine> LinesOn(int yyyymmdd, TransitKind? kind = null) =>
        lines.Where(x => x.RunsOn(yyyymmdd) && (kind is null || x.Kind == kind)).ToList();

    // ---------------------------------------------------------------- queries

    // Plain geometry inside the window. Never throws; a window that is not finite or ordered gives an empty view.
    public RealmapView Query(MapRect window, DetailLevel detail)
    {
        if (!window.IsValid) return RealmapView.Empty;
        try { return QueryCore(window, detail); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or OverflowException or InvalidOperationException)
        {
            return RealmapView.Empty;
        }
    }

    private RealmapView QueryCore(MapRect window, DetailLevel detail)
    {
        var areas = new List<AreaFeature>();
        var lineList = new List<LineFeature>();
        var streets = new List<LineFeature>();
        var labels = new List<LabelFeature>();
        var truncated = false;
        var unreadable = 0;

        if (surface is not null)
        {
            var keys = surface.ChunksIn(window, MaxChunksPerQuery, out truncated);
            var seenLines = new HashSet<(byte, int, float, float, float, float, float, float)>();
            var seenLabels = new HashSet<(byte, float, float, string?)>();
            var minRank = detail switch { DetailLevel.Overview => 160, DetailLevel.Normal => 64, _ => 0 };
            var tolerance = detail switch { DetailLevel.Overview => 6.0, DetailLevel.Normal => 1.5, _ => 0.0 };
            foreach (var key in keys)
            {
                var chunk = surface.Load(key);
                if (chunk is null) { unreadable++; continue; }
                foreach (var area in chunk.Areas)
                {
                    var triangles = TrianglesIn(area.Triangles, window);
                    if (triangles.Length > 0) areas.Add(new AreaFeature(area.Class, triangles));
                }
                foreach (var line in chunk.Lines)
                {
                    if (line.Class == LineClass.Contour && detail != DetailLevel.Close) continue;
                    if (line.Class == LineClass.IndexContour && detail == DetailLevel.Overview) continue;
                    if (!BoundsMeet(line.Points, window)) continue;
                    if (!seenLines.Add(Signature(line))) continue; // whole-map layers repeat in every chunk they touch
                    lineList.Add(tolerance > 0 ? line with { Points = Simplify(line.Points, tolerance) } : line);
                }
                if (detail == DetailLevel.Close)
                    foreach (var street in chunk.Streets)
                        if (BoundsMeet(street.Points, window) && seenLines.Add(Signature(street))) streets.Add(street);
                foreach (var label in chunk.Labels)
                    if (label.Rank >= minRank && label.X >= window.MinX && label.X <= window.MaxX && label.Y >= window.MinY && label.Y <= window.MaxY && seenLabels.Add(((byte)label.Kind, label.X, label.Y, label.Text)))
                        labels.Add(label);
            }
            areas.Sort((a, b) => a.Class.CompareTo(b.Class));
        }

        var markerList = markers.Where(m => m.X >= window.MinX && m.X <= window.MaxX && m.Y >= window.MinY && m.Y <= window.MaxY).ToList();
        var regionList = regions.Where(r => window.Intersects(r.MinX, r.MinY, r.MaxX, r.MaxY)).Select(r => r.Region).ToList();
        return new RealmapView(areas, lineList, streets, labels, markerList, regionList, truncated, unreadable);
    }

    private static (byte, int, float, float, float, float, float, float) Signature(LineFeature line)
    {
        var n = line.PointCount;
        var mid = (n / 2) * 2;
        return ((byte)line.Class, n, line.Points[0], line.Points[1], line.Points[mid], line.Points[mid + 1], line.Points[^2], line.Points[^1]);
    }

    private static float[] TrianglesIn(float[] triangles, MapRect w)
    {
        var kept = new List<float>();
        for (var i = 0; i + 5 < triangles.Length; i += 6)
        {
            var minX = Math.Min(triangles[i], Math.Min(triangles[i + 2], triangles[i + 4]));
            var maxX = Math.Max(triangles[i], Math.Max(triangles[i + 2], triangles[i + 4]));
            var minY = Math.Min(triangles[i + 1], Math.Min(triangles[i + 3], triangles[i + 5]));
            var maxY = Math.Max(triangles[i + 1], Math.Max(triangles[i + 3], triangles[i + 5]));
            if (!w.Intersects(minX, minY, maxX, maxY)) continue;
            for (var k = 0; k < 6; k++) kept.Add(triangles[i + k]);
        }
        return kept.ToArray();
    }

    private static bool BoundsMeet(float[] points, MapRect w)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            minX = Math.Min(minX, points[i]); maxX = Math.Max(maxX, points[i]);
            minY = Math.Min(minY, points[i + 1]); maxY = Math.Max(maxY, points[i + 1]);
        }
        return w.Intersects(minX, minY, maxX, maxY);
    }

    // Douglas-Peucker, iterative (no recursion depth to attack), ends always kept.
    internal static float[] Simplify(float[] points, double tolerance)
    {
        var n = points.Length / 2;
        if (n <= 2) return points;
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double worst = -1; var at = -1;
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i * 2], points[i * 2 + 1], points[a * 2], points[a * 2 + 1], points[b * 2], points[b * 2 + 1]);
                if (d > worst) { worst = d; at = i; }
            }
            if (at >= 0 && worst > tolerance) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        var result = new List<float>();
        for (var i = 0; i < n; i++) if (keep[i]) { result.Add(points[i * 2]); result.Add(points[i * 2 + 1]); }
        return result.ToArray();
    }

    internal static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax; var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 <= 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        var cx = ax + t * dx - px; var cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    // The nearest street piece within reach metres (the spec uses 30 m for the player), or null.
    public StreetHit? StreetAt(double x, double y, double reachMetres = 30)
    {
        if (surface is null || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(reachMetres) || reachMetres <= 0 || reachMetres > 2000) return null;
        try
        {
            LineFeature? best = null;
            var bestDistance = reachMetres;
            foreach (var key in surface.ChunksIn(MapRect.Around(x, y, reachMetres), 9, out _))
            {
                var chunk = surface.Load(key);
                if (chunk is null) continue;
                foreach (var street in chunk.Streets)
                    for (var i = 0; i + 3 < street.Points.Length; i += 2)
                    {
                        var d = DistanceToSegment(x, y, street.Points[i], street.Points[i + 1], street.Points[i + 2], street.Points[i + 3]);
                        if (d <= bestDistance) { bestDistance = d; best = street; }
                    }
            }
            return best is null ? null : new StreetHit(best.Name, best.StreetRank, best.SpeedLimitKmh, best.HeavyLimitKmh, bestDistance);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or OverflowException) { return null; }
    }

    // The municipality the point lies in (smallest polygon when they nest), or null.
    public RegionFeature? RegionAt(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        RegionFeature? best = null;
        var bestArea = double.MaxValue;
        foreach (var (region, minX, minY, maxX, maxY) in regions)
        {
            if (x < minX || x > maxX || y < minY || y > maxY) continue;
            if (!Contains(region.Polygon, x, y)) continue;
            var area = (maxX - minX) * (maxY - minY);
            if (area < bestArea) { best = region; bestArea = area; }
        }
        return best;
    }

    internal static bool Contains(float[] polygon, double x, double y)
    {
        var inside = false;
        var n = polygon.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = polygon[i * 2], yi = polygon[i * 2 + 1], xj = polygon[j * 2], yj = polygon[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
}
