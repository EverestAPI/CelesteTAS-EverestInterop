using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.EverestInterop.InfoHUD;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.InfoHUD;


/// Displays information about specific entities inside the Info HUD
public static class InfoWatchEntity {
    private readonly record struct MemberKey(Type Type, bool DeclaredOnly);

    private record UniqueEntityId {
        public readonly EntityID EntityId;
        public readonly bool GlobalOrPersistent;
        public readonly Type Type;

        public UniqueEntityId(Entity entity, EntityData entityData) {
            Type = entity.GetType();
            GlobalOrPersistent = entity.TagCheck(Tags.Global) || entity.TagCheck(Tags.Persistent) || entity.Get<Holdable>() != null;
            EntityId = entityData.ToEntityId();
        }
    }

    /// Called when an entity has been added to the watch list
    [PublicAPI]
    public static event Action<Entity>? StartWatching;

    /// Called when an entity has been removed from the watch list
    [PublicAPI]
    public static event Action<Entity>? StopWatching;

    /// Called when the watch list has been cleared
    [PublicAPI]
    public static event Action? ClearWatching;

    private static readonly Dictionary<MemberKey, List<MemberInfo>> CachedMemberInfos = new();
    private static readonly WeakReference<Entity?> LastClickedEntity = new(null);

    // Store the entities which should be watched in the current level
    // Fallback to using weak references when a unique ID is not available
    private static AreaKey currentAreaKey;
    internal static List<WeakReference> WatchedEntities = [];
    internal static List<WeakReference> WatchedEntities_Save = []; // Used for save-states

    private static readonly HashSet<UniqueEntityId> WatchedEntityIds = [];

    /// Entities which are actively watched for the current frame
    internal static readonly HashSet<Entity> CurrentlyWatchedEntities = [];

    [PublicAPI]
    public static bool IsWatching(Entity entity) {
        return CurrentlyWatchedEntities.Contains(entity) || (entity.GetEntityData() is EntityData entityData && WatchedEntityIds.Contains(new UniqueEntityId(entity, entityData)));
    }

    internal static void CheckMouseButtons() {
        if (MouseInput.Right.Pressed) {
            ClearWatchEntities();
        }

        if (MouseInput.Left.Pressed && !MouseOverHud() && FindClickedEntity() is { } entity) {
            AddOrRemoveWatching(entity);
            PrintAllSimpleValues(entity);
        }
    }

    private static bool MouseOverHud() {
        var hudRect = new Rectangle(
            (int) TasSettings.InfoPosition.X, (int) TasSettings.InfoPosition.Y,
            (int) InfoHud.Size.X,             (int) InfoHud.Size.Y);

        return hudRect.Contains((int) MouseInput.Position.X, (int) MouseInput.Position.Y);
    }

    /// Resolves the entity, which the mouse is currently over
    internal static Entity? FindClickedEntity() {
        var clickedEntities = FindEntitiesAt(MouseInput.Position)
            // Sort triggers after entities
            .Sort((a, b) => (a is Trigger ? 1 : -1) - (b is Trigger ? 1 : -1))
            .ToArray();

        Entity? clickedEntity;
        if (LastClickedEntity.TryGetTarget(out var lastClicked) && Array.IndexOf(clickedEntities, lastClicked) is var index and >= 0) {
            // Cycle through when clicking multiple times
            clickedEntity = clickedEntities[(index + 1) % clickedEntities.Length];
        } else {
            clickedEntity = clickedEntities.FirstOrDefault();
        }

        LastClickedEntity.SetTarget(clickedEntity);
        return clickedEntity;
    }

    /// Resolves all entities which overlay with the screen position
    private static IEnumerable<Entity> FindEntitiesAt(Vector2 screenPosition) {
        if (Engine.Scene is not Level level) {
            yield break;
        }

        var worldPosition = level.MouseToWorld(screenPosition);
        foreach (var entity in level.Entities.Where(e => !IgnoreEntity(e))) {
            if (entity.Collider == null) {
                // Attempt to reconstruct collider from entity data
                if (entity.GetEntityData() is { } data) {
                    entity.Collider = new Hitbox(data.Width, data.Height);

                    if (entity.CollidePoint(worldPosition)) {
                        yield return entity;
                    }

                    entity.Collider = null;
                }

                continue;
            }

            if (entity.CollidePoint(worldPosition)) {
                yield return entity;
            }
        }

        yield break;

        static bool IgnoreEntity(Entity entity) {
            return entity.GetType() == typeof(Entity)
                   || entity is ParticleSystem
                   || HitboxSimplified.HideHitbox(entity);
        }
    }

