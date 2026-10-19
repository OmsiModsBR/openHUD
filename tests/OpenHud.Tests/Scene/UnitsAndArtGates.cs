using OpenHud.Art;
using OpenHud.Pause;
using OpenHud.Radar;
using OpenHud.Scene;
using OpenHud.Settings;
using OpenHud.Tests.PauseTests;
using static OpenHud.Tests.SceneTestUtil;

namespace OpenHud.Tests;

// The pieces the first release left out: the speed readout and the `units` setting, the player marker style, the stop
// icon on the map, and the pause menu's marks, footer, legend icons and mode icons.
internal static class UnitsAndArtGates
{
    private const double Cx = 1700, Cy = 880, R = 160;

    public static void Register()
    {
        Gates.Run("units.speed-formats-by-system", SpeedFormats);
        Gates.Run("units.speed-edge-cases", SpeedEdges);
        Gates.Run("units.distance-labels-by-system", DistanceLabels);
        Gates.Run("units.scale-steps", ScaleSteps);
        Gates.Run("units.radar-scale-label-follows-the-setting", RadarScaleLabel);
        Gates.Run("units.full-map-scale-bar-follows-the-setting", FullMapScaleBar);
        Gates.Run("radar.strip-shows-speed-and-clock", StripSpeed);
        Gates.Run("radar.player-marker-style", PlayerStyle);
        Gates.Run("radar.stop-icon-replaces-the-dot", StopIcon);
        Gates.Run("pause.footer-versions-and-credit", Footer);
        Gates.Run("pause.header-marks", Marks);
        Gates.Run("pause.legend-and-mode-icons", LegendAndModeIcons);
        Gates.Run("pause.vector-glyphs-keep-the-scene-valid", ScenesStayValid);
    }

    private static readonly RadarTexts English = new();

    // ---------------------------------------------------------------- units

    private static void SpeedFormats()
    {
        Expect(Format(10, UnitSystem.Metric), "36 km/h", "10 m/s");
        Expect(Format(10, UnitSystem.Imperial), "22 mph", "10 m/s in mph");
        Expect(Format(0, UnitSystem.Metric), "0 km/h", "standing");
        Expect(Format(0.1, UnitSystem.Metric), "0 km/h", "0.36 km/h rounds to 0");
        Expect(Format(27.78, UnitSystem.Metric), "100 km/h", "100 km/h");
        Expect(Format(44.704, UnitSystem.Imperial), "100 mph", "100 mph is 44.704 m/s");
        Expect(Format(-5, UnitSystem.Metric), "18 km/h", "reversing shows the magnitude");
        // The unit label follows the language table.
        var pt = new RadarTexts { UnitKmh = "km/h", UnitMph = "mi/h" };
        Expect(UnitFormat.Speed(10, UnitSystem.Imperial, pt)!, "22 mi/h", "label from the texts");
        Gates.Check(UnitFormat.SpeedValue(10, UnitSystem.Metric) == 36 && UnitFormat.SpeedValue(10, UnitSystem.Imperial) == 22, "the cache key is the whole number shown");
    }

    private static string Format(double mps, UnitSystem units) => UnitFormat.Speed(mps, units, English)!;

    private static void Expect(string actual, string expected, string what) => Gates.Check(actual == expected, what + ": expected '" + expected + "', got '" + actual + "'");

    private static void SpeedEdges()
    {
        Gates.Check(UnitFormat.Speed(double.NaN, UnitSystem.Metric, English) is null && UnitFormat.Speed(double.PositiveInfinity, UnitSystem.Imperial, English) is null, "not a number, no text");
        Expect(Format(1e9, UnitSystem.Metric), "9999 km/h", "an absurd speed is capped to four digits");
        Gates.Check(UnitFormat.SpeedValue(double.NaN, UnitSystem.Metric) == 0, "NaN is zero as a key");
        Gates.Check(UnitFormat.SpeedValue(-1e300, UnitSystem.Imperial) == 9999, "overflow is capped");
        Gates.Check(OpenHud.Model.FrameSource.GroundSpeedToMetresPerSecond == 1.0, "the INFERRED unit factor is one named constant (metres per second)");
    }

