using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
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

        // The methods we want to hook.

        // The original adds a few lines of code into "Celeste.Engine"'s Update method.
        // Only issue is that Celeste.Engine doesn't exist, but Monocle.Engine.
        // Furthermore, we can't simply add a few lines of code with runtime mods.
        // Instead, we hook "Celeste.Celeste"'s Update.
        private readonly static MethodInfo m_Update = typeof(Celeste.Celeste).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

                        CelesteAddons = Everest.Relinker.GetRelinkedAssembly(new EverestModuleMetadata {
                            PathDirectory = Everest.PathGame,
                            Name = Metadata.Name,
                            DLL = CelesteAddonsPath
                        }, stream,
                        checksumsExtra: new string[] {
                            Everest.Relinker.GetChecksum(Metadata)
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

            // Used to determine how often Update should repeat.
            f_FrameLoops = Manager.GetField("FrameLoops");

            // Runtime hooks are quite different from static patches.
            Type t_CelesteTASModule = GetType();

            // Relink UpdateInputs to TAS.Manager.UpdateInputs because reflection invoke is slow.
            if (t_CelesteTASModule.GetMethod("UpdateInputs").GetDetourLevel() == 0) {
                t_CelesteTASModule.GetMethod("UpdateInputs").Detour(
                    Manager.GetMethod("UpdateInputs")
                );
            }

            orig_Update = m_Update.Detour<d_Update>(t_CelesteTASModule.GetMethod("Update"));

        }

        public override void Unload() {
            if (CelesteAddons == null)
                return;

            // Undetouring UpdateInputs isn't required.

            // Let's just hope that nothing else detoured this, as this is depth-based...
            RuntimeDetour.Undetour(m_Update);
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

        public delegate void d_Update(Celeste.Celeste self, GameTime gameTime);
        public static d_Update orig_Update;
        public static void Update(Celeste.Celeste self, GameTime gameTime) {
            if (!Settings.Enabled) {
                orig_Update(self, gameTime);
                return;
            }

            // Invoke TAS.Manager.UpdateInputs
            UpdateInputs();

            int loops = FrameLoops;
            for (int i = 0; i < loops; i++) {
                orig_Update(self, gameTime);
            }
        }

    }
}
