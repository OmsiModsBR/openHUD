using System.Text.Json;
using OpenHud.Scene;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// Complete sessions against the fake bridge: Arm, GameplayReady, a synthetic drive, and what reaches the draw layer.
internal static class SessionGates
{
    public static void Register()
    {
        Gates.Run("module.session-radar-drive", RadarDrive);
        Gates.Run("module.session-throttle-and-change-detection", Throttle);
        Gates.Run("module.session-realmap-reaches-the-scene", RealmapReachesTheScene);
        Gates.Run("module.session-pause-package-fonts-and-symbol", PausePackage);
        Gates.Run("module.session-svg-symbol-is-skipped-with-a-reason", SvgSymbolSkipped);
        Gates.Run("module.session-north-rotation", NorthRotation);
        Gates.Run("module.session-corner-size-opacity-language", SettingsShapeTheScene);
        Gates.Run("module.session-reads-only-declared-operations", OnlyDeclared);
        Gates.Run("module.session-offscreen-corner-and-no-player", NoPlayer);
    }

    private static int Count(SceneDocument scene, Func<SceneCommand, bool> test) => scene.Commands.Count(test);

    private static SceneDocument Last(Session s) => Doc.Decode(s.Native.LastScene!);

    private static void RadarDrive()
    {
        using var s = new Session();
        s.Start();
        Gates.Check(s.Native.Calls.Any(c => c.Op == "visible" && c.Args.Contains("true")), "the layer is made visible at GameplayReady");
        s.Tick(60);
        Gates.Check(s.Native.SceneCount >= 5, "scenes are published while driving: " + s.Native.SceneCount);
        s.CheckAllScenesValid("drive");
        var last = Last(s);
        Gates.Check(!last.Background, "the radar is drawn over the game, not over a captured background");
        Gates.Check(Count(last, c => c is ClipCircleCommand) == 1, "the radar clips its map to the circle");
        Gates.Check(Count(last, c => c is TextCommand { Text: "N" }) >= 1, "the north mark is drawn");
        Gates.Check(Count(last, c => c is TextCommand t && t.Text.Contains(':')) >= 1, "the clock strip shows the game time");
        Gates.Check(last.Commands.OfType<PolyCommand>().Count() >= 1, "the other vehicles are drawn");
        var distinct = s.Native.Scenes.Distinct().Count();
        Gates.Check(distinct >= 4, "the scene follows the vehicle: " + distinct + " distinct scenes");
        var status = s.Status();
        Gates.Check(status["radar_state"] == "on" && status["module_state"] == "running", "status says the radar is on: " + status["radar_state"]);
        Gates.Check(status["faults"] == "0", "no fault in a clean drive: " + status["last_fault"]);
    }

