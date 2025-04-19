using System;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using TAS.Gameplay.Hitboxes;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

/// Manages showing improve
public static class HitboxToggle {
    private static bool origDrawHitboxes = false;
    public static bool DrawHitboxes => origDrawHitboxes || TasSettings.ShowHitboxes || !TasSettings.ShowGameplay;

    [Load]
    private static void Load() {
        IL.Celeste.GameplayRenderer.Render += GameplayRendererOnRender;
        IL.Celeste.Distort.Render += DistortOnRender;
        IL.Celeste.Glitch.Apply += GlitchOnApply;
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.GameplayRenderer.Render -= GameplayRendererOnRender;
        IL.Celeste.Distort.Render -= DistortOnRender;
        IL.Celeste.Glitch.Apply -= GlitchOnApply;
    }

    private static void GameplayRendererOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (!ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<GameplayRenderer>("RenderDebug"))) {
            return;
        }

        ilCursor.Remove();
        if (!ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdfld<Monocle.Commands>("Open"))) {
            return;
        }

        ilCursor.Emit(OpCodes.Or);
        ilCursor.EmitDelegate<Func<bool, bool>>(IsShowHitbox);
    }

    private static bool IsShowHitbox(bool orig) {
        origDrawHitboxes = orig;
        return DrawHitboxes && !OffscreenHitbox.ShouldDraw;
    }

    private static void DistortOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), "FxDistort"))) {
            ilCursor.EmitDelegate<Func<Effect, Effect?>>(DisableDistortWhenShowHitbox);
        }
    }

    private static Effect? DisableDistortWhenShowHitbox(Effect effect) {
        return TasSettings.ShowHitboxes ? null : effect;
    }

    private static void GlitchOnApply(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next!;
        ilCursor.EmitDelegate<Func<bool>>(IsShowHitbox);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsShowHitbox() {
        return TasSettings.ShowHitboxes;
    }
}
