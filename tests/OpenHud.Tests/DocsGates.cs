using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenHud.I18n;
using OpenHud.Packages;
using OpenHud.Packages.PauseMenu;
using OpenHud.Packages.Realmap;
using OpenHud.Settings;
using OpenHud.Tests.Core;

namespace OpenHud.Tests;

// Gates of the documentation (docs\). The examples in the pages are the examples the readers
// are run on: a package printed in a page is rebuilt from the page's own text and read with the
// real readers, a setting shown in a page goes through the real settings parser, and the tables
// that mirror code (the settings, the interface texts) are compared with it. A page and the code
// that disagree fail here.
internal static class DocsGates
{
    public static void Register()
    {
        Gates.Run("docs.pause-example-reads-clean", PauseExampleReadsClean);
        Gates.Run("docs.pause-template-reads-clean", PauseTemplateReadsClean);
        Gates.Run("docs.pause-example-values", PauseExampleValues);
        Gates.Run("docs.realmap-example-reads-clean", RealmapExampleReadsClean);
        Gates.Run("docs.rejected-entries-match-the-readers", RejectedEntriesMatchTheReaders);
        Gates.Run("docs.settings-reference-covers-the-template", SettingsReferenceCoversTheTemplate);
        Gates.Run("docs.settings-examples-are-accepted", SettingsExamplesAreAccepted);
        Gates.Run("docs.ui-language-keys-match-the-table", UiLanguageKeysMatchTheTable);
        Gates.Run("docs.command-lines-use-known-flags", CommandLinesUseKnownFlags);
        Gates.Run("docs.no-unfinished-markers", NoUnfinishedMarkers);
    }

    // ------------------------------------------------------------------ the pause-menu package

    private static readonly string PauseGuide = "docs/guides/making-a-pause-menu-package.md";
    private static readonly string PauseReference = "docs/reference/pause-menu-package.md";

