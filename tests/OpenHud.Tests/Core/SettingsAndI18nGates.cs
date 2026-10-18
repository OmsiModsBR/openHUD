using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenHud.I18n;
using OpenHud.Settings;

namespace OpenHud.Tests.Core;

internal static class SettingsAndI18nGates
{
    public static void Register()
    {
        Gates.Run("settings.defaults", Defaults);
        Gates.Run("settings.valid-values", ValidValues);
        Gates.Run("settings.bad-values-fall-back-and-are-reported", BadValues);
        Gates.Run("settings.hostile-dictionary", Hostile);
        Gates.Run("settings.template-agrees-with-parser", TemplateAgrees);
        Gates.Run("i18n.every-key-in-every-language", EveryKey);
        Gates.Run("i18n.required-concepts-exist", RequiredKeys);
        Gates.Run("i18n.language-resolution", Resolution);
        Gates.Run("i18n.suffixed-keys", Suffixed);
        Gates.Run("i18n.format-and-unknown-key", FormatAndUnknown);
    }

    private static Dictionary<string, string> D(params (string, string)[] pairs) => pairs.ToDictionary(x => x.Item1, x => x.Item2);

    private static void Defaults()
    {
        foreach (var input in new IReadOnlyDictionary<string, string>?[] { null, new Dictionary<string, string>() })
        {
            var r = HudSettingsParser.Parse(input);
            Expect.Equal(HudSettings.Defaults, r.Settings, "empty input gives the defaults");
            Expect.Equal(0, r.Issues.Count, "a missing key is not an issue");
        }
        Expect.True(HudSettings.Defaults.Radar && HudSettings.Defaults.PauseMenu && HudSettings.Defaults.UiScalePercent == 100 && HudSettings.Defaults.Language == "auto", "defaults match the template");
    }

    private static void ValidValues()
    {
        var r = HudSettingsParser.Parse(D(("radar", "false"), ("pause_menu", "0"), ("ui_scale_percent", " 150 "), ("language", "pt-br"), ("units", "Imperial"),
            ("radar_zoom", "far"), ("radar_corner", "top-right"), ("radar_size_percent", "50"), ("radar_opacity_percent", "100")));
        Expect.Equal(0, r.Issues.Count, "no issues for valid values");
        var s = r.Settings;
        Expect.True(!s.Radar && !s.PauseMenu && s.UiScalePercent == 150 && s.Language == "PT-BR" && s.Units == UnitSystem.Imperial, "values parsed");
        Expect.True(s.RadarZoom == RadarZoom.Far && s.RadarCorner == RadarCorner.TopRight && s.RadarSizePercent == 50 && s.RadarOpacityPercent == 100, "radar values parsed");
        Expect.Near(1.5, s.UiScale, 1e-9, "scale factor");
        Expect.Equal("auto", HudSettingsParser.Parse(D(("language", "AUTO"))).Settings.Language, "auto is case-insensitive");
        Expect.Equal("auto", HudSettingsParser.Parse(D(("language", ""))).Settings.Language, "empty language is auto");
    }

