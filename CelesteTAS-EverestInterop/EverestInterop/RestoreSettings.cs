using System.Collections.Generic;
using Celeste;
using Celeste.Mod;

namespace TAS.EverestInterop {
public static class RestoreSettings {
    private static Settings origSettings;
    private static Assists? origAssists;
    private static Dictionary<EverestModule, object> origModSettings;

    public static void TryBackup() {
        origSettings = null;
        origAssists = null;
        origModSettings = null;

        if (!CelesteTASModule.Settings.RestoreSettings) {
            return;
        }

        origSettings = Settings.Instance.ShallowClone();

        if (SaveData.Instance != null) {
            origAssists = SaveData.Instance.Assists;
        } else {
            On.Celeste.SaveData.Start -= SaveDataOnStart;
            On.Celeste.SaveData.Start += SaveDataOnStart;
        }

        origModSettings = new Dictionary<EverestModule, object>();
        foreach (EverestModule module in Everest.Modules) {
            if (module._Settings != null && module.SettingsType != null) {
                origModSettings.Add(module, module._Settings.ShallowClone());
            }
        }
    }

    public static void TryRestore() {
        On.Celeste.SaveData.Start -= SaveDataOnStart;

        if (origSettings != null) {
            Settings.Instance.CopyAllFields(origSettings);
            origSettings = null;
        }

        if (origAssists != null) {
            SaveData.Instance.Assists = origAssists.Value;
            origAssists = null;
        }

        if (origModSettings != null) {
            foreach (EverestModule module in Everest.Modules) {
                if (module._Settings != null && origModSettings.TryGetValue(module, out object modSettings) && modSettings != null) {
                    if (modSettings is CelesteTASModuleSettings backupTasSettings) {
                        CelesteTASModuleSettings tasSettings = CelesteTASModule.Settings;
                        backupTasSettings.HideTriggerHitboxes = tasSettings.HideTriggerHitboxes;
                        backupTasSettings.SimplifiedGraphics = tasSettings.SimplifiedGraphics;
                        backupTasSettings.CenterCamera = tasSettings.CenterCamera;
                    }

                    bool showHitbox = GameplayRendererExt.RenderDebug;

                    module._Settings.CopyAllProperties(modSettings);
                    module._Settings.CopyAllFields(modSettings);

                    GameplayRendererExt.RenderDebug = showHitbox;
                }
            }

            origModSettings = null;
        }
    }

    private static void SaveDataOnStart(On.Celeste.SaveData.orig_Start orig, SaveData data, int slot) {
        orig(data, slot);
        if (origAssists == null) {
            On.Celeste.SaveData.Start -= SaveDataOnStart;
            origAssists = SaveData.Instance.Assists;
        }
    }
}
}