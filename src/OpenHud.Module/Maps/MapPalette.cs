using OpenHud.Packages;
using OpenHud.Packages.Realmap;

namespace OpenHud.Maps;

// The colors a realmap package's manifest asks for (the dark set; the radar is always dark), as the scene colors the
// map layers use. Every member is null when the manifest does not say, and the radar's own color then stays.
internal sealed class MapPalette
{
    // The alphas the radar's own contour and border colors carry; a manifest color without one gets the same.
    private const string ContourAlpha = "59";
    private const string BorderAlpha = "40";

    public static MapPalette None { get; } = new(null);

    private readonly RealmapManifest? manifest;

    private MapPalette(RealmapManifest? manifest) => this.manifest = manifest;

    public static MapPalette From(RealmapManifest? manifest) => manifest is null || manifest.DarkColors.IsEmpty ? None : new MapPalette(manifest);

    private string? Get(string key) => manifest is not null && manifest.DarkColors.TryGetValue(key, out var color) ? Hex(color) : null;

    private static string Hex(HudColor color) => color.ToHex();

    private static string WithDefaultAlpha(HudColor color, string alpha) => color.A == 255 ? "#" + color.R.ToString("x2") + color.G.ToString("x2") + color.B.ToString("x2") + alpha : color.ToHex();

    // Land is the map's background; water, green and built-up replace the radar's colors of the same layers.
    public string? Land => Get("land");
    public string? Water => Get("water");
    public string? Green => Get("green");
    public string? Built => Get("built");

    // The relief layer's contour lines.
    public string? Contour => manifest is not null && manifest.DarkColors.TryGetValue("contour", out var c) ? WithDefaultAlpha(c, ContourAlpha) : null;

    // Per-class fill colors for the kinds of green the radar folds into one layer; null = the layer's color.
    public string? Fill(FillClass fill) => fill switch
    {
        FillClass.Grass => Get("grass"),
        FillClass.Forest => Get("forest"),
        FillClass.Sport => Get("sport"),
        FillClass.Farm => Get("farm"),
        _ => null,
    };

    public string? River => Get("river");
    public string? Rail => Get("rail");
    public string? RailEdge => Get("rail_edge");
    public string? Border => manifest is not null && manifest.DarkColors.TryGetValue("border", out var c) ? WithDefaultAlpha(c, BorderAlpha) : null;
}
