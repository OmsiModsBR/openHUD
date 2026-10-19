using System.Globalization;

namespace OpenHud.Scene;

// Enforces the protocol limits before a scene may leave the managed side. The native layer
// refuses the same things; refusing here keeps a bad scene from ever being sent.
// A scene is checked on every tick it changes, so the check allocates nothing for a valid command:
// a message is built only when something is wrong.
public static class SceneValidator
{
    public static readonly string[] FontRoles = { "regular", "medium", "bold", "black", "condensed" };

    private const int MaxIssues = 32;

    public static IReadOnlyList<string> Validate(SceneDocument scene)
    {
        var check = new Check();
        if (scene.Width < 1 || scene.Width > SceneLimits.MaxCanvas || scene.Height < 1 || scene.Height > SceneLimits.MaxCanvas)
            check.Fail("canvas size out of range");
        var cmds = scene.Commands;
        if (cmds is null) { check.Fail("commands missing"); return check.Issues; }
        if (cmds.Count > SceneLimits.MaxCommands)
            check.Fail("too many commands: " + cmds.Count.ToString(CultureInfo.InvariantCulture));

        int clip = 0, xform = 0;
        for (int i = 0; i < cmds.Count; i++)
        {
            check.Index = i;
            switch (cmds[i])
            {
                case RectCommand r:
                    check.F(r.X, "x"); check.F(r.Y, "y"); check.Pos(r.W, "w"); check.Pos(r.H, "h");
                    check.Col(r.Fill, "fill"); check.Col(r.Stroke, "stroke"); check.OF(r.StrokeWidth, "sw"); check.OF(r.Radius, "r"); break;
                case CircleCommand ci:
                    check.F(ci.Cx, "cx"); check.F(ci.Cy, "cy"); check.Pos(ci.R, "r");
                    check.Col(ci.Fill, "fill"); check.Col(ci.Stroke, "stroke"); check.OF(ci.StrokeWidth, "sw"); break;
                case ArcCommand a:
                    check.F(a.Cx, "cx"); check.F(a.Cy, "cy"); check.Pos(a.R, "r"); check.F(a.A0, "a0"); check.F(a.A1, "a1"); check.F(a.StrokeWidth, "sw");
                    check.Col(a.Color, "color", true);
                    if (a.Cap is not null && a.Cap != "butt" && a.Cap != "round") check.At("cap invalid"); break;
                case LineCommand l:
                    check.F(l.X1, "x1"); check.F(l.Y1, "y1"); check.F(l.X2, "x2"); check.F(l.Y2, "y2"); check.F(l.StrokeWidth, "sw");
                    check.Col(l.Color, "color", true);
                    if (l.Cap is not null && l.Cap != "butt" && l.Cap != "round") check.At("cap invalid"); break;
                case PolyCommand p:
                    if (p.Points is null || p.Points.Length % 2 != 0) { check.At("pts must hold x,y pairs"); break; }
                    foreach (var v in p.Points) if (!double.IsFinite(v)) { check.At("pts is not finite"); break; }
                    check.Col(p.Fill, "fill"); check.Col(p.Stroke, "stroke"); check.OF(p.StrokeWidth, "sw"); break;
                case TextCommand t:
                    if (t.Text is null) check.At("s missing");
                    check.F(t.X, "x"); check.F(t.Y, "y"); check.F(t.Size, "size");
                    if (t.Size <= 0) check.At("size must be positive");
                    if (t.Font is not null && Array.IndexOf(FontRoles, t.Font) < 0) check.At("font role unknown");
                    check.Col(t.Color, "color"); check.Col(t.Outline, "outline");
                    if (t.Align is not null && t.Align != "l" && t.Align != "c" && t.Align != "r") check.At("align invalid");
                    if (t.Baseline is not null && t.Baseline != "top" && t.Baseline != "mid" && t.Baseline != "alpha") check.At("base invalid");
                    check.OF(t.MaxWidth, "maxw"); break;
                case ImageCommand im:
                    if (string.IsNullOrEmpty(im.Id)) check.At("id missing");
                    check.F(im.X, "x"); check.F(im.Y, "y"); check.Pos(im.W, "w"); check.Pos(im.H, "h");
                    if (im.Opacity.HasValue && !(im.Opacity.Value >= 0 && im.Opacity.Value <= 1)) check.At("op outside 0..1");
                    check.Col(im.Tint, "tint"); check.OF(im.Rotation, "rot"); break;
                case ClipRectCommand cr:
                    check.F(cr.X, "x"); check.F(cr.Y, "y"); check.Pos(cr.W, "w"); check.Pos(cr.H, "h");
                    if (++clip > SceneLimits.MaxClipDepth) check.At("clip depth exceeds " + SceneLimits.MaxClipDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case ClipCircleCommand cc:
                    check.F(cc.Cx, "cx"); check.F(cc.Cy, "cy"); check.Pos(cc.R, "r");
                    if (++clip > SceneLimits.MaxClipDepth) check.At("clip depth exceeds " + SceneLimits.MaxClipDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case UnclipCommand:
                    if (--clip < 0) { check.At("unclip without clip"); clip = 0; }
                    break;
                case PushCommand pu:
                    check.OF(pu.Tx, "tx"); check.OF(pu.Ty, "ty"); check.OF(pu.Rotation, "rot"); check.OF(pu.Sx, "sx"); check.OF(pu.Sy, "sy");
                    if (++xform > SceneLimits.MaxTransformDepth) check.At("transform depth exceeds " + SceneLimits.MaxTransformDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case PopCommand:
                    if (--xform < 0) { check.At("pop without push"); xform = 0; }
                    break;
                default:
                    check.At("unknown command type"); break;
            }
        }
        if (clip != 0) check.Fail("unbalanced clip/unclip");
        if (xform != 0) check.Fail("unbalanced push/pop");
        return check.Issues;
    }

    private sealed class Check
    {
        public readonly List<string> Issues = new();
        public int Index;

        public void Fail(string what) { if (Issues.Count < MaxIssues) Issues.Add(what); }

        public void At(string what) { if (Issues.Count < MaxIssues) Issues.Add("cmd " + Index.ToString(CultureInfo.InvariantCulture) + ": " + what); }

        public void F(double v, string name) { if (!double.IsFinite(v)) At(name + " is not finite"); }

        public void OF(double? v, string name) { if (v.HasValue) F(v.Value, name); }

        public void Col(string? v, string name, bool required = false)
        {
            if (v is null) { if (required) At(name + " missing"); return; }
            if (!SceneColor.IsValid(v)) At(name + " is not a color");
        }

        public void Pos(double v, string name) { F(v, name); if (v < 0) At(name + " is negative"); }
    }
}
