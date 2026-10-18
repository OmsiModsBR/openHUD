using System.Text;
using OpenHud.Scene;
using static OpenHud.Tests.SceneTestUtil;

namespace OpenHud.Tests;

// Gates of the scene codec: encoder, decoder, validator limits, operation payloads, drawing helpers.
internal static class SceneCodecGates
{
    private static readonly string[] Vectors = { "scene-minimal.json", "scene-radar.json", "scene-pause.json" };

    public static void Register()
    {
        Gates.Run("scene.golden-vectors-roundtrip", GoldenRoundTrip);
        Gates.Run("scene.golden-vectors-built-by-builder", GoldenBuiltByBuilder);
        Gates.Run("scene.encoder-exact-string", EncoderExactString);
        Gates.Run("scene.encoder-number-format", NumberFormat);
        Gates.Run("scene.encoder-string-escaping", StringEscaping);
        Gates.Run("scene.encoder-is-deterministic", Deterministic);
        Gates.Run("scene.encoder-rejects-non-finite", RejectsNonFinite);
        Gates.Run("scene.limit-command-count", LimitCommandCount);
        Gates.Run("scene.limit-json-size", LimitJsonSize);
        Gates.Run("scene.limit-clip-depth", LimitClipDepth);
        Gates.Run("scene.limit-transform-depth", LimitTransformDepth);
        Gates.Run("scene.balance-refused", BalanceRefused);
        Gates.Run("scene.color-syntax", ColorSyntax);
        Gates.Run("scene.field-validation", FieldValidation);
        Gates.Run("scene.decoder-rejects-garbage", DecoderRejectsGarbage);
        Gates.Run("scene.ops-payloads", OpsPayloads);
        Gates.Run("scene.ops-refusals", OpsRefusals);
        Gates.Run("scene.text-measurer", TextMeasurer);
        Gates.Run("scene.draw-helpers", DrawHelpers);
    }

    private static void GoldenRoundTrip()
    {
        foreach (var name in Vectors)
        {
            string original = ReadVector(name);
            var scene = SceneDecoder.Decode(original);
            string encoded = SceneJson.Encode(scene);
            Gates.Check(JsonEqual(original, encoded), name + ": encode(decode(vector)) is structurally the vector");
            Gates.Check(SceneJson.Encode(SceneDecoder.Decode(encoded)) == encoded, name + ": the encoder output is a fixed point");
            Gates.Check(SceneValidator.Validate(scene).Count == 0, name + ": the vector validates");
        }
    }

    private static SceneDocument Minimal() => new SceneBuilder()
        .Rect(10, 20, 300, 120, "#14171cd9", null, null, 12)
        .Text(24, 40, "openHUD", 22, "bold", "#ffffff", null, "mid")
        .Build();

    private static SceneDocument Radar() => new SceneBuilder()
        .Circle(1700, 880, 160, "#0b151ee6", "#ffffff", 4)
        .ClipCircle(1700, 880, 156)
        .Push(1700, 880, -35)
        .Poly(new double[] { -60, -120, 60, -120, 60, 120, -60, 120 }, null, "#5588c7", 6, false)
        .Pop()
        .Unclip()
        .Arc(1700, 880, 158, -40, 40, "#00c94a", 5, "round")
        .Image("player", 1684, 864, 32, 32)
        .Build();

    private static SceneDocument Pause() => new SceneBuilder(1920, 1080, true)
        .Rect(0, 0, 1920, 1080, "#000000a6")
        .Rect(760, 420, 400, 56, "#00c94a", null, null, 10)
        .Text(960, 448, "Resume", 18, "bold", "#ffffff", "c", "mid")
        .Build();

    private static void GoldenBuiltByBuilder()
    {
        var built = new[] { Minimal(), Radar(), Pause() };
        for (int i = 0; i < Vectors.Length; i++)
            Gates.Check(JsonEqual(ReadVector(Vectors[i]), SceneJson.Encode(built[i])), Vectors[i] + ": builder output equals the golden vector");
    }

