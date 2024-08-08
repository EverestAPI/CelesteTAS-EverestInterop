using Celeste.Mod;
using Celeste.Mod.UI;
using TAS.Module;

namespace TAS.EverestInterop;

public static class CriticalErrorHandlerFixer {
    [Load]
    private static void Load() {
        Everest.Events.OnCriticalError += HandleCriticalError;
    }

    [Unload]
    private static void Unload() {
        Everest.Events.OnCriticalError -= HandleCriticalError;
    }

    private static void HandleCriticalError(CriticalErrorHandler criticalErrorHandler) => Manager.DisableRun();
}