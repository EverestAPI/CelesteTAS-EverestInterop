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
    private static float BufferScale => Math.Min(1.0f, (offscreenBuffer?.Width ?? 0) / (CelesteGame.GameWidth / CenterCamera.ZoomLevel));

    private static void DrawHitboxesToBuffer(Scene scene) {
        if (scene is not Level || !HitboxToggle.DrawHitboxes || !ShouldDraw) {
            return;
        }

        offscreenBuffer ??= VirtualContent.CreateRenderTarget("CelesteTAS/offscreen-hitboxes", CelesteGame.TargetWidth + 2, CelesteGame.TargetHeight + 2, depth: true, preserve: true);
        Engine.Graphics.GraphicsDevice.SetRenderTarget(offscreenBuffer);
        Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null, CenterCamera.ScreenCamera.Matrix * Matrix.CreateScale(BufferScale));

        HitboxFixer.DrawingHitboxes = true;
        scene.Entities.DebugRender(CenterCamera.ScreenCamera);
        HitboxFixer.DrawingHitboxes = false;

        Draw.SpriteBatch.End();
    }
    private static void DrawBufferToScreen(Scene scene) {
        if (scene is not Level level || !HitboxToggle.DrawHitboxes || !ShouldDraw || offscreenBuffer == null) {
            return;
        }

        var effects = SpriteEffects.None;
        if (SaveData.Instance?.Assists.MirrorMode ?? false) {
            effects |= SpriteEffects.FlipHorizontally;
        }
        if (ExtendedVariantsInterop.UpsideDown) {
            effects |= SpriteEffects.FlipVertically;
        }

        string? colorGradeOverwrite = ExtendedVariantsInterop.ColorGrading;

        var lastColorTex = GFX.ColorGrades.GetOrDefault(colorGradeOverwrite ?? level.lastColorGrade, GFX.ColorGrades["none"]);
        var nextColorTex = GFX.ColorGrades.GetOrDefault(colorGradeOverwrite ?? level.Session.ColorGrade, GFX.ColorGrades["none"]);
        if (level.colorGradeEase > 0f && lastColorTex != nextColorTex) {
            ColorGrade.Set(lastColorTex, nextColorTex, level.colorGradeEase);
        } else {
            ColorGrade.Set(nextColorTex);
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, ColorGrade.Effect, Engine.ScreenMatrix);
        Draw.SpriteBatch.Draw(offscreenBuffer, Vector2.Zero, CenterCamera.ScreenCamera.Viewport.Bounds, Color.White, 0.0f, Vector2.Zero, CenterCamera.ZoomLevel * (CelesteGame.TargetWidth / CelesteGame.GameWidth) / BufferScale, effects, 0.0f);
        Draw.SpriteBatch.End();
    }
}
