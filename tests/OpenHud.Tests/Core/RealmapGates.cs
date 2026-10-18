using System.Buffers.Binary;
using System.Text;
using OpenHud.Packages;
using OpenHud.Packages.Realmap;

namespace OpenHud.Tests.Core;

internal static class RealmapGates
{
    public static void Register()
    {
        Gates.Run("realmap.manifest", ManifestGate);
        Gates.Run("realmap.manifest-rejections", ManifestRejections);
        Gates.Run("realmap.source-port", SourcePort);
        Gates.Run("realmap.chunk-addressing-and-detail", ChunkQueries);
        Gates.Run("realmap.whole-map-lines-not-repeated", Dedupe);
        Gates.Run("realmap.simplification", Simplification);
        Gates.Run("realmap.street-lookup", Streets);
        Gates.Run("realmap.regions", RegionsGate);
        Gates.Run("realmap.markers-and-icons", MarkersGate);
        Gates.Run("realmap.transit-lines", TransitLinesGate);
        Gates.Run("realmap.query-bounds", QueryBounds);
        Gates.Run("realmap.corrupt-surface-header", CorruptHeader);
        Gates.Run("realmap.corrupt-chunks-degrade-per-chunk", CorruptChunks);
        Gates.Run("realmap.bad-values-dropped-per-primitive", BadPrimitives);
        Gates.Run("realmap.layers-degrade-independently", LayersIndependent);
        Gates.Run("realmap.truncation-at-every-length", TruncationSweep);
        Gates.Run("realmap.random-corruption-never-throws", RandomCorruption);
    }

    private static RealmapPackage Open(TempDir dir) => RealmapPackage.OpenDirectory(dir.Path);
    private static MapRect Around(double x, double y, double h) => MapRect.Around(x, y, h);

    private static RealmapPackage Standard(out TempDir dir)
    {
        dir = new TempDir();
        RealmapFiles.Write(dir, RealmapFiles.Standard());
        return Open(dir);
    }

