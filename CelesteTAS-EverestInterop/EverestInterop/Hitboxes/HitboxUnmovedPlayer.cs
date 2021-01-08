using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxUnmovedPlayer {
        private static readonly FieldInfo PlayerHurtbox = typeof(Player).GetPrivateField("hurtbox");
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;
        private static readonly Color hitboxColor = Color.Red.Invert() * 0.7f;
        private static readonly Color hurtboxColor = Color.Lime.Invert() * 0.7f;

        public static void Load() {
            On.Monocle.Hitbox.Render += HitboxOnRender;
        }

        public static void Unload() {
            On.Monocle.Hitbox.Render -= HitboxOnRender;
        }

        private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            if (!(self.Entity is Player player) || !Settings.ShowHitboxes || !Settings.ShowUnmovedPlayerHitbox) {
                orig(self, camera, color);
                return;
            }

            Vector2 offset = (player.ExactPosition - player.PreviousPosition - player.Speed * Engine.DeltaTime).Round();

            if (offset == Vector2.Zero) {
                orig(self, camera, color);
                return;
            }

            player.Position -= offset;

            // hitbox
            orig(self, camera, hitboxColor);

            // hurtbox
            Collider origCollider = player.Collider;
            Collider hurtbox = (Collider) PlayerHurtbox.GetValue(player);
            player.Collider = hurtbox;
            Draw.HollowRect(hurtbox, hurtboxColor);
            player.Collider = origCollider;

            player.Position += offset;

            orig(self, camera, color);
        }
    }
}
