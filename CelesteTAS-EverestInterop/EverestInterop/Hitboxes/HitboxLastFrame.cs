using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxLastFrame {
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        public static void Load() {
            On.Monocle.Entity.Update += EntityOnUpdate;
            On.Monocle.Hitbox.Render += HitboxOnRender;
            On.Monocle.Circle.Render += CircleOnRender;
        }

        public static void Unload() {
            On.Monocle.Entity.Update -= EntityOnUpdate;
            On.Monocle.Hitbox.Render -= HitboxOnRender;
            On.Monocle.Circle.Render -= CircleOnRender;
        }

        private static void EntityOnUpdate(On.Monocle.Entity.orig_Update orig, Entity self) {
            if (!(self is Player) && self.Get<PlayerCollider>() != null) {
                self.SaveLastPosition();
            }

            orig(self);
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, HitboxColor.EntityColorInverselyLessAlpha, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, HitboxColor.EntityColorInverselyLessAlpha, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void DrawLastFrameHitbox(Collider self, Color color, Color lastFrameColor, Action<Color> invokeOrig) {
            Entity entity = self.Entity;
            if (entity == null
                || entity is Player
                || entity.Get<PlayerCollider>() == null
                || !Settings.ShowHitboxes
                || Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.OFF
                || entity.LoadLastPosition() == entity.Position) {
                invokeOrig(color);
                return;
            }

            if (entity.Get<StaticMover>() is StaticMover staticMover && staticMover.Platform is Platform platform && platform.Scene != null) {
                    if (platform is JumpThru jumpThru && jumpThru.HasPlayerRider()
                        || platform is Solid solid && solid.HasPlayerRider()) {
                        invokeOrig(color);
                        return;
                    }
            }

            Vector2 lastPosition = entity.LoadLastPosition();
            Vector2 currentPosition = entity.Position;

            entity.Position = lastPosition;
            invokeOrig(lastFrameColor);
            entity.Position = currentPosition;

            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append) {
                invokeOrig(color);
            }
        }
    }

    public enum LastFrameHitboxesTypes {
        OFF,
        Override,
        Append
    }
}