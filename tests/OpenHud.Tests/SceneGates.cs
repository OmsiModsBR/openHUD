namespace OpenHud.Tests;

// Gates of the scene codec and the radar/map builders (src/OpenHud.Core/Scene, src/OpenHud.Core/Radar).
internal static class SceneGates
{
    public static void Register()
    {
        SceneCodecGates.Register();
        RadarGates.Register();
        BuildCostGates.Register();
        ReuseGates.Register();
        UnitsAndArtGates.Register();
    }
}
