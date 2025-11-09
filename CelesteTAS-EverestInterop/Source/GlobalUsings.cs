global using static TAS.GlobalVariables;
global using MonocleCommand = Monocle.Command;
global using GameInput = Celeste.Input;
using Celeste.Mod;
using TAS.Input;
using TAS.Module;
using TAS.Playback;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => CelesteTasSettings.Instance;
    public static bool ParsingCommand  => Command.Parsing;

    public static void AbortTas(string message, bool log = false, float duration = PopupToast.DefaultDuration) {
#if DEBUG
        // Always log in debug builds
        log = true;
#endif

        if (log) {
            PopupToast.ShowAndLog(message, duration, LogLevel.Error);
        } else {
            PopupToast.Show(message, duration);
        }

        Manager.DisableRunLater();
    }
}
