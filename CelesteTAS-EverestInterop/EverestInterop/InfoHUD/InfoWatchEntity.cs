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
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public enum WatchEntityTypes {
        Position,
        DeclaredOnly,
        All
    }

    public static class InfoWatchEntity {
        private static readonly Dictionary<string, IEnumerable<MemberInfo>> CachedMemberInfos = new();

        private static readonly List<WeakReference> RequireWatchEntities = new();
        private static readonly HashSet<UniqueEntityId> RequireWatchUniqueEntityIds = new();
        private static readonly HashSet<Entity> WatchingEntities = new();
        private static AreaKey requireWatchAreaKey;

        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
            On.Celeste.Level.Begin += LevelOnBegin;
            On.Celeste.Level.End += LevelOnEnd;
            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            loadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
        }

        public static void Unload() {
            On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
            On.Celeste.Level.Begin -= LevelOnBegin;
            On.Celeste.Level.End -= LevelOnEnd;
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
            origLoadLevelHook?.Dispose();
            loadCustomEntityHook?.Dispose();
            origLoadLevelHook = null;
            loadCustomEntityHook = null;
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
                WatchingEntity(entity);
                PrintAllSimpleValues(entity);
            }
        }

        private static bool IsClickHud(MouseState mouseState) {
            Rectangle rectangle = new((int) Settings.InfoPosition.X, (int) Settings.InfoPosition.Y, (int) InfoHud.Size.X, (int) InfoHud.Size.Y);
            return rectangle.Contains(mouseState.X, mouseState.Y);
        }

        public static Entity FindClickedEntity(MouseState mouseState) {
            if (Engine.Scene is Level level) {
                Vector2 mousePosition = new(mouseState.X, mouseState.Y);
                if (SaveData.Instance?.Assists.MirrorMode == true) {
                    mousePosition.X = Engine.ViewWidth - mousePosition.X;
                }

                Camera camera = level.Camera;
                int viewScale =
                    (int) Math.Round(Engine.Instance.GraphicsDevice.PresentationParameters.BackBufferWidth / (float) camera.Viewport.Width);
                Vector2 mouseWorldPosition = camera.ScreenToCamera((mousePosition / viewScale).Floor());
                Entity tempEntity = new() {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
                Entity clickedEntity = level.Entities.Where(entity =>
                        (Hotkeys.WatchTrigger.Check || entity is not Trigger)
                        && entity.GetType() != typeof(Entity)
                        && entity is not RespawnTargetTrigger
                        && entity is not LookoutBlocker
                        && entity is not Killbox
                        && entity is not Water
                        && entity is not WaterFall
                        && entity is not BigWaterfall
                        && entity is not PlaybackBillboard
                        && entity is not ParticleSystem)
                    .FirstOrDefault(entity => entity.CollideCheck(tempEntity));
                return clickedEntity;
            } else {
                return null;
            }
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

        private static void CacheEntityData(Entity entity, EntityData data) {
            entity.SaveEntityData(data);
        }

        private static void WatchingEntity(Entity clickedEntity) {
            requireWatchAreaKey = clickedEntity.SceneAs<Level>().Session.Area;
            if (clickedEntity.LoadEntityData() is { } entityData) {
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
            RequireWatchEntities.Clear();
            RequireWatchUniqueEntityIds.Clear();
            WatchingEntities.Clear();
            GameInfo.Update();
        }

        public static string GetWatchingEntitiesInfo(string separator = "\n", bool export = false) {
            WatchingEntities.Clear();
            string watchingInfo = string.Empty;
            if (Engine.Scene is not Level level || Settings.InfoWatchEntity == HudOptions.Off && !export) {
                return string.Empty;
            }

            if (RequireWatchEntities.IsNotEmpty()) {
                watchingInfo = string.Join(separator, RequireWatchEntities.Where(reference => reference.IsAlive).Select(
                    reference => {
                        Entity entity = (Entity) reference.Target;
                        WatchingEntities.Add(entity);
                        return GetEntityValues(entity, Settings.InfoWatchEntityType, separator);
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
                        return GetEntityValues(entity, Settings.InfoWatchEntityType, separator);
                    }));
                }
            }

            return watchingInfo;
        }

        private static void PrintAllSimpleValues(Entity entity) {
            ("Info of Clicked Entity:\n" + GetEntityValues(entity, WatchEntityTypes.All)).Log(true);
        }

        private static string GetEntityValues(Entity entity, WatchEntityTypes watchEntityType, string separator = "\n") {
            Type type = entity.GetType();
            string entityId = "";
            if (entity.LoadEntityData() is { } entityData) {
                entityId = $"[{entityData.ToEntityId().ToString()}]";
            }

            if (watchEntityType == WatchEntityTypes.Position) {
                return GetPositionInfo(entity, entityId);
            }

            List<string> values = GetAllSimpleFields(type, watchEntityType == WatchEntityTypes.DeclaredOnly).Select(info => {
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
                        value = Settings.RoundCustomInfo ? $"{floatValue:F2}" : $"{floatValue:F12}";
                    }
                } else if (value is Vector2 vector2) {
                    value = vector2.ToSimpleString(Settings.RoundCustomInfo);
                }

                if (separator == "\t" && value != null) {
                    value = value.ToString().ReplaceLineBreak(" ");
                }

                return $"{type.Name}{entityId}.{info.Name}: {value}";
            }).ToList();

            values.Insert(0, GetPositionInfo(entity, entityId));

            return string.Join(separator, values);
        }

        private static string GetPositionInfo(Entity entity, string entityId) {
            return $"{entity.GetType().Name}{entityId}: {entity.ToSimplePositionString(Settings.RoundCustomInfo)}";
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

                List<MemberInfo> memberInfos = fields.Where(info => {
                    Type t = info.FieldType;
                    return (t.IsPrimitive || t.IsEnum || t == typeof(Vector2)) && !info.Name.EndsWith("k__BackingField");
                }).Cast<MemberInfo>().ToList();

                List<MemberInfo> propertyInfos = properties.Where(
                    info => {
                        Type t = info.PropertyType;
                        return t.IsPrimitive || t.IsEnum || t == typeof(Vector2);
                    }).Cast<MemberInfo>().ToList();
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
                if (entity.LoadEntityData() is not { } entityData) {
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
            GlobalOrPersistent = entity.TagCheck(Tags.Global) || entity.TagCheck(Tags.Persistent);
            EntityId = entityData.ToEntityId();
        }
    }
}