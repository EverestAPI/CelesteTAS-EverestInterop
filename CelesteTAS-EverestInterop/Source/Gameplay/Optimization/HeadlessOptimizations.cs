using Celeste.Mod;
using TAS.Module;
using TAS.Tools;

namespace TAS.Gameplay.Optimization;

/// Optimizations applied to the game, which are only applicable in headless mode
/// Most simply involve not computing data, which is only visually required
internal static class HeadlessOptimizations {

    [LoadContent]
    private static void LoadContent() {
        if (!Everest.Flags.IsHeadless && !SyncChecker.Active) {
            return;
        }

        // Currently none
    }
}
