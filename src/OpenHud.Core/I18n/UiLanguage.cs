namespace OpenHud.I18n;

// The languages the interface itself is translated into.
public enum UiLanguage { English, PortugueseBr, German }

// Resolves which language to show and which OMSI language code to use for the
// language-suffixed keys of a pause-menu package (title.PTB).
public static class LanguageResolver
{
    // OMSI's own three-letter codes for the languages we translate.
    public static string OmsiCode(UiLanguage language) => language switch
    {
        UiLanguage.PortugueseBr => "PTB",
        UiLanguage.German => "DEU",
        _ => "ENG",
    };

    // `setting` is the language setting ("auto" or a code/tag), `omsiCode` what the game reports (ENG, PTB, DEU, ...).
    public static UiLanguage Resolve(string? setting, string? omsiCode)
    {
        var requested = Normalize(setting);
        if (requested is not null && requested != "AUTO") return Map(requested) ?? Map(Normalize(omsiCode)) ?? UiLanguage.English;
        return Map(Normalize(omsiCode)) ?? UiLanguage.English;
    }

    // The OMSI code package keys are suffixed with. An explicit setting wins; "auto" follows the game's own code
    // even when the interface has no translation for it, so a package may still offer its text (title.ESP).
    public static string PackageCode(string? setting, string? omsiCode)
    {
        var requested = Normalize(setting);
        if (requested is not null && requested != "AUTO") return ToOmsiCode(requested) ?? "ENG";
        var game = Normalize(omsiCode);
        return game is { Length: 3 } && game.All(char.IsLetter) ? game : "ENG";
    }

    // The suffix rule shared by identity keys and strings: <key>.<CODE>, then <key>.
    // `tryGet` returns the raw value of an exact key or null.
    public static string? ResolveSuffixed(string key, string? code, Func<string, string?> tryGet)
    {
        if (!string.IsNullOrEmpty(code))
        {
            var specific = tryGet(key + "." + code.ToUpperInvariant());
            if (specific is not null) return specific;
        }
        return tryGet(key);
    }

    private static string? Normalize(string? value)
    {
        if (value is null) return null;
        var text = value.Trim().ToUpperInvariant().Replace('_', '-');
        return text.Length == 0 ? null : text;
    }

    private static UiLanguage? Map(string? code)
    {
        if (code is null) return null;
        var primary = code.Split('-')[0];
        return primary switch
        {
            "ENG" or "EN" => UiLanguage.English,
            "PTB" or "PT" => UiLanguage.PortugueseBr,
            "DEU" or "DE" or "GER" => UiLanguage.German,
            _ => null,
        };
    }

    private static string? ToOmsiCode(string code)
    {
        var primary = code.Split('-')[0];
        return primary switch
        {
            "EN" => "ENG", "PT" => "PTB", "DE" => "DEU", "ES" => "ESP", "FR" => "FRA", "IT" => "ITA", "NL" => "NLD", "PL" => "POL", "CS" => "CZE", "RU" => "RUS",
            _ when primary.Length == 3 && primary.All(char.IsLetter) => primary,
            _ => null,
        };
    }
}
