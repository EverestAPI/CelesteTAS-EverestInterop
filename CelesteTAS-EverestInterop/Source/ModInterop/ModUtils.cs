﻿using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using FMOD;
using System.Collections.Generic;
using TAS.Utils;
using Debug = System.Diagnostics.Debug;

namespace TAS.ModInterop;

internal static class ModUtils {
    public static readonly Assembly VanillaAssembly = typeof(Player).Assembly;

    public static Type? GetType(string modName, string name, bool throwOnError = false, bool ignoreCase = false) {
        return GetAssembly(modName)?.GetType(name, throwOnError, ignoreCase);
    }

    /// Returns all specified types from the given mod, if present
    public static IEnumerable<Type> GetTypes(string modName, params string[] fullTypeNames) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            yield break;
        }

        foreach (string fullTypeName in fullTypeNames) {
            var type = asm.GetType(fullTypeName);
            if (type == null) {
                $"Failed to find type '{fullTypeName}' in assembly '{asm}'".Log(LogLevel.Error);
#if DEBUG
                throw new TypeAccessException();
#endif
            }

            yield return type;
        }
    }

    public static Type? GetType(string name, bool throwOnError = false, bool ignoreCase = false) {
        return FakeAssembly.GetFakeEntryAssembly().GetType(name, throwOnError, ignoreCase);
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
