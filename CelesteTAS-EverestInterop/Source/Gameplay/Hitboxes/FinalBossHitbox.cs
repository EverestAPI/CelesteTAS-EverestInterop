using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication.Util;
using System;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;

namespace TAS.Gameplay.Hitboxes;

/// Adds hitboxes to the Badeline boss fight
internal static class FinalBossHitbox {

    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += On_Entity_DebugRender;
        On.Monocle.Hitbox.Render += On_Hitbox_Render;
    }
    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= On_Entity_DebugRender;
        On.Monocle.Hitbox.Render -= On_Hitbox_Render;
    }

    public static bool IsBeamCollidable(FinalBossBeam beam) => beam.chargeTimer <= 0.0f && beam.activeTimer - Engine.DeltaTime > 0.0f;

    /// Adds correct hitboxes to the laser beam
    private static void On_Entity_DebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes || self is not FinalBossBeam beam) {
            return;
        }

        var color = beam.followTimer > 0.0f ? Color.DarkOrchid : Color.Goldenrod;
        if (beam.LoadActualCollidable() is { } collidable) {
            color = collidable ? color : color * HitboxColor.UnCollidableAlpha;
        } else {
            color = IsBeamCollidable(beam) ? color : color * HitboxColor.UnCollidableAlpha;
        }

        var from = beam.boss.BeamOrigin + Calc.AngleToVector(beam.angle, FinalBossBeam.BeamStartDist);
        var to = beam.boss.BeamOrigin + Calc.AngleToVector(beam.angle, FinalBossBeam.BeamLength);
        var perp = (to - from).Perpendicular().SafeNormalize(FinalBossBeam.CollideCheckSep);
        DrawExactLine(from + perp, to + perp, color);
        DrawExactLine(from - perp, to - perp, color);
        DrawExactLine(from, to, color);
    }

    /// Changes the color of shot bullets
    private static void On_Hitbox_Render(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        if (TasSettings.ShowHitboxes && self.Entity is FinalBossShot) {
            color = Color.Goldenrod;
        }

        orig(self, camera, color);
    }

    /// Draws an exact line, filling all pixels the line actually intersects
    private static void DrawExactLine(Vector2 from, Vector2 to, Color color) {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        float step = Math.Min(Math.Abs(dx), Math.Abs(dy));
        dx /= step;
        dy /= step;

        // Starting / Ending point will post likely not be an integer coordinate
        float startDist = dx > dy
            ? Math.Sign(dx) * 0.5f + 0.5f - from.X.Mod(1.0f)
            : Math.Sign(dy) * 0.5f + 0.5f - from.Y.Mod(1.0f);
        float endDist = dx > dy
            ? -Math.Sign(dx) * 0.5f + 0.5f - to.X.Mod(1.0f)
            : -Math.Sign(dy) * 0.5f + 0.5f - to.Y.Mod(1.0f);

        float startX = from.X + startDist * dx;
        float startY = from.Y + startDist * dy;

        float endX = to.X + endDist * dx;
        float endY = to.Y + endDist * dy;

        int steps = (int) Math.Min(Math.Abs(endX - startX), Math.Abs(endY - startY));

        float x = startX, y = startY;
        for (int i = 0; i < steps; i++, x += dx, y += dy) {
            int left   = (int) MathF.Floor  (Math.Min(x, x + dx));
            int right  = (int) MathF.Ceiling(Math.Max(x, x + dx));
            int top    = (int) MathF.Floor  (Math.Min(y, y + dy));
            int bottom = (int) MathF.Ceiling(Math.Max(y, y + dy));
            Draw.Rect(left, top, right - left, bottom - top, color);
        }

        int startLeft   = (int) MathF.Floor  (Math.Min(from.X, startX));
        int startRight  = (int) MathF.Ceiling(Math.Max(from.X, startX));
        int startTop    = (int) MathF.Floor  (Math.Min(from.Y, startY));
        int startBottom = (int) MathF.Ceiling(Math.Max(from.Y, startY));
        Draw.Rect(startLeft, startTop, startLeft - startRight, startBottom - startTop, color);

        int endLeft   = (int) MathF.Floor  (Math.Min(to.X, endX));
        int endRight  = (int) MathF.Ceiling(Math.Max(to.X, endX));
        int endTop    = (int) MathF.Floor  (Math.Min(to.Y, endY));
        int endBottom = (int) MathF.Ceiling(Math.Max(to.Y, endY));
        Draw.Rect(endLeft, endTop, endLeft - endRight, endBottom - endTop, color);
    }
}
