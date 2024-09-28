using TAS.Module;

namespace TAS.EverestInterop;

public static class PreventExitGame {
    [Load]
    private static void Load() {
        On.Celeste.OuiMainMenu.OnExit += OuiMainMenuOnOnExit;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.OuiMainMenu.OnExit -= OuiMainMenuOnOnExit;
    }

    private static void OuiMainMenuOnOnExit(On.Celeste.OuiMainMenu.orig_OnExit orig, Celeste.OuiMainMenu self) {
        if (Manager.Running) {
            AbortTas("To close the game while TAS is running\nuse the ExitGame command instead");
            return;
        }

        orig(self);
    }
}