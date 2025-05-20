using Celeste.Mod;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.ModInterop;
using System;
using System.Linq;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Utils;

namespace TAS.Module;

/* How to use the CelesteTAS ModInterop API:
 *  1. Copy-Paste the CelesteTasImports class into your mod
 *  2. Remove the [PublicAPI] attribute if you aren't using JetBrains' annotations
 *  3. Add the [ModImportName("CelesteTAS")] attribute to the class
 *  4. Call typeof(CelesteTasImports).ModInterop() in EverestModule.Initialize()
 */

[PublicAPI]
public static class CelesteTasImports {
    public delegate void AddSettingsRestoreHandlerDelegate(EverestModule module, (Func<object> Backup, Action<object> Restore)? handler);
    public delegate void RemoveSettingsRestoreHandlerDelegate(EverestModule module);
    public delegate void DrawAccurateLineDelegate(Vector2 from, Vector2 to, Color color);

    /// Checks if a TAS is active (i.e. running / paused / etc.)
    public static Func<bool> IsTasActive = null!;

    /// Checks if a TAS is currently actively running (i.e. not paused)
    public static Func<bool> IsTasRunning = null!;

    /// Checks if the current TAS is being recorded with TAS Recorder
    public static Func<bool> IsTasRecording = null!;

    /// Registers custom delegates for backing up and restoring mod setting before / after running a TAS
    /// A `null` handler causes the settings to not be backed up and later restored
    public static AddSettingsRestoreHandlerDelegate AddSettingsRestoreHandler = null!;

    /// De-registers a previously registered handler for the module
    public static RemoveSettingsRestoreHandlerDelegate RemoveSettingsRestoreHandler = null!;

    #region Savestates

    public delegate object? GetLatestSavestateForFrameDelegate(int frame);
    public delegate int GetSavestateFrameDelegate(object savestate);
    public delegate bool LoadSavestateDelegate(object savestate);

    /// Provides an opaque savestate-handle to the latest savestate before or at the specified frame.
    /// Returns null if no savestate is found
    public static GetLatestSavestateForFrameDelegate GetLatestSavestateForFrame = null!;

    /// Provides the frame into the TAS for the specified savestate-handle
    public static GetSavestateFrameDelegate GetSavestateFrame = null!;

    /// Attempts to load the specified savestate-handle. Returns whether it was successful
    public static LoadSavestateDelegate LoadSavestate = null!;

    #endregion

    #region Rendering

    /// <summary>
    /// Draws an exact line, filling all pixels the line actually intersects. <br/>
    /// Based on the logic of <see cref="Collide.RectToLine(float,float,float,float,Microsoft.Xna.Framework.Vector2,Microsoft.Xna.Framework.Vector2)">Collide.RectToLine</see> and with the assumption that other colliders are grid-aligned.
    /// </summary>
    ///
    /// <remarks>
    /// Available since CelesteTAS v3.44.0
    /// </remarks>
    public static DrawAccurateLineDelegate DrawAccurateLine = null!;

    #endregion
}

/// Official stable API for interacting with CelesteTAS
[ModExportName("CelesteTAS"), PublicAPI]
public static class CelesteTasExports {
    [Load]
    private static void Load() {
        typeof(CelesteTasExports).ModInterop();
    }

    public static bool IsTasActive() => Manager.Running;
    public static bool IsTasRunning() => Manager.CurrState == Manager.State.Running;
    public static bool IsTasRecording() => TASRecorderInterop.IsRecording;

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

    public static object? GetLatestSavestateForFrame(int frame) {
        foreach (var state in SavestateManager.AllSavestates.Reverse()) {
            if (frame <= state.Frame) {
                return state;
            }
        }

        return null;
    }
    public static int GetSavestateFrame(object savestate) {
        var state = (SavestateManager.Savestate) savestate;
        return state.Frame;
    }
    public static bool LoadSavestate(object savestate) {
        var state = (SavestateManager.Savestate) savestate;
        return state.Load();
    }

    public static void DrawAccurateLine(Vector2 from, Vector2 to, Color color) => HitboxFixer.DrawExactLine(from, to, color);
}
