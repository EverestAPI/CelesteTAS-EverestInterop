using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop;

public static class FastForwardBoost {
    [Load]
    private static void Load() {
        // cause desync
        // https://discord.com/channels/403698615446536203/519281383164739594/1061696803772321923
        // IL.Celeste.DustGraphic.Update += SkipUpdateMethod;

        On.Monocle.Tracker.Initialize += TrackerOnInitialize;
        IL.Celeste.LightningRenderer.Update += LightningRendererOnUpdate;
    }

    [Unload]
    private static void Unload() {
        // TODO: Use Everest tracker improvements once available
        On.Monocle.Tracker.Initialize -= TrackerOnInitialize;
        // TODO: Figure out why this is required to update every 30f
        IL.Celeste.LightningRenderer.Update -= LightningRendererOnUpdate;
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

    private static void LightningRendererOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next!;
        ilCursor.EmitDelegate<Func<bool>>(IsSkipLightningRendererUpdate);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsSkipLightningRendererUpdate() {
        return Manager.FastForwarding && Engine.FrameCounter % 30 > 0;
    }
}
