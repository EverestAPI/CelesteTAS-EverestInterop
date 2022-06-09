#if DEBUG
using JetBrains.Profiler.Api;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;

namespace TAS.EverestInterop;

public static class Benchmark {
    private static bool lastRunning;

    [Load]
    private static void Load() {
        On.Monocle.Engine.Update += EngineOnUpdate;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= EngineOnUpdate;
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        orig(self, gameTime);
        if (lastRunning != Manager.Running && Manager.Controller.HasFastForward) {
            if (Manager.Running) {
                MeasureProfiler.StartCollectingData();
            } else {
                MeasureProfiler.SaveData();
            }
        }

        lastRunning = Manager.Running;
    }
}
#endif