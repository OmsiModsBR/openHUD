using OpenHud.Pause;
using OpenHud.Scene;

namespace OpenHud.Draw;

// Puts the pause menu's draw list and the full map into one scene document. The menu is the
// Pause project's own record types; here each becomes the protocol command it was defined as.
internal static class SceneComposer
{
    public static SceneCommand Convert(MenuDraw draw) => draw switch
    {
        RectDraw r => new RectCommand(r.X, r.Y, r.W, r.H, r.Fill, r.Stroke, r.Stroke is null ? null : r.StrokeWidth, r.Radius > 0 ? r.Radius : null),
        CircleDraw c => new CircleCommand(c.Cx, c.Cy, c.R, c.Fill, c.Stroke, c.Stroke is null ? null : c.StrokeWidth),
        LineDraw l => new LineCommand(l.X1, l.Y1, l.X2, l.Y2, l.Color, l.Width),
        TextDraw t => new TextCommand(t.X, t.Y, t.Text, t.Size, RoleName(t.Role), t.Color, AlignName(t.Align), BaseName(t.Base), t.MaxWidth > 0 ? t.MaxWidth : null, t.Outline),
        ImageDraw i => new ImageCommand(i.Id, i.X, i.Y, i.W, i.H, i.Opacity < 1 ? i.Opacity : null, i.Tint),
        ClipDraw k => new ClipRectCommand(k.X, k.Y, k.W, k.H),
        UnclipDraw => new UnclipCommand(),
        _ => throw new SceneException("unknown draw record " + draw.GetType().Name),
    };

    private static string RoleName(TextRole role) => role switch
    {
        TextRole.Medium => "medium", TextRole.Bold => "bold", TextRole.Black => "black", TextRole.Condensed => "condensed", _ => "regular",
    };

    private static string AlignName(TextAlign align) => align switch { TextAlign.Center => "c", TextAlign.Right => "r", _ => "l" };

    private static string BaseName(TextBase b) => b switch { TextBase.Top => "top", TextBase.Alphabetic => "alpha", _ => "mid" };

    // The menu's commands, then (when given) a sub-scene drawn into the rectangle `area` of the canvas:
    // its own canvas starts at the rectangle's top left, and nothing of it leaves the rectangle.
    public static SceneDocument Compose(MenuScene menu, SceneDocument? sub, MenuRect area)
    {
        var builder = new SceneBuilder(menu.Width, menu.Height, menu.Background);
        foreach (var draw in menu.Commands) builder.Add(Convert(draw));
        if (sub is not null)
        {
            builder.ClipRect(area.X, area.Y, area.W, area.H);
            builder.Push(area.X, area.Y);
            foreach (var command in sub.Commands) builder.Add(command);
            builder.Pop();
            builder.Unclip();
        }
        return builder.Build();
    }
}
