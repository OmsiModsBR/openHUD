using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenHud.Art;
using OpenHud.Packages;
using OpenHud.Scene;

namespace OpenHud.Tests.Core;

// The SVG subset converter: what it draws, what it refuses, and that hostile files cost bounded work.
internal static class SvgGates
{
    public static void Register()
    {
        Gates.Run("svg.shapes", Shapes);
        Gates.Run("svg.paths-every-command", PathCommands);
        Gates.Run("svg.path-grammar-edge-cases", PathGrammar);
        Gates.Run("svg.transforms", Transforms);
        Gates.Run("svg.colors-and-opacity", Colors);
        Gates.Run("svg.view-box", ViewBox);
        Gates.Run("svg.limits", Limits);
        Gates.Run("svg.refusals-and-skips", Refusals);
        Gates.Run("svg.hostile-inputs", Hostile);
        Gates.Run("svg.tolerates-declaration-comments-bom", Tolerated);
        Gates.Run("svg.doctype-message-is-precise", DoctypeMessage);
        Gates.Run("svg.flattening-bounds-property", FlatteningProperty);
        Gates.Run("svg.random-path-data-never-escapes-its-budget", RandomPathData);
        Gates.Run("svg.emits-valid-scene-commands-into-a-slot", EmitIntoSlot);
        Gates.Run("svg.glyph-fits-pictures-and-vectors", GlyphFits);
    }

    private static string Doc(string inner, string viewBox = "0 0 100 100", string rootAttributes = "") =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"" + viewBox + "\" " + rootAttributes + ">" + inner + "</svg>";

    private static SymbolArt Ok(string svg, SvgLimits? limits = null)
    {
        var result = SvgConverter.Convert(svg, limits);
        Expect.True(result.Ok, "converts: " + result.Reason + " :: " + svg[..Math.Min(svg.Length, 120)]);
        return result.Art!;
    }

    private static string Refused(string svg, SvgLimits? limits = null)
    {
        var result = SvgConverter.Convert(svg, limits);
        Expect.True(!result.Ok && result.Art is null, "is refused: " + svg[..Math.Min(svg.Length, 120)]);
        Expect.True(result.Reason.Length > 0, "a refusal says why");
        return result.Reason;
    }

    private static double[] Pts(ArtShape shape) => shape.Values;

    private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- shapes

    private static void Shapes()
    {
        var art = Ok(Doc(
            "<rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" fill=\"#ff0000\"/>" +
            "<rect x=\"1\" y=\"2\" width=\"20\" height=\"10\" rx=\"3\" fill=\"blue\" stroke=\"black\" stroke-width=\"2\"/>" +
            "<circle cx=\"50\" cy=\"50\" r=\"10\" fill=\"lime\"/>" +
            "<ellipse cx=\"60\" cy=\"70\" rx=\"20\" ry=\"8\" fill=\"#00f\"/>" +
            "<line x1=\"0\" y1=\"0\" x2=\"100\" y2=\"50\" stroke=\"#123456\" stroke-width=\"3\" stroke-linecap=\"round\"/>" +
            "<polyline points=\"0,0 10,10 20,0\" fill=\"none\" stroke=\"red\"/>" +
            "<polygon points=\"0 0 10 0 10 10\" fill=\"#0f0\"/>" +
            "<path d=\"M10 10 L20 10 L20 20 Z\" fill=\"#abc\"/>"));
        var s = art.Shapes;
        Expect.Equal(8, s.Count, "one shape each");
        Expect.True(s[0].Kind == ArtKind.Rect && Pts(s[0]).SequenceEqual(new double[] { 10, 20, 30, 40 }) && s[0].Fill == "#ff0000" && s[0].Stroke is null, "plain rect");
        Expect.True(s[1].Kind == ArtKind.Rect && s[1].Radius == 3 && s[1].Stroke == "#000000" && s[1].StrokeWidth == 2 && s[1].Fill == "#0000ff", "rounded rect with stroke");
        Expect.True(s[2].Kind == ArtKind.Circle && Pts(s[2]).SequenceEqual(new double[] { 50, 50, 10 }) && s[2].Fill == "#00ff00", "circle");
        Expect.True(s[3].Kind == ArtKind.Poly && s[3].Closed && s[3].Values.Length >= 16, "an ellipse is a closed polygon");
        double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
        for (var i = 0; i < s[3].Values.Length; i += 2) { minX = Math.Min(minX, s[3].Values[i]); maxX = Math.Max(maxX, s[3].Values[i]); minY = Math.Min(minY, s[3].Values[i + 1]); maxY = Math.Max(maxY, s[3].Values[i + 1]); }
        Expect.Near(40, minX, 0.01, "ellipse left"); Expect.Near(80, maxX, 0.01, "ellipse right"); Expect.Near(62, minY, 0.2, "ellipse top"); Expect.Near(78, maxY, 0.2, "ellipse bottom");
        Expect.True(s[4].Kind == ArtKind.Line && s[4].Stroke == "#123456" && s[4].Cap == "round" && Pts(s[4]).SequenceEqual(new double[] { 0, 0, 100, 50 }), "line with a round cap");
        Expect.True(s[5].Kind == ArtKind.Poly && !s[5].Closed && s[5].Fill is null && s[5].Stroke == "#ff0000" && s[5].Values.Length == 6, "polyline is open");
        Expect.True(s[6].Kind == ArtKind.Poly && s[6].Closed && s[6].Fill == "#00ff00", "polygon is closed");
        Expect.True(s[7].Kind == ArtKind.Poly && s[7].Closed && s[7].Fill == "#aabbcc" && s[7].Values.Length == 6, "path with Z is closed");
        Expect.True(!art.NeedsClip, "everything lies inside the view box");

        // Zero or negative sizes draw nothing; missing geometry too.
        Expect.True(Refused(Doc("<rect width=\"0\" height=\"5\"/><circle r=\"-1\"/><ellipse rx=\"3\"/><polygon points=\"1 2\"/>")).StartsWith("nothing to draw"), "degenerate shapes draw nothing");
        // An open path keeps its fill implicitly closed by the drawing layer; a lone moveto is no shape.
        Expect.Equal(1, Ok(Doc("<path d=\"M1 1 M5 5 L9 9\" stroke=\"red\"/>")).Shapes.Count, "a lone moveto is not a shape");
        // rx only, ry only, clamped to half the side.
        var rounded = Ok(Doc("<rect width=\"20\" height=\"10\" rx=\"50\" ry=\"50\"/>")).Shapes[0];
        Expect.True(rounded.Kind == ArtKind.Poly, "unequal clamped radii (rx 10, ry 5) take the polygon path");
        var half = Ok(Doc("<rect width=\"20\" height=\"20\" rx=\"50\"/>")).Shapes[0];
        Expect.True(half.Kind == ArtKind.Rect && half.Radius == 10, "a radius is clamped to half the side");
    }

    // ---------------------------------------------------------------- paths

    private static double[] OnePath(string d, string attrs = "fill=\"#000\"")
    {
        var art = Ok(Doc("<path d=\"" + d + "\" " + attrs + "/>"));
        Expect.True(art.Shapes.Count >= 1, "a path draws");
        return art.Shapes[0].Values;
    }

