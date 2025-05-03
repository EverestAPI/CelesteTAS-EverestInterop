using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class FastForwardBoost {
    private static Type? creditsType;

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder")?.GetMethodInfo("Update") is { } updateMethod) {
            updateMethod.IlHook(SkipUpdateMethod);
        }

        creditsType = ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Cutscenes.CS_Credits");
        creditsType?.GetMethodInfo("Level_OnLoadEntity")?.IlHook(HookSjCsCredits);
    }

    private static void HookSjCsCredits(ILCursor ilCursor, ILContext ilContext) {
        // CS_Credits credits = level.Entities.ToAdd.OfType<CS_Credits>().FirstOrDefault();
        // to
        // CS_Credits credits = ReduceLinq(level);
        if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Callvirt,
                ins => ins.OpCode == OpCodes.Callvirt,
                ins => ins.OpCode == OpCodes.Call,
                ins => ins.OpCode == OpCodes.Call && ins.Operand.ToString()!.Contains("Enumerable::FirstOrDefault")
            )) {
            ilCursor.RemoveRange(4).EmitDelegate(ReduceLinq);
        }
    }

    private static Entity? ReduceLinq(Level level) {
        foreach (Entity entity in level.Entities.ToAdd) {
            if (entity.GetType() == creditsType) {
                return entity;
            }
        }

        return null;
    }

    [Load]
    private static void Load() {
        // cause desync
        // https://discord.com/channels/403698615446536203/519281383164739594/1061696803772321923
        // IL.Celeste.DustGraphic.Update += SkipUpdateMethod;

        On.Monocle.Tracker.Initialize += TrackerOnInitialize;
        On.Celeste.BackdropRenderer.Update += BackdropRendererOnUpdate;
        On.Celeste.SoundEmitter.Update += SoundEmitterOnUpdate;
        IL.Celeste.ReflectionTentacles.Update += SkipUpdateMethod;
        IL.Monocle.ParticleSystem.Update += SkipUpdateMethod;
        IL.Celeste.Decal.Update += SkipUpdateMethod;
        IL.Celeste.FloatingDebris.Update += SkipUpdateMethod;
        IL.Celeste.AnimatedTiles.Update += SkipUpdateMethod;
        IL.Celeste.Water.Surface.Update += SkipUpdateMethod;
        IL.Celeste.LavaRect.Update += SkipUpdateMethod;
        IL.Celeste.CliffsideWindFlag.Update += SkipUpdateMethod;
        IL.Celeste.CrystalStaticSpinner.UpdateHue += SkipUpdateMethod;
        IL.Celeste.SeekerBarrierRenderer.Update += SkipUpdateMethod;
        IL.Celeste.HiresSnow.Update += SkipUpdateMethod;
        IL.Celeste.Snow3D.Update += SkipUpdateMethod;
        IL.Celeste.AutoSplitterInfo.Update += SkipUpdateMethod;
        IL.Celeste.LightningRenderer.Update += LightningRendererOnUpdate;
        IL.Celeste.SeekerBarrier.Update += SeekerBarrierOnUpdate;
        IL.Monocle.Engine.OnSceneTransition += IgnoreGcCollect;
        IL.Celeste.Level.Reload += IgnoreGcCollect;
        Everest.Events.Input.OnInitialize += InputOnOnInitialize;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Tracker.Initialize -= TrackerOnInitialize;
        On.Celeste.BackdropRenderer.Update -= BackdropRendererOnUpdate;
        On.Celeste.SoundEmitter.Update -= SoundEmitterOnUpdate;
        IL.Celeste.ReflectionTentacles.Update -= SkipUpdateMethod;
        IL.Monocle.ParticleSystem.Update -= SkipUpdateMethod;
        IL.Celeste.Decal.Update -= SkipUpdateMethod;
        IL.Celeste.FloatingDebris.Update -= SkipUpdateMethod;
        IL.Celeste.AnimatedTiles.Update -= SkipUpdateMethod;
        IL.Celeste.Water.Surface.Update -= SkipUpdateMethod;
        IL.Celeste.DustGraphic.Update -= SkipUpdateMethod;
        IL.Celeste.LavaRect.Update -= SkipUpdateMethod;
        IL.Celeste.CliffsideWindFlag.Update -= SkipUpdateMethod;
        IL.Celeste.CrystalStaticSpinner.UpdateHue -= SkipUpdateMethod;
        IL.Celeste.SeekerBarrierRenderer.Update -= SkipUpdateMethod;
        IL.Celeste.HiresSnow.Update -= SkipUpdateMethod;
        IL.Celeste.Snow3D.Update -= SkipUpdateMethod;
        IL.Celeste.AutoSplitterInfo.Update -= SkipUpdateMethod;
        IL.Celeste.LightningRenderer.Update -= LightningRendererOnUpdate;
        IL.Celeste.SeekerBarrier.Update -= SeekerBarrierOnUpdate;
        IL.Monocle.Engine.OnSceneTransition -= IgnoreGcCollect;
        IL.Celeste.Level.Reload -= IgnoreGcCollect;
        Everest.Events.Input.OnInitialize -= InputOnOnInitialize;
    }

