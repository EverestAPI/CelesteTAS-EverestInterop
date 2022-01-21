using System.IO;

namespace CelesteStudio {
    public static class WineUtils {
        private static bool? runningOnWine;

        public static bool RunningOnWine {
            get {
                runningOnWine ??= File.Exists("/proc/self/exe");
                return runningOnWine.Value;
            }
        }
    }
}