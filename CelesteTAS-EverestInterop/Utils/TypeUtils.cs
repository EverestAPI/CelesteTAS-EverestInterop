using System;
using Celeste.Mod.Helpers;

namespace TAS.Utils {
    public static class TypeUtils {
        public static Type GetType(string name, bool throwOnError = false, bool ignoreCase = false) {
            return FakeAssembly.GetFakeEntryAssembly().GetType(name, throwOnError, ignoreCase);
        }

        public static Type[] GetTypes() {
            return FakeAssembly.GetFakeEntryAssembly().GetTypes();
        }
    }
}