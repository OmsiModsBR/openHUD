namespace OpenHud.Tests;

// The test harness: a gate is a named function that throws on failure. Gates are
// registered in Program.cs, one line each, and run in that order.
internal static class Gates
{
    private static int failures;

    public static void Run(string name, Action gate)
    {
        try { gate(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    public static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    public static int Failures => failures;
}
