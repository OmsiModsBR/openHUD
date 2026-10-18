using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenHud.Pause;

namespace OpenHud.Tests.PauseTests;

// Gates of the layout (scale rules, geometry), the draw list, its JSON form, identity and i18n.
internal static class LayoutGates
{
    public static void Register()
    {
        Gates.Run("pause.layout.scale-table", ScaleTable);
        Gates.Run("pause.layout.input-normalization", Normalization);
        Gates.Run("pause.layout.draw-invariants", DrawInvariants);
        Gates.Run("pause.layout.stable-order", StableOrder);
        Gates.Run("pause.layout.regions-and-pages", RegionsAndPages);
        Gates.Run("pause.layout.lines-clip-and-scrollbar", LinesClip);
        Gates.Run("pause.identity.defaults-and-fallbacks", IdentityGates);
        Gates.Run("pause.identity.no-literals", NoLiterals);
        Gates.Run("pause.i18n.keys-and-fallback", I18nGates);
        Gates.Run("pause.scene-json.golden", JsonGolden);
        Gates.Run("pause.scene-json.hostile", JsonHostile);
        Gates.Run("pause.scene-json.protocol-grammar", JsonGrammar);
        Gates.Run("pause.scene-json.vector-files", VectorFiles);
        Gates.Run("pause.input-mirror.matches-bridge", InputMirror);
    }

    private static readonly (int W, int H, double Scale, double Ui, double Fit, double S, double Cs, bool Clamped)[] ScaleRows =
    {
        // fit = min(w/904, h*0.94/600); s = max(scale, min(scale*ui, fit)); cs = s / min(w/1920, h/1080), at most 1.728
        (800, 600, 1, 1, 0.885, 1, 1.728, true),
        (800, 600, 1, 1.5, 0.885, 1, 1.728, true),
        (1920, 1080, 1, 1, 1.692, 1, 1, false),
        (1920, 1080, 1, 1.5, 1.692, 1.5, 1.5, false),
        (1920, 1080, 1, 3, 1.692, 1.692, 1.692, false),
        (1920, 1080, 2, 1, 1.692, 2, 1.728, true),
        (1920, 1080, 0.75, 1.5, 1.692, 1.125, 1.125, false),
        (3440, 1440, 1, 1, 2.256, 1, 0.75, false),
        (3440, 1440, 1, 1.5, 2.256, 1.5, 1.125, false),
        (3440, 1440, 1, 3, 2.256, 2.256, 1.692, false),
        (3440, 1440, 0.75, 1, 2.256, 0.75, 0.5625, false),
        (1280, 720, 1, 1.5, 1.128, 1.128, 1.692, false),
    };

    private static void ScaleTable()
    {
        foreach (var row in ScaleRows)
        {
            var m = MenuMetrics.Compute(new MenuViewport(row.W, row.H, row.Scale, row.Ui));
            string ctx = row.W + "x" + row.H + " scale " + row.Scale + " ui " + row.Ui;
            Gates.Check(Math.Abs(m.Fit - row.Fit) < 0.002, ctx + ": fit " + m.Fit + " expected " + row.Fit);
            Gates.Check(Math.Abs(m.Scale - row.S) < 0.002, ctx + ": s " + m.Scale + " expected " + row.S);
            Gates.Check(Math.Abs(m.CanvasScale - row.Cs) < 0.002, ctx + ": canvas scale " + m.CanvasScale + " expected " + row.Cs);
            Gates.Check(m.Clamped == row.Clamped, ctx + ": clamped " + m.Clamped);
            Gates.Check(m.Scale >= row.Scale - 1e-9, ctx + ": never below the user scale");
            // The block always lies inside the canvas.
            Gates.Check(m.OriginX >= 0 && m.OriginY >= 0 && m.OriginX + MenuMetrics.DesignWidth * m.CanvasScale <= MenuScene.CanvasWidth + 1e-6
                && m.OriginY + MenuMetrics.DesignHeight * m.CanvasScale <= MenuScene.CanvasHeight + 1e-6, ctx + ": block inside the canvas");
        }
    }

    private static void Normalization()
    {
        var bad = new[]
        {
            new MenuViewport(0, 0, 0, 0), new MenuViewport(-5, -5, -1, -1), new MenuViewport(1920, 1080, double.NaN, double.PositiveInfinity),
            new MenuViewport(1920, 1080, double.NegativeInfinity, double.NaN), new MenuViewport(int.MaxValue, 1, 1e12, 1e12),
        };
        foreach (var vp in bad)
        {
            var m = MenuMetrics.Compute(vp);
            Gates.Check(double.IsFinite(m.Scale) && double.IsFinite(m.CanvasScale) && m.CanvasScale > 0 && m.CanvasScale <= 1.728 + 1e-9, "metrics stay finite and inside the canvas for " + vp);
            var menu = new PauseMenu();
            menu.SetViewport(vp);
            Validate(menu.BuildScene(), "hostile viewport " + vp);
        }
        var defaults = MenuMetrics.Compute(new MenuViewport(0, 0, 0, 0));
        Gates.Check(defaults.Viewport.ViewWidth == 1920 && defaults.Viewport.ViewHeight == 1080 && defaults.Viewport.Scale == 1, "invalid input falls back to the 1920x1080 defaults");
    }

