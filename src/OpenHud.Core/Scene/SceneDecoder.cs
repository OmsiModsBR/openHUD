using System.Text.Json;

namespace OpenHud.Scene;

/// <summary>
/// Reads a scene document back into records. Production never needs it (the native layer is the
/// reader); the tests use it to compare golden vectors structurally and to round-trip the encoder.
/// </summary>
public static class SceneDecoder
{
    public static SceneDocument Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("scene must be an object");
        if (Num(root, "v") != SceneLimits.Version) throw new FormatException("unsupported scene version");
        var cmds = new List<SceneCommand>();
        foreach (var e in root.GetProperty("cmds").EnumerateArray()) cmds.Add(Command(e));
        bool background = root.TryGetProperty("background", out var b) && b.ValueKind == JsonValueKind.True;
        return new SceneDocument((int)Num(root, "w"), (int)Num(root, "h"), background, cmds);
    }

    private static SceneCommand Command(JsonElement e)
    {
        string t = Str(e, "t") ?? throw new FormatException("command without t");
        switch (t)
        {
            case "rect": return new RectCommand(Num(e, "x"), Num(e, "y"), Num(e, "w"), Num(e, "h"), Str(e, "fill"), Str(e, "stroke"), ONum(e, "sw"), ONum(e, "r"));
            case "circle": return new CircleCommand(Num(e, "cx"), Num(e, "cy"), Num(e, "r"), Str(e, "fill"), Str(e, "stroke"), ONum(e, "sw"));
            case "arc": return new ArcCommand(Num(e, "cx"), Num(e, "cy"), Num(e, "r"), Num(e, "a0"), Num(e, "a1"), Str(e, "color") ?? "", Num(e, "sw"), Str(e, "cap"));
            case "line": return new LineCommand(Num(e, "x1"), Num(e, "y1"), Num(e, "x2"), Num(e, "y2"), Str(e, "color") ?? "", Num(e, "sw"), Str(e, "cap"));
            case "poly":
                var pts = new List<double>();
                foreach (var v in e.GetProperty("pts").EnumerateArray()) pts.Add(v.GetDouble());
                bool? closed = e.TryGetProperty("closed", out var c) ? c.ValueKind == JsonValueKind.True : null;
                return new PolyCommand(pts.ToArray(), Str(e, "fill"), Str(e, "stroke"), ONum(e, "sw"), closed);
            case "text": return new TextCommand(Num(e, "x"), Num(e, "y"), Str(e, "s") ?? "", Num(e, "size"), Str(e, "font"), Str(e, "color"), Str(e, "align"), Str(e, "base"), ONum(e, "maxw"), Str(e, "outline"));
            case "image": return new ImageCommand(Str(e, "id") ?? "", Num(e, "x"), Num(e, "y"), Num(e, "w"), Num(e, "h"), ONum(e, "op"), Str(e, "tint"), ONum(e, "rot"));
            case "clip":
                return Str(e, "kind") switch
                {
                    "rect" => new ClipRectCommand(Num(e, "x"), Num(e, "y"), Num(e, "w"), Num(e, "h")),
                    "circle" => new ClipCircleCommand(Num(e, "cx"), Num(e, "cy"), Num(e, "r")),
                    _ => throw new FormatException("unknown clip kind"),
                };
            case "unclip": return new UnclipCommand();
            case "push": return new PushCommand(ONum(e, "tx"), ONum(e, "ty"), ONum(e, "rot"), ONum(e, "sx"), ONum(e, "sy"));
            case "pop": return new PopCommand();
            default: throw new FormatException("unknown command type " + t);
        }
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : throw new FormatException("missing number " + name);

    private static double? ONum(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
