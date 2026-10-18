
namespace OpenHud.Model;

// One part of a snapshot: the value, or "unavailable" with the reason. A failed or garbled read
// of a part never affects the other parts.
public sealed class Reading<T> where T : class
{
    private Reading(T? value, string? problem) { Value = value; Problem = problem; }

    public T? Value { get; }
    public string? Problem { get; }
    public bool IsAvailable => Value is not null;

    public static Reading<T> Of(T value) => new(value, null);
    public static Reading<T> Unavailable(string problem) => new(null, problem);
}

public sealed record GameTime(int Hour, int Minute, double Second, int? Day, int? Month, int? Year)
{
    public double SecondsOfDay => Hour * 3600.0 + Minute * 60.0 + Second;
    // yyyymmdd when the date is complete (the form lines.lin uses), else null.
    public int? DateNumber => Year is { } y && Month is { } m && Day is { } d ? y * 10000 + m * 100 + d : null;
}

public sealed record MapInfo(
    bool Loaded,
    string? Name,
    string? FileName,
    string? FriendlyName,
    bool? LeftHandTraffic,
    int? LoadedTiles,
    int? YearStart,
    int? YearEnd,
    double? MaxSpeed)
{
    // maps\<folder>\global.cfg when the reported file name has that shape, else null; the key for a realmap package.
    public string? RealmapIdentity
    {
        get
        {
            if (FileName is null) return null;
            var path = FileName.Replace('/', '\\');
            var at = path.LastIndexOf("maps\\", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            var tail = path[at..];
            var parts = tail.Split('\\');
            return parts.Length == 3 && parts[1].Length > 0 && parts[2].Equals("global.cfg", StringComparison.OrdinalIgnoreCase) ? tail : null;
        }
    }
}

public enum CameraFamily { Driver = 0, Passenger = 1, External = 2, Map = 3, Unknown = -1 }

public sealed record CameraInfo(CameraFamily Family, string? Name, double? FieldOfView, double? NormalFieldOfView, double? Distance);

// A road vehicle's pose and basics. Local* is OMSI's position inside its tile; World* is the tile origin
// plus that (map space, metres of the tile grid) when the tile is known, else the local value as given.
public sealed record VehicleState(
    string? Handle,
    int Tile,
    double LocalX,
    double LocalY,
    double LocalZ,
    double WorldX,
    double WorldY,
    bool WorldResolved,
    // Degrees clockwise from map north (+Y); null when the orientation is not a usable rotation.
    double? HeadingDegrees,
    double SpeedMetresPerSecond,
    int? TrafficType,
    bool? AiEnabled)
{
    public double SpeedKmh => SpeedMetresPerSecond * 3.6;
}

public sealed record PlayerState(bool Present, VehicleState? Vehicle);

public sealed record NearbyVehicles(IReadOnlyList<VehicleState> Vehicles, int KnownCount, bool Truncated);

public sealed record TimetableLogRow(string? BusStop, int? EstimatedArrival, int? EstimatedDeparture, int? ActualArrival, int? ActualDeparture, bool? ArrivalOk, bool? DepartureOk);

public sealed record TimetableInfo(bool? Invalid, int? Tracks, int? Trips, int? BusStops, int? StationLinks, int? Lines, int? RvFiles, IReadOnlyList<TimetableLogRow> Logs);

public sealed record WeatherInfo(
    double? Temperature,
    double? WindSpeed,
    double? WindDirection,
    double? FogDensity,
    double? Precipitation,
    string? CloudTypeName,
    bool? Snow,
    bool? WetGround);

// Immutable picture of the game at one tick; what every consumer (radar, pause, scene) reads.
public sealed record FrameSnapshot(
    long Sequence,
    Reading<GameTime> Time,
    Reading<MapInfo> Map,
    Reading<CameraInfo> Camera,
    Reading<PlayerState> Player,
    Reading<NearbyVehicles> Vehicles,
    Reading<TimetableInfo> Timetable,
    Reading<WeatherInfo> Weather,
    double TileMetres)
{
    public IEnumerable<string> Problems
    {
        get
        {
            if (Time.Problem is { } t) yield return "time: " + t;
            if (Map.Problem is { } m) yield return "map: " + m;
            if (Camera.Problem is { } c) yield return "camera: " + c;
            if (Player.Problem is { } p) yield return "player: " + p;
            if (Vehicles.Problem is { } v) yield return "vehicles: " + v;
            if (Timetable.Problem is { } tt) yield return "timetable: " + tt;
            if (Weather.Problem is { } w) yield return "weather: " + w;
        }
    }

    // The player's vehicle when there is one and its pose was readable.
    public VehicleState? PlayerVehicle => Player.Value is { Present: true, Vehicle: { } v } ? v : null;
}