    private static void PathCommands()
    {
        // Lines: absolute and relative say the same.
        var abs = OnePath("M10 10 L30 10 H50 V40 L50 60 Z");
        var rel = OnePath("m10 10 l20 0 h20 v30 l0 20 z");
        Expect.True(abs.SequenceEqual(rel), "absolute and relative lines agree");
        Expect.True(abs.SequenceEqual(new double[] { 10, 10, 30, 10, 50, 10, 50, 40, 50, 60 }), "H and V keep the other coordinate");
        // Implicit repetition: after M the extra pairs are L; after l they stay l.
        Expect.True(OnePath("M0 0 10 0 10 10").SequenceEqual(new double[] { 0, 0, 10, 0, 10, 10 }), "M repeats as L");
        Expect.True(OnePath("m0 0 10 0 0 10").SequenceEqual(new double[] { 0, 0, 10, 0, 10, 10 }), "m repeats as relative l");

        // Cubic: ends on the target, passes through the control hull, absolute = relative.
        var cubic = OnePath("M0 0 C0 40 40 40 40 0");
        Expect.True(cubic.Length >= 8 && cubic[^2] == 40 && cubic[^1] == 0, "the cubic ends on its target");
        var cubicRel = OnePath("m0 0 c0 40 40 40 40 0");
        Expect.True(cubic.SequenceEqual(cubicRel), "relative cubic agrees");
        var peak = 0.0;
        for (var i = 1; i < cubic.Length; i += 2) peak = Math.Max(peak, cubic[i]);
        Expect.Near(30, peak, 0.5, "the cubic's apex is 3/4 of the control height");

        // S reflects the previous control point; without a previous curve the control point is the current point.
        var smooth = OnePath("M0 0 C0 20 20 20 20 0 S40 -20 40 0");
        var explicitSecond = OnePath("M0 0 C0 20 20 20 20 0 C20 -20 40 -20 40 0");
        Expect.True(smooth.SequenceEqual(explicitSecond), "S reflects the last control point");
        var lonelyS = OnePath("M0 0 S20 20 40 0");
        var asC = OnePath("M0 0 C0 0 20 20 40 0");
        Expect.True(lonelyS.SequenceEqual(asC), "S after a non-curve uses the current point");

        // Q and T.
        var quad = OnePath("M0 0 Q20 40 40 0");
        Expect.True(quad[^2] == 40 && quad[^1] == 0, "the quad ends on its target");
        Expect.Near(20, quad.Where((_, i) => i % 2 == 1).Max(), 0.5, "the quad's apex is half the control height");
        var t = OnePath("M0 0 Q10 20 20 0 T40 0");
        var q2 = OnePath("M0 0 Q10 20 20 0 Q30 -20 40 0");
        Expect.True(t.SequenceEqual(q2), "T reflects the last control point");
        Expect.True(OnePath("m0 0 q10 20 20 0 t20 0").SequenceEqual(t), "relative q and t agree");
        var lonelyT = OnePath("M0 0 T40 0");
        Expect.True(lonelyT[^2] == 40 && lonelyT[^1] == 0 && Enumerable.Range(0, lonelyT.Length / 2).All(i => lonelyT[2 * i + 1] == 0), "T alone is a straight line (control point = current point)");

        // Arc: a half circle of radius 10 from (0,0) to (20,0), both sweeps, all points on the circle.
        foreach (var sweep in new[] { 0, 1 })
        {
            var arc = OnePath("M0 0 A10 10 0 0 " + sweep + " 20 0");
            Expect.True(arc[^2] == 20 && arc[^1] == 0, "the arc ends on its target");
            for (var i = 2; i < arc.Length; i += 2)
                Expect.Near(10, Math.Sqrt((arc[i] - 10) * (arc[i] - 10) + arc[i + 1] * arc[i + 1]), 0.02, "arc point on the circle");
            Expect.True((sweep == 1 ? arc.Where((_, i) => i % 2 == 1).Min() < -5 : arc.Where((_, i) => i % 2 == 1).Max() > 5), "sweep picks the side");
        }
        // Large-arc flag picks the long way round; radii too small are scaled up; zero radius is a line.
        var large = OnePath("M0 0 A10 10 0 1 1 10 10");
        var small = OnePath("M0 0 A10 10 0 0 1 10 10");
        Expect.True(large.Length > small.Length, "the large arc has more points than the small one");
        var scaledUp = OnePath("M0 0 A1 1 0 0 1 20 0");
        Expect.Near(10, scaledUp.Where((_, i) => i % 2 == 1).Select(Math.Abs).Max(), 0.1, "too-small radii grow to the half chord");
        Expect.True(OnePath("M0 0 A0 5 0 0 1 20 0").SequenceEqual(new double[] { 0, 0, 20, 0 }), "a zero radius is a straight line");
        Expect.True(OnePath("M0 0 a10 10 0 0 1 20 0").SequenceEqual(OnePath("M0 0 A10 10 0 0 1 20 0")), "relative arc agrees");
        // Rotated ellipse arc stays on its ellipse.
        var tilted = OnePath("M0 0 A20 10 30 0 1 30 0");
        Expect.True(tilted[^2] == 30 && tilted.Length > 6, "an ellipse arc is flattened into several points");

        // Closed sub-paths and several sub-paths.
        var two = Ok(Doc("<path d=\"M0 0 L10 0 L10 10 Z M50 50 L60 50 L60 60 Z\" fill=\"#000\"/>"));
        Expect.Equal(2, two.Shapes.Count, "two sub-paths are two shapes");
        // After Z a drawing command continues from the start point.
        var after = Ok(Doc("<path d=\"M10 10 L20 10 L20 20 Z L10 30\" stroke=\"red\" fill=\"none\"/>"));
        Expect.Equal(2, after.Shapes.Count, "a command after Z starts a new sub-path at the start point");
        Expect.True(after.Shapes[1].Values.SequenceEqual(new double[] { 10, 10, 10, 30 }), "...from the closed path's first point");
    }

    private static void PathGrammar()
    {
        // Numbers without separators, exponents, leading dots, signs as separators.
        Expect.True(OnePath("M1.5.5L-1-2").SequenceEqual(new double[] { 1.5, 0.5, -1, -2 }), "1.5.5 is two numbers; -1-2 is two numbers");
        Expect.True(OnePath("M1e1 2E0L+3,4").SequenceEqual(new double[] { 10, 2, 3, 4 }), "exponents and explicit plus signs");
        Expect.True(OnePath("M 0,0\n\tL\r\n5   5").SequenceEqual(new double[] { 0, 0, 5, 5 }), "whitespace of every kind separates");
        // Arc flags need no separator: a1 1 0 00.5.5 means flags 0 0 then 0.5 0.5.
        var compact = OnePath("M0 0 a1 1 0 00.5.5");
        var spaced = OnePath("M0 0 a1 1 0 0 0 0.5 0.5");
        Expect.True(compact.SequenceEqual(spaced), "packed arc flags");
        // Errors keep what was read before them and report once.
        var result = SvgConverter.Convert(Doc("<path d=\"M0 0 L10 10 L20\" fill=\"#000\"/>"));
        Expect.True(result.Ok && result.Art!.Shapes[0].Values.SequenceEqual(new double[] { 0, 0, 10, 10 }), "a path stops at its first error and keeps the rest");
        Expect.True(result.Notes.Any(n => n.Contains("path data stops early")), "...and says so: " + string.Join("|", result.Notes));
        // Must start with M; unknown command; empty data.
        Expect.True(!SvgConverter.Convert(Doc("<path d=\"L10 10\" fill=\"#000\"/>")).Ok, "path data must start with M");
        Expect.True(!SvgConverter.Convert(Doc("<path d=\"M0 0 X5 5\" stroke=\"red\"/>")).Ok, "an unknown command stops the path (nothing before it is a shape)");
        Expect.True(!SvgConverter.Convert(Doc("<path d=\"\" fill=\"#000\"/>")).Ok, "empty data draws nothing");
        // Numbers: NaN, Infinity, lone sign, double dots, too long, huge.
        foreach (var bad in new[] { "M NaN 0 L1 1", "M0 0 L Infinity 1", "M0 0 L - 5", "M0 0 L1..5 2", "M0 0 L" + new string('1', 60) + " 0", "M0 0 L1e999 0", "M0 0 L2e6 0", "M0 0 L1 1e-999999999999" })
        {
            var r = SvgConverter.Convert(Doc("<path d=\"" + bad + "\" stroke=\"red\"/>"));
            Expect.True(!r.Ok || r.Art!.Shapes.All(s => s.Values.All(v => double.IsFinite(v) && Math.Abs(v) <= 1e6)), "bad number is contained: " + bad);
        }
        Expect.True(SvgConverter.Convert(Doc("<path d=\"M0 0 L1 1e-999999999999\" stroke=\"red\"/>")).Ok, "an underflowing exponent is zero, not an error");
    }

