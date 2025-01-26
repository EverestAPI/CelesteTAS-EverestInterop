using Celeste;
using Celeste.Mod.Core;
using Monocle;
using TAS.Module;

namespace TAS.Tools;

/// Runs a TAS specified by the "--tas" CLI flag at launch
internal static class PlayTasAtLaunch {
    [Load]
    private static void Load() {
        On.Celeste.Celeste.OnSceneTransition += On_Celeste_OnScreenTransition;
    }
    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.OnSceneTransition -= On_Celeste_OnScreenTransition;
    }

    /// Pending file which should be played
    public static string? FilePath;

    private static void On_Celeste_OnScreenTransition(On.Celeste.Celeste.orig_OnSceneTransition orig, Celeste.Celeste self, Scene last, Scene next) {
        orig(self, last, next);

        if (FilePath != null && next is Overworld) {
            // Allow scene to initialize itself
            CoreModule.Settings.DebugMode = CoreModuleSettings.VanillaTristate.Everest;
            next.OnEndOfFrame += () => {
                Manager.Controller.FilePath = FilePath;
                Manager.EnableRun();

                FilePath = null;
            };

        }
    }
}