    private static void DistanceLabels()
    {
        Expect(UnitFormat.Distance(100, UnitSystem.Metric, English), "100 m", "metric metres");
        Expect(UnitFormat.Distance(1600, UnitSystem.Metric, English), "1.6 km", "metric kilometres");
        Expect(UnitFormat.Distance(3200, UnitSystem.Metric, English), "3.2 km", "3.2 km");
        // The radar's zoom radii in feet and miles.
        var expected = new[] { "330 ft", "660 ft", "0.25 mi", "0.5 mi", "1 mi", "2 mi" };
        for (var i = 0; i < expected.Length; i++)
            Expect(UnitFormat.Distance(RadarOptions.ZoomRadiiMeters[i], UnitSystem.Imperial, English), expected[i], "zoom step " + i + " in imperial");
        Expect(UnitFormat.Distance(3, UnitSystem.Imperial, English), "10 ft", "three metres are ten feet");
        Expect(UnitFormat.Distance(0.1, UnitSystem.Imperial, English), "1 ft", "never zero feet");
        Expect(UnitFormat.Distance(32187, UnitSystem.Imperial, English), "20 mi", "whole miles beyond four");
        Expect(UnitFormat.Distance(1000, UnitSystem.Imperial, English), "0.5 mi", "1000 m is 0.62 mi, shown in quarters");
    }

    private static void ScaleSteps()
    {
        foreach (var units in new[] { UnitSystem.Metric, UnitSystem.Imperial })
        {
            var steps = UnitFormat.ScaleSteps(units, English);
            Gates.Check(steps.Count >= 12, units + ": enough steps");
            for (var i = 1; i < steps.Count; i++) Gates.Check(steps[i].Metres > steps[i - 1].Metres, units + ": ascending at " + i);
            Gates.Check(steps.All(s => s.Label.Length > 0 && s.Label.Length < 12), units + ": short labels");
        }
        var imperial = UnitFormat.ScaleSteps(UnitSystem.Imperial, English);
        Gates.Check(imperial.Any(s => s.Label == "500 ft") && imperial.Any(s => s.Label == "1 mi") && imperial.Any(s => s.Label == "0.25 mi"), "imperial steps read in feet and miles");
        Gates.Check(imperial.Any(s => Math.Abs(s.Metres - 1609.344) < 1e-6), "a mile is 1609.344 m");
        var metric = UnitFormat.ScaleSteps(UnitSystem.Metric, English);
        Gates.Check(metric.Any(s => s.Label == "500 m") && metric.Any(s => s.Label == "2 km"), "metric steps");
    }

    private static void RadarScaleLabel()
    {
        string Label(UnitSystem units, int zoom) =>
            new RadarSceneBuilder().Build(RadarModel.Empty, new RadarOptions { ZoomStep = zoom, Units = units }).Commands.OfType<TextCommand>().First(t => t.Size == 11 && t.Y > Cy + R - 30).Text;
        Expect(Label(UnitSystem.Metric, 2), "400 m", "metric label");
        Expect(Label(UnitSystem.Imperial, 2), "0.25 mi", "imperial label");
        Expect(Label(UnitSystem.Imperial, 0), "330 ft", "imperial near zoom");
        var german = new RadarTexts { UnitFt = "Fuß" };
        Gates.Check(new RadarSceneBuilder(null, german).Build(RadarModel.Empty, new RadarOptions { ZoomStep = 0, Units = UnitSystem.Imperial }).Commands.OfType<TextCommand>().Any(t => t.Text == "330 Fuß"), "the unit word follows the language");
    }

    private static void FullMapScaleBar()
    {
        string Label(UnitSystem units, double mpp) =>
            new FullMapSceneBuilder().Build(RadarModel.Empty, new FullMapViewport(0, 0, mpp), new FullMapOptions { Units = units }).Commands.OfType<TextCommand>()
                .First(t => t.Size == 12 && (t.Text.EndsWith(" m") || t.Text.EndsWith(" km") || t.Text.EndsWith(" ft") || t.Text.EndsWith(" mi"))).Text;
        Expect(Label(UnitSystem.Metric, 3), "200 m", "metric bar at 3 m/px (500 m would be 167 px)");
        var imperial = Label(UnitSystem.Imperial, 3);
        Gates.Check(imperial.EndsWith(" ft") || imperial.EndsWith(" mi"), "imperial bar is in feet or miles: " + imperial);
        Gates.Check(Label(UnitSystem.Imperial, 10).EndsWith(" mi"), "far out the bar is in miles");
        Gates.Check(Label(UnitSystem.Imperial, 0.1).EndsWith(" ft"), "far in the bar is in feet");
        // Whatever the scale the bar is at most 160 px long.
        foreach (var mpp in new[] { 0.1, 0.5, 1, 3, 10, 60, 200 })
            foreach (var units in new[] { UnitSystem.Metric, UnitSystem.Imperial })
            {
                var scene = new FullMapSceneBuilder().Build(RadarModel.Empty, new FullMapViewport(0, 0, mpp), new FullMapOptions { Units = units });
                var bar = scene.Commands.OfType<LineCommand>().First(l => l.Y1 == l.Y2 && l.StrokeWidth == 2 && l.Color == new RadarTheme().Text);
                Gates.Check(bar.X2 - bar.X1 <= 160.5 && bar.X2 - bar.X1 > 0, units + " bar length at " + mpp + " m/px: " + (bar.X2 - bar.X1));
            }
    }

