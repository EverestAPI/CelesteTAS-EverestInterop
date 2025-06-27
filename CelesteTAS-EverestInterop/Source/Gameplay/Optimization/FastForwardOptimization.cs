using Monocle;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TAS.Module;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Gameplay.Optimization;

/// Applies optimization to the gameplay by disabling visual effects which aren't seen anyway, while fast forwarding at high speeds
internal static class FastForwardOptimization {

    private static bool Active => true || Manager.FastForwarding || SyncChecker.Active;

    [Load]
    private static void Load() {
        // Particles
        SkipMethods(
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Update)),
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Clear)),
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.ClearRect)),
            typeof(ParticleEmitter).GetMethodInfo(nameof(ParticleEmitter.Update)),
            typeof(ParticleEmitter).GetMethodInfo(nameof(ParticleEmitter.Emit))
        );
        SkipMethods(
            typeof(ParticleSystem).GetAllMethodInfos().Where(m => m.Name == nameof(ParticleSystem.Emit))
        );
    }

    /// Skips calling the original method while fast forwarding
    public static void SkipMethod(MethodInfo? method) {
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
}
