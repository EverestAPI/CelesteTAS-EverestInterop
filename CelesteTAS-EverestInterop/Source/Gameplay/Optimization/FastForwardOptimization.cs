using Celeste;
using Monocle;
using MonoMod.Cil;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TAS.ModInterop;
using TAS.Module;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Gameplay.Optimization;

/// Applies optimization to the gameplay by disabling visual effects which aren't seen anyway, while fast forwarding at high speeds
internal static class FastForwardOptimization {

    private static bool Active => Manager.FastForwarding || SyncChecker.Active;
    private static bool IgnoreGarbageCollect => Active && TasSettings.IgnoreGcCollect;

    private static Type? sjCreditsType;

    [Initialize]
    private static void Initialize() {
        #region Particles

        SkipMethods(
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Update))!,
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Clear))!,
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.ClearRect))!
        );

        // Some 'Emit()' methods update 'Calc.Random' which needs to be kept
        typeof(ParticleSystem).GetAllMethodInfos()
            .Where(m => m.Name == nameof(ParticleSystem.Emit))
            .ForEach(m => {
                const string typeParam = "type";
                const string amountParam = "amount";
                const string positionRangeParam = "positionRange";

                var param = m.GetParameters();
                if (param.All(p => p.Name is not (typeParam or positionRangeParam))) {
                    SkipMethod(m);
                } else {
                    m.IlHook((cursor, _) => {
                        var start = cursor.MarkLabel();
                        cursor.MoveBeforeLabels();

                        cursor.EmitCall(typeof(FastForwardOptimization).GetGetMethod(nameof(Active))!);
                        cursor.EmitBrfalse(start);

                        if (param.IndexOf(p => p.Name == amountParam) is var amountIdx && amountIdx != -1) {
                            // Maintain 2 * amount 'random.NextDouble()' calls caused by 'random.Range()'
                            cursor.EmitLdarg(amountIdx + 1);
                            cursor.EmitStaticDelegate("SimulateRandomRangeCalls", (int amount) => {
                                for (int i = 0; i < amount; i++) {
                                    Calc.Random.NextDouble();
                                    Calc.Random.NextDouble();
                                }
                            });
                        }
                        if (param.IndexOf(p => p.Name == typeParam) is var typeIdx && typeIdx != -1) {
                            cursor.EmitLdarg(typeIdx + 1);
                            cursor.EmitStaticDelegate("SimulateTypeCreateCalls", (ParticleType type) => {
                                if (type.SourceChooser != null) {
                                    // For 'particle.Source = SourceChooser.Choose();'
                                    Calc.Random.NextDouble();
                                }

                                if (type.SizeRange != 0.0f) {
                                    // For 'particle.StartSize = (particle.Size = Size - SizeRange * 0.5f + Calc.Random.NextFloat(SizeRange));'
                                    Calc.Random.NextDouble();
                                }

                                if (type.ColorMode == ParticleType.ColorModes.Choose) {
                                    // For 'particle.StartColor = (particle.Color = Calc.Random.Choose(color, Color2));'
                                    Calc.Random.Next(2);
                                }

                                // For 'float moveDirection = direction - DirectionRange / 2f + Calc.Random.NextFloat() * DirectionRange;'
                                Calc.Random.NextDouble();
                                // For 'particle.Speed = Calc.AngleToVector(moveDirection, Calc.Random.Range(SpeedMin, SpeedMax));'
                                Calc.Random.NextDouble();
                                // For 'particle.StartLife = (particle.Life = Calc.Random.Range(LifeMin, LifeMax));'
                                Calc.Random.NextDouble();

                                if (type.RotationMode == ParticleType.RotationModes.Random) {
                                    // For 'particle.Rotation = Calc.Random.NextAngle();'
                                    Calc.Random.NextDouble();
                                }

                                // For 'particle.Spin = Calc.Random.Range(SpinMin, SpinMax);'
                                Calc.Random.NextDouble();
                                if (type.SpinFlippedChance) {
                                    // For 'particle.Spin *= Calc.Random.Choose(1, -1);'
                                    Calc.Random.Next(2);
                                }
                            });
                        }


                        cursor.EmitRet();
                    });
                }
            });

        #endregion
        #region Renderers

        // BackdropRenderer needs to update, since it contains relevant 'Calc.Random' calls
        SkipMethod(typeof(SeekerBarrierRenderer).GetMethodInfo(nameof(SeekerBarrierRenderer.Update)));

        #endregion
        #region Sound

        On.Celeste.SoundEmitter.Update += On_SoundEmitter_Update;

        #endregion
        #region Visual Entities

        SkipMethods(
            typeof(ReflectionTentacles).GetMethodInfo(nameof(ReflectionTentacles.Update)),
            typeof(Decal).GetMethodInfo(nameof(Decal.Update)),
            typeof(FloatingDebris).GetMethodInfo(nameof(FloatingDebris.Update)),
            typeof(AnimatedTiles).GetMethodInfo(nameof(AnimatedTiles.Update)),
            typeof(Water.Surface).GetMethodInfo(nameof(Water.Surface.Update)),
            typeof(LavaRect).GetMethodInfo(nameof(LavaRect.Update)),
            typeof(CliffsideWindFlag).GetMethodInfo(nameof(CliffsideWindFlag.Update)),
            typeof(CrystalStaticSpinner).GetMethodInfo(nameof(CrystalStaticSpinner.UpdateHue)),
            typeof(HiresSnow).GetMethodInfo(nameof(HiresSnow.Update)),
            typeof(Snow3D).GetMethodInfo(nameof(Snow3D.Update)),
            typeof(AutoSplitterInfo).GetMethodInfo(nameof(AutoSplitterInfo.Update)),
            typeof(SeekerBarrier).GetMethodInfo(nameof(SeekerBarrier.Update)),

            ModUtils.GetMethod("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder", nameof(Entity.Update))
        );
        // The dust sprites being 'Estableshed' (yes, that's how it's spelled :p) is required for gameplay during stuns
        SkipMethod(typeof(DustGraphic).GetMethodInfo(nameof(DustGraphic.Update)), cursor => {
            cursor.EmitLdarg0();
            cursor.EmitStaticDelegate("EstableshedNodes", (DustGraphic dust) => {
                if (dust.nodes.Count <= 0 && dust.Entity.Scene != null && !dust.Estableshed) {
                    dust.AddDustNodesIfInCamera();
                }
            });
        });

        #endregion
        #region Garbage Collection

        IL.Monocle.Engine.OnSceneTransition += SkipGC;
        IL.Celeste.Level.Reload += SkipGC;
        typeof(Level).GetMethodInfo(nameof(Level._GCCollect))
            ?.SkipMethod(static () => IgnoreGarbageCollect);

        #endregion
        #region Special

        ModUtils.GetMethod("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Cutscenes.CS_Credits", "Level_OnLoadEntity")
            ?.IlHook((cursor, _) => {
                // Reduce LINQ usage of 'CS_Credits credits = level.Entities.ToAdd.OfType<CS_Credits>().FirstOrDefault();'
                if (!cursor.TryGotoNext(
                    instr => instr.MatchCallvirt<Scene>($"get_{nameof(Scene.Entities)}"),
                    instr => instr.MatchCallvirt<EntityList>($"get_{nameof(EntityList.ToAdd)}"),
                    instr => instr.MatchCall(typeof(Enumerable), nameof(Enumerable.OfType)),
                    instr => instr.MatchCall(typeof(Enumerable), nameof(Enumerable.FirstOrDefault))
                )) {
                    return;
                }

                sjCreditsType = ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Cutscenes.CS_Credits")!;

                // Nothing else should be hooking the SJ credits. This is just for performance
                #pragma warning disable CL0005
                cursor.RemoveRange(4);
                cursor.EmitStaticDelegate(static Entity? (Level level) => {
                    foreach (var entity in level.Entities.ToAdd) {
                        if (entity.GetType() == sjCreditsType) {
                            return entity;
                        }
                    }

                    return null;
                });
                #pragma warning restore CL0005
            });

        #endregion
    }
    [Unload]
    private static void Unload() {
        On.Celeste.SoundEmitter.Update -= On_SoundEmitter_Update;

        IL.Monocle.Engine.OnSceneTransition -= SkipGC;
        IL.Celeste.Level.Reload -= SkipGC;
    }

    /// Skips calling the original method while fast forwarding
    public static void SkipMethod(MethodInfo? method, Action<ILCursor>? preSkipCallback = null) {
        if (method == null) {
            return;
        }

#if DEBUG
        Debug.Assert(method.ReturnType == typeof(void));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitCall(typeof(FastForwardOptimization).GetGetMethod(nameof(Active))!);
            cursor.EmitBrfalse(start);

            preSkipCallback?.Invoke(cursor);
            cursor.EmitRet();
        });
    }
    /// Skips calling the original methods while fast forwarding
    public static void SkipMethods(params ReadOnlySpan<MethodInfo?> methods) {
        foreach (var method in methods) {
            SkipMethod(method);
        }
    }
    /// Skips calling the original methods while fast forwarding
    public static void SkipMethods(params IEnumerable<MethodInfo?> methods) {
        foreach (var method in methods) {
            SkipMethod(method);
        }
    }

    private static void SkipGC(ILContext il) {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
                instr => instr.MatchCall(typeof(GC), nameof(GC.Collect)),
                instr => instr.MatchCall(typeof(GC), nameof(GC.WaitForPendingFinalizers))
            )) {
            return;
        }

        var afterGC = cursor.DefineLabel();
        cursor.EmitCall(typeof(FastForwardOptimization).GetGetMethod(nameof(IgnoreGarbageCollect))!);
        cursor.EmitBrtrue(afterGC);

        cursor.Index += 2; // Go past both calls
        cursor.MarkLabel(afterGC);
    }

    private static void On_SoundEmitter_Update(On.Celeste.SoundEmitter.orig_Update orig, SoundEmitter self) {
        // Disable sound sources while fast-forwarding
        if (Manager.FastForwarding) {
            self.RemoveSelf();
        } else {
            orig(self);
        }
    }

}