    private static void AddOrRemoveWatching(Entity clickedEntity) {
        currentAreaKey = clickedEntity.SceneAs<Level>().Session.Area;

        if (clickedEntity.GetEntityData() is { } entityData) {
            var uniqueId = new UniqueEntityId(clickedEntity, entityData);
            if (!WatchedEntityIds.Add(uniqueId)) {
                StopWatching?.Invoke(clickedEntity);
                WatchedEntityIds.Remove(uniqueId);
            } else {
                StartWatching?.Invoke(clickedEntity);
            }
        } else {
            if (WatchedEntities.FirstOrDefault(reference => reference.Target == clickedEntity) is { } alreadyAdded) {
                StopWatching?.Invoke(clickedEntity);
                WatchedEntities.Remove(alreadyAdded);
            } else {
                WatchedEntities.Add(new WeakReference(clickedEntity));
                StartWatching?.Invoke(clickedEntity);
            }
        }

        GameInfo.Update();
    }

    internal static void ClearWatchEntities() {
        LastClickedEntity.SetTarget(null);
        WatchedEntities.Clear();
        WatchedEntities_Save.Clear();
        WatchedEntityIds.Clear();
        CurrentlyWatchedEntities.Clear();
        GameInfo.Update();

        ClearWatching?.Invoke();
    }

    internal static string GetInfo(WatchEntityType watchEntityType, string separator = "\n", bool alwaysUpdate = false, int? decimals = null) {
        CurrentlyWatchedEntities.Clear();
        if (Engine.Scene is not Level level || !TasSettings.WatchEntity && !alwaysUpdate) {
            return string.Empty;
        }

        decimals ??= TasSettings.CustomInfoDecimals;

        var allEntities =
            WatchedEntities
                .Where(reference => reference.IsAlive)
                .Select(reference => (Entity) reference.Target!)
                .Concat(ResolveEntityIds(level).Values);

        return string.Join(separator,
            allEntities
                .Select(entity => {
                    CurrentlyWatchedEntities.Add(entity);
                    return GetEntityValues(entity, watchEntityType, separator, decimals.Value);
                }));
    }

    [PublicAPI]
    public static bool ForceUpdateInfo = false; // for TasHelper.AutoWatchEntity

    internal static void UpdateInfo(string separator = "\n", int? decimals = null) {
        CurrentlyWatchedEntities.Clear();
        GameInfo.HudWatchingInfo = string.Empty;
        GameInfo.StudioWatchingInfo = string.Empty;
        if (Engine.Scene is not Level level || !TasSettings.WatchEntity && !ForceUpdateInfo) {
            return;
        }

        decimals ??= TasSettings.CustomInfoDecimals;

        var allEntities =
            WatchedEntities
                .Where(reference => reference.IsAlive)
                .Select(reference => (Entity)reference.Target!)
                .Concat(ResolveEntityIds(level).Values);

        CurrentlyWatchedEntities.AddRange(allEntities);

        if (!TasSettings.HudWatchEntity) {
            GameInfo.HudWatchingInfo = string.Empty;
        } else {
            GameInfo.HudWatchingInfo = string.Join(separator,
            allEntities
                .Select(entity => GetEntityValues(entity, TasSettings.InfoWatchEntityHudType, separator, decimals.Value))
            );
        }

        if (!TasSettings.StudioWatchEntity || !Communication.CommunicationWrapper.Connected) {
            GameInfo.StudioWatchingInfo = string.Empty;
        } else if (TasSettings.InfoWatchEntityStudioType == TasSettings.InfoWatchEntityHudType) {
            GameInfo.StudioWatchingInfo = GameInfo.HudWatchingInfo;
        } else {
            GameInfo.StudioWatchingInfo = string.Join(separator,
            allEntities
                .Select(entity => GetEntityValues(entity, TasSettings.InfoWatchEntityStudioType, separator, decimals.Value))
            );
        }
    }

    [Load]
    private static void Load() {
        On.Monocle.EntityList.DebugRender += On_EntityList_DebugRender;
        On.Celeste.Level.Begin += On_Level_Begin;
        On.Celeste.Level.End += On_Level_End;
        On.Celeste.Level.LoadLevel += On_Level_LoadLevel;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.EntityList.DebugRender -= On_EntityList_DebugRender;
        On.Celeste.Level.Begin -= On_Level_Begin;
        On.Celeste.Level.End -= On_Level_End;
        On.Celeste.Level.LoadLevel -= On_Level_LoadLevel;
    }

