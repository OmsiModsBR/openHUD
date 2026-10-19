using System.Collections.Immutable;
using System.Globalization;
using OpenHud.Draw;
using OpenHud.Maps;
using OpenHud.Packages.Realmap;
using OpenHud.Pause;
using OpenHud.Radar;
using OpenHud.Scene;
using OpenHud.Settings;
using static OpenHud.Tests.SceneTestUtil;

namespace OpenHud.Tests;

// The work-saving paths must not change what is drawn: culling, the scene hash, the validator, the fast number
// writer, the composed pause scene and the package palette.
internal static class ReuseGates
{
    public static void Register()
    {
        Gates.Run("radar.culled-features-leave-no-trace", CullingChangesNothing);
        Gates.Run("radar.culling-keeps-hostile-features-safe", CullingHostile);
        Gates.Run("radar.builder-scratch-is-reusable-across-builds", ScratchReuse);
        Gates.Run("scene.number-writer-matches-the-round-trip-format", NumberWriter);
        Gates.Run("scene.validator-messages-and-limits-unchanged", ValidatorMessages);
        Gates.Run("scene.encode-without-validation", EncodeLean);
        Gates.Run("scene.fragment-and-composed-json-match-the-document-encoder", ComposedJson);
        Gates.Run("module.palette-colors-reach-the-features", PaletteReachesFeatures);
    }

    // ---------------------------------------------------------------- culling

    private static void CullingChangesNothing()
    {
        var near = DenseModel(300, 350, 20, 10, seed: 3);
        // The same features plus a ring far outside every view the options below can show.
        var rng = new Random(9);
        var far = Enumerable.Range(0, 400).Select(i =>
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var d = 20000 + rng.NextDouble() * 5000;
            double cx = Math.Cos(angle) * d, cy = Math.Sin(angle) * d;
            return new RadarFeature(i % 2 == 0 ? RadarLayer.Road : RadarLayer.Green, new[] { cx, cy, cx + 100, cy + 50, cx + 120, cy - 40, cx - 30, cy - 30 }, i % 2 == 1);
        }).ToList();
        var padded = near with { Features = near.Features!.Concat(far).ToList() };

        foreach (var zoom in new[] { 0, 2, 4 })
            foreach (var mode in new[] { RadarMode.HeadingUp, RadarMode.NorthUp })
            {
                var options = new RadarOptions { ZoomStep = zoom, Mode = mode };
                var a = SceneJson.Encode(new RadarSceneBuilder().Build(near, options));
                var b = SceneJson.Encode(new RadarSceneBuilder().Build(padded, options));
                Gates.Check(a == b, "distant features change nothing at zoom " + zoom + " " + mode);
            }
        foreach (var mpp in new[] { 0.5, 2, 20 })
        {
            var view = new FullMapViewport(10, -20, mpp);
            Gates.Check(SceneJson.Encode(new FullMapSceneBuilder().Build(near, view)) == SceneJson.Encode(new FullMapSceneBuilder().Build(padded, view)), "distant features change nothing on the full map at " + mpp + " m/px");
        }

