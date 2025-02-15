using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using TAS.Module;

namespace TAS.Utils;

public static class AttributeUtils {
    private static readonly Dictionary<Type, MethodInfo[]> attributeMethods = new();

    /// Gathers all static, parameterless methods with attribute T
    /// Only searches through CelesteTAS itself
    public static void CollectOwnMethods<T>(params Type[] parameterTypes) where T : Attribute {
        attributeMethods[typeof(T)] = typeof(CelesteTasModule).Assembly
            .GetTypesSafe()
            .SelectMany(type => type.Collect<T>(parameterTypes))
            .ToArray();
    }

    /// Gathers all static, parameterless methods with attribute T
    /// Searches through all mods - Should only be called after Load()
    public static void CollectAllMethods<T>(params Type[] parameterTypes) where T : Attribute {
        attributeMethods[typeof(T)] = FakeAssembly.GetFakeEntryAssembly()
            .GetTypesSafe()
            .SelectMany(type => type.Collect<T>(parameterTypes))
            .ToArray();
    }

    /// Invokes all previously gathered methods for attribute T
    public static void Invoke<T>(params object?[] parameters) where T : Attribute {
        if (!attributeMethods.TryGetValue(typeof(T), out var methods)) {
            return;
        }

        foreach (var method in methods) {
            method.Invoke(null, parameters);
        }
    }

    private static IEnumerable<MethodInfo> Collect<T>(this Type type, Type[] parameterTypes) where T : Attribute => type
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(info => {
            if (info.GetCustomAttribute<T>() == null) {
                return false;
            }

            if (!info.GetParameters().Select(param => param.ParameterType).SequenceEqual(parameterTypes)) {
                $"Method '{info}' on type '{info.DeclaringType}' has attribute '{typeof(T)}' without matching parameter signature '({string.Join<Type>(", ", parameterTypes)})'".Log(LogLevel.Error);
                return false;
            }

            return true;
        });
}
