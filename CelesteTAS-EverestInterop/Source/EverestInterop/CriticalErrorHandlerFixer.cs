using System;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class CriticalErrorHandlerFixer {
    public static bool Handling => getCurrentHandler?.Invoke(null) != null;
    private static FastReflectionDelegate getCurrentHandler;

    [Load]
    private static void Load() {
        Type type = ModUtils.VanillaAssembly.GetType("Celeste.Mod.UI.CriticalErrorHandler");
        if (type != null) {
            type.GetMethod("Update")?.HookBefore(() => {
                if (Manager.Running) {
                    Manager.DisableRun();
                }
            });

            getCurrentHandler = type.GetProperty("CurrentHandler")?.GetGetMethod()?.GetFastDelegate();
        }
    }
}