    // ---- scene checks

    private static readonly Regex Color = new("^#[0-9a-f]{6}([0-9a-f]{2})?$", RegexOptions.Compiled);

    private static bool Inside(double x, double y, double w, double h) => x >= -0.01 && y >= -0.01 && x + w <= MenuScene.CanvasWidth + 0.01 && y + h <= MenuScene.CanvasHeight + 0.01;

    // Structural rules of every menu scene.
    private static void Validate(MenuScene scene, string ctx)
    {
        Gates.Check(scene.Mode == MenuSceneMode.Menu && scene.Background && scene.Width == 1920 && scene.Height == 1080, ctx + ": menu scene header");
        Gates.Check(scene.Commands.Count > 0 && scene.Commands.Count <= MenuScene.MaxCommands, ctx + ": command count " + scene.Commands.Count);
        int clipDepth = 0;
        for (int i = 0; i < scene.Commands.Count; i++)
        {
            string at = ctx + " cmd " + i + " " + scene.Commands[i].GetType().Name;
            switch (scene.Commands[i])
            {
                case RectDraw r:
                    Gates.Check(Inside(r.X, r.Y, r.W, r.H), at + ": outside the canvas " + r);
                    Gates.Check(r.W >= 0 && r.H >= 0 && (r.Fill != null || r.Stroke != null), at + ": degenerate");
                    CheckColor(r.Fill, at); CheckColor(r.Stroke, at);
                    break;
                case CircleDraw c:
                    Gates.Check(Inside(c.Cx - c.R, c.Cy - c.R, 2 * c.R, 2 * c.R) && c.R > 0, at + ": outside the canvas");
                    CheckColor(c.Fill, at); CheckColor(c.Stroke, at);
                    break;
                case LineDraw l:
                    Gates.Check(Inside(Math.Min(l.X1, l.X2), Math.Min(l.Y1, l.Y2), Math.Abs(l.X2 - l.X1), Math.Abs(l.Y2 - l.Y1)), at + ": outside the canvas");
                    CheckColor(l.Color, at);
                    break;
                case TextDraw t:
                    Gates.Check(!string.IsNullOrWhiteSpace(t.Text), at + ": empty text");
                    Gates.Check(t.X >= 0 && t.X <= MenuScene.CanvasWidth && t.Y >= 0 && t.Y <= MenuScene.CanvasHeight && t.Size > 0, at + ": anchor outside the canvas");
                    Gates.Check(t.MaxWidth >= 0 && t.X + (t.Align == TextAlign.Left ? t.MaxWidth : 0) <= MenuScene.CanvasWidth + 0.01, at + ": clip width leaves the canvas");
                    CheckColor(t.Color, at); CheckColor(t.Outline, at);
                    break;
                case ImageDraw im:
                    Gates.Check(!string.IsNullOrEmpty(im.Id) && Inside(im.X, im.Y, im.W, im.H), at + ": image");
                    break;
                case ClipDraw k:
                    clipDepth++;
                    Gates.Check(clipDepth <= 8 && Inside(k.X, k.Y, k.W, k.H), at + ": clip");
                    break;
                case UnclipDraw:
                    clipDepth--;
                    Gates.Check(clipDepth >= 0, at + ": unclip without clip");
                    break;
                default:
                    throw new InvalidOperationException(at + ": unknown record");
            }
        }
        Gates.Check(clipDepth == 0, ctx + ": clips are balanced");
        foreach (var region in scene.Regions)
            Gates.Check(Inside(region.Rect.X, region.Rect.Y, region.Rect.W, region.Rect.H), ctx + ": region " + region.Name + " inside the canvas");
        // The JSON form must also satisfy the protocol grammar.
        Grammar(MenuSceneJson.Serialize(scene), ctx);
    }

    private static void CheckColor(string? color, string at)
    {
        if (color != null) Gates.Check(Color.IsMatch(color), at + ": bad color " + color);
    }

    private static PauseIdentity RichIdentity() => new()
    {
        Titles = new Dictionary<string, string> { ["en"] = "Sample Map", ["pt-BR"] = "Mapa de Exemplo" },
        SymbolId = "map-symbol",
        Accent = "#d9822b",
        Line = "#cccccc",
        Legend = Enumerable.Range(0, 12).Select(i => new LegendEntry(new Dictionary<string, string> { ["en"] = "Legend " + i, ["pt"] = "Legenda " + i }, i % 3 == 0 ? "#ff0000" : "#00aa55")).ToList(),
    };

