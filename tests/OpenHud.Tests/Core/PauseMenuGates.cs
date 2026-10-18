using System.Collections.Immutable;
using System.Text;
using OpenHud.Packages;
using OpenHud.Packages.PauseMenu;

namespace OpenHud.Tests.Core;

internal static class PauseMenuGates
{
    public static void Register()
    {
        Gates.Run("pausepkg.generic-defaults-without-package", GenericDefaults);
        Gates.Run("pausepkg.manifest-rules", ManifestRules);
        Gates.Run("pausepkg.full-package", FullPackage);
        Gates.Run("pausepkg.language-suffixed-resolution", LanguageResolution);
        Gates.Run("pausepkg.path-safety", PathSafety);
        Gates.Run("pausepkg.links-cannot-escape", LinkEscape);
        Gates.Run("pausepkg.size-limits", SizeLimits);
        Gates.Run("pausepkg.picture-validation", PictureValidation);
        Gates.Run("pausepkg.hostile-text", HostileText);
        Gates.Run("pausepkg.fonts", Fonts);
        Gates.Run("pausepkg.strings-folder", StringsFolder);
        Gates.Run("pausepkg.fingerprint-is-a-cache-key", Fingerprint);
        Gates.Run("pausepkg.identity-is-immutable", Immutable);
    }

    private const string Manifest = "format = 1\nname = Test pause menu\nauthor = Test author\ncredit = Test credit\n";

    private static TempDir Package(string manifest = Manifest)
    {
        var dir = new TempDir();
        if (manifest.Length > 0) dir.Write("manifest.cfg", manifest);
        return dir;
    }

    private static PauseMenuReadResult Read(TempDir dir, string map = "Test Map") => PauseMenuPackageReader.Read(dir.Path, map);

    private static void AssertGeneric(PauseMenuIdentity id, string map)
    {
        Expect.Equal(map, id.Title("PTB"), "default title is the map folder");
        Expect.Equal(SymbolKind.None, id.Symbol.Kind, "no map symbol");
        Expect.True(id.Omsi2Mark.Kind == SymbolKind.Internal && id.Omsi2Mark.Name == "omsi2", "generic @omsi2 mark");
        Expect.Equal("blue", id.Theme, "default theme");
        Expect.Equal(PauseLayout.Classic, id.Layout, "default layout");
        Expect.True(id.StopIcon.Kind == SymbolKind.Internal && id.StopIcon.Name == "simple", "default stop icon @simple");
        Expect.True(!id.Fonts.HasFonts && id.Fonts.ForRole("regular") is null, "system font");
        Expect.True(id.LineColor is null && id.BrtColor is null && id.AccentColor is null, "theme colours");
        Expect.True(id.Legend("station", "ENG") is null, "internal legend");
        Expect.True(id.OpenOmsiMark.Kind == SymbolKind.None, "openomsi mark is @none");
    }

    private static void GenericDefaults()
    {
        var absent = PauseMenuPackageReader.Read(null, "Some Map");
        Expect.Equal(PackageStatus.Absent, absent.Status, "null path");
        AssertGeneric(absent.Identity, "Some Map");
        Expect.True(!absent.Identity.PackageUsed && absent.Identity.Credit is null, "package not used, no credit");
        var missing = PauseMenuPackageReader.Read(Path.Combine(Path.GetTempPath(), "ohud-does-not-exist-" + Guid.NewGuid().ToString("N")), "Some Map");
        Expect.Equal(PackageStatus.Absent, missing.Status, "missing folder");
        AssertGeneric(missing.Identity, "Some Map");
        using var empty = Package("");
        var noManifest = Read(empty, "Some Map");
        Expect.Equal(PackageStatus.Absent, noManifest.Status, "no manifest.cfg: the package does not count");
        AssertGeneric(noManifest.Identity, "Some Map");
        using var manifestOnly = Package();
        var minimal = Read(manifestOnly, "Some Map");
        Expect.Equal(PackageStatus.Loaded, minimal.Status, "a manifest alone makes a package");
        AssertGeneric(minimal.Identity, "Some Map");
        Expect.Equal("Test credit", minimal.Identity.Credit, "credit comes from the manifest");
        Expect.Equal(PauseMenuIdentity.Generic("x").MapFolder, "x", "Generic carries the folder");
    }

