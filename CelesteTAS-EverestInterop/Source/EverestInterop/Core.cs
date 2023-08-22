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
    private static bool InUpdate;
    private static Action PreviousGameLoop;

    [Load]
    private static void Load() {
        using (new DetourContext {After = new List<string> {"*"}}) {
            // The original mod adds a few lines of code into Monocle.Engine::Update.
            On.Celeste.Celeste.Update += Celeste_Update;

            // The original mod makes the MInput.Update call conditional and invokes UpdateInputs afterwards.
            On.Monocle.MInput.Update += MInput_Update;
            IL.Monocle.MInput.Update += MInputOnUpdate;

            // The original mod makes RunThread.Start run synchronously.
            On.Celeste.RunThread.Start += RunThread_Start;

            // Forced: Allow "rendering" entities without actually rendering them.
            IL.Monocle.Entity.Render += SkipRenderMethod;
        }
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.Update -= Celeste_Update;
        On.Monocle.MInput.Update -= MInput_Update;
        IL.Monocle.MInput.Update -= MInputOnUpdate;
        On.Celeste.RunThread.Start -= RunThread_Start;
        IL.Monocle.Entity.Render -= SkipRenderMethod;
    }

    private static void Celeste_Update(On.Celeste.Celeste.orig_Update orig, Celeste.Celeste self, GameTime gameTime) {
        InUpdate = false;

        if (!TasSettings.Enabled || !Manager.Running) {
            orig(self, gameTime);
            return;
        }

        InUpdate = true;

        // The original patch doesn't store FrameLoops in a local variable, but it's only updated in UpdateInputs anyway.
        int loops = Manager.SlowForwarding ? 1 : (int) Manager.FrameLoops;
        for (int i = 0; i < loops; i++) {
            float oldFreezeTimer = Engine.FreezeTimer;

            // Anything happening early on runs in the MInput.Update hook.
            orig(self, gameTime);
            Manager.AdvanceThroughHiddenFrame = false;

            if (TasSettings.HideFreezeFrames && oldFreezeTimer > 0f && oldFreezeTimer > Engine.FreezeTimer) {
                Manager.AdvanceThroughHiddenFrame = true;
                loops += 1;
            } else if (RecordingCommand.StopFastForward) {
                break;
            }
        }

        InUpdate = false;
    }

    private static void MInput_Update(On.Monocle.MInput.orig_Update orig) {
        if (!TasSettings.Enabled) {
            orig();
            return;
        }

        if (!Manager.Running) {
            orig();
        }

        Manager.Update();

        // Hacky, but this works just good enough.
        // The original code executes base.Update(); return; instead.
        if (Manager.SkipFrame && !Manager.IsLoading()) {
            PreviousGameLoop = Engine.OverloadGameLoop;
            Engine.OverloadGameLoop = FrameStepGameLoop;
        }
    }

    // update controller even the game is lose focus
    private static void MInputOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        ilCursor.Goto(il.Instrs.Count - 1);

        if (ilCursor.TryGotoPrev(MoveType.After, i => i.MatchCallvirt<MInput.MouseData>("UpdateNull"))) {
            ilCursor.EmitDelegate(UpdateGamePads);
        }

        // skip the orig GamePads[j].UpdateNull();
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(0))) {
            ilCursor.Emit(OpCodes.Ldc_I4_4).Emit(OpCodes.Add);
        }
    }

    private static void UpdateGamePads() {
        for (int i = 0; i < 4; i++) {
            if (MInput.Active) {
                MInput.GamePads[i].Update();
            } else {
                MInput.GamePads[i].UpdateNull();
            }
        }
    }

    private static void FrameStepGameLoop() {
        Engine.OverloadGameLoop = PreviousGameLoop;
    }

    private static void RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
        if (Manager.Running && name != "USER_IO" && name != "MOD_IO") {
            RunThread.RunThreadWithLogging(method);
            return;
        }

        orig(method, name, highPriority);
    }

    private static void SkipRenderMethod(ILContext il) {
        ILCursor ilCursor = new(il);
        ILLabel startLabel = ilCursor.DefineLabel();
        ilCursor.Emit(OpCodes.Ldsfld, typeof(Core).GetFieldInfo(nameof(InUpdate)))
            .Emit(OpCodes.Brfalse, startLabel)
            .Emit(OpCodes.Ret)
            .MarkLabel(startLabel);
    }
}