    private static void BadValues()
    {
        var bad = new (string Key, string Value)[]
        {
            ("radar", "maybe"), ("pause_menu", "2"), ("ui_scale_percent", "49"), ("ui_scale_percent", "201"), ("ui_scale_percent", "abc"), ("ui_scale_percent", "1e2"),
            ("ui_scale_percent", "99999999999999999999"), ("ui_scale_percent", "1.5"), ("language", "not a language"), ("language", "toolongcode123"), ("language", "12"),
            ("units", "furlongs"), ("radar_zoom", "huge"), ("radar_corner", "middle"), ("radar_size_percent", "-5"), ("radar_opacity_percent", "19"),
        };
        foreach (var (key, value) in bad)
        {
            var r = HudSettingsParser.Parse(D((key, value)));
            Expect.Equal(HudSettings.Defaults, r.Settings, $"{key}={value} falls back to the defaults");
            Expect.True(r.Issues.Count == 1 && r.Issues[0].Key == key, $"{key}={value} is reported once");
        }
        // One bad value does not spoil the others.
        var mixed = HudSettingsParser.Parse(D(("radar", "false"), ("ui_scale_percent", "x"), ("units", "imperial")));
        Expect.True(!mixed.Settings.Radar && mixed.Settings.Units == UnitSystem.Imperial && mixed.Settings.UiScalePercent == 100, "good values survive a bad neighbour");
        // A very long value is clipped in the report.
        var long_ = HudSettingsParser.Parse(D(("units", new string('x', 10_000))));
        Expect.True(long_.Issues[0].Value.Length < 100, "reported values are clipped");
    }

    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => throw new InvalidOperationException();
        public IEnumerable<string> Keys => throw new InvalidOperationException();
        public IEnumerable<string> Values => throw new InvalidOperationException();
        public int Count => throw new InvalidOperationException();
        public bool ContainsKey(string key) => throw new InvalidOperationException();
        public bool TryGetValue(string key, out string value) => throw new InvalidOperationException();
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => throw new InvalidOperationException();
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException();
    }

    private static void Hostile()
    {
        Expect.Equal(HudSettings.Defaults, HudSettingsParser.Parse(new ThrowingDictionary()).Settings, "a dictionary that throws gives the defaults");
        var odd = D(("radar", "\0"), ("language", "‮evil"), ("ui_scale_percent", "\n100\n"), ("unknown_key", "x"));
        var r = HudSettingsParser.Parse(odd);
        Expect.Equal(100, r.Settings.UiScalePercent, "whitespace around a number is tolerated");
        Expect.True(r.Settings.Radar, "a NUL boolean falls back");
        Expect.Equal("auto", r.Settings.Language, "a language with control characters falls back");
    }

    private static string FindTemplate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "module.template.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("module.template.json not found above " + AppContext.BaseDirectory);
    }

    // The template is what OmsiLaunch validates; the parser must agree with it key by key.
    private static void TemplateAgrees()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindTemplate()));
        var settings = doc.RootElement.GetProperty("settings").EnumerateArray().ToList();
        var keys = settings.Select(x => x.GetProperty("key").GetString()!).ToList();
        Expect.Equal(keys.Count, keys.Distinct().Count(), "template keys are unique");
        foreach (var key in new[] { "radar", "pause_menu", "ui_scale_percent", "language" }) Expect.True(keys.Contains(key), "template keeps " + key);
        var defaults = new Dictionary<string, string>();
        foreach (var s in settings)
        {
            var key = s.GetProperty("key").GetString()!;
            var type = s.GetProperty("type").GetString();
            Expect.True(type is "boolean" or "integer" or "choice" or "text", key + ": type is one OmsiLaunch's manifest reader accepts");
            Expect.True(Regex.IsMatch(key, "^[a-z][a-z0-9_]{0,31}$"), key + ": key pattern");
            defaults[key] = s.GetProperty("default").ValueKind == JsonValueKind.String ? s.GetProperty("default").GetString()! : s.GetProperty("default").GetRawText();
            if (type == "choice")
            {
                foreach (var c in s.GetProperty("choices").EnumerateArray())
                {
                    Expect.True(Regex.IsMatch(c.GetString()!, "^[a-z0-9][a-z0-9_.-]{0,31}$") && c.GetString() != "auto", key + ": choice pattern");
                    var r = HudSettingsParser.Parse(D((key, c.GetString()!)));
                    Expect.Equal(0, r.Issues.Count, $"{key}={c.GetString()} is accepted by the parser");
                }
                Expect.True(!s.TryGetProperty("min", out _) && !s.TryGetProperty("max_length", out _), key + ": choice has no min/max");
            }
            if (type == "integer")
            {
                var min = s.GetProperty("min").GetInt32(); var max = s.GetProperty("max").GetInt32();
                Expect.Equal(0, HudSettingsParser.Parse(D((key, min.ToString()))).Issues.Count, key + ": min accepted");
                Expect.Equal(0, HudSettingsParser.Parse(D((key, max.ToString()))).Issues.Count, key + ": max accepted");
                Expect.Equal(1, HudSettingsParser.Parse(D((key, (min - 1).ToString()))).Issues.Count, key + ": below min refused");
                Expect.Equal(1, HudSettingsParser.Parse(D((key, (max + 1).ToString()))).Issues.Count, key + ": above max refused");
            }
            Expect.True(s.GetProperty("description").GetString()!.Length <= 256, key + ": description length");
        }
        var parsed = HudSettingsParser.Parse(defaults);
        Expect.Equal(0, parsed.Issues.Count, "every template default is accepted");
        Expect.Equal(HudSettings.Defaults, parsed.Settings, "the template defaults are the parser defaults");
    }

    // ------------------------------------------------------------------ i18n

    private static void EveryKey()
    {
        var keys = Strings.Table.Select(x => x.Key).ToList();
        Expect.True(keys.Count >= 40, "the table is not trivially small");
        Expect.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count(), "no duplicate keys");
        foreach (var entry in Strings.Table)
        {
            Expect.True(entry.Key.StartsWith("UI_", StringComparison.Ordinal) && Regex.IsMatch(entry.Key, "^UI_[A-Z0-9_]+$"), entry.Key + ": UI_ prefix and shape");
            foreach (var language in Enum.GetValues<UiLanguage>())
            {
                var text = Strings.Get(entry.Key, language);
                Expect.True(!string.IsNullOrWhiteSpace(text) && text != entry.Key, $"{entry.Key} has text in {language}");
            }
            var shape = new Func<string, string>(t => string.Join(",", Strings.Placeholder.Matches(t).Select(m => m.Groups[1].Value).OrderBy(x => x)));
            Expect.Equal(shape(entry.English), shape(entry.Portuguese), entry.Key + ": placeholders match in Portuguese");
            Expect.Equal(shape(entry.English), shape(entry.German), entry.Key + ": placeholders match in German");
        }
        // The translations are real: a core menu word differs between the three languages.
        Expect.True(Strings.Get("UI_PAUSE_RESUME", UiLanguage.English) != Strings.Get("UI_PAUSE_RESUME", UiLanguage.PortugueseBr)
            && Strings.Get("UI_PAUSE_RESUME", UiLanguage.English) != Strings.Get("UI_PAUSE_RESUME", UiLanguage.German), "languages are distinct");
    }

    private static void RequiredKeys()
    {
        // What the pause menu and the radar need to show (see the brief): none may be missing.
        var needed = new[]
        {
            "UI_PAUSE_TITLE", "UI_PAUSE_RESUME", "UI_PAUSE_NEW_GAME", "UI_PAUSE_SAVE", "UI_PAUSE_OPTIONS", "UI_PAUSE_QUIT", "UI_PAUSE_LAYOUT", "UI_PAUSE_THEME", "UI_PAUSE_LEGEND", "UI_PAUSE_FOOTER_VERSIONS",
            "UI_RADAR_NEXT_STOP", "UI_RADAR_END_OF_LINE", "UI_RADAR_YOU_ARE_IN", "UI_RADAR_DESTINATION", "UI_RADAR_PASSENGERS", "UI_RADAR_SPEED", "UI_RADAR_DELAY", "UI_RADAR_REQUESTED_STOP",
            "UI_UNIT_KMH", "UI_UNIT_MPH", "UI_UNIT_M", "UI_UNIT_KM", "UI_UNIT_FT", "UI_UNIT_MI", "UI_UNIT_MIN",
            "UI_LEGEND_STATION", "UI_LEGEND_TERMINAL", "UI_LEGEND_GARAGE",
        };
        foreach (var key in needed) Expect.True(Strings.Has(key), key + " exists");
    }

    private static void Resolution()
    {
        UiLanguage R(string? setting, string? game) => LanguageResolver.Resolve(setting, game);
        Expect.Equal(UiLanguage.English, R("auto", "ENG"), "auto + ENG");
        Expect.Equal(UiLanguage.PortugueseBr, R("auto", "PTB"), "auto + PTB");
        Expect.Equal(UiLanguage.German, R("auto", "DEU"), "auto + DEU");
        Expect.Equal(UiLanguage.German, R("auto", "deu"), "game code is case-insensitive");
        Expect.Equal(UiLanguage.English, R("auto", "ESP"), "untranslated game language falls back to English");
        Expect.Equal(UiLanguage.English, R("auto", null), "no game code falls back to English");
        Expect.Equal(UiLanguage.English, R(null, null), "nothing known falls back to English");
        Expect.Equal(UiLanguage.PortugueseBr, R("PT-BR", "ENG"), "explicit tag wins");
        Expect.Equal(UiLanguage.German, R("de", "PTB"), "explicit two-letter code wins");
        Expect.Equal(UiLanguage.English, R("zz", "ENG"), "unknown explicit with English game");
        Expect.Equal(UiLanguage.PortugueseBr, R("zz", "PTB"), "unknown explicit falls back to the game's language");
        Expect.Equal("PTB", LanguageResolver.OmsiCode(UiLanguage.PortugueseBr), "OMSI code of Portuguese");
        // Codes used to look up package keys.
        Expect.Equal("PTB", LanguageResolver.PackageCode("auto", "PTB"), "package code follows the game");
        Expect.Equal("ESP", LanguageResolver.PackageCode("auto", "esp"), "package code follows the game even when the UI is not translated");
        Expect.Equal("DEU", LanguageResolver.PackageCode("de", "ENG"), "explicit de maps to DEU");
        Expect.Equal("PTB", LanguageResolver.PackageCode("PT-BR", null), "explicit pt-BR maps to PTB");
        Expect.Equal("ENG", LanguageResolver.PackageCode("auto", null), "no information gives ENG");
        Expect.Equal("ENG", LanguageResolver.PackageCode("auto", "toolong"), "malformed game code gives ENG");
    }

    private static void Suffixed()
    {
        var values = new Dictionary<string, string> { ["title"] = "Plain", ["title.PTB"] = "Portugues", ["legend.station"] = "S" };
        string? Get(string k) => values.TryGetValue(k, out var v) ? v : null;
        Expect.Equal("Portugues", LanguageResolver.ResolveSuffixed("title", "PTB", Get), "specific key wins");
        Expect.Equal("Portugues", LanguageResolver.ResolveSuffixed("title", "ptb", Get), "code is case-insensitive");
        Expect.Equal("Plain", LanguageResolver.ResolveSuffixed("title", "DEU", Get), "falls back to the plain key");
        Expect.Equal("Plain", LanguageResolver.ResolveSuffixed("title", null, Get), "no code uses the plain key");
        Expect.Equal(null, LanguageResolver.ResolveSuffixed("missing", "PTB", Get), "absent everywhere is null");
    }

    private static void FormatAndUnknown()
    {
        Expect.Equal("openHUD 0.1.0 · OMSI 2.3.004", Strings.Format("UI_PAUSE_FOOTER_VERSIONS", UiLanguage.English, "0.1.0", "2.3.004"), "placeholders filled");
        Expect.Equal("5 min adiantado", Strings.Format("UI_RADAR_EARLY", UiLanguage.PortugueseBr, "5 min"), "Portuguese format");
        Expect.Equal("{0} early", Strings.Format("UI_RADAR_EARLY", UiLanguage.English), "missing arguments leave the placeholder");
        Expect.Equal("{0} early", Strings.Format("UI_RADAR_EARLY", UiLanguage.English, "x", "y").Replace("x", "{0}"), "extra arguments are ignored");
        Expect.Equal("UI_DOES_NOT_EXIST", Strings.Get("UI_DOES_NOT_EXIST", UiLanguage.German), "unknown key returns the key");
        Expect.Equal("UI_DOES_NOT_EXIST", Strings.Format("UI_DOES_NOT_EXIST", UiLanguage.German, 1), "unknown key formats to the key");
    }
}
