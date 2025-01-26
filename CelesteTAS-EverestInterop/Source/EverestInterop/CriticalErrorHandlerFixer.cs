using Celeste.Mod;
using Celeste.Mod.UI;
using Monocle;
using System;
using System.Runtime.ExceptionServices;
using TAS.Module;
using TAS.SyncCheck;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class CriticalErrorHandlerFixer {
    [Load]
    private static void Load() {
        var handleCriticalError = typeof(CriticalErrorHandler)
            .GetMethodInfo(nameof(CriticalErrorHandler.HandleCriticalError));

        // Prevent critical error handler from interrupting TASes while sync checking
        handleCriticalError.OverrideReturn(_ => SyncChecker.Active, (ExceptionDispatchInfo error) => error);
        handleCriticalError.HookBefore(Manager.DisableRun);
    }
}