    // Writes the worked example of the guide: its text files verbatim, its binary files as stand-ins
    // of the sizes the guide's table states. Returns the package folder.
    private static string WritePauseExample(TempDir dir, Action<TempDir>? after = null)
    {
        var doc = Doc.Read(PauseGuide);
        var files = doc.FileBlocks("openomsi-pausemenu/");
        Expect.True(files.Count == 12, "the guide prints 12 text files, found " + files.Count);
        foreach (var (path, text) in files)
            dir.Write("openomsi-pausemenu/" + path, text);
        var table = doc.TableRows(@"^\| `([^`]+)` \| (PNG|TrueType) \| ([^|]+) \|$");
        Expect.True(table.Count == 10, "the binary files table lists 10 files, found " + table.Count);
        foreach (var row in table)
        {
            var path = "openomsi-pausemenu/" + row[0];
            if (row[1] == "PNG")
            {
                var size = Regex.Match(row[2], @"^(\d+) x (\d+)$");
                Expect.True(size.Success, "the size of " + row[0] + " reads 'W x H'");
                dir.Write(path, Synthetic.Png(int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value)));
            }
            else dir.Write(path, Synthetic.Font());
        }
        after?.Invoke(dir);
        return dir.Combine("openomsi-pausemenu");
    }

    private static void PauseExampleReadsClean()
    {
        using var dir = new TempDir();
        var folder = WritePauseExample(dir);
        var result = PauseMenuPackageReader.Read(folder, "Example City");
        Expect.True(result.Status == PackageStatus.Loaded, "the example package is loaded, status " + result.Status);
        Expect.True(result.Diagnostics.Count == 0, "the example package is read without a rejected entry: " + string.Join(" | ", result.Diagnostics));
        Expect.True(result.Identity.PackageUsed && result.Identity.Fonts.HasFonts, "the example uses its fonts");
    }

    private static void PauseTemplateReadsClean()
    {
        using var dir = new TempDir();
        var folder = WritePauseExample(dir);
        var templates = Doc.Read(PauseReference).FileBlocks("template/");
        Expect.True(templates.Count == 4, "the reference prints 4 template files, found " + templates.Count);
        foreach (var (path, text) in templates) File.WriteAllText(Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)), text, new UTF8Encoding(false));
        var result = PauseMenuPackageReader.Read(folder, "Example City");
        Expect.True(result.Status == PackageStatus.Loaded, "the template package is loaded, status " + result.Status);
        Expect.True(result.Diagnostics.Count == 0, "the template is read without a rejected entry: " + string.Join(" | ", result.Diagnostics));
        Expect.Equal("Cidade Exemplo", result.Identity.Title("PTB"), "the template's Portuguese title");
    }

    // What the guide says the example means.
    private static void PauseExampleValues()
    {
        using var dir = new TempDir();
        var folder = WritePauseExample(dir);
        var id = PauseMenuPackageReader.Read(folder, "Example City").Identity;
        Expect.Equal("Example City pause menu", id.PackageName ?? "", "package name");
        Expect.Equal("Example Maps Studio", id.Author ?? "", "author");
        Expect.Equal("Example City", id.Title("ENG"), "title ENG");
        Expect.Equal("Cidade Exemplo", id.Title("PTB"), "title PTB");
        Expect.Equal("Beispielstadt", id.Title("DEU"), "title DEU");
        Expect.Equal("Example City", id.Title("ESP"), "title for a language without a variant");
        Expect.Equal("Example City", new PauseMenuIdentity { MapFolder = "Example City" }.Title(null), "the generic title is the folder name");
        Expect.True(id.Symbol.IsPicture && id.Symbol.FilePath2x is not null, "symbol with its @2x sibling");
        Expect.True(id.SymbolFor("green", PauseLayout.Classic).Name == "symbols/map-symbol-green.png", "symbol.green");
        Expect.True(id.SymbolFor("blue", PauseLayout.OpenHud).Name == "symbols/map-symbol-wide.png", "symbol.openomsi in the openHUD layout");
        Expect.Equal("green", id.Theme, "theme");
        Expect.True(id.Layout == PauseLayout.Classic, "layout");
        Expect.Equal("#3a7bd5", id.LineColor?.ToHex() ?? "", "color.line (the inline comment is not part of the value)");
        Expect.Equal("#c0392b", id.GroupColors["express"].ToHex(), "color.group.express");
        Expect.True(id.StopIcon.IsPicture && id.StopIcon.Format == PictureFormat.Svg, "stop_icon is an SVG");
        Expect.True(id.MarkerIcons["garage"].Kind == SymbolKind.Internal, "marker_icon.garage = @simple");
        Expect.True(id.ModeIcons.ContainsKey("bus") && id.ModeIcons.ContainsKey("tram"), "mode icons");
        Expect.Equal("Estação", id.Legend("station", "PTB") ?? "", "legend.station.PTB");
        Expect.Equal("Terminal da Linha 12", id.Legend("terminal", "PTB") ?? "", "strings/PTB.cfg wins over identity.cfg");
        Expect.Equal("Terminal", id.Legend("terminal", "ESP") ?? "", "identity.cfg plain key for a language without a variant");
        Expect.True(id.Omsi2Mark.IsPicture && id.Omsi2Mark.Name == "symbols/omsi2-logo.png", "omsi2-logo.png is the OMSI 2 mark");
        Expect.True(id.OpenOmsiMark.IsPicture, "openomsi.png");
        var fonts = id.Fonts;
        Expect.True(fonts.ForRole("bold") is { Substituted: false, Weight: 700 }, "bold font");
        Expect.True(fonts.ForRole("black") is { Weight: 900 }, "black font weight");
    }

    // ------------------------------------------------------------------ the realmap package

    private static readonly string RealmapGuide = "docs/guides/using-a-realmap-package.md";

    private static RealmapPackage OpenRealmapExample(TempDir dir, bool surface = true)
    {
        var doc = Doc.Read(RealmapGuide);
        var files = doc.FileBlocks("openomsi-realmap/");
        Expect.True(files.Count == 4, "the guide prints 4 realmap text files, found " + files.Count);
        foreach (var (path, text) in files) dir.Write("openomsi-realmap/" + path, text);
        Expect.True(doc.Text.Contains("`icons/terminal.png` (64 x 64)") && doc.Text.Contains("`icons/depot.png` (96 x 96)"), "the guide states the icon sizes");
        dir.Write("openomsi-realmap/icons/terminal.png", Synthetic.Png(64, 64));
        dir.Write("openomsi-realmap/icons/depot.png", Synthetic.Png(96, 96));
        if (surface)
        {
            var b = new SurfaceBuilder { OriginX = 0, OriginY = 0, ChunkSize = 2000 };
            var river = b.Str("Example River"); var main = b.Str("Main Street"); var avenue = b.Str("River Avenue");
            var oldTown = b.Str("Old Town"); var terminal = b.Str("Central Terminal");
            var c = b.Chunk(0, 0);
            c.Fills.Add((0, new float[] { 100, 100, 400, 100, 100, 400 }));
            c.Fills.Add((1, new float[] { 1500, 1500, 1700, 1500, 1500, 1700 }));
            c.Lines.Add((0, 0, 6, river, new float[] { 0, 500, 900, 520, 1900, 500 }));
            c.Lines.Add((3, 2, 0, main, new float[] { 200, 200, 800, 200 }));
            c.Lines.Add((4, 40, 50, avenue, new float[] { 200, 300, 800, 300 }));
            c.Labels.Add((0, 200, oldTown, 500, 500));
            c.Labels.Add((1, 255, terminal, 1200, 840));
            dir.Write("openomsi-realmap/surface.oms", b.Build());
        }
        return RealmapPackage.OpenDirectory(dir.Combine("openomsi-realmap"));
    }

    private static void RealmapExampleReadsClean()
    {
        using var dir = new TempDir();
        var p = OpenRealmapExample(dir);
        Expect.True(p.Status == PackageStatus.Loaded, "the realmap example is loaded, status " + p.Status);
        Expect.True(p.Diagnostics.Count == 0, "the realmap example is read without a rejected entry: " + string.Join(" | ", p.Diagnostics));
        Expect.True(p.Layers == (RealmapLayers.Surface | RealmapLayers.Markers | RealmapLayers.Regions | RealmapLayers.TransitLines), "all four layers: " + p.Layers);
        Expect.Equal(300.0, p.GridMetres ?? 0, "grid");
        Expect.True(p.Manifest!.Player.Triangle && p.Manifest.Alignment == Alignment.Fitted, "player marker and alignment");
        Expect.Equal(2, p.Markers.Count, "markers");
        Expect.Equal(2, p.Regions.Count, "regions");
        Expect.Equal(3, p.TransitLines.Count, "line forms");
        Expect.Equal(2, p.LinesOn(20210101).Count, "forms valid in 2021 (Line 12 and the tram)");
        Expect.Equal(3, p.LinesOn(20190615).Count, "forms valid in 2019 (both forms of Line 12 and the tram)");
        Expect.True(p.Regions.First(r => r.Name == "Old Town").HasReadableLabel && !p.Regions.First(r => r.Name == "Harbor District").HasReadableLabel, "the region label boxes");
        Expect.Equal("Old Town", p.RegionAt(500, 500)?.Name ?? "", "the municipality at a point");
        var street = p.StreetAt(500, 205);
        Expect.True(street is { Name: "Main Street", Rank: 2 }, "the street under the player");
        var limited = p.StreetAt(500, 305);
        Expect.True(limited is { Name: "River Avenue", SpeedLimitKmh: 50, HeavyLimitKmh: 40 }, "the street with limits");
        var view = p.Query(new MapRect(0, 0, 2000, 2000), DetailLevel.Close);
        Expect.True(view.Areas.Count == 2 && view.Lines.Count == 1 && view.Streets.Count == 2 && view.Labels.Count == 2 && view.Markers.Count == 2, "a window query returns the example's geometry");
    }

    // ------------------------------------------------------------------ rejected entries

    // One change to the worked example: the file it edits (relative to the package), the text replaced or a setup action.
    private sealed record Bad(string Label, string File, string EntryPart, string Reason, Func<string, string>? Edit = null, Action<TempDir>? Setup = null);

    private const string Id = "identity.cfg";

    // The cases of the pause-menu package table of the guide. Each one changes the example in a single place.
    private static IEnumerable<Bad> PauseCases()
    {
        static Func<string, string> Replace(string from, string to) => text => text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : throw new InvalidOperationException("the example no longer contains: " + from);
        yield return new("symbol.green names a picture that is not there", Id, "symbol.green", "file not found in symbols/ (or beyond the folder limit)", Replace("symbol.green = symbols/map-symbol-green.png", "symbol.green = symbols/missing.png"));
        yield return new("color.accent = blue", Id, "color.accent", "not a #rrggbb colour; default used", Replace("color.accent = #24589a", "color.accent = blue"));
        yield return new("symbol = ../manifest.cfg", Id, "identity.cfg", "unsafe path: '.' or '..' segment", Replace("symbol = symbols/map-symbol.png", "symbol = ../manifest.cfg"));
        yield return new("symbol = C:/logo.png", Id, "identity.cfg", "unsafe path: drive letter or stream", Replace("symbol = symbols/map-symbol.png", "symbol = C:/logo.png"));
        yield return new("symbol = logo.png", Id, "identity.cfg", "pictures live directly in symbols/", Replace("symbol = symbols/map-symbol.png", "symbol = logo.png"));
        yield return new("symbol = @brand", Id, "identity.cfg", "unknown internal symbol '@brand'", Replace("symbol = symbols/map-symbol.png", "symbol = @brand"));
        yield return new("title.EN = Hello", Id, "identity.cfg", "malformed text key", Replace("title.ENG = Example City", "title.EN = Hello"));
        yield return new("layout = fancy", Id, "identity.cfg", "layout must be classic or openhud; default used", Replace("layout = classic", "layout = fancy"));
        yield return new("a second title line", Id, "identity.cfg", "duplicate key 'title' (the first one counts)", Replace("title.ENG = Example City", "title = Another"));
        yield return new("a line with no equals sign", Id, "identity.cfg", "not a key = value line", Replace("theme = green", "theme green"));
        yield return new("an SVG with a DOCTYPE", "symbols/stop.svg", "identity.cfg", "no <svg> root element", Replace("<svg ", "<!DOCTYPE svg><svg "));
        yield return new("a PNG of 3000 x 10", "", "identity.cfg", "PNG is 3000x10, the limit is 2048x2048", Setup: d => d.Write("openomsi-pausemenu/symbols/map-symbol-green.png", Synthetic.Png(3000, 10)));
        yield return new("a text file named .png", "", "identity.cfg", "not a PNG (signature)", Setup: d => d.Write("openomsi-pausemenu/symbols/map-symbol-green.png", new string('x', 80)));
        yield return new("no FONTS-LICENSE.txt", "", "fonts/", "fonts/FONTS-LICENSE.txt is missing or empty; the system font is used", Setup: d => File.Delete(d.Combine("openomsi-pausemenu/fonts/FONTS-LICENSE.txt")));
        yield return new("a font weight of 1000", "fonts/fonts.cfg", "fonts/fonts.cfg", "weight must be 100..900; the role's default is used", Replace("bold = ExampleSans-Bold.ttf | 700", "bold = ExampleSans-Bold.ttf | 1000"));
        yield return new("a variable font", "", "fonts/fonts.cfg", "variable font (instance it offline)", Setup: d => d.Write("openomsi-pausemenu/fonts/ExampleSans-Bold.ttf", Synthetic.Font(0, "fvar")));
        yield return new("a font that is not in fonts/", "fonts/fonts.cfg", "fonts/fonts.cfg", "font file not found (or beyond the limit)", Replace("bold = ExampleSans-Bold.ttf | 700", "bold = Missing.ttf | 700"));
        yield return new("a strings key with a space", "strings/ENG.cfg", "strings/ENG.cfg", "malformed key", Replace("legend.terminal = Terminal", "legend terminal = Terminal"));
    }

    private static void RejectedEntriesMatchTheReaders()
    {
        var guide = Doc.Read(PauseGuide).Text;
        foreach (var bad in PauseCases())
        {
            using var dir = new TempDir();
            var folder = WritePauseExample(dir, d =>
            {
                if (bad.Edit is not null)
                {
                    var path = d.Combine("openomsi-pausemenu/" + bad.File);
                    File.WriteAllText(path, bad.Edit(File.ReadAllText(path)), new UTF8Encoding(false));
                }
                bad.Setup?.Invoke(d);
            });
            var result = PauseMenuPackageReader.Read(folder, "Example City");
            if (Environment.GetEnvironmentVariable("OPENHUD_DOCS_DUMP") == "1")
                foreach (var d in result.Diagnostics) Console.WriteLine("  [" + bad.Label + "] " + d);
            Expect.True(Expect.Mentions(result.Diagnostics, bad.EntryPart, bad.Reason), $"'{bad.Label}' should log '{bad.Reason}' for '{bad.EntryPart}', the reader said: " + string.Join(" | ", result.Diagnostics));
            Expect.True(guide.Contains(bad.Reason, StringComparison.Ordinal), $"the guide's table must show the reader's text '{bad.Reason}'");
        }


        var manifestCases = new (string Label, string Manifest, string Part, string Reason)[]
        {
            ("format = 2", "format = 2\nname = X\n", "openomsi-pausemenu", "unknown package format '2'"),
            ("no name", "format = 1\n", "openomsi-pausemenu", "manifest has no name"),
        };
        foreach (var (label, manifest, part, reason) in manifestCases)
        {
            using var dir = new TempDir();
            dir.Write("openomsi-pausemenu/manifest.cfg", manifest);
            var result = PauseMenuPackageReader.Read(dir.Combine("openomsi-pausemenu"), "X");
            Expect.True(result.Status == PackageStatus.Ignored && Expect.Mentions(result.Diagnostics, part, reason), label + ": " + string.Join(" | ", result.Diagnostics));
            Expect.True(guide.Contains(reason, StringComparison.Ordinal), "the guide shows '" + reason + "'");
        }

        // The realmap mistakes table.
        var realmapGuide = Doc.Read(RealmapGuide).Text;
        var realmapCases = new (string File, string Content, string Reason)[]
        {
            ("markers.cfg", "marker = icons/terminal.png | 32 | 1200\n", "expected icon | width | x | y | name"),
            ("markers.cfg", "marker = elsewhere/x.png | 30 | 1 | 1\n", "icons are PNG files under icons/"),
            ("markers.cfg", "marker = icons/missing.png | 30 | 1 | 1\n", "icon icons/missing.png: not found"),
            ("regions.cfg", "region = Broken | 1 | 1 | 1 | 1 | 1,1 2,2\n", "polygon needs 3 to 20000 points"),
            ("lines.lin", "RLIN\t1\nL\tH1\tG\tHovercraft\thovercraft\n", "unknown kind of vehicle 'hovercraft'"),
        };
        foreach (var (file, content, reason) in realmapCases)
        {
            using var dir = new TempDir();
            OpenRealmapExample(dir, surface: false);
            dir.Write("openomsi-realmap/" + file, content);
            var p = RealmapPackage.OpenDirectory(dir.Combine("openomsi-realmap"));
            Expect.True(Expect.Mentions(p.Diagnostics, file, reason), $"{file}: '{reason}' expected, the reader said: " + string.Join(" | ", p.Diagnostics));
            Expect.True(realmapGuide.Contains(reason.Replace("|", "\\|"), StringComparison.Ordinal) || realmapGuide.Contains(reason, StringComparison.Ordinal), $"the realmap guide shows '{reason}'");
        }
        using (var dir = new TempDir())
        {
            dir.Write("openomsi-realmap/manifest.cfg", "format = 1\ngrid = 0\n");
            var p = RealmapPackage.OpenDirectory(dir.Combine("openomsi-realmap"));
            Expect.True(p.Status == PackageStatus.Ignored && Expect.Mentions(p.Diagnostics, "openomsi-realmap", "manifest grid is missing or not a plausible tile size"), "grid = 0: " + string.Join(" | ", p.Diagnostics));
        }
    }

    // ------------------------------------------------------------------ settings

    private sealed record Setting(string Key, string Type, string Default, int? Min, int? Max, string[] Choices, int? MaxLength);

    private static List<Setting> TemplateSettings()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Doc.Path("module.template.json")));
        var list = new List<Setting>();
        foreach (var s in json.RootElement.GetProperty("settings").EnumerateArray())
        {
            string Text(string n) => s.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.True ? "true" : v.ValueKind == JsonValueKind.False ? "false" : v.ToString()) : "";
            int? Num(string n) => s.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
            var choices = s.TryGetProperty("choices", out var c) ? c.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
            list.Add(new Setting(Text("key"), Text("type"), Text("default"), Num("min"), Num("max"), choices, Num("max_length")));
        }
        return list;
    }

    private static void SettingsReferenceCoversTheTemplate()
    {
        var doc = Doc.Read("docs/reference/settings.md");
        var settings = TemplateSettings();
        Expect.True(settings.Count >= 9, "the template declares its settings");
        var rows = doc.TableRows(@"^\| `([a-z_]+)` \| ([a-z]+) \| `([^`]*)` \| ([^|]+) \|$");
        foreach (var s in settings)
        {
            var row = rows.FirstOrDefault(r => r[0] == s.Key);
            Expect.True(row is not null, "settings.md has a summary row for '" + s.Key + "'");
            Expect.Equal(s.Type, row![1], s.Key + " type");
            Expect.Equal(s.Default, row[2], s.Key + " default");
            var range = row[3].Trim();
            var expected = s.Type switch
            {
                "integer" => $"{s.Min} to {s.Max}",
                "choice" => string.Join(", ", s.Choices.Select(x => "`" + x + "`")),
                "boolean" => "`true` or `false`",
                _ => $"up to {s.MaxLength} characters",
            };
            Expect.Equal(expected, range, s.Key + " range");
            var section = doc.Section(s.Key);
            Expect.True(section is not null, "settings.md has a section '### " + s.Key + "'");
            Expect.True(section!.Contains("/module-setting:openhud." + s.Key + "=", StringComparison.Ordinal), "the section of '" + s.Key + "' has a command-line example");
        }
        Expect.Equal(settings.Count, rows.Count, "settings.md summary rows against template settings");
    }

    // Every setting shown in a page (command line, modules.ini or session profile) goes through the real parser.
    private static void SettingsExamplesAreAccepted()
    {
        var declared = TemplateSettings().Select(s => s.Key).ToHashSet();
        var total = 0;
        foreach (var file in Doc.AllPages())
        {
            var doc = Doc.Read(file);
            foreach (var block in doc.Blocks)
            {
                var found = new List<(string Key, string Value)>();
                foreach (Match m in Regex.Matches(block.Body, @"/module-setting:openhud\.([a-z0-9_]+)=(""[^""]*""|'[^']*'|[^\s""']+)"))
                    found.Add((m.Groups[1].Value, m.Groups[2].Value.Trim('"', '\'')));
                // [openhud] sections of modules.ini
                var inSection = false;
                foreach (var raw in block.Body.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith('[')) { inSection = line == "[openhud]"; continue; }
                    if (!inSection || line.Length == 0 || line[0] is ';' or '#') continue;
                    var eq = line.IndexOf('=');
                    if (eq > 0 && line[..eq].Trim() != "enabled") found.Add((line[..eq].Trim(), line[(eq + 1)..].Trim()));
                }
                // modules: openhud: settings: of a session-profile preset
                var lines = block.Body.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() != "openhud:") continue;
                    var indent = lines[i].Length - lines[i].TrimStart().Length;
                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var l = lines[j];
                        if (l.Trim().Length == 0) continue;
                        if (l.Length - l.TrimStart().Length <= indent) break;
                        var t = l.Trim();
                        var colon = t.IndexOf(':');
                        if (colon <= 0 || t == "settings:" || t.StartsWith("enabled:", StringComparison.Ordinal)) continue;
                        found.Add((t[..colon].Trim(), t[(colon + 1)..].Trim().Trim('"')));
                    }
                }
                foreach (var (key, value) in found)
                {
                    total++;
                    Expect.True(declared.Contains(key), $"{file}: '{key}' is not a setting of the template");
                    var result = HudSettingsParser.Parse(new Dictionary<string, string> { [key] = value });
                    Expect.True(result.Issues.Count == 0, $"{file}: {key}={value} is not accepted: " + string.Join("; ", result.Issues.Select(i => i.Reason)));
                }
            }
        }
        Expect.True(total >= 20, "the pages show at least 20 setting examples, found " + total);
    }

    private static void UiLanguageKeysMatchTheTable()
    {
        var doc = Doc.Read("docs/reference/ui-language-keys.md");
        if (Environment.GetEnvironmentVariable("OPENHUD_DOCS_DUMP_KEYS") == "1")
        {
            foreach (var k in Strings.Keys) Console.WriteLine("ROW| `" + k + "` | " + Strings.Get(k, UiLanguage.English) + " | " + Strings.Get(k, UiLanguage.PortugueseBr) + " | " + Strings.Get(k, UiLanguage.German) + " |");
            foreach (var e in OpenHud.Pause.PauseStrings.Catalog) Console.WriteLine("MENU| `menu:" + e.Key + "` | " + e.English + " | " + e.Portuguese + " | " + e.German + " |");
        }
        var catalogue = doc.TableRows(@"^\| `menu:(UI_[A-Z0-9_]+)` \| (.*) \| (.*) \| (.*) \|$");
        Expect.Equal(OpenHud.Pause.PauseStrings.Catalog.Count, catalogue.Count, "rows of the pause menu catalogue");
        foreach (var e in OpenHud.Pause.PauseStrings.Catalog)
        {
            var r = catalogue.FirstOrDefault(x => x[0] == e.Key);
            Expect.True(r is not null && r[1].Trim() == e.English && r[2].Trim() == e.Portuguese && r[3].Trim() == e.German, "pause menu catalogue row " + e.Key);
        }
        var rows = doc.TableRows(@"^\| `(UI_[A-Z0-9_]+)` \| (.*) \| (.*) \| (.*) \|$");
        var keys = Strings.Keys.ToList();
        Expect.Equal(keys.Count, rows.Count, "rows of ui-language-keys.md against keys of the table");
        foreach (var key in keys)
        {
            var row = rows.FirstOrDefault(r => r[0] == key);
            Expect.True(row is not null, "ui-language-keys.md lists " + key);
            Expect.Equal(Strings.Get(key, UiLanguage.English), row![1].Trim(), key + " English");
            Expect.Equal(Strings.Get(key, UiLanguage.PortugueseBr), row[2].Trim(), key + " Portuguese");
            Expect.Equal(Strings.Get(key, UiLanguage.German), row[3].Trim(), key + " German");
        }
    }

    // Command lines in the pages use flags OmsiLaunch has (checked by hand against its CLI reference).
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "saved", "last", "map", "entrypoint-index", "plan", "module", "no-module", "module-setting",
        "predefined-profile", "predefined-profile-index", "list", "verify-module", "clear-module-data", "json", "startup-timeout", "runtime", "runtime-arg",
    };

    private static void CommandLinesUseKnownFlags()
    {
        var count = 0;
        foreach (var file in Doc.AllPages())
            foreach (var block in Doc.Read(file).Blocks)
                foreach (var line in block.Body.Split('\n'))
                {
                    if (!line.TrimStart().StartsWith("OmsiLaunch.exe", StringComparison.Ordinal)) continue;
                    count++;
                    foreach (Match m in Regex.Matches(line, @"(?<=\s)/([a-z][a-z-]*)"))
                        Expect.True(KnownFlags.Contains(m.Groups[1].Value), $"{file}: unknown flag /{m.Groups[1].Value} in: {line.Trim()}");
                    foreach (Match m in Regex.Matches(line, @"/module(?:-setting)?:openhud[.=:]?"))
                        count += 0;
                    Expect.True(!Regex.IsMatch(line, @"/openhud-"), $"{file}: the module declares no flags of its own: {line.Trim()}");
                }
        Expect.True(count >= 10, "the pages show at least 10 command lines, found " + count);
    }

    private static void NoUnfinishedMarkers()
    {
        foreach (var file in Doc.AllPages().Concat(new[] { "README.md", "CHANGELOG.md" }))
        {
            var text = Doc.Read(file).Text;
            Expect.True(!text.Contains("[[VERIFY", StringComparison.Ordinal) && !text.Contains("[[LOGTABLE", StringComparison.Ordinal) && !text.Contains("§§", StringComparison.Ordinal), file + " still has an unfinished marker");
        }
    }
}

