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
    public static void CollectOwnMethods<T>() where T : Attribute {
        attributeMethods[typeof(T)] = typeof(CelesteTasModule).Assembly
            .GetTypesSafe()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetCustomAttribute<T>() != null && info.GetParameters().Length == 0))
            .ToArray();
    }

    /// Gathers all static, parameterless methods with attribute T
    /// Only searches through all mods - Should only be called after Load()
    public static void CollectAllMethods<T>() where T : Attribute {
        attributeMethods[typeof(T)] = FakeAssembly.GetFakeEntryAssembly()
            .GetTypesSafe()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetCustomAttribute<T>() != null && info.GetParameters().Length == 0))
            .ToArray();
    }

    /// Invokes all previously gathered methods for attribute T
    public static void Invoke<T>() where T : Attribute {
        if (!attributeMethods.TryGetValue(typeof(T), out var methods)) {
            return;
        }

        foreach (var method in methods) {
            method.Invoke(null, []);
        }
    }
}
