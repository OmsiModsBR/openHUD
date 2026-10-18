using System.Globalization;

namespace OpenHud.Settings;

public enum UnitSystem { Metric, Imperial }
public enum RadarZoom { Near, Medium, Far }
public enum RadarCorner { TopLeft, TopRight, BottomLeft, BottomRight }

// One value of the effective settings that could not be used (and was replaced by its default).
public sealed record SettingsIssue(string Key, string Value, string Reason);

// The module's effective settings, typed. Immutable; every field always holds a usable value.
public sealed record HudSettings(
    bool Radar,
    bool PauseMenu,
    int UiScalePercent,
    string Language,
    UnitSystem Units,
    RadarZoom RadarZoom,
    RadarCorner RadarCorner,
    int RadarSizePercent,
    int RadarOpacityPercent)
{
    public const string AutoLanguage = "auto";

    public static HudSettings Defaults { get; } = new(true, true, 100, AutoLanguage, UnitSystem.Metric, RadarZoom.Medium, RadarCorner.BottomLeft, 100, 90);

    // The scale as a factor (1.0 = 100 %).
    public double UiScale => UiScalePercent / 100.0;
}

public sealed record HudSettingsResult(HudSettings Settings, IReadOnlyList<SettingsIssue> Issues);

// Parsing is total: a missing key is the default (not an issue), a bad value is the
// default plus a reported issue. Nothing here throws, whatever the dictionary holds.
public static class HudSettingsParser
{
    public static HudSettingsResult Parse(IReadOnlyDictionary<string, string>? values)
    {
        var issues = new List<SettingsIssue>();
        var d = HudSettings.Defaults;
        string? Raw(string key)
        {
            if (values is null) return null;
            try { return values.TryGetValue(key, out var v) ? v : null; }
            catch (Exception) { return null; }
        }
        var settings = new HudSettings(
            Bool("radar", d.Radar),
            Bool("pause_menu", d.PauseMenu),
            Int("ui_scale_percent", d.UiScalePercent, 50, 200),
            Language(),
            Choice("units", d.Units, ("metric", UnitSystem.Metric), ("imperial", UnitSystem.Imperial)),
            Choice("radar_zoom", d.RadarZoom, ("near", RadarZoom.Near), ("medium", RadarZoom.Medium), ("far", RadarZoom.Far)),
            Choice("radar_corner", d.RadarCorner, ("top-left", RadarCorner.TopLeft), ("top-right", RadarCorner.TopRight), ("bottom-left", RadarCorner.BottomLeft), ("bottom-right", RadarCorner.BottomRight)),
            Int("radar_size_percent", d.RadarSizePercent, 50, 200),
            Int("radar_opacity_percent", d.RadarOpacityPercent, 20, 100));
        return new HudSettingsResult(settings, issues);

        bool Bool(string key, bool fallback)
        {
            var raw = Raw(key);
            if (raw is null) return fallback;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "on": return true;
                case "false": case "0": case "no": case "off": return false;
            }
            issues.Add(new(key, Clip(raw), "not a boolean"));
            return fallback;
        }

        int Int(string key, int fallback, int min, int max)
        {
            var raw = Raw(key);
            if (raw is null) return fallback;
            if (!int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            {
                issues.Add(new(key, Clip(raw), "not an integer"));
                return fallback;
            }
            if (n < min || n > max)
            {
                issues.Add(new(key, Clip(raw), "outside " + min + ".." + max));
                return fallback;
            }
            return n;
        }

        T Choice<T>(string key, T fallback, params (string Name, T Value)[] options)
        {
            var raw = Raw(key);
            if (raw is null) return fallback;
            var name = raw.Trim().ToLowerInvariant();
            foreach (var option in options) if (option.Name == name) return option.Value;
            issues.Add(new(key, Clip(raw), "not one of " + string.Join(", ", options.Select(x => x.Name))));
            return fallback;
        }

        string Language()
        {
            var raw = Raw("language");
            if (raw is null) return HudSettings.AutoLanguage;
            var text = raw.Trim();
            if (text.Length == 0 || string.Equals(text, HudSettings.AutoLanguage, StringComparison.OrdinalIgnoreCase)) return HudSettings.AutoLanguage;
            // A language code or tag: letters, with '-' or '_' between parts (ENG, pt-BR, de).
            if (text.Length <= 8 && text.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-' or '_') && text.Any(char.IsLetter))
                return text.ToUpperInvariant().Replace('_', '-');
            issues.Add(new("language", Clip(raw), "not a language code"));
            return HudSettings.AutoLanguage;
        }
    }

    private static string Clip(string value) => value.Length <= 40 ? value : value[..40] + "...";
}