    private static void EncoderExactString()
    {
        const string expected =
            "{\"v\":1,\"w\":1920,\"h\":1080,\"background\":false,\"cmds\":["
            + "{\"t\":\"rect\",\"x\":10,\"y\":20,\"w\":300,\"h\":120,\"fill\":\"#14171cd9\",\"r\":12},"
            + "{\"t\":\"text\",\"x\":24,\"y\":40,\"s\":\"openHUD\",\"size\":22,\"font\":\"bold\",\"color\":\"#ffffff\",\"base\":\"mid\"}]}";
        Gates.Check(SceneJson.Encode(Minimal()) == expected, "minimal scene encodes to the exact documented string");

        const string everything =
            "{\"v\":1,\"w\":100,\"h\":50,\"background\":true,\"cmds\":["
            + "{\"t\":\"circle\",\"cx\":1,\"cy\":2,\"r\":3,\"fill\":\"#010203\",\"stroke\":\"#04050607\",\"sw\":1.5},"
            + "{\"t\":\"line\",\"x1\":0,\"y1\":0,\"x2\":5,\"y2\":5,\"color\":\"#ffffff\",\"sw\":2,\"cap\":\"butt\"},"
            + "{\"t\":\"poly\",\"pts\":[0,0,1,1,2,0],\"fill\":\"#112233\",\"closed\":true},"
            + "{\"t\":\"text\",\"x\":1,\"y\":2,\"s\":\"a\",\"size\":10,\"font\":\"condensed\",\"color\":\"#ffffff\",\"align\":\"r\",\"base\":\"alpha\",\"maxw\":40,\"outline\":\"#00000080\"},"
            + "{\"t\":\"image\",\"id\":\"i\",\"x\":0,\"y\":0,\"w\":8,\"h\":8,\"op\":0.5,\"tint\":\"#ff0000\",\"rot\":90},"
            + "{\"t\":\"clip\",\"kind\":\"rect\",\"x\":0,\"y\":0,\"w\":10,\"h\":10},"
            + "{\"t\":\"unclip\"},"
            + "{\"t\":\"push\",\"tx\":1,\"ty\":2,\"rot\":3,\"sx\":4,\"sy\":5},"
            + "{\"t\":\"pop\"}]}";
        var scene = new SceneBuilder(100, 50, true)
            .Circle(1, 2, 3, "#010203", "#04050607", 1.5)
            .Line(0, 0, 5, 5, "#ffffff", 2, "butt")
            .Poly(new double[] { 0, 0, 1, 1, 2, 0 }, "#112233", null, null, true)
            .Text(1, 2, "a", 10, "condensed", "#ffffff", "r", "alpha", 40, "#00000080")
            .Image("i", 0, 0, 8, 8, 0.5, "#ff0000", 90)
            .ClipRect(0, 0, 10, 10).Unclip()
            .Push(1, 2, 3, 4, 5).Pop()
            .Build();
        Gates.Check(SceneJson.Encode(scene) == everything, "every command kind encodes in the protocol's member order");
        Gates.Check(JsonEqual(everything, SceneJson.Encode(SceneDecoder.Decode(everything))), "and decodes back losslessly");
    }

    private static string Num(double v) =>
        SceneJson.Encode(new SceneBuilder().Line(v, 0, 0, 0, "#ffffff", 1).Build()).Split("\"x1\":")[1].Split(',')[0];

