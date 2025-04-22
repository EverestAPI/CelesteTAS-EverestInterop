using System;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxConquerorBeam {
    private static GetDelegate<Entity, float>? GetChargeTimer;
    private static GetDelegate<Entity, float>? GetActiveTimer;
    private static GetDelegate<Entity, float>? GetAngle;
    private static GetDelegate<Entity, Entity>? GetBoss;
    private static Type? conquerorBeamType;

    [Initialize]
    private static void Initialize() {
        conquerorBeamType = ModUtils.GetType("Conqueror's Peak", "Celeste.Mod.ricky06ModPack.Entities.ConquerorBeam");

        if (conquerorBeamType != null) {
            GetChargeTimer = conquerorBeamType.CreateGetDelegate<Entity, float>("chargeTimer");
            GetActiveTimer = conquerorBeamType.CreateGetDelegate<Entity, float>("activeTimer");
            GetAngle = conquerorBeamType.CreateGetDelegate<Entity, float>("angle");
            GetBoss = conquerorBeamType.CreateGetDelegate<Entity, Entity>("boss");
            On.Monocle.Entity.DebugRender += ModHitbox;
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= ModHitbox;
    }

    private static void ModHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera);
            return;
        }

        orig(self, camera);

        if (self.GetType() == conquerorBeamType && GetChargeTimer!(self) <= 0f && GetActiveTimer!(self) > 0f) {
            float angle = GetAngle!(self);
            Entity boss = GetBoss!(self);
            Vector2 vector = boss.Center + Calc.AngleToVector(angle, 12f);
            Vector2 vector2 = boss.Center + Calc.AngleToVector(angle, 2000f);
            Vector2 value = (vector2 - vector).Perpendicular().SafeNormalize(2f);
            Draw.Line(vector + value, vector2 + value, HitboxColor.EntityColor);
            Draw.Line(vector - value, vector2 - value, HitboxColor.EntityColor);
            Draw.Line(vector, vector2, HitboxColor.EntityColor);
        }
    }
}
