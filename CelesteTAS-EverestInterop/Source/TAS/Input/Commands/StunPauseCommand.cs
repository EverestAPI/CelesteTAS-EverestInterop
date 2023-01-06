using System;
using System.Collections.Generic;
using Celeste;
using MonoMod.RuntimeDetour;
using On.Monocle;
using TAS.Module;
using TAS.Utils;
using Engine = Monocle.Engine;

namespace TAS.Input.Commands;

public static class StunPauseCommand {
    private static readonly float unpauseTime = typeof(Level).GetFieldInfo("unpauseTimer") != null ? 0.15f : 0f;
    public static bool SimulatePauses;
    public static bool PauseOnCurrentFrame;

    // hook after CycleHitboxColor.Load, so that the grouping color does not change
    [Initialize]
    private static void Initialize() {
        using (new DetourContext {After = new List<string> {"*"}}) {
            Scene.BeforeUpdate += DoublePauses;
        }
    }

    [Unload]
    private static void Unload() {
        Scene.BeforeUpdate -= DoublePauses;
    }

    private static void DoublePauses(Scene.orig_BeforeUpdate orig, Monocle.Scene self) {
        orig(self);

        if (SimulatePauses && Engine.Scene is Level {Paused: false} level) {
            PauseOnCurrentFrame = !PauseOnCurrentFrame;
            if (PauseOnCurrentFrame) {
                orig(self);
                UpdateTime(level);
            }
        }
    }

    private static void UpdateTime(Level level) {
        if (level.InCredits || level.Session.Area.ID == 8 || level.TimerStopped) {
            return;
        }

        long ticks = TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks * ((int) Math.Ceiling(unpauseTime / Engine.RawDeltaTime) + 2);
        SaveData.Instance.AddTime(level.Session.Area, ticks);

        if (!level.Completed && level.TimerStarted) {
            level.Session.Time += ticks;
        }
    }

    [TasCommand("StunPause", LegalInMainGame = false)]
    private static void StunPause() {
        if (!SimulatePauses) {
            SimulatePauses = true;
            PauseOnCurrentFrame = false;
        }
    }

    [TasCommand("EndStunPause", LegalInMainGame = false)]
    private static void EndStunPause() {
        SimulatePauses = false;
        PauseOnCurrentFrame = false;
    }

    [DisableRun]
    private static void DisableRun() {
        SimulatePauses = false;
        PauseOnCurrentFrame = false;
    }
}