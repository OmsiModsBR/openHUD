using System.Text;
using OpenHud.Art;
using OpenHud.Packages;
using OpenHud.Packages.PauseMenu;
using OpenHud.Packages.Realmap;
using OpenHud.Scene;
using BridgeRealmap = OmsiLaunch.Runtime.Modules.RealmapPackage;

namespace OpenHud.Maps;

// Something to hand to the native layer once the tick has time for it. Decoding and base64 are done
// by the worker that built it; the tick only sends.
internal abstract record Upload;
internal sealed record FontUpload(string Role, string Path) : Upload;
internal sealed record ImageUpload(string Id, int Width, int Height, string Payload) : Upload;
internal sealed record ImageDrop(string Id) : Upload;

// The pictures and vector art of the pause-menu package, ready to draw: the map's symbol, the stop icon, the two brand
// marks, the icons by marker kind and by transit mode. A slot the package left empty (or whose file was refused) is empty:
// the menu and the map then draw their own generic marks.
internal sealed class PauseGlyphs
{
    public static PauseGlyphs Empty { get; } = new();

    public Glyph Symbol { get; init; } = Glyph.None;
    public Glyph StopIcon { get; init; } = Glyph.None;
    public Glyph Omsi2Mark { get; init; } = Glyph.None;
    public Glyph OpenOmsiMark { get; init; } = Glyph.None;
    public IReadOnlyDictionary<string, Glyph> MarkerIcons { get; init; } = new Dictionary<string, Glyph>();
    public IReadOnlyDictionary<string, Glyph> ModeIcons { get; init; } = new Dictionary<string, Glyph>();
    // How many of the slots are vector art, for the report.
    public int VectorCount { get; init; }
}

// Both packages of the map the session plays, read once and then immutable. Absent packages are
// ordinary (an empty package stands in); a package that is unusable is Ignored with its diagnostics.
internal sealed class LoadedMap
{
    public const string SymbolImageId = "pkg.symbol";

    public string Identity { get; init; } = default!;
    public string MapFolder { get; init; } = default!;
    public RealmapPackage Realmap { get; init; } = default!;
    public PauseMenuReadResult PauseMenu { get; init; } = default!;
    public IReadOnlyList<Upload> Uploads { get; init; } = default!;
    // Image id of the map's symbol when it was decoded and queued, else null (the generic mark is drawn).
    public string? SymbolImage { get; init; }
    public PauseGlyphs Glyphs { get; init; } = PauseGlyphs.Empty;
    // The realmap manifest's colors, applied to the map layers.
    public MapPalette Palette { get; init; } = MapPalette.None;
    public IReadOnlyDictionary<string, string> MarkerImages { get; init; } = default!;
    public IReadOnlyList<string> Notes { get; init; } = default!;
    public MapDataService Data { get; init; } = default!;
    public MapFrame Frame { get; init; } = default!;
    public int FontCount => Uploads.Count(x => x is FontUpload);
    public int ImageCount => Uploads.Count(x => x is ImageUpload);

    public double TileMetres => Realmap.GridMetres is { } grid && grid >= 50 && grid <= 5000 ? grid : 300;
}

internal static class MapPackageLoader
{
    public const int MaxImages = 48;
    public const long MaxImagePixelBytes = 12L * 1024 * 1024;
    public const int SymbolMaxSide = 512;
    public const int MarkMaxSide = 256;
    public const int IconMaxSide = 128;
    // Pictures the pause-menu package may send to the draw layer (the marker icons of the realmap package have their own cap).
    public const int MaxPauseImages = 24;
    public const int MaxGlyphKinds = 16;

