using OpenHud.Pause;
using OpenHud.Scene;

namespace OpenHud.Tests.ModuleTests;

// The first release's open items, end to end against the fake bridge: the package slots that were read but not drawn,
// the realmap manifest's colors and player marker, the speed readout and the `units` setting, the footer's credit.
internal static class GapSessionGates
{
    public static void Register()
    {
        Gates.Run("module.session-pause-package-slots-are-drawn", PackageSlots);
        Gates.Run("module.session-pause-package-slot-failures-fall-back", SlotFailures);
        Gates.Run("module.session-realmap-manifest-colors-and-player-marker", ManifestColors);
        Gates.Run("module.session-brt-lines-take-the-brt-color", BrtLines);
        Gates.Run("module.session-speed-and-units", SpeedAndUnits);
        Gates.Run("module.session-footer-shows-versions-and-credit", FooterCredit);
        Gates.Run("module.session-incremental-queries-and-scene-reuse", IncrementalWork);
    }

    private static SceneDocument Last(Session s) => Doc.Decode(s.Native.LastScene!);

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">{0}</svg>";

    private static string Shape(string inner) => string.Format(Svg, inner);

    private static void Package(Session s, string identity, params (string File, string Text)[] files)
    {
        var dir = Path.Combine(s.MapsRoot, "openomsi-pausemenu");
        File.WriteAllText(Path.Combine(dir, "identity.cfg"), identity);
        foreach (var (file, text) in files) File.WriteAllText(Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar)), text);
    }

    private static void ToTab(Session s, int tabs)
    {
        for (var i = 0; i < tabs; i++)
        {
            s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab));
            s.Tick(2);
        }
    }

    // ---------------------------------------------------------------- the package slots

    private static void PackageSlots()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true, decodableIcons: false);
        Package(s,
            "format = 1\ntitle = Slots\nsymbol = symbols/logo.png\ncolor.accent = #24589a\nstop_icon = symbols/stop.svg\nmarker_icon.station = symbols/station.svg\n" +
            "marker_icon.garage = symbols/garage.png\nmode_icon.bus = symbols/bus.svg\nmode_icon.tram = symbols/tram.png\nlegend.station = Station\nlegend.garage = Garage\nlegend.stop = Stop\n",
            ("symbols/stop.svg", Shape("<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"#33ccff\"/>")),
            ("symbols/station.svg", Shape("<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"#ff00aa\"/>")),
            ("symbols/bus.svg", Shape("<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" fill=\"#00ffaa\"/>")),
            ("symbols/openomsi.svg", Shape("<rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" fill=\"#8844ff\"/>")),
            ("manifest.cfg", "format = 1\nname = Slots\ncredit = Symbols by the test suite\n"));
        var dir = Path.Combine(s.MapsRoot, "openomsi-pausemenu", "symbols");
        File.WriteAllBytes(Path.Combine(dir, "garage.png"), PackageKit.Icon(40, 20, 10, 200, 10));
        File.WriteAllBytes(Path.Combine(dir, "tram.png"), PackageKit.Icon(30, 30, 200, 100, 10));
        File.WriteAllBytes(Path.Combine(dir, "omsi2-logo.png"), PackageKit.Icon(100, 40, 250, 250, 250));
        s.Start();
        Gates.Check(s.WaitForPackages(), "adopted");
        s.Tick(40);

        var packages = s.Context.Events.Single(e => e.Name == "packages");
        Gates.Check(packages.Data!["pausemenu"] == "Loaded" && packages.Data["vector_symbols"] == "4", "four vector symbols: " + string.Join(",", packages.Data.Select(x => x.Key + "=" + x.Value)));
        Gates.Check(!packages.Data.TryGetValue("diagnostics", out var d) || !d.Contains("pausemenu Dropped"), "nothing dropped from the package");
        Gates.Check(s.Native.Images.Contains("pkg.symbol") && s.Native.Images.Count(i => i.StartsWith("pkg.")) == 4, "pictures: symbol, garage, tram, omsi2 logo: " + string.Join(",", s.Native.Images));
        s.CheckAllScenesValid("slots radar");

        // The radar: markers without a picture of their own show the stop icon (the package's pictures failed to decode).
        var radar = Last(s);
        Gates.Check(radar.Commands.OfType<CircleCommand>().Any(c => c.Fill!.StartsWith("#33ccff")), "the radar draws the stop icon for markers without a picture");

        // The menu.
        s.Pause.UserPauses();
        s.Tick(3);
        var menu = Last(s);
        var images = menu.Commands.OfType<ImageCommand>().Select(i => i.Id).ToList();
        Gates.Check(images.Contains("pkg.symbol"), "the symbol");
        Gates.Check(images.Count(i => i != "pkg.symbol") == 2, "the OMSI 2 logo and the garage legend icon: " + string.Join(",", images));
        Gates.Check(menu.Commands.OfType<RectCommand>().Any(r => r.Fill == "#8844ff"), "the openOMSI mark (an SVG)");
        Gates.Check(menu.Commands.OfType<CircleCommand>().Any(c => c.Fill == "#ff00aa"), "the station legend icon (an SVG)");
        var texts = menu.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        Gates.Check(texts.Contains("openHUD 0.1.0 · OMSI 2.3.004"), "the footer names both versions: " + string.Join("|", texts.TakeLast(6)));
        Gates.Check(texts.Contains("Symbols by the test suite"), "the footer shows the package's credit");
        Gates.Check(!menu.Commands.OfType<RectCommand>().Any(r => r.Stroke is not null && r.Fill is null && r.H <= 60 && r.Y < 400), "no generic OMSI 2 tag when the package has a logo");

        // The lines page: the bus line has an SVG icon, the tram line a PNG one.
        ToTab(s, 2);
        var lines = Last(s);
        Gates.Check(lines.Commands.OfType<RectCommand>().Count(r => r.Fill == "#00ffaa") == 1, "the bus line's icon");
        Gates.Check(lines.Commands.OfType<ImageCommand>().Count(i => i.Id != "pkg.symbol") >= 2, "the tram line's picture and the logo");
        s.CheckAllScenesValid("slots menu");
        Gates.Check(s.Status()["faults"] == "0", "no fault");
    }

    private static void SlotFailures()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        var rich = string.Concat(Enumerable.Range(0, 60).Select(i => "<rect x=\"" + i % 20 + "\" y=\"" + i / 3 % 20 + "\" width=\"1\" height=\"1\"/>"));
        Package(s,
            "format = 1\ntitle = Failures\nstop_icon = symbols/rich.svg\nmarker_icon.station = symbols/script.svg\nmode_icon.bus = symbols/cycle.png\nlegend.station = Station\n",
            ("symbols/rich.svg", Shape(rich)),
            ("symbols/script.svg", Shape("<script>x</script>")));
        File.WriteAllBytes(Path.Combine(s.MapsRoot, "openomsi-pausemenu", "symbols", "cycle.png"), new byte[] { 1, 2, 3 });
        s.Start();
        Gates.Check(s.WaitForPackages(), "adopted");
        s.Tick(10);
        var packages = s.Context.Events.Single(e => e.Name == "packages");
        var diagnostics = packages.Data!["diagnostics"];
        Gates.Check(diagnostics.Contains("rich.svg") && diagnostics.Contains("drawing commands"), "an over-budget icon says why: " + diagnostics);
        Gates.Check(diagnostics.Contains("marker_icon.station") && diagnostics.Contains("mode_icon.bus"), "so do the files the probe refused: " + diagnostics);
        Gates.Check(packages.Data["vector_symbols"] == "0", "no vector symbol");
        s.Pause.UserPauses();
        s.Tick(3);
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu still opens");
        // The generic marks: the legend keeps its swatch, the OMSI 2 slot its neutral tag.
        var menu = Last(s);
        Gates.Check(menu.Commands.OfType<RectCommand>().Any(r => r.Stroke is not null && r.Fill is null && r.H <= 60 && r.Y < 400), "the neutral tag");
        Gates.Check(Last(s).Commands.OfType<ImageCommand>().All(i => s.Native.Images.Contains(i.Id)), "every picture referenced was registered");
        s.CheckAllScenesValid("failures");
    }

    // ---------------------------------------------------------------- the realmap manifest

    private static void ManifestColors()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: false, alter: map =>
        {
            var manifest = Path.Combine(map, "openomsi-realmap", "manifest.cfg");
            File.WriteAllText(manifest, File.ReadAllText(manifest) +
                "land = #101012\nwater = #112233\ngreen = #223344\nbuilt = #334455\nrail = #aa00aa\nrail_edge = #550055\nplayer = triangle\nplayer_free = #ff00ff\nplayer_line = #00ff00\n");
        });
        s.Start();
        Gates.Check(s.WaitForPackages(), "adopted");
        s.Tick(30);
        s.CheckAllScenesValid("manifest colors");
        var radar = Last(s);
        var disc = radar.Commands.OfType<CircleCommand>().First();
        Gates.Check(disc.Fill!.StartsWith("#101012"), "land colours the radar's disc: " + disc.Fill);
        var polys = radar.Commands.OfType<PolyCommand>().ToList();
        Gates.Check(polys.Any(p => p.Closed == true && p.Fill!.StartsWith("#112233")), "water");
        Gates.Check(polys.Any(p => p.Closed == true && p.Fill!.StartsWith("#223344")), "green");
        Gates.Check(polys.Any(p => p.Closed == true && p.Fill!.StartsWith("#334455")), "built-up");
        var player = polys.Single(p => p.Closed == true && p.Stroke?.StartsWith("#0b0e12") == true && p.StrokeWidth == 2);
        Gates.Check(player.Fill!.StartsWith("#ff00ff") && player.Points.Length == 6, "the player is a triangle in the free color (player_line needs an active line)");

        // Without the keys the radar's own colors stay.
        using var plain = new Session();
        PackageKit.Install(plain, PackageKit.Surface(), pausePackage: false);
        plain.Start();
        plain.WaitForPackages();
        plain.Tick(30);
        var own = Last(plain);
        Gates.Check(own.Commands.OfType<PolyCommand>().Any(p => p.Closed == true && p.Fill!.StartsWith("#2b5b8a")), "the radar's own water without the key");
        Gates.Check(own.Commands.OfType<PolyCommand>().Single(p => p.Closed == true && p.Stroke?.StartsWith("#0b0e12") == true && p.StrokeWidth == 2).Points.Length == 8, "the chevron without the key");
    }

    private static void BrtLines()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true, alter: map =>
        {
            var manifest = Path.Combine(map, "openomsi-realmap", "manifest.cfg");
            File.WriteAllText(manifest, File.ReadAllText(manifest) + "brt_lines = 7\n");
        });
        Package(s, "format = 1\ntitle = Brt\ncolor.accent = #24589a\ncolor.brt = #1e7b34\ncolor.group.grp = #aa5500\n");
        s.Start();
        Gates.Check(s.WaitForPackages(), "adopted");
        s.Tick(10);
        s.Pause.UserPauses();
        s.Tick(3);
        ToTab(s, 2);
        var lines = Last(s);
        // Line 7 is listed as BRT: its swatch takes the BRT color; the others take their group's.
        var swatches = lines.Commands.OfType<RectCommand>().Where(r => r.W == r.H && r.W < 40 && r.Fill is "#1e7b34" or "#aa5500").Select(r => r.Fill).ToList();
        Gates.Check(swatches.Contains("#1e7b34") && swatches.Contains("#aa5500"), "BRT and group colours: " + string.Join(",", swatches));
        Gates.Check(swatches.Count(f => f == "#1e7b34") == 1, "only the listed line");
    }

    // ---------------------------------------------------------------- speed and units

    private static string[] StripTexts(SceneDocument scene) => scene.Commands.OfType<TextCommand>().Select(t => t.Text).ToArray();

    private static void SpeedAndUnits()
    {
        using var metric = new Session(d => d.SpeedMetresPerSecond = 10);
        metric.Start();
        metric.Tick(20);
        var texts = StripTexts(Last(metric));
        Gates.Check(texts.Contains("36 km/h"), "10 m/s is 36 km/h: " + string.Join("|", texts));
        Gates.Check(texts.Contains("400 m"), "the scale in metres: " + string.Join("|", texts));
        metric.CheckAllScenesValid("metric");

        using var imperial = new Session(d => d.SpeedMetresPerSecond = 10, v => v["units"] = "imperial");
        imperial.Start();
        imperial.Tick(20);
        texts = StripTexts(Last(imperial));
        Gates.Check(texts.Contains("22 mph") && !texts.Contains("36 km/h"), "imperial: " + string.Join("|", texts));
        Gates.Check(texts.Contains("0.25 mi") && !texts.Contains("400 m"), "the scale in miles: " + string.Join("|", texts));
        imperial.CheckAllScenesValid("imperial");

        // The speed follows the drive: faster, a different number; standing, zero.
        using var standing = new Session(d => d.SpeedMetresPerSecond = 0);
        standing.Start();
        standing.Tick(10);
        Gates.Check(StripTexts(Last(standing)).Contains("0 km/h"), "standing shows zero");
        metric.Status();
        var before = metric.Native.SceneCount;
        metric.Tick(1);
        Gates.Check(metric.Native.SceneCount >= before, "ticking goes on");

        // The language gives the unit its word.
        using var german = new Session(d => d.SpeedMetresPerSecond = 10, v => { v["language"] = "de"; v["units"] = "imperial"; });
        german.Start();
        german.Tick(20);
        Gates.Check(StripTexts(Last(german)).Contains("22 mph"), "mph in German");

        // The full map's scale bar follows the setting too.
        imperial.Pause.UserPauses();
        imperial.Tick(3);
        ToTab(imperial, 1);
        imperial.Tick(10);
        var map = StripTexts(Last(imperial));
        Gates.Check(map.Any(t => t.EndsWith(" ft") || t.EndsWith(" mi")) && !map.Any(t => t.EndsWith(" m") || t.EndsWith(" km")), "the map page's scale bar is imperial: " + string.Join("|", map.Where(t => t.Length < 10)));
    }

    private static void FooterCredit()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        s.WaitForPackages();
        s.Tick(5);
        s.Pause.UserPauses();
        s.Tick(3);
        var texts = StripTexts(Last(s));
        Gates.Check(texts.Contains("openHUD 0.1.0 · OMSI 2.3.004"), "versions on the footer: " + string.Join("|", texts));
        Gates.Check(!texts.Any(t => t.Contains("Credits", StringComparison.OrdinalIgnoreCase) || t.Contains("Créditos")), "the old CREDITS entry is not back");
        // No package credit: versions only.
        var footerTexts = Last(s).Commands.OfType<TextCommand>().Where(t => t.Align == "r").Select(t => t.Text).ToList();
        Gates.Check(footerTexts.Count(t => t.StartsWith("openHUD")) == 1, "one versions line");
    }

    // ---------------------------------------------------------------- the work per tick

    private static void IncrementalWork()
    {
        // A slow drive: the signature is coarse enough that most ticks skip the build, and nothing is lost in doing so.
        using var s = new Session(d => { d.SpeedMetresPerSecond = 0.1; d.AiVehicles = 0; });
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 400), pausePackage: true);
        s.Start();
        s.WaitForPackages();
        s.Tick(40);
        var published = long.Parse(s.Status()["scenes_published"]);
        var skipped = long.Parse(s.Status()["scenes_skipped"]);
        s.Tick(100);
        var publishedAfter = long.Parse(s.Status()["scenes_published"]) - published;
        var skippedAfter = long.Parse(s.Status()["scenes_skipped"]) - skipped;
        Gates.Check(skippedAfter > publishedAfter, "at walking pace most ticks reuse the scene: published " + publishedAfter + ", reused " + skippedAfter);
        s.CheckAllScenesValid("slow drive");

        // The pause menu: hovering does not rebuild the map.
        s.Pause.UserPauses();
        s.Tick(3);
        ToTab(s, 1);
        s.Tick(6);
        var scenes = long.Parse(s.Status()["scenes_published"]);
        var builds = long.Parse(s.Status()["map_builds"]);
        var view = s.Status()["view"].Split('x');
        var geometry = new MenuGeometry(new MenuViewport(int.Parse(view[0]), int.Parse(view[1]), 1, 1));
        var tabs = new[] { OpenHud.Tests.PauseTests.Ev.Center(geometry, MenuGeometry.TabRect(0)), OpenHud.Tests.PauseTests.Ev.Center(geometry, MenuGeometry.TabRect(2)) };
        for (var i = 0; i < 12; i++)
        {
            s.Input.Queue(Keys.Move(tabs[i % 2].X, tabs[i % 2].Y));
            s.Tick(1, advanceDrive: false);
        }
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu is still up");
        s.CheckAllScenesValid("hover");
        Gates.Check(long.Parse(s.Status()["scenes_published"]) - scenes >= 10, "the menu is redrawn as the pointer moves");
        Gates.Check(long.Parse(s.Status()["map_builds"]) - builds <= 1, "...but the map under it is not rebuilt: " + (long.Parse(s.Status()["map_builds"]) - builds));
    }
}
