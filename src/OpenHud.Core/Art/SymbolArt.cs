using System.Globalization;
using OpenHud.Scene;

namespace OpenHud.Art;

public enum ArtKind { Poly, Rect, Circle, Line }

// One drawable of a symbol, in the units of its view box. Points hold x,y pairs (Poly), x y w h (Rect),
// cx cy r (Circle) or x1 y1 x2 y2 (Line). Colors are scene colors ("#rrggbb" or "#rrggbbaa").
public sealed record ArtShape(ArtKind Kind, double[] Values, string? Fill, string? Stroke, double StrokeWidth, bool Closed = false, double Radius = 0, string? Cap = null)
{
    public static ArtShape Poly(double[] points, string? fill, string? stroke, double strokeWidth, bool closed) =>
        new(ArtKind.Poly, points, fill, stroke, strokeWidth, closed);

    public static ArtShape Rect(double x, double y, double w, double h, double radius, string? fill, string? stroke, double strokeWidth) =>
        new(ArtKind.Rect, new[] { x, y, w, h }, fill, stroke, strokeWidth, true, radius);

    public static ArtShape Circle(double cx, double cy, double r, string? fill, string? stroke, double strokeWidth) =>
        new(ArtKind.Circle, new[] { cx, cy, r }, fill, stroke, strokeWidth, true);

    public static ArtShape Line(double x1, double y1, double x2, double y2, string stroke, double strokeWidth, string? cap) =>
        new(ArtKind.Line, new[] { x1, y1, x2, y2 }, null, stroke, strokeWidth, false, 0, cap);

    // The number of coordinates pairs the shape carries (for budgets).
    public int PointCount => Kind switch { ArtKind.Poly => Values.Length / 2, ArtKind.Line => 2, _ => 1 };
}

// A symbol turned into drawing primitives, independent of where it will be drawn: an SVG after conversion.
// Immutable. It draws as plain scene commands (no raster, nothing in the native layer), scaled to fit a slot
// with its aspect kept and centered.
public sealed class SymbolArt
{
    // Coordinates are clamped to this many slot sizes around the slot: far enough to stay clipped, near enough for the protocol.
    private const double ClampSlots = 8;

    public SymbolArt(double width, double height, IReadOnlyList<ArtShape> shapes, bool needsClip)
    {
        Width = width; Height = height; Shapes = shapes; NeedsClip = needsClip;
    }

    public double Width { get; }
    public double Height { get; }
    public IReadOnlyList<ArtShape> Shapes { get; }
    // True when something lies outside the view box (the view box then clips, as in a browser).
    public bool NeedsClip { get; }
    // Scene commands one drawing costs.
    public int CommandCount => Shapes.Count + (NeedsClip ? 2 : 0);
    public double Aspect => Width / Height;

    // The scene commands that draw the symbol fitted into the slot. `opacity` (0..1) multiplies every alpha.
    public IReadOnlyList<SceneCommand> Commands(double x, double y, double w, double h, double opacity = 1)
    {
        var list = new List<SceneCommand>(CommandCount);
        AppendTo(list, x, y, w, h, opacity);
        return list;
    }

    public void AppendTo(SceneBuilder builder, double x, double y, double w, double h, double opacity = 1)
    {
        var list = new List<SceneCommand>(CommandCount);
        AppendTo(list, x, y, w, h, opacity);
        foreach (var command in list) builder.Add(command);
    }

