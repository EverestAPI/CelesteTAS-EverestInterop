using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FloatingDebris = On.Celeste.FloatingDebris;
using MoonCreature = On.Celeste.MoonCreature;
using SoundSource = On.Celeste.SoundSource;

namespace TAS.EverestInterop {
    public class CelesteTASModule : EverestModule {

        public static CelesteTASModule Instance;

        public override Type SettingsType => typeof(CelesteTASModuleSettings);
        public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings) Instance?._Settings;

        public VirtualButton ButtonHitboxes;
        public VirtualButton ButtonGraphics;
        public VirtualButton ButtonCamera;

        private Vector2? SavedCamera;

        // The fields we want to access from Celeste-Addons
        public static bool SkipBaseUpdate;
        public static bool InUpdate;

        public CelesteTASModule() {
            Instance = this;
        }

        public override void Load() {

            // Relink UpdateInputs to TAS.Manager.UpdateInputs because reflection invoke is slow.
            h_UpdateInputs = new Detour(
                typeof(CelesteTASModule).GetMethod("UpdateInputs"),
                typeof(Manager).GetMethod("UpdateInputs")
            );

            // Relink RunThreadWithLogging to Celeste.RunThread.RunThreadWithLogging because reflection invoke is slow.
            h_RunThreadWithLogging = new Detour(
                typeof(CelesteTASModule).GetMethod("RunThreadWithLogging"),
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
                typeof(CelesteTASModule).GetMethod("Game_Update")
            )).GenerateTrampoline<d_Game_Update>();

            // Optional: Disable achievements, stats and terminal.
            On.Celeste.Achievements.Register += Achievements_Register;
            On.Celeste.Stats.Increment += Stats_Increment;

            // Forced: Add more positions to top-left positioning helper.
            IL.Monocle.Commands.Render += Commands_Render;

            // Optional: Show the pathfinder.
            IL.Celeste.Level.Render += Level_Render;
            IL.Celeste.Pathfinder.Render += Pathfinder_Render;

            // Forced: Allow "rendering" entities without actually rendering them.
            On.Monocle.Entity.Render += Entity_Render;

            // Any additional hooks.
            Everest.Events.Input.OnInitialize += OnInputInitialize;
            Everest.Events.Input.OnDeregister += OnInputDeregister;

            // Optional: Various graphical simplifications to cut down on visual noise.
            On.Celeste.LightingRenderer.Render += LightingRenderer_Render;
            On.Monocle.Particle.Render += Particle_Render;
            On.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.ctor_Vector2_bool_CrystalColor += CrystalStaticSpinner_ctor;
            On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave += LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp += DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;

            // Hide distortion when showing hitboxes
            On.Celeste.Distort.Render += Distort_Render;
            
            // Hide SoundSource when showing hitboxes
            On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;

            // Show pufferfish explosion radius
            On.Celeste.Puffer.Render += Puffer_Render;
            
            // Stop updating tentacles texture when fast forward
            On.Celeste.ReflectionTentacles.UpdateVertices += ReflectionTentaclesOnUpdateVertices;

