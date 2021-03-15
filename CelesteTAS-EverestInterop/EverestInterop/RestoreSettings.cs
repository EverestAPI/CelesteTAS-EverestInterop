using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class RestoreSettings {
        private static readonly FieldInfo InputFeather = typeof(Celeste.Input).GetFieldInfo("Feather");
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
                Settings.Instance.CopyAllFields(origSettings);
                Settings.Instance.ApplyVolumes();
                Settings.Instance.ApplyScreen();
                Settings.Instance.ApplyLanguage();
                (Engine.Scene as Overworld)?.GetUI<OuiMainMenu>()?.CreateButtons();
                origSettings = null;
            }

            if (origAssists != null) {
                SaveData.Instance.Assists = origAssists.Value;
                Assists assists = SaveData.Instance.Assists;
                Engine.TimeRateB = assists.GameSpeed / 10f;
                Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = assists.MirrorMode;
                if (InputFeather?.GetValue(null) is VirtualJoystick featherJoystick) {
                    featherJoystick.InvertedX = assists.MirrorMode;
                }

                if (Engine.Scene.Tracker.GetEntity<Player>() is Player player) {
                    PlayerSpriteMode mode = assists.PlayAsBadeline ? PlayerSpriteMode.MadelineAsBadeline : player.DefaultSpriteMode;
                    if (player.Active) {
                        player.ResetSpriteNextFrame(mode);
                    } else {
                        player.ResetSprite(mode);
                    }

                    player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                }

                origAssists = null;
            }

            if (origModSettings != null) {
                foreach (EverestModule module in Everest.Modules) {
                    try {
                        if (module?._Settings != null && origModSettings.TryGetValue(module, out object modSettings) && modSettings != null) {
                            if (modSettings is CelesteTasModuleSettings backupTasSettings) {
                                CelesteTasModuleSettings tasSettings = CelesteTasModule.Settings;
                                backupTasSettings.HideTriggerHitboxes = tasSettings.HideTriggerHitboxes;
                                backupTasSettings.SimplifiedGraphics = tasSettings.SimplifiedGraphics;
                                backupTasSettings.CenterCamera = tasSettings.CenterCamera;
                            }

                            bool showHitbox = GameplayRendererExt.RenderDebug;

                            module._Settings.CopyAllProperties(modSettings);
                            module._Settings.CopyAllFields(modSettings);

                            GameplayRendererExt.RenderDebug = showHitbox;
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
}