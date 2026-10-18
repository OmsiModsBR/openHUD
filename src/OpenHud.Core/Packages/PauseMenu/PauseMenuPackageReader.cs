using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenHud.Packages.PauseMenu;

// Reads an openomsi-pausemenu package (OPENOMSI-PAUSEMENU-PACKAGE-SPEC-001) into an immutable
// PauseMenuIdentity. Read-only, never throws, bad entries are dropped one by one with a reason,
// an unknown format ignores the whole package, and a missing piece takes the generic default.
public static class PauseMenuPackageReader
{
    public const string FolderName = "openomsi-pausemenu";
    public const int SupportedFormat = 1;
    public const long MaxCfgBytes = 64 * 1024;
    public const int MaxSymbolFiles = 64;
    public const long MaxSymbolFolderBytes = 16L * 1024 * 1024;
    public const int MaxFontFiles = 12;
    public const int MaxTextLength = 512;
    public const int MaxStringFiles = 32;
    public const int MaxStringsPerFile = 512;

    private static readonly HashSet<string> InternalSymbols = new(StringComparer.Ordinal) { "omsi2", "simple", "none", "brt", "rmtc" };
    private static readonly string[] ModeNames = { "bus", "tram", "metro", "rail" };
    private static readonly Regex Token = new("^[a-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex LangCode = new("^[A-Za-z]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex TextKey = new("^[a-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant);

    // `packageDirectory`: the openomsi-pausemenu folder (null/missing = no package). `mapFolder`: the map's folder name, the default title.
    public static PauseMenuReadResult Read(string? packageDirectory, string mapFolder)
    {
        var sink = new DiagnosticSink();
        try { return ReadCore(packageDirectory, mapFolder, sink); }
        catch (Exception exception)
        {
            // A reader bug must not take the pause menu down: fall back to the generic menu.
            sink.Ignored(FolderName, "unexpected failure while reading: " + exception.GetType().Name);
            return new PauseMenuReadResult(PauseMenuIdentity.Generic(mapFolder), PackageStatus.Ignored, sink.ToList());
        }
    }

    private static PauseMenuReadResult ReadCore(string? packageDirectory, string mapFolder, DiagnosticSink sink)
    {
        var folder = PackageFolder.Open(packageDirectory);
        if (folder is null) return new(PauseMenuIdentity.Generic(mapFolder), PackageStatus.Absent, sink.ToList());

        var generic = PauseMenuIdentity.Generic(mapFolder);
        var manifestBytes = folder.ReadAll("manifest.cfg", MaxCfgBytes, out var reason);
        if (manifestBytes is null)
        {
            if (reason != "not found") sink.Ignored("manifest.cfg", reason ?? "unreadable");
            return new(generic, reason == "not found" ? PackageStatus.Absent : PackageStatus.Ignored, sink.ToList());
        }
        var manifest = CfgReader.Parse(manifestBytes, "manifest.cfg", sink);
        if (manifest is null) return Ignore("manifest.cfg is unusable");
        if (!TryFormat(manifest.Get("format"), out var format) || format != SupportedFormat) return Ignore("unknown package format '" + manifest.Get("format") + "'");
        var name = manifest.Get("name");
        if (string.IsNullOrWhiteSpace(name)) return Ignore("manifest has no name");

        var fingerprint = folder.Fingerprint();
        var admitted = AdmitSymbols(folder, sink);
        var probes = new Dictionary<string, SymbolRef?>(StringComparer.OrdinalIgnoreCase);

        var b = new Builder();
        b.MapFolder = mapFolder;
        b.Fingerprint = fingerprint;
        b.PackageName = Clip(name);
        b.Author = Clip(manifest.Get("author"));
        b.Credit = Clip(manifest.Get("credit"));

        ReadIdentity(folder, b, admitted, probes, sink);
        ReadMarks(folder, b, admitted, probes, sink);
        b.Fonts = ReadFonts(folder, sink);
        ReadStrings(folder, b, sink);
        return new(b.Build(), PackageStatus.Loaded, sink.ToList());

        PauseMenuReadResult Ignore(string why)
        {
            sink.Ignored(FolderName, why);
            return new(generic, PackageStatus.Ignored, sink.ToList());
        }
    }

    // ---------------------------------------------------------------- identity.cfg

    private static void ReadIdentity(PackageFolder folder, Builder b, HashSet<string> admitted, Dictionary<string, SymbolRef?> probes, DiagnosticSink sink)
    {
        if (!folder.Exists("identity.cfg")) return;
        var bytes = folder.ReadAll("identity.cfg", MaxCfgBytes, out var reason);
        if (bytes is null) { sink.Dropped("identity.cfg", reason ?? "unreadable"); return; }
        var cfg = CfgReader.Parse(bytes, "identity.cfg", sink);
        if (cfg is null) return;
        var formatText = cfg.Get("format");
        if (formatText is not null && (!TryFormat(formatText, out var f) || f != SupportedFormat))
        {
            sink.Dropped("identity.cfg", "unknown identity format '" + formatText + "'; defaults used");
            return;
        }

        foreach (var entry in cfg.Unique)
        {
            var key = entry.Key.ToLowerInvariant();
            var value = entry.Value;
            var at = "identity.cfg:" + entry.Line + " " + key;
            if (key == "format") continue;
            if (key == "title" || key.StartsWith("title.", StringComparison.Ordinal)) { AddText(b, key, value, at, sink); continue; }
            if (key == "symbol") { Symbol(value, at, r => b.Symbol = r); continue; }
            if (key == "symbol.openomsi") { Symbol(value, at, r => b.OpenHudSymbol = r); continue; }
            if (key.StartsWith("symbol.", StringComparison.Ordinal))
            {
                var theme = key[7..];
                if (!Token.IsMatch(theme)) { sink.Dropped(at, "invalid theme name"); continue; }
                Symbol(value, at, r => b.ThemeSymbols[theme] = r);
                continue;
            }
            switch (key)
            {
                case "theme":
                    if (Token.IsMatch(value.ToLowerInvariant())) b.Theme = value.ToLowerInvariant();
                    else sink.Dropped(at, "invalid theme name; default used");
                    continue;
                case "layout":
                    switch (value.ToLowerInvariant())
                    {
                        case "classic": b.Layout = PauseLayout.Classic; break;
                        case "openhud": b.Layout = PauseLayout.OpenHud; break;
                        default: sink.Dropped(at, "layout must be classic or openhud; default used"); break;
                    }
                    continue;
                case "color.line": Color(value, at, c => b.LineColor = c); continue;
                case "color.brt": Color(value, at, c => b.BrtColor = c); continue;
                case "color.accent": Color(value, at, c => b.AccentColor = c); continue;
                case "stop_icon": Symbol(value, at, r => b.StopIcon = r); continue;
            }
            if (key.StartsWith("color.group.", StringComparison.Ordinal))
            {
                var id = key[12..];
                if (!Token.IsMatch(id)) { sink.Dropped(at, "invalid group id"); continue; }
                Color(value, at, c => b.GroupColors[id] = c);
            }
            else if (key.StartsWith("marker_icon.", StringComparison.Ordinal))
            {
                var kind = key[12..];
                if (!Token.IsMatch(kind)) { sink.Dropped(at, "invalid marker kind"); continue; }
                Symbol(value, at, r => b.MarkerIcons[kind] = r);
            }
            else if (key.StartsWith("mode_icon.", StringComparison.Ordinal))
            {
                var mode = key[10..];
                if (Array.IndexOf(ModeNames, mode) < 0) continue; // unknown modes are ignored, not an error
                Symbol(value, at, r => b.ModeIcons[mode] = r);
            }
            else if (key.StartsWith("legend.", StringComparison.Ordinal)) AddText(b, key, value, at, sink);
            // Unknown keys are ignored (forward compatibility).
        }

        void Symbol(string value, string at, Action<SymbolRef> set)
        {
            var symbol = ResolveSymbol(folder, value, at, admitted, probes, sink);
            if (symbol is not null) set(symbol);
        }
        void Color(string value, string at, Action<HudColor> set)
        {
            if (HudColor.TryParse(value, out var c)) set(c);
            else sink.Dropped(at, "not a #rrggbb colour; default used");
        }
    }

    // title, title.<LANG>, legend.<kind>, legend.<kind>.<LANG>
    private static void AddText(Builder b, string key, string value, string at, DiagnosticSink sink)
    {
        var parts = key.Split('.');
        var valid = parts[0] switch
        {
            "title" => parts.Length == 1 || (parts.Length == 2 && LangCode.IsMatch(parts[1])),
            "legend" => (parts.Length == 2 && Token.IsMatch(parts[1])) || (parts.Length == 3 && Token.IsMatch(parts[1]) && LangCode.IsMatch(parts[2])),
            _ => false,
        };
        if (!valid) { sink.Dropped(at, "malformed text key"); return; }
        if (value.Length == 0 || value.Length > MaxTextLength) { sink.Dropped(at, "empty or longer than " + MaxTextLength + " characters"); return; }
        var normalized = parts.Length > 1 && LangCode.IsMatch(parts[^1]) ? string.Join('.', parts[..^1]) + "." + parts[^1].ToUpperInvariant() : key;
        b.Texts[normalized] = value;
    }

    // ---------------------------------------------------------------- symbols/

    // The files of symbols/ that fit the folder limits (first 64 files / 16 MiB in name order).
    private static HashSet<string> AdmitSymbols(PackageFolder folder, DiagnosticSink sink)
    {
        var admitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var count = 0;
        foreach (var (name, length) in folder.ListFiles("symbols"))
        {
            if (count >= MaxSymbolFiles || total + length > MaxSymbolFolderBytes)
            {
                sink.Dropped("symbols/" + name, "beyond the folder limit of " + MaxSymbolFiles + " files / " + MaxSymbolFolderBytes / (1024 * 1024) + " MiB");
                continue;
            }
            count++; total += length;
            admitted.Add(name);
        }
        return admitted;
    }

    private static SymbolRef? ResolveSymbol(PackageFolder folder, string value, string at, HashSet<string> admitted, Dictionary<string, SymbolRef?> probes, DiagnosticSink sink)
    {
        value = value.Trim();
        if (value.StartsWith('@'))
        {
            var internalName = value[1..].ToLowerInvariant();
            if (InternalSymbols.Contains(internalName)) return internalName == "none" ? SymbolRef.None : SymbolRef.Internal(internalName);
            sink.Dropped(at, "unknown internal symbol '" + value + "'");
            return null;
        }
        var unsafeReason = PackageFolder.UnsafeReason(value);
        if (unsafeReason is not null) { sink.Dropped(at, "unsafe path: " + unsafeReason); return null; }
        if (!value.StartsWith("symbols/", StringComparison.OrdinalIgnoreCase)) { sink.Dropped(at, "pictures live directly in symbols/"); return null; }
        var file = value[8..];
        if (file.Contains('/')) { sink.Dropped(at, "pictures live directly in symbols/"); return null; }
        if (probes.TryGetValue(value, out var cached)) return cached;
        var result = ProbePicture(folder, file, admitted, at, sink);
        probes[value] = result;
        return result;
    }

    // Validates symbols/<file> (and its @2x sibling) by header and size.
    private static SymbolRef? ProbePicture(PackageFolder folder, string file, HashSet<string> admitted, string at, DiagnosticSink sink)
    {
        var main = ProbeOne(folder, file, admitted, at, sink);
        if (main is null) return null;
        var dot = file.LastIndexOf('.');
        var sibling = file[..dot] + "@2x" + file[dot..];
        if (admitted.Contains(sibling))
        {
            var two = ProbeOne(folder, sibling, admitted, "symbols/" + sibling, sink);
            if (two is not null) main = main with { FilePath2x = two.FilePath };
        }
        return main;
    }

    private static SymbolRef? ProbeOne(PackageFolder folder, string file, HashSet<string> admitted, string at, DiagnosticSink sink)
    {
        var dot = file.LastIndexOf('.');
        var ext = dot < 0 ? "" : file[dot..].ToLowerInvariant();
        if (ext is not (".png" or ".svg")) { sink.Dropped(at, "only PNG and SVG pictures are read"); return null; }
        if (!admitted.Contains(file)) { sink.Dropped(at, "file not found in symbols/ (or beyond the folder limit)"); return null; }
        var relative = "symbols/" + file;
        string reason;
        if (ext == ".png")
        {
            var head = folder.ReadPrefix(relative, 33, out var length, out var readReason);
            if (head is null) { sink.Dropped(at, readReason ?? "unreadable"); return null; }
            if (!AssetProbe.TryProbePng(head, length, out var w, out var h, out reason)) { sink.Dropped(at, reason); return null; }
            return new SymbolRef(SymbolKind.Picture, relative, PictureFormat.Png, w, h, length, folder.Resolve(relative, out _));
        }
        var content = folder.ReadAll(relative, AssetProbe.MaxSvgBytes, out var svgReason);
        if (content is null) { sink.Dropped(at, svgReason ?? "unreadable"); return null; }
        if (!AssetProbe.TryProbeSvg(content, out reason)) { sink.Dropped(at, reason); return null; }
        return new SymbolRef(SymbolKind.Picture, relative, PictureFormat.Svg, 0, 0, content.Length, folder.Resolve(relative, out _));
    }

    // symbols/omsi2-logo.png and symbols/openomsi.png, with the resolution chains of the spec.
    private static void ReadMarks(PackageFolder folder, Builder b, HashSet<string> admitted, Dictionary<string, SymbolRef?> probes, DiagnosticSink sink)
    {
        var logo = admitted.Contains("omsi2-logo.png") ? ProbePicture(folder, "omsi2-logo.png", admitted, "symbols/omsi2-logo.png", sink) : null;
        b.Omsi2Mark = logo ?? (b.Symbol.Kind != SymbolKind.None ? b.Symbol : SymbolRef.Internal("omsi2"));
        var open = admitted.Contains("openomsi.png") ? ProbePicture(folder, "openomsi.png", admitted, "symbols/openomsi.png", sink) : null;
        b.OpenOmsiMark = open ?? SymbolRef.None;
    }

    // ---------------------------------------------------------------- fonts/

    private static FontSet ReadFonts(PackageFolder folder, DiagnosticSink sink)
    {
        var files = folder.ListFiles("fonts")
            .Where(f => f.Name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0) return FontSet.None;

        FontSet Refuse(string why)
        {
            sink.Dropped("fonts/", why + "; the system font is used");
            return new FontSet(ImmutableDictionary<string, FontFace>.Empty, why);
        }

        var licenceLength = folder.Length("fonts/FONTS-LICENSE.txt");
        if (licenceLength <= 0) return Refuse("fonts/FONTS-LICENSE.txt is missing or empty");
        var cfgBytes = folder.ReadAll("fonts/fonts.cfg", MaxCfgBytes, out var reason);
        if (cfgBytes is null) return Refuse("fonts/fonts.cfg: " + reason);
        var cfg = CfgReader.Parse(cfgBytes, "fonts/fonts.cfg", sink);
        if (cfg is null) return Refuse("fonts/fonts.cfg is unusable");
        if (cfg.Get("format") is { } formatText && (!TryFormat(formatText, out var format) || format != SupportedFormat)) return Refuse("unknown fonts format '" + formatText + "'");

        var admitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (admitted.Count >= MaxFontFiles) { sink.Dropped("fonts/" + f.Name, "beyond the limit of " + MaxFontFiles + " font files"); continue; }
            admitted.Add(f.Name);
        }

        var explicitFaces = new Dictionary<string, FontFace>(StringComparer.Ordinal);
        foreach (var entry in cfg.Unique)
        {
            var role = entry.Key.ToLowerInvariant();
            if (role == "format") continue;
            if (Array.IndexOf(PauseMenuIdentity.FontRoles, role) < 0) continue; // unknown roles are ignored
            var at = "fonts/fonts.cfg:" + entry.Line + " " + role;
            var parts = entry.Value.Split('|').Select(x => x.Trim()).ToArray();
            var name = parts[0];
            if (PackageFolder.UnsafeReason(name) is { } bad) { sink.Dropped(at, "unsafe path: " + bad); continue; }
            if (name.Contains('/')) { sink.Dropped(at, "font files live directly in fonts/"); continue; }
            if (!admitted.Contains(name)) { sink.Dropped(at, "font file not found (or beyond the limit)"); continue; }
            var weight = DefaultWeight(role);
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                if (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var w) && w is >= 100 and <= 900) weight = w;
                else sink.Dropped(at, "weight must be 100..900; the role's default is used");
            }
            var note = parts.Length > 2 && parts[2].Length > 0 ? Clip(parts[2]) : null;
            var relative = "fonts/" + name;
            var head = folder.ReadPrefix(relative, AssetProbe.FontPrefixSize, out var length, out var readReason);
            if (head is null) { sink.Dropped(at, readReason ?? "unreadable"); continue; }
            if (!AssetProbe.TryProbeFont(head, length, out var probeReason)) { sink.Dropped(at, probeReason); continue; }
            explicitFaces[role] = new FontFace(role, relative, folder.Resolve(relative, out _)!, weight, length, note, false);
        }
        if (explicitFaces.Count == 0) return Refuse("no usable font in fonts/fonts.cfg");

