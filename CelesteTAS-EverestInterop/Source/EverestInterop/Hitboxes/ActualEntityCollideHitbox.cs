using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static partial class ActualEntityCollideHitbox {
    private static readonly Dictionary<Entity, Vector2> LastPositions = new();
    private static readonly Dictionary<Entity, bool> LastColldables = new();

    private static bool playerUpdated;
    private static bool dontSaveLastPosition;
    private static bool colliderListRendering;

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController")?.GetMethodInfo("CustomELUpdate") is { } customELUpdate) {
            customELUpdate.IlHook((cursor, _) => cursor.EmitDelegate(Clear));
        }
    }

    [Load]
    private static void Load() {
        typeof(Player).GetMethod("orig_Update").IlHook(ModPlayerOrigUpdateEntity);
        On.Celeste.Player.Update += PlayerOnUpdate;
        On.Monocle.Hitbox.Render += HitboxOnRenderEntity;
        On.Monocle.Circle.Render += CircleOnRender;
        On.Monocle.ColliderList.Render += ColliderListOnRender;
        On.Monocle.EntityList.Update += EntityListOnUpdate;
        On.Celeste.Level.End += LevelOnEnd;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.Update -= PlayerOnUpdate;
        On.Monocle.Hitbox.Render -= HitboxOnRenderEntity;
        On.Monocle.Circle.Render -= CircleOnRender;
        On.Monocle.ColliderList.Render -= ColliderListOnRender;
        On.Monocle.EntityList.Update -= EntityListOnUpdate;
        On.Celeste.Level.End -= LevelOnEnd;
    }

    private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
        dontSaveLastPosition = Manager.FastForwarding || !TasSettings.ShowHitboxes ||
                               TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off || playerUpdated;
        orig(self);
        playerUpdated = true;
    }

    private static void EntityListOnUpdate(On.Monocle.EntityList.orig_Update orig, EntityList self) {
        Clear();
        orig(self);
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        Clear();
        orig(self);
    }

    private static void Clear() {
        playerUpdated = false;
        LastPositions.Clear();
        LastColldables.Clear();
    }

    private static void ModPlayerOrigUpdateEntity(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCastclass<PlayerCollider>())) {
            ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<PlayerCollider>>(SaveEntityPosition);
        }
    }

    private static void SaveEntityPosition(PlayerCollider playerCollider) {
        Entity entity = playerCollider.Entity;

        if (dontSaveLastPosition || entity == null) {
            return;
        }

        entity.SaveActualCollidePosition();
        entity.SaveActualCollidable();
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

        if (Manager.FastForwarding
            || !TasSettings.ShowHitboxes
            || TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off
            || colliderListRendering && self is not ColliderList
            || entity.Get<PlayerCollider>() == null
            || entity.Scene?.Tracker.GetEntity<Player>() == null
            || entity.LoadActualCollidePosition() is not { } actualCollidePosition
            || TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append && entity.Position == actualCollidePosition &&
            entity.Collidable == entity.LoadActualCollidable()
           ) {
            invokeOrig(color);
            return;
        }

        Color lastFrameColor =
            TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append && entity.Position != actualCollidePosition
                ? color.Invert()
                : color;

        if (entity.Collidable && !entity.LoadActualCollidable()) {
            lastFrameColor *= HitboxColor.UnCollidableAlpha;
        } else if (!entity.Collidable && entity.LoadActualCollidable() & HitboxColor.UnCollidableAlpha > 0) {
            lastFrameColor *= 1 / HitboxColor.UnCollidableAlpha;
        }

        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
            if (entity.Position == actualCollidePosition) {
                invokeOrig(lastFrameColor);
                return;
            }

            invokeOrig(color);
        }

        Vector2 currentPosition = entity.Position;

        IEnumerable<PlayerCollider> playerColliders = entity.Components.GetAll<PlayerCollider>().ToArray();
        if (playerColliders.All(playerCollider => playerCollider.Collider != null)) {
            if (playerColliders.Any(playerCollider => playerCollider.Collider == self)) {
                entity.Position = actualCollidePosition;
                invokeOrig(lastFrameColor);
                entity.Position = currentPosition;
            } else {
                invokeOrig(color);
            }
        } else {
            entity.Position = actualCollidePosition;
            invokeOrig(lastFrameColor);
            entity.Position = currentPosition;
        }
    }

    private static void SaveActualCollidePosition(this Entity entity) {
        LastPositions[entity] = entity.Position;
    }

    private static Vector2? LoadActualCollidePosition(this Entity entity) {
        return LastPositions.TryGetValue(entity, out Vector2 result) ? result : null;
    }

    private static void SaveActualCollidable(this Entity entity) {
        LastColldables[entity] = entity.Collidable;
    }

    private static bool LoadActualCollidable(this Entity entity) {
        return LastColldables.TryGetValue(entity, out bool result) && result;
    }
}
