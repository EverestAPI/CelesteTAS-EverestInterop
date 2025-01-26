using System;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.SyncCheck;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS.Playback;

/// Main hooks for allowing for TAS playback
internal static class Core {
    [Load]
    private static void Load() {
        using (new DetourConfigContext(new DetourConfig("CelesteTAS", before: ["*"])).Use()) {
            On.Celeste.Celeste.Update += On_Celeste_Update;
            IL.Monocle.Engine.Update += IL_Engine_Update;

            typeof(GameInput)
                .GetMethod(nameof(GameInput.UpdateGrab))!
                .SkipMethod(IsPaused);

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
    private static DateTime lastMetaUpdate = DateTime.UtcNow;

    private static void On_Celeste_Update(On.Celeste.Celeste.orig_Update orig, Celeste.Celeste self, GameTime gameTime) {
        if (!TasSettings.Enabled || !Manager.Running) {
            Manager.UpdateMeta();

            try {
                orig(self, gameTime);
            } catch (Exception ex) {
                if (!SyncChecker.Active) {
                    throw; // Let Everest handle this
                }

                SyncChecker.ReportCrash(ex.ToString());
                Manager.DisableRun();
                return;
            }

            return;
        }

        elapsedTime += Manager.PlaybackSpeed * Engine.RawDeltaTime;

        Manager.UpdateMeta();
        lastMetaUpdate = DateTime.UtcNow;

        while (elapsedTime >= Engine.RawDeltaTime) {
            try {
                orig(self, gameTime);
            } catch (Exception ex) {
                if (!SyncChecker.Active) {
                    throw; // Let Everest handle this
                }

                SyncChecker.ReportCrash(ex.ToString());
                Manager.DisableRun();
                return;
            }

            elapsedTime -= Engine.RawDeltaTime;

            // Call UpdateMeta every real-time frame
            var now = DateTime.UtcNow;
            if ((now - lastMetaUpdate).TotalSeconds > Engine.RawDeltaTime) {
                // We need to manually poll FNA events, since we don't return to the FNA game-loop while fast-forwarding
                var game = Engine.Instance;
                FNAPlatform.PollEvents(game, ref game.currentAdapter, game.textInputControlDown, ref game.textInputSuppress);

                Manager.UpdateMeta();
                lastMetaUpdate = now;
            }
        }

        if (!TasSettings.HideFreezeFrames) {
            return;
        }

        // Advance through freeze frames
        while (Engine.FreezeTimer > 0.0f && !Manager.Controller.Break) {
            try {
                orig(self, gameTime);
            } catch (Exception ex) {
                if (!SyncChecker.Active) {
                    throw; // Let Everest handle this
                }

                SyncChecker.ReportCrash(ex.ToString());
                Manager.DisableRun();
                return;
            }
        }
    }

    private static void IL_Engine_Update(ILContext il) {
        var cur = new ILCursor(il);

        cur.GotoNext(MoveType.After, ins => ins.MatchCall(typeof(MInput), nameof(MInput.Update)));

        // Prevent further execution while the TAS is paused
        var label = cur.DefineLabel();
        cur.EmitDelegate(IsPaused);
        cur.EmitBrfalse(label);
        cur.EmitRet();
        cur.MarkLabel(label);
    }

    private static bool IsPaused() => Manager.CurrState == Manager.State.Paused && !Manager.IsLoading();

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
