using System;
using System.Linq;
using Celeste.Mod;
using Celeste.Mod.Helpers;

namespace TAS.Utils;

public static class ModUtils {
    public static Type GetType(string name, bool throwOnError = false, bool ignoreCase = false) {
        return FakeAssembly.GetFakeEntryAssembly().GetType(name, throwOnError, ignoreCase);
    }

    public static Type[] GetTypes() {
        return FakeAssembly.GetFakeEntryAssembly().GetTypes();
    }

    public static bool IsInstalled(string modName) {
        return Everest.Modules.Any(module => module.Metadata?.Name == "PandorasBox");
    }
}