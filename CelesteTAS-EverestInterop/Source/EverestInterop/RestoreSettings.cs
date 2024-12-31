using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using System;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class RestoreSettings {
    private static Settings? origSettings;
    private static Assists? origAssists;
    private static Dictionary<EverestModule, object>? origModSettings;

    internal static readonly HashSet<EverestModule> ignoredModules = new();
    internal static readonly Dictionary<EverestModule, (Func<object> Backup, Action<object> Restore)> customHandlers = new();

    [EnableRun]
    private static void TryBackup() {
        origSettings = null;
        origAssists = null;
        origModSettings = null;

        if (!TasSettings.RestoreSettings) {
            return;
        }

        origSettings = Settings.Instance.ShallowClone();
        origAssists = SaveData.Instance?.Assists;

        origModSettings = new Dictionary<EverestModule, object>();
        foreach (var module in Everest.Modules) {
            if (module._Settings == null || module.SettingsType == null || module._Settings is CelesteTasSettings) {
                continue;
            }

            if (ignoredModules.Contains(module)) {
                continue;
            }
            if (customHandlers.TryGetValue(module, out var handler)) {
                origModSettings.Add(module, handler.Backup());
                continue;
            }

            origModSettings.Add(module, module._Settings.ShallowClone());
        }
    }

    [DisableRun]
    private static void TryRestore() {
        if (origSettings != null) {
            Settings.Instance.CopyAllFields(origSettings);
            Settings.Instance.ApplyVolumes();
            Settings.Instance.ApplyScreen();
            Settings.Instance.ApplyLanguage();
            origSettings = null;
        }

        if (origAssists != null) {
            SaveData.Instance.Assists = origAssists.Value;
            SetCommand.ResetVariants(origAssists.Value);
            origAssists = null;
        }

        if (origModSettings != null) {
            TasSettings.Enabled = true;
            TasSettings.RestoreSettings = true;

            foreach (var module in Everest.Modules) {
                try {
                    if (module._Settings == null || !origModSettings.TryGetValue(module, out object? modSettings)) {
                        continue;
                    }

                    if (ignoredModules.Contains(module)) {
                        continue;
                    }
                    if (customHandlers.TryGetValue(module, out var handler)) {
                        handler.Restore(modSettings);
                        continue;
                    }

                    module._Settings.CopyAllProperties(modSettings, true);
                    module._Settings.CopyAllFields(modSettings, true);
                } catch {
                    // ignored
                }
            }

            origModSettings = null;
        }
    }

    [Load]
    private static void Load() {
        On.Celeste.SaveData.Start += SaveDataOnStart;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.SaveData.Start -= SaveDataOnStart;
    }

    private static void SaveDataOnStart(On.Celeste.SaveData.orig_Start orig, SaveData data, int slot) {
        orig(data, slot);

        // The TAS might've been started outside a save file, so backup save-data now
        origAssists ??= SaveData.Instance.Assists;
    }
}
