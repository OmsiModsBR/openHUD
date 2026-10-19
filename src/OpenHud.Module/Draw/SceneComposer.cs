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
        RawDraw raw => raw.Command,
        _ => throw new SceneException("unknown draw record " + draw.GetType().Name),
    };

    private static string RoleName(TextRole role) => role switch
    {
        TextRole.Medium => "medium", TextRole.Bold => "bold", TextRole.Black => "black", TextRole.Condensed => "condensed", _ => "regular",
    };

    private static string AlignName(TextAlign align) => align switch { TextAlign.Center => "c", TextAlign.Right => "r", _ => "l" };

    private static string BaseName(TextBase b) => b switch { TextBase.Top => "top", TextBase.Alphabetic => "alpha", _ => "mid" };

    // The same scene as JSON, assembled from the menu's commands and the sub-scene's cached fragment: the map under a hovered
    // menu does not change, so its (large) encoding is made once and reused. Same refusals as Compose + SceneJson.Encode.
    public static string ComposeJson(MenuScene menu, SceneDocument? sub, string? subFragment, MenuRect area)
    {
        var commands = new List<SceneCommand>(menu.Commands.Count + 4);
        foreach (var draw in menu.Commands) commands.Add(Convert(draw));
        var menuCount = commands.Count;
        var withSub = sub is not null && subFragment is not null;
        if (withSub)
        {
            commands.Add(new ClipRectCommand(area.X, area.Y, area.W, area.H));
            commands.Add(new PushCommand(area.X, area.Y));
            commands.Add(new PopCommand());
            commands.Add(new UnclipCommand());
        }
        // The frame around the sub-scene is validated here; the sub-scene itself was when it was built. Depth adds up.
        var issues = SceneValidator.Validate(new SceneDocument(menu.Width, menu.Height, menu.Background, commands));
        if (issues.Count > 0) throw new SceneException(issues);
        if (withSub)
        {
            if (commands.Count + sub!.Commands.Count > SceneLimits.MaxCommands) throw new SceneException("too many commands: " + (commands.Count + sub.Commands.Count));
            var depth = Depth(sub);
            if (depth.Clip + 1 > SceneLimits.MaxClipDepth || depth.Transform + 1 > SceneLimits.MaxTransformDepth) throw new SceneException("clip or transform depth exceeds the limit");
        }

        var sb = new System.Text.StringBuilder(4096 + (subFragment?.Length ?? 0));
        sb.Append("{\"v\":").Append(SceneLimits.Version).Append(",\"w\":").Append(menu.Width).Append(",\"h\":").Append(menu.Height)
          .Append(",\"background\":").Append(menu.Background ? "true" : "false").Append(",\"cmds\":[");
        for (var i = 0; i < menuCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(SceneJson.EncodeCommand(commands[i]));
        }
        if (withSub)
        {
            if (menuCount > 0) sb.Append(',');
            sb.Append(SceneJson.EncodeCommand(commands[menuCount])).Append(',').Append(SceneJson.EncodeCommand(commands[menuCount + 1]));
            if (subFragment!.Length > 0) sb.Append(',').Append(subFragment);
            sb.Append(',').Append(SceneJson.EncodeCommand(commands[menuCount + 2])).Append(',').Append(SceneJson.EncodeCommand(commands[menuCount + 3]));
        }
        sb.Append("]}");
        var json = sb.ToString();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > SceneLimits.MaxJsonBytes)
            throw new SceneException("scene JSON exceeds " + SceneLimits.MaxJsonBytes + " bytes");
        return json;
    }

    // The deepest clip and transform nesting of a command list.
    internal static (int Clip, int Transform) Depth(SceneDocument scene)
    {
        int clip = 0, maxClip = 0, transform = 0, maxTransform = 0;
        foreach (var command in scene.Commands)
        {
            switch (command)
            {
                case ClipRectCommand or ClipCircleCommand: maxClip = Math.Max(maxClip, ++clip); break;
                case UnclipCommand: clip = Math.Max(0, clip - 1); break;
                case PushCommand: maxTransform = Math.Max(maxTransform, ++transform); break;
                case PopCommand: transform = Math.Max(0, transform - 1); break;
            }
        }
        return (maxClip, maxTransform);
    }

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
