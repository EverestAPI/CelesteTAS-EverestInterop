using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using System;
using System.Runtime.CompilerServices;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

/// Restores settings which were changed during
internal static class RestoreSettings {
    private static Settings? origSettings;
    private static Assists? origAssists;
    private static readonly Dictionary<EverestModule, object> origModSettings = new();
    private static readonly Dictionary<object, object?> origExtendedVariants = new();

    internal static readonly HashSet<EverestModule> ignoredModules = new();
    internal static readonly Dictionary<EverestModule, (Func<object> Backup, Action<object> Restore)> customHandlers = new();

    [EnableRun]
    private static void TryBackup() {
        origSettings = null;
        origAssists = null;

        if (!TasSettings.RestoreSettings) {
            return;
        }

        origSettings = Settings.Instance.ShallowClone();
        origAssists = SaveData.Instance?.Assists;

        origModSettings.Clear();
        foreach (var module in Everest.Modules) {
            if (module._Settings == null || module.SettingsType == null || module._Settings is CelesteTasSettings || module.Metadata.Name == "ExtendedVariantMode") {
                continue;
            }

            if (ignoredModules.Contains(module)) {
                continue;
            }
            if (customHandlers.TryGetValue(module, out var handler)) {
                origModSettings.Add(module, handler.Backup());
                continue;
            }

            // When using savestates, need to deep clone settings, to avoid issues with ButtonBindings breaking
            if (module._Settings.TryDeepClone(out var clonedSettings)) {
                origModSettings.Add(module, clonedSettings);
            } else {
                origModSettings.Add(module, module._Settings.ShallowClone());
            }
            continue;
        }

        origExtendedVariants.Clear();
        if (ExtendedVariantsInterop.GetVariantsEnum() is { } variantsEnum) {
            foreach (object variant in Enum.GetValues(variantsEnum)) {
                try {
                    origExtendedVariants[variant] = ExtendedVariantsInterop.GetCurrentVariantValue(new Lazy<object?>(variant));;
                } catch {
                    // ignore
                }
            }
        }
    }

    [DisableRun]
    private static void TryRestore() {
        if (origSettings != null) {
            Settings.Instance.CopyAllFields(origSettings);
            Settings.Instance.ApplyVolumes();
            Settings.Instance.ApplyLanguage();
            origSettings = null;
        }

        if (origAssists != null) {
            var oldAssists = SaveData.Instance.Assists;
            SaveData.Instance.Assists = origAssists.Value;
            AssistsQueryHandler.ApplyAssists(oldAssists, SaveData.Instance.Assists);
            origAssists = null;
        }

        if (origModSettings.IsNotEmpty()) {
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
                } catch (Exception ex) {
                    $"Failed to restore settings for mod '{module.Metadata.Name}'".Log(LogLevel.Warn);
                    ex.Log(LogLevel.Warn);
                }
            }

            origModSettings.Clear();
        }

        if (origExtendedVariants.IsNotEmpty()) {
            var variantsEnum = ExtendedVariantsInterop.GetVariantsEnum()!;
            foreach (object variant in Enum.GetValues(variantsEnum)) {
                try {
                    // Calling player.ResetSprite during StIntroWakeUp causes the player to be stuck in the state
                    string? name = variant.ToString();
                    if (name is "MadelineBackpackMode" or "PlayAsBadeline" && Engine.Scene.GetPlayer() is { } player && player.StateMachine.State == Player.StIntroWakeUp) {
                        continue;
                    }

                    if (origExtendedVariants.TryGetValue(variant, out object? origValue)) {
                        var lazyVariant = new Lazy<object?>(variant);
                        object? currValue = ExtendedVariantsInterop.GetCurrentVariantValue(lazyVariant);
                        if (currValue != origValue) {
                            ExtendedVariantsInterop.SetVariantValue(lazyVariant, origValue);
                        }
                    }
                } catch (Exception ex) {
                    $"Failed to restore value for Extended Variant '{variant}'".Log(LogLevel.Warn);
                    ex.Log(LogLevel.Warn);
                }
            }

            origExtendedVariants.Clear();
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
