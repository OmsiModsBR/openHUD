using OpenHud.Tests.ModuleTests;

namespace OpenHud.Tests;

// Gates of the integration: the module wired to a fake of the OmsiLaunch bridge (catalog, content,
// input, native draw layer), complete sessions, the adapters, the failure paths and the manifest.
internal static class ModuleGates
{
    public static void Register()
    {
        SessionGates.Register();
        PauseJourneyGates.Register();
        ResilienceGates.Register();
        UnitGates.Register();
        GapSessionGates.Register();
        TickCostGates.Register();
    }
}