    private static void DrawInvariants()
    {
        var viewports = new[]
        {
            new MenuViewport(800, 600, 1, 1), new MenuViewport(1024, 768, 1, 1.5), new MenuViewport(1280, 720, 1, 1.5), new MenuViewport(1920, 1080, 1, 1),
            new MenuViewport(1920, 1080, 1, 3), new MenuViewport(1920, 1080, 2, 1), new MenuViewport(2560, 1440, 1.25, 1.5), new MenuViewport(3440, 1440, 1, 3),
            new MenuViewport(3840, 2160, 0.5, 1), new MenuViewport(640, 1136, 1, 1), new MenuViewport(7680, 1080, 4, 8),
        };
        int built = 0;
        foreach (var vp in viewports)
        foreach (var identity in new[] { PauseIdentity.Generic, RichIdentity() })
        foreach (var lineCount in new[] { 0, 4, 9, 10, 80 })
        {
            var menu = new PauseMenu(null, identity, "pt-BR");
            menu.SetViewport(vp);
            menu.SetLines(Enumerable.Range(0, lineCount).Select(i => new LineEntry(i % 7 == 0 ? "" : "Line " + i, i % 5 == 0 ? "bad" : "#112233", i % 2 == 0 ? "" : "detail " + i)).ToList());
            menu.Reset();
            for (int page = 0; page < 3; page++)
            {
                var g = menu.Geometry;
                // Exercise hover, press and focus on the page.
                var (x, y) = Ev.Center(g, page == 0 ? MenuGeometry.ItemRect(2) : MenuGeometry.TabRect(page));
                menu.Handle(Ev.Move(x, y));
                menu.Handle(Ev.Down(x, y));
                Validate(menu.BuildScene(), vp.ViewWidth + "x" + vp.ViewHeight + " page " + page + " lines " + lineCount);
                built++;
                menu.Handle(Ev.Up(x, y));
                menu.Handle(Ev.KeyDown(Ev.Tab));
                if (lineCount > 0) { menu.Handle(Ev.KeyDown(Ev.End)); menu.Handle(Ev.KeyDown(0x22)); }
            }
        }
        Gates.Check(built > 300, "enough combinations: " + built);
    }

    private static void StableOrder()
    {
        var a = new PauseMenu(null, RichIdentity(), "en");
        var b = new PauseMenu(null, RichIdentity(), "en");
        foreach (var m in new[] { a, b })
        {
            m.SetLines(Enumerable.Range(0, 20).Select(i => new LineEntry("L" + i, "#445566", "x")).ToList());
            m.SetViewport(new MenuViewport(1600, 900, 1, 1.25));
            m.Reset();
            m.Handle(Ev.KeyDown(Ev.Down_));
        }
        var first = MenuSceneJson.Serialize(a.BuildScene());
        Gates.Check(first == MenuSceneJson.Serialize(a.BuildScene()), "same state, same list (repeat)");
        Gates.Check(first == MenuSceneJson.Serialize(b.BuildScene()), "same state, same list (second instance)");
        Gates.Check(a.BuildScene().Commands.SequenceEqual(b.BuildScene().Commands), "records compare equal");
        a.Handle(Ev.KeyDown(Ev.Down_));
        Gates.Check(first != MenuSceneJson.Serialize(a.BuildScene()), "a changed focus changes the list");
        // The order is fixed: dimmer first, then the panel, the footer hint last.
        var cmds = a.BuildScene().Commands;
        Gates.Check(cmds[0] is RectDraw { Fill: "#000000a6", W: 1920, H: 1080 }, "the dimmer is first");
        Gates.Check(cmds[1] is RectDraw { Radius: > 0 }, "the panel is second");
        Gates.Check(cmds[^1] is TextDraw { Role: TextRole.Condensed }, "the hint is last");
    }

