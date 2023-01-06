global using static TAS.GlobalVariables;
using TAS.Entities;
using TAS.Module;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => CelesteTasSettings.Instance;

    public static void AbortTas(string message, bool log = false) {
        if (log) {
            Toast.ShowAndLog(message);
        } else {
            Toast.Show(message);
        }

        Manager.DisableRunLater();
    }
}