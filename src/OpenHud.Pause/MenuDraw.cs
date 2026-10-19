using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenHud.Pause;

public enum TextRole { Regular, Medium, Bold, Black, Condensed }
public enum TextAlign { Left, Center, Right }
public enum TextBase { Top, Middle, Alphabetic }

// One drawing command of the menu. Each record maps to exactly one command of
// docs/scene-protocol.md (rect, circle, line, text, image, clip, unclip), with
// the same members; MenuSceneJson writes them.
public abstract record MenuDraw;

public sealed record RectDraw(double X, double Y, double W, double H, string? Fill = null, string? Stroke = null, double StrokeWidth = 0, double Radius = 0) : MenuDraw;

public sealed record CircleDraw(double Cx, double Cy, double R, string? Fill = null, string? Stroke = null, double StrokeWidth = 0) : MenuDraw;

public sealed record LineDraw(double X1, double Y1, double X2, double Y2, string Color, double Width) : MenuDraw;

public sealed record TextDraw(double X, double Y, string Text, double Size, TextRole Role, string Color,
    TextAlign Align = TextAlign.Left, TextBase Base = TextBase.Middle, double MaxWidth = 0, string? Outline = null) : MenuDraw;

public sealed record ImageDraw(string Id, double X, double Y, double W, double H, double Opacity = 1, string? Tint = null) : MenuDraw;

public sealed record ClipDraw(double X, double Y, double W, double H) : MenuDraw;

public sealed record UnclipDraw : MenuDraw;

// A scene command the menu draws as it is: the primitives of a vector symbol (poly, clip, ...) that have no record of their own.
public sealed record RawDraw(OpenHud.Scene.SceneCommand Command) : MenuDraw;

public readonly record struct MenuRect(double X, double Y, double W, double H)
{
    public bool Contains(double px, double py) => px >= X && px < X + W && py >= Y && py < Y + H;
}

// A named area of the canvas the integrator fills with its own scene (the map,
// the lines list), in canvas coordinates.
public sealed record MenuRegion(string Name, MenuRect Rect);

// None draws nothing; Menu is the menu over the captured background;
// MapBehind is the provisional mode: the native menu is in front, only the map
// (a region named "map" covering the canvas) is drawn behind it.
public enum MenuSceneMode { None, Menu, MapBehind }

public sealed record MenuScene(MenuSceneMode Mode, int Width, int Height, bool Background,
    IReadOnlyList<MenuDraw> Commands, IReadOnlyList<MenuRegion> Regions)
{
    public const int CanvasWidth = 1920;
    public const int CanvasHeight = 1080;
    public const int MaxCommands = 8192;

    public static readonly MenuScene None = new(MenuSceneMode.None, CanvasWidth, CanvasHeight, false, Array.Empty<MenuDraw>(), Array.Empty<MenuRegion>());

    public static readonly MenuScene MapBehind = new(MenuSceneMode.MapBehind, CanvasWidth, CanvasHeight, false, Array.Empty<MenuDraw>(),
        new[] { new MenuRegion("map", new MenuRect(0, 0, CanvasWidth, CanvasHeight)) });

    public MenuRegion? Region(string name) => Regions.FirstOrDefault(r => r.Name == name);
}

