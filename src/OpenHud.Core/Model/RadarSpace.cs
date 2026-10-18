namespace OpenHud.Model;

// Angles and the world-to-radar transform. Map space: X east, Y north, metres. Headings are degrees
// clockwise from +Y (north up). Radar space: metres relative to the radar centre, +X to the right,
// +Y up the screen; pixel space flips Y.
public static class Angles
{
    // [0, 360)
    public static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0;
        var r = degrees % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    // Signed shortest turn from `from` to `to`, in (-180, 180].
    public static double Delta(double from, double to)
    {
        var d = Normalize(to - from);
        return d > 180 ? d - 360 : d;
    }

    // Heading of a unit quaternion's local +Y axis seen from above (Z up), or null when the quaternion
    // is not a rotation (zero, NaN) or the axis points straight up or down.
    public static double? HeadingFromQuaternion(double x, double y, double z, double w)
    {
        var norm = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!double.IsFinite(norm) || norm < 1e-6) return null;
        x /= norm; y /= norm; z /= norm; w /= norm;
        var fx = 2 * (x * y - w * z);
        var fy = 1 - 2 * (x * x + z * z);
        if (Math.Abs(fx) < 1e-9 && Math.Abs(fy) < 1e-9) return null;
        return Normalize(Math.Atan2(fx, fy) * 180.0 / Math.PI);
    }
}

// Maps world points into the radar around a centre. `RotationDegrees` is the heading that should point up
// the screen (the player's heading for "heading up", 0 for "north up", the package's north for "real north up").
public readonly struct RadarProjection
{
    private readonly double centerX, centerY, sin, cos;

    public RadarProjection(double centerX, double centerY, double rotationDegrees)
    {
        this.centerX = centerX;
        this.centerY = centerY;
        RotationDegrees = Angles.Normalize(rotationDegrees);
        var radians = RotationDegrees * Math.PI / 180.0;
        sin = Math.Sin(radians);
        cos = Math.Cos(radians);
    }

    public double RotationDegrees { get; }

    // Metres, relative to the centre; the direction of RotationDegrees ends up on +Y.
    public (double X, double Y) WorldToRadar(double worldX, double worldY)
    {
        var dx = worldX - centerX;
        var dy = worldY - centerY;
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    public (double X, double Y) RadarToWorld(double radarX, double radarY) =>
        (centerX + radarX * cos + radarY * sin, centerY - radarX * sin + radarY * cos);

    // Pixel position for a radar-space point: `pixelsPerMetre` scales, the origin is (originX, originY) and Y grows downward.
    public static (double X, double Y) ToPixels(double radarX, double radarY, double pixelsPerMetre, double originX, double originY) =>
        (originX + radarX * pixelsPerMetre, originY - radarY * pixelsPerMetre);

    public (double X, double Y) WorldToPixels(double worldX, double worldY, double pixelsPerMetre, double originX, double originY)
    {
        var (rx, ry) = WorldToRadar(worldX, worldY);
        return ToPixels(rx, ry, pixelsPerMetre, originX, originY);
    }

    // Heading of an object (degrees from north) as it appears on the radar: 0 = up the screen.
    public double ObjectAngle(double headingDegrees) => Angles.Normalize(headingDegrees - RotationDegrees);

    // Distance from the centre.
    public double DistanceFromCenter(double worldX, double worldY) => Math.Sqrt((worldX - centerX) * (worldX - centerX) + (worldY - centerY) * (worldY - centerY));
}
