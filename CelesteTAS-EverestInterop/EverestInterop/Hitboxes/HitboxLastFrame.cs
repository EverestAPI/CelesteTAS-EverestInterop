using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxLastFrame {
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        public static void Load() {
            IL.Celeste.PlayerCollider.Check += PlayerColliderOnCheck;
            On.Monocle.Hitbox.Render += HitboxOnRender;
            On.Monocle.Circle.Render += CircleOnRender;
        }

        public static void Unload() {
            IL.Celeste.PlayerCollider.Check -= PlayerColliderOnCheck;
            On.Monocle.Hitbox.Render -= HitboxOnRender;
            On.Monocle.Circle.Render -= CircleOnRender;
        }

        private static void PlayerColliderOnCheck(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            while (ilCursor.TryGotoNext(
                ins => ins.OpCode == OpCodes.Ldarg_1,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchCall<Component>("get_Entity"),
                ins => ins.MatchCallvirt<Entity>("CollideCheck")
            )) {
                ilCursor.Index += 3;
                ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<Entity>>(entity => {
                    if (!Settings.ShowHitboxes || Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.OFF) {
                        return;
                    }

                    entity.SaveLastPosition();
                    entity.SaveLastCollidable();
                    if (entity.Get<StaticMover>() is StaticMover staticMover && staticMover.Platform is Platform platform) {
                        platform.SaveLastPosition();
                    }
                });
            }
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void DrawLastFrameHitbox(Collider self, Color color, Action<Color> invokeOrig) {
            Entity entity = self.Entity;

            if (!Settings.ShowHitboxes
                || Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.OFF
                || entity.Get<PlayerCollider>() == null
                || entity.Scene?.Tracker.GetEntity<Player>() == null
                || entity.LoadLastPosition() == null
                || entity.LoadLastPosition() == entity.Position && Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append
            ) {
                invokeOrig(color);
                return;
            }

            Color lastFrameColor = color;

            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append) {
                lastFrameColor = color.Invert();
            }

            if (entity.Collidable && !entity.LoadLastCollidable()) {
                lastFrameColor *= 0.5f;
            } else if (!entity.Collidable && entity.LoadLastCollidable()) {
                lastFrameColor *= 2f;
            }

            // It's a bit complicated on moving platform, so display two frames of hitboxes at the same time.
            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Override
                && entity.Get<StaticMover>() is StaticMover staticMover
                && staticMover.Platform is Platform platform && platform.Scene != null
                && platform.Position != platform.LoadLastPosition()
            ) {
                invokeOrig(color);
                lastFrameColor = lastFrameColor.Invert();
            }

            if (Settings.ShowLastFrameHitboxes == LastFrameHitboxesTypes.Append) {
                invokeOrig(color);
            }

            Vector2 lastPosition = entity.LoadLastPosition().Value;
            Vector2 currentPosition = entity.Position;

            IEnumerable<PlayerCollider> playerColliders = entity.Components.GetAll<PlayerCollider>().ToArray();
            if (playerColliders.All(playerCollider => playerCollider.Collider != null)) {
                if (playerColliders.Any(playerCollider => playerCollider.Collider == self)) {
                    entity.Position = lastPosition;
                    invokeOrig(lastFrameColor);
                    entity.Position = currentPosition;
                } else {
                    invokeOrig(color);
                }
            } else {
                entity.Position = lastPosition;
                invokeOrig(lastFrameColor);
                entity.Position = currentPosition;
            }
        }
    }
}

public enum LastFrameHitboxesTypes {
    OFF,
    Override,
    Append
}