    // Runs on the worker thread. Never throws except for cancellation.
    public static LoadedMap Load(string identity, IRealmapSource source, string? moduleDirectory, int uiScalePercent, CancellationToken stopping)
    {
        var notes = new List<string>();
        var mapFolder = MapFolderOf(identity);

        RealmapSourceResult located;
        try { located = source.Locate(identity); }
        catch (Exception exception) { located = new RealmapSourceResult(false, null, "", "OL_E_MODULE_CONTENT_INPUT_INVALID", exception.GetType().Name); }
        stopping.ThrowIfCancellationRequested();

        RealmapPackage realmap;
        if (located.Found && located.Directory is not null) realmap = RealmapPackage.OpenDirectory(located.Directory, located.Fingerprint);
        else
        {
            realmap = RealmapPackage.None;
            if (located.ErrorCode is not null) notes.Add("realmap package: " + located.ErrorCode + (string.IsNullOrEmpty(located.Detail) ? "" : ": " + located.Detail));
        }
        stopping.ThrowIfCancellationRequested();

        var pauseDirectory = PauseDirectory(located, identity, moduleDirectory);
        var pause = PauseMenuPackageReader.Read(pauseDirectory, mapFolder);
        stopping.ThrowIfCancellationRequested();

        var uploads = new List<Upload>();
        string? symbolImage = null;
        var glyphs = PauseGlyphs.Empty;
        var markerImages = new Dictionary<string, string>(StringComparer.Ordinal);
        long pixelBytes = 0;

        if (pause.Status == PackageStatus.Loaded)
        {
            foreach (var face in pause.Identity.Fonts.Faces.GroupBy(x => x.Role).Select(g => g.First()))
                uploads.Add(new FontUpload(face.Role, face.FilePath));

            var folder = PackageFolder.Open(pauseDirectory);
            if (folder is not null)
            {
                var loader = new GlyphLoader(folder, uiScalePercent, uploads, notes, pixelBytes, stopping);
                glyphs = loader.LoadAll(pause.Identity);
                pixelBytes = loader.PixelBytes;
                symbolImage = glyphs.Symbol.ImageId;
            }
        }
        stopping.ThrowIfCancellationRequested();

        if (realmap.Status == PackageStatus.Loaded)
        {
            var folder = PackageFolder.Open(located.Directory);
            if (folder is not null)
            {
                var count = 0;
                foreach (var icon in realmap.Markers.Select(m => m.IconId).Distinct(StringComparer.Ordinal))
                {
                    if (count >= MaxImages) { notes.Add("marker icons: only the first " + MaxImages + " are used"); break; }
                    stopping.ThrowIfCancellationRequested();
                    if (Decode(folder, icon, IconMaxSide, notes) is not { } image || !Fits(image, ref pixelBytes)) continue;
                    var id = "mk." + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    uploads.Add(Put(id, image));
                    markerImages[icon] = id;
                    count++;
                }
            }
        }

        var frame = new MapFrame(realmap.Manifest?.NorthDegrees ?? 0);
        var captured = markerImages;
        var palette = MapPalette.From(realmap.Manifest);
        var data = new MapDataService(realmap, frame, icon => captured.TryGetValue(icon, out var id) ? id : null, palette);
        return new LoadedMap
        {
            Identity = identity, MapFolder = mapFolder, Realmap = realmap, PauseMenu = pause, Uploads = uploads,
            SymbolImage = symbolImage, Glyphs = glyphs, Palette = palette, MarkerImages = markerImages, Notes = notes, Data = data, Frame = frame,
        };
    }

    // "maps\Some Map\global.cfg" -> "Some Map"
    internal static string MapFolderOf(string identity)
    {
        var parts = identity.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : "";
    }

