using System;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxToggle {
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        private static bool origDrawHitboxes = false;

        public static bool DrawHitboxes => origDrawHitboxes || Settings.ShowHitboxes || !Settings.ShowGameplay;

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
            ilCursor.EmitDelegate<Func<bool, bool>>(orig => {
                origDrawHitboxes = orig;
                return DrawHitboxes && !FreeCameraHitbox.DrawFreeCameraHitboxes;
            });
        }

        private static void DistortOnRender(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), "FxDistort"))) {
                ilCursor.EmitDelegate<Func<Effect, Effect>>(effect => Settings.ShowHitboxes ? null : effect);
            }
        }

        private static void GlitchOnApply(ILContext il) {
            ILCursor ilCursor = new(il);
            Instruction start = ilCursor.Next;
            ilCursor.EmitDelegate<Func<bool>>(() => Settings.ShowHitboxes);
            ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
        }
    }
}