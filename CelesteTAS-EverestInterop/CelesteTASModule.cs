using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod;
using MonoMod.Detour;
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

        public static string CelesteAddonsPath => Path.Combine(Everest.PathGame, "Celeste-Addons.dll");
        public Assembly CelesteAddons;
        public Type Manager;

        // The fields we want to access from the Celeste-Addons.dll
        private static FieldInfo f_FrameLoops;
        private static int FrameLoops {
            get {
                return ((int?) f_FrameLoops?.GetValue(null)) ?? 1;
            }
            set {
                f_FrameLoops?.SetValue(null, value);
            }
        }

        private static FieldInfo f_Running;
        private static bool Running {
            get {
                return ((bool?) f_Running?.GetValue(null)) ?? false;
            }
            set {
                f_Running?.SetValue(null, value);
            }
        }

        private static FieldInfo f_Recording;
        private static bool Recording {
            get {
                return ((bool?) f_Recording?.GetValue(null)) ?? false;
            }
            set {
                f_Recording?.SetValue(null, value);
            }
        }

        private static FieldInfo f_state;
        private static State state {
            get {
                if (f_state == null)
                    return State.None;
                return (State) (int) f_state.GetValue(null);
            }
            set {
                f_state?.SetValue(null, value);
            }
        }

        // The methods we want to hook.

        // The original mod adds a few lines of code into Monocle.Engine::Update.
        private readonly static MethodInfo m_Engine_Update = typeof(Engine).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        // The original mod wraps the input 
        private readonly static MethodInfo m_MInput_Update = typeof(MInput).GetMethod("Update", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        public CelesteTASModule() {
            Instance = this;
        }

        public override void Load() {
            if (!File.Exists(CelesteAddonsPath)) {
                Logger.Log("tas-interop", "Celeste-Addons.dll not found - CelesteTAS-EverestInterop not loading.");
                return;
            }

            Logger.Log("tas-interop", "Loading Celeste-Addons.dll");
            try {
                // Relink from XNA to FNA if required and replace some private accesses with public replacements.
                using (Stream stream = File.OpenRead(CelesteAddonsPath)) {
                    // Backup the old modder and request a new one.
                    MonoModder modderOld = Everest.Relinker.Modder;
                    Everest.Relinker.Modder = null;
                    using (MonoModder modder = Everest.Relinker.Modder) {
                        
                        modder.MethodRewriter += PatchAddonsMethod;

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
                            // Make Celeste-Addons.dll depend on this runtime mod.
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
                Logger.Log("tas-interop", "Failed loading Celeste-Addons.dll");
                e.LogDetailed();
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
            if (t_CelesteTASModule.GetMethod("UpdateInputs").GetDetourLevel() == 0) {
                t_CelesteTASModule.GetMethod("UpdateInputs").Detour(
                    Manager.GetMethod("UpdateInputs")
                );
            }

            orig_Engine_Update = m_Engine_Update.Detour<d_Engine_Update>(t_CelesteTASModule.GetMethod("Engine_Update"));
            orig_MInput_Update = m_MInput_Update.Detour<d_MInput_Update>(t_CelesteTASModule.GetMethod("MInput_Update"));

        }

        public override void Unload() {
            if (CelesteAddons == null)
                return;

            // Undetouring UpdateInputs isn't required.

            // Let's just hope that nothing else detoured this, as this is depth-based...
            RuntimeDetour.Undetour(m_Engine_Update);
            RuntimeDetour.Undetour(m_MInput_Update);
        }

        private TypeDefinition td_Engine;
        private MethodDefinition md_Engine_get_Scene;
        public void PatchAddonsMethod(MonoModder modder, MethodDefinition method) {
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

                // Replace ldfld Engine::scene with ldsfld Engine::Scene.
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateInputs() {
            // This gets relinked to TAS.Manager.UpdateInputs
            throw new Exception("UpdateInputs not relinked!");
        }

        public delegate void d_Engine_Update(Engine self, GameTime gameTime);
        public static d_Engine_Update orig_Engine_Update;
        public static void Engine_Update(Celeste.Celeste self, GameTime gameTime) {
            if (!Settings.Enabled) {
                orig_Engine_Update(self, gameTime);
                return;
            }

            int loops = FrameLoops;
            for (int i = 0; i < loops; i++) {
                orig_Engine_Update(self, gameTime);
            }
        }

        public delegate void d_MInput_Update();
        public static d_MInput_Update orig_MInput_Update;
        public static void MInput_Update() {
            if (!Settings.Enabled) {
                orig_MInput_Update();
                return;
            }

            if (!Running || Recording) {
                orig_MInput_Update();
            }
            UpdateInputs();

            // Hacky, but this works just good enough.
            // The original code executes base.Update(); return; instead.
            if ((state & State.FrameStep) == State.FrameStep) {
                PreviousGameLoop = Engine.OverloadGameLoop;
                Engine.OverloadGameLoop = FrameStepGameLoop;
            }
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

    }
}
