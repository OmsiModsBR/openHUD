using System.Diagnostics;

namespace OpenHud.Tests;

// Gates of the Draw area. The rasterizer, the scene parser, the glyph atlas, the
// operations and the Direct3D 9 backend are native code, so their tests are a native
// console program (tests/native, built by tools/Build-Module.ps1). These gates run
// it once, check its exit code and its output line by line, and report per area.
internal static class DrawGates
{
    // A native test group that must never be skipped; the Direct3D group may be skipped only
    // because no device can be created, and then the gate says so out loud.
    private static readonly string[] Groups = { "json.", "scene.", "raster.", "atlas.", "layer.", "d3d9." };

    private sealed record NativeRun(int ExitCode, string[] Lines, string Error);

    private static NativeRun? run;

    public static void Register()
    {
        Gates.Run("draw.native-tests-pass", () =>
        {
            var result = Run();
            var summary = result.Lines.LastOrDefault(l => l.StartsWith("native tests:", StringComparison.Ordinal));
            Gates.Check(summary is not null, "the native test program printed no summary" + Tail(result));
            Gates.Check(result.ExitCode == 0, "the native tests failed (exit code " + result.ExitCode + "): " + string.Join(" | ", result.Lines.Where(l => l.StartsWith("FAIL", StringComparison.Ordinal))));
            Gates.Check(summary!.Contains(", 0 failed,", StringComparison.Ordinal), "the summary reports failures: " + summary);
            var passed = int.Parse(summary.Split(' ')[2]);
            Gates.Check(passed >= 45, "too few native tests ran (" + passed + "); a test group went missing");
        });

        Gates.Run("draw.native-no-unexpected-skips", () =>
        {
            var result = Run();
            foreach (var skip in result.Lines.Where(l => l.StartsWith("SKIP", StringComparison.Ordinal)))
            {
                var name = skip.Substring(5).Split(':')[0];
                // Only the Direct3D tests may skip, and only because no device could be created.
                Gates.Check(name.StartsWith("d3d9.", StringComparison.Ordinal), "a native test was skipped: " + skip);
                Gates.Check(skip.Contains("Direct3D", StringComparison.Ordinal) || skip.Contains("device", StringComparison.Ordinal) || skip.Contains("back buffer", StringComparison.Ordinal), "a Direct3D test was skipped for a reason other than the device: " + skip);
                Console.WriteLine("  NOTE (not a pass of that test): " + skip);
            }
        });

        foreach (var group in Groups)
        {
            var prefix = group;
            Gates.Run("draw.native-" + prefix.TrimEnd('.'), () =>
            {
                var lines = Run().Lines.Where(l => l.Contains(" " + prefix, StringComparison.Ordinal) && (l.StartsWith("PASS", StringComparison.Ordinal) || l.StartsWith("FAIL", StringComparison.Ordinal) || l.StartsWith("SKIP", StringComparison.Ordinal))).ToArray();
                Gates.Check(lines.Length > 0, "no native test of the group " + prefix + " ran");
                var failures = lines.Where(l => l.StartsWith("FAIL", StringComparison.Ordinal)).ToArray();
                Gates.Check(failures.Length == 0, string.Join(" | ", failures));
                if (prefix != "d3d9.")
                {
                    Gates.Check(lines.All(l => l.StartsWith("PASS", StringComparison.Ordinal)), "a test of the group " + prefix + " was skipped");
                }
                else
                {
                    var passed = lines.Count(l => l.StartsWith("PASS", StringComparison.Ordinal));
                    Console.WriteLine("  Direct3D 9 tests: " + passed + " ran, " + (lines.Length - passed) + " skipped");
                }
            });
        }

        // The parser and renderer refuse a hostile scene end to end: the golden vectors must be what the tests rendered.
        Gates.Run("draw.golden-vectors-present", () =>
        {
            var vectors = FindUp("tests", "vectors");
            foreach (var name in new[] { "scene-minimal.json", "scene-pause.json", "scene-radar.json" })
            {
                var path = Path.Combine(vectors, name);
                Gates.Check(File.Exists(path), "missing golden vector " + name);
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                Gates.Check(document.RootElement.GetProperty("v").GetInt32() == 1, name + " is not a version 1 scene");
            }
        });
    }

    private static string Tail(NativeRun result) => result.Lines.Length == 0 ? " (" + result.Error + ")" : " (last line: " + result.Lines[^1] + ")";

    private static string FindUp(params string[] relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relative).ToArray());
            if (Directory.Exists(candidate) || File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("cannot find " + Path.Combine(relative) + " above " + AppContext.BaseDirectory);
    }

    private static NativeRun Run()
    {
        if (run is not null) return run;
        string exe;
        try { exe = FindUp("artifacts", "bin", "OpenHud.Draw.Tests", "OpenHud.Draw.Tests.exe"); }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("the native tests are not built; run tools/Build-Module.ps1 (it builds tests/native/OpenHud.Draw.Tests.vcxproj)");
        }
        var vectors = FindUp("tests", "vectors");
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--vectors");
        info.ArgumentList.Add(vectors);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("the native test program did not start");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(true);
            throw new InvalidOperationException("the native tests did not finish in five minutes");
        }
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        run = new NativeRun(process.ExitCode, lines, errorTask.Result);
        return run;
    }
}