    private static void ManifestRules()
    {
        foreach (var (text, why) in new[]
        {
            ("format = 2\nname = X\n", "unknown format"),
            ("format = abc\nname = X\n", "non-numeric format"),
            ("name = X\n", "missing format"),
            ("format = 1\n", "missing name"),
            ("format = 1\nname =    \n", "blank name"),
            ("format = -1\nname = X\n", "negative format"),
        })
        {
            using var dir = Package(text);
            var r = Read(dir);
            Expect.Equal(PackageStatus.Ignored, r.Status, why + " ignores the package");
            Expect.True(!r.Identity.PackageUsed, why + ": not used");
            AssertGeneric(r.Identity, "Test Map");
            Expect.True(r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Ignored), why + " is logged");
        }
        // Unknown keys are ignored, comments and blank lines are fine.
        using var ok = Package("# comment\n\nformat = 1\nname = X\nfuture_key = whatever\n");
        Expect.Equal(PackageStatus.Loaded, Read(ok).Status, "unknown manifest keys are ignored");
        // A manifest of 64 KiB + 1 is too large to be a manifest.
        using var big = Package("format = 1\nname = X\n#" + new string('x', 70_000));
        Expect.Equal(PackageStatus.Ignored, Read(big).Status, "oversized manifest");
    }

    private static TempDir FullTree()
    {
        var dir = Package();
        dir.Write("identity.cfg",
            "format = 1\n" +
            "title = Test Map Menu\ntitle.ENG = Test Map Menu (EN)\ntitle.PTB = Menu do Mapa Teste\n" +
            "symbol = symbols/mark.png\nsymbol.green = @simple\nsymbol.dark = symbols/dark.svg\nsymbol.openomsi = symbols/open.png\n" +
            "theme = Green\nlayout = openhud\n" +
            "color.line = #5588C7\ncolor.brt = #1e7b34\ncolor.accent = #24589a80\ncolor.group.exp = #aa0000\n" +
            "stop_icon = @omsi2\nmarker_icon.station = @simple\nmarker_icon.garage = symbols/mark.png\nmode_icon.tram = @simple\nmode_icon.hovercraft = @simple\n" +
            "legend.station = Station text\nlegend.station.ENG = Station (EN)\nlegend.station.PTB = Estacao\n" +
            "some_future_key = 1\n");
        dir.Write("symbols/mark.png", Synthetic.Png(64, 64));
        dir.Write("symbols/mark@2x.png", Synthetic.Png(128, 128));
        dir.Write("symbols/dark.svg", Synthetic.Svg());
        dir.Write("symbols/open.png", Synthetic.Png(32, 32));
        dir.Write("symbols/omsi2-logo.png", Synthetic.Png(200, 60));
        dir.Write("symbols/openomsi.png", Synthetic.Png(48, 48));
        dir.Write("fonts/Regular.ttf", Synthetic.Font());
        dir.Write("fonts/Bold.ttf", Synthetic.Font(padding: 32));
        dir.Write("fonts/FONTS-LICENSE.txt", "Test licence: redistribution allowed in this package.");
        dir.Write("fonts/fonts.cfg", "format = 1\n# role = file | weight | note\nregular = Regular.ttf | 500 | body\nbold = Bold.ttf | 700\nblack = Bold.ttf | 900\nweird = Bold.ttf\n");
        dir.Write("strings/PTB.cfg", "title = Menu PTB (arquivo)\nlegend.garage = Garagem do mapa\n");
        dir.Write("LICENSE.txt", "licence");
        return dir;
    }

    private static void FullPackage()
    {
        using var dir = FullTree();
        var r = Read(dir);
        var id = r.Identity;
        Expect.Equal(PackageStatus.Loaded, r.Status, "loaded");
        Expect.True(id.PackageUsed && id.PackageName == "Test pause menu" && id.Author == "Test author" && id.Credit == "Test credit", "manifest fields");
        Expect.Equal("Test Map Menu", id.Title(null), "title");
        Expect.Equal(SymbolKind.Picture, id.Symbol.Kind, "symbol is a picture");
        Expect.True(id.Symbol.Format == PictureFormat.Png && id.Symbol.Width == 64 && id.Symbol.Height == 64, "png dimensions come from the header");
        Expect.True(id.Symbol.FilePath is not null && File.Exists(id.Symbol.FilePath) && id.Symbol.FilePath2x is not null && id.Symbol.FilePath2x.EndsWith("mark@2x.png"), "@2x sibling found");
        Expect.True(id.ThemeSymbols["green"].Name == "simple" && id.ThemeSymbols["dark"].Format == PictureFormat.Svg, "theme symbols");
        Expect.Equal("simple", id.SymbolFor("green", PauseLayout.Classic).Name, "classic layout uses the theme symbol");
        Expect.Equal("symbols/open.png", id.SymbolFor("green", PauseLayout.OpenHud).Name, "openHUD layout uses symbol.openomsi");
        Expect.Equal("symbols/mark.png", id.SymbolFor("unknown-theme", PauseLayout.Classic).Name, "unknown theme falls back to symbol");
        Expect.Equal("green", id.Theme, "theme is normalised to lower case");
        Expect.Equal(PauseLayout.OpenHud, id.Layout, "layout");
        Expect.True(id.LineColor == new HudColor(0x55, 0x88, 0xC7) && id.BrtColor == new HudColor(0x1e, 0x7b, 0x34) && id.AccentColor == new HudColor(0x24, 0x58, 0x9a, 0x80), "colours");
        Expect.Equal("#24589a80", id.AccentColor!.Value.ToHex(), "colour round trip");
        Expect.True(id.GroupColors["exp"] == new HudColor(0xaa, 0, 0), "group colour");
        Expect.True(id.StopIcon.Name == "omsi2" && id.MarkerIcons["station"].Name == "simple" && id.MarkerIcons["garage"].IsPicture && id.ModeIcons["tram"].Name == "simple", "icons");
        Expect.True(!id.ModeIcons.ContainsKey("hovercraft"), "unknown mode ignored");
        Expect.Equal("symbols/omsi2-logo.png", id.Omsi2Mark.Name, "OMSI 2 logo comes from the package file");
        Expect.Equal("symbols/openomsi.png", id.OpenOmsiMark.Name, "openomsi mark");
        Expect.True(id.Fonts.HasFonts && id.Fonts.ForRole("regular")!.Weight == 500 && id.Fonts.ForRole("regular")!.Note == "body", "explicit font role");
        Expect.True(!id.Fonts.ForRole("bold")!.Substituted && id.Fonts.ForRole("black")!.Weight == 900, "explicit bold and black");
        Expect.True(id.Fonts.ForRole("medium")!.Substituted && id.Fonts.ForRole("medium")!.RelativePath == "fonts/Regular.ttf", "medium takes the nearest weight (500 -> regular, tie rules aside)");
        Expect.True(id.Fonts.ForRole("condensed")!.Substituted && id.Fonts.ForRole("condensed")!.Weight == 700, "condensed takes bold");
        Expect.True(!Expect_Mentions(r, "weird"), "unknown font role is silently ignored");
        Expect.True(id.Fingerprint.Length == 64, "fingerprint is a SHA-256 hex");
        Expect.True(Expect_NoDropped(r), "a clean package logs nothing dropped: " + string.Join("; ", r.Diagnostics));
    }

    private static bool Expect_Mentions(PauseMenuReadResult r, string text) => r.Diagnostics.Any(d => d.Entry.Contains(text) || d.Reason.Contains(text));
    private static bool Expect_NoDropped(PauseMenuReadResult r) => r.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Info);

    private static void LanguageResolution()
    {
        using var dir = FullTree();
        var id = Read(dir).Identity;
        // strings/<LANG>.cfg -> identity <key>.<LANG> -> identity <key> -> built-in (null)
        Expect.Equal("Menu PTB (arquivo)", id.Title("PTB"), "strings file wins over identity.cfg");
        Expect.Equal("Menu PTB (arquivo)", id.Title("ptb"), "language code is case-insensitive");
        Expect.Equal("Test Map Menu (EN)", id.Title("ENG"), "identity language key");
        Expect.Equal("Test Map Menu", id.Title("DEU"), "plain identity key");
        Expect.Equal("Test Map Menu", id.Title(null), "no language");
        Expect.Equal("Garagem do mapa", id.Legend("garage", "PTB"), "legend from strings file");
        Expect.Equal("Estacao", id.Legend("station", "PTB"), "legend language key");
        Expect.Equal("Station text", id.Legend("station", "DEU"), "legend plain key");
        Expect.Equal(null, id.Legend("terminal", "PTB"), "no package text: built-in is used");
        using var bare = Package();
        Expect.Equal("Test Map", Read(bare).Identity.Title("PTB"), "no title key: the map folder name");
    }

    private static void PathSafety()
    {
        using var outside = new TempDir();
        outside.Write("secret.png", Synthetic.Png(10, 10));
        using var dir = Package();
        dir.Write("symbols/good.png", Synthetic.Png(10, 10));
        var hostile = new[]
        {
            "symbols/../../secret.png", "../secret.png", "symbols/..", "symbols/sub/../good.png", "/symbols/good.png", "\\symbols\\good.png", "C:/Windows/x.png", "C:\\x.png",
            "symbols\\good.png", "symbols//good.png", "symbols/good.png:stream", "symbols/CON.png", "symbols/good.png.", "symbols/./good.png",
            "symbols/ma?.png", "good.png", "symbols/sub/good.png", outside.Path.Replace('\\', '/') + "/secret.png", "symbols/\u0001.png", "",
        };
        var lines = new StringBuilder("format = 1\n");
        for (var i = 0; i < hostile.Length; i++) lines.Append("marker_icon.k").Append(i).Append(" = ").Append(hostile[i]).Append('\n');
        lines.Append("marker_icon.fine = symbols/good.png\ntitle = Still here\n");
        dir.Write("identity.cfg", lines.ToString());
        var r = Read(dir);
        Expect.Equal(PackageStatus.Loaded, r.Status, "the package survives hostile entries");
        for (var i = 0; i < hostile.Length; i++)
            Expect.True(!r.Identity.MarkerIcons.ContainsKey("k" + i), "hostile path was dropped: '" + hostile[i] + "'");
        Expect.True(r.Identity.MarkerIcons.ContainsKey("fine"), "a good entry next to bad ones survives");
        Expect.Equal("Still here", r.Identity.Title(null), "the rest of the identity survives");
        Expect.True(r.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Dropped) >= hostile.Length - 1, "every dropped entry has a reason");
        // The path rule itself.
        Expect.True(PackageFolder.UnsafeReason("symbols/a.png") is null, "a plain relative path is fine");
        foreach (var bad in new[] { "..", "a/..", "a/../b", "/a", "a\\b", "c:/a", "a/", "nul", "a/aux.txt", "a/b ", new string('a', 300) })
            Expect.True(PackageFolder.UnsafeReason(bad) is not null, "unsafe: '" + bad + "'");
    }

    private static void LinkEscape()
    {
        using var outside = new TempDir();
        outside.Write("secret.png", Synthetic.Png(10, 10));
        outside.Write("evil/inner.png", Synthetic.Png(10, 10));
        using var dir = Package();
        dir.Write("symbols/good.png", Synthetic.Png(10, 10));
        Directory.CreateDirectory(dir.Combine("symbols/sub"));
        Expect.True(TempDir.MakeJunction(dir.Combine("symbols/escape"), outside.Path), "the gate could not create a junction");
        dir.Write("identity.cfg", "format = 1\nsymbol = symbols/escape/secret.png\nstop_icon = symbols/good.png\n");
        var r = Read(dir);
        Expect.Equal(SymbolKind.None, r.Identity.Symbol.Kind, "a file reached through a junction out of the folder is refused");
        Expect.True(r.Identity.StopIcon.IsPicture, "the good file is still used");
        // The whole symbols folder replaced by a junction: nothing is read from it.
        using var dir2 = Package();
        Expect.True(TempDir.MakeJunction(dir2.Combine("symbols"), outside.Path), "junction for symbols/");
        dir2.Write("identity.cfg", "format = 1\nsymbol = symbols/secret.png\n");
        var r2 = Read(dir2);
        Expect.Equal(SymbolKind.None, r2.Identity.Symbol.Kind, "symbols/ that is a link yields nothing");
        // The package root itself being a link is refused.
        var linkedRoot = outside.Combine("linked-package");
        Expect.True(TempDir.MakeJunction(linkedRoot, dir.Path), "junction for the package root");
        var r3 = PauseMenuPackageReader.Read(linkedRoot, "Map");
        Expect.Equal(PackageStatus.Absent, r3.Status, "a package folder that is a link is not opened");
        Directory.Delete(linkedRoot, false);
    }

    private static void SizeLimits()
    {
        // Picture sides and bytes.
        using var dir = Package();
        dir.Write("symbols/ok2048.png", Synthetic.Png(2048, 2048));
        dir.Write("symbols/wide.png", Synthetic.Png(2049, 10));
        dir.Write("symbols/zero.png", Synthetic.Png(0, 10));
        dir.Write("symbols/heavy.png", Synthetic.Png(10, 10, padding: 2 * 1024 * 1024));
        dir.Write("symbols/almost.png", Synthetic.Png(10, 10, padding: 2 * 1024 * 1024 - 100));
        dir.Write("symbols/big.svg", Synthetic.Svg("<!-- " + new string('x', 300_000) + " -->"));
        dir.Write("symbols/edge.svg", Synthetic.Svg("<!-- " + new string('x', 200_000) + " -->"));
        dir.Write("identity.cfg", "format = 1\nmarker_icon.a = symbols/ok2048.png\nmarker_icon.b = symbols/wide.png\nmarker_icon.c = symbols/zero.png\nmarker_icon.d = symbols/heavy.png\nmarker_icon.e = symbols/almost.png\nmarker_icon.f = symbols/big.svg\nmarker_icon.g = symbols/edge.svg\n");
        var m = Read(dir).Identity.MarkerIcons;
        Expect.True(m.ContainsKey("a") && m["a"].Width == 2048, "2048x2048 is allowed");
        Expect.True(!m.ContainsKey("b") && !m.ContainsKey("c"), "2049 px and 0 px sides refused");
        Expect.True(!m.ContainsKey("d"), "over 2 MiB refused");
        Expect.True(m.ContainsKey("e"), "just under 2 MiB accepted");
        Expect.True(!m.ContainsKey("f") && m.ContainsKey("g"), "SVG limit is 256 KiB");

        // More than 64 files: later names fall outside the folder limit.
        using var many = Package();
        for (var i = 0; i < 70; i++) many.Write($"symbols/s{i:00}.png", Synthetic.Png(8, 8));
        many.Write("identity.cfg", "format = 1\nmarker_icon.first = symbols/s00.png\nmarker_icon.last = symbols/s69.png\nmarker_icon.at64 = symbols/s63.png\nmarker_icon.at65 = symbols/s64.png\n");
        var rm = Read(many);
        Expect.True(rm.Identity.MarkerIcons.ContainsKey("first") && rm.Identity.MarkerIcons.ContainsKey("at64"), "the first 64 files count");
        Expect.True(!rm.Identity.MarkerIcons.ContainsKey("last") && !rm.Identity.MarkerIcons.ContainsKey("at65"), "files beyond 64 are dropped");
        Expect.True(Expect.Mentions(rm.Diagnostics, "symbols/s69.png", "limit"), "the drop is explained");

        // More than 16 MiB in the folder.
        using var heavy = Package();
        for (var i = 0; i < 9; i++) heavy.Write($"symbols/h{i}.png", Synthetic.Png(8, 8, padding: 2 * 1024 * 1024 - 200));
        heavy.Write("identity.cfg", "format = 1\nmarker_icon.a = symbols/h0.png\nmarker_icon.z = symbols/h8.png\n");
        var rh = Read(heavy).Identity.MarkerIcons;
        Expect.True(rh.ContainsKey("a") && !rh.ContainsKey("z"), "the folder byte limit drops the files past 16 MiB");

        // A text file past 64 KiB is refused; the package stays.
        using var cfg = Package();
        cfg.Write("identity.cfg", "format = 1\ntitle = Big\n#" + new string('x', 70_000));
        var rc = Read(cfg);
        Expect.True(rc.Status == PackageStatus.Loaded && rc.Identity.Title(null) == "Test Map", "an oversized identity.cfg is dropped as a whole");
        Expect.True(Expect.Mentions(rc.Diagnostics, "identity.cfg"), "and says so");
    }

    private static void PictureValidation()
    {
        using var dir = Package();
        var good = Synthetic.Png(16, 16);
        var badCrc = (byte[])good.Clone(); badCrc[29] ^= 0xFF;
        var badSig = (byte[])good.Clone(); badSig[1] = (byte)'X';
        var badDepth = Synthetic.Png(16, 16, colorType: 6, depth: 4);
        var badType = Synthetic.Png(16, 16, colorType: 5);
        var palette = Synthetic.Png(16, 16, colorType: 3, depth: 4);
        dir.Write("symbols/good.png", good);
        dir.Write("symbols/crc.png", badCrc);
        dir.Write("symbols/sig.png", badSig);
        dir.Write("symbols/depth.png", badDepth);
        dir.Write("symbols/type.png", badType);
        dir.Write("symbols/palette.png", palette);
        dir.Write("symbols/short.png", good[..20]);
        dir.Write("symbols/empty.png", new byte[0]);
        dir.Write("symbols/text.png", "not a png at all, just text that is long enough to pass a length check of some kind....");
        dir.Write("symbols/photo.jpg", good);
        dir.Write("symbols/ok.svg", Synthetic.Svg());
        dir.Write("symbols/script.svg", Synthetic.Svg("<script>alert(1)</script>"));
        dir.Write("symbols/ext.svg", Synthetic.Svg("<use href=\"http://example.com/a.svg#x\"/>"));
        dir.Write("symbols/ext2.svg", Synthetic.Svg("<rect fill=\"url(http://example.com/g)\"/>"));
        dir.Write("symbols/inner-ref.svg", Synthetic.Svg("<defs><linearGradient id=\"g\"/></defs><rect fill=\"url(#g)\" width=\"5\" height=\"5\"/>"));
        dir.Write("symbols/event.svg", Synthetic.Svg("<rect onload=\"x()\" width=\"1\" height=\"1\"/>"));
        dir.Write("symbols/img.svg", Synthetic.Svg("<image href=\"data:image/png;base64,AAAA\"/>"));
        dir.Write("symbols/entity.svg", "<!DOCTYPE svg [<!ENTITY a \"b\">]><svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        dir.Write("symbols/notsvg.svg", "<html><body>hi</body></html>");
        dir.Write("symbols/badutf.svg", new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E, 0xFF, 0xFE, 0x3C, 0x2F, 0x73, 0x76, 0x67, 0x3E });
        var names = new[] { "good.png", "crc.png", "sig.png", "depth.png", "type.png", "palette.png", "short.png", "empty.png", "text.png", "photo.jpg", "ok.svg", "script.svg", "ext.svg", "ext2.svg", "inner-ref.svg", "event.svg", "img.svg", "entity.svg", "notsvg.svg", "badutf.svg", "missing.png" };
        var sb = new StringBuilder("format = 1\n");
        foreach (var n in names) sb.Append("marker_icon.").Append(n.Replace('.', '_').Replace('-', '_')).Append(" = symbols/").Append(n).Append('\n');
        dir.Write("identity.cfg", sb.ToString());
        var icons = Read(dir).Identity.MarkerIcons;
        var accepted = new HashSet<string> { "good.png", "palette.png", "ok.svg", "inner-ref.svg" };
        foreach (var n in names)
            Expect.Equal(accepted.Contains(n), icons.ContainsKey(n.Replace('.', '_').Replace('-', '_')), "picture " + n + (accepted.Contains(n) ? " accepted" : " refused"));
    }

    private static void HostileText()
    {
        // BOM + CRLF + lone CR + tabs + trailing spaces + inline comments are all fine.
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        using var dir = Package();
        dir.Write("manifest.cfg", bom.Concat(Encoding.UTF8.GetBytes("format = 1\r\nname = Bom\r\n")).ToArray());
        dir.Write("identity.cfg", bom.Concat(Encoding.UTF8.GetBytes("format = 1\r\ntitle\t=\tCRLF title   \rtheme = dark   # trailing note\r\ncolor.line = #112233 # a colour\rlegend.station = Caf\u00e9 \u2013 \u65e5\u672c\n")).ToArray());
        var id = Read(dir).Identity;
        Expect.True(id.PackageUsed && id.PackageName == "Bom", "BOM and CRLF manifest");
        Expect.Equal("CRLF title", id.Title(null), "CRLF/tab/trailing spaces");
        Expect.Equal("dark", id.Theme, "lone CR separates lines; inline comment stripped");
        Expect.True(id.LineColor == new HudColor(0x11, 0x22, 0x33), "colour survives an inline comment");
        Expect.Equal("Caf\u00e9 \u2013 \u65e5\u672c", id.Legend("station", null), "non-ASCII text round-trips");

        // Invalid UTF-8 in identity.cfg: that file is dropped, the package and the generic defaults remain.
        using var bad = Package();
        bad.Write("identity.cfg", new byte[] { 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x3D, 0x31, 0x0A, 0x74, 0x69, 0x74, 0x6C, 0x65, 0x3D, 0xC3, 0x28, 0x0A });
        var rb = Read(bad);
        Expect.True(rb.Status == PackageStatus.Loaded && rb.Identity.Title(null) == "Test Map", "invalid UTF-8 drops identity.cfg only");
        Expect.True(Expect.Mentions(rb.Diagnostics, "identity.cfg", "UTF-8"), "reason is logged");
        // A UTF-16 file (BOM FF FE) is not UTF-8.
        using var u16 = Package();
        u16.Write("identity.cfg", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("format = 1\ntitle = X\n")).ToArray());
        Expect.Equal("Test Map", Read(u16).Identity.Title(null), "UTF-16 identity is refused");
        // NUL inside.
        using var nul = Package();
        nul.Write("identity.cfg", "format = 1\ntitle = A\0B\n");
        Expect.Equal("Test Map", Read(nul).Identity.Title(null), "NUL in a file refuses it");
        // Duplicate keys: the first counts and the duplicate is logged.
        using var dup = Package();
        dup.Write("identity.cfg", "format = 1\ntitle = First\ntitle = Second\nTITLE = Third\n");
        var rd = Read(dup);
        Expect.Equal("First", rd.Identity.Title(null), "first duplicate wins");
        Expect.True(rd.Diagnostics.Count(d => d.Reason.Contains("duplicate")) == 2, "duplicates are logged");
        // Garbage lines, bad values, unknown format of the identity file.
        using var junk = Package();
        junk.Write("identity.cfg", "format = 1\nthis line has no equals\n= no key\ntheme = Not A Theme!\nlayout = sideways\ncolor.line = red\ncolor.accent = #12\ntitle.TOOLONG = x\ntitle = " + new string('y', 600) + "\nlegend.a.b.c = x\nstop_icon = @nonexistent\nsymbol = @none\n");
        var rj = Read(junk);
        var ij = rj.Identity;
        Expect.True(rj.Status == PackageStatus.Loaded && ij.Theme == "blue" && ij.Layout == PauseLayout.Classic, "bad theme/layout fall back");
        Expect.True(ij.LineColor is null && ij.AccentColor is null, "bad colours ignored");
        Expect.True(ij.Title(null) == "Test Map" && ij.Text("title.TOOLONG", null) is null, "bad text keys and over-long texts dropped");
        Expect.True(ij.StopIcon.Name == "simple" && ij.Symbol.Kind == SymbolKind.None, "unknown internal symbol keeps the default; @none is none");
        using var fmt = Package();
        fmt.Write("identity.cfg", "format = 7\ntitle = Future\n");
        var rf = Read(fmt);
        Expect.True(rf.Status == PackageStatus.Loaded && rf.Identity.Title(null) == "Test Map", "an identity.cfg with an unknown format gives the defaults");
        // 20 000+ lines do not hang or throw.
        using var lines = Package();
        lines.Write("identity.cfg", "format = 1\n" + string.Concat(Enumerable.Range(0, 30_000).Select(i => "k" + i + " = v\n")) + "title = late\n");
        Expect.True(Read(lines).Status == PackageStatus.Loaded, "very long files are bounded");
    }

    private static void Fonts()
    {
        // No licence: not loaded.
        using var nolicence = Package();
        nolicence.Write("fonts/A.ttf", Synthetic.Font());
        nolicence.Write("fonts/fonts.cfg", "format = 1\nregular = A.ttf | 400\n");
        var r1 = Read(nolicence);
        Expect.True(!r1.Identity.Fonts.HasFonts && r1.Identity.Fonts.Status.Contains("LICENSE"), "fonts without FONTS-LICENSE.txt are not loaded");
        Expect.True(Expect.Mentions(r1.Diagnostics, "fonts/", "system font"), "and the log says the system font is used");
        nolicence.Write("fonts/FONTS-LICENSE.txt", "");
        Expect.True(!Read(nolicence).Identity.Fonts.HasFonts, "an empty licence file does not count");

        // No fonts.cfg.
        using var nocfg = Package();
        nocfg.Write("fonts/A.ttf", Synthetic.Font());
        nocfg.Write("fonts/FONTS-LICENSE.txt", "licence");
        Expect.True(!Read(nocfg).Identity.Fonts.HasFonts, "fonts.cfg is required when fonts/ has a font");

        // Bad files.
        using var dir = Package();
        dir.Write("fonts/FONTS-LICENSE.txt", "licence");
        dir.Write("fonts/ok.ttf", Synthetic.Font());
        dir.Write("fonts/var.ttf", Synthetic.Font(0, "fvar"));
        dir.Write("fonts/ttc.ttf", Encoding.ASCII.GetBytes("ttcf").Concat(new byte[200]).ToArray());
        dir.Write("fonts/woff.otf", Encoding.ASCII.GetBytes("wOFF").Concat(new byte[200]).ToArray());
        dir.Write("fonts/huge.ttf", Synthetic.Font(padding: 4 * 1024 * 1024 + 10));
        dir.Write("fonts/fonts.cfg", "format = 1\nregular = ok.ttf | 400\nmedium = var.ttf\nbold = ttc.ttf\nblack = woff.otf\ncondensed = huge.ttf\n");
        var rf = Read(dir);
        Expect.True(rf.Identity.Fonts.HasFonts && rf.Identity.Fonts.ForRole("regular")!.RelativePath == "fonts/ok.ttf", "the good font loads");
        // Everything else takes the nearest weight (the only face), never a bad file.
        foreach (var role in PauseMenuIdentity.FontRoles) Expect.Equal("fonts/ok.ttf", rf.Identity.Fonts.ForRole(role)!.RelativePath, role + " resolves to the only valid face");
        Expect.True(Expect.Mentions(rf.Diagnostics, "medium", "variable") && Expect.Mentions(rf.Diagnostics, "bold") && Expect.Mentions(rf.Diagnostics, "black") && Expect.Mentions(rf.Diagnostics, "condensed", "larger"), "each bad font is explained");

        // Path tricks and weights.
        using var paths = Package();
        paths.Write("fonts/FONTS-LICENSE.txt", "licence");
        paths.Write("fonts/ok.ttf", Synthetic.Font());
        paths.Write("fonts/fonts.cfg", "format = 1\nregular = ok.ttf | 5000 | note\nbold = ../ok.ttf\nblack = sub/ok.ttf\nmedium = C:/ok.ttf\nregular = ok.ttf | 700\n");
        var rp = Read(paths);
        Expect.Equal(400, rp.Identity.Fonts.ForRole("regular")!.Weight, "an invalid weight takes the role's default");
        Expect.True(Expect.Mentions(rp.Diagnostics, "bold", "unsafe") && Expect.Mentions(rp.Diagnostics, "medium", "unsafe") && Expect.Mentions(rp.Diagnostics, "black", "directly"), "path tricks in fonts.cfg are dropped");

        // Unknown fonts format ignores the fonts, not the package.
        using var fmt = Package();
        fmt.Write("fonts/FONTS-LICENSE.txt", "licence");
        fmt.Write("fonts/ok.ttf", Synthetic.Font());
        fmt.Write("fonts/fonts.cfg", "format = 9\nregular = ok.ttf\n");
        var rfm = Read(fmt);
        Expect.True(rfm.Status == PackageStatus.Loaded && !rfm.Identity.Fonts.HasFonts, "unknown fonts format");

        // More than 12 font files: the extra ones cannot be named.
        using var many = Package();
        many.Write("fonts/FONTS-LICENSE.txt", "licence");
        for (var i = 0; i < 14; i++) many.Write($"fonts/f{i:00}.ttf", Synthetic.Font());
        many.Write("fonts/fonts.cfg", "format = 1\nregular = f00.ttf\nbold = f13.ttf\n");
        var rm = Read(many).Identity.Fonts;
        Expect.True(rm.ForRole("regular")!.RelativePath == "fonts/f00.ttf" && rm.ForRole("bold")!.RelativePath == "fonts/f00.ttf", "a font past the 12th file is dropped and the role substituted");
        // A font folder without any font file is simply "no fonts".
        using var none = Package();
        none.Write("fonts/readme.txt", "nothing");
        Expect.True(!Read(none).Identity.Fonts.HasFonts && Read(none).Diagnostics.Count == 0, "no font files: nothing to say");
    }

    private static void StringsFolder()
    {
        using var dir = Package();
        dir.Write("strings/DEU.cfg", "format = 1\ntitle = Deutscher Titel\nlegend.garage = Betriebshof\nBad Key! = x\nempty =\n");
        dir.Write("strings/ptb.cfg", "title = Titulo\n");
        dir.Write("strings/english.cfg", "title = ignored\n");
        dir.Write("strings/EN.cfg", "title = ignored\n");
        dir.Write("strings/ENG.txt", "title = ignored\n");
        dir.Write("strings/XYZ.cfg", new byte[] { 0xFF, 0xFE, 0xFD });
        var r = Read(dir);
        var id = r.Identity;
        Expect.Equal("Deutscher Titel", id.Title("DEU"), "DEU.cfg");
        Expect.Equal("Titulo", id.Title("PTB"), "file names are case-insensitive, codes normalised");
        Expect.Equal("Test Map", id.Title("ENG"), "wrong file names are not strings files");
        Expect.Equal("Betriebshof", id.Legend("garage", "DEU"), "legend text from strings");
        Expect.True(id.StringFiles.Keys.OrderBy(x => x).SequenceEqual(new[] { "DEU", "PTB" }), "only valid files were read");
        Expect.True(Expect.Mentions(r.Diagnostics, "XYZ.cfg", "UTF-8"), "a broken strings file is dropped with a reason");
    }

    private static void Fingerprint()
    {
        using var dir = FullTree();
        var a = Read(dir).Identity.Fingerprint;
        var again = Read(dir).Identity.Fingerprint;
        Expect.Equal(a, again, "stable while nothing changes");
        File.AppendAllText(dir.Combine("identity.cfg"), "# touched\n");
        var b = Read(dir).Identity.Fingerprint;
        Expect.True(a != b, "changes when a file changes");
        dir.Write("symbols/new.png", Synthetic.Png(4, 4));
        Expect.True(Read(dir).Identity.Fingerprint != b, "changes when a file is added");
        // The fingerprint is never used for decisions: two packages with equal content in different folders read the same.
        using var one = FullTree();
        using var two = FullTree();
        Expect.Equal(Read(one).Identity.Title("PTB"), Read(two).Identity.Title("PTB"), "decisions do not depend on the fingerprint");
    }

    private static void Immutable()
    {
        using var dir = FullTree();
        var id = Read(dir).Identity;
        Expect.True(id.Texts is ImmutableDictionary<string, string> && id.MarkerIcons is ImmutableDictionary<string, SymbolRef> && id.StringFiles is ImmutableDictionary<string, ImmutableDictionary<string, string>>, "collections are immutable types");
        Expect.True(id.GroupColors is ImmutableDictionary<string, HudColor> && id.ThemeSymbols is ImmutableDictionary<string, SymbolRef> && id.ModeIcons is ImmutableDictionary<string, SymbolRef>, "all dictionaries are immutable");
        var copy = id with { Theme = "x" };
        Expect.True(copy.Theme == "x" && id.Theme == "green", "changing a copy does not change the snapshot");
    }
}
