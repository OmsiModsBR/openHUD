using System.Globalization;

namespace OpenHud.Art;

internal readonly record struct Rgba(byte R, byte G, byte B, double A);

// The colors a static symbol may use: #rgb #rgba #rrggbb #rrggbbaa, rgb()/rgba(), a table of named colors,
// none, transparent and currentColor.
internal static class SvgColors
{
    private static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000, ["silver"] = 0xc0c0c0, ["gray"] = 0x808080, ["grey"] = 0x808080, ["white"] = 0xffffff, ["maroon"] = 0x800000,
        ["red"] = 0xff0000, ["purple"] = 0x800080, ["fuchsia"] = 0xff00ff, ["magenta"] = 0xff00ff, ["green"] = 0x008000, ["lime"] = 0x00ff00,
        ["olive"] = 0x808000, ["yellow"] = 0xffff00, ["navy"] = 0x000080, ["blue"] = 0x0000ff, ["teal"] = 0x008080, ["aqua"] = 0x00ffff,
        ["cyan"] = 0x00ffff, ["orange"] = 0xffa500, ["brown"] = 0xa52a2a, ["pink"] = 0xffc0cb, ["gold"] = 0xffd700, ["crimson"] = 0xdc143c,
        ["indigo"] = 0x4b0082, ["violet"] = 0xee82ee, ["salmon"] = 0xfa8072, ["tomato"] = 0xff6347, ["coral"] = 0xff7f50, ["khaki"] = 0xf0e68c,
        ["beige"] = 0xf5f5dc, ["ivory"] = 0xfffff0, ["tan"] = 0xd2b48c, ["orchid"] = 0xda70d6, ["plum"] = 0xdda0dd, ["turquoise"] = 0x40e0d0,
        ["darkred"] = 0x8b0000, ["darkgreen"] = 0x006400, ["darkblue"] = 0x00008b, ["lightgray"] = 0xd3d3d3, ["lightgrey"] = 0xd3d3d3,
        ["darkgray"] = 0xa9a9a9, ["darkgrey"] = 0xa9a9a9, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969, ["skyblue"] = 0x87ceeb,
        ["steelblue"] = 0x4682b4, ["royalblue"] = 0x4169e1, ["dodgerblue"] = 0x1e90ff, ["limegreen"] = 0x32cd32, ["forestgreen"] = 0x228b22,
        ["seagreen"] = 0x2e8b57, ["darkorange"] = 0xff8c00, ["firebrick"] = 0xb22222, ["gainsboro"] = 0xdcdcdc, ["whitesmoke"] = 0xf5f5f5,
        ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["midnightblue"] = 0x191970, ["lightblue"] = 0xadd8e6, ["lightgreen"] = 0x90ee90,
    };

    // True when `text` is a color or `none`; `paint` is null for none and transparent.
    public static bool TryParse(string text, Rgba current, out Rgba? paint)
    {
        paint = null;
        text = text.Trim();
        if (text.Length == 0 || text.Length > 64) return false;
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) { paint = current; return true; }
        if (text[0] == '#') return Hex(text[1..], out paint);
        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) return Functional(text, out paint);
        if (Names.TryGetValue(text, out var rgb)) { paint = new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 1); return true; }
        return false;
    }

    private static bool Hex(string digits, out Rgba? paint)
    {
        paint = null;
        foreach (var ch in digits) if (!Uri.IsHexDigit(ch)) return false;
        int V(int i) => Convert.ToInt32(digits.Substring(i, 1), 16) * 17;
        int W(int i) => Convert.ToInt32(digits.Substring(i, 2), 16);
        switch (digits.Length)
        {
            case 3: paint = new Rgba((byte)V(0), (byte)V(1), (byte)V(2), 1); return true;
            case 4: paint = new Rgba((byte)V(0), (byte)V(1), (byte)V(2), V(3) / 255.0); return true;
            case 6: paint = new Rgba((byte)W(0), (byte)W(2), (byte)W(4), 1); return true;
            case 8: paint = new Rgba((byte)W(0), (byte)W(2), (byte)W(4), W(6) / 255.0); return true;
            default: return false;
        }
    }

    // rgb(r, g, b), rgb(r g b), rgba(r, g, b, a), rgb(r g b / a); each channel 0..255 or a percentage.
    private static bool Functional(string text, out Rgba? paint)
    {
        paint = null;
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close < open || close != text.Length - 1) return false;
        var name = text[..open].Trim();
        if (!name.Equals("rgb", StringComparison.OrdinalIgnoreCase) && !name.Equals("rgba", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = text[(open + 1)..close].Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4)) return false;
        var channels = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            var part = parts[i];
            var percent = part.EndsWith('%');
            if (percent) part = part[..^1];
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) return false;
            channels[i] = (byte)Math.Clamp(Math.Round(percent ? v * 2.55 : v), 0, 255);
        }
        var alpha = 1.0;
        if (parts.Length == 4)
        {
            var part = parts[3];
            var percent = part.EndsWith('%');
            if (percent) part = part[..^1];
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) || !double.IsFinite(alpha)) return false;
            if (percent) alpha /= 100;
            alpha = Math.Clamp(alpha, 0, 1);
        }
        paint = new Rgba(channels[0], channels[1], channels[2], alpha);
        return true;
    }
}