    // ---------------------------------------------------------------- strip, player, stop icon

    private static RadarModel WithInfo(RadarInfo info, RadarPlayer? player = null, IReadOnlyList<RadarMarker>? markers = null) =>
        new(player ?? new RadarPlayer(0, 0, 0), null, null, markers, null, null, info);

    private static void StripSpeed()
    {
        var scene = new RadarSceneBuilder().Build(WithInfo(new RadarInfo(null, "14:32", null, "47 km/h")));
        var texts = scene.Commands.OfType<TextCommand>().ToList();
        var speed = texts.Single(t => t.Text == "47 km/h");
        var clock = texts.Single(t => t.Text == "14:32");
        Gates.Check(speed.Align == "l" && clock.Align == "r" && speed.X < clock.X, "speed on the left of the strip, clock on the right");
        Gates.Check(Math.Abs(speed.Y - clock.Y) < 0.01 && speed.Y > Cy + R, "same line, under the ring");
        var measurer = new ApproximateTextMeasurer();
        Gates.Check(speed.X + measurer.Measure("47 km/h", 15, "bold") < clock.X - measurer.Measure("14:32", 15, "bold"), "they do not overlap");
        // Speed alone still makes a strip.
        var alone = new RadarSceneBuilder().Build(WithInfo(new RadarInfo(null, null, null, "0 km/h")));
        Gates.Check(alone.Commands.OfType<RectCommand>().Any(r => r.Fill == "#14171ce6") && alone.Commands.OfType<TextCommand>().Any(t => t.Text == "0 km/h"), "speed alone");
        // With a next stop (when a source exists) the stop takes what remains, after the speed.
        var all = new RadarSceneBuilder().Build(WithInfo(new RadarInfo("Central", "14:32", "12", "47 km/h")));
        var stopText = all.Commands.OfType<TextCommand>().Single(t => t.Text == "Central");
        Gates.Check(stopText.X > all.Commands.OfType<TextCommand>().Single(t => t.Text == "47 km/h").X, "the next stop comes after the speed");
        // A hostile speed text is clipped.
        var hostile = new RadarSceneBuilder().Build(WithInfo(new RadarInfo(null, null, null, new string('9', 5000))));
        Gates.Check(hostile.Commands.OfType<TextCommand>().All(t => t.Text.Length <= 48) && SceneValidator.Validate(hostile).Count == 0, "bounded");
        // Narrow ring: the speed gives way instead of overlapping the clock.
        var narrow = new RadarSceneBuilder().Build(WithInfo(new RadarInfo(null, "14:32", null, "123456789 km/h")), options: new RadarOptions { Radius = 40 });
        Gates.Check(SceneValidator.Validate(narrow).Count == 0, "narrow ring stays valid");
    }