// The scene document of docs/scene-protocol.md.
public static class MenuSceneJson
{
    public static string Serialize(MenuScene scene)
    {
        if (scene.Commands.Count > MenuScene.MaxCommands) throw new ArgumentException("the scene has more than " + MenuScene.MaxCommands + " commands");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteNumber("w", scene.Width);
            w.WriteNumber("h", scene.Height);
            w.WriteBoolean("background", scene.Background);
            w.WriteStartArray("cmds");
            foreach (var cmd in scene.Commands) Write(w, cmd);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Num(Utf8JsonWriter w, string name, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("non-finite number in member " + name);
        w.WritePropertyName(name);
        // Two decimals are far below a pixel; invariant culture keeps the text stable.
        w.WriteRawValue((Math.Round(value, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture));
    }

    // The scene is UTF-8: a lone surrogate (invalid UTF-16) becomes U+FFFD instead of failing the whole scene.
    private static string Clean(string text)
    {
        StringBuilder? fixedText = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool bad = char.IsHighSurrogate(c) ? i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]) : char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(text[i - 1]));
            if (bad) { fixedText ??= new StringBuilder(text, 0, i, text.Length); fixedText.Append('�'); }
            else fixedText?.Append(c);
        }
        return fixedText?.ToString() ?? text;
    }

    private static string RoleName(TextRole role) => role switch
    {
        TextRole.Medium => "medium", TextRole.Bold => "bold", TextRole.Black => "black", TextRole.Condensed => "condensed", _ => "regular",
    };

    private static void Write(Utf8JsonWriter w, MenuDraw cmd)
    {
        if (cmd is RawDraw raw)
        {
            w.WriteRawValue(OpenHud.Scene.SceneJson.EncodeCommand(raw.Command));
            return;
        }
        w.WriteStartObject();
        switch (cmd)
        {
            case RectDraw r:
                w.WriteString("t", "rect");
                Num(w, "x", r.X); Num(w, "y", r.Y); Num(w, "w", r.W); Num(w, "h", r.H);
                if (r.Fill != null) w.WriteString("fill", r.Fill);
                if (r.Stroke != null) { w.WriteString("stroke", r.Stroke); Num(w, "sw", r.StrokeWidth); }
                if (r.Radius > 0) Num(w, "r", r.Radius);
                break;
            case CircleDraw c:
                w.WriteString("t", "circle");
                Num(w, "cx", c.Cx); Num(w, "cy", c.Cy); Num(w, "r", c.R);
                if (c.Fill != null) w.WriteString("fill", c.Fill);
                if (c.Stroke != null) { w.WriteString("stroke", c.Stroke); Num(w, "sw", c.StrokeWidth); }
                break;
            case LineDraw l:
                w.WriteString("t", "line");
                Num(w, "x1", l.X1); Num(w, "y1", l.Y1); Num(w, "x2", l.X2); Num(w, "y2", l.Y2);
                w.WriteString("color", l.Color); Num(w, "sw", l.Width);
                break;
            case TextDraw t:
                w.WriteString("t", "text");
                Num(w, "x", t.X); Num(w, "y", t.Y);
                w.WriteString("s", Clean(t.Text));
                Num(w, "size", t.Size);
                w.WriteString("font", RoleName(t.Role));
                w.WriteString("color", t.Color);
                w.WriteString("align", t.Align == TextAlign.Center ? "c" : t.Align == TextAlign.Right ? "r" : "l");
                w.WriteString("base", t.Base == TextBase.Top ? "top" : t.Base == TextBase.Alphabetic ? "alpha" : "mid");
                if (t.MaxWidth > 0) Num(w, "maxw", t.MaxWidth);
                if (t.Outline != null) w.WriteString("outline", t.Outline);
                break;
            case ImageDraw i:
                w.WriteString("t", "image");
                w.WriteString("id", Clean(i.Id));
                Num(w, "x", i.X); Num(w, "y", i.Y); Num(w, "w", i.W); Num(w, "h", i.H);
                if (i.Opacity < 1) Num(w, "op", i.Opacity);
                if (i.Tint != null) w.WriteString("tint", i.Tint);
                break;
            case ClipDraw k:
                w.WriteString("t", "clip");
                w.WriteString("kind", "rect");
                Num(w, "x", k.X); Num(w, "y", k.Y); Num(w, "w", k.W); Num(w, "h", k.H);
                break;
            case UnclipDraw:
                w.WriteString("t", "unclip");
                break;
            default:
                throw new ArgumentException("unknown draw record " + cmd.GetType().Name);
        }
        w.WriteEndObject();
    }
}
