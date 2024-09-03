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

    [EnableRun]
    private static void EnableRun() {
        foreach (MInput.GamePadData gamePadData in MInput.GamePads) {
            gamePadData.StopRumble();
        }
    }
}