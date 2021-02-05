using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

namespace TAS.EverestInterop.Hitboxes {
public static class HitboxFinalBoss {
    public static void Load() {
        On.Monocle.Entity.DebugRender += ModHitbox;
        On.Monocle.Hitbox.Render += HitboxOnRender;
    }

    public static void Unload() {
        On.Monocle.Entity.DebugRender -= ModHitbox;
        On.Monocle.Hitbox.Render -= HitboxOnRender;
    }

    private static void ModHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!CelesteTASModule.Settings.ShowHitboxes) {
            orig(self, camera);
            return;
        }

        orig(self, camera);

        if (self is FinalBossBeam finalBossBeam) {
            DynData<FinalBossBeam> dynData = finalBossBeam.GetDynDataInstance();
            if (dynData.Get<float>("chargeTimer") <= 0f && dynData.Get<float>("activeTimer") > 0f) {
                FinalBoss boss = dynData.Get<FinalBoss>("boss");
                float angle = dynData.Get<float>("angle");
                Vector2 vector = boss.BeamOrigin + Calc.AngleToVector(angle, 12f);
                Vector2 vector2 = boss.BeamOrigin + Calc.AngleToVector(angle, 2000f);
                Vector2 value = (vector2 - vector).Perpendicular().SafeNormalize(2f);
                Player player = boss.Scene.CollideFirst<Player>(vector + value, vector2 + value);
                Draw.Line(vector + value, vector2 + value, Color.Aqua);
                Draw.Line(vector - value, vector2 - value, Color.Aqua);
                Draw.Line(vector, vector2, Color.Aqua);
            }
        }
    }

    private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        if (!CelesteTASModule.Settings.ShowHitboxes) {
            orig(self, camera, color);
            return;
        }

        if (self.Entity is FinalBossShot) {
            color = Color.Aqua;
        }

        orig(self, camera, color);
    }
}
}