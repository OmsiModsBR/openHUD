using System.Text.RegularExpressions;

namespace OpenHud.Model;

public sealed record FrameSourceOptions
{
    // Edge of a tile in map-space metres. A realmap package states the author's grid; set it from
    // RealmapPackage.GridMetres when there is one. 300 m is OMSI's usual tile.
    public double TileMetres { get; init; } = 300;
    // Vehicles farther than this from the player are not reported.
    public double NearbyRadiusMetres { get; init; } = 800;
    public int MaxNearbyVehicles { get; init; } = 128;
    // Calls of road-vehicle.read per frame; the rest keep their last reading (nearest first, unknown first).
    public int VehicleReadBudget { get; init; } = 24;
    // The handle list is re-read every this many frames.
    public int ListRefreshFrames { get; init; } = 5;
    // The tile table (map.tiles.list) is re-read this often, and at once when the player's tile is unknown.
    public int TileRefreshFrames { get; init; } = 60;
    // map, weather and timetable change slowly: re-read every this many frames, reuse in between.
    public int SlowRefreshFrames { get; init; } = 20;
    // A reading of a vehicle older than this many frames is dropped.
    public int MaxVehicleAgeFrames { get; init; } = 40;
    // timetable.logs.read must also be declared by the module for this to work.
    public bool ReadTimetableLogs { get; init; }
    public int MaxListPages { get; init; } = 4;
}

// Reads the game through an IGameReader and builds FrameSnapshots. Nothing here throws: a reader that throws,
// times out or returns garbage makes that part of the snapshot unavailable and leaves the others alone.
public sealed class FrameSource
{
    private static readonly Regex HandlePattern = new(@"^rv-\d{1,9}$", RegexOptions.CultureInvariant);
    private const int MaxTileRows = 4096;
    // While the player's tile is unknown the table is re-read this often (not every frame: the call may be unsupported).
    private const int UnresolvedRetryFrames = 10;

    private readonly IGameReader reader;
    private readonly FrameSourceOptions options;
    private long sequence;

    private Dictionary<int, (int X, int Y)> tiles = new();
    private long tilesReadAt = long.MinValue;
    private List<string> handles = new();
    private long handlesReadAt = long.MinValue;
    private readonly Dictionary<string, (VehicleState State, long Frame)> vehicles = new(StringComparer.Ordinal);
    private long slowReadAt = long.MinValue;
    private Reading<MapInfo> map = Reading<MapInfo>.Unavailable("not read yet");
    private Reading<TimetableInfo> timetable = Reading<TimetableInfo>.Unavailable("not read yet");
    private Reading<WeatherInfo> weather = Reading<WeatherInfo>.Unavailable("not read yet");
    private string? lastMapName;

    public FrameSource(IGameReader reader, FrameSourceOptions? options = null)
    {
        this.reader = reader;
        this.options = options ?? new FrameSourceOptions();
    }

    public FrameSnapshot Read()
    {
        var frame = ++sequence;
        var time = Part(() => ParseTime(Call(CatalogOperations.Time)));
        var camera = Part(() => ParseCamera(Call(CatalogOperations.Camera)));
        if (slowReadAt == long.MinValue || frame - slowReadAt >= options.SlowRefreshFrames)
        {
            slowReadAt = frame;
            map = Part(() => ParseMap(Call(CatalogOperations.Map)));
            weather = Part(() => ParseWeather(Call(CatalogOperations.Weather)));
            timetable = Part(() => ParseTimetable(Call(CatalogOperations.Timetable)));
            // A new map invalidates what was learned about the old one.
            var name = map.Value?.FileName ?? map.Value?.Name;
            if (name != lastMapName) { lastMapName = name; tiles = new(); tilesReadAt = long.MinValue; vehicles.Clear(); handles = new(); handlesReadAt = long.MinValue; }
        }

        var player = Part(() => ParsePlayer(Call(CatalogOperations.PlayerVehicle), frame));
        var nearby = player.Value is { Present: true, Vehicle: { } p }
            ? Part(() => ReadVehicles(p, frame))
            : player.IsAvailable ? Reading<NearbyVehicles>.Of(new NearbyVehicles(Array.Empty<VehicleState>(), 0, false)) : Reading<NearbyVehicles>.Unavailable("the player is unknown");
        return new FrameSnapshot(frame, time, map, camera, player, nearby, timetable, weather, options.TileMetres);
    }

    // ------------------------------------------------------------ calls

