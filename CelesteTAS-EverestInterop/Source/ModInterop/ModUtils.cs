using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using System.Collections.Generic;
using TAS.Utils;

namespace TAS.ModInterop;

internal static class ModUtils {
    public static readonly Assembly VanillaAssembly = typeof(Player).Assembly;

    /// Returns all specified type from the given mod, if the mod is present
    public static Type? GetType(string modName, string fullTypeName) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            return null;
        }

        var type = asm.GetType(fullTypeName);
        if (type == null) {
            $"Failed to find type '{fullTypeName}' in assembly '{asm}'".Log(LogLevel.Error);
            return null;
        }

        return type;
    }

    /// Returns all specified types from the given mod, if the mod is present
    public static IEnumerable<Type> GetTypes(string modName, params string[] fullTypeNames) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            yield break;
        }

        foreach (string fullTypeName in fullTypeNames) {
            var type = asm.GetType(fullTypeName);
            if (type == null) {
                $"Failed to find type '{fullTypeName}' in assembly '{asm}'".Log(LogLevel.Error);
            }

            yield return type;
        }
    }

    /// Returns the specified method from the given mod, if the mod is present
    public static MethodInfo? GetMethod(string modName, string fullTypeName, string methodName) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            return null;
        }

        var type = asm.GetType(fullTypeName);
        if (type == null) {
            $"Failed to find type '{fullTypeName}' in assembly '{asm}'".Log(LogLevel.Error);
            return null;
        }

        var method = type.GetMethodInfo(methodName);
        if (method == null) {
            $"Failed to find method '{methodName}' in type '{type}'".Log(LogLevel.Error);
            return null;
        }

        return method;
    }

    public static Type[] GetTypes() {
        return FakeAssembly.GetFakeEntryAssembly().GetTypes();
    }

    public static EverestModule? GetModule(string modName) {
        return Everest.Modules.FirstOrDefault(module => module.Metadata?.Name == modName);
    }

    public static bool IsInstalled(string modName) {
        return GetModule(modName) != null;
    }

    public static Assembly? GetAssembly(string modName) {
        return GetModule(modName)?.GetType().Assembly;
    }
}