    private static void PlayerStyle()
    {
        List<PolyCommand> Player(RadarPlayer player) => Polys(new RadarSceneBuilder().Build(new RadarModel(player))).Where(p => p.Closed == true && p.StrokeWidth == 2 && p.Stroke == "#0b0e12").ToList();
        var normal = Player(new RadarPlayer(0, 0, 0)).Single();
        Gates.Check(normal.Fill == "#ffffff" && normal.Points.Length == 8, "the default arrow is a chevron with a notch");
        var colored = Player(new RadarPlayer(0, 0, 0, "#ff00ff")).Single();
        Gates.Check(colored.Fill == "#ff00ff" && colored.Points.Length == 8, "the manifest's free color");
        var triangle = Player(new RadarPlayer(0, 0, 0, null, true)).Single();
        Gates.Check(triangle.Points.Length == 6 && triangle.Fill == "#ffffff", "a plain triangle");
        Gates.Check(Math.Abs(triangle.Points[0] - normal.Points[0]) < 1e-9 && Math.Abs(triangle.Points[1] - normal.Points[1]) < 1e-9, "the tip stays where it was");
        var bad = Player(new RadarPlayer(0, 0, 0, "magenta")).Single();
        Gates.Check(bad.Fill == "#ffffff", "a value that is not a scene color falls back to the theme");
        // The same in the full map.
        var full = new FullMapSceneBuilder().Build(new RadarModel(new RadarPlayer(0, 0, 0, "#ff00ff", true)), new FullMapViewport(0, 0, 2));
        Gates.Check(Polys(full).Any(p => p.Fill == "#ff00ff" && p.Points.Length == 6), "the full map draws it too");
    }

    private static void StopIcon()
    {
        var marker = new[] { new RadarMarker(40, 40), new RadarMarker(-40, 40, "icon"), new RadarMarker(60, -60, null, null, "#ff0000") };
        var plain = new RadarSceneBuilder().Build(WithInfo(new RadarInfo(), null, marker));
        var dots = plain.Commands.OfType<CircleCommand>().Count(c => c.R == 4);
        Gates.Check(dots == 2, "two dots (the third marker has a picture): " + dots);

        var art = SvgConverter.Convert("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><circle cx=\"10\" cy=\"10\" r=\"8\" fill=\"#33ccff\"/><rect x=\"8\" y=\"4\" width=\"4\" height=\"12\" fill=\"#ffffff\"/></svg>", SvgLimits.Icon);
        Gates.Check(art.Ok, "stop art converts: " + art.Reason);
        var withArt = new RadarSceneBuilder(null, null, null, new RadarArt { StopIcon = Glyph.Vector(art.Art!) }).Build(WithInfo(new RadarInfo(), null, marker));
        Gates.Check(withArt.Commands.OfType<CircleCommand>().Count(c => c.Fill == "#33ccff") == 2, "the stop icon stands in for each dot");
        Gates.Check(withArt.Commands.OfType<CircleCommand>().Count(c => c.R == 4) == 0, "no dot is left");
        Gates.Check(withArt.Commands.OfType<ImageCommand>().Count() == 1, "the marker with a picture keeps it");
        // A picture stop icon is an image at the marker.
        var picture = new RadarSceneBuilder(null, null, null, new RadarArt { StopIcon = Glyph.Picture("pkg.stop", 32, 32) }).Build(WithInfo(new RadarInfo(), null, marker));
        Gates.Check(picture.Commands.OfType<ImageCommand>().Count(i => i.Id == "pkg.stop") == 2 && picture.Commands.OfType<ImageCommand>().Count(i => i.Id == "icon") == 1, "a picture stop icon");
        // Many markers with art stay inside the command budget and the protocol limits.
        var many = Enumerable.Range(0, 300).Select(i => new RadarMarker(i % 20 * 5 - 50, i / 20 * 5 - 30)).ToList();
        var busy = new RadarSceneBuilder(null, null, null, new RadarArt { StopIcon = Glyph.Vector(art.Art!) }).Build(WithInfo(new RadarInfo(), null, many), new RadarOptions { MaxMarkers = 48 });
        Gates.Check(SceneValidator.Validate(busy).Count == 0, "valid with many art markers");
        Gates.Check(busy.Commands.OfType<CircleCommand>().Count(c => c.Fill == "#33ccff") <= 24, "art is limited to half the marker cap");
        Gates.Check(busy.Commands.OfType<CircleCommand>().Count(c => c.R == 4) > 0, "the rest are dots");
        // And the full map.
        var fullMap = new FullMapSceneBuilder(null, null, null, new RadarArt { StopIcon = Glyph.Vector(art.Art!) }).Build(WithInfo(new RadarInfo(), null, many), new FullMapViewport(0, 0, 0.5));
        Gates.Check(SceneValidator.Validate(fullMap).Count == 0 && fullMap.Commands.OfType<CircleCommand>().Any(c => c.Fill == "#33ccff"), "the full map draws the stop icon");
    }

    // ---------------------------------------------------------------- pause menu

    private static IEnumerable<TextDraw> Texts(MenuScene scene) => scene.Commands.OfType<TextDraw>();

