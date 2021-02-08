using Celeste;
using Monocle;

namespace TAS.EverestInterop {
public static class FastForwardBoost {
    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    private static bool SkipUpdate => Manager.state == State.Enable
                                      && !Settings.FastForwardCallBase
                                      && Manager.FrameLoops >= Settings.FastForwardThreshold;

    public static void Load() {
        On.Celeste.BackdropRenderer.Update += BackdropRendererOnUpdate;
        On.Celeste.ReflectionTentacles.UpdateVertices += ReflectionTentaclesOnUpdateVertices;
    }

    public static void Unload() {
        On.Celeste.BackdropRenderer.Update -= BackdropRendererOnUpdate;
        On.Celeste.ReflectionTentacles.UpdateVertices -= ReflectionTentaclesOnUpdateVertices;
    }

    private static void BackdropRendererOnUpdate(On.Celeste.BackdropRenderer.orig_Update orig, BackdropRenderer self, Scene scene) {
        if (SkipUpdate && Engine.FrameCounter % 1000 > 0) {
            return;
        }

        orig(self, scene);
    }

    private static void ReflectionTentaclesOnUpdateVertices(On.Celeste.ReflectionTentacles.orig_UpdateVertices orig, ReflectionTentacles self) {
        if (SkipUpdate || Settings.SimplifiedGraphics) {
            return;
        }

        orig(self);
    }
}
}