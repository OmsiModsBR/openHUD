using OpenHud.Art;
using OpenHud.I18n;
using OpenHud.Maps;
using OpenHud.Packages;
using OpenHud.Packages.PauseMenu;
using OpenHud.Pause;
using OpenHud.Radar;

namespace OpenHud.Draw;

// How the interface looks and reads for one language and one map identity: the texts of the radar and the
// pause menu from the i18n table, the colours the package asks for, and the pause menu's identity.
internal static class Look
{
    public static RadarTexts Texts(UiLanguage language) => new()
    {
        North = Strings.Get("UI_RADAR_NORTH", language),
        NextStop = Strings.Get("UI_RADAR_NEXT_STOP", language),
        Legend = Strings.Get("UI_PAUSE_LEGEND", language),
        Lines = Strings.Get("UI_MAP_LINES", language),
        Water = Strings.Get("UI_MAP_WATER", language),
        Green = Strings.Get("UI_MAP_GREEN", language),
        Built = Strings.Get("UI_MAP_BUILT", language),
        Roads = Strings.Get("UI_MAP_ROADS", language),
        Relief = Strings.Get("UI_MAP_RELIEF", language),
        Route = Strings.Get("UI_MAP_ROUTE", language),
        You = Strings.Get("UI_MAP_YOU", language),
        More = Strings.Get("UI_MAP_MORE", language),
        UnitKmh = Strings.Get("UI_UNIT_KMH", language),
        UnitMph = Strings.Get("UI_UNIT_MPH", language),
        UnitM = Strings.Get("UI_UNIT_M", language),
        UnitKm = Strings.Get("UI_UNIT_KM", language),
        UnitFt = Strings.Get("UI_UNIT_FT", language),
        UnitMi = Strings.Get("UI_UNIT_MI", language),
    };

    // The realmap manifest's colors replace the radar's land, water, green, built-up and contour colors; the pause-menu package's
    // accent (also the route) and line color (the roads) replace theirs; everything else stays.
    internal static RadarTheme Theme(PauseMenuIdentity? identity, MapPalette? palette = null)
    {
        var theme = new RadarTheme();
        if (palette?.Land is { } land) theme = theme with { Backdrop = land };
        if (palette?.Water is { } water) theme = theme with { Water = water };
        if (palette?.Green is { } green) theme = theme with { Green = green };
        if (palette?.Built is { } built) theme = theme with { Built = built };
        if (palette?.Contour is { } contour) theme = theme with { Relief = contour };
        if (identity?.AccentColor is { } accent)
        {
            var hex = new HudColor(accent.R, accent.G, accent.B).ToHex();
            theme = theme with { Accent = hex, Route = hex };
        }
        if (identity?.LineColor is { } line) theme = theme with { Road = new HudColor(line.R, line.G, line.B).ToHex() };
        return theme;
    }

    // The menu's text lookup: the model's table first, then the menu's own catalogue (it has keys the
    // table does not), else the key (the menu then falls back to English).
    public static Func<string, string> MenuLookup(UiLanguage language)
    {
        var catalogue = PauseStrings.Catalog.ToDictionary(e => e.Key, e => language switch { UiLanguage.PortugueseBr => e.Portuguese, UiLanguage.German => e.German, _ => e.English }, StringComparer.Ordinal);
        return key =>
        {
            if (catalogue.TryGetValue(key, out var own)) return own;
            return Strings.Has(key) ? Strings.Get(key, language) : key;
        };
    }

    private static readonly string[] LegendKinds = { "station", "terminal", "garage", "stop", "landmark" };

    // PauseMenuIdentity (what the package says) -> PauseIdentity (what the menu draws). `code` is OMSI's
    // three-letter language code the package's suffixed keys use.
    internal static PauseIdentity Identity(PauseMenuIdentity package, string code, UiLanguage language, string? symbolImageId, PauseGlyphs? glyphs = null)
    {
        glyphs ??= PauseGlyphs.Empty;
        var accent = package.AccentColor is { } a ? new HudColor(a.R, a.G, a.B).ToHex() : PauseIdentity.DefaultAccent;
        var line = package.LineColor is { } l ? new HudColor(l.R, l.G, l.B).ToHex() : PauseIdentity.DefaultLine;
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var title = package.Title(code);
        if (!string.IsNullOrWhiteSpace(title))
        {
            titles[code] = title;
            titles["en"] = package.Title("ENG");
        }

        var kinds = new List<string>(LegendKinds);
        foreach (var key in package.Texts.Keys)
        {
            var parts = key.Split('.');
            if (parts.Length >= 2 && parts[0] == "legend" && !kinds.Contains(parts[1])) kinds.Add(parts[1]);
        }
        var legend = new List<LegendEntry>();
        foreach (var kind in kinds)
        {
            var builtIn = Strings.Has("UI_LEGEND_" + kind.ToUpperInvariant());
            var text = package.Legend(kind, code) ?? (builtIn ? Strings.Get("UI_LEGEND_" + kind.ToUpperInvariant(), language) : null);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [code] = text };
            texts["en"] = package.Legend(kind, "ENG") ?? (builtIn ? Strings.Get("UI_LEGEND_" + kind.ToUpperInvariant(), UiLanguage.English) : text);
            var color = package.GroupColors.TryGetValue(kind, out var group) ? new HudColor(group.R, group.G, group.B).ToHex() : accent;
            // The package's icon of the marker kind replaces the color swatch; the stop kind falls back to the stop icon.
            var icon = glyphs.MarkerIcons.TryGetValue(kind, out var markerIcon) ? markerIcon : kind == "stop" && !glyphs.StopIcon.IsEmpty ? glyphs.StopIcon : null;
            legend.Add(new LegendEntry(texts, color, icon));
        }
        return new PauseIdentity
        {
            Titles = titles, SymbolId = symbolImageId ?? "", Symbol = glyphs.Symbol, Omsi2Mark = glyphs.Omsi2Mark, OpenOmsiMark = glyphs.OpenOmsiMark,
            ModeIcons = glyphs.ModeIcons, Credit = package.Credit, Accent = accent, Line = line, Legend = legend,
        };
    }
}
