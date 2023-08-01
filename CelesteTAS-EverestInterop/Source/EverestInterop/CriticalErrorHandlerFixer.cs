using System;
using Celeste;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class CriticalErrorHandlerFixer {
    public static bool Handling => getCurrentHandler?.Invoke(null) != null;
    private static GetDelegate<Overlay, Overlay> getCurrentHandler;

    [Load]
    private static void Load() {
        Type type = ModUtils.VanillaAssembly.GetType("Celeste.Mod.UI.CriticalErrorHandler");
        type?.GetMethod("Update")?.HookBefore(() => {
            if (Manager.Running) {
                Manager.DisableRun();
            }
        });

        getCurrentHandler = type?.CreateGetDelegate<Overlay, Overlay>("CurrentHandler");
    }
}