#pragma warning disable CS0612
    private static void TrackerOnInitialize(On.Monocle.Tracker.orig_Initialize orig) {
        orig();
        AddTypeToTracker(typeof(TextMenu));
        AddTypeToTracker(typeof(PlayerSeeker));
        AddTypeToTracker(typeof(PlayerDeadBody));
        AddTypeToTracker(typeof(LockBlock));
        AddTypeToTracker(typeof(KeyboardConfigUI), typeof(ModuleSettingsKeyboardConfigUI));
        AddTypeToTracker(typeof(ButtonConfigUI), typeof(ModuleSettingsButtonConfigUI));
    }
#pragma warning restore CS0612

    private static void AddTypeToTracker(Type type, params Type[] subTypes) {
        Tracker.StoredEntityTypes.Add(type);

        if (!Tracker.TrackedEntityTypes.ContainsKey(type)) {
            Tracker.TrackedEntityTypes[type] = new List<Type> {type};
        } else if (!Tracker.TrackedEntityTypes[type].Contains(type)) {
            Tracker.TrackedEntityTypes[type].Add(type);
        }

        foreach (Type subType in subTypes) {
            if (!Tracker.TrackedEntityTypes.ContainsKey(subType)) {
                Tracker.TrackedEntityTypes[subType] = new List<Type> {type};
            } else if (!Tracker.TrackedEntityTypes[subType].Contains(type)) {
                Tracker.TrackedEntityTypes[subType].Add(type);
            }
        }

        if (Engine.Scene?.Tracker.Entities is { } entities) {
            if (!entities.ContainsKey(type)) {
                entities[type] = Engine.Scene.Entities.Where(e => e.GetType() == type).ToList();
            }
        }
    }

    private static void BackdropRendererOnUpdate(On.Celeste.BackdropRenderer.orig_Update orig, BackdropRenderer self, Scene scene) {
        if (Manager.FastForwarding && Engine.FrameCounter % 1000 > 0) {
            return;
        }

        orig(self, scene);
    }

    private static void SoundEmitterOnUpdate(On.Celeste.SoundEmitter.orig_Update orig, SoundEmitter self) {
        if (Manager.FastForwarding) {
            self.RemoveSelf();
        } else {
            orig(self);
        }
    }

    private static void SkipUpdateMethod(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next!;
        ilCursor.Emit(OpCodes.Call, typeof(Manager).GetPropertyInfo(nameof(Manager.FastForwarding), BindingFlags.Public | BindingFlags.Static)!.GetMethod!);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static void LightningRendererOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next!;
        ilCursor.EmitDelegate<Func<bool>>(IsSkipLightningRendererUpdate);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsSkipLightningRendererUpdate() {
        return Manager.FastForwarding && Engine.FrameCounter % 30 > 0;
    }

    private static void SeekerBarrierOnUpdate(ILContext il) {
        ILCursor cursor = new(il);

        if (!cursor.TryGotoNext(MoveType.AfterLabel,
                ins => ins.MatchLdarg(0),
                ins => ins.MatchLdfld<SeekerBarrier>("speeds"),
                ins => ins.MatchLdlen())
           ) {
            return;
        }

        ILLabel target = cursor.DefineLabel();
        cursor.EmitDelegate<Func<bool>>(IsSkipSeekerBarrierOverloadPart);
        cursor.Emit(OpCodes.Brtrue, target);

        if (!cursor.TryGotoNext(ins => ins.MatchLdarg(0),
                ins => ins.MatchCall<Solid>("Update"))
           ) {
            return;
        }

        cursor.MarkLabel(target);
    }

    private static bool IsSkipSeekerBarrierOverloadPart() {
        return Manager.FastForwarding;
    }

    private static void IgnoreGcCollect(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(ins => ins.MatchCall(typeof(GC), "Collect"),
                ins => ins.MatchCall(typeof(GC), "WaitForPendingFinalizers"))) {
            Instruction afterGc = ilCursor.Next!.Next.Next;
            ilCursor.EmitDelegate<Func<bool>>(IsIgnoreGcCollect);
            ilCursor.Emit(OpCodes.Brtrue, afterGc);
        }
    }

    private static bool IsIgnoreGcCollect() {
        return TasSettings.IgnoreGcCollect && Manager.FastForwarding;
    }

    private static void InputOnOnInitialize() {
        CelesteTasModule.Instance.OnInputDeregister();
    }
}
