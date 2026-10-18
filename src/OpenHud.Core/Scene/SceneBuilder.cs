namespace OpenHud.Scene;

/// <summary>Collects commands in draw order and produces a validated <see cref="SceneDocument"/>.</summary>
public sealed class SceneBuilder
{
    private readonly List<SceneCommand> commands = new();
    private readonly int width;
    private readonly int height;
    private readonly bool background;

    public SceneBuilder(int width = 1920, int height = 1080, bool background = false)
    {
        this.width = width; this.height = height; this.background = background;
    }

    public int Count => commands.Count;
    public int ClipDepth { get; private set; }
    public int TransformDepth { get; private set; }
    public int Width => width;
    public int Height => height;

    public SceneBuilder Add(SceneCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case ClipRectCommand or ClipCircleCommand: ClipDepth++; break;
            case UnclipCommand: if (ClipDepth > 0) ClipDepth--; break;
            case PushCommand: TransformDepth++; break;
            case PopCommand: if (TransformDepth > 0) TransformDepth--; break;
        }
        commands.Add(command);
        return this;
    }

    public SceneBuilder Rect(double x, double y, double w, double h, string? fill = null, string? stroke = null, double? sw = null, double? r = null)
        => Add(new RectCommand(x, y, w, h, fill, stroke, sw, r));

    public SceneBuilder Circle(double cx, double cy, double r, string? fill = null, string? stroke = null, double? sw = null)
        => Add(new CircleCommand(cx, cy, r, fill, stroke, sw));

    public SceneBuilder Arc(double cx, double cy, double r, double a0, double a1, string color, double sw, string? cap = null)
        => Add(new ArcCommand(cx, cy, r, a0, a1, color, sw, cap));

    public SceneBuilder Line(double x1, double y1, double x2, double y2, string color, double sw, string? cap = null)
        => Add(new LineCommand(x1, y1, x2, y2, color, sw, cap));

    public SceneBuilder Poly(double[] pts, string? fill = null, string? stroke = null, double? sw = null, bool? closed = null)
        => Add(new PolyCommand(pts, fill, stroke, sw, closed));

    public SceneBuilder Text(double x, double y, string s, double size, string? font = null, string? color = null,
        string? align = null, string? baseline = null, double? maxw = null, string? outline = null)
        => Add(new TextCommand(x, y, s, size, font, color, align, baseline, maxw, outline));

    public SceneBuilder Image(string id, double x, double y, double w, double h, double? op = null, string? tint = null, double? rot = null)
        => Add(new ImageCommand(id, x, y, w, h, op, tint, rot));

    public SceneBuilder ClipRect(double x, double y, double w, double h) => Add(new ClipRectCommand(x, y, w, h));
    public SceneBuilder ClipCircle(double cx, double cy, double r) => Add(new ClipCircleCommand(cx, cy, r));
    public SceneBuilder Unclip() => Add(new UnclipCommand());
    public SceneBuilder Push(double? tx = null, double? ty = null, double? rot = null, double? sx = null, double? sy = null)
        => Add(new PushCommand(tx, ty, rot, sx, sy));
    public SceneBuilder Pop() => Add(new PopCommand());

    /// <summary>Validates and returns the scene; throws <see cref="SceneException"/> on any protocol violation.</summary>
    public SceneDocument Build()
    {
        var scene = new SceneDocument(width, height, background, commands.ToArray());
        var issues = SceneValidator.Validate(scene);
        if (issues.Count > 0) throw new SceneException(issues);
        return scene;
    }
}
