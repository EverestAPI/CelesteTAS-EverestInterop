using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop {
    public static class Core {
        public delegate void DGameUpdate(Game self, GameTime gameTime);

        // The fields we want to access from Celeste-Addons
        public static bool SkipBaseUpdate;
        public static bool InUpdate;

        public static Detour HRunThreadWithLogging;

        public static Detour HGameUpdate;

        public static DGameUpdate OrigGameUpdate;

        public static Action PreviousGameLoop;

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            // Relink RunThreadWithLogging to Celeste.RunThread.RunThreadWithLogging because reflection invoke is slow.
            HRunThreadWithLogging = new Detour(
                typeof(Core).GetMethod("RunThreadWithLogging"),
                typeof(RunThread).GetMethod("RunThreadWithLogging", BindingFlags.NonPublic | BindingFlags.Static)
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
                typeof(Game).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                typeof(Core).GetMethod("Game_Update")
            )).GenerateTrampoline<DGameUpdate>();

            // Forced: Allow "rendering" entities without actually rendering them.
            On.Monocle.Entity.Render += Entity_Render;

            On.Monocle.Scene.AfterUpdate += Scene_AfterUpdate;
        }

        public static void Unload() {
            HRunThreadWithLogging.Dispose();
            On.Monocle.Engine.Update -= Engine_Update;
            On.Monocle.MInput.Update -= MInput_Update;
            On.Celeste.RunThread.Start -= RunThread_Start;
            HGameUpdate.Dispose();
            On.Monocle.Entity.Render -= Entity_Render;
            On.Monocle.Scene.AfterUpdate -= Scene_AfterUpdate;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RunThreadWithLogging(Action method) {
            // This gets relinked to Celeste.RunThread.RunThreadWithLogging
            throw new Exception("Failed relinking RunThreadWithLogging!");
        }

        public static void Engine_Update(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
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
                if (Engine.Scene is Level level && UserIO.Saving && !SaveData.Instance.Areas[level.Session.Area.ID].Modes[0].Completed) {
                    if (Engine.Scene.Entities.FindFirst<EventTrigger>() != null
                        || Engine.Scene.Entities.FindFirst<NPC>() != null
                        || Engine.Scene.Entities.FindFirst<FlingBirdIntro>() != null) {
                        skipBaseUpdate = false;
                        loops = 1;
                    }
                }
            }

            SkipBaseUpdate = false;
            InUpdate = false;

            if (CelesteTasModule.UnixRtcEnabled && Manager.CurrentStatus != null) {
                StreamWriter writer = CelesteTasModule.Instance.UnixRtcStreamOut;
                try {
                    writer.Write(PlayerInfo.Status.Replace('\n', '~'));
                    writer.Write('%');
                    writer.Write(Manager.CurrentStatus.Replace('\n', '~'));
                    writer.Write('%');
                    if (Engine.Scene is Level level) {
                        writer.Write(level.Session.LevelData.Name);
                    }

                    writer.WriteLine();
                    writer.FlushAsync();
                } catch {
                    // ignored
                }
            }

            if (skipBaseUpdate) {
                OrigGameUpdate(self, gameTime);
            }
        }

        public static void MInput_Update(On.Monocle.MInput.orig_Update orig) {
            if (!Settings.Enabled) {
                orig();
                return;
            }

            if (!Manager.Running || Manager.Recording) {
                orig();
            }

            if (Manager.Running || Engine.Scene?.Entities.Any(entity => entity.GetType().IsSubclassOf(typeof(KeyboardConfigUI))) != true) {
                Manager.Update();
            }

            // Hacky, but this works just good enough.
            // The original code executes base.Update(); return; instead.
            if ((Manager.State & State.FrameStep) == State.FrameStep) {
                PreviousGameLoop = Engine.OverloadGameLoop;
                Engine.OverloadGameLoop = FrameStepGameLoop;
            }
        }

        public static void Game_Update(Game self, GameTime gameTime) {
            if (Settings.Enabled && SkipBaseUpdate) {
                return;
            }

            OrigGameUpdate(self, gameTime);
        }

        public static void FrameStepGameLoop() {
            Engine.OverloadGameLoop = PreviousGameLoop;
        }

        public static void RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
            if (Manager.Running) {
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

        private static void Scene_AfterUpdate(On.Monocle.Scene.orig_AfterUpdate orig, Scene self) {
            orig(self);
            PlayerInfo.Update();
        }
    }
}