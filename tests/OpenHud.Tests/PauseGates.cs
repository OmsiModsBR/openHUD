using OpenHud.Tests.PauseTests;

namespace OpenHud.Tests;

// Gates of the Pause area: the state machine, the menu model, the layout and the scene form.
internal static class PauseGates
{
    public static void Register()
    {
        MachineGates.Register();
        MenuGates.Register();
        LayoutGates.Register();
    }
}
