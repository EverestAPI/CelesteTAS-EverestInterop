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
        HitboxFixer.DrawExactLine(from + perp, to + perp, color);
        HitboxFixer.DrawExactLine(from - perp, to - perp, color);
        HitboxFixer.DrawExactLine(from, to, color);
    }

    /// Changes the color of shot bullets
    private static void On_Hitbox_Render(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        if (TasSettings.ShowHitboxes && self.Entity is FinalBossShot) {
            color = Color.Goldenrod;
        }

        orig(self, camera, color);
    }
}
