using System.Globalization;

namespace OpenHud.Model;

// Defensive access to a string dictionary of outputs: a value that is missing, empty, not a number,
// NaN or infinite is simply "not there". Numbers are read culture-independently.
internal readonly struct Fields
{
    private readonly IReadOnlyDictionary<string, string> values;
    public Fields(IReadOnlyDictionary<string, string> values) => this.values = values;

    public string? Text(string key, int maxLength = 256)
    {
        if (!values.TryGetValue(key, out var v) || v is null) return null;
        v = v.Trim();
        if (v.Length == 0) return null;
        v = new string(v.Where(c => !char.IsControl(c)).ToArray());
        return v.Length > maxLength ? v[..maxLength] : v;
    }

    public double? Double(string key)
    {
        if (!values.TryGetValue(key, out var v) || v is null) return null;
        return double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
    }

    public double? Double(string key, double min, double max) => Double(key) is { } d && d >= min && d <= max ? d : null;

    public int? Int(string key, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!values.TryGetValue(key, out var v) || v is null) return null;
        if (int.TryParse(v.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i)) return i >= min && i <= max ? i : null;
        // Some readers print whole numbers as floats ("3.0").
        return Double(key) is { } d && d == Math.Floor(d) && d >= min && d <= max ? (int)d : null;
    }

    public bool? Bool(string key)
    {
        if (!values.TryGetValue(key, out var v) || v is null) return null;
        return v.Trim().ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };
    }
}