    // ---------------------------------------------------------------- transforms

    private static void Transforms()
    {
        var a = Ok(Doc("<g transform=\"translate(10 20)\"><rect width=\"5\" height=\"5\" fill=\"red\"/></g>")).Shapes[0];
        Expect.True(a.Values.SequenceEqual(new double[] { 10, 20, 5, 5 }), "translate on a group moves a rect");
        var b = Ok(Doc("<g transform=\"scale(2)\"><rect x=\"5\" y=\"5\" width=\"5\" height=\"5\" stroke=\"red\" stroke-width=\"1\" fill=\"none\"/></g>")).Shapes[0];
        Expect.True(b.Values.SequenceEqual(new double[] { 10, 10, 10, 10 }) && b.StrokeWidth == 2, "scale scales the rect and the stroke");
        var nested = Ok(Doc("<g transform=\"translate(10 0)\"><g transform=\"scale(2)\"><circle cx=\"5\" cy=\"5\" r=\"3\" fill=\"red\"/></g></g>")).Shapes[0];
        Expect.True(nested.Kind == ArtKind.Circle && nested.Values.SequenceEqual(new double[] { 20, 10, 6 }), "nested groups compose (outer translate after inner scale)");
        var rotated = Ok(Doc("<rect x=\"-5\" y=\"-5\" width=\"10\" height=\"10\" fill=\"red\" transform=\"translate(50 50) rotate(45)\"/>")).Shapes[0];
        Expect.True(rotated.Kind == ArtKind.Poly && rotated.Closed && rotated.Values.Length == 8, "a rotated rect is a polygon");
        Expect.Near(50, rotated.Values[0], 1e-9, "rotated corner x"); Expect.Near(50 - 5 * Math.Sqrt(2), rotated.Values[1], 1e-9, "rotated corner y");
        var aboutPoint = Ok(Doc("<path d=\"M10 0 L20 0\" stroke=\"red\" transform=\"rotate(90 10 0)\"/>")).Shapes[0].Values;
        Expect.Near(10, aboutPoint[0], 1e-9, "rotate about a point keeps that point"); Expect.Near(0, aboutPoint[1], 1e-9, "...y");
        Expect.Near(10, aboutPoint[2], 1e-9, "the other end swings round"); Expect.Near(10, aboutPoint[3], 1e-9, "...to (10,10)");
        var skew = Ok(Doc("<path d=\"M0 10 L10 10\" stroke=\"red\" transform=\"skewX(45)\"/>")).Shapes[0].Values;
        Expect.Near(10, skew[0], 1e-9, "skewX shifts x by y"); Expect.Near(20, skew[2], 1e-9, "skewX second point");
        var skewY = Ok(Doc("<path d=\"M10 0 L10 10\" stroke=\"red\" transform=\"skewY(45)\"/>")).Shapes[0].Values;
        Expect.Near(10, skewY[1], 1e-9, "skewY shifts y by x");
        var matrix = Ok(Doc("<path d=\"M1 1 L2 2\" stroke=\"red\" transform=\"matrix(1 0 0 1 5 6)\"/>")).Shapes[0].Values;
        Expect.True(matrix.SequenceEqual(new double[] { 6, 7, 7, 8 }), "matrix");
        // A circle under a non-uniform scale becomes an ellipse polygon; under a rotation it stays a circle.
        var stretched = Ok(Doc("<circle cx=\"20\" cy=\"20\" r=\"10\" fill=\"red\" transform=\"scale(2 1)\"/>")).Shapes[0];
        Expect.True(stretched.Kind == ArtKind.Poly, "non-uniform scale makes an ellipse");
        var xs = Enumerable.Range(0, stretched.Values.Length / 2).Select(i => stretched.Values[2 * i]).ToArray();
        Expect.Near(20, xs.Min(), 0.01, "ellipse left"); Expect.Near(60, xs.Max(), 0.01, "ellipse right");
        Expect.True(Ok(Doc("<circle cx=\"20\" cy=\"20\" r=\"10\" fill=\"red\" transform=\"rotate(30) scale(1.5)\"/>")).Shapes[0].Kind == ArtKind.Circle, "similarity keeps a circle");
        // Reflection keeps a circle, flips a rect into a polygon.
        Expect.True(Ok(Doc("<rect width=\"5\" height=\"5\" fill=\"red\" transform=\"scale(-1 1)\"/>")).Shapes[0].Kind == ArtKind.Poly, "a mirrored rect is a polygon");
        // Invalid or hostile transforms skip the element, with a reason.
        foreach (var bad in new[] { "rotate(", "translate(a)", "foo(1)", "scale(1 2 3)", "matrix(1 2 3)", "translate(1e999)", "scale(0)" })
        {
            var r = SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"red\" transform=\"" + bad + "\"/><circle cx=\"5\" cy=\"5\" r=\"2\"/>"));
            Expect.True(r.Ok && r.Art!.Shapes.Count == 1 && r.Art.Shapes[0].Kind == ArtKind.Circle, "the element with '" + bad + "' is skipped, the rest stays");
            Expect.True(r.Notes.Count > 0, "...with a reason: " + bad);
        }
        Expect.True(!SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" transform=\"" + string.Concat(Enumerable.Repeat("translate(1)", 17)) + "\"/>")).Ok, "more than 16 transform functions are not read");
    }

    // ---------------------------------------------------------------- colors

    private static void Colors()
    {
        string Fill(string value, string root = "") => Ok(Doc("<rect width=\"5\" height=\"5\" stroke=\"#000\" fill=\"" + value + "\"/>", rootAttributes: root)).Shapes[0].Fill ?? "none";
        Expect.Equal("#ff0000", Fill("#f00"), "#rgb");
        Expect.Equal("#ff000088", Fill("#f008"), "#rgba (the digit doubles)");
        Expect.Equal("#123456", Fill("#123456"), "#rrggbb");
        Expect.Equal("#12345678", Fill("#12345678"), "#rrggbbaa");
        Expect.Equal("#ff0000", Fill("RED"), "names are case-insensitive");
        Expect.Equal("#808080", Fill("grey"), "grey");
        Expect.Equal("#0000ff", Fill("rgb(0, 0, 255)"), "rgb()");
        Expect.Equal("#0000ff", Fill("rgb(0 0 255)"), "rgb() with spaces");
        Expect.Equal("#ff000080", Fill("rgba(255,0,0,0.5)"), "rgba()");
        Expect.Equal("#ff0000", Fill("rgb(100%, 0%, 0%)"), "percent channels");
        Expect.Equal("#ff000080", Fill("rgb(255 0 0 / 50%)"), "slash alpha");
        Expect.Equal("none", Fill("none"), "none");
        Expect.Equal("none", Ok(Doc("<rect width=\"5\" height=\"5\" fill=\"transparent\" stroke=\"red\"/>")).Shapes[0].Fill ?? "none", "transparent is no fill");
        Expect.Equal("#00ff00", Fill("currentColor", "color=\"lime\""), "currentColor reads the color property");
        Expect.Equal("#000000", Fill("currentColor"), "currentColor defaults to black");
        // Unknown colors keep the inherited paint and leave a note.
        var unknown = SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"notacolor\"/>"));
        Expect.True(unknown.Ok && unknown.Art!.Shapes[0].Fill == "#000000" && unknown.Notes.Any(n => n.Contains("unknown color")), "an unknown color is ignored with a note");
        // Default paints: fill black, stroke none; a shape with neither draws nothing.
        Expect.Equal("#000000", Ok(Doc("<rect width=\"5\" height=\"5\"/>")).Shapes[0].Fill!, "default fill is black");
        Expect.True(!SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"none\"/>")).Ok, "no fill and no stroke draws nothing");
        Expect.True(!SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"none\" stroke=\"red\" stroke-width=\"0\"/>")).Ok, "a zero stroke width is no stroke");
        // Inheritance and the style attribute.
        var inherited = Ok(Doc("<g fill=\"#00f\" stroke=\"#f00\" stroke-width=\"4\"><rect width=\"5\" height=\"5\"/><rect width=\"5\" height=\"5\" fill=\"#0f0\"/></g>")).Shapes;
        Expect.True(inherited[0].Fill == "#0000ff" && inherited[0].Stroke == "#ff0000" && inherited[0].StrokeWidth == 4, "children inherit the group's paint");
        Expect.True(inherited[1].Fill == "#00ff00" && inherited[1].Stroke == "#ff0000", "a child may override one property");
        var styled = Ok(Doc("<rect width=\"5\" height=\"5\" fill=\"red\" style=\"fill:#00f; stroke : #0f0 ;stroke-width:3;unknown:1\"/>")).Shapes[0];
        Expect.True(styled.Fill == "#0000ff" && styled.Stroke == "#00ff00" && styled.StrokeWidth == 3, "style beats the attribute; unknown properties are ignored");
        // Opacity multiplies down the tree and into the alpha.
        var faded = Ok(Doc("<g opacity=\"0.5\"><rect width=\"5\" height=\"5\" fill=\"#ff0000\" fill-opacity=\"0.5\" opacity=\"0.5\"/></g>")).Shapes[0];
        Expect.Equal("#ff000020", faded.Fill!, "0.5 * 0.5 * 0.5 of 255 is 32");
        Expect.Equal("#ff0000", Ok(Doc("<rect width=\"5\" height=\"5\" fill=\"#f00\" opacity=\"7\"/>")).Shapes[0].Fill!, "opacity is clamped to 1");
        Expect.True(!SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"#f00\" opacity=\"0\"/>")).Ok, "opacity 0 draws nothing");
        Expect.Equal("#ff000080", Ok(Doc("<rect width=\"5\" height=\"5\" fill=\"#f00\" opacity=\"50%\"/>")).Shapes[0].Fill!, "percent opacity");
        // display none / visibility hidden skip the subtree.
        Expect.Equal(1, Ok(Doc("<g display=\"none\"><rect width=\"5\" height=\"5\"/></g><rect width=\"6\" height=\"6\"/><rect width=\"7\" height=\"7\" style=\"display:none\"/><rect width=\"8\" height=\"8\" visibility=\"hidden\"/>")).Shapes.Count, "hidden things are not drawn");
        // Paint servers are not drawn; the element is skipped as none.
        var gradient = SvgConverter.Convert(Doc("<defs><linearGradient id=\"g\"/></defs><rect width=\"5\" height=\"5\" fill=\"url(#g)\" stroke=\"red\"/>"));
        Expect.True(gradient.Ok && gradient.Art!.Shapes[0].Fill is null && gradient.Notes.Any(n => n.Contains("gradient")), "url(#id) fill is none, with a note");
        // Every emitted color is a scene color.
        foreach (var shape in Ok(Doc("<g opacity=\"0.3\"><rect width=\"5\" height=\"5\" fill=\"rgb(10,20,30)\" stroke=\"#abcd\"/></g>")).Shapes)
            Expect.True(SceneColor.IsValid(shape.Fill) && SceneColor.IsValid(shape.Stroke), "scene colors only");
    }

    // ---------------------------------------------------------------- view box

    private static void ViewBox()
    {
        var shifted = Ok(Doc("<rect x=\"100\" y=\"200\" width=\"10\" height=\"10\"/>", "100 200 50 40"));
        Expect.Equal(50.0, shifted.Width, "view box width"); Expect.Equal(40.0, shifted.Height, "view box height");
        Expect.True(shifted.Shapes[0].Values.SequenceEqual(new double[] { 0, 0, 10, 10 }), "the view box origin moves to zero");
        Expect.True(!shifted.NeedsClip, "inside the box needs no clip");
        var sized = Ok("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24px\" height=\"12\"><rect width=\"5\" height=\"5\"/></svg>");
        Expect.True(sized.Width == 24 && sized.Height == 12, "width and height stand in for a view box");
        var outside = Ok(Doc("<rect x=\"90\" y=\"90\" width=\"30\" height=\"30\"/>"));
        Expect.True(outside.NeedsClip && outside.CommandCount == outside.Shapes.Count + 2, "a shape beyond the box asks for a clip");
        foreach (var bad in new[] { "0 0 0 10", "0 0 -5 5", "0 0 10", "a b c d", "0 0 1e9 1e9", "1e9 0 10 10", "0 0 NaN 4" })
            Refused(Doc("<rect width=\"1\" height=\"1\"/>", bad));
        Refused("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");
        Refused("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"50%\" height=\"50%\"><rect width=\"1\" height=\"1\"/></svg>");
        Expect.Contains("not <svg>", Refused("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body/></html>"), "a non-svg root");
        Expect.Contains("not <svg>", Refused("<svg xmlns=\"http://example.com/other\" viewBox=\"0 0 1 1\"/>"), "the wrong namespace");
    }

    // ---------------------------------------------------------------- limits

    private static void Limits()
    {
        Expect.Contains("KiB", Refused(Doc("<!-- " + new string('x', 300_000) + " -->")), "bytes");
        Expect.Contains("KiB", Refused(Doc("<rect width=\"1\" height=\"1\"/>"), new SvgLimits { MaxBytes = 10 }), "bytes (custom)");
        // Elements.
        var many = Doc(string.Concat(Enumerable.Repeat("<rect width=\"1\" height=\"1\"/>", 400)));
        Expect.True(SvgConverter.Convert(many, new SvgLimits { MaxCommands = 400 }).Ok, "400 rects are within a budget of 400");
        Expect.Contains("elements", Refused(many, new SvgLimits { MaxElements = 399, MaxCommands = 400 }), "element count");
        Expect.True(SvgConverter.Convert(many, new SvgLimits { MaxElements = 401, MaxCommands = 400 }).Ok, "...at the edge it passes (root + 400)");
        // Commands (the per-symbol budget).
        Expect.Contains("drawing commands", Refused(many), "the default budget of 256");
        Expect.Contains("drawing commands", Refused(many, new SvgLimits { MaxCommands = 399 }), "command budget");
        Expect.Contains("drawing commands", Refused(many, SvgLimits.Icon), "the small budget");
        Expect.Equal(400, Ok(many, new SvgLimits { MaxCommands = 400 }).Shapes.Count, "exactly at the budget passes");
        // Depth.
        Expect.Contains("deeper", Refused(Doc(string.Concat(Enumerable.Repeat("<g>", 40)) + "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 40)))), "depth");
        Expect.True(SvgConverter.Convert(Doc(string.Concat(Enumerable.Repeat("<g>", 30)) + "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 30)))).Ok, "30 levels are fine");
        // Points: one polyline over the per-shape limit, many paths over the document limit.
        var bigPoly = Doc("<polyline fill=\"none\" stroke=\"red\" points=\"" + string.Join(" ", Enumerable.Range(0, 3000).Select(i => i % 100 + "," + i % 90)) + "\"/>");
        Expect.Contains("points", Refused(bigPoly), "points of one shape");
        var smallShapes = Doc(string.Concat(Enumerable.Range(0, 120).Select(i => "<polyline fill=\"none\" stroke=\"red\" points=\"" + string.Join(" ", Enumerable.Range(0, 100).Select(k => k % 50 + "," + (k + i) % 70)) + "\"/>")));
        Expect.True(SvgConverter.Convert(smallShapes, new SvgLimits { MaxTotalPoints = 12000 }).Ok, "12000 points fit when allowed");
        Expect.Contains("points", Refused(smallShapes, new SvgLimits { MaxTotalPoints = 8000 }), "points of the document");
        // A path that flattens to many points is stopped by the same budget (curves expand).
        var curves = Doc("<path stroke=\"red\" fill=\"none\" d=\"" + string.Concat(Enumerable.Repeat("M0 0C0 100 100 -100 100 0 ", 400)) + "\"/>");
        Expect.True(!SvgConverter.Convert(curves).Ok, "400 steep curves exceed the point or command budget");
        // Attributes.
        Refused(Doc("<rect " + string.Join(" ", Enumerable.Range(0, 80).Select(i => "a" + i + "=\"1\"")) + "/>"));
        // Huge numbers.
        Refused(Doc("<rect width=\"1\" height=\"1\"/>", "0 0 5000000 5"));
        var huge = SvgConverter.Convert(Doc("<rect x=\"1e300\" width=\"5\" height=\"5\"/><circle cx=\"9\" cy=\"9\" r=\"2\"/>"));
        Expect.True(huge.Ok && huge.Art!.Shapes.Count == 1, "a coordinate beyond the limit costs its shape only");
    }

    // ---------------------------------------------------------------- refusals

    private static void Refusals()
    {
        var rejected = new (string Name, string Svg)[]
        {
            ("script", Doc("<script>alert(1)</script>")),
            ("style sheet", Doc("<style>rect{fill:red}</style><rect width=\"1\" height=\"1\"/>")),
            ("foreignObject", Doc("<foreignObject><div xmlns=\"http://www.w3.org/1999/xhtml\"/></foreignObject>")),
            ("image", Doc("<image href=\"#a\" width=\"1\" height=\"1\"/>")),
            ("iframe", Doc("<iframe/>")),
            ("event handler", Doc("<rect width=\"1\" height=\"1\" onload=\"x()\"/>")),
            ("onclick on the root", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\" onclick=\"x()\"/>"),
            ("external href", Doc("<a href=\"http://example.com\"><rect width=\"1\" height=\"1\"/></a>")),
            ("external xlink:href", "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 1 1\"><a xlink:href=\"file:///c:/x\"/></svg>"),
            ("external url()", Doc("<rect width=\"1\" height=\"1\" fill=\"url(http://example.com/g)\"/>")),
            ("quoted external url()", Doc("<rect width=\"1\" height=\"1\" style=\"fill:url('//example.com/g')\"/>")),
            ("javascript url", Doc("<a href=\"javascript:alert(1)\"/>")),
            ("xml-stylesheet", "<?xml-stylesheet href=\"a.css\"?><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>"),
            ("doctype", "<!DOCTYPE svg><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>"),
            ("entity", "<!DOCTYPE svg [<!ENTITY a \"b\">]><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\">&a;</svg>"),
            ("undeclared entity", Doc("&foo;")),
            ("not xml", "this is not xml"),
            ("empty", ""),
            ("html", "<html><body>hi</body></html>"),
        };
        foreach (var (name, svg) in rejected)
        {
            var result = SvgConverter.Convert(svg);
            Expect.True(!result.Ok && result.Art is null && result.Reason.Length > 0, name + " is refused as a whole");
        }
        // Skipped with a reason, the rest is drawn.
        foreach (var (inner, word) in new[]
        {
            ("<use href=\"#a\"/>", "use"), ("<filter id=\"f\"><feGaussianBlur/></filter>", "filter"), ("<mask id=\"m\"/>", "mask"), ("<clipPath id=\"c\"><rect width=\"1\" height=\"1\"/></clipPath>", "clip"),
            ("<pattern id=\"p\"/>", "pattern"), ("<text>hi</text>", "text"), ("<marker id=\"k\"/>", "marker"), ("<symbol id=\"s\"/>", "symbol"), ("<animate attributeName=\"x\"/>", "animation"),
            ("<svg><rect width=\"9\" height=\"9\"/></svg>", "nested"), ("<g filter=\"url(#f)\"><rect width=\"9\" height=\"9\"/></g>", "filters"),
        })
        {
            var result = SvgConverter.Convert(Doc(inner + "<circle cx=\"5\" cy=\"5\" r=\"2\"/>"));
            Expect.True(result.Ok && result.Art!.Shapes.Count == 1 && result.Art.Shapes[0].Kind == ArtKind.Circle, word + " is skipped, the circle is drawn");
            Expect.True(result.Notes.Any(n => n.Contains(word, StringComparison.OrdinalIgnoreCase)), word + " leaves a reason: " + string.Join("|", result.Notes));
        }
        // defs, metadata, title, desc and foreign (editor) elements are skipped without a fuss; their children never draw.
        var quiet = SvgConverter.Convert(Doc("<title>t</title><desc>d</desc><metadata><x/></metadata><defs><rect width=\"9\" height=\"9\"/></defs><sodipodi:namedview xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\"><rect width=\"9\" height=\"9\"/></sodipodi:namedview><circle cx=\"5\" cy=\"5\" r=\"2\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" inkscape:label=\"x\"/>"));
        Expect.True(quiet.Ok && quiet.Art!.Shapes.Count == 1 && quiet.Notes.Count == 0, "metadata and defs are silent: " + string.Join("|", quiet.Notes));
        // The notes list is bounded.
        var noisy = SvgConverter.Convert(Doc(string.Concat(Enumerable.Range(0, 60).Select(i => "<rect width=\"1\" height=\"1\" transform=\"foo" + i + "(1)\"/>")) + "<circle cx=\"5\" cy=\"5\" r=\"2\"/>"));
        Expect.True(noisy.Ok && noisy.Notes.Count <= 12, "notes are bounded: " + noisy.Notes.Count);
    }

    // ---------------------------------------------------------------- hostile

    private static void Hostile()
    {
        var timer = Stopwatch.StartNew();

        // Billion laughs: refused before any entity expands.
        var laughs = new StringBuilder("<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">");
        for (var i = 1; i <= 9; i++) laughs.Append("<!ENTITY lol").Append(i).Append(" \"").Append(string.Concat(Enumerable.Repeat("&lol" + (i - 1 == 0 ? "" : (i - 1).ToString()) + ";", 10))).Append("\">");
        laughs.Append("]><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><text>&lol9;</text></svg>");
        Expect.Contains("DOCTYPE", Refused(laughs.ToString()), "billion laughs");
        // Quadratic blow-up and external entities, same refusal.
        Refused("<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\">&x;</svg>");
        Refused("<!doctype svg><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"/>");

        // Deep nesting: tens of thousands of levels are refused at level 33, in bounded time and memory.
        var deep = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\">" + string.Concat(Enumerable.Repeat("<g>", 20000)) + string.Concat(Enumerable.Repeat("</g>", 20000)) + "</svg>";
        Expect.Contains("deeper", Refused(deep), "deep nesting");
        // A wide document of empty groups.
        Refused(Doc(string.Concat(Enumerable.Repeat("<g/>", 5000))));

        // Unterminated things.
        foreach (var broken in new[]
        {
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\" fill=\"red",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"/>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><!-- never closed",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><![CDATA[ never closed",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path d=\"M0 0 L5 5",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><g></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox='0 0 10 10",
            "<<<<>>>>",
            "<svg",
            "\u0000<svg/>",
        })
            Expect.True(!SvgConverter.Convert(broken).Ok, "unterminated or broken input is refused: " + broken[..Math.Min(40, broken.Length)].Replace("\0", "NUL"));
        // Unterminated strings inside attributes that the parser accepts: the path and transform readers stop, never loop.
        Expect.True(SvgConverter.Convert(Doc("<path d=\"M0 0 L5 5 L\" stroke=\"red\"/>")).Ok, "path data that ends mid-command keeps what it read");
        Expect.True(SvgConverter.Convert(Doc("<path d=\"M0 0 L5 5\" stroke=\"red\" style=\"fill:url(#a\"/>")).Ok, "an unclosed url( in a style is not a reference to the outside");

        // Huge and odd numbers everywhere.
        foreach (var number in new[] { "1e308", "-1e308", "1e400", "99999999999999999999999999", "0x10", "1,5", "--1", "١٢٣", "NaN", "Infinity", "-Infinity", "1e-400" })
        {
            var svg = Doc("<rect x=\"" + number + "\" y=\"" + number + "\" width=\"" + number + "\" height=\"" + number + "\" fill=\"red\"/><circle cx=\"" + number + "\" cy=\"1\" r=\"" + number + "\" fill=\"red\"/><path d=\"M" + number + " 0 L" + number + " 1\" stroke=\"red\"/><polyline points=\"" + number + "," + number + " 1,1\" stroke=\"red\"/><line x1=\"" + number + "\" stroke=\"red\"/>");
            var r = SvgConverter.Convert(svg);
            if (r.Ok) foreach (var shape in r.Art!.Shapes) Expect.True(shape.Values.All(v => double.IsFinite(v) && Math.Abs(v) <= 1e7), "finite and bounded for " + number);
        }
        Expect.True(SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" stroke-width=\"NaN\" opacity=\"NaN\" fill-opacity=\"Infinity\" fill=\"red\"/>")).Ok, "NaN and Infinity in styles are ignored");
        var nanOpacity = Ok(Doc("<rect width=\"5\" height=\"5\" opacity=\"NaN\" fill=\"#f00\"/>")).Shapes[0];
        Expect.Equal("#ff0000", nanOpacity.Fill!, "NaN opacity is ignored, not applied");

        // Attribute and text bombs.
        Refused(Doc("<rect width=\"1\" height=\"1\" fill=\"" + new string('a', 300_000) + "\"/>"));
        Expect.True(SvgConverter.Convert(Doc("<rect width=\"1\" height=\"1\" fill=\"" + new string('a', 100_000) + "\"/><circle cx=\"2\" cy=\"2\" r=\"1\"/>")).Ok, "a long unknown paint is ignored cheaply");

        // A path of 100k one-character commands inside the byte limit.
        var spam = SvgConverter.Convert(Doc("<path stroke=\"red\" d=\"M0 0" + string.Concat(Enumerable.Repeat("l1 1", 40_000)) + "\"/>"));
        Expect.True(!spam.Ok, "a path with 40000 segments exceeds the point budget");
        // Many overlapping huge arcs: the work per arc is bounded by the segment cap.
        var arcs = SvgConverter.Convert(Doc("<path stroke=\"red\" fill=\"none\" d=\"M0 0" + string.Concat(Enumerable.Repeat("a1e5 1e5 0 1 1 5 5", 300)) + "\"/>"));
        Expect.True(!arcs.Ok || arcs.Art!.Shapes.All(s => s.Values.Length <= 2 * 2000), "huge arcs are bounded");

        Expect.True(timer.ElapsedMilliseconds < 5000, "all of the hostile inputs together convert or refuse quickly: " + timer.ElapsedMilliseconds + " ms");
    }

    private static void Tolerated()
    {
        const string body = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\" fill=\"red\"/></svg>";
        foreach (var prefix in new[] { "", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n", "<!-- made by an editor -->\n", "<?xml version=\"1.0\"?>\n<!-- one --><!-- two -->\n  \t\r\n", "\uFEFF<?xml version=\"1.0\"?>" })
            Expect.True(SvgConverter.Convert(prefix + body).Ok, "accepted with the prefix: " + prefix.Replace("\n", "\\n"));
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + body)).ToArray();
        Expect.True(SvgConverter.Convert(bytes).Ok, "a UTF-8 byte order mark");
        Expect.True(!SvgConverter.Convert(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E, 0xFF, 0xFE, 0x3C, 0x2F, 0x73, 0x76, 0x67, 0x3E }).Ok, "invalid UTF-8 is refused");
        Expect.True(SvgConverter.Convert("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\"><g id=\"a\"><rect width=\"5\" height=\"5\" fill=\"red\"/></g></svg>").Ok, "an unused xlink namespace is fine");
        Expect.True(SvgConverter.Convert("<svg viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\" fill=\"red\"/></svg>").Ok, "a missing xmlns is tolerated");
        Expect.True(SvgConverter.Convert(Doc("<rect width=\"5\" height=\"5\" fill=\"red\">&lt;&amp;&#65;</rect>")).Ok, "predefined and numeric references are fine");
    }

    private static void DoctypeMessage()
    {
        // The known limitation: a file starting with a DOCTYPE used to be reported as having no <svg> root.
        const string withDoctype = "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"/></svg>";
        var probe = AssetProbe.TryProbeSvg(Encoding.UTF8.GetBytes(withDoctype), out var reason);
        Expect.True(!probe, "the probe still refuses a DOCTYPE");
        Expect.True(reason.Contains("DOCTYPE") && !reason.Contains("no <svg> root"), "the probe says DOCTYPE: " + reason);
        var converted = SvgConverter.Convert(withDoctype);
        Expect.True(!converted.Ok && converted.Reason.Contains("DOCTYPE") && converted.Reason.Contains("remove"), "the converter says DOCTYPE and how to fix it: " + converted.Reason);
        // A real missing root keeps its own message.
        Expect.False(AssetProbe.TryProbeSvg(Encoding.UTF8.GetBytes("<html/>"), out var noRoot), "html is not svg");
        Expect.True(noRoot.StartsWith("no <svg> root element"), "the missing-root message stays for files that have none: " + noRoot);
        // Lowercase and with a leading declaration and comment.
        Expect.True(AssetProbe.TryProbeSvg(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!-- c --><!doctype svg><svg/>"), out reason) == false && reason.Contains("DOCTYPE"), "lower case doctype");
    }

    // ---------------------------------------------------------------- property tests

    private static double Dist(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        double qx = ax + t * dx, qy = ay + t * dy;
        return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
    }

    // The largest distance from the dense sample of the true curve to the flattened polyline.
    private static double MaxDeviation(Func<double, (double X, double Y)> curve, double[] polyline, double startX, double startY)
    {
        var worst = 0.0;
        for (var i = 0; i <= 2000; i++)
        {
            var (x, y) = curve(i / 2000.0);
            var best = double.MaxValue;
            double ax = startX, ay = startY;
            for (var k = 0; k < polyline.Length; k += 2)
            {
                best = Math.Min(best, Dist(x, y, ax, ay, polyline[k], polyline[k + 1]));
                ax = polyline[k]; ay = polyline[k + 1];
            }
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    private static void FlatteningProperty()
    {
        var random = new Random(20241017);
        double R(double range) => (random.NextDouble() * 2 - 1) * range;
        var unclampedChecked = 0;
        for (var round = 0; round < 400; round++)
        {
            var range = round % 4 == 0 ? 1e5 : round % 4 == 1 ? 1e3 : 100;
            var tolerance = range * (round % 3 == 0 ? 0.0015 : 0.01);
            double x0 = R(range), y0 = R(range), x1 = R(range), y1 = R(range), x2 = R(range), y2 = R(range), x3 = R(range), y3 = R(range);

            // Cubic
            var cubic = new List<double>();
            PathFlattener.Cubic(x0, y0, x1, y1, x2, y2, x3, y3, tolerance, cubic);
            Expect.True(cubic.Count % 2 == 0 && cubic.Count / 2 >= 1 && cubic.Count / 2 <= PathFlattener.MaxSegmentsPerCurve, "cubic segment count is bounded: " + cubic.Count / 2);
            Expect.True(cubic.All(double.IsFinite), "cubic points are finite");
            Expect.Near(x3, cubic[^2], 1e-9 * Math.Max(1, Math.Abs(x3)), "cubic ends on its target x"); Expect.Near(y3, cubic[^1], 1e-9 * Math.Max(1, Math.Abs(y3)), "cubic ends on its target y");
            double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
            double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
            for (var i = 0; i < cubic.Count; i += 2)
                Expect.True(cubic[i] >= minX - 1e-6 && cubic[i] <= maxX + 1e-6 && cubic[i + 1] >= minY - 1e-6 && cubic[i + 1] <= maxY + 1e-6, "cubic points stay in the control hull's box");
            var ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3)); var ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
            if (0.75 * Math.Sqrt(ddx * ddx + ddy * ddy) / (PathFlattener.MaxSegmentsPerCurve * PathFlattener.MaxSegmentsPerCurve) <= tolerance && round % 5 == 0)
            {
                var deviation = MaxDeviation(t => { var u = 1 - t; return (u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3, u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3); }, cubic.ToArray(), x0, y0);
                Expect.True(deviation <= tolerance * 1.02 + 1e-9, "cubic flattening error " + deviation + " is within " + tolerance);
                unclampedChecked++;
            }

            // Quad
            var quad = new List<double>();
            PathFlattener.Quad(x0, y0, x1, y1, x2, y2, tolerance, quad);
            Expect.True(quad.Count / 2 >= 1 && quad.Count / 2 <= PathFlattener.MaxSegmentsPerCurve && quad.All(double.IsFinite), "quad is bounded and finite");
            if (round % 5 == 1 && 0.25 * Math.Sqrt(Math.Pow(Math.Abs(x0 - 2 * x1 + x2), 2) + Math.Pow(Math.Abs(y0 - 2 * y1 + y2), 2)) / (PathFlattener.MaxSegmentsPerCurve * PathFlattener.MaxSegmentsPerCurve) <= tolerance)
            {
                var deviation = MaxDeviation(t => { var u = 1 - t; return (u * u * x0 + 2 * u * t * x1 + t * t * x2, u * u * y0 + 2 * u * t * y1 + t * t * y2); }, quad.ToArray(), x0, y0);
                Expect.True(deviation <= tolerance * 1.02 + 1e-9, "quad flattening error " + deviation + " is within " + tolerance);
            }

            // Arc
            var arc = new List<double>();
            var rx = Math.Abs(R(range)) + 0.01; var ry = Math.Abs(R(range)) + 0.01;
            PathFlattener.Arc(x0, y0, rx, ry, R(180), random.Next(2) == 1, random.Next(2) == 1, x3, y3, tolerance, arc);
            Expect.True(arc.Count / 2 >= 1 && arc.Count / 2 <= PathFlattener.MaxSegmentsPerArc && arc.All(double.IsFinite), "arc is bounded and finite: " + arc.Count / 2);
            Expect.Near(x3, arc[^2], 1e-9 * Math.Max(1, Math.Abs(x3)), "arc ends on its target");
        }
        Expect.True(unclampedChecked >= 30, "enough unclamped cubics were checked against the dense reference: " + unclampedChecked);

        // Degenerate input: coincident points, zero radii, equal endpoints.
        var line = new List<double>();
        PathFlattener.Cubic(5, 5, 5, 5, 5, 5, 5, 5, 0.1, line);
        Expect.True(line.Count / 2 == 1 && line[0] == 5, "a degenerate cubic is one point");
        var none = new List<double>();
        PathFlattener.Arc(1, 1, 5, 5, 0, true, true, 1, 1, 0.1, none);
        Expect.Equal(0, none.Count, "an arc to its own start draws nothing (SVG rule)");
        // The segment count follows the curvature: a gentle curve needs fewer segments than a tight one.
        Expect.True(PathFlattener.CubicSegments(1, 0.1) < PathFlattener.CubicSegments(100, 0.1), "tighter curves get more segments");
        Expect.Equal(PathFlattener.MaxSegmentsPerCurve, PathFlattener.CubicSegments(1e12, 1e-9), "and never more than the cap");
    }

    private static void RandomPathData()
    {
        var random = new Random(77);
        var tokens = new[] { "M", "m", "L", "l", "H", "h", "V", "v", "C", "c", "S", "s", "Q", "q", "T", "t", "A", "a", "Z", "z", " ", ",", "-", ".", "e", "1e2", "+", "0", "1", "NaN", "99999999999", "1e-5" };
        var shapes = 0;
        for (var round = 0; round < 6000; round++)
        {
            var text = new StringBuilder(round % 3 == 0 ? "M5 5 " : "");
            var length = random.Next(0, 70);
            for (var i = 0; i < length; i++) text.Append(random.Next(3) == 0 ? tokens[random.Next(tokens.Length)] : random.Next(-50, 50).ToString(CultureInfo.InvariantCulture) + (random.Next(2) == 0 ? " " : ","));
            var budget = new PointBudget(500, 100);
            PathResult result;
            try { result = PathFlattener.Flatten(text.ToString(), Affine.Identity, 0.1, budget, 1e6); }
            catch (ArtLimitException) { continue; } // the budget is allowed to stop a path
            foreach (var sub in result.Subpaths)
            {
                Expect.True(sub.Points.Length % 2 == 0 && sub.Points.Length / 2 <= 100, "a sub-path stays within the per-shape budget");
                Expect.True(sub.Points.All(v => double.IsFinite(v) && Math.Abs(v) <= 1e6), "points are finite and bounded");
                shapes++;
            }
            Expect.True(budget.Remaining >= 0 && budget.Remaining <= 500, "the budget never goes negative");
        }
        Expect.True(shapes > 100, "the random strings produced some paths: " + shapes);

        // Valid random paths: the output equals what the budget was charged.
        for (var round = 0; round < 300; round++)
        {
            var d = new StringBuilder("M0 0");
            for (var i = 0; i < random.Next(1, 20); i++)
            {
                switch (random.Next(5))
                {
                    case 0: d.Append(" L").Append(random.Next(-100, 100)).Append(' ').Append(random.Next(-100, 100)); break;
                    case 1: d.Append(" C").Append(string.Join(" ", Enumerable.Range(0, 6).Select(_ => random.Next(-100, 100)))); break;
                    case 2: d.Append(" Q").Append(string.Join(" ", Enumerable.Range(0, 4).Select(_ => random.Next(-100, 100)))); break;
                    case 3: d.Append(" A").Append(random.Next(1, 80)).Append(' ').Append(random.Next(1, 80)).Append(' ').Append(random.Next(0, 90)).Append(' ').Append(random.Next(2)).Append(' ').Append(random.Next(2)).Append(' ').Append(random.Next(-100, 100)).Append(' ').Append(random.Next(-100, 100)); break;
                    default: d.Append(" Z"); break;
                }
            }
            var budget = new PointBudget(100_000, 10_000);
            var result = PathFlattener.Flatten(d.ToString(), Affine.Identity, 0.2, budget, 1e6);
            Expect.True(result.Error is null, "valid random data reads fully: " + d + " -> " + result.Error);
            Expect.Equal(100_000 - result.Subpaths.Sum(s => s.Points.Length / 2), budget.Remaining, "the budget was charged for exactly the points produced");
        }
    }

    // ---------------------------------------------------------------- output

    private static void EmitIntoSlot()
    {
        var art = Ok(Doc("<rect width=\"100\" height=\"50\" fill=\"#ff0000\"/><path d=\"M0 0 L100 50\" stroke=\"#0000ff\" stroke-width=\"10\"/>", "0 0 100 50"));
        // Fitted into a 200x200 slot at (10,20): the picture is 100x50, scale 2, centered vertically.
        var commands = art.Commands(10, 20, 200, 200);
        Expect.Equal(2, commands.Count, "no clip when everything is inside");
        var rect = (RectCommand)commands[0];
        Expect.True(rect.X == 10 && rect.Y == 70 && rect.W == 200 && rect.H == 100, $"fit and centre: {rect}");
        var poly = (PolyCommand)commands[1];
        Expect.True(poly.Points[0] == 10 && poly.Points[1] == 70 && poly.Points[2] == 210 && poly.Points[3] == 170 && poly.StrokeWidth == 20, "points and stroke width scale together");
        // Opacity multiplies alpha.
        var half = art.Commands(0, 0, 100, 50, 0.5);
        Expect.Equal("#ff000080", ((RectCommand)half[0]).Fill!, "opacity 0.5 on an opaque fill");
        Expect.True(art.Commands(0, 0, 100, 50, 0).Count == 0, "opacity 0 draws nothing");
        Expect.True(art.Commands(0, 0, 0, 50).Count == 0 && art.Commands(0, 0, 50, -3).Count == 0 && art.Commands(double.NaN, 0, 5, 5).Count == 0 && art.Commands(0, 0, double.PositiveInfinity, 5).Count == 0, "an empty or non-finite slot draws nothing");
        // A shape outside the box brings a clip that is balanced; a hostile coordinate stays inside the protocol's range.
        var overflow = Ok(Doc("<rect x=\"-500000\" y=\"-500000\" width=\"900000\" height=\"900000\" fill=\"red\"/><circle cx=\"5\" cy=\"5\" r=\"2\" fill=\"blue\"/>"));
        var emitted = overflow.Commands(100, 100, 24, 24);
        Expect.True(emitted[0] is ClipRectCommand && emitted[^1] is UnclipCommand, "clip around overflowing art");
        var builder = new SceneBuilder(1920, 1080);
        foreach (var command in emitted) builder.Add(command);
        var scene = builder.Build();
        Expect.True(SceneValidator.Validate(scene).Count == 0, "valid scene");
        foreach (var c in scene.Commands.OfType<RectCommand>()) Expect.True(Math.Abs(c.X) <= 1e6 && Math.Abs(c.W) <= 1e6, "coordinates stay far inside the native layer's range");
        // The art serializes through the normal encoder.
        var json = SceneJson.Encode(scene);
        Expect.True(json.Contains("\"t\":\"clip\"") && json.Contains("\"t\":\"unclip\""), "encodes");
        // Appending to a builder.
        var b2 = new SceneBuilder(100, 100);
        art.AppendTo(b2, 0, 0, 100, 50);
        Expect.Equal(2, b2.Count, "AppendTo adds the commands");
        Expect.Equal(art.CommandCount, art.Commands(0, 0, 100, 50).Count, "CommandCount is what Commands produces");
    }

    private static void GlyphFits()
    {
        var picture = Glyph.Picture("pkg.symbol", 200, 100);
        var commands = picture.Commands(10, 10, 48, 48);
        var image = (ImageCommand)commands.Single();
        Expect.True(image.W == 48 && image.H == 24 && image.X == 10 && image.Y == 22, "a wide picture keeps its aspect and is centered: " + image);
        var tall = (ImageCommand)Glyph.Picture("p", 50, 100).Commands(0, 0, 40, 40).Single();
        Expect.True(tall.W == 20 && tall.H == 40 && tall.X == 10 && tall.Y == 0, "a tall picture");
        var tinted = (ImageCommand)Glyph.Picture("p", 10, 10).Commands(0, 0, 10, 10, 0.5, "#ff0000").Single();
        Expect.True(tinted.Opacity == 0.5 && tinted.Tint == "#ff0000", "opacity and tint pass to the image");
        Expect.True(((ImageCommand)Glyph.Picture("p", 10, 10).Commands(0, 0, 10, 10, 1, "red").Single()).Tint is null, "a bad tint is dropped");
        Expect.True(Glyph.None.IsEmpty && Glyph.None.Commands(0, 0, 5, 5).Count == 0 && Glyph.Picture("", 5, 5).IsEmpty && Glyph.Picture("a", 0, 5).IsEmpty, "an empty glyph draws nothing");
        var vector = Glyph.Vector(Ok(Doc("<circle cx=\"50\" cy=\"50\" r=\"40\" fill=\"red\"/>")));
        Expect.True(!vector.IsEmpty && vector.CommandCount == 1 && vector.Commands(0, 0, 10, 10).Single() is CircleCommand, "a vector glyph draws its shapes");
        Expect.Throws<ArgumentNullException>(() => Glyph.Vector(null!), "no art, no glyph");
    }
}