    public void AppendTo(List<SceneCommand> output, double x, double y, double w, double h, double opacity = 1)
    {
        if (!(w > 0) || !(h > 0) || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h)) return;
        var s = Math.Min(w / Width, h / Height);
        if (!(s > 0) || !double.IsFinite(s)) return;
        double fw = Width * s, fh = Height * s;
        double ox = x + (w - fw) / 2, oy = y + (h - fh) / 2;
        double minX = ox - ClampSlots * fw, maxX = ox + (ClampSlots + 1) * fw, minY = oy - ClampSlots * fh, maxY = oy + (ClampSlots + 1) * fh;
        var alpha = double.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1);
        if (alpha <= 0) return;

        double X(double v) => Math.Round(Math.Clamp(ox + v * s, minX, maxX), 2);
        double Y(double v) => Math.Round(Math.Clamp(oy + v * s, minY, maxY), 2);
        double L(double v) => Math.Round(Math.Clamp(v * s, 0, 4 * Math.Max(fw, fh)), 2);
        string? C(string? color) => color is null ? null : alpha >= 1 ? color : Scaled(color, alpha);

        if (NeedsClip) output.Add(new ClipRectCommand(Math.Round(ox, 2), Math.Round(oy, 2), Math.Round(fw, 2), Math.Round(fh, 2)));
        foreach (var shape in Shapes)
        {
            var v = shape.Values;
            var sw = shape.Stroke is null ? (double?)null : Math.Max(0.1, L(shape.StrokeWidth));
            switch (shape.Kind)
            {
                case ArtKind.Poly:
                {
                    var pts = new double[v.Length];
                    for (var i = 0; i + 1 < v.Length; i += 2) { pts[i] = X(v[i]); pts[i + 1] = Y(v[i + 1]); }
                    output.Add(new PolyCommand(pts, C(shape.Fill), C(shape.Stroke), sw, shape.Closed));
                    break;
                }
                case ArtKind.Rect:
                    output.Add(new RectCommand(X(v[0]), Y(v[1]), L(v[2]), L(v[3]), C(shape.Fill), C(shape.Stroke), sw, shape.Radius > 0 ? L(shape.Radius) : null));
                    break;
                case ArtKind.Circle:
                    output.Add(new CircleCommand(X(v[0]), Y(v[1]), L(v[2]), C(shape.Fill), C(shape.Stroke), sw));
                    break;
                case ArtKind.Line:
                    output.Add(new LineCommand(X(v[0]), Y(v[1]), X(v[2]), Y(v[3]), C(shape.Stroke)!, sw ?? 1, shape.Cap));
                    break;
            }
        }
        if (NeedsClip) output.Add(new UnclipCommand());
    }

    private static string Scaled(string color, double factor)
    {
        var alpha = color.Length == 9 ? int.Parse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 255;
        var scaled = (int)Math.Round(alpha * factor);
        return color[..7].ToLowerInvariant() + scaled.ToString("x2", CultureInfo.InvariantCulture);
    }
}

// A symbol slot of the interface as the drawing code sees it: a registered picture (a PNG the module sent to the
// draw layer), vector art (a converted SVG), or nothing. Pictures keep their aspect and fit the slot.
public sealed class Glyph
{
    public static Glyph None { get; } = new(null, 0, 0, null);

    private Glyph(string? imageId, int pictureWidth, int pictureHeight, SymbolArt? art)
    {
        ImageId = imageId; PictureWidth = pictureWidth; PictureHeight = pictureHeight; Art = art;
    }

    public static Glyph Picture(string imageId, int width, int height) =>
        string.IsNullOrEmpty(imageId) || width < 1 || height < 1 ? None : new(imageId, width, height, null);

    public static Glyph Vector(SymbolArt art) => new(null, 0, 0, art ?? throw new ArgumentNullException(nameof(art)));

    public string? ImageId { get; }
    public int PictureWidth { get; }
    public int PictureHeight { get; }
    public SymbolArt? Art { get; }
    public bool IsEmpty => ImageId is null && Art is null;
    public int CommandCount => Art?.CommandCount ?? (ImageId is null ? 0 : 1);

    // The commands that draw it fitted into the slot (centered, aspect kept, never stretched).
    public IReadOnlyList<SceneCommand> Commands(double x, double y, double w, double h, double opacity = 1, string? tint = null)
    {
        var list = new List<SceneCommand>(CommandCount);
        AppendTo(list, x, y, w, h, opacity, tint);
        return list;
    }

    public void AppendTo(List<SceneCommand> output, double x, double y, double w, double h, double opacity = 1, string? tint = null)
    {
        if (Art is not null) { Art.AppendTo(output, x, y, w, h, opacity); return; }
        if (ImageId is null || !(w > 0) || !(h > 0)) return;
        var s = Math.Min(w / PictureWidth, h / PictureHeight);
        double fw = PictureWidth * s, fh = PictureHeight * s;
        var op = double.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1);
        output.Add(new ImageCommand(ImageId, Math.Round(x + (w - fw) / 2, 2), Math.Round(y + (h - fh) / 2, 2), Math.Round(fw, 2), Math.Round(fh, 2),
            op < 1 ? op : null, SceneColor.IsValid(tint) ? tint : null));
    }
}
