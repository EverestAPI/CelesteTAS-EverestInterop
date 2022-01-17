using System;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxConquerorBeam {
        private static Type conquerorBeamType;

        [Initialize]
        private static void Initialize() {
            conquerorBeamType = FakeAssembly.GetFakeEntryAssembly().GetType("Celeste.Mod.ricky06ModPack.Entities.ConquerorBeam");

            if (conquerorBeamType != null) {
                On.Monocle.Entity.DebugRender += ModHitbox;
            }
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Entity.DebugRender -= ModHitbox;
        }

        private static void ModHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (!CelesteTasModule.Settings.ShowHitboxes) {
                orig(self, camera);
                return;
            }

            orig(self, camera);

            if (self.GetType() == conquerorBeamType) {
                DynamicData dynamicData = new(conquerorBeamType, self);
                if (dynamicData.Get<float>("chargeTimer") <= 0f && dynamicData.Get<float>("activeTimer") > 0f) {
                    Entity boss = dynamicData.Get<Entity>("boss");
                    float angle = dynamicData.Get<float>("angle");
                    Vector2 vector = boss.Center + Calc.AngleToVector(angle, 12f);
                    Vector2 vector2 = boss.Center + Calc.AngleToVector(angle, 2000f);
                    Vector2 value = (vector2 - vector).Perpendicular().SafeNormalize(2f);
                    Draw.Line(vector + value, vector2 + value, HitboxColor.EntityColor);
                    Draw.Line(vector - value, vector2 - value, HitboxColor.EntityColor);
                    Draw.Line(vector, vector2, HitboxColor.EntityColor);
                }
            }
        }
    }
}