        // A feature that straddles the window edge is drawn, one just outside is not.
        var inside = new RadarFeature(RadarLayer.Road, new double[] { 390, -5, 410, 5 }, false, "#123456", 6);
        var outside = new RadarFeature(RadarLayer.Road, new double[] { 1500, -5, 1600, 5 }, false, "#654321", 6);
        var scene = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), new[] { inside, outside }), new RadarOptions { ZoomStep = 2, Mode = RadarMode.NorthUp });
        Gates.Check(Polys(scene).Any(p => p.Stroke == "#123456") && !Polys(scene).Any(p => p.Stroke == "#654321"), "the edge feature is clipped in, the far one is not drawn");
    }

    private static void CullingHostile()
    {
        var nan = double.NaN;
        var features = new[]
        {
            new RadarFeature(RadarLayer.Road, new[] { nan, 0, 10, 10, 20, 20 }, false, "#111111", 6),
            new RadarFeature(RadarLayer.Road, new[] { 1e300, 0, 10, 10 }, false, "#222222", 6),
            new RadarFeature(RadarLayer.Road, new[] { double.PositiveInfinity, 0, 10, 10, 30, 30 }, false, "#333333", 6),
            new RadarFeature(RadarLayer.Green, new[] { 0.0, 0, 50, 0, 50, 50, nan, nan }, true),
            new RadarFeature(RadarLayer.Road, new double[] { 5 }, false),
            new RadarFeature(RadarLayer.Road, Array.Empty<double>(), false),
            new RadarFeature(RadarLayer.Road, null!, false),
            null!,
        };
        var scene = new RadarSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0), features), new RadarOptions { ZoomStep = 1, Mode = RadarMode.NorthUp });
        Gates.Check(SceneValidator.Validate(scene).Count == 0, "valid");
        Gates.Check(Polys(scene).Any(p => p.Stroke == "#111111"), "the readable part of a feature with one bad point is still drawn");
        var bounds = FeatureBounds.Of(new[] { nan, 0, 1, 1 });
        Gates.Check(!bounds.Valid && bounds.Intersects(-1, -1, 1, 1) && bounds.Intersects(1e6, 1e6, 2e6, 2e6), "a box that cannot be trusted is never used to skip");
        var good = FeatureBounds.Of(new double[] { 1, 2, 5, -3, 4, 8 });
        Gates.Check(good.Valid && good.MinX == 1 && good.MaxX == 5 && good.MinY == -3 && good.MaxY == 8, "box of the points");
        Gates.Check(good.Intersects(5, 8, 9, 9) && !good.Intersects(5.01, 0, 9, 9) && !good.Intersects(0, 8.01, 9, 9), "touching counts, missing does not");
        Gates.Check(!FeatureBounds.Of(Array.Empty<double>()).Valid, "no points, no box");
    }

    private static void ScratchReuse()
    {
        // The per-thread scratch lists and candidate objects carry nothing from one build to the next.
        var a = DenseModel(800, 300, 10, 10, seed: 1);
        var b = DenseModel(1500, 600, 30, 5, seed: 2);
        var first = SceneJson.Encode(new RadarSceneBuilder().Build(a));
        for (var i = 0; i < 5; i++)
        {
            SceneJson.Encode(new RadarSceneBuilder().Build(b));
            SceneJson.Encode(new FullMapSceneBuilder().Build(b, FullMapViewport.Fit(b, 1200, 800)));
            Gates.Check(SceneJson.Encode(new RadarSceneBuilder().Build(a)) == first, "the same model builds the same scene after other builds, round " + i);
        }
        // Several threads at once get their own scratch.
        var results = new string[8];
        Parallel.For(0, results.Length, i => results[i] = SceneJson.Encode(new RadarSceneBuilder().Build(i % 2 == 0 ? a : b)));
        Gates.Check(results.Where((_, i) => i % 2 == 0).All(r => r == first), "concurrent builds do not share scratch");
        Gates.Check(results.Where((_, i) => i % 2 == 1).Distinct().Count() == 1, "...either");
    }

    // ---------------------------------------------------------------- the writer and the validator

    private static void NumberWriter()
    {
        var rng = new Random(5);
        var values = new List<double> { 0, -0.0, 1, -1, 0.1, -0.1, 0.5, 12.3, 100.0, 1234567.8, -999.9, 1e12, 123456789012.3, 1e13, 1e15, 0.05, 0.001, 1.0 / 3, Math.PI, 1e-7, -1e-7, 1e300, double.Epsilon };
        for (var i = 0; i < 20000; i++)
        {
            values.Add(Math.Round((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(0, 9)), 1));       // what the builders produce
            values.Add(Math.Round((rng.NextDouble() - 0.5) * 5000, 2));                               // two decimals
            values.Add((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-5, 8)));                      // anything
        }
        foreach (var v in values)
        {
            var text = EncodedNumber(v);
            var expected = v == 0 ? "0" : v.ToString("R", CultureInfo.InvariantCulture);
            Gates.Check(text == expected, "number " + v.ToString("R", CultureInfo.InvariantCulture) + " written as " + text + ", expected " + expected);
            Gates.Check(double.Parse(text, CultureInfo.InvariantCulture) == v, "round trip of " + text);
        }
    }

    private static string EncodedNumber(double v)
    {
        var json = SceneJson.Encode(new SceneDocument(100, 100, false, new SceneCommand[] { new RectCommand(v, 0, 1, 1, "#ffffff") }));
        var at = json.IndexOf("\"x\":", StringComparison.Ordinal) + 4;
        return json[at..json.IndexOf(',', at)];
    }

    private static void ValidatorMessages()
    {
        string One(SceneCommand c) => string.Join("|", SceneValidator.Validate(new SceneDocument(100, 100, false, new[] { c })));
        Gates.Check(One(new RectCommand(double.NaN, 0, 1, 1, "#fff")).Contains("cmd 0: x is not finite") && One(new RectCommand(double.NaN, 0, 1, 1, "#fff")).Contains("fill is not a color"), "rect messages");
        Gates.Check(One(new RectCommand(0, 0, -1, 1)) == "cmd 0: w is negative", "negative width: " + One(new RectCommand(0, 0, -1, 1)));
        Gates.Check(One(new LineCommand(0, 0, 1, 1, "#12", 1)) == "cmd 0: color is not a color", "line color");
        Gates.Check(One(new PolyCommand(new double[] { 1, 2, 3 })) == "cmd 0: pts must hold x,y pairs", "poly pairs");
        Gates.Check(One(new PolyCommand(new[] { 1, double.NaN })) == "cmd 0: pts is not finite", "poly finite");
        Gates.Check(One(new TextCommand(0, 0, "x", 0, "serif")).Contains("size must be positive") && One(new TextCommand(0, 0, "x", 5, "serif")) == "cmd 0: font role unknown", "text");
        Gates.Check(One(new UnclipCommand()) == "cmd 0: unclip without clip", "unclip");
        Gates.Check(One(new PopCommand()) == "cmd 0: pop without push", "pop");
        Gates.Check(One(new ImageCommand("a", 0, 0, 1, 1, 2.0)) == "cmd 0: op outside 0..1", "image opacity");
        var many = Enumerable.Range(0, SceneLimits.MaxClipDepth + 1).Select(_ => (SceneCommand)new ClipRectCommand(0, 0, 1, 1)).Concat(Enumerable.Repeat<SceneCommand>(new UnclipCommand(), SceneLimits.MaxClipDepth + 1)).ToArray();
        Gates.Check(SceneValidator.Validate(new SceneDocument(100, 100, false, many)).Any(i => i.Contains("clip depth exceeds 8")), "clip depth");
        Gates.Check(SceneValidator.Validate(new SceneDocument(100, 100, false, new SceneCommand[] { new ClipRectCommand(0, 0, 1, 1) })).Single() == "unbalanced clip/unclip", "unbalanced");
        Gates.Check(SceneValidator.Validate(new SceneDocument(0, 100, false, Array.Empty<SceneCommand>())).Single() == "canvas size out of range", "canvas");
        // At most 32 findings are kept, however bad the scene.
        var bad = Enumerable.Range(0, 500).Select(_ => (SceneCommand)new RectCommand(double.NaN, double.NaN, -1, -1, "x", "y")).ToArray();
        Gates.Check(SceneValidator.Validate(new SceneDocument(100, 100, false, bad)).Count == 32, "findings are capped");
    }

    private static void EncodeLean()
    {
        var scene = new RadarSceneBuilder().Build(SmallModel());
        Gates.Check(SceneJson.Encode(scene, validate: false) == SceneJson.Encode(scene), "the same text with or without the check");
        var bad = new SceneDocument(100, 100, false, new SceneCommand[] { new RectCommand(double.NaN, 0, 1, 1) });
        Throws<SceneException>(() => SceneJson.Encode(bad), "the check still refuses a bad scene");
        var tooMany = new SceneDocument(100, 100, false, Enumerable.Repeat<SceneCommand>(new PopCommand(), SceneLimits.MaxCommands + 1).ToArray());
        Throws<SceneException>(() => SceneJson.Encode(tooMany, validate: false), "the command cap holds even unchecked");
        // The reusable buffer carries nothing over, also after a failed encode.
        var after = SceneJson.Encode(scene);
        Gates.Check(after == SceneJson.Encode(scene, validate: false), "stable after a refusal");
    }

    // ---------------------------------------------------------------- the composed pause scene

    private static void ComposedJson()
    {
        var menu = new PauseMenu();
        menu.SetLines(new[] { new LineEntry("1", "#ff0000", "One"), new LineEntry("2", "#00ff00", "Two") });
        menu.Handle(new PauseInputEvent(PauseInputKind.KeyDown, 0x09, 0, 0, PauseMouseButton.None, 0, 0));
        var scene = menu.BuildScene();
        var area = scene.Region("map")!.Rect;
        var sub = new FullMapSceneBuilder().Build(SmallModel(), new FullMapViewport(0, 0, 2), new FullMapOptions { CanvasWidth = (int)area.W, CanvasHeight = (int)area.H, Margin = 0, SidePanelWidth = 0, OverBackground = false });
        var fragment = SceneJson.EncodeFragment(sub.Commands);

        var withSub = SceneComposer.ComposeJson(scene, sub, fragment, area);
        Gates.Check(SceneTestUtil.JsonEqual(withSub, SceneJson.Encode(SceneComposer.Compose(scene, sub, area))), "with a map, the same document the old path made");
        var without = SceneComposer.ComposeJson(scene, null, null, default);
        Gates.Check(SceneTestUtil.JsonEqual(without, SceneJson.Encode(SceneComposer.Compose(scene, null, default))), "without a map");
        Gates.Check(withSub.StartsWith("{\"v\":1,\"w\":1920,\"h\":1080,\"background\":true,\"cmds\":[") && withSub.EndsWith("]}"), "the document frame");
        Gates.Check(SceneValidator.Validate(OpenHud.Tests.ModuleTests.Doc.Decode(withSub)).Count == 0, "valid");

        // An empty fragment (a map with no commands) still makes valid JSON.
        var empty = new SceneDocument(10, 10, false, Array.Empty<SceneCommand>());
        Gates.Check(SceneValidator.Validate(OpenHud.Tests.ModuleTests.Doc.Decode(SceneComposer.ComposeJson(scene, empty, "", area))).Count == 0, "empty map");

        // Limits: too many commands, nesting too deep, JSON too long: all refused as before.
        var big = new SceneDocument(10, 10, false, Enumerable.Repeat<SceneCommand>(new CircleCommand(1, 1, 1, "#ffffff"), SceneLimits.MaxCommands).ToArray());
        Throws<SceneException>(() => SceneComposer.ComposeJson(scene, big, SceneJson.EncodeFragment(big.Commands), area), "too many commands");
        var deep = new SceneDocument(10, 10, false, Enumerable.Repeat<SceneCommand>(new ClipRectCommand(0, 0, 1, 1), SceneLimits.MaxClipDepth).Concat(Enumerable.Repeat<SceneCommand>(new UnclipCommand(), SceneLimits.MaxClipDepth)).ToArray());
        Throws<SceneException>(() => SceneComposer.ComposeJson(scene, deep, SceneJson.EncodeFragment(deep.Commands), area), "nesting adds up with the frame's clip");
        var fat = new string('x', SceneLimits.MaxJsonBytes);
        Throws<SceneException>(() => SceneComposer.ComposeJson(scene, empty, fat, area), "too long");
        Gates.Check(SceneComposer.Depth(deep) == (SceneLimits.MaxClipDepth, 0), "depth of a command list");

        // The fragment is the commands of a document, comma-separated, as the full encoder writes them.
        var full = SceneJson.Encode(sub);
        Gates.Check(full.Contains(fragment) && SceneJson.EncodeFragment(Array.Empty<SceneCommand>()) == "", "fragment text");
    }

    // ---------------------------------------------------------------- the package palette

    private static void PaletteReachesFeatures()
    {
        var manifest = new RealmapManifest("n", 300, null, 0, Alignment.Unspecified, 0,
            new Dictionary<string, OpenHud.Packages.HudColor>
            {
                ["land"] = new(1, 2, 3), ["water"] = new(4, 5, 6), ["green"] = new(7, 8, 9), ["built"] = new(10, 11, 12), ["grass"] = new(13, 14, 15), ["forest"] = new(16, 17, 18),
                ["river"] = new(19, 20, 21), ["rail"] = new(22, 23, 24), ["rail_edge"] = new(25, 26, 27), ["border"] = new(28, 29, 30), ["contour"] = new(31, 32, 33),
            }.ToImmutableDictionary(), ImmutableDictionary<string, OpenHud.Packages.HudColor>.Empty,
            new PlayerMarkerStyle(true, new OpenHud.Packages.HudColor(255, 0, 255), null, null, ImmutableArray<string>.Empty),
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
        var palette = MapPalette.From(manifest);
        Gates.Check(palette.Land == "#010203" && palette.Water == "#040506" && palette.Green == "#070809" && palette.Built == "#0a0b0c", "land, water, green, built");
        Gates.Check(palette.Fill(FillClass.Grass) == "#0d0e0f" && palette.Fill(FillClass.Forest) == "#101112" && palette.Fill(FillClass.Sport) is null && palette.Fill(FillClass.Water) is null, "per-class fills only where the manifest says");
        Gates.Check(palette.River == "#131415" && palette.Rail == "#161718" && palette.RailEdge == "#191a1b", "river and rail");
        Gates.Check(palette.Border == "#1c1d1e40" && palette.Contour == "#1f202159", "border and contour take the radar's own alpha when the manifest gives none");
        Gates.Check(MapPalette.From(null) == MapPalette.None && MapPalette.None.Water is null && MapPalette.None.Contour is null, "no manifest, no colors");

        var theme = Look.Theme(null, palette);
        Gates.Check(theme.Backdrop == "#010203" && theme.Water == "#040506" && theme.Green == "#070809" && theme.Built == "#0a0b0c" && theme.Relief == "#1f202159", "the radar theme");
        Gates.Check(theme.Accent == new RadarTheme().Accent && theme.Road == new RadarTheme().Road, "everything else stays");
        theme.Validate();

        // The converter paints by class.
        var view = new RealmapView(
            new[] { new AreaFeature(FillClass.Grass, new float[] { 0, 0, 10, 0, 0, 10 }), new AreaFeature(FillClass.Water, new float[] { 0, 0, 10, 0, 0, 10 }) },
            new[] { new LineFeature(LineClass.Rail, 0, 0, null, new float[] { 0, 0, 50, 50 }), new LineFeature(LineClass.River, 0, 12, null, new float[] { 0, 0, 50, 5 }), new LineFeature(LineClass.Border, 0, 0, null, new float[] { 0, 0, 5, 50 }) },
            Array.Empty<LineFeature>(), Array.Empty<LabelFeature>(), Array.Empty<MarkerFeature>(), Array.Empty<RegionFeature>(), false, 0);
        var set = RealmapConverter.Convert(view, new MapFrame(0), 1, new MapRect(0, 0, 100, 100), DetailLevel.Close, _ => null, true, palette: palette);
        Gates.Check(set.Features.Any(f => f.Layer == RadarLayer.Green && f.Color == "#0d0e0f") && set.Features.Any(f => f.Layer == RadarLayer.Water && f.Closed && f.Color is null), "areas: the grass color, the water layer's");
        Gates.Check(set.Features.Any(f => f.Layer == RadarLayer.Road && f.Color == "#161718" && f.WidthMeters == 3), "the rail");
        var edge = set.Features.Where(f => f.Color == "#191a1b").ToList();
        Gates.Check(edge.Count == 1 && edge[0].WidthMeters > 3, "a wider edge line under the rail");
        Gates.Check(set.Features.ToList().FindIndex(f => f.Color == "#191a1b") < set.Features.ToList().FindIndex(f => f.Color == "#161718"), "...drawn first");
        Gates.Check(set.Features.Any(f => f.Layer == RadarLayer.Water && !f.Closed && f.Color == "#131415"), "the river");
        Gates.Check(set.Features.Any(f => f.Layer == RadarLayer.Relief && f.Color == "#1c1d1e40"), "the border");
        var plain = RealmapConverter.Convert(view, new MapFrame(0), 2, new MapRect(0, 0, 100, 100), DetailLevel.Close, _ => null, true);
        Gates.Check(plain.Features.Any(f => f.Color == "#7d8590") && plain.Features.Any(f => f.Color == "#c7cdd640") && plain.Features.All(f => f.Color != "#191a1b"), "without a palette the radar's own colors");
    }
}
