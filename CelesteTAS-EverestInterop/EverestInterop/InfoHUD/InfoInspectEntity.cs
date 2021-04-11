using System;
using System.Collections.Generic;
using System.Linq;
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
    public static class InfoInspectEntity {
        private static readonly List<WeakReference> RequireInspectEntities = new List<WeakReference>();
        private static readonly HashSet<EntityID> RequireInspectEntityIds = new HashSet<EntityID>();
        private static readonly HashSet<Entity> InspectingEntities = new HashSet<Entity>();
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
                FindClickedEntity(mouseState) is Entity entity) {
                InspectingEntity(entity);
            }
        }

        public static Entity FindClickedEntity(MouseState mouseState) {
            Vector2 mousePosition = new Vector2(mouseState.X, mouseState.Y);
            if (Engine.Scene is Level level) {
                Camera cam = level.Camera;
                int viewScale = (int) Math.Round(Engine.Instance.GraphicsDevice.PresentationParameters.BackBufferWidth / (float) cam.Viewport.Width);
                Vector2 mouseWorldPosition = mousePosition;
                mouseWorldPosition = (mouseWorldPosition / viewScale).Floor();
                mouseWorldPosition = cam.ScreenToCamera(mouseWorldPosition);
                Entity tempEntity = new Entity {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
                Entity clickedEntity = level.Entities.Where(entity => !(entity is Trigger)
                                                                      && entity.GetType() != typeof(Entity)
                                                                      && !(entity is LookoutBlocker)
                                                                      && !(entity is Killbox)
                                                                      && !(entity is WindController)
                                                                      && !(entity is Water)
                                                                      && !(entity is WaterFall)
                                                                      && !(entity is BigWaterfall)
                                                                      && !(entity is PlaybackBillboard)
                                                                      && !(entity is ParticleSystem))
                    .FirstOrDefault(entity => entity.CollideCheck(tempEntity));
                return clickedEntity;
            } else {
                return null;
            }
        }

        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (cursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()")) {
                cursor.Index++;
                object entityDataOperand = cursor.Next.Operand;
                while (cursor.TryGotoNext(MoveType.Before,
                    i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters && m.Parameters.Count == 1 &&
                         m.Parameters[0].ParameterType.Name == "Vector2",
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                    cursor.Index++;
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, entityDataOperand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }

            cursor.Goto(0);
            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out ILCursor[] results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v && v.VariableType.Name == "EntityData")) {
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }
        }

        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new ILCursor(il);

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
            if (clickedEntity.LoadEntityData() is EntityData entityData) {
                EntityID entityId = entityData.ToEntityId();
                if (RequireInspectEntityIds.Contains(entityId)) {
                    RequireInspectEntityIds.Remove(entityId);
                } else {
                    RequireInspectEntityIds.Add(entityId);
                }
            } else {
                if (RequireInspectEntities.FirstOrDefault(reference => reference.Target == clickedEntity) is WeakReference alreadyAdded) {
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

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerintro, bool isfromloader) {
            orig(self, playerintro, isfromloader);

            RequireInspectEntities.ToList().ForEach(reference => {
                if (reference.Target is Entity entity && entity.Scene == null) {
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
            if (!(Engine.Scene is Level level)) {
                return string.Empty;
            }

            string inspectingInfo = string.Join(separator, RequireInspectEntities.Where(reference => reference.IsAlive).Select(
                reference => {
                    Entity entity = (Entity) reference.Target;
                    InspectingEntities.Add(entity);
                    return $"{entity.GetType().Name}: {GetPosition(entity)}";
                }
            ));

            Dictionary<EntityID, Entity> allEntities = GetAllEntities(level);
            EntityID[] entityIds = RequireInspectEntityIds.Where(id => allEntities.ContainsKey(id)).ToArray();
            if (entityIds.IsNotEmpty()) {
                if (inspectingInfo.IsNotNullOrEmpty()) {
                    inspectingInfo += separator;
                }

                inspectingInfo += string.Join(separator, entityIds.Select(id => {
                    Entity entity = allEntities[id];
                    InspectingEntities.Add(entity);
                    return $"{entity.GetType().Name}: {GetPosition(entity)}";
                }));
            }

            return inspectingInfo;
        }

        private static string GetPosition(Entity entity) {
            if (entity is Actor actor) {
                return $"{actor.X + actor.PositionRemainder.X:F2}, {actor.Y + actor.PositionRemainder.Y:F2}";
            } else {
                return $"{entity.X}, {entity.Y}";
            }
        }

        private static Dictionary<EntityID, Entity> GetAllEntities(Level level) {
            Dictionary<EntityID, Entity> result = new Dictionary<EntityID, Entity>();

            Entity[] entities = level.Entities.FindAll<Entity>().Where(entity => !(entity is Trigger)).ToArray();
            foreach (Entity entity in entities) {
                if (!(entity.LoadEntityData() is EntityData entityData)) {
                    continue;
                }

                EntityID entityId = entityData.ToEntityId();
                if (!result.ContainsKey(entityId)) {
                    result[entityId] = entity;
                }
            }

            return result;
        }
    }
}