    // The pause-menu package sits next to the realmap package (same lookup: the map's own folder). When
    // the realmap package is not there to say where the map folder is, the installation root is read off
    // the module's own location (<root>\plugins\OmsiLaunch.Modules\<id>).
    internal static string? PauseDirectory(RealmapSourceResult located, string identity, string? moduleDirectory)
    {
        try
        {
            if (located.Found && located.Directory is not null)
            {
                var parent = Path.GetDirectoryName(located.Directory.TrimEnd('\\', '/'));
                return parent is null ? null : Path.Combine(parent, PauseMenuPackageReader.FolderName);
            }
            if (string.IsNullOrWhiteSpace(moduleDirectory)) return null;
            var modules = Path.GetDirectoryName(moduleDirectory.TrimEnd('\\', '/'));
            var plugins = modules is null ? null : Path.GetDirectoryName(modules);
            var root = plugins is null ? null : Path.GetDirectoryName(plugins);
            if (root is null || !string.Equals(Path.GetFileName(modules), "OmsiLaunch.Modules", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(plugins), "plugins", StringComparison.OrdinalIgnoreCase)) return null;
            var realmapDirectory = BridgeRealmap.Directory(root, identity);
            var mapFolder = realmapDirectory is null ? null : Path.GetDirectoryName(realmapDirectory);
            return mapFolder is null ? null : Path.Combine(mapFolder, PauseMenuPackageReader.FolderName);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException) { return null; }
    }

    internal static string UseDoubleSize(SymbolRef symbol, int uiScalePercent)
    {
        if (uiScalePercent > 150 && symbol.FilePath2x is not null)
        {
            var dot = symbol.Name.LastIndexOf('.');
            if (dot > 0) return symbol.Name[..dot] + "@2x" + symbol.Name[dot..];
        }
        return symbol.Name;
    }

    internal static DecodedImage? Decode(PackageFolder folder, string relative, int maxSide, List<string> notes)
    {
        var bytes = folder.ReadAll(relative, AssetProbe.MaxPictureBytes, out var reason);
        if (bytes is null) { notes.Add(relative + ": " + (reason ?? "unreadable")); return null; }
        var image = PngDecoder.Decode(bytes, out reason);
        if (image is null) { notes.Add(relative + ": " + reason); return null; }
        return PngDecoder.Downscale(image, maxSide);
    }

    internal static bool Fits(DecodedImage image, ref long used)
    {
        if (used + image.Rgba.Length > MaxImagePixelBytes) return false;
        used += image.Rgba.Length;
        return true;
    }

    internal static ImageUpload Put(string id, DecodedImage image) => new(id, image.Width, image.Height, SceneOps.ImagePut(id, image.Width, image.Height, image.Rgba));
}

// Loads every picture slot of a pause-menu package: PNG files are decoded and queued for the draw layer, SVG files are
// converted to vector art on the spot. A file used by several slots is read once. Runs on the worker thread.
internal sealed class GlyphLoader
{
    private readonly PackageFolder folder;
    private readonly int uiScalePercent;
    private readonly List<Upload> uploads;
    private readonly List<string> notes;
    private readonly CancellationToken stopping;
    private readonly Dictionary<string, Glyph> cache = new(StringComparer.OrdinalIgnoreCase);
    private int images;
    private int vectors;

    public GlyphLoader(PackageFolder folder, int uiScalePercent, List<Upload> uploads, List<string> notes, long pixelBytes, CancellationToken stopping)
    {
        this.folder = folder; this.uiScalePercent = uiScalePercent; this.uploads = uploads; this.notes = notes; this.stopping = stopping;
        PixelBytes = pixelBytes;
    }

    public long PixelBytes { get; private set; }

    public PauseGlyphs LoadAll(PauseMenuIdentity identity)
    {
        var symbol = Load(identity.SymbolFor(identity.Theme, identity.Layout), "symbol", MapPackageLoader.SymbolMaxSide, SvgLimits.Symbol, LoadedMap.SymbolImageId);
        var stop = Load(identity.StopIcon, "stop_icon", MapPackageLoader.IconMaxSide, SvgLimits.Icon);
        // The OMSI 2 mark falls back to the map's symbol (the reader's chain): the same file is not read and sent twice.
        var omsi2 = identity.Omsi2Mark.Kind == SymbolKind.Picture && identity.Omsi2Mark.Name == identity.SymbolFor(identity.Theme, identity.Layout).Name
            ? symbol
            : Load(identity.Omsi2Mark, "omsi2 mark", MapPackageLoader.MarkMaxSide, SvgLimits.Symbol);
        var open = Load(identity.OpenOmsiMark, "openomsi mark", MapPackageLoader.MarkMaxSide, SvgLimits.Symbol);
        var markers = LoadKinds(identity.MarkerIcons, "marker_icon.");
        var modes = LoadKinds(identity.ModeIcons, "mode_icon.");
        return new PauseGlyphs { Symbol = symbol, StopIcon = stop, Omsi2Mark = omsi2, OpenOmsiMark = open, MarkerIcons = markers, ModeIcons = modes, VectorCount = vectors };
    }

