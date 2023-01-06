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
    private static readonly GetDelegate<Level, float> unpauseTimer = FastReflection.CreateGetDelegate<Level, float>("unpauseTimer");
    private static readonly float unpauseTime = unpauseTimer != null ? 0.15f : 0f;
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

        if (SimulatePauses && self is Level level) {
            if (CanPause(level)) {
                PauseOnCurrentFrame = !PauseOnCurrentFrame;
                if (PauseOnCurrentFrame) {
                    orig(self);
                    UpdateTime(level);
                }
            } else {
                PauseOnCurrentFrame = false;
            }
        }
    }

    private static bool CanPause(Level level) {
        if (unpauseTimer == null) {
            return level.CanPause;
        } else {
            return level.CanPause && unpauseTimer(level) <= 0f;
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

    [DisableRun]
    [TasCommand("EndStunPause", LegalInMainGame = false)]
    private static void EndStunPause() {
        SimulatePauses = false;
        PauseOnCurrentFrame = false;
    }
}