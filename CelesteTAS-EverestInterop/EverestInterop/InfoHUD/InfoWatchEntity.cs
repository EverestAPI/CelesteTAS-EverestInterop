using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public enum WatchEntityType {
        Position,
        DeclaredOnly,
        All
    }

    public static class InfoWatchEntity {
        private static readonly Dictionary<string, IEnumerable<MemberInfo>> CachedMemberInfos = new();

        private static readonly WeakReference<Entity> LastClickedEntity = new(null);

        // TODO FIXME: entity w/o id not work properly after retry or load state
        private static readonly List<WeakReference> RequireWatchEntities = new();
        private static readonly HashSet<UniqueEntityId> RequireWatchUniqueEntityIds = new();
        public static readonly HashSet<Entity> WatchingEntities = new();
        private static AreaKey requireWatchAreaKey;

        private static readonly List<ILHook> ilHooks = new();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [Load]
        private static void Load() {
            On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
            On.Celeste.Level.Begin += LevelOnBegin;
            On.Celeste.Level.End += LevelOnEnd;
            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
            On.Celeste.Player.Added += PlayerOnAdded;
            On.Celeste.Strawberry.ctor += StrawberryOnCtor;
            On.Celeste.StrawberrySeed.ctor += StrawberrySeedOnCtor;
            IL.Celeste.Level.LoadCustomEntity += ModLoadCustomEntity;
            ilHooks.Add(new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel));
        }

        [LoadContent]
        private static void LoadContent() {
            Dictionary<string, string[]> typeMethodNames = new() {
                {"Celeste.SeekerStatue+<>c__DisplayClass3_0, Celeste", new[] {"<.ctor>b__0"}},
                {"Celeste.FireBall, Celeste", new[] {"Added"}},
                {"Celeste.WindAttackTrigger, Celeste", new[] {"OnEnter"}},
                {"Celeste.OshiroTrigger, Celeste", new[] {"OnEnter"}},
                {"Celeste.NPC03_Oshiro_Rooftop, Celeste", new[] {"Added"}},
                {"Celeste.CS03_OshiroRooftop, Celeste", new[] {"OnEnd"}},
                {"Celeste.NPC10_Gravestone, Celeste", new[] {"Added", "Interact"}},
                {"Celeste.CS10_Gravestone, Celeste", new[] {"OnEnd"}},
                {"Celeste.Mod.DJMapHelper.Triggers.OshiroRightTrigger, DJMapHelper", new[] {"OnEnter"}},
                {"Celeste.Mod.DJMapHelper.Triggers.WindAttackLeftTrigger, DJMapHelper", new[] {"OnEnter"}},
                {"Celeste.Mod.RubysEntities.FastOshiroTrigger, RubysEntities", new[] {"OnEnter"}},
                {"FrostHelper.SnowballTrigger, FrostTempleHelper", new[] {"OnEnter"}},
                {"OshiroCaller, FemtoHelper", new[] {"OnHoldable", "OnPlayer"}},
                {"Celeste.Mod.PandorasBox.CloneSpawner, PandorasBox", new[] {"handleClone"}},
            };

            foreach (string typeName in typeMethodNames.Keys) {
                foreach (string methodName in typeMethodNames[typeName]) {
                    if (Type.GetType(typeName)?.GetMethodInfo(methodName) is { } methodInfo) {
                        ilHooks.Add(new ILHook(methodInfo, ModSpawnEntity));
                    }
                }
            }
        }

        [Unload]
        private static void Unload() {
            On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
            On.Celeste.Level.Begin -= LevelOnBegin;
            On.Celeste.Level.End -= LevelOnEnd;
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
            On.Celeste.Player.Added -= PlayerOnAdded;
            On.Celeste.Strawberry.ctor -= StrawberryOnCtor;
            On.Celeste.StrawberrySeed.ctor -= StrawberrySeedOnCtor;
            IL.Celeste.Level.LoadCustomEntity -= ModLoadCustomEntity;
            ilHooks.ForEach(hook => hook.Dispose());
            ilHooks.Clear();
        }

        public static void HandleMouseData(MouseState mouseState, MouseState lastMouseData) {
            if (!Engine.Instance.IsActive) {
                return;
            }

            if (mouseState.RightButton == ButtonState.Pressed && lastMouseData.RightButton == ButtonState.Released) {
                ClearWatchEntities();
            }

            if (mouseState.LeftButton == ButtonState.Pressed && lastMouseData.LeftButton == ButtonState.Released &&
                !IsClickHud(mouseState) && FindClickedEntity(mouseState) is { } entity) {
                AddOrRemoveWatching(entity);
                PrintAllSimpleValues(entity);
            }
        }

        private static bool IsClickHud(MouseState mouseState) {
            Rectangle rectangle = new((int) Settings.InfoPosition.X, (int) Settings.InfoPosition.Y, (int) InfoHud.Size.X, (int) InfoHud.Size.Y);
            return rectangle.Contains(mouseState.X, mouseState.Y);
        }

        private static List<Entity> FindClickedEntities(MouseState mouseState) {
            if (Engine.Scene is Level level) {
                Vector2 mousePosition = new(mouseState.X, mouseState.Y);
                float viewScale = (float) Engine.ViewWidth / Engine.Width;
                Vector2 mouseWorldPosition = level.ScreenToWorld(mousePosition / viewScale).Floor();
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

        public static Entity FindClickedEntity(MouseState mouseState) {
            List<Entity> clickedEntities = FindClickedEntities(mouseState);

            Entity clickedEntity;
            if (LastClickedEntity.TryGetTarget(out Entity lastClicked) && clickedEntities.IndexOf(lastClicked) is int index and >= 0) {
                clickedEntity = clickedEntities[(index + 1) % clickedEntities.Count];
            } else {
                clickedEntity = clickedEntities.FirstOrDefault();
            }

            LastClickedEntity.SetTarget(clickedEntity);
            return clickedEntity;
        }

        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new(il);

            // NPC
            if (cursor.TryGotoNext(MoveType.Before,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()",
                ins => ins.OpCode == OpCodes.Stloc_S
            )) {
                cursor.Index++;
                object entityDataOperand = cursor.Next.Operand;
                while (cursor.TryGotoNext(MoveType.Before,
                    i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference {HasParameters: true} m && m.Parameters.Count == 1 &&
                         m.Parameters[0].ParameterType.Name == "Vector2",
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                    cursor.Index++;
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, entityDataOperand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }

            // DashSwitch.Create and FallingBlock.Create
            cursor.Goto(0);
            if (cursor.TryGotoNext(MoveType.Before,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()",
                ins => ins.OpCode == OpCodes.Stloc_S
            )) {
                cursor.Index++;
                object entityDataOperand = cursor.Next.Operand;
                while (cursor.TryGotoNext(MoveType.Before,
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString().Contains("::Create"),
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                    cursor.Index++;
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, entityDataOperand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }

            // General
            cursor.Goto(0);
            while (cursor.TryGotoNext(MoveType.After,
                i => (i.OpCode == OpCodes.Newobj) && i.Operand is MethodReference {HasParameters: true} m &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out ILCursor[] results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v && v.VariableType.Name == "EntityData")) {
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }
        }

        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new(il);

            if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Level.EntityLoader>("Invoke"))) {
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
            }

            cursor.Goto(0);
            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand.ToString().Contains("::.ctor(Celeste.EntityData"))) {
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
            }
        }

        private static void PlayerOnAdded(On.Celeste.Player.orig_Added orig, Player self, Scene scene) {
            orig(self, scene);

            if (self.GetEntityData() == null && scene is Level level && level.Session.MapData.StartLevel() is { } levelData) {
                self.SetEntityData(new EntityData {
                    ID = 0, Level = levelData, Name = levelData.Name
                });
            }
        }

        private static void StrawberryOnCtor(On.Celeste.Strawberry.orig_ctor orig, Strawberry self, EntityData data, Vector2 offset, EntityID gid) {
            self.SetEntityData(data);
            orig(self, data, offset, gid);
        }

        private static void StrawberrySeedOnCtor(On.Celeste.StrawberrySeed.orig_ctor orig, StrawberrySeed self, Strawberry strawberry,
            Vector2 position, int index, bool ghost) {
            orig(self, strawberry, position, index, ghost);
            if (strawberry.GetEntityData() is { } entityData) {
                EntityData clonedEntityData = entityData.ShallowClone();
                clonedEntityData.ID = clonedEntityData.ID * -100 - index;
                self.SetEntityData(clonedEntityData);
            }
        }

        private static void ModSpawnEntity(ILContext il) {
            ILCursor cursor = new(il);

            if (cursor.TryGotoNext(
                i => i.OpCode == OpCodes.Callvirt && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                if (il.ToString().Contains("ldfld Celeste.SeekerStatue Celeste.SeekerStatue/<>c__DisplayClass3_0::<>4__this")
                    && Type.GetType("Celeste.SeekerStatue+<>c__DisplayClass3_0, Celeste")?.GetFieldInfo("<>4__this") is { } seekerStatue
                ) {
                    cursor.Emit(OpCodes.Ldfld, seekerStatue);
                }

                cursor.EmitDelegate<Action<Entity, Entity>>((spawnedEntity, entity) => {
                    if (entity.GetEntityData() is { } entityData) {
                        EntityData clonedEntityData = entityData.ShallowClone();
                        if (spawnedEntity is FireBall fireBall) {
                            clonedEntityData.ID = clonedEntityData.ID * -100 - fireBall.GetFieldValue<int>("index");
                        } else if (entity is CS03_OshiroRooftop) {
                            clonedEntityData.ID = 2;
                        } else {
                            clonedEntityData.ID *= -1;
                        }

                        spawnedEntity.SetEntityData(clonedEntityData);
                    }
                });
            }
        }

        private static void CacheEntityData(Entity entity, EntityData data) {
            entity.SetEntityData(data);
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

            if (Settings.ShowHitboxes) {
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

        private static void ClearWatchEntities() {
            LastClickedEntity.SetTarget(null);
            RequireWatchEntities.Clear();
            RequireWatchUniqueEntityIds.Clear();
            WatchingEntities.Clear();
            GameInfo.Update();
        }

        public static string GetWatchingEntitiesInfo(string separator = "\n", bool alwaysUpdate = false, int? decimals = null) {
            WatchingEntities.Clear();
            string watchingInfo = string.Empty;
            if (Engine.Scene is not Level level || Settings.InfoWatchEntity == HudOptions.Off && !alwaysUpdate) {
                return string.Empty;
            }

            decimals ??= Settings.CustomInfoDecimals;
            if (RequireWatchEntities.IsNotEmpty()) {
                watchingInfo = string.Join(separator, RequireWatchEntities.Where(reference => reference.IsAlive).Select(
                    reference => {
                        Entity entity = (Entity) reference.Target;
                        WatchingEntities.Add(entity);
                        return GetEntityValues(entity, Settings.InfoWatchEntityType, separator, decimals.Value);
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
                        return GetEntityValues(entity, Settings.InfoWatchEntityType, separator, decimals.Value);
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
            string key = type.FullName + "-" + declaredOnly;

            if (CachedMemberInfos.ContainsKey(key)) {
                return CachedMemberInfos[key];
            } else {
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

                List<MemberInfo> result = new();
                foreach (IGrouping<bool, MemberInfo> grouping in memberInfos.GroupBy(info => type == info.DeclaringType)) {
                    List<MemberInfo> infos = grouping.ToList();
                    infos.Sort((info1, info2) => string.Compare(info1.Name, info2.Name, StringComparison.InvariantCultureIgnoreCase));
                    if (grouping.Key) {
                        result.InsertRange(0, infos);
                    } else {
                        result.AddRange(infos);
                    }
                }

                CachedMemberInfos[key] = result;
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
}