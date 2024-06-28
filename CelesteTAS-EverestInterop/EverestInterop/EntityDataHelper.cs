using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class EntityDataHelper {
    public static Dictionary<Entity, EntityData> CachedEntityData = new();

    private static readonly Lazy<EntityData> Ch6FlyFeatherData = new(() => new EntityData() {
        Name = "infiniteStar", ID = -1, Level = AreaData.Get(6).Mode[0].MapData.StartLevel(), Position = new Vector2(88, 256),
        Values = new Dictionary<string, object> {{"shielded", false}, {"singleUse", false}}
    });

    [Load]
    private static void Load() {
        On.Celeste.Player.Added += PlayerOnAdded;
        On.Celeste.Strawberry.ctor += StrawberryOnCtor;
        On.Celeste.StrawberrySeed.ctor += StrawberrySeedOnCtor;
        IL.Celeste.Level.LoadCustomEntity += ModLoadCustomEntity;
        On.Celeste.Level.End += LevelOnEnd;
        On.Monocle.Entity.Removed += EntityOnRemoved;
        On.Celeste.FlyFeather.ctor_Vector2_bool_bool += FlyFeatherOnCtor_Vector2_bool_bool;
        typeof(Level).GetMethod("orig_LoadLevel").IlHook(ModOrigLoadLevel);
    }

    [Initialize]
    private static void Initialize() {
        Dictionary<Type, string[]> typeMethodNames = new() {
            {typeof(SeekerStatue).GetNestedType("<>c__DisplayClass3_0", BindingFlags.NonPublic), new[] {"<.ctor>b__0"}},
            {typeof(FireBall), new[] {"Added"}},
            {typeof(WindAttackTrigger), new[] {"OnEnter"}},
            {typeof(OshiroTrigger), new[] {"OnEnter"}},
            {typeof(NPC03_Oshiro_Rooftop), new[] {"Added"}},
            {typeof(CS03_OshiroRooftop), new[] {"OnEnd"}},
            {typeof(NPC10_Gravestone), new[] {"Added", "Interact"}},
            {typeof(CS10_Gravestone), new[] {"OnEnd"}},
        };

        if (ModUtils.GetType("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.OshiroRightTrigger") is { } oshiroRightTrigger) {
            typeMethodNames.Add(oshiroRightTrigger, new[] {"OnEnter"});
        }

        if (ModUtils.GetType("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.WindAttackLeftTrigger") is { } windAttackLeftTrigger) {
            typeMethodNames.Add(windAttackLeftTrigger, new[] {"OnEnter"});
        }

        if (ModUtils.GetType("Monika's D-Sides", "Celeste.Mod.RubysEntities.FastOshiroTrigger") is { } fastOshiroTrigger) {
            typeMethodNames.Add(fastOshiroTrigger, new[] {"OnEnter"});
        }

        if (ModUtils.GetType("FrostHelper", "FrostHelper.SnowballTrigger") is { } snowballTrigger) {
            typeMethodNames.Add(snowballTrigger, new[] {"OnEnter"});
        }

        if (ModUtils.GetType("FemtoHelper", "OshiroCaller") is { } oshiroCaller) {
            typeMethodNames.Add(oshiroCaller, new[] {"OnHoldable", "OnPlayer"});
        }

        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.CloneSpawner") is { } cloneSpawner) {
            typeMethodNames.Add(cloneSpawner, new[] {"handleClone"});
        }

        foreach (Type type in typeMethodNames.Keys) {
            if (type == null) {
                continue;
            }

            foreach (string methodName in typeMethodNames[type]) {
                if (type.GetMethodInfo(methodName) is { } methodInfo) {
                    methodInfo.IlHook(ModSpawnEntity);
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
        On.Celeste.FlyFeather.ctor_Vector2_bool_bool -= FlyFeatherOnCtor_Vector2_bool_bool;
    }

    private static void SetEntityData(this Entity entity, EntityData data) {
        if (entity != null) {
            CachedEntityData[entity] = data;
        }
    }

    public static EntityData GetEntityData(this Entity entity) {
        return entity != null && CachedEntityData.TryGetValue(entity, out EntityData data) ? data : null;
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        if (self.Entities.IsEmpty()) {
            CachedEntityData.Clear();
        }
    }

    private static void EntityOnRemoved(On.Monocle.Entity.orig_Removed orig, Entity self, Scene scene) {
        orig(self, scene);
        CachedEntityData.Remove(self);
    }

    private static void FlyFeatherOnCtor_Vector2_bool_bool(On.Celeste.FlyFeather.orig_ctor_Vector2_bool_bool orig, FlyFeather self, Vector2 position,
        bool shielded, bool singleUse) {
        orig(self, position, shielded, singleUse);

        if (Engine.Scene.GetSession() is { } session && session.Area.ToString() == "6" && session.LevelData.Name == "start" &&
            position == new Vector2(88, 256)) {
            self.SetEntityData(Ch6FlyFeatherData.Value);
        }
    }

    public static EntityID ToEntityId(this EntityData entityData) {
        return new(entityData.Level?.Name ?? "None", entityData.ID);
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
                ID = 0, Level = levelData, Name = "player"
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

            cursor.EmitDelegate<Action<Entity, Entity>>(SetCustomEntityData);
        }
    }

    private static void SetCustomEntityData(Entity spawnedEntity, Entity entity) {
        if (entity.GetEntityData() is { } entityData) {
            EntityData clonedEntityData = entityData.ShallowClone();
            if (spawnedEntity is FireBall fireBall) {
                clonedEntityData.ID = clonedEntityData.ID * -100 - fireBall.index;
            } else if (entity is CS03_OshiroRooftop) {
                clonedEntityData.ID = 2;
            } else {
                clonedEntityData.ID *= -1;
            }

            spawnedEntity.SetEntityData(clonedEntityData);
        }
    }

    private static void CacheEntityData(Entity entity, EntityData data) {
        entity.SetEntityData(data);
    }
}