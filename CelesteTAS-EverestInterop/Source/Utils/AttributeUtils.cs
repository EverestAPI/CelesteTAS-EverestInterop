using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using TAS.Module;

namespace TAS.Utils;

internal static class AttributeUtils {
    private static readonly Dictionary<Type, MethodInfo[]> attributeMethods = new();

    /// Gathers all static, parameterless methods with attribute T
    public static void CollectMethods<T>() where T : Attribute {
        attributeMethods[typeof(T)] = typeof(CelesteTasModule).Assembly
            .GetTypesSafe()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetParameters().Length == 0 && info.GetCustomAttribute<T>() != null))
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