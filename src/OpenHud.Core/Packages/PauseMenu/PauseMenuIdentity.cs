using System.Collections.Immutable;
using OpenHud.I18n;

namespace OpenHud.Packages.PauseMenu;

public enum SymbolKind { None, Internal, Picture }
public enum PictureFormat { Png, Svg }
public enum PauseLayout { Classic, OpenHud }

// A symbol, logo or icon: nothing, a mark openHUD draws itself (@name), or a validated picture file.
// The file is validated by header and size only; decoding happens in the draw layer.
public sealed record SymbolRef(SymbolKind Kind, string Name, PictureFormat? Format = null, int Width = 0, int Height = 0, long Bytes = 0, string? FilePath = null, string? FilePath2x = null)
{
    public static SymbolRef None { get; } = new(SymbolKind.None, "none");
    public static SymbolRef Internal(string name) => new(SymbolKind.Internal, name);
    public bool IsPicture => Kind == SymbolKind.Picture;
}

// A validated font file and the role it serves. Substituted: the package named no file for this role
// and the nearest weight present stands in.
public sealed record FontFace(string Role, string RelativePath, string FilePath, int Weight, long Bytes, string? Note, bool Substituted);

public sealed class FontSet
{
    public static FontSet None { get; } = new(ImmutableDictionary<string, FontFace>.Empty, "the package carries no fonts");
    private readonly ImmutableDictionary<string, FontFace> roles;

    public FontSet(ImmutableDictionary<string, FontFace> roles, string status)
    {
        this.roles = roles;
        Status = status;
    }

    // Why the set is empty (or "ok"); one line for the log.
    public string Status { get; }
    public bool HasFonts => roles.Count > 0;
    public IEnumerable<FontFace> Faces => roles.Values;

    // Null: use the system font for this role.
    public FontFace? ForRole(string role) => roles.TryGetValue(role, out var face) ? face : null;
}

// The immutable result of reading a pause-menu package: what the pause menu, HUD and radar
// draw as the map's identity. Without a package (or for any missing piece) every field holds
// the generic OMSI 2 default of the spec.
public sealed record PauseMenuIdentity
{
    public const string DefaultTheme = "blue";
    public static readonly string[] FontRoles = { "regular", "medium", "bold", "black", "condensed" };

    public string MapFolder { get; init; } = "";
    // True when a package with a known format was read.
    public bool PackageUsed { get; init; }
    // Cache key only (changes when the package files change).
    public string Fingerprint { get; init; } = "";
    public string? PackageName { get; init; }
    public string? Author { get; init; }
    public string? Credit { get; init; }

    public SymbolRef Symbol { get; init; } = SymbolRef.None;
    public ImmutableDictionary<string, SymbolRef> ThemeSymbols { get; init; } = ImmutableDictionary.Create<string, SymbolRef>(StringComparer.OrdinalIgnoreCase);
    public SymbolRef? OpenHudSymbol { get; init; }
    public string Theme { get; init; } = DefaultTheme;
    public PauseLayout Layout { get; init; } = PauseLayout.Classic;

    public HudColor? LineColor { get; init; }
    public HudColor? BrtColor { get; init; }
    public HudColor? AccentColor { get; init; }
    public ImmutableDictionary<string, HudColor> GroupColors { get; init; } = ImmutableDictionary.Create<string, HudColor>(StringComparer.OrdinalIgnoreCase);

    public SymbolRef StopIcon { get; init; } = SymbolRef.Internal("simple");
    public ImmutableDictionary<string, SymbolRef> MarkerIcons { get; init; } = ImmutableDictionary.Create<string, SymbolRef>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<string, SymbolRef> ModeIcons { get; init; } = ImmutableDictionary.Create<string, SymbolRef>(StringComparer.OrdinalIgnoreCase);

    // The OMSI 2 mark: the package's symbols/omsi2-logo.png, else its symbol, else the generic @omsi2 mark
    // (the draw layer falls back to the text "OMSI 2" if even that is unavailable).
    public SymbolRef Omsi2Mark { get; init; } = SymbolRef.Internal("omsi2");
    public SymbolRef OpenOmsiMark { get; init; } = SymbolRef.None;

    public FontSet Fonts { get; init; } = FontSet.None;

    // identity.cfg texts (title, title.PTB, legend.station, legend.station.ENG ...), keys lower case with an upper case language suffix.
    public ImmutableDictionary<string, string> Texts { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);
    // strings/<LANG>.cfg: language code -> key -> text.
    public ImmutableDictionary<string, ImmutableDictionary<string, string>> StringFiles { get; init; } = ImmutableDictionary.Create<string, ImmutableDictionary<string, string>>(StringComparer.Ordinal);

    public static PauseMenuIdentity Generic(string mapFolder, string fingerprint = "") => new() { MapFolder = mapFolder, Fingerprint = fingerprint };

    // strings/<LANG>.cfg, then identity.cfg <key>.<LANG>, then identity.cfg <key>; null = use openHUD's built-in text.
    public string? Text(string key, string? languageCode)
    {
        key = key.ToLowerInvariant();
        if (languageCode is { Length: > 0 } && StringFiles.TryGetValue(languageCode.ToUpperInvariant(), out var file) && file.TryGetValue(key, out var fromFile)) return fromFile;
        return LanguageResolver.ResolveSuffixed(key, languageCode, k => Texts.TryGetValue(k, out var v) ? v : null);
    }

    // The menu title: the package's, else the map folder name.
    public string Title(string? languageCode) => Text("title", languageCode) ?? MapFolder;

    // The package's legend text of a marker kind, or null (use UI_LEGEND_<KIND> from the string table).
    public string? Legend(string kind, string? languageCode) => Text("legend." + kind, languageCode);

    // The symbol to draw for a theme and layout: symbol.openomsi (openHUD layout), symbol.<theme>, symbol.
    public SymbolRef SymbolFor(string? theme, PauseLayout layout)
    {
        if (layout == PauseLayout.OpenHud && OpenHudSymbol is not null) return OpenHudSymbol;
        if (theme is not null && ThemeSymbols.TryGetValue(theme, out var themed)) return themed;
        return Symbol;
    }
}


public sealed record PauseMenuReadResult(PauseMenuIdentity Identity, PackageStatus Status, IReadOnlyList<PackageDiagnostic> Diagnostics);
