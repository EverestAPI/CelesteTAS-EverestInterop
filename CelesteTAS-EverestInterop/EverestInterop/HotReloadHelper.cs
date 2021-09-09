using System;
using System.IO;
using Celeste.Mod;
using MonoMod.Utils;

#if DEBUG
namespace TAS.EverestInterop {
    public static class HotReloadHelper {
        private static FileSystemWatcher watcher;

        [Load]
        private static void Load() {
            EverestModuleMetadata meta = CelesteTasModule.Instance.Metadata;
            try {
                watcher = new FileSystemWatcher {
                    Path = Path.GetDirectoryName(meta.DLL),
                    NotifyFilter = NotifyFilters.LastWrite,
                };

                watcher.Changed += (s, e) => {
                    if (e.FullPath == meta.DLL && Manager.Running) {
                        Manager.DisableExternal();
                    }
                };

                watcher.EnableRaisingEvents = true;
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "CelesteTAS/HotReloadHelper", $"Failed watching folder: {Path.GetDirectoryName(meta.DLL)}");
                e.LogDetailed();
                Unload();
            }
        }

        [Unload]
        private static void Unload() {
            watcher?.Dispose();
            watcher = null;
        }
    }
}
#endif