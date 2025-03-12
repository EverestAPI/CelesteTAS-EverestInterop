using System;
using Celeste;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop;

public static class HideGameplay {
    [Load]
    private static void Load() {
        IL.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
    }

    private static void GameplayRenderer_Render(ILContext il) {
        ILCursor c;

        // Mark the instr after RenderExcept.
        ILLabel lblAfterEntities = il.DefineLabel();
        c = new ILCursor(il).Goto(0);
        c.GotoNext(
            i => i.MatchCallvirt(typeof(EntityList), "RenderExcept")
        );
        c.Index++;
        c.MarkLabel(lblAfterEntities);

        // Branch after calling Begin.
        c = new ILCursor(il).Goto(0);
        // GotoNext skips c.Next
        if (!c.Next!.MatchCall(typeof(GameplayRenderer), "Begin")) {
            c.GotoNext(i => i.MatchCall(typeof(GameplayRenderer), "Begin"));
        }

        c.Index++;
        c.EmitDelegate<Func<bool>>(IsHideGamePlay);
        // c.Emit(OpCodes.Call, typeof(CelesteTASModule).GetMethodInfo("get_Settings"));
        // c.Emit(OpCodes.Callvirt, typeof(CelesteTASModuleSettings).GetMethodInfo("get_HideGameplay"));
        c.Emit(OpCodes.Brtrue, lblAfterEntities);
    }

    private static bool IsHideGamePlay() {
        return !TasSettings.ShowGameplay;
    }
}
