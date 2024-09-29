using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public enum WatchEntityType {
    Position,
    DeclaredOnly,
    All
}

public static class InfoWatchEntity {
    // ReSharper disable UnusedMember.Local
    private record struct MemberKey(Type Type, bool DeclaredOnly) {
        public readonly Type Type = Type;
        public readonly bool DeclaredOnly = DeclaredOnly;
    }
    // ReSharper restore UnusedMember.Local

    private static readonly Dictionary<MemberKey, List<MemberInfo>> CachedMemberInfos = new();

    private static readonly WeakReference<Entity> LastClickedEntity = new(null);

    // TODO FIXME: entity w/o id not work properly after retry or load state
    public static List<WeakReference> RequireWatchEntities = new();
    public static List<WeakReference> SavedRequireWatchEntities = new();
    private static readonly HashSet<UniqueEntityId> RequireWatchUniqueEntityIds = new();
    public static readonly HashSet<Entity> WatchingEntities = new();
    private static AreaKey requireWatchAreaKey;


    [Load]
    private static void Load() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Level.End += LevelOnEnd;
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
        On.Celeste.Level.Begin -= LevelOnBegin;
        On.Celeste.Level.End -= LevelOnEnd;
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
    }

    public static void CheckMouseButtons() {
        if (MouseButtons.Right.Pressed) {
            ClearWatchEntities();
        }

        if (MouseButtons.Left.Pressed && !IsClickHud() && FindClickedEntity() is { } entity) {
            AddOrRemoveWatching(entity);
            PrintAllSimpleValues(entity);
        }
    }

    private static bool IsClickHud() {
        Rectangle rectangle = new((int) TasSettings.InfoPosition.X, (int) TasSettings.InfoPosition.Y, (int) InfoHud.Size.X, (int) InfoHud.Size.Y);
        return rectangle.Contains((int) MouseButtons.Position.X, (int) MouseButtons.Position.Y);
    }

    private static List<Entity> FindClickedEntities() {
        if (Engine.Scene is Level level) {
            Vector2 mouseWorldPosition = level.MouseToWorld(MouseButtons.Position);
            Entity tempEntity = new() {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
            List<Entity> allEntities = level.Entities.Where(entity =>
                entity.GetType() != typeof(Entity)
                && entity is not ParticleSystem).ToList();

            List<Entity> noColliderEntities = allEntities.Where(entity =>
                entity.Collider == null
                && entity.GetEntityData() != null
            ).ToList();

            foreach (Entity entity in noColliderEntities) {
                EntityData data = entity.GetEntityData();
                entity.Collider = new Hitbox(data.Width, data.Height);
            }

            List<Entity> result = allEntities.Where(entity => entity.CollideCheck(tempEntity)).ToList();

            foreach (Entity entity in noColliderEntities) {
                entity.Collider = null;
            }

            // put trigger after entity
            result.Sort((entity1, entity2) => (entity1 is Trigger ? 1 : -1) - (entity2 is Trigger ? 1 : -1));
            return result;
        } else {
            return new List<Entity>();
        }
    }

    public static Entity FindClickedEntity() {
        List<Entity> clickedEntities = FindClickedEntities();

        Entity clickedEntity;
        if (LastClickedEntity.TryGetTarget(out Entity lastClicked) && clickedEntities.IndexOf(lastClicked) is int index and >= 0) {
            clickedEntity = clickedEntities[(index + 1) % clickedEntities.Count];
        } else {
            clickedEntity = clickedEntities.FirstOrDefault();
        }

        LastClickedEntity.SetTarget(clickedEntity);
        return clickedEntity;
    }

    private static void AddOrRemoveWatching(Entity clickedEntity) {
        requireWatchAreaKey = clickedEntity.SceneAs<Level>().Session.Area;
        if (clickedEntity.GetEntityData() is { } entityData) {
            UniqueEntityId uniqueEntityId = new(clickedEntity, entityData);
            if (RequireWatchUniqueEntityIds.Contains(uniqueEntityId)) {
                RequireWatchUniqueEntityIds.Remove(uniqueEntityId);
            } else {
                RequireWatchUniqueEntityIds.Add(uniqueEntityId);
            }
        } else {
            if (RequireWatchEntities.FirstOrDefault(reference => reference.Target == clickedEntity) is { } alreadyAdded) {
                RequireWatchEntities.Remove(alreadyAdded);
            } else {
                RequireWatchEntities.Add(new WeakReference(clickedEntity));
            }
        }

        GameInfo.Update();
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (TasSettings.ShowHitboxes) {
            foreach (Entity entity in Engine.Scene.Entities) {
                if (WatchingEntities.Contains(entity)) {
                    Draw.Point(entity.Position, HitboxColor.EntityColorInversely);
                }
            }
        }
    }

    private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
        orig(self);

        if (self.Session.Area != requireWatchAreaKey) {
            ClearWatchEntities();
        }
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        WatchingEntities.Clear();
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        RequireWatchEntities.ToList().ForEach(reference => {
            if (reference.Target is Entity {Scene: null}) {
                RequireWatchEntities.Remove(reference);
            }
        });
    }

    public static void ClearWatchEntities() {
        LastClickedEntity.SetTarget(null);
        RequireWatchEntities.Clear();
        SavedRequireWatchEntities.Clear();
        RequireWatchUniqueEntityIds.Clear();
        WatchingEntities.Clear();
        GameInfo.Update();
    }

    public static string GetInfo(string separator = "\n", bool alwaysUpdate = false, int? decimals = null) {
        WatchingEntities.Clear();
        string watchingInfo = string.Empty;
        if (Engine.Scene is not Level level || TasSettings.InfoWatchEntity == HudOptions.Off && !alwaysUpdate) {
            return string.Empty;
        }

        decimals ??= TasSettings.CustomInfoDecimals;
        if (RequireWatchEntities.IsNotEmpty()) {
            watchingInfo = string.Join(separator, RequireWatchEntities.Where(reference => reference.IsAlive).Select(
                reference => {
                    Entity entity = (Entity) reference.Target;
                    WatchingEntities.Add(entity);
                    return GetEntityValues(entity, TasSettings.InfoWatchEntityType, separator, decimals.Value);
                }
            ));
        }

        if (RequireWatchUniqueEntityIds.IsNotEmpty()) {
            Dictionary<UniqueEntityId, Entity> matchEntities = GetMatchEntities(level);
            if (matchEntities.IsNotEmpty()) {
                if (watchingInfo.IsNotNullOrEmpty()) {
                    watchingInfo += separator;
                }

                watchingInfo += string.Join(separator, matchEntities.Select(pair => {
                    Entity entity = matchEntities[pair.Key];
                    WatchingEntities.Add(entity);
                    return GetEntityValues(entity, TasSettings.InfoWatchEntityType, separator, decimals.Value);
                }));
            }
        }

        return watchingInfo;
    }

    private static void PrintAllSimpleValues(Entity entity) {
        ("Info of Clicked Entity:\n" + GetEntityValues(entity, WatchEntityType.All)).Log(true);
    }

    private static string GetEntityValues(Entity entity, WatchEntityType watchEntityType, string separator = "\n", int decimals = 2) {
        Type type = entity.GetType();
        string entityId = "";
        if (entity.GetEntityData() is { } entityData) {
            entityId = $"[{entityData.ToEntityId().ToString()}]";
        }

        if (watchEntityType == WatchEntityType.Position) {
            return GetPositionInfo(entity, entityId, decimals);
        }

        List<string> values = GetAllSimpleFields(type, watchEntityType == WatchEntityType.DeclaredOnly).Select(info => {
            object value;
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
                    value = GameInfo.ConvertToFrames(floatValue);
                } else {
                    value = floatValue.ToFormattedString(decimals);
                }
            } else if (value is Vector2 vector2) {
                value = vector2.ToSimpleString(decimals);
            }

            if (separator == "\t" && value != null) {
                value = value.ToString().ReplaceLineBreak(" ");
            }

            return $"{type.Name}{entityId}.{info.Name}: {value}";
        }).ToList();

        values.Insert(0, GetPositionInfo(entity, entityId, decimals));

        return string.Join(separator, values);
    }

    private static string GetPositionInfo(Entity entity, string entityId, int decimals) {
        return $"{entity.GetType().Name}{entityId}: {entity.ToSimplePositionString(decimals)}";
    }

    private static IEnumerable<MemberInfo> GetAllSimpleFields(Type type, bool declaredOnly = false) {
        MemberKey key = new(type, declaredOnly);

        if (CachedMemberInfos.TryGetValue(key, out List<MemberInfo> result)) {
            return result;
        } else {
            CachedMemberInfos[key] = result = new List<MemberInfo>();

            FieldInfo[] fields;
            PropertyInfo[] properties;

            if (declaredOnly) {
                BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                fields = type.GetFields(bindingFlags);
                properties = type.GetProperties(bindingFlags);
            } else {
                fields = type.GetAllFieldInfos().ToArray();
                properties = type.GetAllProperties().ToArray();
            }

            List<MemberInfo> memberInfos = fields.Where(info => info.FieldType.IsSimpleType() && !info.Name.EndsWith("k__BackingField"))
                .Cast<MemberInfo>().ToList();
            List<MemberInfo> propertyInfos = properties.Where(info => info.PropertyType.IsSimpleType()).Cast<MemberInfo>().ToList();
            memberInfos.AddRange(propertyInfos);

            foreach (IGrouping<bool, MemberInfo> grouping in memberInfos.GroupBy(info => type == info.DeclaringType)) {
                List<MemberInfo> infos = grouping.ToList();
                infos.Sort((info1, info2) => string.Compare(info1.Name, info2.Name, StringComparison.InvariantCultureIgnoreCase));
                if (grouping.Key) {
                    result.InsertRange(0, infos);
                } else {
                    result.AddRange(infos);
                }
            }

            return result;
        }
    }

    private static Dictionary<UniqueEntityId, Entity> GetMatchEntities(Level level) {
        Dictionary<UniqueEntityId, Entity> result = new();
        List<Entity> possibleEntities = new();
        HashSet<Type> possibleTypes = new();

        string currentRoom = level.Session.Level;
        foreach (UniqueEntityId id in RequireWatchUniqueEntityIds.Where(id => id.GlobalOrPersistent || id.EntityId.Level == currentRoom)) {
            possibleTypes.Add(id.Type);
        }

        if (possibleTypes.IsEmpty()) {
            return result;
        }

        if (possibleTypes.All(type => level.Tracker.Entities.ContainsKey(type))) {
            foreach (Type type in possibleTypes) {
                possibleEntities.AddRange(level.Tracker.Entities[type]);
            }
        } else {
            possibleEntities.AddRange(level.Entities.Where(entity => possibleTypes.Contains(entity.GetType())));
        }

        foreach (Entity entity in possibleEntities) {
            if (entity.GetEntityData() is not { } entityData) {
                continue;
            }

            UniqueEntityId uniqueEntityId = new(entity, entityData);
            if (RequireWatchUniqueEntityIds.Contains(uniqueEntityId) && !result.ContainsKey(uniqueEntityId)) {
                result[uniqueEntityId] = entity;

                if (result.Count == RequireWatchUniqueEntityIds.Count) {
                    return result;
                }
            }
        }

        return result;
    }
}

internal record UniqueEntityId {
    public readonly EntityID EntityId;
    public readonly bool GlobalOrPersistent;
    public readonly Type Type;

    public UniqueEntityId(Entity entity, EntityData entityData) {
        Type = entity.GetType();
        GlobalOrPersistent = entity.TagCheck(Tags.Global) || entity.TagCheck(Tags.Persistent) || entity.Get<Holdable>() != null;
        EntityId = entityData.ToEntityId();
    }
}
