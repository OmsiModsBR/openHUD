using System.Globalization;
using System.Text;

namespace OpenHud.Scene;

/// <summary>
/// JSON payloads of the native component's small operations (docs/scene-protocol.md). These only
/// build text; sending it is the module's job. Every helper refuses what the native layer would.
/// </summary>
public static class SceneOps
{
    public static string ImagePut(string id, int width, int height, ReadOnlySpan<byte> rgba)
    {
        CheckId(id);
        if (width < 1 || height < 1) throw new SceneException("image size must be positive");
        long expected = (long)width * height * 4;
        if (expected > SceneLimits.MaxImageBytes) throw new SceneException("image larger than 4 MiB");
        if (rgba.Length != expected) throw new SceneException("rgba length must be w*h*4");
        return "{\"id\":" + Quote(id) + ",\"w\":" + width.ToString(CultureInfo.InvariantCulture)
            + ",\"h\":" + height.ToString(CultureInfo.InvariantCulture)
            + ",\"rgba\":\"" + Convert.ToBase64String(rgba) + "\"}";
    }

    public static string ImageDrop(string id)
    {
        CheckId(id);
        return "{\"id\":" + Quote(id) + "}";
    }

    public static string FontPut(string role, string absolutePath)
    {
        if (Array.IndexOf(SceneValidator.FontRoles, role) < 0) throw new SceneException("unknown font role");
        if (string.IsNullOrEmpty(absolutePath) || !Path.IsPathRooted(absolutePath)) throw new SceneException("font path must be absolute");
        return "{\"role\":" + Quote(role) + ",\"path\":" + Quote(absolutePath) + "}";
    }

    /// <summary>Payload of `measure`: the width of one line of text in canvas pixels, laid out with the registered fonts.</summary>
    public static string Measure(string text, double size, string font)
    {
        if (text is null) throw new SceneException("text missing");
        if (text.Length > MaxMeasureLength) throw new SceneException("text longer than " + MaxMeasureLength.ToString(CultureInfo.InvariantCulture) + " characters");
        if (!double.IsFinite(size) || size < 1 || size > 512) throw new SceneException("size outside 1..512");
        if (Array.IndexOf(SceneValidator.FontRoles, font) < 0) throw new SceneException("unknown font role");
        return "{\"text\":" + Quote(text) + ",\"size\":" + size.ToString("R", CultureInfo.InvariantCulture) + ",\"font\":" + Quote(font) + "}";
    }

    public const int MaxMeasureLength = 512;

    public static string Visible(bool on) => on ? "{\"on\":true}" : "{\"on\":false}";

    public static string BackgroundCapture() => "{}";

    public static string BackgroundRelease() => "{}";

    public static string Stats() => "{}";

    /// <summary>The empty scene (draws nothing).</summary>
    public static string EmptyScene() => "{\"v\":1,\"cmds\":[]}";

    private static void CheckId(string id)
    {
        if (string.IsNullOrEmpty(id)) throw new SceneException("image id missing");
        if (id.Length > 64) throw new SceneException("image id too long");
    }

    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char ch in value)
        {
            if (ch == '"') sb.Append("\\\"");
            else if (ch == '\\') sb.Append("\\\\");
            else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
