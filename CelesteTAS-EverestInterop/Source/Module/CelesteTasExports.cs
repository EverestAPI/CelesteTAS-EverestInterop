using MonoMod.ModInterop;

namespace TAS.Module;

[ModExportName("CelesteTAS")]
public static class CelesteTasExports {
    internal static void Export() => typeof(CelesteTasExports).ModInterop();

    public static void Register(int state, string stateName) => PlayerStates.Register(state, stateName);
    public static void Unregister(int state) => PlayerStates.Unregister(state);
}