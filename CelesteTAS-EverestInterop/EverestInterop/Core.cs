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
class Core {
    public delegate void d_Game_Update(Game self, GameTime gameTime);

    public static Core instance;

    // The fields we want to access from Celeste-Addons
    public static bool SkipBaseUpdate;
    public static bool InUpdate;

    public static Detour h_RunThreadWithLogging;

    public static Detour h_Game_Update;

    public static d_Game_Update orig_Game_Update;

    public static Action PreviousGameLoop;

    public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public void Load() {
        // Relink RunThreadWithLogging to Celeste.RunThread.RunThreadWithLogging because reflection invoke is slow.
        h_RunThreadWithLogging = new Detour(
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
        orig_Game_Update = (h_Game_Update = new Detour(
            typeof(Game).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            typeof(Core).GetMethod("Game_Update")
        )).GenerateTrampoline<d_Game_Update>();

        // Forced: Allow "rendering" entities without actually rendering them.
        On.Monocle.Entity.Render += Entity_Render;

        On.Monocle.Scene.AfterUpdate += Scene_AfterUpdate;
    }

    public void Unload() {
        h_RunThreadWithLogging.Dispose();
        On.Monocle.Engine.Update -= Engine_Update;
        On.Monocle.MInput.Update -= MInput_Update;
        On.Celeste.RunThread.Start -= RunThread_Start;
        h_Game_Update.Dispose();
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

        if (CelesteTASModule.UnixRTCEnabled && Manager.CurrentStatus != null) {
            StreamWriter writer = CelesteTASModule.Instance.UnixRTCStreamOut;
            try {
                writer.Write(Manager.PlayerStatus.Replace('\n', '~'));
                writer.Write('%');
                writer.Write(Manager.CurrentStatus.Replace('\n', '~'));
                writer.Write('%');
                if (Engine.Scene is Level level) {
                    writer.Write(level.Session.LevelData.Name);
                }

                writer.WriteLine();
                writer.FlushAsync();
            } catch { }
        }

        if (skipBaseUpdate) {
            orig_Game_Update(self, gameTime);
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
        if ((Manager.state & State.FrameStep) == State.FrameStep) {
            PreviousGameLoop = Engine.OverloadGameLoop;
            Engine.OverloadGameLoop = FrameStepGameLoop;
        }
    }

    public static void Game_Update(Game self, GameTime gameTime) {
        if (Settings.Enabled && SkipBaseUpdate) {
            return;
        }

        orig_Game_Update(self, gameTime);
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

    private void Entity_Render(On.Monocle.Entity.orig_Render orig, Entity self) {
        if (InUpdate) {
            return;
        }

        orig(self);
    }

    private void Scene_AfterUpdate(On.Monocle.Scene.orig_AfterUpdate orig, Scene self) {
        orig(self);
        Manager.UpdatePlayerInfo();
    }
}
}