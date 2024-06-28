using System;
using Celeste;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxTrigger {
    [Load]
    private static void Load() {
        IL.Monocle.Entity.DebugRender += ModDebugRender;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Entity.DebugRender -= ModDebugRender;
    }

    private static void ModDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Ldarg_0)
            .EmitDelegate<Func<Entity, bool>>(IsHideTriggerHitbox);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsHideTriggerHitbox(Entity entity) {
        return TasSettings.ShowHitboxes && !TasSettings.ShowTriggerHitboxes && entity is Trigger &&
               !InfoWatchEntity.WatchingEntities.Contains(entity);
    }
}