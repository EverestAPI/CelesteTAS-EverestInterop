using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public enum InspectEntityTypes {
        Position,
        DeclaredOnly,
        All
    }

    public static class InfoInspectEntity {
        private static readonly Dictionary<string, IEnumerable<MemberInfo>> CachedMemberInfos = new();
        private static readonly Regex NewLineRegex = new(@"\r\n?|\n", RegexOptions.Compiled);

        private static readonly PropertyInfo ActorExactPosition = typeof(Actor).GetPropertyInfo("ExactPosition");
        private static readonly FieldInfo EntityPosition = typeof(Entity).GetFieldInfo("Position");

        private static readonly List<WeakReference> RequireInspectEntities = new();
        private static readonly HashSet<string> RequireInspectEntityIds = new();
        private static readonly HashSet<Entity> InspectingEntities = new();
        private static AreaKey requireInspectAreaKey;

        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;

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
                ClearInspectEntities();
            }

            if (mouseState.LeftButton == ButtonState.Pressed && lastMouseData.LeftButton == ButtonState.Released &&
                FindClickedEntity(mouseState) is { } entity) {
                InspectingEntity(entity);
                PrintAllSimpleValues(entity);
            }
        }

        public static Entity FindClickedEntity(MouseState mouseState) {
            Vector2 mousePosition = new(mouseState.X, mouseState.Y);
            if (Engine.Scene is Level level) {
                Camera cam = level.Camera;
                int viewScale = (int) Math.Round(Engine.Instance.GraphicsDevice.PresentationParameters.BackBufferWidth / (float) cam.Viewport.Width);
                Vector2 mouseWorldPosition = mousePosition;
                mouseWorldPosition = (mouseWorldPosition / viewScale).Floor();
                mouseWorldPosition = cam.ScreenToCamera(mouseWorldPosition);
                Entity tempEntity = new() {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
                Entity clickedEntity = level.Entities.Where(entity =>
                        (!CelesteTasModule.Settings.InfoIgnoreTriggerWhenClickEntity || entity is not Trigger)
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
            if (cursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()")) {
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
            if (cursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()")) {
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

        private static void InspectingEntity(Entity clickedEntity) {
            requireInspectAreaKey = clickedEntity.SceneAs<Level>().Session.Area;
            if (clickedEntity.LoadEntityData() is { } entityData) {
                string uniqueId = entityData.ToUniqueId();
                if (RequireInspectEntityIds.Contains(uniqueId)) {
                    RequireInspectEntityIds.Remove(uniqueId);
                } else {
                    RequireInspectEntityIds.Add(uniqueId);
                }
            } else {
                if (RequireInspectEntities.FirstOrDefault(reference => reference.Target == clickedEntity) is { } alreadyAdded) {
                    RequireInspectEntities.Remove(alreadyAdded);
                } else {
                    RequireInspectEntities.Add(new WeakReference(clickedEntity));
                }
            }

            GameInfo.Update();
        }

        private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
            orig(self, camera);

            if (CelesteTasModule.Settings.ShowHitboxes) {
                foreach (Entity entity in Engine.Scene.Entities) {
                    if (InspectingEntities.Contains(entity)) {
                        Draw.Point(entity.Position, HitboxColor.EntityColorInversely);
                    }
                }
            }
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
            orig(self);

            if (self.Session.Area != requireInspectAreaKey) {
                ClearInspectEntities();
            }
        }

        private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
            orig(self);
            InspectingEntities.Clear();
        }

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
            orig(self, playerIntro, isFromLoader);

            RequireInspectEntities.ToList().ForEach(reference => {
                if (reference.Target is Entity {Scene: null}) {
                    RequireInspectEntities.Remove(reference);
                }
            });
        }

        private static void ClearInspectEntities() {
            RequireInspectEntities.Clear();
            RequireInspectEntityIds.Clear();
            InspectingEntities.Clear();
            GameInfo.Update();
        }

        public static string GetInspectingEntitiesInfo(string separator = "\n") {
            InspectingEntities.Clear();
            if (Engine.Scene is not Level level) {
                return string.Empty;
            }

            string inspectingInfo = string.Join(separator, RequireInspectEntities.Where(reference => reference.IsAlive).Select(
                reference => {
                    Entity entity = (Entity) reference.Target;
                    InspectingEntities.Add(entity);
                    return GetEntityValues(entity, CelesteTasModule.Settings.InfoInspectEntityType);
                }
            ));

            Dictionary<string, Entity> allEntities = GetAllEntities(level);
            string[] entityIds = RequireInspectEntityIds.Where(id => allEntities.ContainsKey(id)).ToArray();
            if (entityIds.IsNotEmpty()) {
                if (inspectingInfo.IsNotNullOrEmpty()) {
                    inspectingInfo += separator;
                }

                inspectingInfo += string.Join(separator, entityIds.Select(id => {
                    Entity entity = allEntities[id];
                    InspectingEntities.Add(entity);
                    return GetEntityValues(entity, CelesteTasModule.Settings.InfoInspectEntityType);
                }));
            }

            return inspectingInfo;
        }

        private static void PrintAllSimpleValues(Entity entity) {
            ("Info of Clicked Entity:\n" + GetEntityValues(entity, InspectEntityTypes.All)).Log(true);
        }

        private static string GetEntityValues(Entity entity, InspectEntityTypes inspectEntityType) {
            Type type = entity.GetType();
            string entityId = "";
            if (entity.LoadEntityData() is { } entityData) {
                entityId = $"[{entityData.ToEntityId().ToString()}]";
            }

            if (inspectEntityType == InspectEntityTypes.Position) {
                return GetPositionInfo(entity, entityId);
            }

            List<string> values = GetAllSimpleFields(type, inspectEntityType == InspectEntityTypes.DeclaredOnly).Select(info => {
                object value = info switch {
                    FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                    PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                    _ => null
                };

                if (value is float floatValue) {
                    value = $"{(int) 60f / Engine.TimeRateB * floatValue:F0}";
                } else if (value is Vector2 vector2) {
                    value = $"{vector2.X}, {vector2.Y}";
                }

                return $"{type.Name}{entityId}.{info.Name}: {value}";
            }).ToList();

            return string.Join("\n", values);
        }

        private static string GetPositionInfo(Entity entity, string entityId) {
            if (entity is Actor actor) {
                return $"{entity.GetType().Name}{entityId}: {actor.X + actor.PositionRemainder.X:F2}, {actor.Y + actor.PositionRemainder.Y:F2}";
            } else {
                return $"{entity.GetType().Name}{entityId}: {entity.X}, {entity.Y}";
            }
        }

        private static IEnumerable<MemberInfo> GetAllSimpleFields(Type type, bool declaredOnly = false) {
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (declaredOnly) {
                bindingFlags |= BindingFlags.DeclaredOnly;
            }

            string key = type.FullName + "-" + bindingFlags;

            if (CachedMemberInfos.ContainsKey(key)) {
                return CachedMemberInfos[key];
            } else {
                List<MemberInfo> memberInfos = type
                    .GetFields(bindingFlags).Where(info => {
                        Type t = info.FieldType;
                        return (t.IsPrimitive || t.IsEnum || t == typeof(Vector2)) && !info.Name.EndsWith("k__BackingField");
                    }).Cast<MemberInfo>().ToList();
                List<MemberInfo> propertyInfos = type
                    .GetProperties(bindingFlags).Where(
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

                MemberInfo positionMemberInfo = type.IsSubclassOf(typeof(Actor)) ? ActorExactPosition : EntityPosition;
                if (!declaredOnly) {
                    result.Remove(positionMemberInfo);
                }

                result.Insert(0, positionMemberInfo);

                CachedMemberInfos[key] = result;
                return result;
            }
        }

        private static Dictionary<string, Entity> GetAllEntities(Level level) {
            Dictionary<string, Entity> result = new();

            Entity[] entities = level.Entities.FindAll<Entity>()
                .Where(entity => !CelesteTasModule.Settings.InfoIgnoreTriggerWhenClickEntity || entity is not Trigger).ToArray();
            foreach (Entity entity in entities) {
                if (entity.LoadEntityData() is not { } entityData) {
                    continue;
                }

                string uniqueId = entityData.ToUniqueId();
                if (!result.ContainsKey(uniqueId)) {
                    result[uniqueId] = entity;
                }
            }

            return result;
        }
    }
}