    private static void RegionsAndPages()
    {
        var menu = new PauseMenu();
        menu.SetViewport(new MenuViewport(1920, 1080, 1, 1));
        menu.Reset();
        var scene = menu.BuildScene();
        Gates.Check(scene.Regions.Count == 0, "the menu page has no region");
        Gates.Check(scene.Commands.OfType<TextDraw>().Count(t => t.Role == TextRole.Bold) >= 6, "title and five items are bold");
        menu.Handle(Ev.KeyDown(Ev.Tab));
        scene = menu.BuildScene();
        var map = scene.Region("map");
        Gates.Check(map is not null && scene.Regions.Count == 1 && Math.Abs(map.Rect.W - 856) < 0.01 && Math.Abs(map.Rect.H - 396) < 0.01, "the map region is the content area at scale 1: " + map);
        Gates.Check(scene.Commands.OfType<TextDraw>().All(t => t.Text != "Back to the game"), "menu items are not drawn on the map page");
        menu.Handle(Ev.KeyDown(Ev.Tab));
        scene = menu.BuildScene();
        Gates.Check(scene.Region("lines") is not null && scene.Region("map") is null, "the lines region");
        Gates.Check(scene.Commands.OfType<TextDraw>().Any(t => t.Text == "No lines to show"), "empty lines page says so");
        // The map-behind scene of the provisional mode covers the canvas.
        var behind = MenuScene.MapBehind;
        Gates.Check(behind.Mode == MenuSceneMode.MapBehind && !behind.Background && behind.Commands.Count == 0 && behind.Region("map")!.Rect == new MenuRect(0, 0, 1920, 1080), "map-behind scene");
        Gates.Check(MenuScene.None.Mode == MenuSceneMode.None && MenuScene.None.Commands.Count == 0, "none scene");
    }

    private static void LinesClip()
    {
        var menu = new PauseMenu();
        menu.SetLines(Enumerable.Range(0, 50).Select(i => new LineEntry("L" + i, "#778899", "d")).ToList());
        menu.Handle(Ev.KeyDown(Ev.Tab)); menu.Handle(Ev.KeyDown(Ev.Tab));
        var cmds = menu.BuildScene().Commands;
        int clip = cmds.TakeWhile(c => c is not ClipDraw).Count();
        int unclip = cmds.TakeWhile(c => c is not UnclipDraw).Count();
        Gates.Check(clip < cmds.Count && unclip > clip, "rows are drawn inside a clip");
        int rowLabels = cmds.Skip(clip).Take(unclip - clip).OfType<TextDraw>().Count(t => t.Role == TextRole.Bold);
        Gates.Check(rowLabels == MenuGeometry.LinesVisibleRows, "only the visible rows are drawn: " + rowLabels);
        var thumbs = cmds.Skip(unclip).OfType<RectDraw>().Count();
        Gates.Check(thumbs == 2, "scroll track and thumb after the clip");
        menu.SetLines(Enumerable.Range(0, 3).Select(i => new LineEntry("L" + i, "#778899")).ToList());
        cmds = menu.BuildScene().Commands;
        int after = cmds.TakeWhile(c => c is not UnclipDraw).Count();
        Gates.Check(cmds.Skip(after).OfType<RectDraw>().Count() == 0, "no scrollbar for a short list");
    }

    // ---- identity

    private static void IdentityGates()
    {
        var titles = new Dictionary<string, string> { ["en"] = "English", ["pt-BR"] = "Brasil", ["de"] = "Deutsch", ["fr"] = "  " };
        var id = new PauseIdentity { Titles = titles };
        var table = new (string Lang, string? Expected)[]
        {
            ("pt-BR", "Brasil"), ("PT-br", "Brasil"), ("de-DE", "Deutsch"), ("de", "Deutsch"), ("en-GB", "English"), ("pt", "English"),
            ("fr", "English"), ("", "English"), ("zz", "English"),
        };
        foreach (var (lang, expected) in table)
            Gates.Check(id.TitleFor(lang) == expected, "title for '" + lang + "': " + id.TitleFor(lang) + " expected " + expected);
        var onlyOne = new PauseIdentity { Titles = new Dictionary<string, string> { ["ja"] = "Nihon" } };
        Gates.Check(onlyOne.TitleFor("en") == "Nihon", "any title when nothing matches");
        Gates.Check(PauseIdentity.Generic.TitleFor("en") is null, "no title in the generic identity");
        Gates.Check(new PauseIdentity { Titles = new Dictionary<string, string> { ["en"] = " " } }.TitleFor("en") is null, "blank titles are no titles");

        // The generic scene uses the lookup's title; a title from the package wins.
        var generic = new PauseMenu(k => k == PauseStrings.Title ? "Paused!" : k, null, "en").BuildScene();
        Gates.Check(generic.Commands.OfType<TextDraw>().Any(t => t.Text == "Paused!"), "generic title comes from the i18n lookup");
        Gates.Check(generic.Commands.OfType<CircleDraw>().Any(c => c.Fill == PauseIdentity.DefaultAccent), "generic symbol uses the default accent");
        Gates.Check(!generic.Commands.OfType<ImageDraw>().Any(), "generic identity draws no image");
        var rich = new PauseMenu(null, RichIdentity(), "pt-BR").BuildScene();
        Gates.Check(rich.Commands.OfType<TextDraw>().Any(t => t.Text == "Mapa de Exemplo"), "package title for the language");
        Gates.Check(rich.Commands.OfType<ImageDraw>().Any(i => i.Id == "map-symbol"), "package symbol");
        Gates.Check(rich.Commands.OfType<TextDraw>().Any(t => t.Text == "Legenda 3"), "legend text by primary language subtag");
        Gates.Check(rich.Commands.OfType<TextDraw>().Count(t => t.Text.StartsWith("Legenda")) == 9, "at most nine legend rows");

        // Malformed package colors fall back to the defaults and never reach the scene.
        var bad = new PauseIdentity { Accent = "red", Line = "#12", SymbolId = "  ", Legend = new[] { new LegendEntry(new Dictionary<string, string> { ["en"] = "x" }, "javascript") } };
        var scene = new PauseMenu(null, bad, "en").BuildScene();
        string json = MenuSceneJson.Serialize(scene);
        Gates.Check(!json.Contains("red") && !json.Contains("javascript") && !json.Contains("#12\""), "bad colors never reach the scene");
        Gates.Check(json.Contains(PauseIdentity.DefaultAccent), "the default accent is used instead");
        Validate(scene, "bad identity");
        // A legend entry with no text is skipped, never drawn empty.
        var emptyLegend = new PauseIdentity { Legend = new[] { new LegendEntry(new Dictionary<string, string>(), "#ff0000"), new LegendEntry(new Dictionary<string, string> { ["en"] = "Only" }, "#00ff00") } };
        Validate(new PauseMenu(null, emptyLegend, "en").BuildScene(), "empty legend entry");
    }

