using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxUnmovedPlayer {
        private static readonly FieldInfo PlayerHurtbox = typeof(Player).GetPrivateField("hurtbox");
        private static readonly Color hitboxColor = Color.Red.Invert() * 0.7f;
        private static readonly Color hurtboxColor = Color.Lime.Invert() * 0.7f;
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        public static void Load() {
            On.Celeste.Player.Update += PlayerOnUpdate;
            On.Monocle.Hitbox.Render += HitboxOnRender;
        }

        public static void Unload() {
            On.Celeste.Player.Update -= PlayerOnUpdate;
            On.Monocle.Hitbox.Render -= HitboxOnRender;
        }

        private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
            orig(self);
            self.SaveLastPosition();
        }

        private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            if (!(self.Entity is Player player) || !Settings.ShowHitboxes || !Settings.ShowUnmovedPlayerHitbox
                || player.LoadLastPosition() == null
                || player.LoadLastPosition().Value == player.Position
                || player.Scene is Level level && level.Transitioning
            ) {
                orig(self, camera, color);
                return;
            }

            // unmoved hitbox
            DrawAssistedHitbox(orig, self, camera, player, player.LoadLastPosition().Value);

            orig(self, camera, color);
        }

        private static void DrawAssistedHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Player player,
            Vector2 hitboxPosition) {
            Collider origCollider = player.Collider;
            Collider hurtbox = (Collider) PlayerHurtbox.GetValue(player);
            Vector2 origPosition = player.Position;

            player.Position = hitboxPosition;

            orig(self, camera, hitboxColor);
            player.Collider = hurtbox;
            Draw.HollowRect(hurtbox, hurtboxColor);
            player.Collider = origCollider;

            player.Position = origPosition;
        }
    }
}