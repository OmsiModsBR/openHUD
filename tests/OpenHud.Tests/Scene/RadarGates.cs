using System.Diagnostics;
using OpenHud.Radar;
using OpenHud.Scene;
using static OpenHud.Tests.SceneTestUtil;

namespace OpenHud.Tests;

// Gates of the radar and full-map scene builders.
internal static class RadarGates
{
    private const double Cx = 1700, Cy = 880, R = 160;
    private const string GoldenName = "radar-golden-small.json";

    public static void Register()
    {
        Gates.Run("radar.small-scene-structure", SmallSceneStructure);
        Gates.Run("radar.projection-north-up", ProjectionNorthUp);
        Gates.Run("radar.projection-heading-up", ProjectionHeadingUp);
        Gates.Run("radar.zoom-steps", ZoomSteps);
        Gates.Run("radar.compass-follows-mode", CompassFollowsMode);
        Gates.Run("radar.culling-and-clipping", CullingAndClipping);
        Gates.Run("radar.vehicles-nearest-capped", VehiclesNearestCapped);
        Gates.Run("radar.markers-capped", MarkersCapped);
        Gates.Run("radar.lod-dense-map-under-cap", LodDenseMap);
        Gates.Run("radar.lod-keeps-important-layers", LodKeepsRoads);
        Gates.Run("radar.lod-point-budget", LodPointBudget);
        Gates.Run("radar.hostile-input-never-throws", HostileInput);
        Gates.Run("radar.hostile-options", HostileOptions);
        Gates.Run("radar.deterministic", Deterministic);
        Gates.Run("radar.theme-validated", ThemeValidated);
        Gates.Run("radar.info-strip", InfoStrip);
        Gates.Run("radar.golden-file", GoldenFile);
        Gates.Run("radar.performance-20000-features", Performance);
        Gates.Run("map.full-structure", FullMapStructure);
        Gates.Run("map.full-viewport", FullMapViewportMath);
        Gates.Run("map.full-line-list-overflow", FullMapLineOverflow);
        Gates.Run("map.full-dense-and-hostile", FullMapDenseAndHostile);
    }

    private static SceneDocument Small(RadarOptions? options = null, double heading = 30) =>
        new RadarSceneBuilder().Build(SmallModel(heading), options);

    private static void CheckBalanced(SceneDocument scene)
    {
        Gates.Check(SceneValidator.Validate(scene).Count == 0, "scene validates");
        Gates.Check(SceneJson.Encode(scene).Length > 0, "scene encodes");
    }

    private static void SmallSceneStructure()
    {
        var scene = Small();
        CheckBalanced(scene);
        Gates.Check(scene.Width == 1920 && scene.Height == 1080 && !scene.Background, "canvas and background flag");
        Gates.Check(Count<ClipCircleCommand>(scene) == 1 && Count<UnclipCommand>(scene) == 1, "one circle clip, balanced");
        Gates.Check(Count<PushCommand>(scene) == 0, "no transforms: geometry is projected in managed code");
        // The clip is the first thing after the backdrop and closes before the ring.
        int clipAt = scene.Commands.ToList().FindIndex(c => c is ClipCircleCommand);
        int unclipAt = scene.Commands.ToList().FindIndex(c => c is UnclipCommand);
        Gates.Check(clipAt == 1 && unclipAt > clipAt, "clip order");
        var clip = (ClipCircleCommand)scene.Commands[clipAt];
        Gates.Check(clip.Cx == Cx && clip.Cy == Cy && clip.R == R - 3, "clip circle sits inside the ring");
        var polys = Polys(scene);
        Gates.Check(polys.Count(p => p.Fill == "#2b5b8a") == 1, "one water area");
        Gates.Check(polys.Count(p => p.Fill == "#27503a") == 1, "one green area");
        Gates.Check(polys.Count(p => p.Fill == "#2d323b") == 1, "one built area");
        Gates.Check(polys.Count(p => p.Stroke == "#aab2bd") == 2, "two roads");
        Gates.Check(polys.Count(p => p.Stroke == "#8a7f6a59") == 1, "one relief line");
        Gates.Check(polys.Count(p => p.Stroke == "#3d8bfd") == 1 && polys.Count(p => p.Stroke == "#0b0e12" && p.Fill is null) == 1, "route: line plus casing");
        Gates.Check(polys.Count(p => p.Fill == "#ffb020") == 1 && polys.Count(p => p.Fill == "#c7ccd4") == 1, "bus and car dots");
        Gates.Check(polys.Count(p => p.Fill == "#ffffff") == 1, "the player's arrow");
        Gates.Check(Count<ImageCommand>(scene) == 1 && Count<CircleCommand>(scene) >= 3, "marker icon, marker dot, backdrop and ring");
        var texts = scene.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        foreach (var expected in new[] { "N", "400 m", "Central Station", "14:32", "12", "Next", "Riverside" })
            Gates.Check(texts.Contains(expected), "text '" + expected + "' present");
        Gates.Check(WithinCanvas(scene, 0), "all shapes lie within the canvas");
        Gates.Check(scene.Commands.Count < 80, "a small map stays small: " + scene.Commands.Count);
        // Layer order: areas, then relief, then roads, then route, then vehicles, then the player.
        int Idx(Func<PolyCommand, bool> f) => scene.Commands.ToList().FindIndex(c => c is PolyCommand p && f(p));
        Gates.Check(Idx(p => p.Fill == "#2b5b8a") < Idx(p => p.Stroke == "#8a7f6a59")
            && Idx(p => p.Stroke == "#8a7f6a59") < Idx(p => p.Stroke == "#aab2bd")
            && Idx(p => p.Stroke == "#aab2bd") < Idx(p => p.Stroke == "#3d8bfd")
            && Idx(p => p.Stroke == "#3d8bfd") < Idx(p => p.Fill == "#ffb020")
            && Idx(p => p.Fill == "#ffb020") < Idx(p => p.Fill == "#ffffff"), "draw order of the layers");
    }

