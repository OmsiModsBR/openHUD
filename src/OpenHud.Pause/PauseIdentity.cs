namespace OpenHud.Pause;

// One legend row of the menu: a color swatch and a text per language.
public sealed record LegendEntry(IReadOnlyDictionary<string, string> Texts, string Color);

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
    public string Accent { get; init; } = DefaultAccent;
    public string Line { get; init; } = DefaultLine;
    public IReadOnlyList<LegendEntry> Legend { get; init; } = Array.Empty<LegendEntry>();

    public static readonly PauseIdentity Generic = new();

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
