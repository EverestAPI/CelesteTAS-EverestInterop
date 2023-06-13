using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class FastForwardBoost {
    private static bool UltraFastForwarding => Manager.UltraFastForwarding;
    private static Process celesteProcess;

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder")?.GetMethodInfo("Update") is { } updateMethod) {
            updateMethod.IlHook(SkipUpdateMethod);
        }
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
        IL.Celeste.LightningRenderer.Update += LightningRendererOnUpdate;
        IL.Celeste.SeekerBarrier.Update += SeekerBarrierOnUpdate;
        IL.Monocle.Engine.OnSceneTransition += IgnoreGcCollect;
        IL.Celeste.Level.Reload += IgnoreGcCollect;
        On.Monocle.Engine.Update += EngineOnUpdate;
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
        IL.Celeste.LightningRenderer.Update -= LightningRendererOnUpdate;
        IL.Celeste.SeekerBarrier.Update -= SeekerBarrierOnUpdate;
        IL.Monocle.Engine.OnSceneTransition -= IgnoreGcCollect;
        IL.Celeste.Level.Reload -= IgnoreGcCollect;
        On.Monocle.Engine.Update -= EngineOnUpdate;
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
        if (UltraFastForwarding && Engine.FrameCounter % 1000 > 0) {
            return;
        }

        orig(self, scene);
    }

    private static void SoundEmitterOnUpdate(On.Celeste.SoundEmitter.orig_Update orig, SoundEmitter self) {
        if (UltraFastForwarding) {
            self.RemoveSelf();
        } else {
            orig(self);
        }
    }

    private static void SkipUpdateMethod(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Call, typeof(Manager).GetProperty(nameof(Manager.UltraFastForwarding)).GetMethod);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static void LightningRendererOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.EmitDelegate<Func<bool>>(IsSkipLightningRendererUpdate);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsSkipLightningRendererUpdate() {
        return Manager.UltraFastForwarding && Engine.FrameCounter % 30 > 0;
    }

    private static void SeekerBarrierOnUpdate(ILContext il) {
        ILCursor cursor = new(il);

        if (!cursor.TryGotoNext(MoveType.AfterLabel,
                instr => instr.MatchLdarg(0),
                instr => instr.MatchLdfld<SeekerBarrier>("speeds"),
                instr => instr.MatchLdlen())
           ) {
            return;
        }

        ILLabel target = cursor.DefineLabel();
        cursor.EmitDelegate<Func<bool>>(IsSkipSeekerBarrierOverloadPart);
        cursor.Emit(OpCodes.Brtrue, target);

        if (!cursor.TryGotoNext(instr => instr.MatchLdarg(0),
                instr => instr.MatchCall<Solid>("Update"))
           ) {
            return;
        }

        cursor.MarkLabel(target);
    }

    private static bool IsSkipSeekerBarrierOverloadPart() {
        return UltraFastForwarding;
    }

    private static void IgnoreGcCollect(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(ins => ins.MatchCall(typeof(GC), "Collect"),
                ins => ins.MatchCall(typeof(GC), "WaitForPendingFinalizers"))) {
            Instruction afterGc = ilCursor.Next.Next.Next;
            ilCursor.EmitDelegate<Func<bool>>(IsIgnoreGcCollect);
            ilCursor.Emit(OpCodes.Brtrue, afterGc);
        }
    }

    private static bool IsIgnoreGcCollect() {
        bool result = !Environment.Is64BitProcess && TasSettings.IgnoreGcCollect && UltraFastForwarding;
        if (celesteProcess == null && result) {
            celesteProcess = Process.GetCurrentProcess();
        }

        if (celesteProcess != null) {
            celesteProcess.Refresh();
            // 2.5GB
            if (celesteProcess.PrivateMemorySize64 > 1024L * 1024L * 1024L * 2.5) {
                result = false;
            }
        }

        return result;
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        orig(self, gameTime);

        if (UltraFastForwarding) {
            return;
        }

        if (celesteProcess != null) {
            celesteProcess.Dispose();
            celesteProcess = null;
        }
    }

    private static void InputOnOnInitialize() {
        CelesteTasModule.Instance.OnInputDeregister();
    }
}