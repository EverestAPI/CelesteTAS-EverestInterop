using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;

namespace TAS.Utils;

internal static class ModUtils {
    public static readonly Assembly VanillaAssembly = typeof(Player).Assembly;

    public static Type GetType(string modName, string name, bool throwOnError = false, bool ignoreCase = false) {
        return GetAssembly(modName)?.GetType(name, throwOnError, ignoreCase);
    }

    public static Type GetType(string name, bool throwOnError = false, bool ignoreCase = false) {
        return FakeAssembly.GetFakeEntryAssembly().GetType(name, throwOnError, ignoreCase);
    }

    public static Type[] GetTypes() {
        return FakeAssembly.GetFakeEntryAssembly().GetTypes();
    }

    public static EverestModule GetModule(string modName) {
        return Everest.Modules.FirstOrDefault(module => module.Metadata?.Name == modName);
    }

    public static bool IsInstalled(string modName) {
        return GetModule(modName) != null;
    }

    public static Assembly GetAssembly(string modName) {
        return GetModule(modName)?.GetType().Assembly;
    }
}