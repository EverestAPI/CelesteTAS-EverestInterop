using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable once UnusedType.Global
public static class RestoreSettings {
    private static Settings origSettings;
    private static Assists? origAssists;
    private static bool backupAssists;
    private static Dictionary<EverestModule, object> origModSettings;

    // ReSharper disable once UnusedMember.Local
    [EnableRun]
    private static void TryBackup() {
        origSettings = null;
        origAssists = null;
        origModSettings = null;

        if (!TasSettings.RestoreSettings) {
            return;
        }

        origSettings = Settings.Instance.ShallowClone();

        if (SaveData.Instance != null) {
            origAssists = SaveData.Instance.Assists;
        } else {
            backupAssists = true;
        }

        origModSettings = new Dictionary<EverestModule, object>();
        foreach (EverestModule module in Everest.Modules) {
            if (module._Settings != null && module.SettingsType != null && module._Settings is not CelesteTasSettings) {
                origModSettings.Add(module, module._Settings.ShallowClone());
            }
        }
    }

    // ReSharper disable once UnusedMember.Local
    [DisableRun]
    private static void TryRestore() {
        backupAssists = false;

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
            foreach (EverestModule module in Everest.Modules) {
                try {
                    if (module?._Settings != null && origModSettings.TryGetValue(module, out object modSettings) && modSettings != null) {
                        module._Settings.CopyAllProperties(modSettings, true);
                        module._Settings.CopyAllFields(modSettings, true);
                    }
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
        if (origAssists == null && backupAssists) {
            backupAssists = false;
            origAssists = SaveData.Instance.Assists;
        }
    }
}