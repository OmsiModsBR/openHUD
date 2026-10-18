using OpenHud.Tests;

// OpenHud.Tests: offline gates of the openHUD module. Each phase adds its gates
// below; the order is the order they run in. Exit code 0 = all green.
internal static class Program
{
    private static int Main()
    {
        Gates.Run("module.skeleton-loads", SkeletonGate);
        CoreGates.Register();
        SceneGates.Register();
        PauseGates.Register();
        DrawGates.Register();
        AuxiGates.Register();
        Console.WriteLine(Gates.Failures == 0 ? "all gates passed" : Gates.Failures + " gate(s) failed");
        return Gates.Failures == 0 ? 0 : 1;
    }

    private static void SkeletonGate()
    {
        var module = new OpenHud.HudModule();
        Gates.Check(module is OmsiLaunch.Runtime.Modules.IRuntimeModule, "the entry implements IRuntimeModule");
    }
}
