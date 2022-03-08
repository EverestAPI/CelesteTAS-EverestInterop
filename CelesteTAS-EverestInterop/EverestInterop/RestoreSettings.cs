using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Monocle;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable once UnusedType.Global
public static class RestoreSettings {
    private static Settings origSettings;
    private static Assists? origAssists;
    private static Dictionary<EverestModule, object> origModSettings;

    // ReSharper disable once UnusedMember.Local
    [EnableRun]
    private static void TryBackup() {
        origSettings = null;
        origAssists = null;
        origModSettings = null;

        if (!CelesteTasModule.Settings.RestoreSettings) {
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

    // ReSharper disable once UnusedMember.Local
    [DisableRun]
    private static void TryRestore() {
        On.Celeste.SaveData.Start -= SaveDataOnStart;

        if (origSettings != null) {
            Settings.Instance.CopyAllFields(origSettings, true);
            Settings.Instance.ApplyVolumes();
            Settings.Instance.ApplyScreen();
            Settings.Instance.ApplyLanguage();
            (Engine.Scene as Overworld)?.GetUI<OuiMainMenu>()?.CreateButtons();
            origSettings = null;
        }

        if (origAssists != null) {
            SaveData.Instance.Assists = origAssists.Value;
            SetCommandHandler.ResetVariants(origAssists.Value);
            origAssists = null;
        }

        if (origModSettings != null) {
            foreach (EverestModule module in Everest.Modules) {
                try {
                    if (module?._Settings != null && origModSettings.TryGetValue(module, out object modSettings) && modSettings != null) {
                        bool showHitbox = CelesteTasModule.Settings.ShowHitboxes;

                        if (modSettings is CelesteTasModuleSettings backupTasSettings) {
                            CelesteTasModuleSettings tasSettings = CelesteTasModule.Settings;
                            backupTasSettings.ShowTriggerHitboxes = tasSettings.ShowTriggerHitboxes;
                            backupTasSettings.ShowActualCollideHitboxes = tasSettings.ShowActualCollideHitboxes;
                            backupTasSettings.SimplifiedGraphics = tasSettings.SimplifiedGraphics;
                            backupTasSettings.ShowGameplay = tasSettings.ShowGameplay;
                            backupTasSettings.CenterCamera = tasSettings.CenterCamera;
                            backupTasSettings.InfoHud = tasSettings.InfoHud;
                            backupTasSettings.InfoCustomTemplate = tasSettings.InfoCustomTemplate;
                            backupTasSettings.PositionDecimals = tasSettings.PositionDecimals;
                            backupTasSettings.SpeedDecimals = tasSettings.SpeedDecimals;
                            backupTasSettings.VelocityDecimals = tasSettings.VelocityDecimals;
                            backupTasSettings.CustomInfoDecimals = tasSettings.CustomInfoDecimals;
                        }

                        module._Settings.CopyAllProperties(modSettings);
                        module._Settings.CopyAllFields(modSettings, true);

                        CelesteTasModule.Settings.ShowHitboxes = showHitbox;
                    }
                } catch (NullReferenceException) {
                    // maybe caused by hot reloading
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