namespace OpenHud.Scene;

// The scene protocol's commands as immutable records (docs/scene-protocol.md). Colors are
// "#rrggbb" or "#rrggbbaa" strings, angles are degrees (0 = up, clockwise). Optional
// members are null when absent; the encoder omits them.
public abstract record SceneCommand;

public sealed record RectCommand(double X, double Y, double W, double H,
    string? Fill = null, string? Stroke = null, double? StrokeWidth = null, double? Radius = null) : SceneCommand;

public sealed record CircleCommand(double Cx, double Cy, double R,
    string? Fill = null, string? Stroke = null, double? StrokeWidth = null) : SceneCommand;

public sealed record ArcCommand(double Cx, double Cy, double R, double A0, double A1,
    string Color, double StrokeWidth, string? Cap = null) : SceneCommand;

public sealed record LineCommand(double X1, double Y1, double X2, double Y2,
    string Color, double StrokeWidth, string? Cap = null) : SceneCommand;

public sealed record PolyCommand(double[] Points,
    string? Fill = null, string? Stroke = null, double? StrokeWidth = null, bool? Closed = null) : SceneCommand;

public sealed record TextCommand(double X, double Y, string Text, double Size,
    string? Font = null, string? Color = null, string? Align = null, string? Baseline = null,
    double? MaxWidth = null, string? Outline = null) : SceneCommand;

public sealed record ImageCommand(string Id, double X, double Y, double W, double H,
    double? Opacity = null, string? Tint = null, double? Rotation = null) : SceneCommand;

public sealed record ClipRectCommand(double X, double Y, double W, double H) : SceneCommand;

public sealed record ClipCircleCommand(double Cx, double Cy, double R) : SceneCommand;

public sealed record UnclipCommand : SceneCommand;

public sealed record PushCommand(double? Tx = null, double? Ty = null, double? Rotation = null,
    double? Sx = null, double? Sy = null) : SceneCommand;

public sealed record PopCommand : SceneCommand;

/// <summary>One complete scene: the virtual canvas and the ordered commands.</summary>
public sealed record SceneDocument(int Width, int Height, bool Background, IReadOnlyList<SceneCommand> Commands);

public static class SceneLimits
{
    public const int Version = 1;
    public const int MaxCommands = 8192;
    public const int MaxJsonBytes = 1 << 20;
    public const int MaxClipDepth = 8;
    public const int MaxTransformDepth = 16;
    public const int MaxImageBytes = 4 << 20;
    public const int MaxCanvas = 16384;
}

/// <summary>Thrown when a scene or an operation payload breaks the protocol; carries every issue found.</summary>
public sealed class SceneException : Exception
{
    public IReadOnlyList<string> Issues { get; }

    public SceneException(IReadOnlyList<string> issues)
        : base("Invalid scene: " + string.Join("; ", issues)) => Issues = issues;

    public SceneException(string issue) : this(new[] { issue }) { }
}
