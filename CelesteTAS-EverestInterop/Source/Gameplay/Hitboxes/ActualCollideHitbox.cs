using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes;

/// Stores and displays the state of entity colliders, during the player update instead of at the end of the frame
public static class ActualCollideHitbox {
    private static readonly Color ActualPlayerHitboxColor = Color.Red.Invert();
    private static readonly Color ActualPlayerHurtboxColor = Color.Lime.Invert();

    private static readonly Dictionary<Entity, Vector2> LastPositions = new();
    private static readonly Dictionary<Entity, bool> LastCollidables = new();

    /// Special cases for entity which check additional variables, other than entity.Collidable
    private static readonly Dictionary<Type, Func<Entity, bool>> CollidableHandlers = new();

    /// Actual-collide hitboxes are disabled, while they aren't used
    [PublicAPI]
    public static bool Disabled => !TasSettings.ShowHitboxes || TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off || Manager.FastForwarding;

    private static bool playerUpdated;
    private static bool colliderListRendering;

    /// Checks if the entity is collidable, accounting for special cases for some specific entities
    [PublicAPI]
    public static bool IsCollidable(this Entity entity) {
        var entityType = entity.GetType();
        if (CollidableHandlers.TryGetValue(entityType, out var sameClassHandler)) {
            return sameClassHandler(entity);
        }
        if (CollidableHandlers.FirstOrNull(entry => entityType.IsSameOrSubclassOf(entry.Key)) is { } subClassHandler) {
            return subClassHandler.Value(entity);
        }

        return entity.Collidable;
    }

    /// Resets any stored collider data
    [PublicAPI]
    public static void Clear() {
        playerUpdated = false;
        LastPositions.Clear();
        LastCollidables.Clear();
    }

    /// Returns the position of the entity, while the Player updated
    [PublicAPI]
    public static Vector2? LoadActualCollidePosition(this Entity entity) {
        return LastPositions.TryGetValue(entity, out Vector2 result) ? result : null;
    }
    /// Returns the actual collidability of the entity, while the Player updated
    [PublicAPI]
    public static bool? LoadActualCollidable(this Entity entity) {
        return LastCollidables.TryGetValue(entity, out bool result) ? result : null;
    }

