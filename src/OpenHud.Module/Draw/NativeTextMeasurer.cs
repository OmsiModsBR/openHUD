using OpenHud.Bridge;
using OpenHud.Scene;

namespace OpenHud.Draw;

// Text widths from the draw layer, so layouts follow the fonts that are actually registered. Results
// are cached (the same labels come back every tick). When the layer cannot answer, the deterministic
// approximation stands in; after a few failures in a row the layer is not asked again.
internal sealed class NativeTextMeasurer : ITextMeasurer
{
    private const int MaxEntries = 4096;
    private const int GiveUpAfter = 4;

    private readonly NativeLink native;
    private readonly ITextMeasurer fallback = new ApproximateTextMeasurer();
    private readonly Dictionary<(string Text, int Size, string Font), double> cache = new();
    private int failures;
    private int generation;

    public NativeTextMeasurer(NativeLink native) => this.native = native;

    // False once the layer stopped answering: only the approximation is used from then on.
    public bool UsingNative => failures < GiveUpAfter;
    public long NativeCalls { get; private set; }
    public long CacheHits { get; private set; }

    // A font was registered: widths measured before no longer hold.
    public void FontsChanged()
    {
        cache.Clear();
        generation++;
        failures = 0;
    }

    public int Generation => generation;

    public double Measure(string text, double size, string font)
    {
        if (string.IsNullOrEmpty(text) || !(size > 0) || !double.IsFinite(size)) return 0;
        if (failures >= GiveUpAfter || text.Length > SceneOps.MaxMeasureLength) return fallback.Measure(text, size, font);
        var key = (text, (int)Math.Round(size * 100), font);
        if (cache.TryGetValue(key, out var known)) { CacheHits++; return known; }
        double? width = null;
        try
        {
            NativeCalls++;
            width = native.Measure(text, Math.Clamp(size, 1, 512), font);
        }
        catch (Exception) { /* an argument the protocol refuses: the approximation answers */ }
        if (width is null)
        {
            failures++;
            return fallback.Measure(text, size, font);
        }
        failures = 0;
        if (cache.Count >= MaxEntries) cache.Clear();
        cache[key] = width.Value;
        return width.Value;
    }
}
