using System.Globalization;

namespace OpenHud.Scene;

// Enforces the protocol limits before a scene may leave the managed side. The native layer
// refuses the same things; refusing here keeps a bad scene from ever being sent.
public static class SceneValidator
{
    public static readonly string[] FontRoles = { "regular", "medium", "bold", "black", "condensed" };

    public static IReadOnlyList<string> Validate(SceneDocument scene)
    {
        var issues = new List<string>();
        void Fail(string what) { if (issues.Count < 32) issues.Add(what); }

        if (scene.Width < 1 || scene.Width > SceneLimits.MaxCanvas || scene.Height < 1 || scene.Height > SceneLimits.MaxCanvas)
            Fail("canvas size out of range");
        var cmds = scene.Commands;
        if (cmds is null) { Fail("commands missing"); return issues; }
        if (cmds.Count > SceneLimits.MaxCommands)
            Fail("too many commands: " + cmds.Count.ToString(CultureInfo.InvariantCulture));

        int clip = 0, xform = 0;
        for (int i = 0; i < cmds.Count; i++)
        {
            var c = cmds[i];
            string at = "cmd " + i.ToString(CultureInfo.InvariantCulture) + ": ";
            void F(double v, string name) { if (!double.IsFinite(v)) Fail(at + name + " is not finite"); }
            void OF(double? v, string name) { if (v.HasValue) F(v.Value, name); }
            void Col(string? v, string name, bool required = false)
            {
                if (v is null) { if (required) Fail(at + name + " missing"); return; }
                if (!SceneColor.IsValid(v)) Fail(at + name + " is not a color");
            }
            void Pos(double v, string name) { F(v, name); if (v < 0) Fail(at + name + " is negative"); }

            switch (c)
            {
                case RectCommand r:
                    F(r.X, "x"); F(r.Y, "y"); Pos(r.W, "w"); Pos(r.H, "h");
                    Col(r.Fill, "fill"); Col(r.Stroke, "stroke"); OF(r.StrokeWidth, "sw"); OF(r.Radius, "r"); break;
                case CircleCommand ci:
                    F(ci.Cx, "cx"); F(ci.Cy, "cy"); Pos(ci.R, "r");
                    Col(ci.Fill, "fill"); Col(ci.Stroke, "stroke"); OF(ci.StrokeWidth, "sw"); break;
                case ArcCommand a:
                    F(a.Cx, "cx"); F(a.Cy, "cy"); Pos(a.R, "r"); F(a.A0, "a0"); F(a.A1, "a1"); F(a.StrokeWidth, "sw");
                    Col(a.Color, "color", true);
                    if (a.Cap is not null && a.Cap != "butt" && a.Cap != "round") Fail(at + "cap invalid"); break;
                case LineCommand l:
                    F(l.X1, "x1"); F(l.Y1, "y1"); F(l.X2, "x2"); F(l.Y2, "y2"); F(l.StrokeWidth, "sw");
                    Col(l.Color, "color", true);
                    if (l.Cap is not null && l.Cap != "butt" && l.Cap != "round") Fail(at + "cap invalid"); break;
                case PolyCommand p:
                    if (p.Points is null || p.Points.Length % 2 != 0) { Fail(at + "pts must hold x,y pairs"); break; }
                    foreach (var v in p.Points) if (!double.IsFinite(v)) { Fail(at + "pts is not finite"); break; }
                    Col(p.Fill, "fill"); Col(p.Stroke, "stroke"); OF(p.StrokeWidth, "sw"); break;
                case TextCommand t:
                    if (t.Text is null) Fail(at + "s missing");
                    F(t.X, "x"); F(t.Y, "y"); F(t.Size, "size");
                    if (t.Size <= 0) Fail(at + "size must be positive");
                    if (t.Font is not null && Array.IndexOf(FontRoles, t.Font) < 0) Fail(at + "font role unknown");
                    Col(t.Color, "color"); Col(t.Outline, "outline");
                    if (t.Align is not null && t.Align != "l" && t.Align != "c" && t.Align != "r") Fail(at + "align invalid");
                    if (t.Baseline is not null && t.Baseline != "top" && t.Baseline != "mid" && t.Baseline != "alpha") Fail(at + "base invalid");
                    OF(t.MaxWidth, "maxw"); break;
                case ImageCommand im:
                    if (string.IsNullOrEmpty(im.Id)) Fail(at + "id missing");
                    F(im.X, "x"); F(im.Y, "y"); Pos(im.W, "w"); Pos(im.H, "h");
                    if (im.Opacity.HasValue && !(im.Opacity.Value >= 0 && im.Opacity.Value <= 1)) Fail(at + "op outside 0..1");
                    Col(im.Tint, "tint"); OF(im.Rotation, "rot"); break;
                case ClipRectCommand cr:
                    F(cr.X, "x"); F(cr.Y, "y"); Pos(cr.W, "w"); Pos(cr.H, "h");
                    if (++clip > SceneLimits.MaxClipDepth) Fail(at + "clip depth exceeds " + SceneLimits.MaxClipDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case ClipCircleCommand cc:
                    F(cc.Cx, "cx"); F(cc.Cy, "cy"); Pos(cc.R, "r");
                    if (++clip > SceneLimits.MaxClipDepth) Fail(at + "clip depth exceeds " + SceneLimits.MaxClipDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case UnclipCommand:
                    if (--clip < 0) { Fail(at + "unclip without clip"); clip = 0; }
                    break;
                case PushCommand pu:
                    OF(pu.Tx, "tx"); OF(pu.Ty, "ty"); OF(pu.Rotation, "rot"); OF(pu.Sx, "sx"); OF(pu.Sy, "sy");
                    if (++xform > SceneLimits.MaxTransformDepth) Fail(at + "transform depth exceeds " + SceneLimits.MaxTransformDepth.ToString(CultureInfo.InvariantCulture));
                    break;
                case PopCommand:
                    if (--xform < 0) { Fail(at + "pop without push"); xform = 0; }
                    break;
                default:
                    Fail(at + "unknown command type"); break;
            }
        }
        if (clip != 0) Fail("unbalanced clip/unclip");
        if (xform != 0) Fail("unbalanced push/pop");
        return issues;
    }
}
