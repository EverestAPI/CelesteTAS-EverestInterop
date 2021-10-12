using System;
using Celeste;
using MonoMod.Cil;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxToggle {
        [Load]
        private static void Load() {
            IL.Celeste.GameplayRenderer.Render += GameplayRendererOnRender;
        }

        [Unload]
        private static void Unload() {
            IL.Celeste.GameplayRenderer.Render -= GameplayRendererOnRender;
        }

        private static void GameplayRendererOnRender(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<GameplayRenderer>("RenderDebug"))) {
                ilCursor.EmitDelegate<Func<bool, bool>>(renderDebug => renderDebug || CelesteTasModule.Settings.ShowHitboxes);
            }
        }
    }
}