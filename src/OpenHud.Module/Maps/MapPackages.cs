using System.Text;
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
    public const int IconMaxSide = 128;

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
        var markerImages = new Dictionary<string, string>(StringComparer.Ordinal);
        long pixelBytes = 0;

        if (pause.Status == PackageStatus.Loaded)
        {
            foreach (var face in pause.Identity.Fonts.Faces.GroupBy(x => x.Role).Select(g => g.First()))
                uploads.Add(new FontUpload(face.Role, face.FilePath));

            var folder = PackageFolder.Open(pauseDirectory);
            var symbol = pause.Identity.SymbolFor(pause.Identity.Theme, pause.Identity.Layout);
            if (folder is not null && symbol.Kind == SymbolKind.Picture)
            {
                if (symbol.Format == PictureFormat.Svg)
                    notes.Add(symbol.Name + ": SVG pictures are not rasterized by this version; the generic mark is drawn");
                else if (Decode(folder, UseDoubleSize(symbol, uiScalePercent), SymbolMaxSide, notes) is { } image && Fits(image, ref pixelBytes))
                {
                    uploads.Add(Put(LoadedMap.SymbolImageId, image));
                    symbolImage = LoadedMap.SymbolImageId;
                }
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
        var data = new MapDataService(realmap, frame, icon => captured.TryGetValue(icon, out var id) ? id : null);
        return new LoadedMap
        {
            Identity = identity, MapFolder = mapFolder, Realmap = realmap, PauseMenu = pause, Uploads = uploads,
            SymbolImage = symbolImage, MarkerImages = markerImages, Notes = notes, Data = data, Frame = frame,
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

    private static string UseDoubleSize(SymbolRef symbol, int uiScalePercent)
    {
        if (uiScalePercent > 150 && symbol.FilePath2x is not null)
        {
            var dot = symbol.Name.LastIndexOf('.');
            if (dot > 0) return symbol.Name[..dot] + "@2x" + symbol.Name[dot..];
        }
        return symbol.Name;
    }

    private static DecodedImage? Decode(PackageFolder folder, string relative, int maxSide, List<string> notes)
    {
        var bytes = folder.ReadAll(relative, AssetProbe.MaxPictureBytes, out var reason);
        if (bytes is null) { notes.Add(relative + ": " + (reason ?? "unreadable")); return null; }
        var image = PngDecoder.Decode(bytes, out reason);
        if (image is null) { notes.Add(relative + ": " + reason); return null; }
        return PngDecoder.Downscale(image, maxSide);
    }

    private static bool Fits(DecodedImage image, ref long used)
    {
        if (used + image.Rgba.Length > MaxImagePixelBytes) return false;
        used += image.Rgba.Length;
        return true;
    }

    private static ImageUpload Put(string id, DecodedImage image) => new(id, image.Width, image.Height, SceneOps.ImagePut(id, image.Width, image.Height, image.Rgba));
}