    private static double[] RoadPoints(SceneDocument scene) =>
        Polys(scene).First(p => p.Stroke == "#aab2bd").Points;

    private static RadarModel OneRoad(double heading, params double[] pts) =>
        new(new RadarPlayer(0, 0, heading), new[] { new RadarFeature(RadarLayer.Road, pts) });

    private static void ProjectionNorthUp()
    {
        // zoom 0: 100 m across the 160 px radius = 1.6 px per meter.
        var opt = new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = 0 };
        var scene = new RadarSceneBuilder().Build(OneRoad(77, -50, 50, 50, 50), opt);
        var pts = RoadPoints(scene);
        Gates.Check(pts.SequenceEqual(new[] { Cx - 80, Cy - 80, Cx + 80, Cy - 80 }), "a road north of the player is above the center: " + string.Join(",", pts));
        var stroke = Polys(scene).First(p => p.Stroke == "#aab2bd");
        Gates.Check(stroke.StrokeWidth == 9.6 && stroke.Closed == false, "6 m road at 1.6 px/m");
        var east = new RadarSceneBuilder().Build(OneRoad(0, 30, -10, 30, 10), opt);
        Gates.Check(RoadPoints(east).SequenceEqual(new[] { Cx + 48, Cy + 16, Cx + 48, Cy - 16 }), "east is right, north is up");
    }

    private static void ProjectionHeadingUp()
    {
        var opt = new RadarOptions { Mode = RadarMode.HeadingUp, ZoomStep = 0 };
        // Facing east, a road to the north lies on the left of the screen.
        var pts = RoadPoints(new RadarSceneBuilder().Build(OneRoad(90, -10, 50, 10, 50), opt));
        Gates.Check(Math.Abs(pts[0] - (Cx - 80)) < 0.11 && Math.Abs(pts[2] - (Cx - 80)) < 0.11, "north is on the left when facing east");
        Gates.Check(Math.Abs(pts[1] - (Cy + 16)) < 0.11 && Math.Abs(pts[3] - (Cy - 16)) < 0.11, "east is up when facing east");
        // The player arrow points up in heading-up mode, regardless of heading.
        var arrow = Polys(new RadarSceneBuilder().Build(SmallModel(137), opt)).First(p => p.Fill == "#ffffff").Points;
        Gates.Check(Math.Abs(arrow[0] - Cx) < 0.11 && arrow[1] < Cy - 5, "arrow tip is straight up");
        // ...and rotates in north-up mode.
        var northUp = new RadarOptions { Mode = RadarMode.NorthUp };
        var turned = Polys(new RadarSceneBuilder().Build(SmallModel(90), northUp)).First(p => p.Fill == "#ffffff").Points;
        Gates.Check(turned[0] > Cx + 5 && Math.Abs(turned[1] - Cy) < 0.11, "heading east points the arrow right in north-up");
    }

    private static void ZoomSteps()
    {
        var model = OneRoad(0, 0, 0, 0, 100); // 100 m due north
        double Top(int zoom)
        {
            var s = new RadarSceneBuilder().Build(model, new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = zoom });
            return Cy - RoadPoints(s)[3]; // pixels from center
        }
        Gates.Check(Math.Abs(Top(0) - 144) < 0.11 || Math.Abs(Top(0) - 160) < 0.11, "zoom 0 spans the window (clipped at the edge)");
        Gates.Check(Math.Abs(Top(1) - 80) < 0.11, "zoom 1: 100 m = 80 px");
        Gates.Check(Math.Abs(Top(2) - 40) < 0.11, "zoom 2: 100 m = 40 px");
        Gates.Check(Math.Abs(Top(3) - 20) < 0.11, "zoom 3: 100 m = 20 px");
        Gates.Check(Math.Abs(Top(4) - 10) < 0.11, "zoom 4: 100 m = 10 px");
        var o = new RadarOptions { ZoomStep = 0 };
        Gates.Check(o.ZoomIn().ZoomStep == 0 && o.ZoomOut().ZoomStep == 1, "zoom steps clamp at the low end");
        var last = new RadarOptions { ZoomStep = RadarOptions.ZoomRadiiMeters.Length - 1 };
        Gates.Check(last.ZoomOut().ZoomStep == last.ZoomStep && last.ZoomIn().ZoomStep == last.ZoomStep - 1, "and at the high end");
        Gates.Check(RadarOptions.ClampZoom(-5) == 0 && RadarOptions.ClampZoom(99) == RadarOptions.ZoomRadiiMeters.Length - 1, "out-of-range steps clamp");
        var labels = new[] { 0, 1, 2, 3, 4, 5 }.Select(z => new RadarSceneBuilder().Build(RadarModel.Empty, new RadarOptions { ZoomStep = z })
            .Commands.OfType<TextCommand>().First(t => t.Text.EndsWith("m")).Text).ToArray();
        Gates.Check(labels.SequenceEqual(new[] { "100 m", "200 m", "400 m", "800 m", "1.6 km", "3.2 km" }), "scale labels: " + string.Join("|", labels));
    }

    private static void CompassFollowsMode()
    {
        // The red north triangle sits at the top in north-up and rotates against the heading otherwise.
        (double x, double y) NorthTip(RadarMode mode, double heading)
        {
            var s = new RadarSceneBuilder().Build(SmallModel(heading), new RadarOptions { Mode = mode });
            var tri = Polys(s).First(p => p.Fill == "#e5484d" && p.Points.Length == 6).Points;
            return (tri[0], tri[1]);
        }
        var up = NorthTip(RadarMode.NorthUp, 90);
        Gates.Check(Math.Abs(up.x - Cx) < 0.11 && up.y < Cy - 100, "north-up: mark at the top");
        var rot = NorthTip(RadarMode.HeadingUp, 90);
        Gates.Check(rot.x < Cx - 100 && Math.Abs(rot.y - Cy) < 0.11, "heading east: north is on the left");
        var s0 = new RadarSceneBuilder().Build(SmallModel(0), new RadarOptions { Mode = RadarMode.NorthUp });
        Gates.Check(Count<LineCommand>(s0) == 4, "four cardinal ticks");
        Gates.Check(s0.Commands.OfType<LineCommand>().Count(l => l.Color == "#e5484d") == 1, "one of them is the north tick");
    }

    private static void CullingAndClipping()
    {
        var opt = new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = 1 }; // 200 m window
        var model = new RadarModel(new RadarPlayer(0, 0, 0), new[]
        {
            new RadarFeature(RadarLayer.Road, new double[] { 5000, 5000, 5100, 5100 }),                // far away
            new RadarFeature(RadarLayer.Road, new double[] { -100, 20, 9000, 20 }),                     // leaves the window
            new RadarFeature(RadarLayer.Water, new double[] { 8000, 0, 8100, 0, 8100, 100, 8000, 100 }, true), // far area
            new RadarFeature(RadarLayer.Water, new double[] { -5000, -5000, 5000, -5000, 5000, 5000, -5000, 5000 }, true), // covers it all
            new RadarFeature(RadarLayer.Road, new double[] { 1, 1, 1.2, 1.2 }),                         // sub-pixel
        });
        var scene = new RadarSceneBuilder().Build(model, opt);
        CheckBalanced(scene);
        var polys = Polys(scene);
        var roads = polys.Where(p => p.Stroke == "#aab2bd").ToList();
        Gates.Check(roads.Count == 1, "only the road that reaches the window survives: " + roads.Count);
        Gates.Check(roads[0].Points.Max() <= Cx + R + 0.11 && roads[0].Points.Length == 4, "the road is cut at the window edge");
        var water = polys.Where(p => p.Fill == "#2b5b8a").ToList();
        Gates.Check(water.Count == 1, "the far pond is culled, the big lake kept");
        Gates.Check(water[0].Points.Length <= 16 && water[0].Points.Where((v, i) => i % 2 == 0).All(x => x >= Cx - R - 0.11 && x <= Cx + R + 0.11),
            "the big lake is clipped to the window rectangle");
    }

    private static void VehiclesNearestCapped()
    {
        var vs = new List<RadarVehicle>();
        for (int i = 0; i < 500; i++) vs.Add(new RadarVehicle(i % 2 == 0 ? i * 0.5 : -i * 0.5, 10, i, RadarVehicleKind.Car)); // 0..250 m out
        var scene = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), null, null, null, vs),
            new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = 2, MaxVehicles = 10 });
        var dots = Polys(scene).Where(p => p.Fill == "#c7ccd4").ToList();
        Gates.Check(dots.Count == 10, "capped to 10: " + dots.Count);
        // the ten nearest are the ones with the smallest x offsets (indices 0..9 -> |x| <= 4.5 m)
        Gates.Check(dots.All(d => Math.Abs(d.Points[0] - Cx) <= 4.5 * 0.4 + 12), "the nearest vehicles were kept");
        var scene0 = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), null, null, null, vs), new RadarOptions { MaxVehicles = 0 });
        Gates.Check(Polys(scene0).Count(p => p.Fill == "#c7ccd4") == 0, "cap 0 draws none");
        // vehicles outside the circle are dropped even when under the cap
        var outside = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), null, null, null,
            new[] { new RadarVehicle(900, 0, 0), new RadarVehicle(0, 60, 0) }), new RadarOptions { Mode = RadarMode.NorthUp });
        Gates.Check(Polys(outside).Count(p => p.Fill == "#8791a0") == 1, "only the vehicle inside the window is drawn");
    }

    private static void MarkersCapped()
    {
        var model = DenseModel(0, 150, 0, 300);
        var scene = new RadarSceneBuilder().Build(model, new RadarOptions { ZoomStep = 2, MaxMarkers = 20 });
        int shown = Count<ImageCommand>(scene) + scene.Commands.OfType<CircleCommand>().Count(c => c.R == 4);
        Gates.Check(shown == 20, "markers capped to 20: " + shown);
        CheckBalanced(scene);
    }

    private static void LodDenseMap()
    {
        var model = DenseModel(20000, 2500, 800, 800);
        foreach (var zoom in new[] { 1, 3, 5 })
        {
            var opt = new RadarOptions { ZoomStep = zoom };
            var scene = new RadarSceneBuilder().Build(model, opt);
            CheckBalanced(scene);
            Gates.Check(scene.Commands.Count <= opt.MaxCommands, "zoom " + zoom + ": " + scene.Commands.Count + " <= " + opt.MaxCommands);
            Gates.Check(scene.Commands.Count < SceneLimits.MaxCommands, "under the protocol cap");
            Gates.Check(Count<ClipCircleCommand>(scene) == 1 && Count<UnclipCommand>(scene) == 1, "clip stays balanced");
            Gates.Check(WithinCanvas(scene, 0.5), "everything drawn is on the canvas");
            Gates.Check(SceneJson.EncodeUtf8(scene).Length < SceneLimits.MaxJsonBytes, "JSON under 1 MiB");
        }
        // A tiny budget is honored too.
        var tight = new RadarSceneBuilder().Build(model, new RadarOptions { ZoomStep = 5, MaxCommands = 200, MaxVehicles = 5, MaxMarkers = 5 });
        Gates.Check(tight.Commands.Count <= 200 + 0, "tight budget respected: " + tight.Commands.Count);
    }

    private static void LodKeepsRoads()
    {
        // Far more relief lines than the budget, plus a few roads of equal screen size: the roads must survive.
        var list = new List<RadarFeature>();
        for (int i = 0; i < 3000; i++) list.Add(new RadarFeature(RadarLayer.Relief, new double[] { -150, -150 + i * 0.1, 150, -150 + i * 0.1 }));
        for (int i = 0; i < 40; i++) list.Add(new RadarFeature(RadarLayer.Road, new double[] { -140, -100 + i * 5, 140, -100 + i * 5 }));
        var opt = new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = 2, MaxCommands = 400, MaxVehicles = 0, MaxMarkers = 0 };
        var scene = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), list), opt);
        var polys = Polys(scene);
        Gates.Check(polys.Count(p => p.Stroke == "#aab2bd") == 40, "all roads kept: " + polys.Count(p => p.Stroke == "#aab2bd"));
        Gates.Check(polys.Count(p => p.Stroke == "#8a7f6a59") > 0 && scene.Commands.Count <= 400, "the remaining budget goes to relief, within the cap");
    }

    private static void LodPointBudget()
    {
        var list = new List<RadarFeature>();
        for (int f = 0; f < 60; f++)
        {
            var pts = new double[2 * 2000];
            for (int i = 0; i < 2000; i++) { pts[2 * i] = -150 + (i % 2 == 0 ? 0 : 300); pts[2 * i + 1] = -150 + (f * 5) + (i * 0.1); }
            list.Add(new RadarFeature(RadarLayer.Road, pts));
        }
        var opt = new RadarOptions { Mode = RadarMode.NorthUp, ZoomStep = 2, MaxPoints = 5000, MaxVehicles = 0, MaxMarkers = 0 };
        var scene = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), list), opt);
        int points = Polys(scene).Where(p => p.Stroke == "#aab2bd").Sum(p => p.Points.Length / 2);
        Gates.Check(points <= 5000 && points > 0, "map points within budget: " + points);
        CheckBalanced(scene);
    }

    private static void HostileInput()
    {
        double nan = double.NaN, inf = double.PositiveInfinity;
        var bad = new RadarModel(
            new RadarPlayer(nan, inf, nan),
            new RadarFeature?[]
            {
                null,
                new RadarFeature(RadarLayer.Road, new double[] { nan, nan, 1, 2 }),
                new RadarFeature(RadarLayer.Road, new double[] { 1e300, 1e300, -1e300, 5 }),
                new RadarFeature(RadarLayer.Road, new double[] { inf, 0, 0, inf, -inf, 0 }),
                new RadarFeature(RadarLayer.Water, Array.Empty<double>(), true),
                new RadarFeature(RadarLayer.Green, null!, true),
                new RadarFeature(RadarLayer.Built, new double[] { 0, 0, 1e308, 0, 0, 1e308 }, true),
                new RadarFeature(RadarLayer.Water, new double[] { -1e8, -1e8, 1e8, -1e8, 1e8, 1e8, -1e8, 1e8 }, true),
                new RadarFeature(RadarLayer.Road, new double[] { 1, 2, 3 }),                                // odd length
                new RadarFeature(RadarLayer.Road, new double[] { 0, 0, 50, 50 }, false, "not-a-color", nan),
                new RadarFeature(RadarLayer.Relief, new double[] { 0, 0, 0, 0, 0, 0 }),                      // degenerate
                new RadarFeature((RadarLayer)99, new double[] { 0, 0, 50, 50 }),
            }!,
            new RadarRegion?[] { null, new RadarRegion("", 0, 0), new RadarRegion(new string('x', 100000), 5, 5), new RadarRegion("n", nan, 0) }!,
            new RadarMarker?[] { null, new RadarMarker(nan, 0), new RadarMarker(1e300, 1e300, "i"), new RadarMarker(1, 1, "i", null, "bad") }!,
            new RadarVehicle?[] { null, new RadarVehicle(nan, nan, nan), new RadarVehicle(5, 5, inf, (RadarVehicleKind)42), new RadarVehicle(-inf, 0, 0) }!,
            new double[] { nan, 0, 1, 1, inf, inf, 5, 5, 1e300, 1e300, 7, 7 },
            new RadarInfo(new string('s', 100000), new string('c', 5000), new string('l', 4000)),
            new RadarLine?[] { null, new RadarLine("x", "bad") }!);
        foreach (var mode in new[] { RadarMode.NorthUp, RadarMode.HeadingUp })
        {
            var scene = new RadarSceneBuilder().Build(bad, new RadarOptions { Mode = mode });
            CheckBalanced(scene);
            Gates.Check(SceneJson.EncodeUtf8(scene).Length < 20000, "hostile strings did not inflate the scene");
        }
        // an all-null model body
        var minimal = new RadarModel(new RadarPlayer(0, 0, 0), null, null, null, null, null, null, null);
        CheckBalanced(new RadarSceneBuilder().Build(minimal));
        CheckBalanced(new RadarSceneBuilder().Build(null));
        CheckBalanced(new RadarSceneBuilder().Build(new RadarModel(null!)));
        // empty layers
        CheckBalanced(new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), Array.Empty<RadarFeature>(), Array.Empty<RadarRegion>(),
            Array.Empty<RadarMarker>(), Array.Empty<RadarVehicle>(), Array.Empty<double>(), new RadarInfo(), Array.Empty<RadarLine>())));
        // huge but finite player position
        CheckBalanced(new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(1e12, -1e12, 720), SmallModel().Features)));
        // the full map takes the same hostile model
        var full = new FullMapSceneBuilder().Build(bad, new FullMapViewport(nan, inf, nan));
        CheckBalanced(full);
    }

    private static void HostileOptions()
    {
        var opts = new[]
        {
            new RadarOptions { CanvasWidth = -5, CanvasHeight = 0 },
            new RadarOptions { CanvasWidth = int.MaxValue, CanvasHeight = int.MaxValue },
            new RadarOptions { Radius = double.NaN, CenterX = double.PositiveInfinity, CenterY = double.NaN },
            new RadarOptions { Radius = 1e12 },
            new RadarOptions { Radius = -3 },
            new RadarOptions { ZoomStep = int.MinValue },
            new RadarOptions { MaxCommands = int.MaxValue, MaxPoints = int.MaxValue, MaxVehicles = int.MaxValue, MaxMarkers = int.MaxValue },
            new RadarOptions { MaxCommands = -1, MaxPoints = -1, MaxVehicles = -1, MaxMarkers = -1 },
            new RadarOptions { Mode = (RadarMode)9 },
        };
        foreach (var o in opts)
        {
            var scene = new RadarSceneBuilder().Build(DenseModel(3000, 800, 100, 100), o);
            CheckBalanced(scene);
            Gates.Check(scene.Commands.Count <= SceneLimits.MaxCommands, "under the cap");
        }
    }

    private static void Deterministic()
    {
        var model = DenseModel(5000, 1500, 100, 100);
        string a = SceneJson.Encode(new RadarSceneBuilder().Build(model));
        string b = SceneJson.Encode(new RadarSceneBuilder().Build(DenseModel(5000, 1500, 100, 100)));
        Gates.Check(a == b, "same inputs, byte-identical scene");
        var builder = new RadarSceneBuilder();
        Gates.Check(SceneJson.Encode(builder.Build(model)) == SceneJson.Encode(builder.Build(model)), "a builder instance has no hidden state between builds");
        string full1 = SceneJson.Encode(new FullMapSceneBuilder().Build(model, FullMapViewport.Fit(model, 1400, 900)));
        string full2 = SceneJson.Encode(new FullMapSceneBuilder().Build(model, FullMapViewport.Fit(model, 1400, 900)));
        Gates.Check(full1 == full2, "the full map is deterministic too");
    }

    private static void ThemeValidated()
    {
        Throws<ArgumentException>(() => new RadarSceneBuilder(new RadarTheme { Accent = "blue" }), "bad accent");
        Throws<ArgumentException>(() => new FullMapSceneBuilder(new RadarTheme { Water = "#12" }), "bad water");
        var themed = new RadarSceneBuilder(new RadarTheme { Accent = "#ff0000", Route = "#ff0000", Water = "#0000ff" }).Build(SmallModel());
        Gates.Check(Polys(themed).Any(p => p.Stroke == "#ff0000") && Polys(themed).Any(p => p.Fill == "#0000ff"), "theme tokens reach the scene");
        var texted = new RadarSceneBuilder(null, new RadarTexts { North = "O", NextStop = "Proxima" }).Build(SmallModel());
        Gates.Check(texted.Commands.OfType<TextCommand>().Any(t => t.Text == "O") && texted.Commands.OfType<TextCommand>().Any(t => t.Text == "Proxima"), "texts come from the table");
        // A feature color override wins over the theme; an invalid override falls back to it.
        var model = new RadarModel(new RadarPlayer(0, 0, 0), new[]
        {
            new RadarFeature(RadarLayer.Road, new double[] { -50, 0, 50, 0 }, false, "#123456"),
            new RadarFeature(RadarLayer.Road, new double[] { -50, 20, 50, 20 }, false, "#12"),
        });
        var s = new RadarSceneBuilder().Build(model, new RadarOptions { Mode = RadarMode.NorthUp });
        Gates.Check(Polys(s).Any(p => p.Stroke == "#123456") && Polys(s).Any(p => p.Stroke == "#aab2bd"), "feature colors: override and fallback");
    }

    private static void InfoStrip()
    {
        var none = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0)));
        Gates.Check(!none.Commands.OfType<RectCommand>().Any(), "no info, no strip");
        var withInfo = new RadarSceneBuilder().Build(SmallModel());
        var strip = withInfo.Commands.OfType<RectCommand>().First(r => r.Fill == "#14171ce6");
        Gates.Check(strip.Y > Cy + R && strip.X == Cx - R && strip.W == 2 * R, "strip under the circle, as wide as the ring");
        // Not enough room below: the strip moves above.
        var low = new RadarSceneBuilder().Build(SmallModel(), new RadarOptions { CenterY = 1080 - R });
        var above = low.Commands.OfType<RectCommand>().First(r => r.Fill == "#14171ce6");
        Gates.Check(above.Y + above.H < 1080 - 2 * R + 1, "strip above when the canvas has no room below");
        // A narrow ring still produces valid text boxes.
        var narrow = new RadarSceneBuilder().Build(SmallModel(), new RadarOptions { Radius = 30 });
        CheckBalanced(narrow);
        foreach (var t in narrow.Commands.OfType<TextCommand>().Where(t => t.MaxWidth.HasValue)) Gates.Check(t.MaxWidth > 0, "text boxes keep a positive width");
    }

    private static string Pretty(string json) => json.Replace(",{\"t\"", ",\n {\"t\"").Replace("\"cmds\":[{", "\"cmds\":[\n {") + "\n";

    private static void GoldenFile()
    {
        string actual = Pretty(SceneJson.Encode(new RadarSceneBuilder().Build(SmallModel(), new RadarOptions())));
        string path = Path.Combine(VectorsDirectory(), GoldenName);
        if (Environment.GetEnvironmentVariable("OPENHUD_UPDATE_VECTORS") == "1") File.WriteAllText(path, actual, new System.Text.UTF8Encoding(false));
        string expected = ReadVector(GoldenName);
        Gates.Check(actual == expected, GoldenName + " differs from the radar builder output (regenerate with OPENHUD_UPDATE_VECTORS=1 and review)");
        Gates.Check(SceneValidator.Validate(SceneDecoder.Decode(expected)).Count == 0, "the golden file is a valid scene");
    }

    private static void Performance()
    {
        var model = DenseModel(20000, 3000, 300, 300);
        var builder = new RadarSceneBuilder();
        builder.Build(model); // warm up the JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        SceneDocument scene = builder.Build(model);
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Gates.Check(sw.ElapsedMilliseconds < 1500, "20 000 features built in " + sw.ElapsedMilliseconds + " ms (bound 1500)");
        Gates.Check(allocated < 160L * 1024 * 1024, "allocated " + allocated / 1024 / 1024 + " MiB (bound 160)");
        sw.Restart();
        string json = SceneJson.Encode(scene);
        sw.Stop();
        Gates.Check(sw.ElapsedMilliseconds < 500, "encoding took " + sw.ElapsedMilliseconds + " ms (bound 500)");
        Gates.Check(json.Length < SceneLimits.MaxJsonBytes && scene.Commands.Count <= SceneLimits.MaxCommands, "result within protocol limits");
    }

    // ---- full map ---------------------------------------------------------------------------

    private static void FullMapStructure()
    {
        var model = SmallModel();
        var vp = new FullMapViewport(0, 0, 0.5);
        var scene = new FullMapSceneBuilder().Build(model, vp);
        CheckBalanced(scene);
        Gates.Check(scene.Background, "over the captured pause background by default");
        Gates.Check(scene.Commands[0] is RectCommand { Fill: "#000000a6", W: 1920, H: 1080 }, "dim layer first");
        Gates.Check(Count<ClipRectCommand>(scene) == 1 && Count<UnclipCommand>(scene) == 1, "map clipped to its frame");
        var (mx, my, mw, mh) = FullMapSceneBuilder.MapArea(new FullMapOptions());
        var clip = scene.Commands.OfType<ClipRectCommand>().Single();
        Gates.Check(clip.X == mx + 1 && clip.Y == my + 1 && clip.W == mw - 2 && clip.H == mh - 2, "clip is the map frame");
        Gates.Check(mx + mw < 1920 - 360 && mh == 1080 - 48, "map area leaves room for the side panel");
        var polys = Polys(scene);
        Gates.Check(polys.Count(p => p.Fill == "#2b5b8a") == 1 && polys.Count(p => p.Stroke == "#aab2bd") >= 2, "layers drawn");
        Gates.Check(polys.Count(p => p.Fill == "#ffffff") == 1, "the player's arrow");
        var texts = scene.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        foreach (var expected in new[] { "Legend", "Lines", "Water", "Roads", "Relief", "Route", "N", "12", "34", "Riverside" })
            Gates.Check(texts.Contains(expected), "text '" + expected + "' present");
        Gates.Check(texts.Any(t => t.EndsWith(" m")), "scale bar label");
        // The active line row has a highlight, the other does not.
        Gates.Check(scene.Commands.OfType<RectCommand>().Count(r => r.Fill == "#3d8bfd38") == 1, "active line highlighted");
        Gates.Check(WithinCanvas(scene, 0.5), "everything on the canvas");
        // opaque variant
        var opaque = new FullMapSceneBuilder().Build(model, vp, new FullMapOptions { OverBackground = false });
        Gates.Check(!opaque.Background && opaque.Commands[0] is RectCommand { Fill: "#14171c" }, "opaque backdrop when not over the pause background");
        // the map is north-up: a road north of the player is above the map center
        var north = new FullMapSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 90), new[] { new RadarFeature(RadarLayer.Road, new double[] { -20, 100, 20, 100 }) }), new FullMapViewport(0, 0, 1));
        var road = Polys(north).First(p => p.Stroke == "#aab2bd").Points;
        Gates.Check(Math.Abs(road[1] - (my + mh / 2 - 100)) < 0.11 && Math.Abs(road[0] - (mx + mw / 2 - 20)) < 0.11, "north-up projection on the map");
    }

    private static void FullMapViewportMath()
    {
        var vp = new FullMapViewport(100, 200, 2);
        var panned = vp.Pan(50, 30); // dragging right/down moves the map right/down: the center goes west/north... y flips
        Gates.Check(panned.CenterX == 0 && panned.CenterY == 260 && panned.MetersPerPixel == 2, "pan converts pixels to meters (y is flipped)");
        Gates.Check(vp.Zoom(2).MetersPerPixel == 1 && vp.Zoom(0.5).MetersPerPixel == 4, "zoom factor");
        Gates.Check(vp.Zoom(1e9).MetersPerPixel == FullMapViewport.MinMetersPerPixel && vp.Zoom(1e-9).MetersPerPixel == FullMapViewport.MaxMetersPerPixel, "zoom clamps");
        Gates.Check(vp.Zoom(double.NaN).MetersPerPixel == 2 && vp.Zoom(-3).MetersPerPixel == 2, "bad zoom factors are ignored");
        var fit = FullMapViewport.Fit(SmallModel(), 1400, 900);
        Gates.Check(Math.Abs(fit.CenterX - 0) < 100 && fit.MetersPerPixel > 0.1 && fit.MetersPerPixel < 3, "fit frames the data");
        var empty = FullMapViewport.Fit(RadarModel.Empty, 1400, 900);
        Gates.Check(double.IsFinite(empty.MetersPerPixel) && empty.MetersPerPixel >= FullMapViewport.MinMetersPerPixel, "fit on an empty model is sane");
        var hostile = FullMapViewport.Fit(new RadarModel(new RadarPlayer(double.NaN, 0, 0), new[] { new RadarFeature(RadarLayer.Road, new double[] { double.NaN, 1, 1e300, 5 }) }), 0, 0);
        Gates.Check(double.IsFinite(hostile.MetersPerPixel) && double.IsFinite(hostile.CenterX), "fit ignores garbage coordinates");
        // zooming in on the full map brings a far road into view, zooming out shrinks it
        var model = new RadarModel(new RadarPlayer(0, 0, 0), new[] { new RadarFeature(RadarLayer.Road, new double[] { 0, 0, 0, 600 }) });
        double Length(FullMapViewport v)
        {
            var p = Polys(new FullMapSceneBuilder().Build(model, v)).First(q => q.Stroke == "#aab2bd").Points;
            return Math.Abs(p[3] - p[1]);
        }
        Gates.Check(Math.Abs(Length(new FullMapViewport(0, 300, 1)) - 600) < 0.2 && Math.Abs(Length(new FullMapViewport(0, 300, 2)) - 300) < 0.2, "scale follows meters per pixel");
    }

    private static void FullMapLineOverflow()
    {
        var lines = Enumerable.Range(0, 200).Select(i => new RadarLine("L" + i, "#3d8bfd", i == 3)).ToList();
        var model = new RadarModel(new RadarPlayer(0, 0, 0), null, null, null, null, null, null, lines);
        var scene = new FullMapSceneBuilder().Build(model, new FullMapViewport(0, 0, 2));
        CheckBalanced(scene);
        var names = scene.Commands.OfType<TextCommand>().Select(t => t.Text).Where(t => t.Length > 1 && t[0] == 'L' && char.IsDigit(t[1])).ToList();
        Gates.Check(names.Count > 10 && names.Count < 200, "the list is cut to fit the panel: " + names.Count);
        var more = scene.Commands.OfType<TextCommand>().Single(t => t.Text.StartsWith("+"));
        Gates.Check(more.Text == "+" + (200 - names.Count) + " more", "overflow row: " + more.Text);
        var few = new RadarModel(new RadarPlayer(0, 0, 0), null, null, null, null, null, null, lines.Take(5).ToList());
        Gates.Check(!new FullMapSceneBuilder().Build(few, new FullMapViewport(0, 0, 2)).Commands.OfType<TextCommand>().Any(t => t.Text.StartsWith("+")), "no overflow row when all fit");
        var broken = new FullMapSceneBuilder(null, new RadarTexts { More = "{ {0} {" }).Build(model, new FullMapViewport(0, 0, 2));
        CheckBalanced(broken);
        var noSide = new FullMapSceneBuilder().Build(model, new FullMapViewport(0, 0, 2), new FullMapOptions { SidePanelWidth = 0 });
        Gates.Check(!noSide.Commands.OfType<TextCommand>().Any(t => t.Text == "Legend"), "no side panel when its width is 0");
    }

    private static void FullMapDenseAndHostile()
    {
        var model = DenseModel(20000, 3000, 1500, 1500);
        var opt = new FullMapOptions();
        var scene = new FullMapSceneBuilder().Build(model, FullMapViewport.Fit(model, 1400, 900), opt);
        CheckBalanced(scene);
        Gates.Check(scene.Commands.Count <= opt.MaxCommands, "dense full map within its budget: " + scene.Commands.Count);
        Gates.Check(SceneJson.EncodeUtf8(scene).Length < SceneLimits.MaxJsonBytes, "under 1 MiB");
        foreach (var o in new[]
        {
            new FullMapOptions { CanvasWidth = 1, CanvasHeight = 1, Margin = double.NaN, SidePanelWidth = double.PositiveInfinity },
            new FullMapOptions { Margin = -50, SidePanelWidth = -5, MaxCommands = -1, MaxPoints = -1, MaxVehicles = -1, MaxMarkers = -1 },
            new FullMapOptions { MaxCommands = int.MaxValue, MaxPoints = int.MaxValue, MaxVehicles = int.MaxValue, MaxMarkers = int.MaxValue },
        })
            CheckBalanced(new FullMapSceneBuilder().Build(model, new FullMapViewport(0, 0, 1), o));
        foreach (var vp in new[] { new FullMapViewport(double.NaN, double.NaN, double.NaN), new FullMapViewport(1e300, -1e300, 0), new FullMapViewport(0, 0, 1e300), new FullMapViewport(0, 0, -5) })
            CheckBalanced(new FullMapSceneBuilder().Build(SmallModel(), vp));
        CheckBalanced(new FullMapSceneBuilder().Build(null, new FullMapViewport(0, 0, 1)));
    }
}
