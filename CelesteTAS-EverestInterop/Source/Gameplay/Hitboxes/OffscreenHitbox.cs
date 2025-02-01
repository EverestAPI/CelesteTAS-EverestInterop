using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using System;
using TAS.EverestInterop.Hitboxes;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes;

/// Renders hitboxes which are outside the regular gameplay screen,
/// when zooming out with the center camera
internal static class OffscreenHitbox {
    public static bool ShouldDraw => TasSettings.CenterCamera && CenterCamera.ZoomedOut;
    private static VirtualRenderTarget? offscreenBuffer;

    [Load]
    private static void Load() {
        // Render hitboxes into own buffer, below sub-HUD layer
        typeof(SubHudRenderer)
            .GetMethodInfo(nameof(SubHudRenderer.BeforeRender))?
            .IlHook((cursor, _) => {
                cursor.EmitLdarg1();
                cursor.EmitDelegate(DrawHitboxesToBuffer);
            });
        typeof(SubHudRenderer)
            .GetMethodInfo(nameof(SubHudRenderer.Render))?
            .IlHook((cursor, _) => {
                cursor.EmitLdarg1();
                cursor.EmitDelegate(DrawBufferToScreen);
            });
    }

    // Scale down rendered hitboxes once camera is zoomed out too far
    private static float BufferScale => Math.Min(1.0f, (offscreenBuffer?.Width ?? 0) / (Celeste.Celeste.GameWidth / CenterCamera.ZoomLevel));

    private static void DrawHitboxesToBuffer(Scene scene) {
        if (scene is not Level || !HitboxToggle.DrawHitboxes || !ShouldDraw) {
            return;
        }

        offscreenBuffer ??= VirtualContent.CreateRenderTarget("CelesteTAS/offscreen-hitboxes", Celeste.Celeste.TargetWidth + 2, Celeste.Celeste.TargetHeight + 2, depth: true, preserve: true);
        Engine.Graphics.GraphicsDevice.SetRenderTarget(offscreenBuffer);
        Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null, CenterCamera.ScreenCamera.Matrix * Matrix.CreateScale(BufferScale));

        HitboxFixer.DrawingHitboxes = true;
        scene.Entities.DebugRender(CenterCamera.ScreenCamera);
        HitboxFixer.DrawingHitboxes = false;

        Draw.SpriteBatch.End();
    }
    private static void DrawBufferToScreen(Scene scene) {
        if (scene is not Level || !HitboxToggle.DrawHitboxes || !ShouldDraw || offscreenBuffer == null) {
            return;
        }

        var effects = SpriteEffects.None;
        if (SaveData.Instance?.Assists.MirrorMode ?? false) {
            effects |= SpriteEffects.FlipHorizontally;
        }
        if (ExtendedVariantsInterop.UpsideDown) {
            effects |= SpriteEffects.FlipVertically;
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Engine.ScreenMatrix);
        Draw.SpriteBatch.Draw(offscreenBuffer, Vector2.Zero, CenterCamera.ScreenCamera.Viewport.Bounds, Color.White, 0.0f, Vector2.Zero, CenterCamera.ZoomLevel * (Celeste.Celeste.TargetWidth / Celeste.Celeste.GameWidth) / BufferScale, effects, 0.0f);
        Draw.SpriteBatch.End();
    }
}
