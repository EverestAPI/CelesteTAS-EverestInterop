using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class EntityDataHelper {
    private static Dictionary<Entity, EntityData> cachedEntityData = new();
    private static Dictionary<Entity, EntityData> savedEntityData = new();
    private static readonly List<ILHook> ilHooks = new();

    [Load]
    private static void Load() {
        On.Celeste.Player.Added += PlayerOnAdded;
        On.Celeste.Strawberry.ctor += StrawberryOnCtor;
        On.Celeste.StrawberrySeed.ctor += StrawberrySeedOnCtor;
        IL.Celeste.Level.LoadCustomEntity += ModLoadCustomEntity;
        On.Celeste.Level.End += LevelOnEnd;
        On.Monocle.Entity.Removed += EntityOnRemoved;
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
        On.Celeste.Player.Added -= PlayerOnAdded;
        On.Celeste.Strawberry.ctor -= StrawberryOnCtor;
        On.Celeste.StrawberrySeed.ctor -= StrawberrySeedOnCtor;
        IL.Celeste.Level.LoadCustomEntity -= ModLoadCustomEntity;
        On.Celeste.Level.End -= LevelOnEnd;
        On.Monocle.Entity.Removed -= EntityOnRemoved;
        ilHooks.ForEach(hook => hook.Dispose());
        ilHooks.Clear();
    }

    private static void SetEntityData(this Entity entity, EntityData data) {
        if (entity != null) {
            cachedEntityData.Remove(entity);
            cachedEntityData.Add(entity, data);
        }
    }

    public static EntityData GetEntityData(this Entity entity) {
        return entity != null && cachedEntityData.TryGetValue(entity, out EntityData data) ? data : null;
    }

    public static void OnSave() {
        savedEntityData = cachedEntityData.DeepCloneShared();
    }

    public static void OnLoad() {
        cachedEntityData = savedEntityData.DeepCloneShared();
    }

    public static void OnClear() {
        savedEntityData.Clear();
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        if (self.Entities.IsEmpty()) {
            cachedEntityData.Clear();
        }
    }

    private static void EntityOnRemoved(On.Monocle.Entity.orig_Removed orig, Entity self, Scene scene) {
        orig(self, scene);
        cachedEntityData.Remove(self);
    }

    public static EntityID ToEntityId(this EntityData entityData) {
        return new(entityData.Level.Name, entityData.ID);
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
                && ModUtils.VanillaAssembly.GetType("Celeste.SeekerStatue+<>c__DisplayClass3_0")?.GetFieldInfo("<>4__this") is { } seekerStatue
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
}