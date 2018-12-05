using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.HookGen;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Logger = Celeste.Mod.Logger;

namespace TAS.EverestInterop {
    public class CelesteTASModule : EverestModule {

        public static CelesteTASModule Instance;

        public override Type SettingsType => typeof(CelesteTASModuleSettings);
        public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings) Instance._Settings;

        public static string CelesteAddonsPath { get; protected set; }
        public Assembly CelesteAddons;
        public Type Manager;

        // The fields we want to access from Celeste-Addons
        private static FieldInfo f_FrameLoops;
        public static int FrameLoops {
            get {
                return ((int?) f_FrameLoops?.GetValue(null)) ?? 1;
            }
            set {
                f_FrameLoops?.SetValue(null, value);
            }
        }

        private static FieldInfo f_Running;
        public static bool Running {
            get {
                return ((bool?) f_Running?.GetValue(null)) ?? false;
            }
            set {
                f_Running?.SetValue(null, value);
            }
        }

        private static FieldInfo f_Recording;
        public static bool Recording {
            get {
                return ((bool?) f_Recording?.GetValue(null)) ?? false;
            }
            set {
                f_Recording?.SetValue(null, value);
            }
        }

        private static FieldInfo f_state;
        public static State state {
            get {
                if (f_state == null)
                    return State.None;
                return (State) (int) f_state.GetValue(null);
            }
            set {
                f_state?.SetValue(null, value);
            }
        }

        private static bool SkipBaseUpdate;

        public CelesteTASModule() {
            Instance = this;
        }