    private static void Footer()
    {
        var plain = new PauseMenu();
        Gates.Check(Texts(plain.BuildScene()).Count(t => t.Size < 13 * 1.1 && t.Align == TextAlign.Right) == 0, "no versions, no credit, no right-hand text");
        var hint = Texts(plain.BuildScene()).Single(t => t.Role == TextRole.Condensed && t.Align == TextAlign.Left && t.Y > 500);
        Gates.Check(hint.MaxWidth > 700, "the hint takes the whole footer when there is nothing on the right: " + hint.MaxWidth);

        var menu = new PauseMenu();
        menu.SetFooterVersions("openHUD 0.1.0 · OMSI 2.3.004");
        var versions = Texts(menu.BuildScene()).Single(t => t.Text.StartsWith("openHUD"));
        Gates.Check(versions.Align == TextAlign.Right && versions.Text == "openHUD 0.1.0 · OMSI 2.3.004" && versions.Y > 500, "versions at the right of the footer");
        Gates.Check(Texts(menu.BuildScene()).Single(t => t.Role == TextRole.Condensed && t.Align == TextAlign.Left && t.Y > 500).MaxWidth < hint.MaxWidth, "the hint gives way to them");

        menu.SetIdentity(new PauseIdentity { Credit = "Symbols (c) Example Studio" });
        var scene = menu.BuildScene();
        var credit = Texts(scene).Single(t => t.Text == "Symbols (c) Example Studio");
        var version = Texts(scene).Single(t => t.Text.StartsWith("openHUD"));
        Gates.Check(credit.Y > version.Y && credit.Align == TextAlign.Right && credit.X == version.X, "the credit is under the versions, aligned with them");
        Gates.Check(credit.Y < 1080 * 0.98 && credit.Y + credit.Size / 2 < MenuGeometryBottom(menu), "inside the panel");

        var creditOnly = new PauseMenu(null, new PauseIdentity { Credit = "Only a credit" });
        var only = Texts(creditOnly.BuildScene()).Single(t => t.Text == "Only a credit");
        Gates.Check(only.Align == TextAlign.Right && only.Y > 500, "a credit alone is centered in the footer line");
        // The map page has its own hint and keeps the footer text.
        menu.Handle(Ev.KeyDown(Ev.Tab));
        Gates.Check(Texts(menu.BuildScene()).Any(t => t.Text == "Symbols (c) Example Studio"), "also on the map page");
        // Blank text is nothing.
        menu.SetFooterVersions("   ");
        Gates.Check(menu.FooterVersions is null, "blank versions are none");
        // Long credit is bounded by its box.
        menu.SetIdentity(new PauseIdentity { Credit = new string('x', 500) });
        Gates.Check(Texts(menu.BuildScene()).Single(t => t.Text.StartsWith("xxx")).MaxWidth > 0, "bounded box");
        // Language: the footer text is whatever the integrator passes.
        Gates.Check(MenuSceneJson.Serialize(menu.BuildScene()).Length > 0, "serializes");
    }

    private static double MenuGeometryBottom(PauseMenu menu) { var g = menu.Geometry; var p = g.ToCanvas(MenuGeometry.Panel); return p.Y + p.H; }

