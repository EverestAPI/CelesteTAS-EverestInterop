using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;

namespace TAS.EverestInterop {
    public static class FastForwardBoost {
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        private static bool SkipUpdate => Manager.State == State.Enable
                                          && !Settings.FastForwardCallBase
                                          && Manager.FrameLoops >= Settings.FastForwardThreshold;

        [Load]
        private static void Load() {
            On.Monocle.Tracker.Initialize += TrackerOnInitialize;
            On.Celeste.BackdropRenderer.Update += BackdropRendererOnUpdate;
            On.Celeste.ReflectionTentacles.UpdateVertices += ReflectionTentaclesOnUpdateVertices;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Tracker.Initialize -= TrackerOnInitialize;
            On.Celeste.BackdropRenderer.Update -= BackdropRendererOnUpdate;
            On.Celeste.ReflectionTentacles.UpdateVertices -= ReflectionTentaclesOnUpdateVertices;
        }

        private static void TrackerOnInitialize(On.Monocle.Tracker.orig_Initialize orig) {
            orig();
            AddTypeToTracker(typeof(PlayerSeeker));
            AddTypeToTracker(typeof(LockBlock));
            AddTypeToTracker(typeof(KeyboardConfigUI), typeof(ModuleSettingsKeyboardConfigUI));
            AddTypeToTracker(typeof(ButtonConfigUI), typeof(ModuleSettingsButtonConfigUI));
        }

        private static void AddTypeToTracker(Type type, params Type[] subTypes) {
            if (!Tracker.StoredEntityTypes.Contains(type)) {
                Tracker.StoredEntityTypes.Add(type);
            }

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
        }

        private static void BackdropRendererOnUpdate(On.Celeste.BackdropRenderer.orig_Update orig, BackdropRenderer self, Scene scene) {
            if (SkipUpdate && Engine.FrameCounter % 1000 > 0) {
                return;
            }

            orig(self, scene);
        }

        private static void ReflectionTentaclesOnUpdateVertices(On.Celeste.ReflectionTentacles.orig_UpdateVertices orig, ReflectionTentacles self) {
            if (SkipUpdate) {
                return;
            }

            orig(self);
        }
    }
}