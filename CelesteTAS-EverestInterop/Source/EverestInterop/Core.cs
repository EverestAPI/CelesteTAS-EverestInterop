using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS.EverestInterop;

public static class Core {
    // The fields we want to access from Celeste-Addons
    private static bool SkipBaseUpdate;
    private static bool InUpdate;

    private static Detour HGameUpdate;
    private static DGameUpdate OrigGameUpdate;

    private static Action PreviousGameLoop;

    // https://github.com/EverestAPI/Everest/commit/b2a6f8e7c41ddafac4e6fde0e43a09ce1ac4f17e
    private static readonly Lazy<bool> CantPauseWhileSaving = new(() => Everest.Version < new Version(1, 2865));
    private static readonly bool updateGrab = typeof(GameInput).GetMethod("UpdateGrab") != null;

    [Load]
    private static void Load() {
        // The original mod makes the base.Update call conditional.
        // We need to use Detour for two reasons:
        // 1. Expose the trampoline to be used for the base.Update call in MInput_Update
        // 2. XNA Framework methods would require a separate MMHOOK .dll
        OrigGameUpdate = (HGameUpdate = new Detour(
            typeof(Game).GetMethodInfo("Update"),
            typeof(Core).GetMethodInfo("Game_Update")
        )).GenerateTrampoline<DGameUpdate>();

        using (new DetourContext {After = new List<string> {"*"}}) {
            // The original mod adds a few lines of code into Monocle.Engine::Update.
            On.Monocle.Engine.Update += Engine_Update;

            // The original mod makes the MInput.Update call conditional and invokes UpdateInputs afterwards.
            On.Monocle.MInput.Update += MInput_Update;
            IL.Monocle.MInput.Update += MInputOnUpdate;

            // The original mod makes RunThread.Start run synchronously.
            On.Celeste.RunThread.Start += RunThread_Start;

            // Forced: Allow "rendering" entities without actually rendering them.
            On.Monocle.Entity.Render += Entity_Render;

            if (updateGrab) {
                HookHelper.SkipMethod(typeof(Core), nameof(IgnoreOrigUpdateGrab), typeof(GameInput).GetMethod("UpdateGrab"));
            }
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= Engine_Update;
        On.Monocle.MInput.Update -= MInput_Update;
        IL.Monocle.MInput.Update -= MInputOnUpdate;
        On.Celeste.RunThread.Start -= RunThread_Start;
        HGameUpdate.Dispose();
        On.Monocle.Entity.Render -= Entity_Render;
    }

    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        SkipBaseUpdate = false;
        InUpdate = false;

        if (!TasSettings.Enabled || !Manager.Running) {
            orig(self, gameTime);
            return;
        }

        if (Manager.SlowForwarding) {
            orig(self, gameTime);
            TryUpdateGrab();
            return;
        }

        // The original patch doesn't store FrameLoops in a local variable, but it's only updated in UpdateInputs anyway.
        int loops = (int) Manager.FrameLoops;
        bool skipBaseUpdate = loops >= 2;

        SkipBaseUpdate = skipBaseUpdate;
        InUpdate = true;

        for (int i = 0; i < loops; i++) {
            // Anything happening early on runs in the MInput.Update hook.
            orig(self, gameTime);
            TryUpdateGrab();

            // Autosaving prevents opening the menu to skip cutscenes during fast forward.
            if (CantPauseWhileSaving.Value && Engine.Scene is Level level && UserIO.Saving
                && level.Entities.Any(entity => entity is EventTrigger or NPC or FlingBirdIntro)
               ) {
                skipBaseUpdate = false;
                loops = 1;
            }
        }

        SkipBaseUpdate = false;
        InUpdate = false;

        if (skipBaseUpdate) {
            OrigGameUpdate(self, gameTime);
        }
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

    // ReSharper disable once UnusedMember.Local
    private static void Game_Update(Game self, GameTime gameTime) {
        if (TasSettings.Enabled && SkipBaseUpdate) {
            return;
        }

        OrigGameUpdate(self, gameTime);
    }

    private static void FrameStepGameLoop() {
        Engine.OverloadGameLoop = PreviousGameLoop;
    }

    private static void RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
        if (Manager.Running && (CantPauseWhileSaving.Value || name != "USER_IO" && name != "MOD_IO")) {
            RunThread.RunThreadWithLogging(method);
            return;
        }

        orig(method, name, highPriority);
    }

    private static void Entity_Render(On.Monocle.Entity.orig_Render orig, Entity self) {
        if (InUpdate) {
            return;
        }

        orig(self);
    }

    private static void TryUpdateGrab() {
        if (!updateGrab || Manager.SkipFrame) {
            return;
        }

        UpdateGrab();
    }

    private static void UpdateGrab() {
        // this method needs to be isolated for compatibility with Celeste v1.3+
        if (Settings.Instance.GrabMode == GrabModes.Toggle && GameInput.Grab.Pressed) {
            GameInput.grabToggle = !GameInput.grabToggle;
        }
    }

    private static bool IgnoreOrigUpdateGrab() {
        return Manager.Running;
    }

    private delegate void DGameUpdate(Game self, GameTime gameTime);
}