        public override void Load() {
            if (typeof(Game).Assembly.GetName().Name.Contains("FNA")) {
                CelesteAddonsPath = Path.Combine(Everest.PathGame, "Celeste-Addons-OpenGL.dll");
            } else {
                CelesteAddonsPath = Path.Combine(Everest.PathGame, "Celeste-Addons-XNA.dll");
            }

            if (!File.Exists(CelesteAddonsPath))
                CelesteAddonsPath = Path.Combine(Everest.PathGame, "Celeste-Addons.dll");

            if (!File.Exists(CelesteAddonsPath)) {
                Logger.Log("tas-interop", "Celeste-Addons not found - CelesteTAS-EverestInterop not loading.");
                return;
            }

            Logger.Log("tas-interop", "Loading Celeste-Addons");
            try {
                // Relink from XNA to FNA if required and replace some private accesses with public replacements.
                using (Stream stream = File.OpenRead(CelesteAddonsPath)) {
                    // Backup the old modder and request a new one.
                    MonoModder modderOld = Everest.Relinker.Modder;
                    Everest.Relinker.Modder = null;
                    using (MonoModder modder = Everest.Relinker.Modder) {
                        
                        modder.MethodRewriter += PatchAddons;

                        // Normal brain: Hardcoded relinker map.
                        // 0x0ade brain: Helper attribute, dynamically generated map.
                        Type proxies = typeof(CelesteTASProxies);
                        foreach (MethodInfo proxy in proxies.GetMethods()) {
                            CelesteTASProxyAttribute attrib = proxy.GetCustomAttribute<CelesteTASProxyAttribute>();
                            if (attrib == null)
                                continue;
                            modder.RelinkMap[attrib.FindableID] = new RelinkMapEntry(proxies.FullName, proxy.GetFindableID(withType: false));
                        }

                        CelesteAddons = Everest.Relinker.GetRelinkedAssembly(new EverestModuleMetadata {
                            PathDirectory = Everest.PathGame,
                            Name = Metadata.Name,
                            DLL = CelesteAddonsPath
                        }, stream,
                        checksumsExtra: new string[] {
                            Everest.Relinker.GetChecksum(Metadata)
                        }, prePatch: _ => {
                            // Make Celeste-Addons depend on this runtime mod.
                            Assembly interop = Assembly.GetExecutingAssembly();
                            // Add the assembly name reference to the list of the dependencies.
                            AssemblyName interopName = interop.GetName();
                            AssemblyNameReference interopRef = new AssemblyNameReference(interopName.Name, interopName.Version);
                            modder.Module.AssemblyReferences.Add(interopRef);
                            // Preload the new dependency.
                            // We shouldn't rely on interop.Location... but on the other hand, this relies on the mod never shipping precached.
                            string interopPath = Everest.Relinker.GetCachedPath(Metadata);
                            modder.DependencyCache[interopRef.Name] =
                            modder.DependencyCache[interopRef.FullName] =
                                MonoModExt.ReadModule(interopPath, modder.GenReaderParameters(false, interopPath));
                            // Map it.
                            modder.MapDependency(modder.Module, interopRef);
                        });

                        // Prevent MonoMod from clearing the shared caches.
                        modder.RelinkModuleMap = new Dictionary<string, ModuleDefinition>();
                        modder.RelinkMap = new Dictionary<string, object>();
                    }
                    // Restore the old modder.
                    Everest.Relinker.Modder = modderOld;
                }
            } catch (Exception e) {
                Logger.Log("tas-interop", "Failed loading Celeste-Addons");
                Logger.LogDetailed(e);
            }
            if (CelesteAddons == null)
                return;

            // Get everything reflection-related.
            Manager = CelesteAddons.GetType("TAS.Manager");
            f_FrameLoops = Manager.GetField("FrameLoops");
            f_Running = Manager.GetField("Running");
            f_Recording = Manager.GetField("Recording");
            f_state = Manager.GetField("state");

            // Runtime hooks are quite different from static patches.
            Type t_CelesteTASModule = GetType();

            // Relink UpdateInputs to TAS.Manager.UpdateInputs because reflection invoke is slow.
            h_UpdateInputs = new Detour(
                typeof(CelesteTASModule).GetMethod("UpdateInputs"),
                Manager.GetMethod("UpdateInputs")
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
        }

        public override void Unload() {
            if (CelesteAddons == null)
                return;

            h_UpdateInputs.Dispose();
            h_RunThreadWithLogging.Dispose();
            On.Monocle.Engine.Update -= Engine_Update;
            On.Monocle.MInput.Update -= MInput_Update;
            On.Celeste.RunThread.Start -= RunThread_Start;
            h_Game_Update.Dispose();
            On.Celeste.Achievements.Register -= Achievements_Register;
            On.Celeste.Stats.Increment -= Stats_Increment;
        }

        private TypeDefinition td_Engine;
        private MethodDefinition md_Engine_get_Scene;
        public void PatchAddons(MonoModder modder, MethodDefinition method) {
            if (!method.HasBody)
                return;

            if (td_Engine == null)
                td_Engine = modder.FindType("Monocle.Engine")?.Resolve();
            if (td_Engine == null)
                return;

            if (md_Engine_get_Scene == null)
                md_Engine_get_Scene = td_Engine.FindMethod("Monocle.Scene get_Scene()");
            if (md_Engine_get_Scene == null)
                return;

            Mono.Collections.Generic.Collection<Instruction> instrs = method.Body.Instructions;
            ILProcessor il = method.Body.GetILProcessor();
            for (int instri = 0; instri < instrs.Count; instri++) {
                Instruction instr = instrs[instri];

                // Replace ldfld Engine::scene with call Engine::get_Scene.
                if (instr.OpCode == OpCodes.Ldfld && (instr.Operand as FieldReference)?.FullName == "Monocle.Scene Monocle.Engine::scene") {

                    // Pop the loaded instance.
                    instrs.Insert(instri, il.Create(OpCodes.Pop));
                    instri++;

                    // Replace the field load with a property getter call.
                    instr.OpCode = OpCodes.Call;
                    instr.Operand = md_Engine_get_Scene;
                }
            }
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

            if (!Settings.Enabled) {
                orig(self, gameTime);
                return;
            }

            // The original patch doesn't store FrameLoops in a local variable, but it's only updated in UpdateInputs anyway.
            int loops = FrameLoops;

            SkipBaseUpdate = loops >= 10;

            for (int i = 0; i < loops; i++) {
                // Anything happening early on runs in the MInput.Update hook.
                orig(self, gameTime);

                if (i >= 8 && Engine.Scene?.Tracker.GetEntity<FinalBoss>() != null) {
                    break;
                }
            }

            SkipBaseUpdate = false;
            if (loops >= 10)
                orig_Game_Update(self, gameTime);
        }

        public static void MInput_Update(On.Monocle.MInput.orig_Update orig) {
            if (!Settings.Enabled) {
                orig();
                return;
            }

            if (!Running || Recording) {
                orig();
            }
            UpdateInputs();

            // Hacky, but this works just good enough.
            // The original code executes base.Update(); return; instead.
            if ((state & State.FrameStep) == State.FrameStep) {
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
        }

        public static Action PreviousGameLoop;
        public static void FrameStepGameLoop() {
            Engine.OverloadGameLoop = PreviousGameLoop;
        }

        [Flags]
        public enum State {
            None = 0,
            Enable = 1,
            Record = 2,
            FrameStep = 4
        }

        public static void RunThread_Start(On.Celeste.RunThread.orig_Start orig, Action method, string name, bool highPriority) {
            if (Running) {
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

        public static void Commands_Render(HookIL il) {
            il.At(0).FindNext(out HookILCursor[] found,
                i => i.MatchLdstr("\n level:       {0}, {1}"),
                i => i.MatchCall(typeof(string), "Format")
            );
            HookILCursor format = found[1];
            format.Remove();
            format.EmitDelegate<Func<string, object, object, string>>((text, xObj, yObj) => {
                int x = (int) xObj;
                int y = (int) yObj;
                Level level = Engine.Scene as Level;
                return
                    $"\n world:       {(int) Math.Round(x + level.LevelOffset.X)}, {(int) Math.Round(y + level.LevelOffset.Y)}" +
                    $"\n level:       {x}, {y}";
            });
        }

    }
}
