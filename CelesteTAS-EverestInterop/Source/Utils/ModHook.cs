using Celeste.Mod;
using JetBrains.Annotations;
using MonoMod.Cil;
using System;
using System.Linq;
using System.Reflection;
using TAS.ModInterop;
using TAS.Module;

namespace TAS.Utils;

/// Applies an On-hook to the target method, if the target mod is loaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ModOnHook(string Mod, string Type, string Method) : Attribute {
    public readonly string Mod = Mod;
    public readonly string Type = Type;
    public readonly string Method = Method;

    [Initialize]
    private static void Initialize() {
        typeof(CelesteTasModule).Assembly
            .GetTypesSafe()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetCustomAttribute<ModOnHook>() != null))
            .ForEach(info => {
                var attr = info.GetCustomAttribute<ModOnHook>()!;
                if (ModUtils.GetMethod(attr.Mod, attr.Type, attr.Method) is not { } target) {
                    return;
                }

                target.OnHook(info);
            });
    }
}

/// Applies an IL-hook to the target method, if the target mod is loaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ModILHook(string Mod, string Type, string Method) : Attribute {
    public readonly string Mod = Mod;
    public readonly string Type = Type;
    public readonly string Method = Method;

    [Initialize]
    private static void Initialize() {
        typeof(CelesteTasModule).Assembly
            .GetTypesSafe()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetCustomAttribute<ModILHook>() != null))
            .ForEach(info => {
                var attr = info.GetCustomAttribute<ModILHook>()!;
                if (ModUtils.GetMethod(attr.Mod, attr.Type, attr.Method) is not { } target) {
                    return;
                }

                var param = info.GetParameters();

                switch (param.Length) {
                    case 1 when param[0].ParameterType == typeof(ILContext):
                        target.IlHook(il => info.Invoke(null, [il]));
                        break;
                    case 1 when param[0].ParameterType == typeof(ILCursor):
                        target.IlHook(il => info.Invoke(null, [new ILCursor(il)]));
                        break;
                    case 2 when param[0].ParameterType == typeof(ILContext) && param[1].ParameterType == typeof(ILCursor):
                        target.IlHook(il => info.Invoke(null, [il, new ILCursor(il)]));
                        break;

                    default:
                        throw new ArgumentException($"Invalid signature for IL-hook: {info}");
                }
            });
    }
}
