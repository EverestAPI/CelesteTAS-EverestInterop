using MonoMod.Cil;
using System;
using TAS.EverestInterop.Hitboxes;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes.ModInterop;

/// Improves hitboxes for SpirialisHelper
internal static class SpirialisHelperHitbox {

    /// Inserts CelesteTAS hitbox check into TimeGameplayRenderer
    [ModILHook("SpirialisHelper", "Celeste.Mod.Spirialis.TimeGameplayRenderer", "Render")]
    private static void FixStopwatchRendering(ILCursor cursor) {
        cursor.GotoNext(MoveType.After, instr => instr.MatchLdsfld("Celeste.Mod.Spirialis.TimeGameplayRenderer", "RenderDebug"));
        cursor.EmitStaticDelegate<Func<bool, bool>>(orig => orig || HitboxToggle.DrawHitboxes);
    }
}
