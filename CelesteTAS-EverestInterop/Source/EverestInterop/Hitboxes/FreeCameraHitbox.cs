using System;
using Celeste;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class FreeCameraHitbox {
    private static VirtualRenderTarget hitboxCanvas;
    public static bool DrawFreeCameraHitboxes => TasSettings.CenterCamera && CenterCamera.LevelZoomOut;

    [Load]
    private static void Load() {
        On.Celeste.Mod.UI.SubHudRenderer.RenderContent += SubHudRendererOnRenderContent;
        On.Celeste.Mod.UI.SubHudRenderer.BeforeRender += SubHudRendererOnBeforeRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Mod.UI.SubHudRenderer.RenderContent -= SubHudRendererOnRenderContent;
        On.Celeste.Mod.UI.SubHudRenderer.BeforeRender -= SubHudRendererOnBeforeRender;
    }

    private static void SubHudRendererOnBeforeRender(On.Celeste.Mod.UI.SubHudRenderer.orig_BeforeRender orig, SubHudRenderer self, Scene scene) {
        hitboxCanvas ??= VirtualContent.CreateRenderTarget("freecamera-hitbox", (int) Math.Round(320 * CenterCamera.MaximumViewportScale),
            (int) Math.Round(180 * CenterCamera.MaximumViewportScale));
        if (scene is Level && HitboxToggle.DrawHitboxes && DrawFreeCameraHitboxes) {
            Engine.Graphics.GraphicsDevice.SetRenderTarget(hitboxCanvas);
            Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);

            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointWrap, DepthStencilState.Default,
                RasterizerState.CullNone, null, CenterCamera.ScreenCamera.Matrix);

            HitboxFixer.DrawingHitboxes = true;
            scene.Entities.DebugRender(CenterCamera.ScreenCamera);
            HitboxFixer.DrawingHitboxes = false;

            Draw.SpriteBatch.End();
        }

        orig(self, scene);
    }

    private static void SubHudRendererOnRenderContent(On.Celeste.Mod.UI.SubHudRenderer.orig_RenderContent orig, SubHudRenderer self,
        Scene scene) {
        if (scene is Level && HitboxToggle.DrawHitboxes && DrawFreeCameraHitboxes) {
            SubHudRenderer.BeginRender(sampler: SamplerState.PointWrap);

            // buffer draws at (-1, -1) to screen so we need to draw at (1, 1) to buffer
            Vector2 position = SubHudRenderer.DrawToBuffer ? Vector2.One : Vector2.Zero;

            SpriteEffects spriteEffects = SpriteEffects.None;
            if (SaveData.Instance?.Assists.MirrorMode ?? false) {
                spriteEffects |= SpriteEffects.FlipHorizontally;
            }

            if (ExtendedVariantsUtils.UpsideDown) {
                spriteEffects |= SpriteEffects.FlipVertically;
            }

            Draw.SpriteBatch.Draw(hitboxCanvas, position,
                new Rectangle(0, 0, CenterCamera.ScreenCamera.Viewport.Width, CenterCamera.ScreenCamera.Viewport.Height), Color.White, 0f,
                Vector2.Zero, CenterCamera.LevelZoom * 6f, spriteEffects, 0f);

            SubHudRenderer.EndRender();
        }

        orig(self, scene);
    }
}