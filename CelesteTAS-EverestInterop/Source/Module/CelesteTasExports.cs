using Celeste.Mod;
using JetBrains.Annotations;
using MonoMod.ModInterop;
using System;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.Module;

// Copy-Paste the CelesteTasImports class into your mod and call typeof(CelesteTasImports).ModInterop() in EverestModule.Initialize()
// You can omit the [PublicAPI] attribute
[PublicAPI]
public static class CelesteTasImports {
    public delegate void AddSettingsRestoreHandlerDelegate(EverestModule module, (Func<object> Backup, Action<object> Restore)? handler);
    public delegate void RemoveSettingsRestoreHandlerDelegate(EverestModule module);

    /// Registers custom delegates for backing up and restoring mod setting before / after running a TAS
    /// A `null` handler causes the settings to not be backed up and later restored
    public static AddSettingsRestoreHandlerDelegate AddSettingsRestoreHandler = null!;

    /// De-registers a previously registered handler for the module
    public static RemoveSettingsRestoreHandlerDelegate RemoveSettingsRestoreHandler = null!;
}

/// Official stable API for interacting with CelesteTAS
[ModExportName("CelesteTAS"), PublicAPI]
public static class CelesteTasExports {
    [Load]
    private static void Load() {
        typeof(CelesteTasExports).ModInterop();
    }

    public static void AddSettingsRestoreHandler(EverestModule module, (Func<object> Backup, Action<object> Restore)? handler) {
        if (RestoreSettings.ignoredModules.Contains(module) || RestoreSettings.customHandlers.ContainsKey(module)) {
            $"Tried to register a custom setting-restore handler for mod '{module.Metadata.Name}', while already having a handler registered".Log(LogLevel.Warn);
            return;
        }

        if (handler == null) {
            RestoreSettings.ignoredModules.Add(module);
        } else {
            RestoreSettings.customHandlers[module] = handler.Value;
        }
    }
    public static void RemoveSettingsRestoreHandler(EverestModule module) {
        if (RestoreSettings.ignoredModules.Contains(module)) {
            RestoreSettings.ignoredModules.Remove(module);
        } else if (RestoreSettings.customHandlers.ContainsKey(module)) {
            RestoreSettings.customHandlers.Remove(module);
        } else {
            $"Tried to de-register a custom setting-restore handler for mod '{module.Metadata.Name}', without having a handler previously registered".Log(LogLevel.Warn);
        }
    }
}