    private sealed class ReadFailure : Exception { public ReadFailure(string message) : base(message) { } }

    // The reader is a foreign port (a bridge call, a recording): any exception it throws is converted
    // to an unavailable part. This is the one place that catches broadly, on purpose.
    private IReadOnlyDictionary<string, string> Call(string operation, IReadOnlyDictionary<string, string>? arguments = null)
    {
        GameReply reply;
        try { reply = reader.Read(operation, arguments); }
        catch (Exception exception) { throw new ReadFailure(operation + " failed: " + exception.GetType().Name); }
        if (reply is null) throw new ReadFailure(operation + " returned nothing");
        if (!reply.Succeeded) throw new ReadFailure(operation + " answered " + (reply.ErrorCode ?? "an error"));
        if (reply.Values is null) throw new ReadFailure(operation + " returned no values");
        return reply.Values;
    }

    private static Reading<T> Part<T>(Func<T> read) where T : class
    {
        try { return Reading<T>.Of(read()); }
        catch (ReadFailure failure) { return Reading<T>.Unavailable(failure.Message); }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            // A parser bug on a strange reply must not cost the frame.
            return Reading<T>.Unavailable("unreadable reply: " + exception.GetType().Name);
        }
    }

    // Pages through a list operation. Stops at the page cap, on a repeated cursor or when no next_cursor remains.
    private List<IReadOnlyDictionary<string, string>> Pages(string operation, int limit)
    {
        var pages = new List<IReadOnlyDictionary<string, string>>();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.MaxListPages; i++)
        {
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (cursor is not null) arguments["cursor"] = cursor;
            var page = Call(operation, arguments);
            pages.Add(page);
            var fields = new Fields(page);
            var next = fields.Text("next_cursor", 64);
            if (next is null || fields.Bool("truncated") == false || !seen.Add(next)) break;
            cursor = next;
        }
        return pages;
    }

    // ------------------------------------------------------------ parsers

    private static GameTime ParseTime(IReadOnlyDictionary<string, string> v)
    {
        var f = new Fields(v);
        var hour = f.Int("hour", 0, 23);
        var minute = f.Int("minute", 0, 59);
        if (hour is null || minute is null) throw new ReadFailure("time.read: hour/minute missing or out of range");
        var second = f.Double("second", 0, 60) ?? 0;
        return new GameTime(hour.Value, minute.Value, second, f.Int("day", 1, 31), f.Int("month", 1, 12), f.Int("year", 1, 9999));
    }

    private static MapInfo ParseMap(IReadOnlyDictionary<string, string> v)
    {
        var f = new Fields(v);
        var loaded = f.Bool("loaded") ?? throw new ReadFailure("map.read: 'loaded' missing");
        return new MapInfo(loaded, f.Text("name"), f.Text("filename", 260), f.Text("friendly_name"), f.Bool("left_hand_traffic"),
            f.Int("loaded_tiles", 0, 1_000_000), f.Int("year_start"), f.Int("year_end"), f.Double("max_speed"));
    }

    private static CameraInfo ParseCamera(IReadOnlyDictionary<string, string> v)
    {
        var f = new Fields(v);
        var family = f.Int("family") switch { 0 => CameraFamily.Driver, 1 => CameraFamily.Passenger, 2 => CameraFamily.External, 3 => CameraFamily.Map, _ => throw new ReadFailure("camera.read: 'family' missing or unknown") };
        return new CameraInfo(family, f.Text("name"), f.Double("field_of_view", 1, 179), f.Double("normal_field_of_view", 1, 179), f.Double("distance", 0, 1e6));
    }

    private static WeatherInfo ParseWeather(IReadOnlyDictionary<string, string> v)
    {
        var f = new Fields(v);
        var info = new WeatherInfo(f.Double("temperature", -90, 90), f.Double("wind_speed", 0, 500), f.Double("wind_direction", 0, 360), f.Double("fog_density", 0, 1e7),
            f.Double("precipitation", 0, 255), f.Text("cloud_type_name", 64), f.Bool("snow"), f.Bool("wet_ground"));
        if (info.Temperature is null && info.WindSpeed is null && info.WindDirection is null && info.FogDensity is null && info.Precipitation is null && info.CloudTypeName is null && info.Snow is null && info.WetGround is null)
            throw new ReadFailure("weather.read: no readable field");
        return info;
    }

    private TimetableInfo ParseTimetable(IReadOnlyDictionary<string, string> v)
    {
        var f = new Fields(v);
        var logs = new List<TimetableLogRow>();
        if (options.ReadTimetableLogs)
        {
            try
            {
                foreach (var page in Pages(CatalogOperations.TimetableLogs, 256))
                {
                    var p = new Fields(page);
                    foreach (var index in RowIndexes(page, "log."))
                    {
                        var prefix = "log." + index + ".";
                        logs.Add(new TimetableLogRow(p.Text(prefix + "bus_stop", 128), p.Int(prefix + "estimated_arrival"), p.Int(prefix + "estimated_departure"),
                            p.Int(prefix + "actual_arrival"), p.Int(prefix + "actual_departure"), p.Bool(prefix + "arrival_ok"), p.Bool(prefix + "departure_ok")));
                        if (logs.Count >= 4096) break;
                    }
                }
            }
            catch (ReadFailure) { /* the counts are still good; the rows are just absent */ }
        }
        var info = new TimetableInfo(f.Bool("invalid"), f.Int("tracks", 0), f.Int("trips", 0), f.Int("bus_stops", 0), f.Int("station_links", 0), f.Int("lines", 0), f.Int("rv_files", 0), logs);
        if (info.Invalid is null && info.Tracks is null && info.Trips is null && info.BusStops is null && info.StationLinks is null && info.Lines is null && info.RvFiles is null)
            throw new ReadFailure("timetable.read: no readable field");
        return info;
    }

    // Row numbers n of keys "<prefix><n>.<field>", ascending, without duplicates.
    private static IEnumerable<int> RowIndexes(IReadOnlyDictionary<string, string> page, string prefix)
    {
        var found = new SortedSet<int>();
        foreach (var key in page.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = key.AsSpan(prefix.Length);
            var dot = rest.IndexOf('.');
            if (dot > 0 && int.TryParse(rest[..dot], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) found.Add(n);
            if (found.Count >= 5000) break;
        }
        return found;
    }

    private PlayerState ParsePlayer(IReadOnlyDictionary<string, string> v, long frame)
    {
        var f = new Fields(v);
        var present = f.Bool("present") ?? throw new ReadFailure("player-vehicle.read: 'present' missing");
        if (!present) return new PlayerState(false, null);
        RefreshTiles(frame);
        var vehicle = ParseVehicle(v, null) ?? throw new ReadFailure("player-vehicle.read: position/orientation unreadable");
        // The player's tile may be new: learn it once, rather than waiting for the next scheduled refresh.
        if (!vehicle.WorldResolved && frame - tilesReadAt >= UnresolvedRetryFrames) { RefreshTiles(frame, force: true); vehicle = ParseVehicle(v, null) ?? vehicle; }
        return new PlayerState(true, vehicle);
    }

    // A road-vehicle.read (or player-vehicle.read) reply as a pose, or null when position or orientation is unusable.
    private VehicleState? ParseVehicle(IReadOnlyDictionary<string, string> v, string? handleOverride)
    {
        var f = new Fields(v);
        var x = f.Double("position_x", -1e7, 1e7);
        var y = f.Double("position_y", -1e7, 1e7);
        if (x is null || y is null) return null;
        var z = f.Double("position_z", -1e6, 1e6) ?? 0;
        var tile = f.Int("tile", int.MinValue, int.MaxValue) ?? -1;
        double wx = x.Value, wy = y.Value;
        var resolved = false;
        if (tiles.TryGetValue(tile, out var cell))
        {
            wx = cell.X * options.TileMetres + x.Value;
            wy = cell.Y * options.TileMetres + y.Value;
            resolved = true;
        }
        double? heading = null;
        var rx = f.Double("rotation_x"); var ry = f.Double("rotation_y"); var rz = f.Double("rotation_z"); var rw = f.Double("rotation_w");
        if (rx is not null && ry is not null && rz is not null && rw is not null) heading = Angles.HeadingFromQuaternion(rx.Value, ry.Value, rz.Value, rw.Value);
        var handle = handleOverride ?? f.Text("handle", 32);
        if (handle is not null && !HandlePattern.IsMatch(handle)) handle = null;
        // ground_speed is read as metres per second (the unit the game keeps it in); negative when reversing.
        var speed = f.Double("ground_speed", -1000, 1000) ?? 0;
        return new VehicleState(handle, tile, x.Value, y.Value, z, wx, wy, resolved, heading, speed, f.Int("traffic_type", 0, 255), f.Bool("ai_enabled"));
    }

    // ------------------------------------------------------------ tiles

    private void RefreshTiles(long frame, bool force = false)
    {
        if (!force && tilesReadAt != long.MinValue && frame - tilesReadAt < options.TileRefreshFrames) return;
        tilesReadAt = frame;
        try
        {
            var table = new Dictionary<int, (int, int)>();
            foreach (var page in Pages(CatalogOperations.MapTiles, 256))
            {
                var f = new Fields(page);
                foreach (var row in RowIndexes(page, "tile."))
                {
                    var prefix = "tile." + row + ".";
                    var gx = f.Int(prefix + "x"); var gy = f.Int(prefix + "y");
                    if (gx is null || gy is null) continue;
                    // A tile that cannot be read (state=unavailable) has no trustworthy position.
                    if (f.Text(prefix + "state") == "unavailable") continue;
                    var index = f.Int(prefix + "tile_index", 0) ?? row;
                    table[index] = (gx.Value, gy.Value);
                    if (table.Count >= MaxTileRows) break;
                }
            }
            if (table.Count > 0) tiles = table;
        }
        catch (ReadFailure) { /* positions stay as given; the player's pose says WorldResolved = false */ }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidOperationException) { }
    }

    // ------------------------------------------------------------ nearby vehicles

    private NearbyVehicles ReadVehicles(VehicleState player, long frame)
    {
        if (handlesReadAt == long.MinValue || frame - handlesReadAt >= options.ListRefreshFrames)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in Pages(CatalogOperations.RoadVehicles, 1000))
            {
                foreach (var row in RowIndexes(page, "vehicle."))
                {
                    var handle = new Fields(page).Text("vehicle." + row + ".handle", 32);
                    if (handle is not null && HandlePattern.IsMatch(handle) && seen.Add(handle)) list.Add(handle);
                }
                if (list.Count >= 4000) break;
            }
            handles = list;
            handlesReadAt = frame;
            foreach (var gone in vehicles.Keys.Where(k => !seen.Contains(k)).ToList()) vehicles.Remove(gone);
        }

        // Which handles to refresh now: never read ones first, then the oldest of those near the player.
        var budget = Math.Max(0, options.VehicleReadBudget);
        var order = handles
            .Select(h => (Handle: h, Known: vehicles.TryGetValue(h, out var k) ? k : default((VehicleState State, long Frame)?)))
            .OrderBy(x => x.Known is null ? 0 : 1)
            .ThenBy(x => x.Known is { } k ? Distance(k.State, player) : 0)
            .ThenBy(x => x.Known?.Frame ?? 0)
            .Take(budget)
            .ToList();
        var failures = 0;
        foreach (var (handle, _) in order)
        {
            try
            {
                var state = ParseVehicle(Call(CatalogOperations.RoadVehicle, new Dictionary<string, string>(StringComparer.Ordinal) { ["handle"] = handle }), handle);
                if (state is null) vehicles.Remove(handle); else vehicles[handle] = (state, frame);
            }
            catch (ReadFailure) { vehicles.Remove(handle); failures++; } // stale handle or a failed call: drop it, the list will bring it back if real
        }
        if (order.Count > 0 && failures == order.Count && vehicles.Count == 0) throw new ReadFailure("road-vehicle.read failed for every vehicle tried");

        foreach (var stale in vehicles.Where(kv => frame - kv.Value.Frame > options.MaxVehicleAgeFrames).Select(kv => kv.Key).ToList()) vehicles.Remove(stale);
        var near = vehicles.Values.Select(v => v.State)
            .Where(s => Distance(s, player) <= options.NearbyRadiusMetres && !IsSamePose(s, player))
            .OrderBy(s => Distance(s, player))
            .ToList();
        var truncated = near.Count > options.MaxNearbyVehicles;
        return new NearbyVehicles(near.Take(options.MaxNearbyVehicles).ToList(), handles.Count, truncated);
    }

    // The player is also in road-vehicles.list; do not report it twice.
    private static bool IsSamePose(VehicleState a, VehicleState b) => a.Tile == b.Tile && Math.Abs(a.LocalX - b.LocalX) < 1e-3 && Math.Abs(a.LocalY - b.LocalY) < 1e-3;

    private static double Distance(VehicleState a, VehicleState b)
    {
        var dx = a.WorldX - b.WorldX; var dy = a.WorldY - b.WorldY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
