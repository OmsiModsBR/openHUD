using System.Globalization;
using System.Text;
using System.Xml;

namespace OpenHud.Art;

// Hard limits of one conversion. Every one is checked while reading, so a hostile file is refused early and
// the work is bounded by them, not by the file.
public sealed record SvgLimits
{
    public long MaxBytes { get; init; } = 256 * 1024;
    public int MaxElements { get; init; } = 2000;
    public int MaxDepth { get; init; } = 32;
    public int MaxAttributes { get; init; } = 64;
    public int MaxTransformFunctions { get; init; } = 16;
    // Flattened points of the whole document, and of one sub-path.
    public int MaxTotalPoints { get; init; } = 8000;
    public int MaxPointsPerShape { get; init; } = 2000;
    // Scene commands one drawing of the symbol may cost (the per-symbol budget).
    public int MaxCommands { get; init; } = 256;
    // Numbers beyond this are refused (user units).
    public double MaxAbsValue { get; init; } = 1e6;
    // Flattening error as a fraction of the larger side of the view box.
    public double FlatnessFraction { get; init; } = 0.0015;

    public static SvgLimits Default { get; } = new();
    // A symbol or brand mark of the pause menu (drawn once or twice per scene).
    public static SvgLimits Symbol { get; } = new() { MaxCommands = 160, MaxTotalPoints = 4000, MaxPointsPerShape = 1500 };
    // An icon (stop, marker kind, transit mode) is drawn many times in one scene: a smaller budget each.
    public static SvgLimits Icon { get; } = new() { MaxCommands = 32, MaxTotalPoints = 600, MaxPointsPerShape = 300 };
}

public sealed record SvgResult(bool Ok, string Reason, SymbolArt? Art, IReadOnlyList<string> Notes)
{
    public static SvgResult Refused(string reason, IReadOnlyList<string>? notes = null) => new(false, reason, null, notes ?? Array.Empty<string>());
}

