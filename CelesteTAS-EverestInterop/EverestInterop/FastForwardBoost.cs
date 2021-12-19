using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop {
    public static class FastForwardBoost {
        private static bool SkipUpdate => Manager.UltraFastForwarding;

        [Load]
        private static void Load() {
            On.Monocle.Tracker.Initialize += TrackerOnInitialize;
            On.Celeste.BackdropRenderer.Update += BackdropRendererOnUpdate;
            On.Celeste.ReflectionTentacles.Update += ReflectionTentaclesOnUpdate;
            On.Monocle.ParticleSystem.Update += ParticleSystemOnUpdate;
            On.Celeste.Decal.Update += DecalOnUpdate;
            On.Celeste.FloatingDebris.Update += FloatingDebrisOnUpdate;
            On.Celeste.AnimatedTiles.Update += AnimatedTilesOnUpdate;
            On.Celeste.Water.Surface.Update += WaterSurfaceOnUpdate;
            On.Celeste.Debris.Update += DebrisOnUpdate;
            On.Celeste.SoundEmitter.Update += SoundEmitterOnUpdate;
            On.Celeste.LightningRenderer.Update += LightningRendererOnUpdate;
            On.Celeste.DustGraphic.Update += DustGraphicOnUpdate;
            On.Celeste.LavaRect.Update += LavaRectOnUpdate;
            On.Celeste.CliffsideWindFlag.Update += CliffsideWindFlagOnUpdate;
            On.Celeste.SeekerBarrierRenderer.Update += SeekerBarrierRendererOnUpdate;
            IL.Celeste.SeekerBarrier.Update += SeekerBarrierOnUpdate;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Tracker.Initialize -= TrackerOnInitialize;
            On.Celeste.BackdropRenderer.Update -= BackdropRendererOnUpdate;
            On.Celeste.ReflectionTentacles.Update -= ReflectionTentaclesOnUpdate;
            On.Monocle.ParticleSystem.Update -= ParticleSystemOnUpdate;
            On.Celeste.Decal.Update -= DecalOnUpdate;
            On.Celeste.FloatingDebris.Update -= FloatingDebrisOnUpdate;
            On.Celeste.AnimatedTiles.Update -= AnimatedTilesOnUpdate;
            On.Celeste.Water.Surface.Update -= WaterSurfaceOnUpdate;
            On.Celeste.Debris.Update -= DebrisOnUpdate;
            On.Celeste.SoundEmitter.Update -= SoundEmitterOnUpdate;
            On.Celeste.LightningRenderer.Update -= LightningRendererOnUpdate;
            On.Celeste.DustGraphic.Update -= DustGraphicOnUpdate;
            On.Celeste.LavaRect.Update -= LavaRectOnUpdate;
            On.Celeste.CliffsideWindFlag.Update -= CliffsideWindFlagOnUpdate;
            On.Celeste.SeekerBarrierRenderer.Update -= SeekerBarrierRendererOnUpdate;
            IL.Celeste.SeekerBarrier.Update -= SeekerBarrierOnUpdate;
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

        private static void ReflectionTentaclesOnUpdate(On.Celeste.ReflectionTentacles.orig_Update orig, ReflectionTentacles self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void ParticleSystemOnUpdate(On.Monocle.ParticleSystem.orig_Update orig, ParticleSystem self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void DecalOnUpdate(On.Celeste.Decal.orig_Update orig, Decal self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void FloatingDebrisOnUpdate(On.Celeste.FloatingDebris.orig_Update orig, FloatingDebris self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void AnimatedTilesOnUpdate(On.Celeste.AnimatedTiles.orig_Update orig, AnimatedTiles self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void WaterSurfaceOnUpdate(On.Celeste.Water.Surface.orig_Update orig, Water.Surface self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void DebrisOnUpdate(On.Celeste.Debris.orig_Update orig, Debris self) {
            if (!SkipUpdate) {
                orig(self);
            } else {
                self.RemoveSelf();
            }
        }

        private static void SoundEmitterOnUpdate(On.Celeste.SoundEmitter.orig_Update orig, SoundEmitter self) {
            if (!SkipUpdate) {
                orig(self);
            } else {
                self.RemoveSelf();
            }
        }

        private static void LightningRendererOnUpdate(On.Celeste.LightningRenderer.orig_Update orig, LightningRenderer self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void DustGraphicOnUpdate(On.Celeste.DustGraphic.orig_Update orig, DustGraphic self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void LavaRectOnUpdate(On.Celeste.LavaRect.orig_Update orig, LavaRect self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void CliffsideWindFlagOnUpdate(On.Celeste.CliffsideWindFlag.orig_Update orig, CliffsideWindFlag self) {
            if (!SkipUpdate) {
                orig(self);
            }
        }

        private static void SeekerBarrierRendererOnUpdate(On.Celeste.SeekerBarrierRenderer.orig_Update orig, SeekerBarrierRenderer self) {
            if (!SkipUpdate) {
                orig(self);
            }
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
            cursor.EmitDelegate<Func<bool>>(() => SkipUpdate);
            cursor.Emit(OpCodes.Brtrue, target);

            if (!cursor.TryGotoNext(instr => instr.MatchLdarg(0),
                instr => instr.MatchCall<Solid>("Update"))
            ) {
                return;
            }

            cursor.MarkLabel(target);
        }

    }
}