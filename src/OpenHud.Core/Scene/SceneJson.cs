using System.Globalization;
using System.Text;

namespace OpenHud.Scene;

/// <summary>
/// Deterministic scene encoder: compact JSON, members in the protocol's table order, invariant
/// culture, shortest round-trip numbers ("R"), no negative zero. A scene that breaks a protocol
/// limit is refused (<see cref="SceneException"/>), never truncated.
/// </summary>
public static class SceneJson
{
    public static string Encode(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var issues = SceneValidator.Validate(scene);
        if (issues.Count > 0) throw new SceneException(issues);

        var sb = new StringBuilder(256 + scene.Commands.Count * 64);
        sb.Append("{\"v\":").Append(SceneLimits.Version.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"w\":").Append(scene.Width.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"h\":").Append(scene.Height.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"background\":").Append(scene.Background ? "true" : "false");
        sb.Append(",\"cmds\":[");
        for (int i = 0; i < scene.Commands.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Write(sb, scene.Commands[i]);
        }
        sb.Append("]}");

        var text = sb.ToString();
        if (Encoding.UTF8.GetByteCount(text) > SceneLimits.MaxJsonBytes)
            throw new SceneException("scene JSON exceeds " + SceneLimits.MaxJsonBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
        return text;
    }

    public static byte[] EncodeUtf8(SceneDocument scene) => Encoding.UTF8.GetBytes(Encode(scene));

    // ---- one command -------------------------------------------------------------------

    private static void Write(StringBuilder sb, SceneCommand command)
    {
        switch (command)
        {
            case RectCommand c:
                Open(sb, "rect"); N(sb, "x", c.X); N(sb, "y", c.Y); N(sb, "w", c.W); N(sb, "h", c.H);
                S(sb, "fill", c.Fill); S(sb, "stroke", c.Stroke); N(sb, "sw", c.StrokeWidth); N(sb, "r", c.Radius); break;
            case CircleCommand c:
                Open(sb, "circle"); N(sb, "cx", c.Cx); N(sb, "cy", c.Cy); N(sb, "r", c.R);
                S(sb, "fill", c.Fill); S(sb, "stroke", c.Stroke); N(sb, "sw", c.StrokeWidth); break;
            case ArcCommand c:
                Open(sb, "arc"); N(sb, "cx", c.Cx); N(sb, "cy", c.Cy); N(sb, "r", c.R); N(sb, "a0", c.A0); N(sb, "a1", c.A1);
                S(sb, "color", c.Color); N(sb, "sw", c.StrokeWidth); S(sb, "cap", c.Cap); break;
            case LineCommand c:
                Open(sb, "line"); N(sb, "x1", c.X1); N(sb, "y1", c.Y1); N(sb, "x2", c.X2); N(sb, "y2", c.Y2);
                S(sb, "color", c.Color); N(sb, "sw", c.StrokeWidth); S(sb, "cap", c.Cap); break;
            case PolyCommand c:
                Open(sb, "poly"); sb.Append(",\"pts\":[");
                for (int i = 0; i < c.Points.Length; i++) { if (i > 0) sb.Append(','); Num(sb, c.Points[i]); }
                sb.Append(']');
                S(sb, "fill", c.Fill); S(sb, "stroke", c.Stroke); N(sb, "sw", c.StrokeWidth);
                if (c.Closed.HasValue) sb.Append(",\"closed\":").Append(c.Closed.Value ? "true" : "false");
                break;
            case TextCommand c:
                Open(sb, "text"); N(sb, "x", c.X); N(sb, "y", c.Y); S(sb, "s", c.Text); N(sb, "size", c.Size);
                S(sb, "font", c.Font); S(sb, "color", c.Color); S(sb, "align", c.Align); S(sb, "base", c.Baseline);
                N(sb, "maxw", c.MaxWidth); S(sb, "outline", c.Outline); break;
            case ImageCommand c:
                Open(sb, "image"); S(sb, "id", c.Id); N(sb, "x", c.X); N(sb, "y", c.Y); N(sb, "w", c.W); N(sb, "h", c.H);
                N(sb, "op", c.Opacity); S(sb, "tint", c.Tint); N(sb, "rot", c.Rotation); break;
            case ClipRectCommand c:
                Open(sb, "clip"); S(sb, "kind", "rect"); N(sb, "x", c.X); N(sb, "y", c.Y); N(sb, "w", c.W); N(sb, "h", c.H); break;
            case ClipCircleCommand c:
                Open(sb, "clip"); S(sb, "kind", "circle"); N(sb, "cx", c.Cx); N(sb, "cy", c.Cy); N(sb, "r", c.R); break;
            case UnclipCommand: Open(sb, "unclip"); break;
            case PushCommand c:
                Open(sb, "push"); N(sb, "tx", c.Tx); N(sb, "ty", c.Ty); N(sb, "rot", c.Rotation); N(sb, "sx", c.Sx); N(sb, "sy", c.Sy); break;
            case PopCommand: Open(sb, "pop"); break;
            default: throw new SceneException("unknown command type " + command.GetType().Name);
        }
        sb.Append('}');
    }

    private static void Open(StringBuilder sb, string t) => sb.Append("{\"t\":\"").Append(t).Append('"');

    private static void N(StringBuilder sb, string name, double value)
    {
        sb.Append(",\"").Append(name).Append("\":");
        Num(sb, value);
    }

    private static void N(StringBuilder sb, string name, double? value)
    {
        if (value.HasValue) N(sb, name, value.Value);
    }

    private static void S(StringBuilder sb, string name, string? value)
    {
        if (value is null) return;
        sb.Append(",\"").Append(name).Append("\":");
        Str(sb, value);
    }

    private static void Num(StringBuilder sb, double value)
    {
        if (value == 0) { sb.Append('0'); return; } // also folds -0
        Span<char> buffer = stackalloc char[40];
        value.TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture);
        sb.Append(buffer.Slice(0, written));
    }

    private static void Str(StringBuilder sb, string value)
    {
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        sb.Append(ch).Append(value[i + 1]); i++;
                    }
                    else if (char.IsSurrogate(ch)) sb.Append('�'); // a lone surrogate is not valid UTF-8
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}