    private Dictionary<string, Glyph> LoadKinds(IReadOnlyDictionary<string, SymbolRef> source, string prefix)
    {
        var result = new Dictionary<string, Glyph>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, symbol) in source.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (result.Count >= MapPackageLoader.MaxGlyphKinds) { notes.Add(prefix + "*: only the first " + MapPackageLoader.MaxGlyphKinds + " are used"); break; }
            var glyph = Load(symbol, prefix + kind, MapPackageLoader.IconMaxSide, SvgLimits.Icon);
            if (!glyph.IsEmpty) result[kind] = glyph;
        }
        return result;
    }

    // None for an internal mark (@name), an unreadable file, or a slot over its budget.
    private Glyph Load(SymbolRef symbol, string slot, int maxSide, SvgLimits svgLimits, string? imageId = null)
    {
        stopping.ThrowIfCancellationRequested();
        if (symbol.Kind != SymbolKind.Picture) return Glyph.None;
        var name = symbol.Format == PictureFormat.Svg ? symbol.Name : MapPackageLoader.UseDoubleSize(symbol, uiScalePercent);
        // The same file in a smaller slot has a smaller side limit; the key keeps the largest need apart.
        var key = name + "|" + maxSide + "|" + svgLimits.MaxCommands;
        if (cache.TryGetValue(key, out var known)) return known;
        var glyph = symbol.Format == PictureFormat.Svg ? LoadVector(name, slot, svgLimits) : LoadPicture(name, slot, maxSide, imageId);
        cache[key] = glyph;
        return glyph;
    }

    private Glyph LoadVector(string name, string slot, SvgLimits limits)
    {
        var bytes = folder.ReadAll(name, AssetProbe.MaxSvgBytes, out var reason);
        if (bytes is null) { notes.Add(name + ": " + (reason ?? "unreadable")); return Glyph.None; }
        SvgResult result;
        try { result = SvgConverter.Convert(bytes, limits); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            notes.Add(name + ": the SVG could not be converted (" + exception.GetType().Name + "); the generic mark is drawn");
            return Glyph.None;
        }
        if (!result.Ok) { notes.Add(name + " (" + slot + "): " + result.Reason + "; the generic mark is drawn"); return Glyph.None; }
        foreach (var note in result.Notes.Take(3)) notes.Add(name + ": " + note);
        vectors++;
        return Glyph.Vector(result.Art!);
    }

    private Glyph LoadPicture(string name, string slot, int maxSide, string? imageId)
    {
        if (images >= MapPackageLoader.MaxPauseImages) { notes.Add(name + ": more than " + MapPackageLoader.MaxPauseImages + " pictures from the package; this one is not used"); return Glyph.None; }
        if (MapPackageLoader.Decode(folder, name, maxSide, notes) is not { } image) return Glyph.None;
        var used = PixelBytes;
        if (!MapPackageLoader.Fits(image, ref used)) { notes.Add(name + ": the package's pictures are over the memory limit; this one is not used"); return Glyph.None; }
        PixelBytes = used;
        var id = imageId ?? "pkg." + (images + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        images++;
        uploads.Add(MapPackageLoader.Put(id, image));
        return Glyph.Picture(id, image.Width, image.Height);
    }
}