    private static Glyph Vector(string inner) => Glyph.Vector(SvgConverter.Convert("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">" + inner + "</svg>", SvgLimits.Symbol).Art!);

    private static void Marks()
    {
        var generic = new PauseMenu().BuildScene();
        // The generic mark: an outlined tag with a wheel, in the header's right, and no brand text.
        var tag = generic.Commands.OfType<RectDraw>().First(r => r.Stroke is not null && r.Fill is null && r.H <= 40);
        var omsi = new MenuGeometry(MenuViewport.Default).ToCanvas(MenuGeometry.Omsi2Mark);
        Gates.Check(tag.X >= omsi.X && tag.X + tag.W <= omsi.X + omsi.W + 0.01 && tag.Y >= omsi.Y && tag.Y + tag.H <= omsi.Y + omsi.H + 0.01, "the generic tag lies in the mark's slot");
        Gates.Check(!generic.Commands.OfType<RawDraw>().Any(), "nothing vector without a package");

        var identity = new PauseIdentity
        {
            Omsi2Mark = Glyph.Picture("pkg.2", 200, 80), OpenOmsiMark = Vector("<rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" fill=\"#8844ff\"/>"),
            Symbol = Glyph.Picture("pkg.symbol", 100, 50),
        };
        var scene = new PauseMenu(null, identity).BuildScene();
        var images = scene.Commands.OfType<ImageDraw>().ToList();
        var geometry = new MenuGeometry(MenuViewport.Default);
        var symbolSlot = geometry.ToCanvas(MenuGeometry.Symbol);
        var symbol = images.Single(i => i.Id == "pkg.symbol");
        Gates.Check(Math.Abs(symbol.W - symbolSlot.W) < 0.01 && Math.Abs(symbol.H - symbolSlot.H / 2) < 0.01 && Math.Abs(symbol.Y - (symbolSlot.Y + symbolSlot.H / 4)) < 0.01, "a wide symbol keeps its aspect: " + symbol);
        var logo = images.Single(i => i.Id == "pkg.2");
        Gates.Check(Math.Abs(logo.H - omsi.H) < 0.01 && Math.Abs(logo.W - omsi.H * 2.5) < 0.01, "the OMSI 2 logo fits its slot: " + logo);
        Gates.Check(!scene.Commands.OfType<RectDraw>().Any(r => r.Stroke is not null && r.Fill is null && r.H <= 40), "the package's logo replaces the generic tag");
        var open = geometry.ToCanvas(MenuGeometry.OpenOmsiMark);
        var rect = scene.Commands.OfType<RawDraw>().Select(r => r.Command).OfType<RectCommand>().Single(r => r.Fill == "#8844ff");
        Gates.Check(rect.X >= open.X && rect.X + rect.W <= open.X + open.W && rect.Y >= open.Y && rect.Y + rect.H <= open.Y + open.H, "the openOMSI mark lies in its slot");
        Gates.Check(Math.Abs(rect.W - rect.H) < 0.01, "a square mark stays square in a wide slot");
        // The title is bounded so it cannot run under the marks.
        var title = scene.Commands.OfType<TextDraw>().First(t => t.Role == TextRole.Bold && t.Size > 20);
        Gates.Check(title.X + title.MaxWidth <= omsi.X + 0.01, "the title stops before the marks: " + (title.X + title.MaxWidth) + " vs " + omsi.X);
        // A vector symbol.
        var vector = new PauseMenu(null, new PauseIdentity { Symbol = Vector("<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#ff8800\"/>") }).BuildScene();
        Gates.Check(vector.Commands.OfType<RawDraw>().Any(r => r.Command is CircleCommand c && c.Fill == "#ff8800"), "a vector symbol is drawn as primitives");
        Gates.Check(!vector.Commands.OfType<CircleDraw>().Any(c => c.Fill == "#3b82f6"), "...and the default disc is not");
        // The legacy symbol id still works.
        var legacy = new PauseMenu(null, new PauseIdentity { SymbolId = "pkg.symbol" }).BuildScene();
        var legacyImage = legacy.Commands.OfType<ImageDraw>().Single();
        Gates.Check(Math.Abs(legacyImage.W - symbolSlot.W) < 0.01 && Math.Abs(legacyImage.H - symbolSlot.H) < 0.01, "a registered id fills the square slot as before");
    }

    private static void LegendAndModeIcons()
    {
        var legendEntry = new LegendEntry(new Dictionary<string, string> { ["en"] = "Station" }, "#00ff00", Vector("<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"#ff00aa\"/>"));
        var plainEntry = new LegendEntry(new Dictionary<string, string> { ["en"] = "Garage" }, "#0000ff");
        var scene = new PauseMenu(null, new PauseIdentity { Legend = new[] { legendEntry, plainEntry } }, "en").BuildScene();
        var swatches = scene.Commands.OfType<RectDraw>().Where(r => r.Fill is "#00ff00" or "#0000ff").ToList();
        Gates.Check(swatches.Count == 1 && swatches[0].Fill == "#0000ff", "an entry with an icon has no swatch, the other keeps its swatch");
        var icon = scene.Commands.OfType<RawDraw>().Select(r => r.Command).OfType<CircleCommand>().Single(c => c.Fill == "#ff00aa");
        var panel = new MenuGeometry(MenuViewport.Default).ToCanvas(MenuGeometry.LegendPanel);
        Gates.Check(icon.Cx > panel.X && icon.Cx < panel.X + 60 && icon.Cy > panel.Y, "the icon sits where the swatch would");

        // Mode icons on the lines page: the label column shifts so that rows line up, with or without an icon.
        var lines = new[] { new LineEntry("1", "#112233", "Alpha", "bus"), new LineEntry("2", "#445566", "Beta", "tram"), new LineEntry("3", "#778899", "Gamma") };
        var without = new PauseMenu(null, null, "en");
        without.SetLines(lines);
        without.Handle(Ev.KeyDown(Ev.Tab)); without.Handle(Ev.KeyDown(Ev.Tab));
        var plainRows = without.BuildScene();
        var labelX = Texts(plainRows).Where(t => t.Text is "1" or "2" or "3").Select(t => t.X).Distinct().ToList();
        Gates.Check(labelX.Count == 1, "labels line up in the plain list");

        var identity = new PauseIdentity
        {
            ModeIcons = new Dictionary<string, Glyph> { ["bus"] = Vector("<rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" fill=\"#00ffaa\"/>") },
        };
        var withIcons = new PauseMenu(null, identity, "en");
        withIcons.SetLines(lines);
        withIcons.Handle(Ev.KeyDown(Ev.Tab)); withIcons.Handle(Ev.KeyDown(Ev.Tab));
        var rows = withIcons.BuildScene();
        var shifted = Texts(rows).Where(t => t.Text is "1" or "2" or "3").Select(t => t.X).Distinct().ToList();
        Gates.Check(shifted.Count == 1 && shifted[0] > labelX[0] + 10, "all labels move right together once a mode has an icon");
        var busIcon = rows.Commands.OfType<RawDraw>().Select(r => r.Command).OfType<RectCommand>().Where(r => r.Fill == "#00ffaa").ToList();
        Gates.Check(busIcon.Count == 1, "one bus line, one bus icon (the tram has none, the third no mode): " + busIcon.Count);
        Gates.Check(busIcon[0].X < shifted[0] && busIcon[0].X + busIcon[0].W <= shifted[0], "the icon is left of the label");
        // Everything stays inside the lines clip, which nests with the icon's own clip when it needs one.
        Gates.Check(SceneValidatorFor(rows), "valid scene");
    }

    private static bool SceneValidatorFor(MenuScene scene)
    {
        var builder = new SceneBuilder(scene.Width, scene.Height, scene.Background);
        foreach (var draw in scene.Commands)
        {
            if (draw is RawDraw raw) builder.Add(raw.Command);
            else builder.Add(OpenHud.Draw.SceneComposer.Convert(draw));
        }
        return builder.Build() is not null;
    }

    private static void ScenesStayValid()
    {
        // A busy identity: every slot a vector with the symbol budget, nine legend entries with icons, and a full lines page.
        var rich = "<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"#ff00aa\"/>" + string.Concat(Enumerable.Range(0, 20).Select(i => "<rect x=\"" + i % 10 + "\" y=\"" + i / 2 % 10 + "\" width=\"2\" height=\"2\" fill=\"#ffffff\"/>"));
        var glyph = Vector(rich);
        var legend = Enumerable.Range(0, 9).Select(i => new LegendEntry(new Dictionary<string, string> { ["en"] = "Kind " + i }, "#112233", glyph)).ToList();
        var identity = new PauseIdentity
        {
            Symbol = glyph, Omsi2Mark = glyph, OpenOmsiMark = glyph, Legend = legend, Credit = "c",
            ModeIcons = new Dictionary<string, Glyph> { ["bus"] = glyph, ["tram"] = glyph, ["metro"] = glyph, ["rail"] = glyph },
        };
        var menu = new PauseMenu(null, identity, "en");
        menu.SetFooterVersions("openHUD 0.1.0");
        menu.SetLines(Enumerable.Range(0, 60).Select(i => new LineEntry(i.ToString(), "#445566", "Line " + i, new[] { "bus", "tram", "metro", "rail" }[i % 4])).ToList());
        for (var page = 0; page < 3; page++)
        {
            var scene = menu.BuildScene();
            Gates.Check(scene.Commands.Count <= MenuScene.MaxCommands, "page " + page + " has " + scene.Commands.Count + " commands");
            Gates.Check(SceneValidatorFor(scene), "page " + page + " is a valid scene");
            Gates.Check(MenuSceneJson.Serialize(scene).Length < SceneLimits.MaxJsonBytes, "page " + page + " fits the JSON limit");
            menu.Handle(Ev.KeyDown(Ev.Tab));
        }
    }
}