    private static string Translate(string key, string lang)
    {
        var entry = PauseStrings.Catalog.First(e => e.Key == key);
        return lang == "pt-BR" ? entry.Portuguese : lang == "de" ? entry.German : entry.English;
    }

    private static void NoLiterals()
    {
        string[] forbidden = { "RMG", "BRT", "Futura", "rmg-leste", "RMTC", "OMSI 2", "http" };
        foreach (var lang in new[] { "en", "pt-BR", "de" })
        {
            var menu = new PauseMenu(key => Translate(key, lang), null, lang);
            menu.SetLines(new[] { new LineEntry("Line A", "#112233", "detail") });
            for (int page = 0; page < 3; page++)
            {
                string json = MenuSceneJson.Serialize(menu.BuildScene());
                foreach (var word in forbidden)
                    Gates.Check(!json.Contains(word, StringComparison.Ordinal), "the generic menu carries no '" + word + "' (" + lang + ", page " + page + ")");
                menu.Handle(Ev.KeyDown(Ev.Tab));
            }
        }
        foreach (var entry in PauseStrings.Catalog)
            foreach (var word in forbidden)
                Gates.Check(!(entry.English + entry.Portuguese + entry.German).Contains(word), "the string " + entry.Key + " has no '" + word + "'");
    }

    // ---- i18n

    private static void I18nGates()
    {
        var keys = PauseStrings.Catalog.Select(e => e.Key).ToList();
        Gates.Check(keys.Distinct().Count() == keys.Count, "keys are unique");
        foreach (var e in PauseStrings.Catalog)
        {
            Gates.Check(e.Key.StartsWith("UI_PAUSE_"), "key prefix: " + e.Key);
            Gates.Check(!string.IsNullOrWhiteSpace(e.English) && !string.IsNullOrWhiteSpace(e.Portuguese) && !string.IsNullOrWhiteSpace(e.German), "three languages for " + e.Key);
        }
        // Every key the renderer asks for is in the catalog, and every catalog key is used somewhere.
        var asked = new HashSet<string>();
        string Record(string key) { asked.Add(key); return key + "~"; }
        var menu = new PauseMenu(Record, new PauseIdentity { Legend = new[] { new LegendEntry(new Dictionary<string, string> { ["en"] = "x" }, "#ffffff") } }, "en");
        for (int page = 0; page < 3; page++) { MenuSceneJson.Serialize(menu.BuildScene()); menu.Handle(Ev.KeyDown(Ev.Tab)); }
        Gates.Check(asked.All(keys.Contains), "all asked keys are cataloged: " + string.Join(",", asked.Except(keys)));
        Gates.Check(keys.All(asked.Contains), "all cataloged keys are used: " + string.Join(",", keys.Except(asked)));
        menu.Handle(Ev.KeyDown(Ev.Tab)); menu.Handle(Ev.KeyDown(Ev.Tab));
        var scene = menu.BuildScene();
        Gates.Check(scene.Commands.OfType<TextDraw>().Any(t => t.Text == "UI_PAUSE_NO_LINES~"), "text comes from the lookup");

        // A lookup that misses, answers blank, echoes the key or throws falls back to English: no empty label.
        foreach (Func<string, string> faulty in new Func<string, string>[] { _ => "", _ => "   ", k => k, _ => null!, _ => throw new InvalidOperationException("table down") })
        {
            var m = new PauseMenu(faulty, null, "en");
            var drawn = m.BuildScene();
            Gates.Check(drawn.Commands.OfType<TextDraw>().Any(t => t.Text == "Back to the game"), "English fallback for a faulty lookup");
            Validate(drawn, "faulty lookup");
        }
        Gates.Check(new PauseMenu(null).BuildScene().Commands.OfType<TextDraw>().Any(t => t.Text == "Options"), "no lookup at all");
    }

