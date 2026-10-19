namespace OpenHud.Pause;

// One legend row of the menu: a color swatch (or the package's icon of the marker kind) and a text per language.
public sealed record LegendEntry(IReadOnlyDictionary<string, string> Texts, string Color, OpenHud.Art.Glyph? Icon = null);

// What makes the menu "belong" to a map: a title per language, a symbol (an
// image id the integrator registered), two colors and legend texts. All of it is
// data from the map's menu package; absent or malformed pieces fall back to the
// OMSI-neutral defaults below, so the menu never carries a per-map literal.
public sealed record PauseIdentity
{
    public const string DefaultAccent = "#3b82f6";
    public const string DefaultLine = "#ffffff";

    public IReadOnlyDictionary<string, string> Titles { get; init; } = new Dictionary<string, string>();
    public string SymbolId { get; init; } = "";
    // The map's symbol as a picture or vector art; when empty, SymbolId (a registered image) stands in.
    public OpenHud.Art.Glyph Symbol { get; init; } = OpenHud.Art.Glyph.None;
    // The OMSI 2 mark and the openOMSI mark of the header; empty = the menu draws its generic marks.
    public OpenHud.Art.Glyph Omsi2Mark { get; init; } = OpenHud.Art.Glyph.None;
    public OpenHud.Art.Glyph OpenOmsiMark { get; init; } = OpenHud.Art.Glyph.None;
    // Icons by transit mode ("bus", "tram", "metro", "rail") for the lines list.
    public IReadOnlyDictionary<string, OpenHud.Art.Glyph> ModeIcons { get; init; } = new Dictionary<string, OpenHud.Art.Glyph>();
    // The package's credit line, shown in the footer next to the versions.
    public string? Credit { get; init; }
    public string Accent { get; init; } = DefaultAccent;
    public string Line { get; init; } = DefaultLine;
    public IReadOnlyList<LegendEntry> Legend { get; init; } = Array.Empty<LegendEntry>();

    public static readonly PauseIdentity Generic = new();

    // The symbol to draw: the glyph, else the registered image id (aspect unknown, drawn to fill its square slot).
    public OpenHud.Art.Glyph SymbolGlyph =>
        !Symbol.IsEmpty ? Symbol : !string.IsNullOrWhiteSpace(SymbolId) ? OpenHud.Art.Glyph.Picture(SymbolId, 1, 1) : OpenHud.Art.Glyph.None;

    // The title for a language tag such as "pt-BR": exact tag, then its
    // primary subtag, then "en", then any entry; null when there is none.
    public string? TitleFor(string language) => Pick(Titles, language);

    internal static string? Pick(IReadOnlyDictionary<string, string> texts, string language)
    {
        if (texts.Count == 0) return null;
        if (!string.IsNullOrEmpty(language))
        {
            if (Lookup(texts, language) is { } exact) return exact;
            int dash = language.IndexOfAny(new[] { '-', '_' });
            if (dash > 0 && Lookup(texts, language[..dash]) is { } primary) return primary;
        }
        if (Lookup(texts, "en") is { } english) return english;
        foreach (var pair in texts)
            if (!string.IsNullOrWhiteSpace(pair.Value)) return pair.Value;
        return null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> texts, string key)
    {
        foreach (var pair in texts)
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value)) return pair.Value;
        return null;
    }
}

// Colors of the scene protocol: #rrggbb or #rrggbbaa.
internal static class Hex
{
    public static bool IsColor(string? text)
    {
        if (text is null || (text.Length != 7 && text.Length != 9) || text[0] != '#') return false;
        for (int i = 1; i < text.Length; i++)
            if (!Uri.IsHexDigit(text[i])) return false;
        return true;
    }

    public static string Or(string? text, string fallback) => IsColor(text) ? text!.ToLowerInvariant() : fallback;

    // The color with its alpha replaced (0..255).
    public static string WithAlpha(string color, int alpha) => color[..7] + Math.Clamp(alpha, 0, 255).ToString("x2");
}
