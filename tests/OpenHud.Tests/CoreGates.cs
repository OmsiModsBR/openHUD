using OpenHud.Tests.Core;

namespace OpenHud.Tests;

// Gates of the Core area (settings, i18n, packages, model). Each topic registers its own lines.
internal static class CoreGates
{
    public static void Register()
    {
        SettingsAndI18nGates.Register();
        PauseMenuGates.Register();
        RealmapGates.Register();
        ModelGates.Register();
    }
}
