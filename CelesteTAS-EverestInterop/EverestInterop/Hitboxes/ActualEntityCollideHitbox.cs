using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop.Hitboxes {
    public static partial class ActualEntityCollideHitbox {
        private const string ActualCollidePositionKey = nameof(ActualCollidePositionKey);
        private const string ActualCollidableKey = nameof(ActualCollidableKey);
        private static ILHook ilHookPlayerOrigUpdateEntity;
        private static Vector2? beforeUpdatePosition;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            ilHookPlayerOrigUpdate = new ILHook(typeof(Player).GetMethod("orig_Update"), ModPlayerOrigUpdateEntity);
            On.Monocle.Hitbox.Render += HitboxOnRenderEntity;
            On.Monocle.Circle.Render += CircleOnRender;
            LoadPlayerHook();
        }

        public static void Unload() {
            ilHookPlayerOrigUpdate?.Dispose();
            On.Monocle.Hitbox.Render -= HitboxOnRenderEntity;
            On.Monocle.Circle.Render -= CircleOnRender;
            UnloadPlayerHook();
        }

        private static void ModPlayerOrigUpdateEntity(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCastclass<PlayerCollider>())) {
                ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<PlayerCollider>>(playerCollider => {
                    Entity entity = playerCollider.Entity;

                    if (entity == null || !Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Off
                        || Manager.FrameLoops > 1) {
                        return;
                    }

                    entity.SaveActualCollidePosition();
                    entity.SaveActualCollidable();
                });
            }
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void HitboxOnRenderEntity(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            DrawLastFrameHitbox(self, color, hitboxColor => orig(self, camera, hitboxColor));
        }

        private static void DrawLastFrameHitbox(Collider self, Color color, Action<Color> invokeOrig) {
            Entity entity = self.Entity;

            if (!Settings.ShowHitboxes
                || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Off
                || Manager.FrameLoops > 1
                || entity.Get<PlayerCollider>() == null
                || entity.Scene?.Tracker.GetEntity<Player>() == null
                || entity.LoadActualCollidePosition() == null
                || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Append && entity.LoadActualCollidePosition() == entity.Position
            ) {
                invokeOrig(color);
                return;
            }

            Color lastFrameColor = Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Append ? color.Invert() : color;

            if (entity.Collidable && !entity.LoadActualCollidable()) {
                lastFrameColor *= 0.5f;
            } else if (!entity.Collidable && entity.LoadActualCollidable()) {
                lastFrameColor *= 2f;
            }

            if (Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Append) {
                invokeOrig(color);
            }

            Vector2 lastPosition = entity.LoadActualCollidePosition().Value;
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

        private static void SaveActualCollidePosition(this Entity entity) {
            entity.SetExtendedDataValue(ActualCollidePositionKey, entity.Position);
        }

        private static Vector2? LoadActualCollidePosition(this Entity entity) {
            return entity.GetExtendedDataValue<Vector2?>(ActualCollidePositionKey);
        }

        private static void ClearActualCollidePosition(this Entity entity) {
            entity.SetExtendedDataValue(ActualCollidePositionKey, null);
        }

        private static void SaveActualCollidable(this Entity entity) {
            entity.SetExtendedDataValue(ActualCollidableKey, entity.Collidable);
        }

        private static bool LoadActualCollidable(this Entity entity) {
            return entity.GetExtendedDataValue<bool>(ActualCollidableKey);
        }
    }

    public enum ActualCollideHitboxTypes {
        Off,

        // ReSharper disable once UnusedMember.Global
        Override,
        Append
    }
}