    /// Stores the state of the entity's collider as it was during Player.Update
    [PublicAPI]
    public static void StoreActualColliderState(Entity? entity) {
        // If a PlayerCollider is checked multiple times, only use the first one
        if (entity == null || playerUpdated || Disabled) {
            return;
        }

        LastPositions[entity] = entity.Position;
        LastCollidables[entity] = entity.IsCollidable();
    }

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController")?.GetMethodInfo("CustomELUpdate") is { } customELUpdate) {
            customELUpdate.IlHook((cursor, _) => cursor.EmitDelegate(Clear));
        }

        CollidableHandlers.Clear();
        CollidableHandlers.Add(typeof(Lightning), e => e.Collidable && !((Lightning)e).disappearing);
        if (ModUtils.GetType("ChronoHelper", "Celeste.Mod.ChronoHelper.Entities.DarkLightning") is { } chronoLightningType) {
            // Not a subclass of Lightning
            var disappearingField = chronoLightningType.GetFieldInfo("disappearing");
            CollidableHandlers.Add(chronoLightningType, e => {
                if (!e.Collidable) {
                    return false;
                }
                if (disappearingField.GetValue(e) is bool b) {
                    return !b;
                }
                return true;
            });
        }
        if (ModUtils.GetType("Glyph", "Celeste.Mod.AcidHelper.Entities.AcidLightning") is { } acidLightningType) {
            // Subclass of Lightning, but has it's own "toggleOffset" and "disappearing"
            var disappearingField = acidLightningType.GetFieldInfo("disappearing");
            CollidableHandlers.Add(acidLightningType, e => {
                if (!e.Collidable) {
                    return false;
                }
                if (disappearingField.GetValue(e) is bool b) {
                    return !b;
                }
                return true;
            });
        }
    }

    [Load]
    private static void Load() {
        typeof(Player).GetMethodInfo("orig_Update")!.IlHook(IL_Player_origUpdate);

        On.Celeste.Player.Update += On_Player_Update;
        On.Monocle.EntityList.Update += On_EntityList_Update;
        On.Celeste.Player.DebugRender += On_Player_DebugRender;
        On.Monocle.Hitbox.Render += On_Hitbox_Render;
        On.Monocle.Circle.Render += On_Circle_Render;
        On.Monocle.ColliderList.Render += On_ColliderList_Render;
        On.Celeste.Level.End += On_Level_End;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.Update -= On_Player_Update;
        On.Monocle.EntityList.Update -= On_EntityList_Update;
        On.Celeste.Player.DebugRender -= On_Player_DebugRender;
        On.Monocle.Hitbox.Render -= On_Hitbox_Render;
        On.Monocle.Circle.Render -= On_Circle_Render;
        On.Monocle.ColliderList.Render -= On_ColliderList_Render;
        On.Celeste.Level.End -= On_Level_End;
    }

    private static void IL_Player_origUpdate(ILContext il) {
        var cursor = new ILCursor(il);

        // Store player
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt(typeof(Tracker).GetMethodInfo(nameof(Tracker.GetComponents)).MakeGenericMethod(typeof(PlayerCollider))))) {
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate(StoreActualColliderState);
        } else {
            "Failed to apply patch for storing player state during Update for actual-collide-hitboxes".Log(LogLevel.Warn);
        }

        // Store entities with PlayerCollider
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCastclass<PlayerCollider>())) {
            cursor.EmitDup();
            cursor.EmitCall(typeof(Component).GetPropertyInfo(nameof(Component.Entity)).GetMethod!);
            cursor.EmitDelegate(StoreActualColliderState);
        } else {
            "Failed to apply patch for storing entity state during Update for actual-collide-hitboxes".Log(LogLevel.Warn);
        }
    }

    [PublicAPI]
    [Obsolete("Use StoreActualColliderState instead")]
    public static void StoreCollider(Entity? entity) => StoreActualColliderState(entity);

    private static void On_Player_Update(On.Celeste.Player.orig_Update orig, Player self) {
        orig(self);
        playerUpdated = true;
    }

    private static void On_EntityList_Update(On.Monocle.EntityList.orig_Update orig, EntityList self) {
        Clear(); // Prepare for this Update() chain
        orig(self);
    }

    private static void On_Player_DebugRender(On.Celeste.Player.orig_DebugRender orig, Player player, Camera camera) {
        if (Disabled
            || player.Scene is Level { Transitioning: true }
            || player.LoadActualCollidePosition() is not { } actualCollidePosition
            || player.Position == actualCollidePosition
        ) {
            orig(player, camera);
            return;
        }

        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Override) {
            DrawActualPlayerHitbox(player, actualCollidePosition);
            return;
        }

        orig(player, camera);
        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
            DrawActualPlayerHitbox(player, actualCollidePosition);
        }
    }

    private static void On_Hitbox_Render(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        DrawActualCollider(self, color, hitboxColor => orig(self, camera, hitboxColor));
    }
    private static void On_Circle_Render(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
        DrawActualCollider(self, color, hitboxColor => orig(self, camera, hitboxColor));
    }
    private static void On_ColliderList_Render(On.Monocle.ColliderList.orig_Render orig, ColliderList self, Camera camera, Color color) {
        colliderListRendering = true; // Prevent child components from rendering themselves
        DrawActualCollider(self, color, hitboxColor => orig(self, camera, hitboxColor));
        colliderListRendering = false;
    }

    private static void On_Level_End(On.Celeste.Level.orig_End orig, Level self) {
        Clear();
        orig(self);
    }

    private static void DrawActualCollider(Collider self, Color color, Action<Color> invokeOrig) {
        var entity = self.Entity;

        if (entity == null || entity is Player || Disabled
            || colliderListRendering && self is not ColliderList
            || entity.LoadActualCollidePosition() is not { } actualCollidePosition
            || entity.Position == actualCollidePosition
        ) {
            invokeOrig(color);
            return;
        }

        var actualColliderColor =
            TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append
                ? color.Invert()
                : color;

        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
            invokeOrig(color); // Render original
        }

        var currentPosition = entity.Position;

        // If the entity has a PlayerCollider with a custom hitbox, only show the actual hitbox for those and not the main Collider
        var playerColliders = entity.Components.GetAll<PlayerCollider>().ToArray();
        if (playerColliders.All(playerCollider => playerCollider.Collider != null)) {
            if (playerColliders.Any(playerCollider => playerCollider.Collider == self)) {
                entity.Position = actualCollidePosition;
                invokeOrig(actualColliderColor);
                entity.Position = currentPosition;
            } else {
                invokeOrig(color);
            }
        } else {
            entity.Position = actualCollidePosition;
            invokeOrig(actualColliderColor);
            entity.Position = currentPosition;
        }
    }

    private static void DrawActualPlayerHitbox(Player player, Vector2 hitboxPosition) {
        var origPosition = player.Position;
        var origCollider = player.Collider;

        player.Position = hitboxPosition;
        Draw.HollowRect(origCollider, TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append ? ActualPlayerHitboxColor : Color.Red);

        player.Collider = player.hurtbox;
        Draw.HollowRect(player.hurtbox, TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append ? ActualPlayerHurtboxColor : Color.Lime);

        player.Collider = origCollider;
        player.Position = origPosition;
    }
}
