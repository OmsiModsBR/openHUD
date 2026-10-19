namespace OpenHud.Scene;

/// <summary>
/// A 64-bit hash of everything a scene says (canvas, commands, every member), computed without encoding it. Two scenes
/// that encode to the same JSON hash alike (numbers are compared by value, -0 is 0), so "the same hash as last time" lets
/// the module skip the encoding and the call to the draw layer. It is a change detector, not a security check.
/// </summary>
public static class SceneHash
{
    private const ulong Seed = 0xcbf29ce484222325UL;
    private const ulong Prime = 0x100000001b3UL;

    public static ulong Of(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var h = Seed;
        h = Mix(h, (ulong)scene.Width); h = Mix(h, (ulong)scene.Height); h = Mix(h, scene.Background ? 1UL : 0UL);
        var commands = scene.Commands;
        h = Mix(h, (ulong)commands.Count);
        for (int i = 0; i < commands.Count; i++) h = Command(h, commands[i]);
        return h;
    }

    private static ulong Command(ulong h, SceneCommand command)
    {
        switch (command)
        {
            case RectCommand c:
                h = Mix(h, 1); h = D(h, c.X); h = D(h, c.Y); h = D(h, c.W); h = D(h, c.H); h = S(h, c.Fill); h = S(h, c.Stroke); h = O(h, c.StrokeWidth); return O(h, c.Radius);
            case CircleCommand c:
                h = Mix(h, 2); h = D(h, c.Cx); h = D(h, c.Cy); h = D(h, c.R); h = S(h, c.Fill); h = S(h, c.Stroke); return O(h, c.StrokeWidth);
            case ArcCommand c:
                h = Mix(h, 3); h = D(h, c.Cx); h = D(h, c.Cy); h = D(h, c.R); h = D(h, c.A0); h = D(h, c.A1); h = S(h, c.Color); h = D(h, c.StrokeWidth); return S(h, c.Cap);
            case LineCommand c:
                h = Mix(h, 4); h = D(h, c.X1); h = D(h, c.Y1); h = D(h, c.X2); h = D(h, c.Y2); h = S(h, c.Color); h = D(h, c.StrokeWidth); return S(h, c.Cap);
            case PolyCommand c:
                h = Mix(h, 5); h = Mix(h, (ulong)c.Points.Length);
                foreach (var v in c.Points) h = D(h, v);
                h = S(h, c.Fill); h = S(h, c.Stroke); h = O(h, c.StrokeWidth); return Mix(h, c.Closed is null ? 2UL : c.Closed.Value ? 1UL : 0UL);
            case TextCommand c:
                h = Mix(h, 6); h = D(h, c.X); h = D(h, c.Y); h = S(h, c.Text); h = D(h, c.Size); h = S(h, c.Font); h = S(h, c.Color); h = S(h, c.Align); h = S(h, c.Baseline); h = O(h, c.MaxWidth); return S(h, c.Outline);
            case ImageCommand c:
                h = Mix(h, 7); h = S(h, c.Id); h = D(h, c.X); h = D(h, c.Y); h = D(h, c.W); h = D(h, c.H); h = O(h, c.Opacity); h = S(h, c.Tint); return O(h, c.Rotation);
            case ClipRectCommand c:
                h = Mix(h, 8); h = D(h, c.X); h = D(h, c.Y); h = D(h, c.W); return D(h, c.H);
            case ClipCircleCommand c:
                h = Mix(h, 9); h = D(h, c.Cx); h = D(h, c.Cy); return D(h, c.R);
            case UnclipCommand: return Mix(h, 10);
            case PushCommand c:
                h = Mix(h, 11); h = O(h, c.Tx); h = O(h, c.Ty); h = O(h, c.Rotation); h = O(h, c.Sx); return O(h, c.Sy);
            case PopCommand: return Mix(h, 12);
            default: return Mix(h, 0xdeadUL);
        }
    }

    private static ulong Mix(ulong h, ulong v)
    {
        h ^= v + 0x9e3779b97f4a7c15UL + (h << 6) + (h >> 2);
        return h * Prime;
    }

    private static ulong D(ulong h, double v) => Mix(h, v == 0 ? 0UL : (ulong)BitConverter.DoubleToInt64Bits(v));

    private static ulong O(ulong h, double? v) => v.HasValue ? D(Mix(h, 1), v.Value) : Mix(h, 0);

    private static ulong S(ulong h, string? s)
    {
        if (s is null) return Mix(h, 0x5eedUL);
        h = Mix(h, (ulong)s.Length);
        foreach (var ch in s) h = (h ^ ch) * Prime;
        return h;
    }
}
