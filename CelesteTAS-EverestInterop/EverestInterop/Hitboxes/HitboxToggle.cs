using System;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxToggle {
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [Load]
        private static void Load() {
            IL.Celeste.GameplayRenderer.Render += GameplayRendererOnRender;
            On.Celeste.Distort.Render += Distort_Render;
        }

        [Unload]
        private static void Unload() {
            IL.Celeste.GameplayRenderer.Render -= GameplayRendererOnRender;
            On.Celeste.Distort.Render -= Distort_Render;
        }

        private static void GameplayRendererOnRender(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<GameplayRenderer>("RenderDebug"))) {
                ilCursor.EmitDelegate<Func<bool, bool>>(renderDebug =>
                    renderDebug || CelesteTasModule.Settings.ShowHitboxes || !CelesteTasModule.Settings.ShowGameplay);
            }
        }

        private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion) {
            if (Settings.ShowHitboxes) {
                Distort.Anxiety = 0f;
                Distort.GameRate = 1f;
                hasDistortion = false;
            }

            orig(source, map, hasDistortion);
        }
    }
}