            // Optional: Center the camera
            On.Celeste.Level.BeforeRender += Level_BeforeRender;
            On.Celeste.Level.AfterRender += Level_AfterRender;
        }

        private void Level_BeforeRender(On.Celeste.Level.orig_BeforeRender orig, Level self) {
            orig.Invoke(self);
            if (Settings.CenterCameraMayCauseSoftlocks) {
                Player player = self.Tracker.GetEntity<Player>();
                if (player != null) {
                    SavedCamera = self.Camera.Position;
                    self.Camera.Position = player.Position;
                    self.Camera.CenterOrigin();
                }
                else
                    SavedCamera = null;
            }
        }

        private void Level_AfterRender(On.Celeste.Level.orig_AfterRender orig, Level self) {
            if (SavedCamera != null)
                self.Camera.Position = (Vector2)SavedCamera;
            orig.Invoke(self);
        }

        public override void Unload() {
            h_UpdateInputs.Dispose();
            h_RunThreadWithLogging.Dispose();
            On.Monocle.Engine.Update -= Engine_Update;
            On.Monocle.MInput.Update -= MInput_Update;
            On.Celeste.RunThread.Start -= RunThread_Start;
            h_Game_Update.Dispose();
            On.Celeste.Achievements.Register -= Achievements_Register;
            On.Celeste.Stats.Increment -= Stats_Increment;
            On.Monocle.Entity.Render -= Entity_Render;

            Everest.Events.Input.OnInitialize -= OnInputInitialize;
            Everest.Events.Input.OnDeregister -= OnInputDeregister;

            On.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
            On.Monocle.Particle.Render -= Particle_Render;
            On.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.ctor_Vector2_bool_CrystalColor -= CrystalStaticSpinner_ctor;
            On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave -= LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp -= DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
            On.Celeste.Distort.Render -= Distort_Render;
            On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
            On.Celeste.Puffer.Render -= Puffer_Render;
            On.Celeste.ReflectionTentacles.UpdateVertices -= ReflectionTentaclesOnUpdateVertices;
            On.Celeste.Level.BeforeRender -= Level_BeforeRender;
            On.Celeste.Level.AfterRender -= Level_AfterRender;
        }

        public void OnInputInitialize() {
            ButtonHitboxes = new VirtualButton();
            AddButtonsTo(ButtonHitboxes, Settings.ButtonHitboxes);
            AddKeysTo(ButtonHitboxes, Settings.KeyHitboxes);

            ButtonGraphics = new VirtualButton();
            AddButtonsTo(ButtonGraphics, Settings.ButtonGraphics);
            AddKeysTo(ButtonGraphics, Settings.KeyGraphics);

            ButtonCamera = new VirtualButton();
            AddButtonsTo(ButtonCamera, Settings.ButtonCamera);
            AddKeysTo(ButtonCamera, Settings.KeyCamera);

            if (Settings.KeyStart.Count == 0) {
                Settings.KeyStart = new List<Keys> { Keys.RightControl, Keys.OemOpenBrackets };
                Settings.KeyFastForward = new List<Keys> { Keys.RightControl, Keys.RightShift };
                Settings.KeyFrameAdvance = new List<Keys> { Keys.OemOpenBrackets };
                Settings.KeyPause = new List<Keys> { Keys.OemCloseBrackets };
            }
        }

        public void OnInputDeregister() {
            ButtonHitboxes?.Deregister();
            ButtonGraphics?.Deregister();
        }

        public static void AddButtonsTo(VirtualButton vbtn, List<Buttons> buttons) {
            if (buttons == null)
                return;
            foreach (Buttons button in buttons) {
                if (button == Buttons.LeftTrigger) {
                    vbtn.Nodes.Add(new VirtualButton.PadLeftTrigger(Input.Gamepad, 0.25f));
                } else if (button == Buttons.RightTrigger) {
                    vbtn.Nodes.Add(new VirtualButton.PadRightTrigger(Input.Gamepad, 0.25f));
                } else {
                    vbtn.Nodes.Add(new VirtualButton.PadButton(Input.Gamepad, button));
                }
            }
        }

        public static void AddKeysTo(VirtualButton vbtn, List<Keys> keys) {
            if (keys == null)
                return;
            foreach (Keys key in keys) {
                vbtn.Nodes.Add(new VirtualButton.KeyboardKey(key));
            }
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
            base.CreateModMenuSection(menu, inGame, snapshot);

            menu.Add(new TextMenu.Button("modoptions_celestetas_reload".DialogCleanOrNull() ?? "Reload Settings").Pressed(() => {
                LoadSettings();
                OnInputDeregister();
                OnInputInitialize();
            }));
        }

        public static Detour h_UpdateInputs;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateInputs() {
            // This gets relinked to TAS.Manager.UpdateInputs
            throw new Exception("Failed relinking UpdateInputs!");
        }

        public static Detour h_RunThreadWithLogging;
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
                if (i < loops - 1)
                    Engine.Scene?.Tracker.GetEntity<FinalBoss>()?.Render();

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

            if (skipBaseUpdate)
                orig_Game_Update(self, gameTime);
        }

        public static void MInput_Update(On.Monocle.MInput.orig_Update orig) {
            if (!Settings.Enabled) {
                orig();
                return;
            }

            if (!Manager.Running || Manager.Recording) {
                orig();
            }
            UpdateInputs();

            // Hacky, but this works just good enough.
            // The original code executes base.Update(); return; instead.
            if ((Manager.state & State.FrameStep) == State.FrameStep) {
                PreviousGameLoop = Engine.OverloadGameLoop;
                Engine.OverloadGameLoop = FrameStepGameLoop;
            }
        }

        public static Detour h_Game_Update;
        public delegate void d_Game_Update(Game self, GameTime gameTime);
        public static d_Game_Update orig_Game_Update;
        public static void Game_Update(Game self, GameTime gameTime) {
            if (Settings.Enabled && SkipBaseUpdate) {
                return;
            }

            orig_Game_Update(self, gameTime);

            // Check for our own keybindings here.
            if (Instance.ButtonHitboxes.Pressed)
                Settings.ShowHitboxes = !Settings.ShowHitboxes;
            if (Instance.ButtonGraphics.Pressed)
                Settings.SimplifiedGraphics = !Settings.SimplifiedGraphics;
            if (Instance.ButtonCamera.Pressed)
                Settings.CenterCameraMayCauseSoftlocks = !Settings.CenterCameraMayCauseSoftlocks;
        }

        public static Action PreviousGameLoop;
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

        public static void Achievements_Register(On.Celeste.Achievements.orig_Register orig, Achievement achievement) {
            if (Settings.DisableAchievements)
                return;
            orig(achievement);
        }

        public static void Stats_Increment(On.Celeste.Stats.orig_Increment orig, Stat stat, int increment) {
            if (Settings.DisableAchievements)
                return;
            orig(stat, increment);
        }

        public static void Commands_Render(ILContext il) {
            // Hijack string.Format("\n level:       {0}, {1}", xObj, yObj)
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdstr("\n level:       {0}, {1}"),
                i => i.MatchCall(typeof(string), "Format")
            );
            ILCursor c = found[1];
            c.Remove();
            c.EmitDelegate<Func<string, object, object, string>>((text, xObj, yObj) => {
                int x = (int) xObj;
                int y = (int) yObj;
                Level level = Engine.Scene as Level;
                return
                    $"\n world:       {(int) Math.Round(x + level.LevelOffset.X)}, {(int) Math.Round(y + level.LevelOffset.Y)}" +
                    $"\n level:       {x}, {y}";
            });
        }

        public static void Level_Render(ILContext il) {
            ILCursor c;
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdfld(typeof(Pathfinder), "DebugRenderEnabled"),
                i => i.MatchCall(typeof(Draw), "get_SpriteBatch"),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0)
            );

            // Place labels at and after pathfinder rendering code
            ILLabel render = il.DefineLabel();
            ILLabel skipRender = il.DefineLabel();
            c = found[1];
            c.MarkLabel(render);
            c = found[4];
            c.MarkLabel(skipRender);

            // || the value of DebugRenderEnabled with Debug rendering being enabled, && with seekers being present.
            c = found[0];
            c.Index++;
            c.Emit(OpCodes.Brtrue_S, render.Target);
            c.Emit(OpCodes.Call, typeof(GameplayRendererExt).GetMethod("get_RenderDebug"));
            c.Emit(OpCodes.Brfalse_S, skipRender.Target);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Callvirt, typeof(Scene).GetMethod("get_Tracker"));
            MethodInfo GetEntity = typeof(Tracker).GetMethod("GetEntity");
            c.Emit(OpCodes.Callvirt, GetEntity.MakeGenericMethod(new Type[] { typeof(Seeker) }));
        }

        private void Pathfinder_Render(ILContext il) {
            // Remove the for loop which draws pathfinder tiles
            ILCursor c = new ILCursor(il);
            c.FindNext(out ILCursor[] found, i => i.MatchLdfld(typeof(Pathfinder), "lastPath"));
            c.RemoveRange(found[0].Index - 1);
        }

        private void Entity_Render(On.Monocle.Entity.orig_Render orig, Entity self) {
            if (InUpdate)
                return;
            orig(self);
        }

        private void LightingRenderer_Render(On.Celeste.LightingRenderer.orig_Render orig, LightingRenderer self, Scene scene) {
            if (Settings.SimplifiedGraphics)
                return;
            orig(self, scene);
        }

        private void Particle_Render(On.Monocle.Particle.orig_Render orig, ref Particle self) {
            if (Settings.SimplifiedGraphics)
                return;
            orig(ref self);
        }

        private void BackdropRenderer_Render(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene) {
            if (Settings.SimplifiedGraphics)
                return;
            orig(self, scene);
        }

        private void CrystalStaticSpinner_ctor(On.Celeste.CrystalStaticSpinner.orig_ctor_Vector2_bool_CrystalColor orig, CrystalStaticSpinner self, Vector2 position, bool attachToSolid, CrystalColor color) {
            if (Settings.SimplifiedGraphics)
                color = CrystalColor.Blue;
            orig(self, position, attachToSolid, color);
        }

        private DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
            if (Settings.SimplifiedGraphics) {
                return new DustStyles.DustStyle {
                    EdgeColors = new Vector3[] {
                        Color.Orange.ToVector3(),
                        Color.Orange.ToVector3(),
                        Color.Orange.ToVector3()
                    },
                    EyeColor = Color.Orange,
                    EyeTextures = "danger/dustcreature/eyes"
                };
            }
            return orig(session);
        }

        private float LavaRect_Wave(On.Celeste.LavaRect.orig_Wave orig, LavaRect self, int step, float length) {
            if (Settings.SimplifiedGraphics)
                return 0f;
            return orig(self, step, length);
        }

        private float DreamBlock_Lerp(On.Celeste.DreamBlock.orig_Lerp orig, DreamBlock self, float a, float b, float percent) {
            if (Settings.SimplifiedGraphics)
                return 0f;
            return orig(self, a, b, percent);
        }

        private static void FloatingDebris_ctor(FloatingDebris.orig_ctor_Vector2 orig, Celeste.FloatingDebris self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics)
                self.Add(new RemoveSelfComponent());
        }
        
        private static void MoonCreature_ctor(MoonCreature.orig_ctor_Vector2 orig, Celeste.MoonCreature self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics)
                self.Add(new RemoveSelfComponent());
        }

        private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion) {
            if (GameplayRendererExt.RenderDebug || Settings.SimplifiedGraphics) {
                Distort.Anxiety = 0f;
                Distort.GameRate = 1f;
                hasDistortion = false;
            }
            orig(source, map, hasDistortion);
        }
        
        private static void SoundSource_DebugRender(SoundSource.orig_DebugRender orig, Celeste.SoundSource self, Camera camera) {
            if (!Settings.ShowHitboxes)
                orig(self, camera);
        }
        
        private void Puffer_Render(On.Celeste.Puffer.orig_Render orig, Puffer self) {
            if (GameplayRendererExt.RenderDebug)
                Draw.Circle(self.Position, 32f, Color.Red, 32);
            orig(self);
        }

        private void ReflectionTentaclesOnUpdateVertices(On.Celeste.ReflectionTentacles.orig_UpdateVertices orig, ReflectionTentacles self) {
            if ((Manager.state == State.Enable && Manager.FrameLoops > 1) || Settings.SimplifiedGraphics)
                return;

            orig(self);
        }
    }
}
