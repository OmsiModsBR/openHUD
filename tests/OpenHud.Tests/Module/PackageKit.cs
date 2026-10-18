using OmsiLaunch.Runtime.Modules;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// Synthetic packages for the map "Synthetic Map", written below the session's folder the way the bridge
// finds them: <root>\maps\<map>\openomsi-realmap and openomsi-pausemenu next to it.
internal static class PackageKit
{
    // A surface whose features lie where the synthetic drive runs (origin at the map's zero).
    public static SurfaceBuilder Surface(int extraTriangles = 0, int extraLines = 0, int seed = 5, float triangleMin = 4, float triangleSpread = 30)
    {
        var b = new SurfaceBuilder { OriginX = 0, OriginY = 0 };
        var river = b.Str("Synthetic River");
        var street = b.Str("Street A");
        var centre = b.Str("Centre");
        var c = b.Chunk(0, 0);
        c.Fills.Add((0, SurfaceBuilder.Tri(120, 140, 120)));        // water next to the road
        c.Fills.Add((1, SurfaceBuilder.Tri(300, 60, 100)));         // green
        c.Fills.Add((2, SurfaceBuilder.Tri(200, 200, 90)));         // built
        c.Lines.Add((0, 0, 6, river, new float[] { 0, 250, 300, 260, 600, 240 }));
        c.Lines.Add((3, 2, 0, street, new float[] { 0, 100, 600, 100 }));
        c.Lines.Add((4, 40, 60, street, new float[] { 250, 0, 250, 600 }));
        c.Lines.Add((5, 0, 100, uint.MaxValue, new float[] { 0, 400, 600, 420 }));
        c.Labels.Add((0, 200, centre, 300, 300));
        var random = new Random(seed);
        for (var i = 0; i < extraTriangles; i++)
            c.Fills.Add(((byte)(i % 3), SurfaceBuilder.Tri((float)(random.NextDouble() * 1800), (float)(random.NextDouble() * 1800), triangleMin + (float)(random.NextDouble() * triangleSpread))));
        for (var i = 0; i < extraLines; i++)
        {
            var x = (float)(random.NextDouble() * 1800);
            var y = (float)(random.NextDouble() * 1800);
            c.Lines.Add(((byte)(i % 2 == 0 ? 3 : 5), 2, 0, uint.MaxValue, new[] { x, y, x + 20, y + 5, x + 40, y + 25, x + 70, y + 30 }));
        }
        return b;
    }

    public const string Markers =
        "marker = icons/terminal.png | 30 | 300 | 120 | Central Terminal\n" +
        "marker = icons/depot.png | 54 | 180 | 300 | Depot\n" +
        "marker = icons/terminal.png | 30 | 5000 | 5000 | Far away\n";

    // The realmap package (and optionally the pause-menu one) below the session's folder; the bridge fake finds it.
    public static void Install(Session s, SurfaceBuilder? surface, bool pausePackage = false, bool realmap = true, bool decodableIcons = true, double north = 0, Action<string>? alter = null)
    {
        var map = s.MapsRoot;
        var realmapDir = Path.Combine(map, "openomsi-realmap");
        Directory.CreateDirectory(map);
        if (realmap)
        {
            using var scratch = new TempDir();
            RealmapFiles.Write(scratch, surface, markers: false, regions: true, lines: true, manifest: Manifest(north));
            CopyTree(scratch.Path, realmapDir);
            File.WriteAllText(Path.Combine(realmapDir, "markers.cfg"), Markers);
            Directory.CreateDirectory(Path.Combine(realmapDir, "icons"));
            File.WriteAllBytes(Path.Combine(realmapDir, "icons", "terminal.png"), decodableIcons ? Icon(64, 64, 200, 40, 40) : Synthetic.Png(64, 64));
            File.WriteAllBytes(Path.Combine(realmapDir, "icons", "depot.png"), decodableIcons ? Icon(128, 96, 40, 40, 200) : Synthetic.Png(128, 96));
            var length = Directory.EnumerateFiles(realmapDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            s.Context.ContentFake.Maps[Session.MapIdentity] = new ModuleContentResult(true, new RealmapPackageLocation(Session.MapIdentity, realmapDir, "FP1", 8, length), null, null);
        }
        if (pausePackage) InstallPauseMenu(map);
        alter?.Invoke(map);
    }

    public static void InstallPauseMenu(string mapFolder)
    {
        var dir = Path.Combine(mapFolder, "openomsi-pausemenu");
        Directory.CreateDirectory(Path.Combine(dir, "symbols"));
        Directory.CreateDirectory(Path.Combine(dir, "fonts"));
        File.WriteAllText(Path.Combine(dir, "manifest.cfg"), "format = 1\nname = Synthetic pause menu\nauthor = tests\n");
        File.WriteAllText(Path.Combine(dir, "identity.cfg"),
            "format = 1\ntitle = Synthetic Map\ntitle.PTB = Mapa Sintetico\nsymbol = symbols/logo.png\ncolor.accent = #24589a\ncolor.line = #5588c7\n" +
            "legend.station = Synthetic station\nlegend.station.PTB = Estacao sintetica\nlegend.ferry = Ferry\n");
        File.WriteAllBytes(Path.Combine(dir, "symbols", "logo.png"), Icon(96, 96, 30, 120, 220));
        File.WriteAllBytes(Path.Combine(dir, "symbols", "wordmark.svg"), System.Text.Encoding.UTF8.GetBytes(Synthetic.Svg()));
        File.WriteAllBytes(Path.Combine(dir, "fonts", "Test-Regular.ttf"), Synthetic.Font());
        File.WriteAllBytes(Path.Combine(dir, "fonts", "Test-Bold.ttf"), Synthetic.Font(16));
        File.WriteAllText(Path.Combine(dir, "fonts", "FONTS-LICENSE.txt"), "Synthetic fonts for tests; no licence applies.");
        File.WriteAllText(Path.Combine(dir, "fonts", "fonts.cfg"), "format = 1\nregular = Test-Regular.ttf | 400\nbold = Test-Bold.ttf | 700\n");
    }

    private static string Manifest(double north) =>
        "format = 1\nname = Synthetic Map\ngrid = 300\nnorth = " + north.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";

    public static byte[] Icon(int width, int height, byte r, byte g, byte b) =>
        PngWriter.Rgba(width, height, (x, y) => (r, g, b, (byte)((x + y) % 7 == 0 ? 0 : 255)));

    public static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
