using Celeste.Mod;
using JetBrains.Annotations;
using MonoMod.ModInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.Module;

// Copy-Paste the CelesteTasImports class into your mod and call typeof(CelesteTasImports).ModInterop() in EverestModule.Initialize()
// You can omit the [PublicAPI] attribute
[PublicAPI]
public static class CelesteTasImports {
    public delegate void AddSettingsRestoreHandlerDelegate(EverestModule module, (Func<object> Backup, Action<object> Restore)? handler);
    public delegate void RemoveSettingsRestoreHandlerDelegate(EverestModule module);

    public delegate void RegisterHudWindowHandlerDelegate(
        Func<bool> visibleProvider,
        Func<IEnumerable<string>> textProvider,
        Func<Vector2>? loadPosition,
        Action<Vector2>? storePosition,
        (Func<bool> visibleProvider, Func<Vector2> sizeProvider, Action<Vector2, float> render)[] renderers
    );

    /// Registers custom delegates for backing up and restoring mod setting before / after running a TAS <br/>
    /// A <c>null</c> handler causes the settings to not be backed up and later restored
    public static AddSettingsRestoreHandlerDelegate AddSettingsRestoreHandler = null!;

    /// De-registers a previously registered handler for the module
    public static RemoveSettingsRestoreHandlerDelegate RemoveSettingsRestoreHandler = null!;

    /// Register a handler for displaying a custom HUD window, like the Info HUD. <br/>
    /// Visibility is <b>not</b> tied to the CelesteTAS Info HUD being visible. It must be manually handled by the callback.
    public static RegisterHudWindowHandlerDelegate RegisterHudWindowHandler = null!;
}

/// Official stable API for interacting with CelesteTAS
[ModExportName("CelesteTAS"), PublicAPI]
internal static class CelesteTasExports {
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

    public static void RegisterHudWindowHandler(
        Func<bool> visibleProvider,
        Func<IEnumerable<string>> textProvider,
        Func<Vector2>? loadPosition,
        Action<Vector2>? storePosition,
        (Func<bool> visibleProvider, Func<Vector2> sizeProvider, Action<Vector2, float> render)[] renderers
    ) {
        var handler = new WindowManager.Handler(
                visibleProvider, textProvider, loadPosition, storePosition,
                renderers
                    .Select(entry => new WindowManager.Renderer(entry.visibleProvider, entry.sizeProvider, entry.render))
                    .ToArray());

        WindowManager.Register(handler);
    }
}