    // ---- JSON

    private static void JsonGolden()
    {
        string accented = "openHUD \"q\" " + (char)0xE9 + "\n";
        var scene = new MenuScene(MenuSceneMode.Menu, 1920, 1080, true, new MenuDraw[]
        {
            new RectDraw(10, 20.5, 300, 120, "#14171cd9", null, 0, 12),
            new RectDraw(0, 0, 5, 5, null, "#ffffff", 2),
            new CircleDraw(100, 100, 24, "#3b82f6"),
            new LineDraw(0, 0, 10, -0.001, "#ffffff", 1.5),
            new TextDraw(24, 40, accented, 22, TextRole.Black, "#ffffff", TextAlign.Right, TextBase.Alphabetic, 100, "#000000ff"),
            new TextDraw(1, 2, "a", 9, TextRole.Condensed, "#fff000", TextAlign.Center, TextBase.Top),
            new ImageDraw("s1", 1, 2, 3, 4, 0.5, "#ff0000"),
            new ClipDraw(1, 2, 3, 4),
            new UnclipDraw(),
        }, Array.Empty<MenuRegion>());
        string expected = "{\"v\":1,\"w\":1920,\"h\":1080,\"background\":true,\"cmds\":["
            + "{\"t\":\"rect\",\"x\":10,\"y\":20.5,\"w\":300,\"h\":120,\"fill\":\"#14171cd9\",\"r\":12},"
            + "{\"t\":\"rect\",\"x\":0,\"y\":0,\"w\":5,\"h\":5,\"stroke\":\"#ffffff\",\"sw\":2},"
            + "{\"t\":\"circle\",\"cx\":100,\"cy\":100,\"r\":24,\"fill\":\"#3b82f6\"},"
            + "{\"t\":\"line\",\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0,\"color\":\"#ffffff\",\"sw\":1.5},"
            + "{\"t\":\"text\",\"x\":24,\"y\":40,\"s\":\"openHUD \\\"q\\\" " + (char)0xE9 + "\\n\",\"size\":22,\"font\":\"black\",\"color\":\"#ffffff\",\"align\":\"r\",\"base\":\"alpha\",\"maxw\":100,\"outline\":\"#000000ff\"},"
            + "{\"t\":\"text\",\"x\":1,\"y\":2,\"s\":\"a\",\"size\":9,\"font\":\"condensed\",\"color\":\"#fff000\",\"align\":\"c\",\"base\":\"top\"},"
            + "{\"t\":\"image\",\"id\":\"s1\",\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"op\":0.5,\"tint\":\"#ff0000\"},"
            + "{\"t\":\"clip\",\"kind\":\"rect\",\"x\":1,\"y\":2,\"w\":3,\"h\":4},"
            + "{\"t\":\"unclip\"}]}";
        string json = MenuSceneJson.Serialize(scene);
        Gates.Check(json == expected, "golden JSON differs:\n" + json + "\n" + expected);
        Grammar(json, "golden");

        // Invariant culture: a comma-decimal culture does not change the text.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Gates.Check(MenuSceneJson.Serialize(scene) == expected, "culture independent");
        }
        finally { CultureInfo.CurrentCulture = previous; }

