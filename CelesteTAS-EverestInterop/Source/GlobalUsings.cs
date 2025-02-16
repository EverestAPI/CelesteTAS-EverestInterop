global using static TAS.GlobalVariables;
global using MonocleCommand = Monocle.Command;

global using Microsoft.Xna.Framework;
global using CelesteGame = Celeste.Celeste;

using TAS.Entities;
using TAS.Input;
using TAS.Module;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => CelesteTasSettings.Instance;
    public static bool ParsingCommand  => Command.Parsing;

    public static void AbortTas(string message, bool log = false, float duration = 2f) {
#if DEBUG
        // Always log in debug builds
        log = true;
#endif

        if (log) {
            Toast.ShowAndLog(message, duration);
        } else {
            Toast.Show(message, duration);
        }

        Manager.DisableRunLater();
    }
}