// A static subset of SVG turned into scene primitives (no raster, nothing for the native layer to decode).
// Drawn: svg (view box), g, a, rect, circle, ellipse, line, polyline, polygon, path (M L H V C S Q T A Z, absolute and
// relative); fill, stroke, stroke-width, opacity, fill-opacity, stroke-opacity, stroke-linecap, color; the `style`
// attribute for those; transform (translate scale rotate skewX skewY matrix); named, hex and rgb() colors.
// Refused as a whole: DOCTYPE and entities, scripts, style sheets, embedded images, external references, event
// handlers. Skipped with a reason: use, filters, masks, clip paths, patterns, gradients, text, markers, animation.
public static class SvgConverter
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> RejectedElements = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "foreignObject", "iframe", "embed", "object", "image", "audio", "video", "canvas" };

    private static readonly HashSet<string> SilentElements = new(StringComparer.OrdinalIgnoreCase)
        { "title", "desc", "metadata", "defs" };

    private static readonly Dictionary<string, string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["use"] = "<use> is not drawn", ["filter"] = "filters are not drawn", ["mask"] = "masks are not drawn", ["clipPath"] = "clip paths are not drawn",
        ["pattern"] = "patterns are not drawn", ["linearGradient"] = "gradients are not drawn", ["radialGradient"] = "gradients are not drawn",
        ["text"] = "text is not drawn", ["tspan"] = "text is not drawn", ["marker"] = "markers are not drawn", ["symbol"] = "<symbol> is not drawn",
        ["switch"] = "<switch> is not drawn", ["animate"] = "animation is not drawn", ["animateTransform"] = "animation is not drawn",
        ["animateMotion"] = "animation is not drawn", ["set"] = "animation is not drawn", ["svg"] = "a nested <svg> is not drawn", ["view"] = "<view> is not drawn",
    };

    private static readonly HashSet<string> Shapes = new(StringComparer.Ordinal) { "rect", "circle", "ellipse", "line", "polyline", "polygon", "path" };

    public static SvgResult Convert(byte[] content, SvgLimits? limits = null)
    {
        limits ??= SvgLimits.Default;
        if (content is null) return SvgResult.Refused("no content");
        if (content.Length > limits.MaxBytes) return SvgResult.Refused("larger than " + limits.MaxBytes / 1024 + " KiB");
        string text;
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        try { text = new UTF8Encoding(false, true).GetString(content, start, content.Length - start); }
        catch (DecoderFallbackException) { return SvgResult.Refused("not valid UTF-8"); }
        if (text.IndexOf('\0') >= 0) return SvgResult.Refused("contains a NUL character");
        return Convert(text, limits);
    }

    public static SvgResult Convert(string text, SvgLimits? limits = null)
    {
        limits ??= SvgLimits.Default;
        var notes = new Notes();
        if (text is null) return SvgResult.Refused("no content");
        // A character is at least one byte, so this is never stricter than the byte limit.
        if (text.Length > limits.MaxBytes) return SvgResult.Refused("larger than " + limits.MaxBytes / 1024 + " KiB");
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        try
        {
            var refusal = Prescan(text);
            if (refusal is not null) return SvgResult.Refused(refusal);
            return new Run(limits, notes).Execute(text);
        }
        catch (ArtLimitException exception) { return SvgResult.Refused(exception.Message, notes.List); }
        catch (XmlException exception) { return SvgResult.Refused("malformed XML: " + Clip(exception.Message, 120), notes.List); }
    }

    // Text-level refusals that must come before any parser sees the file (entities expand inside parsers).
    private static string? Prescan(string text)
    {
        if (text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            return "a DOCTYPE declaration is refused (it can define entities and load external files); remove the <!DOCTYPE ...> line";
        return null;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];

    // A bounded, de-duplicated list of the reasons something was skipped.
    private sealed class Notes
    {
        public const int Max = 12;
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public List<string> List { get; } = new();
        public void Add(string note)
        {
            if (List.Count < Max && seen.Add(note)) List.Add(note);
        }
    }

    private sealed class Style
    {
        public Rgba? Fill = new Rgba(0, 0, 0, 1);
        public Rgba? Stroke;
        public double StrokeWidth = 1;
        public double FillOpacity = 1, StrokeOpacity = 1, Opacity = 1;
        public string? Cap;
        public Rgba Color = new(0, 0, 0, 1);

        public Style Clone() => (Style)MemberwiseClone();
    }

    private sealed class Frame
    {
        public Style Style = new();
        public Affine Matrix = Affine.Identity;
    }

    private sealed class Run
    {
        private readonly SvgLimits limits;
        private readonly Notes notes;
        private readonly List<ArtShape> shapes = new();
        private PointBudget points = default!;
        private double viewW, viewH, tolerance;
        private int elements;

        public Run(SvgLimits limits, Notes notes) { this.limits = limits; this.notes = notes; }

        public SvgResult Execute(string text)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = false,
                MaxCharactersInDocument = Math.Max(1024, limits.MaxBytes * 2),
                MaxCharactersFromEntities = 0,
                CloseInput = true,
            };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var frames = new List<Frame>();
            var skipDepth = -1;
            var sawRoot = false;
            var rootMatrix = Affine.Identity;

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction:
                        if (reader.Name.StartsWith("xml-stylesheet", StringComparison.OrdinalIgnoreCase)) return SvgResult.Refused("style sheets are refused", notes.List);
                        break;
                    case XmlNodeType.DocumentType:
                        return SvgResult.Refused("a DOCTYPE declaration is refused", notes.List);
                    case XmlNodeType.EndElement:
                        if (reader.Depth == skipDepth) skipDepth = -1;
                        break;
                    case XmlNodeType.Element:
                    {
                        if (++elements > limits.MaxElements) return SvgResult.Refused("more than " + limits.MaxElements + " elements");
                        var depth = reader.Depth;
                        if (depth > limits.MaxDepth) return SvgResult.Refused("nested deeper than " + limits.MaxDepth + " levels");
                        var name = reader.LocalName;
                        var empty = reader.IsEmptyElement;
                        var inSvg = reader.NamespaceURI.Length == 0 || reader.NamespaceURI == SvgNamespace;
                        var attributes = ReadAttributes(reader, out var attributeRefusal);
                        if (attributeRefusal is not null) return SvgResult.Refused(attributeRefusal, notes.List);

                        if (!sawRoot)
                        {
                            if (!inSvg || name != "svg") return SvgResult.Refused("the root element is <" + name + ">, not <svg>");
                            sawRoot = true;
                            var refusal = ReadRoot(attributes, out rootMatrix);
                            if (refusal is not null) return SvgResult.Refused(refusal, notes.List);
                            var root = new Frame { Matrix = rootMatrix };
                            ApplyStyle(root.Style, attributes);
                            frames.Clear(); frames.Add(root);
                            continue;
                        }
                        if (skipDepth >= 0) continue;
                        if (!inSvg) { if (!empty) skipDepth = depth; continue; }

                        if (RejectedElements.Contains(name)) return SvgResult.Refused("<" + name + "> is not allowed in a static symbol", notes.List);
                        if (SilentElements.Contains(name)) { if (!empty) skipDepth = depth; continue; }
                        if (SkippedElements.TryGetValue(name, out var why)) { notes.Add(why); if (!empty) skipDepth = depth; continue; }

                        var parent = frames[Math.Min(depth - 1, frames.Count - 1)];
                        var style = parent.Style.Clone();
                        ApplyStyle(style, attributes);
                        if (Hidden(attributes)) { if (!empty) skipDepth = depth; continue; }
                        if (attributes.TryGetValue("filter", out var filter) && filter != "none") { notes.Add("filters are not drawn; the element is skipped"); if (!empty) skipDepth = depth; continue; }
                        if (attributes.ContainsKey("mask") || attributes.ContainsKey("clip-path")) notes.Add("masks and clip paths are ignored");

                        var matrix = parent.Matrix;
                        if (attributes.TryGetValue("transform", out var transform))
                        {
                            if (!TryTransform(transform, out var local)) { notes.Add("an invalid transform; the element is skipped"); if (!empty) skipDepth = depth; continue; }
                            matrix = matrix.Times(local);
                        }
                        if (!matrix.IsFinite) { notes.Add("an overflowing transform; the element is skipped"); if (!empty) skipDepth = depth; continue; }

                        if (Shapes.Contains(name))
                        {
                            DrawShape(name, attributes, style, matrix);
                            if (!empty) skipDepth = depth; // shapes have no drawable children
                            continue;
                        }
                        if (name is "g" or "a")
                        {
                            if (!empty)
                            {
                                while (frames.Count <= depth) frames.Add(new Frame());
                                frames[depth] = new Frame { Style = style, Matrix = matrix };
                            }
                            continue;
                        }
                        notes.Add("<" + name + "> is not drawn");
                        if (!empty) skipDepth = depth;
                        break;
                    }
                }
            }
            if (!sawRoot) return SvgResult.Refused("no <svg> root element");
            if (shapes.Count == 0) return SvgResult.Refused("nothing to draw" + (notes.List.Count > 0 ? " (" + notes.List[0] + ")" : ""), notes.List);
            return new SvgResult(true, "", new SymbolArt(viewW, viewH, shapes, NeedsClip()), notes.List);
        }

        // ---- attributes

        private Dictionary<string, string> ReadAttributes(XmlReader reader, out string? refusal)
        {
            refusal = null;
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (reader.AttributeCount > limits.MaxAttributes) { refusal = "an element has more than " + limits.MaxAttributes + " attributes"; return attributes; }
            while (reader.MoveToNextAttribute())
            {
                var local = reader.LocalName;
                var value = reader.Value;
                if (reader.Prefix == "xmlns" || local == "xmlns") continue;
                if (local.StartsWith("on", StringComparison.OrdinalIgnoreCase) && reader.NamespaceURI.Length == 0) { refusal = "an event handler attribute (" + Clip(local, 20) + ") is refused"; return attributes; }
                if (local.Equals("href", StringComparison.OrdinalIgnoreCase) && !value.TrimStart().StartsWith('#')) { refusal = "an external reference (href) is refused"; return attributes; }
                if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)) { refusal = "a script URL is refused"; return attributes; }
                if (ExternalUrl(value)) { refusal = "an external reference (url()) is refused"; return attributes; }
                if (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != SvgNamespace) continue; // foreign attributes (editor data) carry no drawing
                if (value.Length > limits.MaxBytes) { refusal = "an attribute is longer than the file limit"; return attributes; }
                attributes[local] = value;
            }
            reader.MoveToElement();
            return attributes;
        }

        // url(...) that does not point inside the document.
        private static bool ExternalUrl(string value)
        {
            var at = 0;
            while ((at = value.IndexOf("url(", at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var rest = value.AsSpan(at + 4).TrimStart();
                if (rest.Length > 0 && (rest[0] == '\'' || rest[0] == '"')) rest = rest[1..].TrimStart();
                if (rest.Length == 0 || rest[0] != '#') return true;
                at += 4;
            }
            return false;
        }

        private static bool Hidden(Dictionary<string, string> attributes)
        {
            if (attributes.TryGetValue("display", out var display) && display.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            if (attributes.TryGetValue("visibility", out var visibility) && visibility.Trim() is "hidden" or "collapse") return true;
            if (attributes.TryGetValue("style", out var style))
                foreach (var (name, value) in Declarations(style))
                    if (name == "display" && value.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static IEnumerable<(string Name, string Value)> Declarations(string style)
        {
            foreach (var part in style.Split(';'))
            {
                var colon = part.IndexOf(':');
                if (colon <= 0) continue;
                yield return (part[..colon].Trim().ToLowerInvariant(), part[(colon + 1)..].Trim());
            }
        }

        // ---- the root

        private string? ReadRoot(Dictionary<string, string> a, out Affine matrix)
        {
            matrix = Affine.Identity;
            double x = 0, y = 0, w = 0, h = 0;
            var haveBox = false;
            if (a.TryGetValue("viewBox", out var box))
            {
                var reader = new PathFlattener.Reader(box);
                if (!reader.Number(out x) || !reader.Number(out y) || !reader.Number(out w) || !reader.Number(out h)) return "the viewBox is not four numbers";
                haveBox = true;
            }
            else
            {
                if (a.TryGetValue("width", out var ws) && a.TryGetValue("height", out var hs) && TryLength(ws, out w) && TryLength(hs, out h)) haveBox = true;
                else return "the <svg> has no viewBox and no width and height";
            }
            if (!haveBox || !(w > 0) || !(h > 0) || w > limits.MaxAbsValue || h > limits.MaxAbsValue || Math.Abs(x) > limits.MaxAbsValue || Math.Abs(y) > limits.MaxAbsValue)
                return "the view box must have a positive size within " + limits.MaxAbsValue.ToString("0", CultureInfo.InvariantCulture);
            viewW = w; viewH = h;
            matrix = new Affine(1, 0, 0, 1, -x, -y);
            tolerance = Math.Max(w, h) * limits.FlatnessFraction;
            points = new PointBudget(limits.MaxTotalPoints, limits.MaxPointsPerShape);
            return null;
        }

        private bool TryLength(string text, out double value)
        {
            text = text.Trim();
            if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
            value = 0;
            if (text.Length == 0 || text.Length > PathFlattener.MaxNumberLength) return false;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
            return Math.Abs(value) <= limits.MaxAbsValue;
        }

        // ---- style

        private void ApplyStyle(Style style, Dictionary<string, string> attributes)
        {
            foreach (var name in StyleProperties)
                if (attributes.TryGetValue(name, out var value)) Apply(style, name, value.Trim());
            if (attributes.TryGetValue("style", out var inline))
                foreach (var (name, value) in Declarations(inline))
                    if (Array.IndexOf(StyleProperties, name) >= 0) Apply(style, name, value);
        }

        private static readonly string[] StyleProperties = { "color", "fill", "stroke", "stroke-width", "opacity", "fill-opacity", "stroke-opacity", "stroke-linecap", "stroke-dasharray", "fill-rule" };

        private void Apply(Style style, string name, string value)
        {
            switch (name)
            {
                case "color":
                    if (SvgColors.TryParse(value, style.Color, out var color) && color is { } c) style.Color = c;
                    break;
                case "fill":
                case "stroke":
                {
                    if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) { notes.Add("gradients and patterns are not drawn; that paint is none"); Set(null); break; }
                    if (SvgColors.TryParse(value, style.Color, out var paint)) Set(paint);
                    else notes.Add("an unknown color '" + Clip(value, 24) + "' is ignored");
                    break;
                    void Set(Rgba? p) { if (name == "fill") style.Fill = p; else style.Stroke = p; }
                }
                case "stroke-width":
                    if (TryLength(value, out var width) && width >= 0) style.StrokeWidth = width;
                    break;
                case "opacity":
                    if (TryOpacity(value, out var opacity)) style.Opacity *= opacity;
                    break;
                case "fill-opacity":
                    if (TryOpacity(value, out var fo)) style.FillOpacity = fo;
                    break;
                case "stroke-opacity":
                    if (TryOpacity(value, out var so)) style.StrokeOpacity = so;
                    break;
                case "stroke-linecap":
                    style.Cap = value == "round" ? "round" : null;
                    break;
                case "stroke-dasharray":
                    if (value != "none" && value.Length > 0) notes.Add("dashed strokes are drawn solid");
                    break;
                case "fill-rule":
                    if (value == "evenodd") notes.Add("fill-rule evenodd is drawn as nonzero (holes are not cut out)");
                    break;
            }
        }

        private static bool TryOpacity(string value, out double opacity)
        {
            opacity = 1;
            var percent = value.EndsWith('%');
            if (percent) value = value[..^1];
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) return false;
            if (percent) v /= 100;
            opacity = Math.Clamp(v, 0, 1);
            return true;
        }

        private static string? Hex(Rgba? paint, double opacity)
        {
            if (paint is not { } p) return null;
            var alpha = Math.Clamp(p.A * opacity, 0, 1);
            var a = (int)Math.Round(alpha * 255);
            if (a == 0) return null;
            return a == 255
                ? "#" + p.R.ToString("x2", CultureInfo.InvariantCulture) + p.G.ToString("x2", CultureInfo.InvariantCulture) + p.B.ToString("x2", CultureInfo.InvariantCulture)
                : "#" + p.R.ToString("x2", CultureInfo.InvariantCulture) + p.G.ToString("x2", CultureInfo.InvariantCulture) + p.B.ToString("x2", CultureInfo.InvariantCulture) + a.ToString("x2", CultureInfo.InvariantCulture);
        }

        // ---- transform

        private bool TryTransform(string text, out Affine result)
        {
            result = Affine.Identity;
            var i = 0;
            var functions = 0;
            while (true)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
                if (i >= text.Length) return true;
                if (++functions > limits.MaxTransformFunctions) return false;
                var open = text.IndexOf('(', i);
                var close = open < 0 ? -1 : text.IndexOf(')', open);
                if (open < 0 || close < 0) return false;
                var name = text[i..open].Trim();
                var reader = new PathFlattener.Reader(text[(open + 1)..close]);
                var values = new List<double>(6);
                while (true)
                {
                    reader.SkipSeparators();
                    if (reader.AtEnd) break;
                    if (!reader.Number(out var v) || values.Count >= 6) return false;
                    values.Add(v);
                }
                i = close + 1;
                Affine step;
                switch (name)
                {
                    case "translate" when values.Count is 1 or 2: step = new Affine(1, 0, 0, 1, values[0], values.Count == 2 ? values[1] : 0); break;
                    case "scale" when values.Count is 1 or 2: step = new Affine(values[0], 0, 0, values.Count == 2 ? values[1] : values[0], 0, 0); break;
                    case "rotate" when values.Count is 1 or 3:
                    {
                        var r = values[0] * Math.PI / 180;
                        double cos = Math.Cos(r), sin = Math.Sin(r);
                        var rotation = new Affine(cos, sin, -sin, cos, 0, 0);
                        if (values.Count == 3)
                            rotation = new Affine(1, 0, 0, 1, values[1], values[2]).Times(rotation).Times(new Affine(1, 0, 0, 1, -values[1], -values[2]));
                        step = rotation;
                        break;
                    }
                    case "skewX" when values.Count == 1: step = new Affine(1, 0, Math.Tan(values[0] * Math.PI / 180), 1, 0, 0); break;
                    case "skewY" when values.Count == 1: step = new Affine(1, Math.Tan(values[0] * Math.PI / 180), 0, 1, 0, 0); break;
                    case "matrix" when values.Count == 6: step = new Affine(values[0], values[1], values[2], values[3], values[4], values[5]); break;
                    default: return false;
                }
                if (!step.IsFinite) return false;
                result = result.Times(step);
            }
        }

        // ---- shapes

        private void DrawShape(string name, Dictionary<string, string> a, Style style, Affine matrix)
        {
            var scale = matrix.Scale;
            if (!(scale > 1e-9) || !double.IsFinite(scale)) { notes.Add("a degenerate transform; the element is skipped"); return; }
            var fill = Hex(style.Fill, style.FillOpacity * style.Opacity);
            var stroke = style.StrokeWidth > 0 ? Hex(style.Stroke, style.StrokeOpacity * style.Opacity) : null;
            if (fill is null && stroke is null) return;
            var strokeWidth = stroke is null ? 0 : style.StrokeWidth * scale;
            var user = tolerance / scale;

            // A number that is there but unusable (huge, NaN, a percentage) spoils the element; a missing one is zero.
            var bad = false;
            double Num(string key, double fallback = 0)
            {
                if (!a.TryGetValue(key, out var v)) return fallback;
                if (TryLength(v, out var n)) return n;
                bad = true;
                return fallback;
            }
            bool Spoiled()
            {
                if (bad) notes.Add("<" + name + "> has a number that cannot be used; the element is skipped");
                return bad;
            }

            switch (name)
            {
                case "rect":
                {
                    double x = Num("x"), y = Num("y"), w = Num("width"), h = Num("height");
                    var hasRx = a.ContainsKey("rx") && Num("rx", -1) >= 0;
                    var hasRy = a.ContainsKey("ry") && Num("ry", -1) >= 0;
                    double rx = hasRx ? Num("rx") : hasRy ? Num("ry") : 0;
                    double ry = hasRy ? Num("ry") : hasRx ? Num("rx") : 0;
                    if (Spoiled() || !(w > 0) || !(h > 0)) return;
                    rx = Math.Min(rx, w / 2); ry = Math.Min(ry, h / 2);
                    if (matrix.IsUniformScale && (rx == 0 || Math.Abs(rx - ry) < 1e-9))
                    {
                        var (tx, ty) = matrix.Apply(x, y);
                        AddShape(ArtShape.Rect(tx, ty, w * matrix.A, h * matrix.A, rx * matrix.A, fill, stroke, strokeWidth));
                        return;
                    }
                    var d = rx > 0 && ry > 0
                        ? $"M{F(x + rx)} {F(y)}H{F(x + w - rx)}A{F(rx)} {F(ry)} 0 0 1 {F(x + w)} {F(y + ry)}V{F(y + h - ry)}A{F(rx)} {F(ry)} 0 0 1 {F(x + w - rx)} {F(y + h)}H{F(x + rx)}A{F(rx)} {F(ry)} 0 0 1 {F(x)} {F(y + h - ry)}V{F(y + ry)}A{F(rx)} {F(ry)} 0 0 1 {F(x + rx)} {F(y)}Z"
                        : $"M{F(x)} {F(y)}H{F(x + w)}V{F(y + h)}H{F(x)}Z";
                    AddPath(d, matrix, user, fill, stroke, strokeWidth);
                    return;
                }
                case "circle":
                case "ellipse":
                {
                    double cx = Num("cx"), cy = Num("cy");
                    double rx = name == "circle" ? Num("r") : Num("rx"), ry = name == "circle" ? rx : Num("ry");
                    if (Spoiled() || !(rx > 0) || !(ry > 0)) return;
                    if (Math.Abs(rx - ry) < 1e-9 && matrix.IsSimilarity)
                    {
                        var (tx, ty) = matrix.Apply(cx, cy);
                        AddShape(ArtShape.Circle(tx, ty, rx * scale, fill, stroke, strokeWidth));
                        return;
                    }
                    AddPath($"M{F(cx + rx)} {F(cy)}A{F(rx)} {F(ry)} 0 1 0 {F(cx - rx)} {F(cy)}A{F(rx)} {F(ry)} 0 1 0 {F(cx + rx)} {F(cy)}Z", matrix, user, fill, stroke, strokeWidth);
                    return;
                }
                case "line":
                {
                    double lx1 = Num("x1"), ly1 = Num("y1"), lx2 = Num("x2"), ly2 = Num("y2");
                    if (Spoiled() || stroke is null) return;
                    var (x1, y1) = matrix.Apply(lx1, ly1);
                    var (x2, y2) = matrix.Apply(lx2, ly2);
                    AddShape(ArtShape.Line(x1, y1, x2, y2, stroke, strokeWidth, style.Cap));
                    return;
                }
                case "polyline":
                case "polygon":
                {
                    if (!a.TryGetValue("points", out var list)) return;
                    var reader = new PathFlattener.Reader(list);
                    var coordinates = new List<double>();
                    while (true)
                    {
                        reader.SkipSeparators();
                        if (reader.AtEnd || !reader.Number(out var v)) break;
                        if (Math.Abs(v) > limits.MaxAbsValue) { notes.Add("a coordinate is beyond the limit; the points stop there"); break; }
                        coordinates.Add(v);
                        if (coordinates.Count > 2 * points.PerShape) throw new ArtLimitException("a shape has more than " + points.PerShape + " points");
                    }
                    if (coordinates.Count % 2 == 1) coordinates.RemoveAt(coordinates.Count - 1);
                    if (coordinates.Count < 4) return;
                    if (!points.Spend(coordinates.Count / 2)) throw new ArtLimitException("the document has more points than the limit");
                    var mapped = new double[coordinates.Count];
                    for (var i = 0; i < mapped.Length; i += 2) (mapped[i], mapped[i + 1]) = matrix.Apply(coordinates[i], coordinates[i + 1]);
                    AddShape(ArtShape.Poly(mapped, fill, stroke, strokeWidth, name == "polygon"));
                    return;
                }
                case "path":
                {
                    if (!a.TryGetValue("d", out var data)) return;
                    AddPath(data, matrix, user, fill, stroke, strokeWidth);
                    return;
                }
            }
        }

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private void AddPath(string data, Affine matrix, double userTolerance, string? fill, string? stroke, double strokeWidth)
        {
            var result = PathFlattener.Flatten(data, matrix, userTolerance, points, limits.MaxAbsValue);
            if (result.Error is not null) notes.Add("path data stops early: " + result.Error);
            foreach (var sub in result.Subpaths)
            {
                // A lone moveto with nothing to stroke or fill is not a shape.
                if (sub.Points.Length < 4 && !(sub.Closed && sub.Points.Length >= 2)) continue;
                AddShape(ArtShape.Poly(sub.Points, fill, stroke, strokeWidth, sub.Closed));
            }
        }

        private void AddShape(ArtShape shape)
        {
            foreach (var v in shape.Values)
                if (!double.IsFinite(v)) { notes.Add("a shape with a non-finite coordinate is skipped"); return; }
            if (shapes.Count + 1 > limits.MaxCommands) throw new ArtLimitException("needs more than " + limits.MaxCommands + " drawing commands (the per-symbol budget); simplify the drawing");
            shapes.Add(shape);
        }

        private bool NeedsClip()
        {
            var slack = Math.Max(viewW, viewH) * 0.005;
            foreach (var shape in shapes)
            {
                var v = shape.Values;
                switch (shape.Kind)
                {
                    case ArtKind.Circle:
                        if (v[0] - v[2] < -slack || v[0] + v[2] > viewW + slack || v[1] - v[2] < -slack || v[1] + v[2] > viewH + slack) return true;
                        break;
                    case ArtKind.Rect:
                        if (v[0] < -slack || v[1] < -slack || v[0] + v[2] > viewW + slack || v[1] + v[3] > viewH + slack) return true;
                        break;
                    default:
                        for (var i = 0; i + 1 < v.Length; i += 2)
                            if (v[i] < -slack || v[i] > viewW + slack || v[i + 1] < -slack || v[i + 1] > viewH + slack) return true;
                        break;
                }
            }
            return false;
        }
    }
}
