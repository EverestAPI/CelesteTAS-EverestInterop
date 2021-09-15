using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using StudioCommunication;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class Core {
        // The fields we want to access from Celeste-Addons
        private static bool SkipBaseUpdate;
        private static bool InUpdate;

        private static Detour HRunThreadWithLogging;

        private static Detour HGameUpdate;

        private static DGameUpdate OrigGameUpdate;

        private static Action PreviousGameLoop;
        private static readonly Lazy<bool> CantPauseWhileSaving = new(() => Everest.Version < new Version(1, 2865));

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [Load]
        private static void Load() {
            // Relink RunThreadWithLogging to Celeste.RunThread.RunThreadWithLogging because reflection invoke is slow.
            HRunThreadWithLogging = new Detour(
                typeof(Core).GetMethodInfo("RunThreadWithLogging"),
                typeof(RunThread).GetMethodInfo("RunThreadWithLogging")
            );

            // The original mod adds a few lines of code into Monocle.Engine::Update.
            On.Monocle.Engine.Update += Engine_Update;

            // The original mod makes the MInput.Update call conditional and invokes UpdateInputs afterwards.
            On.Monocle.MInput.Update += MInput_Update;

            // The original mod makes RunThread.Start run synchronously.
            On.Celeste.RunThread.Start += RunThread_Start;

            // The original mod makes the base.Update call conditional.
            // We need to use Detour for two reasons:
            // 1. Expose the trampoline to be used for the base.Update call in MInput_Update
            // 2. XNA Framework methods would require a separate MMHOOK .dll
            OrigGameUpdate = (HGameUpdate = new Detour(
                typeof(Game).GetMethodInfo("Update"),
                typeof(Core).GetMethodInfo("Game_Update")
            )).GenerateTrampoline<DGameUpdate>();

            // Forced: Allow "rendering" entities without actually rendering them.
            On.Monocle.Entity.Render += Entity_Render;
        }

        [Unload]
        private static void Unload() {
            HRunThreadWithLogging.Dispose();
            On.Monocle.Engine.Update -= Engine_Update;
            On.Monocle.MInput.Update -= MInput_Update;
            On.Celeste.RunThread.Start -= RunThread_Start;
            HGameUpdate.Dispose();
            On.Monocle.Entity.Render -= Entity_Render;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunThreadWithLogging(Action method) {
            // This gets relinked to Celeste.RunThread.RunThreadWithLogging
            throw new Exception("Failed relinking RunThreadWithLogging!");
        }

        private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
            SkipBaseUpdate = false;
            InUpdate = false;

            if (!Settings.Enabled) {
                orig(self, gameTime);
                return;
            }

            // The original patch doesn't store FrameLoops in a local variable, but it's only updated in UpdateInputs anyway.
            int loops = Manager.FrameLoops;
            bool skipBaseUpdate = !Settings.FastForwardCallBase && loops >= Settings.FastForwardThreshold;

            SkipBaseUpdate = skipBaseUpdate;
            InUpdate = true;

            for (int i = 0; i < loops; i++) {
                // Anything happening early on runs in the MInput.Update hook.
                orig(self, gameTime);

                // Badeline does some dirty stuff in Render.
                if (i < loops - 1) {
                    Engine.Scene?.Tracker.GetEntity<FinalBoss>()?.Render();
                }

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
                GameInfo.Update(true);
            }
        }

        private static void MInput_Update(On.Monocle.MInput.orig_Update orig) {
            if (!Settings.Enabled) {
                orig();
                return;
            }

            if ((!Manager.Running || Manager.Recording) && Engine.Instance.IsActive) {
                orig();
            }

            Manager.Update();

            // Hacky, but this works just good enough.
            // The original code executes base.Update(); return; instead.
            if ((Manager.State & State.FrameStep) == State.FrameStep) {
                PreviousGameLoop = Engine.OverloadGameLoop;
                Engine.OverloadGameLoop = FrameStepGameLoop;
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static void Game_Update(Game self, GameTime gameTime) {
            if (Settings.Enabled && SkipBaseUpdate) {
                return;
            }

            OrigGameUpdate(self, gameTime);
        }

        private static void FrameStepGameLoop() {
            Engine.OverloadGameLoop = PreviousGameLoop;
        }

        private static void RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
            if (Manager.Running && (CantPauseWhileSaving.Value || name != "USER_IO" && name != "MOD_IO")) {
                RunThreadWithLogging(method);
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

        private delegate void DGameUpdate(Game self, GameTime gameTime);
    }
}