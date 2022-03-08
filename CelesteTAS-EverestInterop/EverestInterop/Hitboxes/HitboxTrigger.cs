using System;
using Celeste;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxTrigger {
    private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

    [Load]
    private static void Load() {
        IL.Monocle.Entity.DebugRender += HideHitbox;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Entity.DebugRender -= HideHitbox;
    }

    private static void HideHitbox(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Ldarg_0)
            .EmitDelegate<Func<Entity, bool>>(entity =>
                Settings.ShowHitboxes && !Settings.ShowTriggerHitboxes && entity is Trigger &&
                !InfoWatchEntity.WatchingEntities.Contains(entity));
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }
}