        // The empty scene of the "draw nothing" mode.
        Gates.Check(MenuSceneJson.Serialize(MenuScene.None) == "{\"v\":1,\"w\":1920,\"h\":1080,\"background\":false,\"cmds\":[]}", "empty scene");
    }

    private static void JsonHostile()
    {
        foreach (var bad in new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var scene = new MenuScene(MenuSceneMode.Menu, 1920, 1080, false, new MenuDraw[] { new RectDraw(bad, 0, 1, 1, "#ffffff") }, Array.Empty<MenuRegion>());
            bool refused = false;
            try { MenuSceneJson.Serialize(scene); } catch (ArgumentException) { refused = true; }
            Gates.Check(refused, "non-finite number " + bad + " is refused");
        }
        var many = new MenuScene(MenuSceneMode.Menu, 1920, 1080, false, Enumerable.Repeat<MenuDraw>(new UnclipDraw(), MenuScene.MaxCommands + 1).ToList(), Array.Empty<MenuRegion>());
        bool tooMany = false;
        try { MenuSceneJson.Serialize(many); } catch (ArgumentException) { tooMany = true; }
        Gates.Check(tooMany, "more than 8192 commands is refused");
        var exact = new MenuScene(MenuSceneMode.Menu, 1920, 1080, false, Enumerable.Repeat<MenuDraw>(new UnclipDraw(), MenuScene.MaxCommands).ToList(), Array.Empty<MenuRegion>());
        Gates.Check(MenuSceneJson.Serialize(exact).Length > 0, "exactly 8192 commands is accepted");
        // Control characters and lone surrogates stay valid JSON (the surrogate becomes U+FFFD).
        string text = "a" + (char)1 + "\t" + (char)0x2028 + (char)0xD800 + "b" + (char)0xDC00;
        var odd = new MenuScene(MenuSceneMode.Menu, 1920, 1080, false, new MenuDraw[] { new TextDraw(1, 1, text, 10, TextRole.Regular, "#ffffff") }, Array.Empty<MenuRegion>());
        using var parsed = JsonDocument.Parse(MenuSceneJson.Serialize(odd));
        string back = parsed.RootElement.GetProperty("cmds")[0].GetProperty("s").GetString()!;
        Gates.Check(back == "a" + (char)1 + "\t" + (char)0x2028 + (char)0xFFFD + "b" + (char)0xFFFD, "lone surrogates are replaced");
        // A valid surrogate pair survives.
        string pair = "x" + char.ConvertFromUtf32(0x1F68C) + "y";
        var emoji = new MenuScene(MenuSceneMode.Menu, 1920, 1080, false, new MenuDraw[] { new TextDraw(1, 1, pair, 10, TextRole.Regular, "#ffffff") }, Array.Empty<MenuRegion>());
        using var parsedPair = JsonDocument.Parse(MenuSceneJson.Serialize(emoji));
        Gates.Check(parsedPair.RootElement.GetProperty("cmds")[0].GetProperty("s").GetString() == pair, "a valid surrogate pair is kept");
    }

    // The grammar of docs/scene-protocol.md: members allowed per command type.
    private static readonly Dictionary<string, (string[] Required, string[] Optional)> CommandGrammar = new()
    {
        ["rect"] = (new[] { "x", "y", "w", "h" }, new[] { "fill", "stroke", "sw", "r" }),
        ["circle"] = (new[] { "cx", "cy", "r" }, new[] { "fill", "stroke", "sw" }),
        ["arc"] = (new[] { "cx", "cy", "r", "a0", "a1", "color", "sw" }, new[] { "cap" }),
        ["line"] = (new[] { "x1", "y1", "x2", "y2", "color", "sw" }, new[] { "cap" }),
        ["poly"] = (new[] { "pts" }, new[] { "fill", "stroke", "sw", "closed" }),
        ["text"] = (new[] { "x", "y", "s", "size" }, new[] { "font", "color", "align", "base", "maxw", "outline" }),
        ["image"] = (new[] { "id", "x", "y", "w", "h" }, new[] { "op", "tint", "rot" }),
        ["clip"] = (new[] { "kind" }, new[] { "x", "y", "w", "h", "cx", "cy", "r" }),
        ["unclip"] = (Array.Empty<string>(), Array.Empty<string>()),
        ["push"] = (Array.Empty<string>(), new[] { "tx", "ty", "rot", "sx", "sy" }),
        ["pop"] = (Array.Empty<string>(), Array.Empty<string>()),
    };

    private static void Grammar(string json, string ctx)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Gates.Check(root.GetProperty("v").GetInt32() == 1, ctx + ": version");
        Gates.Check(root.GetProperty("w").GetInt32() > 0 && root.GetProperty("h").GetInt32() > 0, ctx + ": canvas size");
        var background = root.GetProperty("background");
        Gates.Check(background.ValueKind == JsonValueKind.True || background.ValueKind == JsonValueKind.False, ctx + ": background flag");
        var cmds = root.GetProperty("cmds");
        Gates.Check(cmds.GetArrayLength() <= MenuScene.MaxCommands && json.Length <= 1 << 20, ctx + ": scene limits (8192 commands, 1 MiB)");
        int index = 0;
        foreach (var cmd in cmds.EnumerateArray())
        {
            string at = ctx + " json cmd " + index++;
            string t = cmd.GetProperty("t").GetString()!;
            Gates.Check(CommandGrammar.TryGetValue(t, out var grammar), at + ": unknown type " + t);
            var members = cmd.EnumerateObject().Select(p => p.Name).Where(n => n != "t").ToList();
            foreach (var required in grammar.Required) Gates.Check(members.Contains(required), at + " (" + t + "): missing " + required);
            foreach (var member in members) Gates.Check(grammar.Required.Contains(member) || grammar.Optional.Contains(member), at + " (" + t + "): unexpected member " + member);
            foreach (var p in cmd.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number) Gates.Check(double.IsFinite(p.Value.GetDouble()), at + ": finite " + p.Name);
                if (p.Name is "fill" or "stroke" or "color" or "tint" or "outline") Gates.Check(Color.IsMatch(p.Value.GetString()!), at + ": color " + p.Value);
            }
            if (t == "text")
            {
                Gates.Check(cmd.GetProperty("s").GetString()!.Length > 0, at + ": empty text");
                if (cmd.TryGetProperty("font", out var font)) Gates.Check(new[] { "regular", "medium", "bold", "black", "condensed" }.Contains(font.GetString()), at + ": font role");
                if (cmd.TryGetProperty("align", out var align)) Gates.Check(new[] { "l", "c", "r" }.Contains(align.GetString()), at + ": align");
                if (cmd.TryGetProperty("base", out var baseline)) Gates.Check(new[] { "top", "mid", "alpha" }.Contains(baseline.GetString()), at + ": base");
            }
            if (t == "image" && cmd.TryGetProperty("op", out var op)) Gates.Check(op.GetDouble() >= 0 && op.GetDouble() <= 1, at + ": opacity");
        }
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void JsonGrammar()
    {
        // The validator itself must refuse what the protocol refuses (otherwise the checks above prove nothing).
        foreach (var bad in new[]
        {
            "{\"v\":2,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[{\"t\":\"rect\",\"x\":1}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":1,\"h\":1,\"bogus\":3}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[{\"t\":\"wat\"}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[{\"t\":\"text\",\"x\":1,\"y\":1,\"s\":\"\",\"size\":2}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"background\":false,\"cmds\":[{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":1,\"h\":1,\"fill\":\"red\"}]}",
        })
        {
            bool refused = false;
            try { Grammar(bad, "negative"); } catch (Exception) { refused = true; }
            Gates.Check(refused, "the grammar check refuses: " + bad);
        }
    }

    private static void VectorFiles()
    {
        foreach (var name in new[] { "scene-minimal.json", "scene-pause.json", "scene-radar.json" })
        {
            string? path = FindRepoFile(Path.Combine("tests", "vectors", name));
            Gates.Check(path != null, "vector file " + name + " is found");
            Grammar(File.ReadAllText(path!), name);
        }
        // The pause vector is what a menu scene looks like: a background, a dimmer, a button with centered bold text.
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile(Path.Combine("tests", "vectors", "scene-pause.json"))!));
        Gates.Check(doc.RootElement.GetProperty("background").GetBoolean(), "the pause vector draws over the captured background");
        using var ours = JsonDocument.Parse(MenuSceneJson.Serialize(new PauseMenu().BuildScene()));
        Gates.Check(ours.RootElement.GetProperty("background").GetBoolean(), "so does the menu scene");
        Gates.Check(ours.RootElement.GetProperty("cmds")[0].GetProperty("t").GetString() == doc.RootElement.GetProperty("cmds")[0].GetProperty("t").GetString()
            && ours.RootElement.GetProperty("cmds")[0].GetProperty("fill").GetString() == doc.RootElement.GetProperty("cmds")[0].GetProperty("fill").GetString(),
            "both start with the same full-canvas dimmer");
    }

    private static void InputMirror()
    {
        static void SameEnum(Type ours, Type theirs)
        {
            var a = Enum.GetNames(ours).ToDictionary(n => n, n => Convert.ToInt64(Enum.Parse(ours, n)));
            var b = Enum.GetNames(theirs).ToDictionary(n => n, n => Convert.ToInt64(Enum.Parse(theirs, n)));
            Gates.Check(a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value), ours.Name + " mirrors " + theirs.Name);
            Gates.Check(Enum.GetUnderlyingType(ours) == Enum.GetUnderlyingType(theirs), ours.Name + " has the same underlying type");
        }
        SameEnum(typeof(PauseInputKind), typeof(OmsiLaunch.Runtime.Modules.ModuleInputKind));
        SameEnum(typeof(PauseMouseButton), typeof(OmsiLaunch.Runtime.Modules.ModuleMouseButton));
        var ours = typeof(PauseInputEvent).GetConstructors().Single(c => c.GetParameters().Length == 7).GetParameters();
        var theirs = typeof(OmsiLaunch.Runtime.Modules.ModuleInputEvent).GetConstructors().Single(c => c.GetParameters().Length == 7).GetParameters();
        for (int i = 0; i < 7; i++)
        {
            Gates.Check(ours[i].Name == theirs[i].Name, "field " + i + " name: " + ours[i].Name + " vs " + theirs[i].Name);
            bool enumMirror = ours[i].ParameterType.IsEnum;
            Gates.Check(enumMirror ? Enum.GetUnderlyingType(ours[i].ParameterType) == Enum.GetUnderlyingType(theirs[i].ParameterType) : ours[i].ParameterType == theirs[i].ParameterType, "field " + ours[i].Name + " type");
        }
    }
}