// A page of the documentation: its text, its fenced blocks, its tables and its sections.
internal sealed class Doc
{
    public sealed record Block(string Lang, string Body, string? Heading);

    public string Text { get; }
    public IReadOnlyList<Block> Blocks { get; }

    private Doc(string text)
    {
        Text = text.Replace("\r\n", "\n");
        var blocks = new List<Block>();
        var lines = Text.Split('\n');
        string? heading = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith('#')) { heading = line.TrimStart('#').Trim(); continue; }
            var fence = Regex.Match(line, @"^```(\w*)\s*$");
            if (!fence.Success) continue;
            var body = new StringBuilder();
            var j = i + 1;
            for (; j < lines.Length && lines[j] != "```"; j++) body.Append(lines[j]).Append('\n');
            blocks.Add(new Block(fence.Groups[1].Value, body.ToString(), heading));
            i = j;
        }
        Blocks = blocks;
    }

    private static string? root;

    // The repository root: the nearest folder above the test program that holds module.template.json.
    public static string Root
    {
        get
        {
            if (root is not null) return root;
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "module.template.json"))) dir = dir.Parent;
                if (dir is not null) return root = dir.FullName;
            }
            throw new InvalidOperationException("the repository root (module.template.json) was not found");
        }
    }

    public static string Path(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static Doc Read(string relative) => new(File.ReadAllText(Path(relative), new UTF8Encoding(false)));

    // docs/**/*.md, relative to the root, with forward slashes.
    public static IEnumerable<string> AllPages() =>
        Directory.EnumerateFiles(Path("docs"), "*.md", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal);

    // The fenced block that follows a heading of the form  #### `prefix/path`  : path -> body.
    public IReadOnlyList<(string Path, string Text)> FileBlocks(string prefix)
    {
        var result = new List<(string, string)>();
        foreach (var block in Blocks)
            if (block.Heading is { } h && h.StartsWith("`" + prefix, StringComparison.Ordinal) && h.EndsWith('`'))
                result.Add((h[(1 + prefix.Length)..^1], block.Body));
        return result;
    }

    // Cells of the table rows that match the pattern (the pattern captures the cells).
    public List<string[]> TableRows(string pattern)
    {
        var rows = new List<string[]>();
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        foreach (var line in Text.Split('\n'))
        {
            var m = regex.Match(line);
            if (m.Success) rows.Add(m.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray());
        }
        return rows;
    }

    // The text of the section "### `key`" up to the next heading of the same or a higher level.
    public string? Section(string key)
    {
        var lines = Text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "### `" + key + "`") continue;
            var sb = new StringBuilder();
            for (var j = i + 1; j < lines.Length && !Regex.IsMatch(lines[j], @"^#{1,3} "); j++) sb.Append(lines[j]).Append('\n');
            return sb.ToString();
        }
        return null;
    }
}
