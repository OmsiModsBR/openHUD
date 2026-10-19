namespace OpenHud.Radar;

// The bounding box of a feature's points. A feature whose box misses the view cannot show, so it is skipped without
// projecting any of its points; the scan is a few comparisons per point, a small part of what projecting and clipping costs.
public readonly struct FeatureBounds
{
    public readonly double MinX, MinY, MaxX, MaxY;
    // False when a point is not a usable coordinate: such a feature is never skipped by its box (the projection drops the bad point).
    public readonly bool Valid;

    private FeatureBounds(double minX, double minY, double maxX, double maxY, bool valid) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; Valid = valid; }

    /// <summary>The box of x,y pairs.</summary>
    public static FeatureBounds Of(double[] points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            double x = points[i], y = points[i + 1];
            if (!(Math.Abs(x) <= MapView.MaxCoordinate) || !(Math.Abs(y) <= MapView.MaxCoordinate)) return new FeatureBounds(0, 0, 0, 0, false);
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        return minX > maxX ? new FeatureBounds(0, 0, 0, 0, false) : new FeatureBounds(minX, minY, maxX, maxY, true);
    }

    public bool Intersects(double left, double bottom, double right, double top) =>
        !Valid || (MaxX >= left && MinX <= right && MaxY >= bottom && MinY <= top);
}
