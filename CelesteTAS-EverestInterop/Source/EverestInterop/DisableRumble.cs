using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class DisableRumble {
    [Load]
    private static void Load() {
        HookHelper.SkipMethod(typeof(DisableRumble), nameof(IsDisableRumble), typeof(MInput.GamePadData).GetMethodInfo("Rumble"));
    }

    private static bool IsDisableRumble() {
        return Manager.Running;
    }
}