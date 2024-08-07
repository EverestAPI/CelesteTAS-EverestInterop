using System;
using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS.EverestInterop;

public static class Core {
    [Load]
    private static void Load() {
        using (new DetourContext {After = new List<string> {"*"}}) {
            On.Celeste.Celeste.Update += On_Celeste_Update;
            IL.Monocle.Engine.Update += IL_Engine_Update;

            if (typeof(GameInput).GetMethod(nameof(GameInput.UpdateGrab)) is { } updateGrabMethod) {
                HookHelper.SkipMethod(typeof(Manager), nameof(Manager.IsPaused), updateGrabMethod);
            }

            // The original mod makes the MInput.Update call conditional and invokes UpdateInputs afterwards.
            On.Monocle.MInput.Update += On_MInput_Update;
            IL.Monocle.MInput.Update += IL_MInput_Update;

            // The original mod makes RunThread.Start run synchronously.
            On.Celeste.RunThread.Start += On_RunThread_Start;
        }
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.Update -= On_Celeste_Update;
        IL.Monocle.Engine.Update -= IL_Engine_Update;

        On.Monocle.MInput.Update -= On_MInput_Update;
        IL.Monocle.MInput.Update -= IL_MInput_Update;

        On.Celeste.RunThread.Start -= On_RunThread_Start;
    }

    private static float elapsedTime = 0.0f;

    private static void On_Celeste_Update(On.Celeste.Celeste.orig_Update orig, Celeste.Celeste self, GameTime gameTime) {
        Manager.UpdateHotkeys();

        if (!TasSettings.Enabled || !Manager.Running) {
            orig(self, gameTime);
            return;
        }

        elapsedTime += Manager.PlaybackSpeed * Engine.RawDeltaTime;
        while (elapsedTime >= Engine.RawDeltaTime) {
            orig(self, gameTime);
            elapsedTime -= Engine.RawDeltaTime;
        }

        if (TasSettings.HideFreezeFrames) {
            while (Engine.FreezeTimer > 0.0f && !Manager.Controller.Break) {
                orig(self, gameTime);
            }
        }
    }

    private static void IL_Engine_Update(ILContext il) {
        var cur = new ILCursor(il);

        if (cur.TryGotoNext(MoveType.After, instr => instr.MatchCall(typeof(MInput), nameof(MInput.Update)))) {
            var label = cur.DefineLabel();

            // Prevent further execution while the TAS is paused
            cur.EmitDelegate(Manager.IsPaused);
            cur.Emit(OpCodes.Brfalse, label);
            cur.Emit(OpCodes.Ret);
            cur.MarkLabel(label);
        }
    }

    private static void On_MInput_Update(On.Monocle.MInput.orig_Update orig) {
        if (!TasSettings.Enabled) {
            orig();
            return;
        }

        if (!Manager.Running) {
            orig();
        }

        Manager.Update();
    }

    // Update controllers, even if the game isn't focused
    private static void IL_MInput_Update(ILContext il) {
        var cur = new ILCursor(il) {
            Index = il.Instrs.Count - 1,
        };

        if (cur.TryGotoPrev(MoveType.After, i => i.MatchCallvirt<MInput.MouseData>("UpdateNull"))) {
            cur.EmitDelegate(UpdateGamePads);
        }

        // Skip the orig GamePads[j].UpdateNull();
        if (cur.TryGotoNext(MoveType.After, i => i.MatchLdcI4(0))) {
            cur.Emit(OpCodes.Ldc_I4_4).Emit(OpCodes.Add);
        }

        static void UpdateGamePads() {
            for (int i = 0; i < 4; i++) {
                if (MInput.Active) {
                    MInput.GamePads[i].Update();
                } else {
                    MInput.GamePads[i].UpdateNull();
                }
            }
        }
    }

    private static void On_RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
        if (Manager.Running && name != "USER_IO" && name != "MOD_IO") {
            RunThread.RunThreadWithLogging(method);
            return;
        }

        orig(method, name, highPriority);
    }
}