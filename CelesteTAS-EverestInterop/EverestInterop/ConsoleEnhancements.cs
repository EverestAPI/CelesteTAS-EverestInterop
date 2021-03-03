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

namespace TAS.EverestInterop {
    public static class ConsoleEnhancements {
        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;

        private static string clickedEntityInfo = string.Empty;
        private static ButtonState lastButtonState;
        private static readonly List<WeakReference> RequireInspectEntities = new List<WeakReference>();
        private static readonly HashSet<EntityID> RequireInspectEntityIds = new HashSet<EntityID>();
        private static WeakReference<HashSet<Entity>> inspectingEntities;

        public static void Load() {
            // Forced: Add more positions to top-left positioning helper.
            IL.Monocle.Commands.Render += Commands_Render;
            On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            loadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
        }

        public static void Unload() {
            IL.Monocle.Commands.Render -= Commands_Render;
            On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
            origLoadLevelHook?.Dispose();
            loadCustomEntityHook?.Dispose();
            origLoadLevelHook = null;
            loadCustomEntityHook = null;
        }

        private static void Commands_Render(ILContext il) {
            // Hijack string.Format("\n level:       {0}, {1}", xObj, yObj)
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdstr("\n level:       {0}, {1}"),
                i => i.MatchCall(typeof(string), "Format")
            );
            ILCursor c = found[1];
            c.Remove();
            c.EmitDelegate<Func<string, object, object, string>>((text, xObj, yObj) => {
                Level level = Engine.Scene as Level;
                int x = (int) xObj;
                int y = (int) yObj;
                int worldX = (int) Math.Round(x + level.LevelOffset.X);
                int worldY = (int) Math.Round(y + level.LevelOffset.Y);

                MouseState mouseState = Mouse.GetState();
                if (mouseState.RightButton == ButtonState.Pressed) {
                    ClearClickedEntities();
                }

                if (Engine.Instance.IsActive && mouseState.LeftButton == ButtonState.Pressed && lastButtonState == ButtonState.Released) {
                    Entity tempEntity = new Entity {Position = new Vector2(worldX, worldY), Collider = new Hitbox(1, 1)};
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
                    if (clickedEntity != null) {
                        Type type = clickedEntity.GetType();
                        clickedEntityInfo = "\n entity type: ";
                        if (type.Assembly == typeof(Celeste.Celeste).Assembly) {
                            clickedEntityInfo += type.Name;
                        } else {
                            // StartExport uses a comma as a separator, so we can't use comma,
                            // use @ to place it and replace it back with a comma when looking for the type
                            clickedEntityInfo += type.FullName + "@" + type.Assembly.GetName().Name;
                        }

                        if (clickedEntity.LoadEntityData() is EntityData entityData) {
                            clickedEntityInfo += $"\n entity name: {entityData.Name}";
                            clickedEntityInfo += $"\n entity id  : {entityData.ToEntityId()}";
                        }

                        ProcessInspectingEntity(clickedEntity);

                        ("Info of entity to be clicked: " + clickedEntityInfo).Log();
                    } else {
                        clickedEntityInfo = string.Empty;
                    }
                }

                lastButtonState = mouseState.LeftButton;

                return
                    (string.IsNullOrEmpty(clickedEntityInfo) ? string.Empty : clickedEntityInfo) +
                    $"\n world:       {worldX}, {worldY}" +
                    $"\n level:       {x}, {y}";
            });
        }

        private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
            orig(self, camera);

            if (CelesteTasModule.Settings.ShowHitboxes) {
                foreach (Entity entity in Engine.Scene.Entities) {
                    if (inspectingEntities != null && inspectingEntities.TryGetTarget(out HashSet<Entity> entities) && entities.Contains(entity)) {
                        Draw.Point(entity.Position, HitboxColor.EntityColorInversely);
                    }
                }
            }
        }

        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out ILCursor[] results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v && v.VariableType.Name == "EntityData")) {
                    // cursor.Previous.Log();
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

        private static void ProcessInspectingEntity(Entity clickedEntity) {
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
        }

        private static void ClearClickedEntities() {
            RequireInspectEntities.Clear();
            RequireInspectEntityIds.Clear();
            inspectingEntities = null;
        }

        private static void CacheEntityData(Entity entity, EntityData data) {
            entity.SaveEntityData(data);
        }

        public static string GetInspectingEntitiesInfo(string separator = "\n") {
            HashSet<Entity> target = new HashSet<Entity>();
            inspectingEntities = new WeakReference<HashSet<Entity>>(target);
            if (!(Engine.Scene is Level level)) {
                return string.Empty;
            }

            string inspectingInfo = string.Join(separator, RequireInspectEntities.Where(reference => reference.IsAlive).Select(
                reference => {
                    Entity entity = (Entity) reference.Target;
                    target.Add(entity);
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
                    target.Add(entity);
                    return $"{entity.GetType().Name}: {GetPosition(entity)}";
                }));
            }

            return inspectingInfo;
        }

        private static string GetPosition(Entity entity) {
            if (entity is Actor actor) {
                return $"{actor.X + actor.PositionRemainder.X}, {actor.Y + actor.PositionRemainder.Y}";
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