    private static void On_EntityList_DebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes) {
            return;
        }

        // Highlight currently watched entities
        foreach (var entity in self) {
            if (CurrentlyWatchedEntities.Contains(entity)) {
                Draw.Point(entity.Position, HitboxColor.EntityColorInversely);
            }
        }
    }

    private static void On_Level_Begin(On.Celeste.Level.orig_Begin orig, Level self) {
        orig(self);

        if (self.Session.Area != currentAreaKey) {
            // Entity IDs are only unique per area, so need to clear them
            ClearWatchEntities();
        }
    }
    private static void On_Level_End(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        CurrentlyWatchedEntities.Clear();
    }
    private static void On_Level_LoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        // Clean-up
        WatchedEntities.RemoveAll(reference => !reference.IsAlive || reference.Target is Entity { Scene: null });
    }

    private static void PrintAllSimpleValues(Entity entity) {
        ("Info of Clicked Entity:\n" + GetEntityValues(entity, WatchEntityType.All)).Log(string.Empty, TasSettings.InfoWatchEntityLogToConsole);
    }

    /// Formats all member values into a multi-line string
    private static string GetEntityValues(Entity entity, WatchEntityType watchEntityType, string separator = "\n", int decimals = 2) {
        if (watchEntityType == WatchEntityType.None) {
            return "";
        }

        var entityType = entity.GetType();

        string entityId = "";
        if (entity.GetEntityData() is { } entityData) {
            entityId = $"[{entityData.ToEntityId().ToString()}]";
        }

        string entityPrefix = $"{entityType.Name}{entityId}";
        string positionInfo = $"{entityPrefix}: {entity.ToSimplePositionString(decimals)}";

        if (watchEntityType == WatchEntityType.Position) {
            return positionInfo;
        }

        List<string> values = [positionInfo];
        values.AddRange(ResolveAllSimpleMembers(entityType, watchEntityType == WatchEntityType.DeclaredOnly).Select(info => {
            object? value;
            try {
                value = info switch {
                    FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                    PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                    _ => null
                };
            } catch {
                value = string.Empty;
            }

            if (value is float floatValue) {
                if (info.Name.EndsWith("Timer")) {
                    value = $"{GameInfo.ConvertToFrames(floatValue)}f ({floatValue.ToFormattedString(decimals)})" ;
                } else {
                    value = floatValue.ToFormattedString(decimals);
                }
            } else if (value is Vector2 vector2) {
                value = vector2.ToSimpleString(decimals);
            }

            if (separator == "\t" && value != null) {
                value = value.ToString()?.ReplaceLineBreak(" ");
            }

            return $"{entityPrefix}.{info.Name}: {value}";
        }));

        return string.Join(separator, values);
    }

    /// Resolves all members with a "simple" type (see <see cref="TAS.Utils.TypeExtensions.IsSimpleType"/>)
    private static IEnumerable<MemberInfo> ResolveAllSimpleMembers(Type type, bool declaredOnly = false) {
        var key = new MemberKey(type, declaredOnly);

        if (CachedMemberInfos.TryGetValue(key, out var result)) {
            return result;
        }

        CachedMemberInfos[key] = result = [];

        FieldInfo[] fields;
        PropertyInfo[] properties;
        if (declaredOnly) {
            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            fields = type.GetFields(bindingFlags);
            properties = type.GetProperties(bindingFlags);
        } else {
            fields = type.GetAllFieldInfos().ToArray();
            properties = type.GetAllPropertyInfos().ToArray();
        }

        List<MemberInfo> memberInfos = [];
        memberInfos.AddRange(fields.Where(info => info.FieldType.IsSimpleType() && !info.Name.EndsWith("k__BackingField")));
        memberInfos.AddRange(properties.Where(info => info.PropertyType.IsSimpleType()));

        foreach (var grouping in memberInfos.GroupBy(info => type == info.DeclaringType)) {
            var infos = grouping
                .Sort((infoA, infoB) => string.Compare(infoA.Name, infoB.Name, StringComparison.InvariantCultureIgnoreCase));

            if (grouping.Key) {
                // Place declared members at the top
                result.InsertRange(0, infos);
            } else {
                result.AddRange(infos);
            }
        }

        return result;
    }

    /// Tries to resolve all entity instances for the currently watched entity IDs
    private static Dictionary<UniqueEntityId, Entity> ResolveEntityIds(Level level) {
        Dictionary<UniqueEntityId, Entity> result = new();
        string currentRoom = level.Session.Level;

        var possibleTypes = WatchedEntityIds
            .Where(id => id.GlobalOrPersistent || id.EntityId.Level == currentRoom)
            .Select(id => id.Type)
            .ToHashSet();

        if (possibleTypes.IsEmpty()) {
            return result;
        }

        List<Entity> possibleEntities = [];
        if (possibleTypes.All(type => level.Tracker.Entities.ContainsKey(type))) {
            foreach (var type in possibleTypes) {
                possibleEntities.AddRange(level.Tracker.Entities[type]);
            }
        } else {
            possibleEntities.AddRange(level.Entities.Where(entity => possibleTypes.Contains(entity.GetType())));
        }

        foreach (var entity in possibleEntities) {
            if (entity.GetEntityData() is not { } entityData) {
                continue;
            }

            var uniqueId = new UniqueEntityId(entity, entityData);
            if (!WatchedEntityIds.Contains(uniqueId) || !result.TryAdd(uniqueId, entity)) {
                continue;
            }

            if (result.Count == WatchedEntityIds.Count) {
                return result;
            }
        }

        return result;
    }
}
