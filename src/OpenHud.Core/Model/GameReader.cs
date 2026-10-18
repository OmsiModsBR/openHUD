using System.Collections.ObjectModel;

namespace OpenHud.Model;

// The answer of one catalog operation: typed error code or the string dictionary of its outputs.
// Same shape as the bridge's ModuleCallResult; the module project adapts one to the other.
public sealed record GameReply(bool Succeeded, string? ErrorCode, IReadOnlyDictionary<string, string> Values)
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public static GameReply Ok(IReadOnlyDictionary<string, string> values) => new(true, null, values);
    public static GameReply Ok(params (string Key, string Value)[] values) => new(true, null, values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
    public static GameReply Fail(string code) => new(false, code, NoValues);
}

// The only way the model reads the game: catalog operations by name with string arguments.
// Implementations may throw (a timed-out call, a broken pipe); FrameSource treats that as
// "this part is unavailable" and never lets it escape.
public interface IGameReader
{
    GameReply Read(string operation, IReadOnlyDictionary<string, string>? arguments = null);
}

public static class CatalogOperations
{
    public const string Time = "time.read";
    public const string Map = "map.read";
    public const string MapTiles = "map.tiles.list";
    public const string Camera = "camera.read";
    public const string PlayerVehicle = "player-vehicle.read";
    public const string RoadVehicles = "road-vehicles.list";
    public const string RoadVehicle = "road-vehicle.read";
    public const string Timetable = "timetable.read";
    public const string TimetableLogs = "timetable.logs.read";
    public const string Weather = "weather.read";
}

// One recorded instant: the reply of every call that the consumer may make, keyed by ReplayGameReader.Key.
public sealed class ReplayFrame
{
    public Dictionary<string, GameReply> Replies { get; } = new(StringComparer.Ordinal);
    public ReplayFrame Add(string operation, GameReply reply, params (string Key, string Value)[] arguments)
    {
        Replies[ReplayGameReader.Key(operation, arguments.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal))] = reply;
        return this;
    }
}

// Plays a recorded sequence back as an IGameReader so every consumer can run offline. A call the frame
// does not hold answers OL_E_RUNTIME_OPERATION_UNAVAILABLE, like an operation the build lacks.
public sealed class ReplayGameReader : IGameReader
{
    private readonly IReadOnlyList<ReplayFrame> frames;
    private int index;

    public ReplayGameReader(IReadOnlyList<ReplayFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("a replay needs at least one frame", nameof(frames));
        this.frames = frames;
    }

    public int FrameIndex => index;
    public int FrameCount => frames.Count;
    public int Calls { get; private set; }

    // Moves to the next frame; stays on the last one when the recording ends. Returns false at the end.
    public bool Advance()
    {
        if (index + 1 >= frames.Count) return false;
        index++;
        return true;
    }

    public GameReply Read(string operation, IReadOnlyDictionary<string, string>? arguments = null)
    {
        Calls++;
        return frames[index].Replies.TryGetValue(Key(operation, arguments), out var reply) ? reply : GameReply.Fail("OL_E_RUNTIME_OPERATION_UNAVAILABLE");
    }

    // "op" or "op?k=v&k2=v2" with arguments sorted by name.
    public static string Key(string operation, IReadOnlyDictionary<string, string>? arguments)
    {
        if (arguments is null || arguments.Count == 0) return operation;
        return operation + "?" + string.Join("&", arguments.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
    }
}