        // A role left out takes the nearest weight present (ties go to the lighter face).
        var all = ImmutableDictionary.CreateBuilder<string, FontFace>(StringComparer.Ordinal);
        foreach (var role in PauseMenuIdentity.FontRoles)
        {
            if (explicitFaces.TryGetValue(role, out var face)) { all[role] = face; continue; }
            var want = DefaultWeight(role);
            var best = explicitFaces.Values.OrderBy(x => Math.Abs(x.Weight - want)).ThenBy(x => x.Weight).First();
            all[role] = best with { Role = role, Substituted = true };
        }
        return new FontSet(all.ToImmutable(), "ok");
    }

    private static int DefaultWeight(string role) => role switch { "regular" => 400, "medium" => 500, "bold" => 700, "black" => 900, _ => 700 };

    // ---------------------------------------------------------------- strings/

    private static void ReadStrings(PackageFolder folder, Builder b, DiagnosticSink sink)
    {
        var count = 0;
        foreach (var (name, _) in folder.ListFiles("strings"))
        {
            var dot = name.LastIndexOf('.');
            if (dot != 3 || !name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || !LangCode.IsMatch(name[..3])) continue;
            if (++count > MaxStringFiles) { sink.Dropped("strings/" + name, "beyond the limit of " + MaxStringFiles + " files"); continue; }
            var bytes = folder.ReadAll("strings/" + name, MaxCfgBytes, out var reason);
            if (bytes is null) { sink.Dropped("strings/" + name, reason ?? "unreadable"); continue; }
            var cfg = CfgReader.Parse(bytes, "strings/" + name, sink);
            if (cfg is null) continue;
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in cfg.Unique)
            {
                var key = entry.Key.ToLowerInvariant();
                if (key == "format") continue;
                if (!TextKey.IsMatch(key)) { sink.Dropped("strings/" + name + ":" + entry.Line, "malformed key"); continue; }
                if (entry.Value.Length == 0 || entry.Value.Length > MaxTextLength) { sink.Dropped("strings/" + name + ":" + entry.Line, "empty or longer than " + MaxTextLength); continue; }
                if (table.Count >= MaxStringsPerFile) { sink.Dropped("strings/" + name, "more than " + MaxStringsPerFile + " strings; the rest is not read"); break; }
                table[key] = entry.Value;
            }
            b.StringFiles[name[..3].ToUpperInvariant()] = table.ToImmutableDictionary(StringComparer.Ordinal);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool TryFormat(string? text, out int format) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out format);

    private static string? Clip(string? value) => value is null ? null : value.Length <= MaxTextLength ? value : value[..MaxTextLength];

    private sealed class Builder
    {
        public string MapFolder = "", Fingerprint = "", Theme = PauseMenuIdentity.DefaultTheme;
        public string? PackageName, Author, Credit;
        public SymbolRef Symbol = SymbolRef.None, StopIcon = SymbolRef.Internal("simple"), Omsi2Mark = SymbolRef.Internal("omsi2"), OpenOmsiMark = SymbolRef.None;
        public SymbolRef? OpenHudSymbol;
        public PauseLayout Layout = PauseLayout.Classic;
        public HudColor? LineColor, BrtColor, AccentColor;
        public FontSet Fonts = FontSet.None;
        public readonly Dictionary<string, SymbolRef> ThemeSymbols = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HudColor> GroupColors = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SymbolRef> MarkerIcons = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SymbolRef> ModeIcons = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, ImmutableDictionary<string, string>> StringFiles = new(StringComparer.Ordinal);

        public PauseMenuIdentity Build() => new()
        {
            MapFolder = MapFolder, PackageUsed = true, Fingerprint = Fingerprint, PackageName = PackageName, Author = Author, Credit = Credit,
            Symbol = Symbol, OpenHudSymbol = OpenHudSymbol, Theme = Theme, Layout = Layout,
            ThemeSymbols = ThemeSymbols.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            LineColor = LineColor, BrtColor = BrtColor, AccentColor = AccentColor,
            GroupColors = GroupColors.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            StopIcon = StopIcon,
            MarkerIcons = MarkerIcons.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            ModeIcons = ModeIcons.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            Omsi2Mark = Omsi2Mark, OpenOmsiMark = OpenOmsiMark, Fonts = Fonts,
            Texts = Texts.ToImmutableDictionary(StringComparer.Ordinal),
            StringFiles = StringFiles.ToImmutableDictionary(StringComparer.Ordinal),
        };
    }
}
