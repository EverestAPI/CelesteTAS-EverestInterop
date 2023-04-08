using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static partial class InfoWatchEntity {
    internal static List<UniqueEntityId> WatchCheckList = new();
    internal static List<UniqueEntityId> WatchMissingList = new();
    internal static EntityWatchingList WatchingList = new();

    [Load]
    private static void LoadTracker() {
        On.Celeste.Level.Begin += Level_Begin;
        On.Celeste.Level.End += Level_End;
        On.Celeste.Level.LoadLevel += Level_LoadLevel;
        On.Monocle.EntityList.UpdateLists += EntityList_UpdateLists;
    }

    [Unload]
    private static void UnloadTracker() {
        On.Celeste.Level.Begin -= Level_Begin;
        On.Celeste.Level.End -= Level_End;
        On.Celeste.Level.LoadLevel -= Level_LoadLevel;
        On.Monocle.EntityList.UpdateLists -= EntityList_UpdateLists;
    }

    private static void ToggleWatching(Entity clickedEntity) {
        // Update last watch chapter
        requireWatchAreaKey = clickedEntity.SceneAs<Level>().Session.Area;

        UniqueEntityId id = new(clickedEntity);

        if (WatchCheckList.Contains(id) && WatchingList.Has(clickedEntity, out var tuple)) {
            // We are watching the entity already, stop watching and remove it from the lists
            WatchMissingList.Remove(id);
            WatchingList.Remove(tuple);
            WatchCheckList.Remove(id);
        } else {
            // We aren't watching the entity, add it to the list and start watching
            WatchCheckList.Add(id);
            WatchingList.Add(id, clickedEntity);
        }

        GameInfo.Update();
    }

    private static void TryRemoveEntity(Entity entity) {
        // Find the watch tuple that corresponds to the entity
        Tuple<UniqueEntityId, WeakReference> tuple = WatchingList.Tuples.Find((watchedTuple) => watchedTuple.Item2.Target is Entity watched && entity.Equals(watched));

        // The entity isn't watched
        if (tuple is null)
            return;

        // If we have the entity id and are tracking it across rooms
        if (WatchCheckList.Contains(tuple.Item1)) {
            // Add it to the missing list, so we can track it in its room later
            WatchMissingList.Add(tuple.Item1);
        }

        WatchingList.Remove(tuple);
    }

    private static void TryAddEntity(Entity entity) {
        UniqueEntityId id = new(entity);

        // The entity is missing, add it to the list
        if (WatchMissingList.Contains(id)) {
            WatchMissingList.Remove(id);
            WatchingList.Add(id, entity);
        }
    }

    public static void RefreshWatchEntities() {
        if (Engine.Scene is not Level level)
            return;

        WatchingList.Clear();
        WatchMissingList = new(WatchCheckList);

        foreach (var entity in level) {
            TryAddEntity(entity);
        }
    }

    public static void ClearWatchEntities(bool update = true, bool clearCheckList = false) {
        LastClickedEntity.SetTarget(null);
        WatchingList.Clear();
        if (clearCheckList) {
            WatchCheckList.Clear();
            WatchMissingList.Clear();
        } else {
            WatchMissingList = new List<UniqueEntityId>(WatchCheckList);
        }
        if (update) GameInfo.Update();
    }

    private static void Level_Begin(On.Celeste.Level.orig_Begin orig, Level self) {
        orig(self);

        if (requireWatchAreaKey != self.Session.Area) {
            ClearWatchEntities(clearCheckList: true);
        }
    }

    private static void Level_End(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        ClearWatchEntities(update: false);
    }

    private static void Level_LoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        foreach (var tuple in WatchingList.Tuples) {
            if (tuple.Item2.Target is not Entity { Scene: not null }) {
                WatchingList.RemoveDelayedQueue(tuple);
            }
        }
        WatchingList.RemoveDelayed();
    }

    private static void EntityList_UpdateLists(On.Monocle.EntityList.orig_UpdateLists orig, EntityList self) {
        if (self.Scene is not Level) {
            orig(self);
            return;
        }

        foreach (var addedEntity in self.toAdd) {
            TryAddEntity(addedEntity);
        }

        foreach (var removedEntity in self.toRemove) {
            TryRemoveEntity(removedEntity);
        }

        orig(self);
    }

    internal class EntityWatchingList {
        public HashSet<UniqueEntityId> Ids = new();
        public List<Tuple<UniqueEntityId, WeakReference>> Tuples = new();
        private List<Tuple<UniqueEntityId, WeakReference>> ToRemove = new();

        public void Remove(UniqueEntityId id) {
            var tuple = Tuples.Find((tuple) => tuple.Item1.Equals(id));
            Remove(tuple);
        }

        public void Remove(Entity entity) {
            var tuple = Tuples.Find((tuple) => entity.Equals(tuple.Item2.Target));
            Remove(tuple);
        }

        public void Remove(Tuple<UniqueEntityId, WeakReference> tuple) {
            if (tuple is null)
                return;
            Tuples.Remove(tuple);
            Ids.Remove(tuple.Item1);
        }

        public void Add(UniqueEntityId id, Entity entity) {
            WeakReference reference = new WeakReference(entity);
            Tuples.Add(new(id, reference));
            Ids.Add(id);
        }

        public bool Has(UniqueEntityId id) {
            return Ids.Contains(id);
        }

        public bool Has(Entity entity, out Tuple<UniqueEntityId, WeakReference> foundTuple) {
            foreach (var tuple in Tuples) {
                if (entity.Equals(tuple.Item2.Target)) {
                    foundTuple = tuple;
                    return true;
                }
            }
            foundTuple = null;
            return false;
        }

        public void RemoveDelayedQueue(Tuple<UniqueEntityId, WeakReference> tuple) {
            ToRemove.Add(tuple);
        }

        public void RemoveDelayed() {
            foreach (var toRemove in ToRemove) {
                Remove(toRemove);
            }
            ToRemove.Clear();
        }

        public void Clear() {
            Ids.Clear();
            ToRemove.Clear();
            Tuples.Clear();
        }
    }

    internal record UniqueEntityId {
        public readonly EntityID? EntityId;
        public readonly Type Type;
        public readonly bool GlobalOrPersistent;

        public UniqueEntityId(Entity entity) {
            Type = entity.GetType();
            GlobalOrPersistent = entity.TagCheck(Tags.Global) || entity.TagCheck(Tags.Persistent) || entity.Get<Holdable>() != null;

            if (entity.GetEntityData() is { } data) {
                EntityId = data.ToEntityId();
            } else {
                // We don't have the entity data to determine exact original room and its identifier
                // Track the entity in current room and don't differentiate using ID
                if (entity.Scene is Level level) {
                    EntityId = new EntityID(entity.SceneAs<Level>().Session.Level, 0);
                } else {
                    EntityId = null;
                }
            }
        }
    }
}