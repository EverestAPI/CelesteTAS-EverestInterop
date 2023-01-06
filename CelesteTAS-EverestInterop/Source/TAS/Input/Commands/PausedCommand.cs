using System;
using System.Collections.Generic;
using Celeste;
using Monocle;
using TAS.Entities;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PausedCommand {
    [Load]
    private static void Load() {
        On.Monocle.Scene.BeforeUpdate += DoublePauses;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Scene.BeforeUpdate -= DoublePauses;
    }

    private static bool simulatePauses;
    private static bool pauseOnCurrentFrame;

    private static void DoublePauses(On.Monocle.Scene.orig_BeforeUpdate orig, Monocle.Scene self) {
        orig(self);
        if (simulatePauses) {
            pauseOnCurrentFrame = !pauseOnCurrentFrame;
            if (pauseOnCurrentFrame) {
                orig(self);
                if (Engine.Scene is Level level) {
                    long ticks = TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks * 11;
                    SaveData.Instance.AddTime(level.Session.Area, ticks);
                    level.Session.Time += ticks;
                }
            }
        }
    }

    // "Paused"
    [TasCommand("Paused", LegalInMainGame = false)]
    private static void Paused() {
        simulatePauses = true;
    }

    // "EndPaused"
    [TasCommand("EndPaused", LegalInMainGame = false)]
    private static void EndPaused() {
        simulatePauses = false;
    }

}