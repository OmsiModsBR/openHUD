using System.Globalization;
using OpenHud.Scene;

namespace OpenHud.Draw;

// The scene protocol has no group opacity, so the radar's opacity setting is applied to the scene
// itself: every colour's alpha (and every image's opacity) is multiplied by the factor.
internal static class SceneOpacity
{
    public static SceneDocument Apply(SceneDocument scene, int percent)
    {
        if (percent >= 100) return scene;
        var factor = Math.Clamp(percent, 0, 100) / 100.0;
        var commands = new SceneCommand[scene.Commands.Count];
        for (var i = 0; i < commands.Length; i++) commands[i] = Scale(scene.Commands[i], factor);
        return scene with { Commands = commands };
    }

    internal static string? Color(string? color, double factor)
    {
        if (color is null || !SceneColor.IsValid(color)) return color;
        var alpha = color.Length == 9 ? System.Convert.ToInt32(color.Substring(7, 2), 16) : 255;
        var scaled = (int)Math.Round(alpha * factor);
        return color.Substring(0, 7) + scaled.ToString("x2", CultureInfo.InvariantCulture);
    }

    private static SceneCommand Scale(SceneCommand command, double f) => command switch
    {
        RectCommand r => r with { Fill = Color(r.Fill, f), Stroke = Color(r.Stroke, f) },
        CircleCommand c => c with { Fill = Color(c.Fill, f), Stroke = Color(c.Stroke, f) },
        ArcCommand a => a with { Color = Color(a.Color, f)! },
        LineCommand l => l with { Color = Color(l.Color, f)! },
        PolyCommand p => p with { Fill = Color(p.Fill, f), Stroke = Color(p.Stroke, f) },
        TextCommand t => t with { Color = Color(t.Color ?? "#ffffff", f), Outline = Color(t.Outline, f) },
        ImageCommand i => i with { Opacity = (i.Opacity ?? 1) * f },
        _ => command,
    };
}