    private static void Throttle()
    {
        using var s = new Session();
        s.Start();
        s.Tick(30);
        var moving = s.Native.SceneCount;
        // 30 ticks of 50 ms is 1.5 s: at most one scene per 100 ms (plus the first and the empty one).
        Gates.Check(moving <= 18, "the publish rate is capped: " + moving + " scenes in 30 ticks");
        var before = s.Native.SceneCount;
        s.Tick(40, advanceDrive: false);
        Gates.Check(s.Native.SceneCount - before <= 1, "a standing vehicle republishes nothing: " + (s.Native.SceneCount - before));
        Gates.Check(long.Parse(s.Status()["scenes_skipped"]) > 0, "skipped scenes are counted");
        // The standing scene is not rebuilt either: the zoom changes it, though.
        before = s.Native.SceneCount;
        s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "out" });
        s.Tick(3, advanceDrive: false);
        Gates.Check(s.Native.SceneCount - before >= 1, "zooming changes the scene");
    }

    private static void RealmapReachesTheScene()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface());
        s.Start();
        s.Tick(10);
        var without = Last(s).Commands.OfType<PolyCommand>().Count();
        Gates.Check(s.WaitForPackages(), "the packages are adopted");
        Gates.Check(s.WaitFor(() => { s.Tick(1); return int.Parse(s.Status()["radar_features"]) > 0; }), "the radar window arrives from the worker");
        s.Tick(40);
        s.CheckAllScenesValid("realmap");
        var with = Last(s);
        Gates.Check(with.Commands.OfType<PolyCommand>().Count() > without, "the package's features are drawn: " + with.Commands.OfType<PolyCommand>().Count() + " > " + without);
        Gates.Check(s.Context.ContentFake.Locates == 1, "the package is located once: " + s.Context.ContentFake.Locates);
        Gates.Check(s.Native.Images.Contains("mk.0") && s.Native.Images.Contains("mk.1"), "marker icons are decoded and registered: " + string.Join(",", s.Native.Images));
        var shown = s.SceneDocuments().Any(d => d.Commands.OfType<ImageCommand>().Any(i => i.Id.StartsWith("mk.")));
        Gates.Check(shown, "a marker icon is drawn near the terminal");
        var packages = s.Context.Events.Where(e => e.Name == "packages").ToList();
        Gates.Check(packages.Count == 1 && packages[0].Data!["realmap"] == "Loaded", "one packages event says the realmap loaded");
        Gates.Check(s.Status()["realmap_status"] == "Loaded" && s.Status()["pausemenu_status"] == "Absent", "status names the package states");
    }

    private static void PausePackage()
    {
        using var s = new Session(settings: v => v["language"] = "pt-BR");
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        Gates.Check(s.WaitForPackages(), "the packages are adopted");
        s.Tick(10);
        Gates.Check(s.Native.Fonts.Count == 5, "a font is registered for every role: " + s.Native.Fonts.Count);
        Gates.Check(s.Native.Fonts.All(f => Path.IsPathRooted(f.Path) && f.Path.StartsWith(s.Dir.Path, StringComparison.OrdinalIgnoreCase)), "font paths are absolute and inside the package");
        Gates.Check(s.Native.Fonts.Select(f => f.Role).OrderBy(x => x).SequenceEqual(new[] { "black", "bold", "condensed", "medium", "regular" }), "all five roles");
        Gates.Check(s.Native.Images.Contains("pkg.symbol"), "the map's symbol is registered");
        Gates.Check(s.Native.Calls.Where(c => c.Op == "image.put").All(c => JsonDocument.Parse(c.Args).RootElement.GetProperty("rgba").GetString()!.Length > 0), "pictures carry pixels");
        Gates.Check(s.Status()["pausemenu_status"] == "Loaded" && s.Status()["fonts_registered"] == "5", "status names the pause package state");
        // The measurer asks the layer once fonts exist, and answers from its cache afterwards.
        Gates.Check(s.Native.MeasureCalls > 0 && s.Status()["text_measure"] == "native", "text is measured by the draw layer");
        s.Pause.UserPauses();
        s.Tick(3);
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu is drawn");
        var menu = Last(s);
        var texts = menu.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        Gates.Check(texts.Contains("Mapa Sintetico"), "the title follows the language (PTB): " + string.Join("|", texts.Take(6)));
        Gates.Check(texts.Contains("Voltar ao jogo"), "the menu texts follow the language: " + string.Join("|", texts.Take(12)));
        Gates.Check(menu.Commands.OfType<ImageCommand>().Any(i => i.Id == "pkg.symbol"), "the header shows the package's symbol");
        Gates.Check(menu.Commands.OfType<RectCommand>().Any(r => r.Fill is not null && r.Fill.StartsWith("#24589a")), "the package's accent colours the menu");
        s.CheckAllScenesValid("pause package");
    }

    private static void SvgSymbolSkipped()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        File.WriteAllText(Path.Combine(s.MapsRoot, "openomsi-pausemenu", "identity.cfg"), "format = 1\ntitle = Svg Map\nsymbol = symbols/wordmark.svg\n");
        s.Start();
        Gates.Check(s.WaitForPackages(), "the packages are adopted");
        s.Tick(5);
        Gates.Check(!s.Native.Images.Contains("pkg.symbol"), "an SVG symbol is not registered");
        var packages = s.Context.Events.Single(e => e.Name == "packages");
        Gates.Check(packages.Data!["diagnostics"].Contains("SVG"), "the reason is logged: " + packages.Data["diagnostics"]);
        s.Pause.UserPauses();
        s.Tick(3);
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu still opens, with the generic mark");
        Gates.Check(!Last(s).Commands.OfType<ImageCommand>().Any(), "no picture is referenced that was never registered");
    }

    private static void NorthRotation()
    {
        // A package whose north is 90 degrees: the same plane is turned, vehicles and features alike.
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), north: 90);
        s.Start();
        Gates.Check(s.WaitForPackages(), "adopted");
        s.Tick(30);
        s.CheckAllScenesValid("north");
        var last = Last(s);
        Gates.Check(last.Commands.OfType<PolyCommand>().Count() > 2, "the rotated package still draws");
        Gates.Check(s.Status()["faults"] == "0", "no fault with a rotated package");
    }

    private static void SettingsShapeTheScene()
    {
        SceneDocument Scene(Action<Dictionary<string, string>> settings)
        {
            using var s = new Session(settings: settings);
            s.Start();
            s.Tick(12);
            s.CheckAllScenesValid("settings");
            return Last(s);
        }
        static (double X, double Y) RingCentre(SceneDocument d) { var c = d.Commands.OfType<ClipCircleCommand>().Single(); return (c.Cx, c.Cy); }
        static double Radius(SceneDocument d) => d.Commands.OfType<ClipCircleCommand>().Single().R;

        var bl = RingCentre(Scene(v => v["radar_corner"] = "bottom-left"));
        var tr = RingCentre(Scene(v => v["radar_corner"] = "top-right"));
        Gates.Check(bl.X < 400 && bl.Y > 600, "bottom-left corner: " + bl);
        Gates.Check(tr.X > 1500 && tr.Y < 400, "top-right corner: " + tr);
        var normal = Radius(Scene(_ => { }));
        var big = Radius(Scene(v => v["radar_size_percent"] = "150"));
        Gates.Check(big > normal * 1.4, "the size setting scales the radar: " + normal + " -> " + big);
        var scaled = Radius(Scene(v => v["ui_scale_percent"] = "150"));
        Gates.Check(scaled > normal * 1.4, "the interface scale scales the radar");

        var opaque = Scene(_ => { });
        var faint = Scene(v => v["radar_opacity_percent"] = "30");
        static int Alpha(string? colour) => colour is { Length: 9 } ? Convert.ToInt32(colour.Substring(7, 2), 16) : 255;
        var opaqueAlpha = opaque.Commands.OfType<CircleCommand>().First().Fill;
        var faintAlpha = faint.Commands.OfType<CircleCommand>().First().Fill;
        Gates.Check(Alpha(faintAlpha) < Alpha(opaqueAlpha) * 0.5, "opacity multiplies the alpha: " + opaqueAlpha + " -> " + faintAlpha);

        var near = Scene(v => v["radar_zoom"] = "near").Commands.OfType<TextCommand>().Select(t => t.Text);
        var far = Scene(v => v["radar_zoom"] = "far").Commands.OfType<TextCommand>().Select(t => t.Text);
        Gates.Check(near.Contains("200 m") && far.Contains("800 m"), "zoom near/far: " + string.Join(",", near.Where(t => t.EndsWith('m'))) + " / " + string.Join(",", far.Where(t => t.EndsWith('m'))));

        using var off = new Session(settings: v => v["radar"] = "false");
        off.Start();
        off.Tick(20);
        Gates.Check(off.Native.Scenes.All(j => Doc.Decode(j).Commands.Count == 0), "with the radar off nothing is drawn");
        Gates.Check(off.Status()["radar_state"] == "off", "status says off");
    }

    private static void OnlyDeclared()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        s.WaitForPackages();
        s.Tick(30);
        s.Pause.UserPauses();
        s.Tick(5);
        s.Input.Queue(Keys.Down(Keys.Esc), Keys.Up(Keys.Esc));
        s.Tick(6);
        Gates.Check(s.Runtime.Undeclared.Count == 0, "undeclared operations were called: " + string.Join(",", s.Runtime.Undeclared.Distinct()));
        var used = s.Runtime.PerOperation.Keys.ToList();
        foreach (var op in new[] { "time.read", "map.read", "map.tiles.list", "camera.read", "player-vehicle.read", "road-vehicles.list", "road-vehicle.read", "ui.pause.state", "ui.pause.take-over", "ui.pause.action" })
            Gates.Check(used.Contains(op), op + " is used");
    }

    private static void NoPlayer()
    {
        using var s = new Session();
        s.Start();
        s.Tick(5);
        // The player's vehicle disappears from the catalog (the user left the vehicle): nothing is drawn, nothing fails.
        var frame = s.Runtime.Frame;
        frame.Add("player-vehicle.read", OpenHud.Model.GameReply.Ok(("present", "false")));
        s.Tick(10, advanceDrive: false);
        var last = Last(s);
        Gates.Check(last.Commands.Count == 0, "no player vehicle, no radar");
        Gates.Check(s.Status()["radar_state"] == "no player vehicle", "status says why: " + s.Status()["radar_state"]);
        // Unresolved tile: positions would be wrong, so nothing is drawn either.
        s.Runtime.Frame = s.Drive.Frame(3);
        s.Runtime.Frame.Add("map.tiles.list", OpenHud.Model.GameReply.Fail("OL_E_RUNTIME_OPERATION_UNAVAILABLE"), ("limit", "256"));
        s.Tick(80, advanceDrive: false);
        Gates.Check(s.Status()["faults"] == "0", "no fault");
    }
}
