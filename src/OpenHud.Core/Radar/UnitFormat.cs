using System.Globalization;
using OpenHud.Settings;

namespace OpenHud.Radar;

// What the `units` setting changes: the speed on the radar's strip and the scale labels of the radar and the map.
// Metric shows km/h, m and km; imperial shows mph, ft and mi. The inputs are metres and metres per second.
public static class UnitFormat
{
    public const double MetresPerFoot = 0.3048;
    public const double MetresPerMile = 1609.344;
    public const double KmhPerMetrePerSecond = 3.6;
    public const double MphPerMetrePerSecond = 3600.0 / MetresPerMile;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // "47 km/h" or "29 mph"; the magnitude only (a reversing vehicle shows its speed, not a sign). Null when the speed is not a number.
    public static string? Speed(double metresPerSecond, UnitSystem units, RadarTexts texts)
    {
        if (!double.IsFinite(metresPerSecond)) return null;
        return SpeedValue(metresPerSecond, units).ToString("0", Invariant) + " " + (units == UnitSystem.Imperial ? texts.UnitMph : texts.UnitKmh);
    }

    // The whole number the strip shows (also the cache key: the scene changes when this does).
    public static int SpeedValue(double metresPerSecond, UnitSystem units)
    {
        if (!double.IsFinite(metresPerSecond)) return 0;
        var value = Math.Abs(metresPerSecond) * (units == UnitSystem.Imperial ? MphPerMetrePerSecond : KmhPerMetrePerSecond);
        return (int)Math.Min(Math.Round(value), 9999);
    }

    // A distance for a scale label: "100 m", "1.6 km"; "330 ft", "0.25 mi".
    public static string Distance(double metres, UnitSystem units, RadarTexts texts)
    {
        if (units != UnitSystem.Imperial)
            return metres >= 1000
                ? (metres / 1000).ToString("0.##", Invariant) + " " + texts.UnitKm
                : metres.ToString("0", Invariant) + " " + texts.UnitM;
        var feet = metres / MetresPerFoot;
        if (feet < 1000)
            return (feet >= 100 ? Math.Round(feet / 10) * 10 : Math.Max(1, Math.Round(feet))).ToString("0", Invariant) + " " + texts.UnitFt;
        var miles = metres / MetresPerMile;
        // Quarter miles up to four miles, whole miles beyond, so the zoom steps read as 0.25, 0.5, 1, 2 mi.
        miles = miles < 4 ? Math.Round(miles * 4) / 4 : Math.Round(miles);
        return miles.ToString("0.##", Invariant) + " " + texts.UnitMi;
    }

    // The lengths a scale bar may have, ascending: 1/2/5 times a power of ten of the unit (feet below a mile, miles above).
    public static IReadOnlyList<(double Metres, string Label)> ScaleSteps(UnitSystem units, RadarTexts texts)
    {
        var steps = new List<(double, string)>();
        if (units != UnitSystem.Imperial)
        {
            for (double decade = 1; decade <= 1e6; decade *= 10)
                foreach (var step in new[] { 1.0, 2.0, 5.0 })
                    steps.Add((step * decade, Distance(step * decade, units, texts)));
            return steps;
        }
        for (double decade = 1; decade <= 100; decade *= 10)
            foreach (var step in new[] { 1.0, 2.0, 5.0 })
            {
                var feet = step * decade;
                if (feet < 1000) steps.Add((feet * MetresPerFoot, feet.ToString("0", Invariant) + " " + texts.UnitFt));
            }
        steps.Add((0.25 * MetresPerMile, "0.25 " + texts.UnitMi));
        steps.Add((0.5 * MetresPerMile, "0.5 " + texts.UnitMi));
        for (double decade = 1; decade <= 1e4; decade *= 10)
            foreach (var step in new[] { 1.0, 2.0, 5.0 })
            {
                var miles = step * decade;
                steps.Add((miles * MetresPerMile, miles.ToString("0", Invariant) + " " + texts.UnitMi));
            }
        return steps;
    }
}