    private static void ManifestGate()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        Expect.Equal(PackageStatus.Loaded, p.Status, "loaded (CRLF manifest)");
        var m = p.Manifest!;
        Expect.True(m.Name == "Synthetic Map" && m.GridMetres == 300 && m.Credit == "(c) synthetic test data", "name, grid, credit");
        Expect.Near(172.2, m.NorthDegrees, 1e-9, "north");
        Expect.Equal(Alignment.Fitted, m.Alignment, "alignment");
        Expect.Near(12.5, m.TerrainOffsetMetres, 1e-9, "terrain offset");
        Expect.True(m.DarkColors["water"] == new HudColor(0x7f, 0xb2, 0xe5) && m.LightColors["water"] == new HudColor(0x11, 0x22, 0x33), "dark and light colours");
        Expect.True(!m.DarkColors.ContainsKey("built") && Expect.Mentions(p.Diagnostics, "built"), "an invalid colour is dropped and logged");
        Expect.True(m.Player.Triangle && m.Player.Free == new HudColor(255, 255, 255) && m.Player.OnLine == new HudColor(0x55, 0x88, 0xc7) && m.Player.OnBrtLine == new HudColor(0x1e, 0x7b, 0x34), "player marker (inline comment stripped)");
        Expect.True(m.Player.BrtLines.SequenceEqual(new[] { "A1", "A2", "7" }), "brt_lines trimmed");
        Expect.True(m.TramVehicles.SequenceEqual(new[] { "vehicles/tram/", "vehicles/tram2/" }) && m.MetroVehicles.SequenceEqual(new[] { "vehicles/metro/" }) && m.RailVehicles.IsEmpty, "vehicle path lists normalised");
        Expect.Equal(300.0, p.GridMetres, "GridMetres");
        Expect.True(p.Layers == (RealmapLayers.Surface | RealmapLayers.Markers | RealmapLayers.Regions | RealmapLayers.TransitLines), "all layers on");
        Expect.True(p.Fingerprint.Length == 64, "fingerprint computed when the source gave none");
        // north wraps and survives odd values.
        using var d2 = new TempDir();
        d2.Write("manifest.cfg", "format = 1\ngrid = 585.795\nnorth = -90\nalignment = sideways\nterrain_offset = x\n");
        var q = Open(d2);
        Expect.Near(270, q.Manifest!.NorthDegrees, 1e-9, "negative north wraps");
        Expect.True(q.Manifest.Alignment == Alignment.Unspecified && q.Manifest.TerrainOffsetMetres == 0 && q.Manifest.Name is null, "optional keys default");
        Expect.Near(585.795, q.Manifest.GridMetres, 1e-9, "fractional grid");
    }

    private static void ManifestRejections()
    {
        foreach (var (text, why) in new[]
        {
            ("grid = 300\n", "no format"), ("format = 2\ngrid = 300\n", "unknown format"), ("format = 1\n", "no grid"), ("format = 1\ngrid = 1\n", "grid not above 1"),
            ("format = 1\ngrid = 0\n", "zero grid"), ("format = 1\ngrid = -300\n", "negative grid"), ("format = 1\ngrid = abc\n", "non-numeric grid"), ("format = 1\ngrid = NaN\n", "NaN grid"),
            ("format = 1\ngrid = Infinity\n", "infinite grid"), ("format = 1\ngrid = 1e9\n", "absurd grid"),
        })
        {
            using var dir = new TempDir();
            dir.Write("manifest.cfg", text);
            dir.Write("markers.cfg", RealmapFiles.Markers);
            var p = Open(dir);
            Expect.Equal(PackageStatus.Ignored, p.Status, why + " ignores the package");
            Expect.True(p.Manifest is null && p.Layers == RealmapLayers.None && p.Markers.Count == 0, why + ": no layer is read");
            Expect.True(p.Query(Around(0, 0, 100), DetailLevel.Close).Areas.Count == 0, why + ": queries answer empty");
        }
        using var none = new TempDir();
        Expect.Equal(PackageStatus.Absent, Open(none).Status, "no manifest: no package");
        Expect.Equal(PackageStatus.Absent, RealmapPackage.OpenDirectory(Path.Combine(none.Path, "nowhere")).Status, "no folder: no package");
        Expect.Equal(PackageStatus.Absent, RealmapPackage.OpenDirectory(null).Status, "null folder");
        using var bom = new TempDir();
        bom.Write("manifest.cfg", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("format = 1\r\ngrid = 300\r\n")).ToArray());
        Expect.Equal(PackageStatus.Loaded, Open(bom).Status, "BOM + CRLF");
        using var bad = new TempDir();
        bad.Write("manifest.cfg", new byte[] { 0x66, 0xFF, 0xFE });
        Expect.Equal(PackageStatus.Ignored, Open(bad).Status, "invalid UTF-8 manifest");
        using var dup = new TempDir();
        dup.Write("manifest.cfg", "format = 1\ngrid = 300\ngrid = 5\n");
        var pd = Open(dup);
        Expect.True(pd.Manifest!.GridMetres == 300 && Expect.Mentions(pd.Diagnostics, "manifest.cfg", "duplicate"), "duplicate keys: first wins, logged");
    }

    private sealed class FakeSource : IRealmapSource
    {
        public Func<string, RealmapSourceResult> Locator = _ => new(false, null, "", null, null);
        public string? LastIdentity;
        public RealmapSourceResult Locate(string mapIdentity) { LastIdentity = mapIdentity; return Locator(mapIdentity); }
    }

    private static void SourcePort()
    {
        var source = new FakeSource();
        Expect.Equal(PackageStatus.Absent, RealmapPackage.Open(source, @"maps\x\global.cfg").Status, "not found is not an error");
        Expect.Equal(@"maps\x\global.cfg", source.LastIdentity, "identity is passed through");
        source.Locator = _ => new(false, null, "", "OL_E_MODULE_CONTENT_INPUT_INVALID", "reparse point");
        var unsafePackage = RealmapPackage.Open(source, "m");
        Expect.True(unsafePackage.Status == PackageStatus.Ignored && Expect.Mentions(unsafePackage.Diagnostics, "realmap", "reparse"), "an unsafe package is ignored with the bridge's reason");
        source.Locator = _ => throw new IOException("bridge down");
        Expect.Equal(PackageStatus.Ignored, RealmapPackage.Open(source, "m").Status, "a throwing source is contained");
        using var dir = new TempDir();
        RealmapFiles.Write(dir, RealmapFiles.Standard());
        source.Locator = _ => new(true, dir.Path, "FINGERPRINT-FROM-BRIDGE", null, null);
        var ok = RealmapPackage.Open(source, "m");
        Expect.True(ok.Status == PackageStatus.Loaded && ok.Fingerprint == "FINGERPRINT-FROM-BRIDGE", "found: loaded, the bridge's fingerprint is kept as the cache key");
        source.Locator = _ => new(true, Path.Combine(dir.Path, "gone"), "x", null, null);
        Expect.Equal(PackageStatus.Absent, RealmapPackage.Open(source, "m").Status, "a folder that vanished");
    }

    private static void ChunkQueries()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        // Origin (1000, 2000); chunk (0,0) covers x 1000..3000, y 2000..4000.
        var close = p.Query(Around(1500, 2500, 300), DetailLevel.Close);
        Expect.True(close.Areas.Count == 1 && close.Areas[0].Class == FillClass.Water && close.Areas[0].TriangleCount == 1, "only the water triangle meets the window");
        Expect.Near(1100, close.Areas[0].Triangles[0], 0.01, "triangle is in map space (origin + offset)");
        Expect.True(close.Lines.Any(l => l.Class == LineClass.River && l.Name == "Synthetic River" && l.Width == 6), "river line with its name and width");
        Expect.True(close.Lines.All(l => l.Class != LineClass.Border && !l.IsContour), "border and contours are outside this window");
        Expect.Equal(2, close.Streets.Count, "two streets at close detail");
        Expect.Equal(2, close.Labels.Count, "both labels at close detail");
        Expect.Equal(0, close.UnreadableChunks, "nothing unreadable");

        var normal = p.Query(Around(1500, 2500, 300), DetailLevel.Normal);
        Expect.True(normal.Labels.Count == 1 && normal.Labels[0].Text == "Centre" && normal.Labels[0].Kind == LabelKind.District, "normal detail keeps important labels only");
        Expect.Equal(0, normal.Streets.Count, "streets are not returned below close detail");
        var overview = p.Query(Around(1500, 2500, 300), DetailLevel.Overview);
        Expect.True(overview.Labels.Count == 1 && overview.Streets.Count == 0, "overview labels and streets");

        // Contours: heights above sea level with the manifest's terrain offset applied.
        var win = Around(1500, 2950, 200);
        var c = p.Query(win, DetailLevel.Close).Lines.Where(l => l.IsContour).OrderBy(l => l.Width).ToList();
        Expect.True(c.Count == 2 && c[0].Class == LineClass.Contour && c[1].Class == LineClass.IndexContour, "both contour classes at close detail");
        Expect.Near(112.5, c[0].HeightMetres!.Value, 1e-3, "contour height + terrain offset");
        Expect.Near(162.5, c[1].HeightMetres!.Value, 1e-3, "index contour height + terrain offset");
        var n = p.Query(win, DetailLevel.Normal).Lines.Where(l => l.IsContour).ToList();
        Expect.True(n.Count == 1 && n[0].Class == LineClass.IndexContour, "normal detail keeps index contours only");
        Expect.Equal(0, p.Query(win, DetailLevel.Overview).Lines.Count(l => l.IsContour), "overview has no contours");

        // Negative chunk coordinates.
        var west = p.Query(Around(300, 2200, 300), DetailLevel.Close);
        Expect.True(west.Areas.Count == 1 && west.Areas[0].Class == FillClass.Forest, "chunk (-1,0)");
        Expect.True(west.Labels.Count == 1 && west.Labels[0].Text == "Terminal" && west.Labels[0].Kind == LabelKind.Terminal && west.Labels[0].Rank == 255, "label in chunk (-1,0)");
        // A window across three chunks sees all of them, areas ordered by class.
        var wide = p.Query(new MapRect(0, 2000, 4000, 2600), DetailLevel.Close);
        Expect.True(wide.Areas.Select(a => a.Class).SequenceEqual(new[] { FillClass.Water, FillClass.Built, FillClass.Forest }.OrderBy(x => x)), "areas from three chunks, sorted by class");
        Expect.Equal(4, wide.Labels.Count, "labels from three chunks (the unknown kind is skipped)");
        // The unknown fill and line classes were skipped without breaking the chunk.
        Expect.True(wide.Areas.All(a => a.Class <= FillClass.Farm), "unknown fill class skipped");
        // A window in empty space.
        var empty = p.Query(Around(20000, 20000, 100), DetailLevel.Close);
        Expect.True(empty.Areas.Count == 0 && empty.Lines.Count == 0 && empty.Labels.Count == 0 && empty.UnreadableChunks == 0, "empty area");
        // Triangles are filtered one by one.
        Expect.Equal(0, p.Query(Around(2000, 3000, 50), DetailLevel.Close).Areas.Count, "no triangle meets this window");
    }

    private static void Dedupe()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        var v = p.Query(new MapRect(2900, 3400, 3100, 3700), DetailLevel.Close);
        Expect.Equal(1, v.Lines.Count(l => l.Class == LineClass.Border), "the border stored in two chunks is returned once");
        // Same for a window that merely touches both chunks' labels.
        Expect.Equal(1, p.Query(new MapRect(3400, 2400, 3600, 2600), DetailLevel.Close).Labels.Count(l => l.Text == "Town"), "label once");
    }

    private static void Simplification()
    {
        var flat = new float[] { 0, 0, 10, 0.3f, 20, 0, 30, 0.2f, 40, 0 };
        var s = RealmapPackage.Simplify(flat, 1.0);
        Expect.True(s.SequenceEqual(new float[] { 0, 0, 40, 0 }), "points within tolerance are dropped, the ends kept");
        Expect.True(RealmapPackage.Simplify(flat, 0.1).Length == flat.Length, "tight tolerance keeps everything");
        var peak = new float[] { 0, 0, 10, 0.3f, 20, 0, 25, 30, 30, 0, 40, 0.2f, 50, 0 };
        var sp = RealmapPackage.Simplify(peak, 1.0);
        Expect.True(sp.Length < peak.Length && sp[0] == 0 && sp[^2] == 50 && Enumerable.Range(0, sp.Length / 2).Any(i => sp[i * 2] == 25 && sp[i * 2 + 1] == 30), "the peak survives, ends kept");
        Expect.Equal(4, RealmapPackage.Simplify(new float[] { 0, 0, 5, 5 }, 100).Length, "two points stay");
        var big = new float[2 * 50_000];
        for (var i = 0; i < 50_000; i++) { big[i * 2] = i; big[i * 2 + 1] = (float)Math.Sin(i * 0.001) * 100; }
        var reduced = RealmapPackage.Simplify(big, 2.0);
        Expect.True(reduced.Length < big.Length / 10 && reduced[0] == 0 && reduced[^2] == 49_999, "a long smooth line shrinks (iterative, no stack overflow)");
        Expect.Near(5, RealmapPackage.DistanceToSegment(5, 5, 0, 0, 10, 0), 1e-9, "distance to segment");
        Expect.Near(Math.Sqrt(50), RealmapPackage.DistanceToSegment(15, 5, 0, 0, 10, 0), 1e-9, "distance past the end");
        Expect.Near(Math.Sqrt(29), RealmapPackage.DistanceToSegment(0, 3, 5, 5, 5, 5), 1e-9, "degenerate segment does not divide by zero");
    }

    private static void Streets()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        var a = p.StreetAt(1500, 2215)!;
        Expect.True(a.Name == "Street A" && a.Rank == 2 && a.SpeedLimitKmh is null && Math.Abs(a.DistanceMetres - 15) < 0.01, "street A with its rank");
        var b = p.StreetAt(1500, 2290)!;
        Expect.True(b.Name == "Avenue B" && b.SpeedLimitKmh == 60 && b.HeavyLimitKmh == 40 && b.Rank is null, "street with limits");
        Expect.True(p.StreetAt(1500, 2250) is null, "50 m from both streets is out of the default 30 m reach");
        Expect.True(p.StreetAt(1500, 2250, 60) is not null, "a wider reach finds one");
        Expect.True(p.StreetAt(50_000, 50_000) is null, "far from everything");
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
            Expect.True(p.StreetAt(bad, 0) is null && p.StreetAt(0, bad) is null, "non-finite point");
        Expect.True(p.StreetAt(1500, 2215, -1) is null && p.StreetAt(1500, 2215, 0) is null && p.StreetAt(1500, 2215, 1e9) is null, "unusable reach");
    }

    private static void RegionsGate()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        Expect.Equal(2, p.Regions.Count, "broken regions are dropped");
        Expect.True(Expect.Mentions(p.Diagnostics, "regions.cfg:4") && Expect.Mentions(p.Diagnostics, "regions.cfg:5") && Expect.Mentions(p.Diagnostics, "regions.cfg:6"), "each bad region is logged with its line");
        Expect.Equal("Inner", p.RegionAt(5000, 3000)!.Name, "the smaller nested region wins");
        Expect.Equal("Alpha", p.RegionAt(2000, 2000)!.Name, "outer region");
        Expect.True(p.RegionAt(0, 0) is null && p.RegionAt(double.NaN, 1) is null, "outside / NaN");
        Expect.True(p.Regions.First(r => r.Name == "Alpha").HasReadableLabel && !p.Regions.First(r => r.Name == "Inner").HasReadableLabel, "a zero box has no readable label");
        var v = p.Query(Around(5000, 3000, 10), DetailLevel.Close);
        Expect.Equal(2, v.Regions.Count, "query returns the regions meeting the window");
        Expect.Equal(0, p.Query(Around(-5000, -5000, 10), DetailLevel.Close).Regions.Count, "none far away");
        Expect.True(RealmapPackage.Contains(new float[] { 0, 0, 10, 0, 10, 10, 0, 10 }, 5, 5) && !RealmapPackage.Contains(new float[] { 0, 0, 10, 0, 10, 10, 0, 10 }, 15, 5), "point in polygon");
        // A concave polygon.
        var l = new float[] { 0, 0, 10, 0, 10, 10, 5, 10, 5, 5, 0, 5 };
        Expect.True(RealmapPackage.Contains(l, 7, 8) && !RealmapPackage.Contains(l, 2, 8), "concave polygon");
    }

    private static void MarkersGate()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        Expect.Equal(3, p.Markers.Count, "valid markers kept, the rest dropped");
        foreach (var line in new[] { 5, 6, 7, 8, 9, 10, 11 }) Expect.True(Expect.Mentions(p.Diagnostics, "markers.cfg:" + line), "marker on line " + line + " is logged");
        var terminal = p.Markers[0];
        Expect.True(terminal.IconId == "icons/terminal.png" && terminal.WidthPixels == 30 && terminal.X == 1200.5f && terminal.Name == "Central Terminal", "marker fields");
        var icon = p.Icon(terminal.IconId)!;
        Expect.True(icon.Width == 64 && icon.Height == 64 && File.Exists(icon.FilePath), "icon info from the PNG header");
        Expect.True(p.Icon("icons/huge.png") is null && p.Icon("../manifest.cfg") is null, "refused icons are not exposed");
        var v = p.Query(Around(1200, 2840, 100), DetailLevel.Close);
        Expect.True(v.Markers.Count == 1 && v.Markers[0].Name == "Central Terminal", "query filters markers by window");
        // A marker whose picture is replaced by a link/escape is refused.
        using var outside = new TempDir();
        outside.Write("x.png", Synthetic.Png(8, 8));
        using var d = new TempDir();
        d.Write("manifest.cfg", "format = 1\ngrid = 300\n");
        Expect.True(TempDir.MakeJunction(d.Combine("icons"), outside.Path), "junction for the test");
        d.Write("markers.cfg", "marker = icons/x.png | 20 | 1 | 1 | linked\n");
        Expect.Equal(0, Open(d).Markers.Count, "an icons/ folder that is a link yields nothing");
        // Too many markers are capped.
        using var many = new TempDir();
        many.Write("manifest.cfg", "format = 1\ngrid = 300\n");
        many.Write("icons/a.png", Synthetic.Png(8, 8));
        many.Write("markers.cfg", string.Concat(Enumerable.Range(0, RealmapPackage.MaxMarkers + 50).Select(i => $"marker = icons/a.png | 10 | {i} | 0 | m\n")));
        Expect.Equal(RealmapPackage.MaxMarkers, Open(many).Markers.Count, "marker count is capped");
    }

    private static void TransitLinesGate()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        var lines = p.TransitLines;
        Expect.Equal(4, lines.Count, "four valid line forms");
        var first = lines[0];
        Expect.True(first.Number == "7" && first.Kind == TransitKind.Bus && first.Routes.Length == 2 && first.From == 0 && first.To == 0, "first form, a third route is dropped");
        Expect.True(first.Routes[0].Stops == 35 && first.Routes[0].Minutes == 35 && !first.Routes[0].Circular && first.Routes[0].Itinerary == "North - Central", "route fields");
        Expect.True(first.Routes[0].Way.SequenceEqual(new float[] { 101, 202, 103, 204 }), "the way is made absolute with ORIGEM");
        Expect.True(first.Routes[1].StopPoints.SequenceEqual(new float[] { 103, 204 }), "stops");
        Expect.True(lines[1].Routes[0].Circular && lines[1].To == 19891231, "second era of line 7");
        Expect.True(lines[2].Kind == TransitKind.Tram && lines[2].From == 19900101 && lines[3].Kind == TransitKind.Metro, "kinds");
        Expect.True(!lines.Any(l => l.Number is "X1" or "B1"), "unknown kind and invalid date are dropped");
        Expect.True(Expect.Mentions(p.Diagnostics, "lines.lin", "hovercraft") && Expect.Mentions(p.Diagnostics, "lines.lin", "yyyymmdd"), "and logged");
        Expect.Equal(3, p.LinesOn(19900615).Count, "forms that run on 1990-06-15");
        Expect.True(p.LinesOn(19900615).All(l => l.To == 0 || l.To >= 19900615), "the old era is out");
        Expect.True(p.LinesOn(19891231).Any(l => l.Name.EndsWith("(old)")) && p.LinesOn(19891231).All(l => l.Kind != TransitKind.Tram), "on 1989-12-31 the old era runs and the tram does not");
        Expect.Equal(1, p.LinesOn(19900615, TransitKind.Metro).Count, "filter by kind");
        // Hostile lines.lin.
        foreach (var (text, why) in new[] { ("RLIN\t2\nL\t1\ta\tb\n", "unknown version"), ("L\t1\ta\tb\tbus\n", "no header"), ("", "empty"), ("RLIN\t1\nL\n", "short record") })
        {
            using var h = new TempDir();
            h.Write("manifest.cfg", "format = 1\ngrid = 300\n");
            h.Write("lines.lin", text);
            var ph = Open(h);
            Expect.True(ph.Status == PackageStatus.Loaded && ph.TransitLines.Count == 0, why + ": no lines, package fine");
        }
        using var junk = new TempDir();
        junk.Write("manifest.cfg", "format = 1\ngrid = 300\n");
        junk.Write("lines.lin", "RLIN\t1\nORIGEM\tx\ty\nL\t1\tG\tName\nR\tnan\t1\t1\t0\ta\tb\tc\nP\t1 2 3\nR\t1\t1\t1\t0\ta\tb\tc\nP\t1e30 1\nS\t1 2\n");
        var pj = Open(junk);
        Expect.True(pj.TransitLines.Count == 1 && pj.TransitLines[0].Routes.Length == 1 && pj.TransitLines[0].Routes[0].Way.Length == 0, "bad route dropped; bad coordinates leave an empty way, not garbage");
        using var big = new TempDir();
        big.Write("manifest.cfg", "format = 1\ngrid = 300\n");
        big.Write("lines.lin", "RLIN\t1\n" + string.Concat(Enumerable.Range(0, RealmapPackage.MaxLineForms + 20).Select(i => $"L\t{i}\tG\tN\tbus\n")));
        Expect.Equal(RealmapPackage.MaxLineForms, Open(big).TransitLines.Count, "line forms are capped");
    }

    private static void QueryBounds()
    {
        var p = Standard(out var dir);
        using var _ = dir;
        foreach (var bad in new[]
        {
            new MapRect(double.NaN, 0, 1, 1), new MapRect(0, 0, double.PositiveInfinity, 1), new MapRect(10, 0, 0, 1), new MapRect(0, 10, 1, 0), default,
        })
        {
            var v = p.Query(bad, DetailLevel.Close);
            Expect.True(v.Areas.Count == 0 && v.Lines.Count == 0 && v.Labels.Count == 0, "invalid window answers empty: " + bad);
        }
        var giant = p.Query(new MapRect(-1e300, -1e300, 1e300, 1e300), DetailLevel.Close);
        Expect.True(giant.Areas.Count > 0, "an absurdly large window is served from the chunks that exist");
        Expect.True(p.Query(new MapRect(-1e18, -1e18, 1e18, 1e18), DetailLevel.Overview).Areas.Count > 0, "huge window, overview");
        Expect.True(p.Query(new MapRect(1e300, 1e300, 1e301, 1e301), DetailLevel.Close).Areas.Count == 0, "far window");

        // Many chunks: a window larger than one query serves is truncated to the nearest chunks.
        var many = new SurfaceBuilder { OriginX = 0, OriginY = 0, ChunkSize = 100 };
        for (var i = 0; i < 300; i++) many.Chunk(i, 0).Fills.Add((0, SurfaceBuilder.Tri(i * 100 + 10, 10, 10)));
        using var d = new TempDir();
        RealmapFiles.Write(d, many, markers: false, regions: false, lines: false);
        var pm = Open(d);
        var all = pm.Query(new MapRect(0, 0, 30000, 100), DetailLevel.Close);
        Expect.True(all.Truncated && all.Areas.Count == RealmapPackage.MaxChunksPerQuery, "only " + RealmapPackage.MaxChunksPerQuery + " chunks are used");
        var middle = pm.Query(new MapRect(14000, 0, 16000, 100), DetailLevel.Close);
        Expect.True(!middle.Truncated && middle.Areas.Count > 0, "a normal window is not truncated");
        var centred = pm.Query(new MapRect(14900, 0, 15100, 100), DetailLevel.Close);
        Expect.True(centred.Areas.Count > 0, "centred");
        var nearest = pm.Query(new MapRect(0, 0, 30000, 100), DetailLevel.Close).Areas.Select(a => a.Triangles[0]).ToList();
        Expect.True(nearest.Contains(15010f) || nearest.Contains(15110f), "the nearest chunks to the centre are the ones kept");
    }

    private static void CorruptHeader()
    {
        byte[] Good() => RealmapFiles.Standard().Build();
        void Check(string what, Func<byte[], byte[]> corrupt, bool surfaceOff = true, string? reason = null)
        {
            using var d = new TempDir();
            RealmapFiles.Write(d, null);
            d.Write("surface.oms", corrupt(Good()));
            var p = Open(d);
            Expect.Equal(PackageStatus.Loaded, p.Status, what + ": the package stays");
            Expect.Equal(surfaceOff, !p.Layers.HasFlag(RealmapLayers.Surface), what + ": surface layer " + (surfaceOff ? "off" : "on"));
            Expect.True(p.Layers.HasFlag(RealmapLayers.Markers) && p.Layers.HasFlag(RealmapLayers.Regions) && p.Layers.HasFlag(RealmapLayers.TransitLines), what + ": other layers unaffected");
            if (reason is not null) Expect.True(Expect.Mentions(p.Diagnostics, "surface.oms", reason), what + ": explained");
            var v = p.Query(new MapRect(-1e5, -1e5, 1e5, 1e5), DetailLevel.Close);
            Expect.True(v.Markers.Count > 0 || surfaceOff, what + ": query works");
        }
        Check("bad magic", b => { b[0] = (byte)'X'; return b; }, reason: "magic");
        Check("version 2", b => { b[4] = 2; return b; }, reason: "version");
        Check("version 0", b => { b[4] = 0; return b; }, reason: "version");
        Check("empty file", _ => Array.Empty<byte>(), reason: "short");
        Check("three bytes", b => b[..3], reason: "short");
        Check("NaN chunk size", b => { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(40), float.NaN); return b; }, reason: "chunk size");
        Check("zero chunk size", b => { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(40), 0); return b; }, reason: "chunk size");
        Check("huge chunk size", b => { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(40), 1e12f); return b; }, reason: "chunk size");
        Check("NaN origin", b => { BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(8), double.NaN); return b; }, reason: "origin");
        Check("origin 1e300", b => { BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(16), 1e300); return b; }, reason: "origin");
        Check("string count past the file", b => { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(44), uint.MaxValue); return b; }, reason: "string");
        Check("string length past the file", b => { BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(48), ushort.MaxValue); return b; }, reason: "header");
        var builder = RealmapFiles.Standard();
        var indexAt = (int)builder.IndexStart();
        Check("chunk count past the file", b => { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(indexAt - 4), uint.MaxValue); return b; }, reason: "index");
        Check("chunk count 2^31", b => { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(indexAt - 4), 0x80000000); return b; }, reason: "index");
    }

    private static void CorruptChunks()
    {
        // An index entry pointing past the end: that chunk is dropped, the others work.
        var builder = RealmapFiles.Standard();
        var bytes = builder.Build();
        var indexAt = (int)builder.IndexStart();
        var patched = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(patched.AsSpan(indexAt + 8), ulong.MaxValue);
        using (var d = new TempDir())
        {
            RealmapFiles.Write(d, null);
            d.Write("surface.oms", patched);
            var p = Open(d);
            Expect.True(p.Layers.HasFlag(RealmapLayers.Surface) && Expect.Mentions(p.Diagnostics, "surface.oms", "outside"), "bad index entry logged, surface on");
            var v = p.Query(new MapRect(-1e4, 0, 1e4, 1e4), DetailLevel.Close);
            Expect.True(v.Areas.Count >= 2 && v.UnreadableChunks == 0, "the other chunks are served");
        }
        // Offsets inside the header, and zero length.
        foreach (var (what, off, len) in new[] { ("offset inside the header", 10UL, 50u), ("zero length", 0UL, 0u), ("oversized length", 0UL, uint.MaxValue) })
        {
            var b = (byte[])bytes.Clone();
            var entryOffset = off == 0 ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(indexAt + 8)) : off;
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(indexAt + 8), entryOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(indexAt + 16), len);
            using var d = new TempDir();
            RealmapFiles.Write(d, null);
            d.Write("surface.oms", b);
            var v = Open(d).Query(new MapRect(-1e4, 0, 1e4, 1e4), DetailLevel.Close);
            Expect.True(v.Areas.Count >= 2, what + ": the rest is served");
        }
        // Duplicate chunk keys: the first entry counts.
        var dup = RealmapFiles.Standard();
        dup.Chunk(0, 0).Fills.Add((6, SurfaceBuilder.Tri(100, 100)));
        using (var d = new TempDir())
        {
            RealmapFiles.Write(d, dup);
            var p = Open(d);
            Expect.True(Expect.Mentions(p.Diagnostics, "surface.oms", "repeated"), "duplicates are logged");
            Expect.True(p.Query(Around(1500, 2500, 300), DetailLevel.Close).Areas.All(a => a.Class != FillClass.Farm), "the first duplicate wins");
        }
        // Payloads that lie about their structure.
        foreach (var (what, mutate) in new (string, Action<SurfaceBuilder.ChunkSpec>)[]
        {
            ("fill count past the payload", c => c.FillCountOverride = uint.MaxValue),
            ("line count past the payload", c => c.LineCountOverride = 0x7FFFFFFF),
            ("label count past the payload", c => c.LabelCountOverride = uint.MaxValue),
            ("payload cut in half", c => c.TruncatePayload = true),
        })
        {
            var lying = RealmapFiles.Standard();
            var spec = lying.Chunk(5, 5);
            spec.Fills.Add((0, SurfaceBuilder.Tri(11000, 10000)));
            spec.Lines.Add((0, 0, 1, uint.MaxValue, new float[] { 11000, 10000, 11100, 10100 }));
            spec.Labels.Add((0, 1, uint.MaxValue, 11000, 10000));
            mutate(spec);
            using var d = new TempDir();
            RealmapFiles.Write(d, lying);
            var p = Open(d);
            var bad = p.Query(Around(12000, 12000, 500), DetailLevel.Close);
            Expect.Equal(1, bad.UnreadableChunks, what + " marks exactly the lying chunk unreadable");
            var good = p.Query(Around(1500, 2500, 300), DetailLevel.Close);
            Expect.True(good.Areas.Count == 1 && good.UnreadableChunks == 0, what + ": the other chunks are unaffected");
        }
    }

    private static void BadPrimitives()
    {
        var b = new SurfaceBuilder { OriginX = 0, OriginY = 0, ChunkSize = 1000 };
        var c = b.Chunk(0, 0);
        c.Fills.Add((0, new float[] { 1, 1, 2, 1, 1, 2 }));                                  // good
        c.Fills.Add((0, new float[] { float.NaN, 1, 2, 1, 1, 2, 10, 10, 20, 10, 10, 20 }));  // first triangle bad, second good
        c.Fills.Add((1, new float[] { float.PositiveInfinity, 1, 2, 1, 1, 2 }));              // all bad: no feature
        c.Fills.Add((2, new float[] { 1e9f, 1, 2, 1, 1, 2 }));                                 // absurd coordinate
        c.Lines.Add((0, 0, 3, uint.MaxValue, new float[] { 1, 1, 5, 5 }));                    // good
        c.Lines.Add((0, 0, 3, uint.MaxValue, new float[] { 1, 1, float.NaN, 5 }));            // NaN point
        c.Lines.Add((0, 0, 3, uint.MaxValue, new float[] { 1, 1 }));                           // one point
        c.Lines.Add((0, 0, float.NaN, uint.MaxValue, new float[] { 1, 1, 5, 5 }));            // NaN width
        c.Lines.Add((3, 1, 0, 9999, new float[] { 2, 2, 6, 6 }));                              // name index out of range
        c.Labels.Add((0, 5, uint.MaxValue, 10, 10));                                           // good, no name
        c.Labels.Add((0, 5, 9999, 20, 20));                                                    // name out of range
        c.Labels.Add((0, 5, uint.MaxValue, float.NaN, 20));                                    // NaN
        c.Labels.Add((0, 5, uint.MaxValue, 1e12f, 20));                                        // absurd
        using var d = new TempDir();
        RealmapFiles.Write(d, b, markers: false, regions: false, lines: false);
        var p = Open(d);
        var v = p.Query(new MapRect(-100, -100, 1000, 1000), DetailLevel.Close);
        Expect.Equal(0, v.UnreadableChunks, "the chunk itself is readable");
        Expect.True(v.Areas.Sum(a => a.TriangleCount) == 2 && v.Areas.All(a => a.Class == FillClass.Water), "bad triangles dropped one by one");
        Expect.Equal(1, v.Lines.Count, "bad lines dropped one by one");
        Expect.Equal(1, v.Streets.Count, "street kept");
        Expect.True(v.Streets[0].Name is null, "a name index out of range is no name");
        Expect.Equal(2, v.Labels.Count, "bad labels dropped one by one");
        Expect.True(v.Labels.All(l => l.Text is null), "unnamed labels have no text");
        Expect.True(v.Areas.All(a => a.Triangles.All(float.IsFinite)) && v.Lines.All(l => l.Points.All(float.IsFinite)), "everything returned is finite");
    }

    private static void LayersIndependent()
    {
        // Each file corrupt on its own: only that layer goes away.
        using var good = new TempDir();
        RealmapFiles.Write(good, RealmapFiles.Standard());
        var all = Open(good);
        var everything = RealmapLayers.Surface | RealmapLayers.Markers | RealmapLayers.Regions | RealmapLayers.TransitLines;
        Expect.True(all.Layers == everything, "baseline");
        foreach (var (file, off) in new[] { ("surface.oms", RealmapLayers.Surface), ("markers.cfg", RealmapLayers.Markers), ("regions.cfg", RealmapLayers.Regions), ("lines.lin", RealmapLayers.TransitLines) })
        {
            foreach (var junk in new byte[][] { new byte[] { 0xFF, 0xFE, 0x00, 0x80 }, new byte[0], Enumerable.Repeat((byte)'x', 5000).ToArray() })
            {
                using var d = new TempDir();
                RealmapFiles.Write(d, RealmapFiles.Standard());
                d.Write(file, junk);
                var p = Open(d);
                Expect.True(p.Status == PackageStatus.Loaded && p.Layers == (everything & ~off), $"{file} garbage ({junk.Length} bytes) switches off exactly that layer, got {p.Layers}");
            }
        }
        // A file replaced by a directory of the same name.
        using var dd = new TempDir();
        RealmapFiles.Write(dd, null);
        Directory.CreateDirectory(dd.Combine("surface.oms"));
        Expect.True(!Open(dd).Layers.HasFlag(RealmapLayers.Surface), "surface.oms that is a folder");
        // Oversized text files are refused as a whole.
        using var big = new TempDir();
        RealmapFiles.Write(big, null, markers: false, regions: false);
        big.Write("regions.cfg", new string('#', (int)RealmapPackage.MaxCfgBytes + 10));
        Expect.True(!Open(big).Layers.HasFlag(RealmapLayers.Regions) && Open(big).Layers.HasFlag(RealmapLayers.TransitLines), "oversized regions.cfg");
        // Surface off: queries still serve markers/regions.
        using var noSurface = new TempDir();
        RealmapFiles.Write(noSurface, null);
        var ns = Open(noSurface);
        var vs = ns.Query(new MapRect(0, 0, 10000, 10000), DetailLevel.Close);
        Expect.True(vs.Areas.Count == 0 && vs.Markers.Count == 3 && vs.Regions.Count == 2 && ns.StreetAt(1, 1) is null, "no surface: markers and regions only");
    }

    private static void TruncationSweep()
    {
        var bytes = RealmapFiles.Standard().Build();
        using var d = new TempDir();
        RealmapFiles.Write(d, null);
        var path = d.Combine("surface.oms");
        for (var length = 0; length <= bytes.Length; length++)
        {
            File.WriteAllBytes(path, bytes[..length]);
            var p = Open(d);
            var v = p.Query(new MapRect(-1e5, -1e5, 1e5, 1e5), DetailLevel.Close);
            p.StreetAt(1500, 2215);
            Expect.True(p.Layers.HasFlag(RealmapLayers.Markers), "markers unaffected at length " + length);
            if (length == bytes.Length) Expect.True(v.Areas.Count >= 3 && v.UnreadableChunks == 0, "the whole file reads completely");
        }
    }

    private static void RandomCorruption()
    {
        var original = RealmapFiles.Standard().Build();
        var random = new Random(20261017);
        using var d = new TempDir();
        RealmapFiles.Write(d, null);
        var path = d.Combine("surface.oms");
        var served = 0;
        for (var round = 0; round < 600; round++)
        {
            var b = (byte[])original.Clone();
            var flips = 1 + random.Next(8);
            for (var i = 0; i < flips; i++)
            {
                var at = random.Next(b.Length);
                b[at] = random.Next(3) switch { 0 => (byte)random.Next(256), 1 => 0xFF, _ => (byte)(b[at] ^ (1 << random.Next(8))) };
            }
            File.WriteAllBytes(path, b);
            var p = Open(d);
            foreach (var window in new[] { new MapRect(-1e5, -1e5, 1e5, 1e5), Around(1500, 2500, 400) })
            {
                var v = p.Query(window, DetailLevel.Close);
                foreach (var area in v.Areas) Expect.True(area.Triangles.All(float.IsFinite), "finite triangles after corruption");
                foreach (var line in v.Lines.Concat(v.Streets)) Expect.True(line.Points.All(float.IsFinite) && line.Points.Length >= 4, "finite lines after corruption");
                if (v.Areas.Count > 0) served++;
            }
            p.StreetAt(1500, 2215);
        }
        Expect.True(served > 100, "many corrupted files still serve their intact chunks (" + served + ")");
    }
}
