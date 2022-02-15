using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes {
    public static partial class ActualEntityCollideHitbox {
        private static readonly Dictionary<Entity, Vector2> LastPositions = new();
        private static readonly Dictionary<Entity, bool> LastColldables = new();

        private static bool colliderListRendering;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [Load]
        private static void Load() {
            ilHookPlayerOrigUpdate = new ILHook(typeof(Player).GetMethod("orig_Update"), ModPlayerOrigUpdateEntity);
            On.Monocle.Hitbox.Render += HitboxOnRenderEntity;
            On.Monocle.Circle.Render += CircleOnRender;
            On.Monocle.ColliderList.Render += ColliderListOnRender;
            On.Monocle.EntityList.Update += EntityListOnUpdate;
            On.Celeste.Level.End += LevelOnEnd;
        }

        [Unload]
        private static void Unload() {
            ilHookPlayerOrigUpdate?.Dispose();
            On.Monocle.Hitbox.Render -= HitboxOnRenderEntity;
            On.Monocle.Circle.Render -= CircleOnRender;
            On.Monocle.ColliderList.Render -= ColliderListOnRender;
            On.Monocle.EntityList.Update -= EntityListOnUpdate;
            On.Celeste.Level.End -= LevelOnEnd;
        }

        private static void EntityListOnUpdate(On.Monocle.EntityList.orig_Update orig, EntityList self) {
            LastPositions.Clear();
            LastColldables.Clear();
            orig(self);
        }

        private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
            LastPositions.Clear();
            LastColldables.Clear();
            orig(self);
        }

        private static void ModPlayerOrigUpdateEntity(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCastclass<PlayerCollider>())) {
                ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<PlayerCollider>>(playerCollider => {
                    Entity entity = playerCollider.Entity;

                    if (Manager.UltraFastForwarding || entity == null || !Settings.ShowHitboxes ||
                        Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off) {
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

        private static void ColliderListOnRender(On.Monocle.ColliderList.orig_Render orig, ColliderList self, Camera camera, Color color) {
            colliderListRendering = true;
            DrawLastFrameHitbox(self, color, hitboxColor => orig(self, camera, hitboxColor));
            colliderListRendering = false;
        }

        private static void DrawLastFrameHitbox(Collider self, Color color, Action<Color> invokeOrig) {
            Entity entity = self.Entity;

            if (Manager.UltraFastForwarding
                || !Settings.ShowHitboxes
                || Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off
                || colliderListRendering && self is not ColliderList
                || entity.Get<PlayerCollider>() == null
                || entity.Scene?.Tracker.GetEntity<Player>() == null
                || entity.LoadActualCollidePosition() == null
                || Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append && entity.Position == entity.LoadActualCollidePosition() &&
                entity.Collidable == entity.LoadActualCollidable()
               ) {
                invokeOrig(color);
                return;
            }

            Color lastFrameColor =
                Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append && entity.Position != entity.LoadActualCollidePosition()
                    ? color.Invert()
                    : color;

            if (entity.Collidable && !entity.LoadActualCollidable()) {
                lastFrameColor *= 0.5f;
            } else if (!entity.Collidable && entity.LoadActualCollidable()) {
                lastFrameColor *= 2f;
            }

            if (Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
                if (entity.Position == entity.LoadActualCollidePosition()) {
                    invokeOrig(lastFrameColor);
                    return;
                }

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
            LastPositions[entity] = entity.Position;
        }

        private static Vector2? LoadActualCollidePosition(this Entity entity) {
            if (LastPositions.ContainsKey(entity)) {
                return LastPositions[entity];
            }

            return null;
        }

        private static void SaveActualCollidable(this Entity entity) {
            LastColldables[entity] = entity.Collidable;
        }

        private static bool LoadActualCollidable(this Entity entity) {
            return LastColldables.ContainsKey(entity) && LastColldables[entity];
        }
    }

    // ReSharper disable once UnusedMember.Global
    public enum ActualCollideHitboxType {
        Off,
        Override,
        Append
    }
}