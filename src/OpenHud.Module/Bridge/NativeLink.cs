using System.Globalization;
using System.Text.Json;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Scene;

namespace OpenHud.Bridge;

// The answer of one operation of the native draw layer: {"ok":true,...} or {"ok":false,"code":...,"detail":...}.
internal sealed record NativeAnswer(bool Ok, string? Code, string? Detail, string Raw)
{
    public static NativeAnswer Failed(string code, string detail) => new(false, code, detail, "");
}

// What the layer reports about itself (the `stats` operation); only what the module acts on.
internal sealed record NativeStats(bool Visible, bool Failed, int ViewWidth, int ViewHeight, string? LastError);

// The module's door to the native draw layer. Every failure (no export, an exception, a malformed
// answer, ok:false) becomes a NativeAnswer; nothing is thrown.
internal sealed class NativeLink
{
    private readonly IModuleNativeComponent? component;

    public NativeLink(IModuleNativeComponent? component) => this.component = component;

    public bool Available
    {
        get
        {
            try { return component is { HasExecute: true }; }
            catch (Exception) { return false; }
        }
    }

    public long Calls { get; private set; }
    public long Failures { get; private set; }

    public NativeAnswer Call(string operation, string argumentsJson)
    {
        Calls++;
        NativeAnswer answer;
        try
        {
            var raw = component?.Execute(operation, argumentsJson);
            answer = raw is null ? NativeAnswer.Failed("no_execute", "the draw layer has no execute export") : Parse(raw);
        }
        catch (Exception exception)
        {
            answer = NativeAnswer.Failed("exception", exception.GetType().Name);
        }
        if (!answer.Ok) Failures++;
        return answer;
    }

    internal static NativeAnswer Parse(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new NativeAnswer(false, "bad_answer", "the answer has no ok member", raw);
            if (ok.ValueKind == JsonValueKind.True) return new NativeAnswer(true, null, null, raw);
            return new NativeAnswer(false, Text(root, "code") ?? "failed", Text(root, "detail"), raw);
        }
        catch (JsonException)
        {
            return new NativeAnswer(false, "bad_answer", "the answer is not JSON", raw);
        }
    }

    public NativeStats? Stats()
    {
        var answer = Call("stats", SceneOps.Stats());
        if (!answer.Ok) return null;
        try
        {
            using var document = JsonDocument.Parse(answer.Raw);
            var root = document.RootElement;
            return new NativeStats(Bool(root, "visible"), Bool(root, "failed"), Int(root, "view_w"), Int(root, "view_h"), Text(root, "last_error"));
        }
        catch (JsonException) { return null; }
    }

    // Width of `text` in canvas pixels as the layer's fonts lay it out; null when the layer cannot say.
    public double? Measure(string text, double size, string font)
    {
        string arguments;
        try { arguments = SceneOps.Measure(text, size, font); }
        catch (SceneException) { return null; }
        var answer = Call("measure", arguments);
        if (!answer.Ok) return null;
        try
        {
            using var document = JsonDocument.Parse(answer.Raw);
            return document.RootElement.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i >= 0 ? i : 0;

    internal static string Describe(NativeAnswer answer) => answer.Ok ? "ok" : (answer.Code ?? "failed") + (string.IsNullOrEmpty(answer.Detail) ? "" : ": " + answer.Detail);

    internal static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
