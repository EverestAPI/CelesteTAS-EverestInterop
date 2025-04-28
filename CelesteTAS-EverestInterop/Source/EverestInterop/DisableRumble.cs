using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class DisableRumble {
    [Load]
    private static void Load() {
        typeof(MInput.GamePadData).GetMethodInfo("Rumble")!.SkipMethod(IsDisableRumble);
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
