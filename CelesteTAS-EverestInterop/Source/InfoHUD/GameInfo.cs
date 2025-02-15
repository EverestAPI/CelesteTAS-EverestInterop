using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using TAS.Gameplay;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Provides detailed information about the current game state
public static class GameInfo {
    public enum Target { InGameHud, Studio, ExactInfo, ExactInfoAllowCodeExecution }

    /// Fetches the info for the current frame
    /// May **not** be called during scene.Update! Only before or after Update.
    [PublicAPI]
    public static string Query(Target target) {
#if DEBUG
        if (midUpdate) {
            "Attempted to call GameInfo.Query() during Update!".Log(LogLevel.Error);
            return "<ERROR: Attempted to call GameInfo.Query() during Update>";
        }
#endif

        return target.ToString();
    }

    // Store data which is required for the info, but will change until it is used
    [Events.PostSceneUpdate]
    private static void PostSceneUpdate(Scene scene) {
        if (scene is Level level) {

        }
    }

    // Safety check that Query isn't used mid-update
#if DEBUG
    private static bool midUpdate = false;

    private static void Load() {
        typeof(Scene)
            .GetMethodInfo(nameof(Scene.BeforeUpdate))!
            .HookBefore(() => midUpdate = true);
        typeof(Scene)
            .GetMethodInfo(nameof(Scene.AfterUpdate))!
            .HookAfter(() => midUpdate = false);
    }
#endif
}