    private static void NumberFormat()
    {
        Gates.Check(Num(0.1) == "0.1", "0.1 stays 0.1");
        Gates.Check(Num(1.0 / 3) == "0.3333333333333333", "shortest round-trip for 1/3");
        Gates.Check(Num(-0.0) == "0", "negative zero is folded");
        Gates.Check(Num(1920) == "1920", "integers have no fraction");
        Gates.Check(Num(-35.5) == "-35.5", "negative fractions");
        Gates.Check(double.Parse(Num(1e-7), System.Globalization.CultureInfo.InvariantCulture) == 1e-7, "tiny values round-trip");
        Gates.Check(double.Parse(Num(123456789012345.6), System.Globalization.CultureInfo.InvariantCulture) == 123456789012345.6, "large values round-trip");
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            Gates.Check(Num(1.5) == "1.5", "the current culture does not leak into the output");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    private static void StringEscaping()
    {
        string tricky = "a\"b\\c\nd\te\u0001f\u00e7\u00e3o\U0001F68C";
        var scene = new SceneBuilder().Text(0, 0, tricky, 10).Build();
        string json = SceneJson.Encode(scene);
        Gates.Check(json.Contains("\\\"") && json.Contains("\\\\") && json.Contains("\\n") && json.Contains("\\t") && json.Contains("\\u0001"), "specials are escaped");
        Gates.Check(json.Contains("\u00e7\u00e3o\U0001F68C"), "non-ASCII text is kept as UTF-8, not \\u-escaped");
        var back = (TextCommand)SceneDecoder.Decode(json).Commands[0];
        Gates.Check(back.Text == tricky, "text round-trips exactly");
        string lone = SceneJson.Encode(new SceneBuilder().Text(0, 0, "x\ud800y", 10).Build());
        Gates.Check(((TextCommand)SceneDecoder.Decode(lone).Commands[0]).Text == "x\ufffdy", "a lone surrogate becomes U+FFFD");
        Encoding.UTF8.GetBytes(lone); // must be encodable
    }

    private static void Deterministic()
    {
        var a = SceneJson.Encode(Radar());
        var b = SceneJson.Encode(Radar());
        Gates.Check(a == b, "same scene, same bytes");
        Gates.Check(SceneJson.EncodeUtf8(Radar()).SequenceEqual(Encoding.UTF8.GetBytes(a)), "EncodeUtf8 is the UTF-8 of Encode");
    }

    private static void RejectsNonFinite()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<SceneException>(() => SceneJson.Encode(new SceneDocument(10, 10, false, new SceneCommand[] { new RectCommand(bad, 0, 1, 1) })), "rect x " + bad);
            Throws<SceneException>(() => SceneJson.Encode(new SceneDocument(10, 10, false, new SceneCommand[] { new PolyCommand(new[] { 0, 0, bad, 1.0 }) })), "poly pt " + bad);
            Throws<SceneException>(() => SceneJson.Encode(new SceneDocument(10, 10, false, new SceneCommand[] { new TextCommand(0, 0, "x", 10, MaxWidth: bad) })), "text maxw " + bad);
            Throws<SceneException>(() => new SceneBuilder().Push(rot: bad).Pop().Build(), "push rot " + bad);
            Throws<SceneException>(() => new SceneBuilder().Arc(0, 0, 5, 0, bad, "#ffffff", 1).Build(), "arc a1 " + bad);
        }
    }

    private static void LimitCommandCount()
    {
        var ok = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxCommands; i++) ok.Rect(0, 0, 1, 1, "#ffffff");
        SceneJson.Encode(ok.Build());
        var over = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxCommands + 1; i++) over.Rect(0, 0, 1, 1, "#ffffff");
        Throws<SceneException>(() => over.Build(), "8193 commands");
    }

    private static void LimitJsonSize()
    {
        // One polygon with enough long numbers to pass 1 MiB while staying a single command.
        var pts = new double[150_000];
        for (int i = 0; i < pts.Length; i++) pts[i] = 1234.5678 + i * 0.001;
        var doc = new SceneBuilder().Poly(pts, null, "#ffffff", 1).Build(); // structurally valid
        var ex = Throws<SceneException>(() => SceneJson.Encode(doc), "oversized json");
        Gates.Check(ex.Message.Contains("bytes"), "refused for size: " + ex.Message);
        // just under the limit is accepted
        var small = new double[20_000];
        for (int i = 0; i < small.Length; i++) small[i] = i % 1000;
        Gates.Check(SceneJson.Encode(new SceneBuilder().Poly(small, null, "#ffffff", 1).Build()).Length < SceneLimits.MaxJsonBytes, "a big but legal scene is accepted");
    }

    private static void LimitClipDepth()
    {
        var ok = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxClipDepth; i++) ok.ClipRect(0, 0, 10, 10);
        for (int i = 0; i < SceneLimits.MaxClipDepth; i++) ok.Unclip();
        ok.Build();
        var over = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxClipDepth + 1; i++) over.ClipCircle(5, 5, 5);
        for (int i = 0; i < SceneLimits.MaxClipDepth + 1; i++) over.Unclip();
        Throws<SceneException>(() => over.Build(), "clip depth 9");
    }

    private static void LimitTransformDepth()
    {
        var ok = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxTransformDepth; i++) ok.Push(1, 1);
        for (int i = 0; i < SceneLimits.MaxTransformDepth; i++) ok.Pop();
        ok.Build();
        var over = new SceneBuilder();
        for (int i = 0; i < SceneLimits.MaxTransformDepth + 1; i++) over.Push(1, 1);
        for (int i = 0; i < SceneLimits.MaxTransformDepth + 1; i++) over.Pop();
        Throws<SceneException>(() => over.Build(), "transform depth 17");
    }

    private static void BalanceRefused()
    {
        Throws<SceneException>(() => new SceneBuilder().ClipRect(0, 0, 1, 1).Build(), "clip never closed");
        Throws<SceneException>(() => new SceneBuilder().Unclip().Build(), "unclip without clip");
        Throws<SceneException>(() => new SceneBuilder().Push(1, 1).Build(), "push never popped");
        Throws<SceneException>(() => new SceneBuilder().Pop().Build(), "pop without push");
        Throws<SceneException>(() => new SceneBuilder().ClipRect(0, 0, 1, 1).Unclip().Unclip().Build(), "extra unclip");
        new SceneBuilder().ClipRect(0, 0, 1, 1).Push(1, 1).Pop().Unclip().Build(); // interleaved but balanced
    }

    private static void ColorSyntax()
    {
        foreach (var good in new[] { "#000000", "#ABCDEF", "#abcdef12", "#00c94a" })
            Gates.Check(SceneColor.IsValid(good), good + " is valid");
        foreach (var bad in new[] { "", "#", "#fff", "#ffff", "#fffff", "#fffffff", "#ffffffffff", "ffffff", "#gggggg", "red", "#ffffff ", "rgb(0,0,0)", "#ffffffg0" })
        {
            Gates.Check(!SceneColor.IsValid(bad), "'" + bad + "' is not valid");
            Throws<SceneException>(() => new SceneBuilder().Rect(0, 0, 1, 1, bad).Build(), "fill '" + bad + "'");
        }
        Throws<SceneException>(() => new SceneBuilder().Line(0, 0, 1, 1, "nope", 1).Build(), "line color");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 10, null, "#12").Build(), "text color");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 10, null, null, null, null, null, "#12").Build(), "text outline");
        Gates.Check(SceneColor.WithAlpha("#14171c", 0.5) == "#14171c80", "WithAlpha replaces alpha");
        Gates.Check(SceneColor.WithAlpha("#14171cd9", 1) == "#14171cff", "WithAlpha on an rgba color");
        Gates.Check(SceneColor.WithAlpha("#14171c", double.NaN) == "#14171cff", "NaN alpha is opaque");
    }

    private static void FieldValidation()
    {
        Throws<SceneException>(() => new SceneBuilder().Poly(new double[] { 1, 2, 3 }).Build(), "odd pts");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 0).Build(), "text size 0");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 10, "comic").Build(), "unknown font role");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 10, null, null, "m").Build(), "align");
        Throws<SceneException>(() => new SceneBuilder().Text(0, 0, "x", 10, null, null, null, "bottom").Build(), "base");
        Throws<SceneException>(() => new SceneBuilder().Arc(0, 0, 5, 0, 90, "#ffffff", 1, "square").Build(), "cap");
        Throws<SceneException>(() => new SceneBuilder().Image("", 0, 0, 1, 1).Build(), "empty image id");
        Throws<SceneException>(() => new SceneBuilder().Image("a", 0, 0, 1, 1, 1.5).Build(), "op > 1");
        Throws<SceneException>(() => new SceneBuilder().Image("a", 0, 0, 1, 1, -0.1).Build(), "op < 0");
        Throws<SceneException>(() => new SceneBuilder().Rect(0, 0, -1, 1).Build(), "negative width");
        Throws<SceneException>(() => new SceneBuilder().Circle(0, 0, -2).Build(), "negative radius");
        Throws<SceneException>(() => new SceneBuilder(0, 100).Build(), "canvas 0");
        Throws<SceneException>(() => new SceneBuilder(100, SceneLimits.MaxCanvas + 1).Build(), "canvas too large");
        Gates.Check(new SceneBuilder().Build().Commands.Count == 0, "an empty scene is valid");
        Throws<ArgumentNullException>(() => new SceneBuilder().Add(null!), "null command");
    }

    private static void DecoderRejectsGarbage()
    {
        foreach (var bad in new[]
        {
            "", "[]", "{", "null", "{\"v\":2,\"w\":1,\"h\":1,\"cmds\":[]}", "{\"w\":1,\"h\":1,\"cmds\":[]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"cmds\":[{\"t\":\"warp\"}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"cmds\":[{\"t\":\"rect\",\"x\":1}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"cmds\":[{\"t\":\"clip\",\"kind\":\"star\"}]}",
            "{\"v\":1,\"w\":1,\"h\":1,\"cmds\":[{\"x\":1}]}",
        })
        {
            bool threw = false;
            try { SceneDecoder.Decode(bad); }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException) { threw = true; }
            Gates.Check(threw, "decoder refuses: " + bad);
        }
    }

    private static void OpsPayloads()
    {
        var rgba = new byte[2 * 2 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)i;
        string put = SceneOps.ImagePut("s1", 2, 2, rgba);
        Gates.Check(put == "{\"id\":\"s1\",\"w\":2,\"h\":2,\"rgba\":\"" + Convert.ToBase64String(rgba) + "\"}", "image.put payload: " + put);
        Gates.Check(SceneOps.ImageDrop("s1") == "{\"id\":\"s1\"}", "image.drop");
        Gates.Check(SceneOps.FontPut("bold", @"C:\fonts\a.ttf") == "{\"role\":\"bold\",\"path\":\"C:\\\\fonts\\\\a.ttf\"}", "font.put escapes the path");
        Gates.Check(SceneOps.Visible(true) == "{\"on\":true}" && SceneOps.Visible(false) == "{\"on\":false}", "visible");
        Gates.Check(SceneOps.BackgroundCapture() == "{}" && SceneOps.BackgroundRelease() == "{}" && SceneOps.Stats() == "{}", "empty-argument operations");
        Gates.Check(SceneOps.EmptyScene() == "{\"v\":1,\"cmds\":[]}", "empty scene document");
        foreach (var json in new[] { put, SceneOps.EmptyScene(), SceneOps.FontPut("regular", @"D:\x.otf") })
            System.Text.Json.JsonDocument.Parse(json).Dispose();
        Gates.Check(SceneJson.Encode(SceneDecoder.Decode(SceneOps.EmptyScene().Replace("\"cmds\"", "\"w\":1,\"h\":1,\"cmds\""))).Contains("\"cmds\":[]"), "the empty scene decodes");
    }

    private static void OpsRefusals()
    {
        Throws<SceneException>(() => SceneOps.ImagePut("", 1, 1, new byte[4]), "empty id");
        Throws<SceneException>(() => SceneOps.ImagePut("a", 0, 1, Array.Empty<byte>()), "zero width");
        Throws<SceneException>(() => SceneOps.ImagePut("a", 2, 2, new byte[15]), "short rgba");
        Throws<SceneException>(() => SceneOps.ImagePut("a", 2, 2, new byte[17]), "long rgba");
        Throws<SceneException>(() => SceneOps.ImagePut("a", 1025, 1024, new byte[1025 * 1024 * 4]), "image over 4 MiB");
        SceneOps.ImagePut("a", 1024, 1024, new byte[1024 * 1024 * 4]); // exactly 4 MiB is allowed
        Throws<SceneException>(() => SceneOps.ImageDrop(""), "drop empty id");
        Throws<SceneException>(() => SceneOps.FontPut("comic", @"C:\a.ttf"), "unknown role");
        Throws<SceneException>(() => SceneOps.FontPut("bold", "a.ttf"), "relative path");
        Throws<SceneException>(() => SceneOps.FontPut("bold", ""), "empty path");
    }

    private static void TextMeasurer()
    {
        var m = new ApproximateTextMeasurer();
        Gates.Check(m.Measure("", 14, "regular") == 0, "empty text has no width");
        Gates.Check(m.Measure("abc", 0, "regular") == 0 && m.Measure("abc", double.NaN, "regular") == 0, "no size, no width");
        double w10 = m.Measure("Hello", 10, "regular"), w20 = m.Measure("Hello", 20, "regular");
        Gates.Check(Math.Abs(w20 - 2 * w10) < 1e-9, "width scales with size");
        Gates.Check(m.Measure("Hello", 10, "bold") > w10, "bold is wider");
        Gates.Check(m.Measure("Hello", 10, "regular") == w10, "deterministic");
    }

    private static void DrawHelpers()
    {
        var m = new ApproximateTextMeasurer();
        var b = new SceneBuilder();
        b.Panel(10, 10, 100, 50, 8, "#14171c", "#2a2f38");
        Gates.Check(b.Count == 1 && ((RectCommand)SceneDecoder.Decode(SceneJson.Encode(b.Build())).Commands[0]).Radius == 8, "panel is one rounded rect");

        b = new SceneBuilder();
        b.Button(0, 0, 200, 50, "Resume", new ButtonStyle("#3d8bfd", "#ffffff"));
        var btn = b.Build();
        Gates.Check(btn.Commands.Count == 2 && btn.Commands[1] is TextCommand { Align: "c", Baseline: "mid", X: 100, Y: 25 }, "button: rect plus centered label");
        Gates.Check(((TextCommand)btn.Commands[1]).MaxWidth == 184, "label is clipped to the button width");

        b = new SceneBuilder();
        double chipW = b.Chip(5, 5, "Line 12", new ChipStyle("#3d8bfd", "#ffffff"), m);
        Gates.Check(Math.Abs(chipW - (m.Measure("Line 12", 13, "medium") + 20)) < 1e-9, "chip width follows the measurer");
        double chipW2 = new SceneBuilder().Chip(5, 5, "Line 12 and more", new ChipStyle("#3d8bfd", "#ffffff"), m);
        Gates.Check(chipW2 > chipW, "longer label, wider chip");
        Gates.Check(b.Count == 2, "chip: pill plus label");

        b = new SceneBuilder();
        b.Badge(20, 20, 10, "3", "#e5484d", "#ffffff");
        Gates.Check(b.Count == 2 && b.Build().Commands[0] is CircleCommand { R: 10 }, "badge: disc plus text");

        b = new SceneBuilder();
        b.IconSlot(0, 0, 32, "stop", "#ffffff", "#2a2f38").IconSlot(40, 0, 32, null, null, "#2a2f38").IconSlot(80, 0, 32, "", null, "#2a2f38");
        var icons = b.Build();
        Gates.Check(icons.Commands[0] is ImageCommand { Id: "stop" } && icons.Commands[1] is RectCommand && icons.Commands[2] is RectCommand, "icon slot: image, else placeholder");

        b = new SceneBuilder();
        b.ArcGauge(100, 100, 50, 0.5, -120, 240, "#2a2f38", "#00c94a", 8);
        var g = b.Build();
        Gates.Check(g.Commands.Count == 2 && g.Commands[1] is ArcCommand { A0: -120, A1: 0 }, "gauge at 0.5 sweeps half the track");
        foreach (var v in new[] { double.NaN, -1, 0 })
        {
            b = new SceneBuilder();
            b.ArcGauge(0, 0, 5, v, 0, 90, "#2a2f38", "#00c94a", 2);
            Gates.Check(b.Build().Commands.Count == 1, "an empty gauge draws the track only (" + v + ")");
        }
        b = new SceneBuilder();
        b.ArcGauge(0, 0, 5, 7, 0, 90, "#2a2f38", "#00c94a", 2);
        Gates.Check(b.Build().Commands[1] is ArcCommand { A1: 90 }, "values above 1 clamp to a full arc");
    }
}
