namespace OpenHud.Scene;

/// <summary>
/// Text width in canvas pixels. The real implementation is fed by the native layer's font metrics
/// (the module asks it per font role); everything here depends only on this interface so layout
/// stays testable without a device.
/// </summary>
public interface ITextMeasurer
{
    double Measure(string text, double size, string font);
}

/// <summary>
/// Deterministic stand-in: width = sum of per-character advances scaled by size. Used by tests and
/// as the fallback before the native metrics are known; it is intentionally a little generous so
/// layouts sized with it do not overflow when the real font is narrower.
/// </summary>
public sealed class ApproximateTextMeasurer : ITextMeasurer
{
    public double Measure(string text, double size, string font)
    {
        if (string.IsNullOrEmpty(text) || !(size > 0)) return 0;
        double factor = font == "bold" || font == "black" ? 0.58 : font == "condensed" ? 0.44 : 0.52;
        double units = 0;
        foreach (char ch in text)
            units += ch == ' ' || ch == 'i' || ch == 'l' || ch == '.' || ch == ',' || ch == ':' || ch == '\'' || ch == '|' ? 0.5
                : ch == 'm' || ch == 'w' || ch == 'M' || ch == 'W' ? 1.4
                : char.IsUpper(ch) || char.IsDigit(ch) ? 1.1 : 1.0;
        return units * factor * size;
    }
}

public sealed record ButtonStyle(string Fill, string TextColor, string? Stroke = null,
    double Radius = 10, double FontSize = 18, string Font = "bold");

public sealed record ChipStyle(string Fill, string TextColor, string? Stroke = null,
    double FontSize = 13, string Font = "medium", double PaddingX = 10, double Height = 24);

/// <summary>Composite shapes built from scene primitives. Pure functions of their arguments.</summary>
public static class SceneDraw
{
    public static SceneBuilder Panel(this SceneBuilder b, double x, double y, double w, double h, double radius,
        string fill, string? stroke = null, double strokeWidth = 1)
        => b.Rect(x, y, w, h, fill, stroke, stroke is null ? null : strokeWidth, radius);

    /// <summary>A rounded button with its label centered; the label is cut with an ellipsis if wider than the button.</summary>
    public static SceneBuilder Button(this SceneBuilder b, double x, double y, double w, double h, string label, ButtonStyle style)
    {
        b.Rect(x, y, w, h, style.Fill, style.Stroke, style.Stroke is null ? null : 1, style.Radius);
        return b.Text(x + w / 2, y + h / 2, label, style.FontSize, style.Font, style.TextColor, "c", "mid", Math.Max(1, w - 16));
    }

    /// <summary>A pill sized to its label; returns the width so callers can lay chips out in a row.</summary>
    public static double Chip(this SceneBuilder b, double x, double y, string label, ChipStyle style, ITextMeasurer measurer)
    {
        double w = measurer.Measure(label, style.FontSize, style.Font) + style.PaddingX * 2;
        b.Rect(x, y, w, style.Height, style.Fill, style.Stroke, style.Stroke is null ? null : 1, style.Height / 2);
        b.Text(x + w / 2, y + style.Height / 2, label, style.FontSize, style.Font, style.TextColor, "c", "mid");
        return w;
    }

    /// <summary>A small round badge with a short text (a counter, a line number); the circle is centered on (cx, cy).</summary>
    public static SceneBuilder Badge(this SceneBuilder b, double cx, double cy, double radius, string text, string fill, string textColor,
        double fontSize = 12)
    {
        b.Circle(cx, cy, radius, fill);
        return b.Text(cx, cy, text, fontSize, "bold", textColor, "c", "mid", radius * 2);
    }

    /// <summary>
    /// A square slot showing a registered image; when the module has no image for it (null id) a
    /// neutral rounded placeholder is drawn so layouts do not shift.
    /// </summary>
    public static SceneBuilder IconSlot(this SceneBuilder b, double x, double y, double size, string? imageId, string? tint,
        string placeholderFill)
    {
        if (string.IsNullOrEmpty(imageId)) return b.Rect(x, y, size, size, placeholderFill, null, null, size * 0.2);
        return b.Image(imageId, x, y, size, size, null, tint);
    }

    /// <summary>
    /// A circular gauge: a full track arc and a value arc over it. Angles in degrees (0 = up,
    /// clockwise); <paramref name="value"/> is clamped to 0..1 and NaN counts as 0.
    /// </summary>
    public static SceneBuilder ArcGauge(this SceneBuilder b, double cx, double cy, double radius, double value,
        double startAngle, double sweep, string trackColor, string valueColor, double strokeWidth)
    {
        if (double.IsNaN(value)) value = 0;
        value = Math.Clamp(value, 0, 1);
        b.Arc(cx, cy, radius, startAngle, startAngle + sweep, trackColor, strokeWidth, "round");
        if (value > 0) b.Arc(cx, cy, radius, startAngle, startAngle + sweep * value, valueColor, strokeWidth, "round");
        return b;
    }
}
