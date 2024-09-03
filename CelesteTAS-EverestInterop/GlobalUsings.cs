global using static TAS.GlobalVariables;
using TAS.Entities;
using TAS.Input.Commands;
using TAS.Module;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => CelesteTasSettings.Instance;
    public static bool ParsingCommand  => Command.Parsing;

    public static void AbortTas(string message, bool log = false, float duration = 2f) {
        if (log) {
            Toast.ShowAndLog(message, duration);
        } else {
            Toast.Show(message, duration);
        }

        Manager.DisableRunLater();
    }
}