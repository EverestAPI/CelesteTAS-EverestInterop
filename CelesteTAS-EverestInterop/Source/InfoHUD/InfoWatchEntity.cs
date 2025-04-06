using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using System.Text;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
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

    /// Checks if the specified entity is intended to be watched
    [PublicAPI]
    public static bool IsWatching(Entity entity) {
        return CurrentlyWatchedEntities.Value.Contains(entity)
               || (entity.GetEntityData() is { } entityData && WatchedEntityIds.Contains(new UniqueEntityId(entity, entityData)));
    }

    private static readonly Dictionary<MemberKey, List<MemberInfo>> CachedMemberInfos = new();
    private static readonly WeakReference<Entity?> LastClickedEntity = new(null);

    // Store the entities which should be watched in the current level
    // Fallback to using weak references when a unique ID is not available
    private static AreaKey currentAreaKey;
    internal static List<WeakReference> WatchedEntities = [];
    internal static List<WeakReference> WatchedEntities_Save = []; // Used for save-states

    private static readonly HashSet<UniqueEntityId> WatchedEntityIds = [];

    /// Entities which are actively watched for the current frame
    internal static LazySet<Entity> CurrentlyWatchedEntities = new(PopulateWatchedEntities);

    internal static LazyValue<string> InfoPosition = new(() => QueryInfo(WatchEntityType.Position, TasSettings.WatchEntityDecimals));
    internal static LazyValue<string> InfoDeclaredOnly = new(() => QueryInfo(WatchEntityType.DeclaredOnly, TasSettings.WatchEntityDecimals));
    internal static LazyValue<string> InfoAll = new(() => QueryInfo(WatchEntityType.All, TasSettings.WatchEntityDecimals));
    internal static LazyValue<string> InfoAllExact = new(() => QueryInfo(WatchEntityType.All, GameSettings.MaxDecimals));

    private static readonly StringBuilder infoBuilder = new();
    private static string QueryInfo(WatchEntityType watchType, int decimals, string separator = "\n") {
        infoBuilder.Clear();
        foreach (var entity in CurrentlyWatchedEntities.Value) {
            infoBuilder.AppendEntityValues(entity, watchType, decimals, separator);
        }
        return infoBuilder.ToString();
    }

    private static void PopulateWatchedEntities(HashSet<Entity> entities) {
        if (Engine.Scene is not Level level) {
            return;
        }

        entities.AddRange(
            WatchedEntities
                .Where(reference => reference.IsAlive)
                .Select(reference => (Entity)reference.Target!)
        );
        entities.AddRange(ResolveEntityIds(level).Values);
    }

    [Events.PreEntityListUpdate]
    private static void PreEntityListUpdate(EntityList list) {
        // Don't check if specifically a watched entity is removed, as it's not worth
        if (list.toAdd.Count != 0 || list.toRemove.Count != 0) {
            CurrentlyWatchedEntities.Reset();
        }
    }

    [UpdateMeta]
    private static void UpdateMeta() {
        if (!Hotkeys.InfoHud.Check) {
            return;
        }

        if (MouseInput.Right.Pressed) {
            ClearWatchEntities();
        }

        CenterCamera.AdjustCamera();
        if (MouseInput.Left.Pressed && !WindowManager.IsMouseOverWindow() && FindClickedEntity() is { } entity) {
            AddOrRemoveWatching(entity);
            PrintAllSimpleValues(entity);
        }
        CenterCamera.RestoreCamera();
    }

    /// Resolves the entity, which the mouse is currently over
    internal static Entity? FindClickedEntity() {
        var clickedEntities = FindEntities()
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

        static IEnumerable<Entity> FindEntities() {
            if (Engine.Scene is not Level level) {
                yield break;
            }

            var worldPosition = level.MouseToWorldPosition(MouseInput.Position);
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
        }

        static bool IgnoreEntity(Entity entity) {
            return entity.GetType() == typeof(Entity)
                   || entity is ParticleSystem
                   || TasHelperInterop.GetUnimportantTriggers().Contains(entity);
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

        CurrentlyWatchedEntities.Reset();
        InfoPosition.Reset();
        InfoDeclaredOnly.Reset();
        InfoAll.Reset();
        InfoAllExact.Reset();
    }

    internal static void ClearWatchEntities() {
        LastClickedEntity.SetTarget(null);
        WatchedEntities.Clear();
        WatchedEntities_Save.Clear();
        WatchedEntityIds.Clear();

        CurrentlyWatchedEntities.Reset();
        InfoPosition.Reset();
        InfoDeclaredOnly.Reset();
        InfoAll.Reset();
        InfoAllExact.Reset();

        ClearWatching?.Invoke();
    }

    [Obsolete("No longer needed", error: true)]
    public static bool ForceUpdateInfo = false; // for TasHelper.AutoWatchEntity

    [Load]
    private static void Load() {
        On.Celeste.Level.Begin += On_Level_Begin;
        On.Celeste.Level.End += On_Level_End;
        On.Celeste.Level.LoadLevel += On_Level_LoadLevel;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Begin -= On_Level_Begin;
        On.Celeste.Level.End -= On_Level_End;
        On.Celeste.Level.LoadLevel -= On_Level_LoadLevel;
    }

    [Events.PostDebugRender]
    private static void PostDebugRender(Scene scene) {
        if (!TasSettings.ShowHitboxes) {
            return;
        }

        // Highlight currently watched entities
        foreach (var entity in scene.Entities) {
            if (CurrentlyWatchedEntities.Value.Contains(entity)) {
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
        CurrentlyWatchedEntities.Reset();
    }
    private static void On_Level_LoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        // Clean-up
        WatchedEntities.RemoveAll(reference => !reference.IsAlive || reference.Target is Entity { Scene: null });
    }

    private static void PrintAllSimpleValues(Entity entity) {
        var builder = new StringBuilder();
        builder.AppendLine("Info of Clicked Entity:");
        builder.AppendEntityValues(entity, WatchEntityType.All, TasSettings.WatchEntityDecimals);

        builder.ToString().Log(string.Empty, TasSettings.InfoWatchEntityLogToConsole);
    }

    /// Formats all member values into a multi-line string
    private static void AppendEntityValues(this StringBuilder builder, Entity entity, WatchEntityType watchEntityType, int decimals, string separator = "\n") {
        if (watchEntityType == WatchEntityType.None) {
            return;
        }

        var entityType = entity.GetType();

        string entityId = "";
        if (entity.GetEntityData() is { } entityData) {
            entityId = $"[{entityData.ToEntityId().ToString()}]";
        }

        string entityPrefix = $"{entityType.Name}{entityId}";
        builder.Append(entityPrefix);
        builder.Append(": ");
        builder.Append(entity.FormatPosition(decimals));

        if (watchEntityType == WatchEntityType.Position) {
            return;
        }

        foreach (var member in ResolveAllSimpleMembers(entityType, watchEntityType == WatchEntityType.DeclaredOnly)) {
            object? value;
            try {
                value = member switch {
                    FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                    PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                    _ => null
                };
            } catch {
                value = string.Empty;
            }

            if (value is float floatValue) {
                if (member.Name.EndsWith("Timer")) {
                    value = $"{floatValue.ToCeilingFrames()}f ({floatValue.FormatValue(decimals)})" ;
                } else {
                    value = floatValue.FormatValue(decimals);
                }
            } else if (value is Vector2 vectorValue) {
                value = vectorValue.FormatValue(decimals);
            }

            if (separator == "\t" && value != null) {
                value = value.ToString()?.ReplaceLineBreak(" ");
            }

            builder.Append(separator);
            builder.Append(entityPrefix);
            builder.Append('.');
            builder.Append(member.Name);
            builder.Append(": ");
            builder.Append(value);
        }
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
