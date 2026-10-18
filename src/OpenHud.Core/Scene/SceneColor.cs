using System.Globalization;

namespace OpenHud.Scene;

public static class SceneColor
{
    /// <summary>True for "#rrggbb" and "#rrggbbaa" (hex digits of either case).</summary>
    public static bool IsValid(string? color)
    {
        if (color is null || (color.Length != 7 && color.Length != 9) || color[0] != '#') return false;
        for (int i = 1; i < color.Length; i++)
            if (!Uri.IsHexDigit(color[i])) return false;
        return true;
    }

    public static string Rgba(byte r, byte g, byte b, byte a) =>
        "#" + r.ToString("x2", CultureInfo.InvariantCulture) + g.ToString("x2", CultureInfo.InvariantCulture)
        + b.ToString("x2", CultureInfo.InvariantCulture) + a.ToString("x2", CultureInfo.InvariantCulture);

    /// <summary>The same color with its alpha replaced by <paramref name="alpha"/> (0..1, clamped; NaN = opaque).</summary>
    public static string WithAlpha(string color, double alpha)
    {
        if (!IsValid(color)) throw new ArgumentException("not a scene color: " + color, nameof(color));
        if (double.IsNaN(alpha)) alpha = 1;
        var a = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        return color.Substring(0, 7).ToLowerInvariant() + a.ToString("x2", CultureInfo.InvariantCulture);
    }

    /// <summary>Returns <paramref name="color"/> when valid, otherwise <paramref name="fallback"/>.</summary>
    public static string Or(string? color, string fallback) => IsValid(color) ? color! : fallback;
}
