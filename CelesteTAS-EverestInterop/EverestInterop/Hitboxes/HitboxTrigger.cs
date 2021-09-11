using System;
using Celeste;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxTrigger {
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            IL.Monocle.Entity.DebugRender += HideHitbox;
        }

        public static void Unload() {
            IL.Monocle.Entity.DebugRender -= HideHitbox;
        }

        private static void HideHitbox(ILContext il) {
            ILCursor ilCursor = new(il);
            Instruction start = ilCursor.Next;
            ilCursor.Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Entity, bool>>(entity => Settings.ShowHitboxes && !Settings.ShowTriggerHitboxes && entity is Trigger && !InfoWatchEntity.WatchingEntities.Contains(entity));
            ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
        }
    }
}