global using SavestateData = System.Collections.Generic.Dictionary<string, object?>;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Microsoft.Xna.Framework.Input;
using MonoMod.ModInterop;
using StudioCommunication.Util;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.ModInterop;

/// Invoked with a <c>Dictionary&lt;string, object&gt;</c> to which relevant data should be saved.
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class SaveStateAttribute(int priority = 0) : EventAttribute(priority);

/// Invoked with a <c>Dictionary&lt;string, object&gt;</c> from which previously saved data should be retrieved.
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class LoadStateAttribute(int priority = 0) : EventAttribute(priority);

/// Invoked when savestate data is cleared
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ClearStateAttribute(int priority = 0) : EventAttribute(priority);

/// Mod-Interop with Speedrun Tool
internal static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }
    public static bool MultipleSaveSlotsSupported { get; private set; }

    private static object saveLoadHandle = null!;

    [Initialize]
    private static void Initialize() {
        typeof(SpeedrunToolSaveLoadImport).ModInterop();
        typeof(SpeedrunToolTasActionImports).ModInterop();

        AttributeUtils.CollectOwnMethods<SaveStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<LoadStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<ClearStateAttribute>();

        Installed = CheckInstalled();

        // NOTE: SpeedrunToolTasActionImports first appeared in SRT v3.24.4
        //       In v3.24.4, everything is same as before, except that this mod-interop is added for compatibility reasons
        //       After v3.25.0, SRT supports multiple saveslots
        if (Everest.Modules.FirstOrDefault(module => module.Metadata.Name == "SpeedrunTool") is { } srtModule) {
            MultipleSaveSlotsSupported = srtModule.Metadata.Version >= new Version(3, 25, 0);
        }

#if DEBUG
        Everest.Events.AssetReload.OnBeforeReload += _ => {
            if (Installed) {
                ClearSaveLoadAction();
            }

            Installed = false;
        };
        Everest.Events.AssetReload.OnAfterReload += _ => {
            Installed = CheckInstalled();

            if (Installed) {
                AddSaveLoadAction();
            }
        };
#endif

        if (Installed) {
            AddSaveLoadAction();
        }

        return;

        static bool CheckInstalled() => SpeedrunToolSaveLoadImport.DeepClone != null && SpeedrunToolTasActionImports.SaveState != null;
    }
    [Unload]
    private static void Unload() {
        if (Installed) {
            ClearSaveLoadAction();
        }
    }

    [DisableRun]

    private static void OnTasDisableRun() {
        SpeedrunToolTasActionImports.OnTasDisableRun?.Invoke();
    }

    public const string DefaultSlot = "CelesteTAS";

    /// Saves the current state into the specified slot. Returns whether it was successful
    public static bool SaveState(string? slot = null) => SpeedrunToolTasActionImports.SaveState?.Invoke(slot ?? DefaultSlot) ?? false;

    /// Loads the specified slot into the current state. Returns whether it was successful
    public static bool LoadState(string? slot = null) => SpeedrunToolTasActionImports.LoadState?.Invoke(slot ?? DefaultSlot) ?? false;

    /// Clears the specified save slot
    public static void ClearState(string? slot = null) => SpeedrunToolTasActionImports.ClearState?.Invoke(slot ?? DefaultSlot);

    /// Checks if something is saved in the specified save slot
    public static bool IsSaved(string? slot = null) => SpeedrunToolTasActionImports.TasIsSaved?.Invoke(slot ?? DefaultSlot) ?? false;

    /// Creates a deep clone of the object. Crashes if SpeedrunTool isn't installed.
    public static T DeepClone<T>(this T from) where T: notnull {
        return (T) SpeedrunToolSaveLoadImport.DeepClone!(from);
    }
    /// Attempts to create a deep clone of the object. Fails if SpeedrunTool isn't installed.
    public static bool TryDeepClone<T>(this T from, [NotNullWhen(true)] out T? to) where T : notnull {
        if (SpeedrunToolSaveLoadImport.DeepClone is { } deepClone) {
            to = (T) deepClone(from);
            return true;
        }

        to = default;
        return false;
    }

    private static void AddSaveLoadAction() {
        if (!Installed) {
            return;
        }

        saveLoadHandle = SpeedrunToolSaveLoadImport.RegisterSaveLoadAction!(
            (savedValues, _) => {
                var saveData = new SavestateData {
                    { nameof(GameInfo.LastPos), GameInfo.LastPos },
                    { nameof(GameInfo.LastDiff), GameInfo.LastDiff },
                    { nameof(GameInfo.LastPlayerSeekerPos), GameInfo.LastPlayerSeekerPos },
                    { nameof(GameInfo.LastPlayerSeekerDiff), GameInfo.LastPlayerSeekerDiff },

                    { nameof(CycleHitboxColor.GroupCounter), CycleHitboxColor.GroupCounter },
                    { nameof(StunPauseCommand.SimulatePauses), StunPauseCommand.SimulatePauses },
                    { nameof(StunPauseCommand.PauseOnCurrentFrame), StunPauseCommand.PauseOnCurrentFrame },
                    { nameof(StunPauseCommand.SkipFrames), StunPauseCommand.SkipFrames },
                    { nameof(StunPauseCommand.WaitingFrames), StunPauseCommand.WaitingFrames },
                    { nameof(StunPauseCommand.LocalMode), StunPauseCommand.LocalMode },
                    { nameof(StunPauseCommand.GlobalModeRuntime), StunPauseCommand.GlobalModeRuntime },
                    { nameof(PressCommand.PressKeys), PressCommand.PressKeys },
                    { nameof(MouseCommand.CurrentState), MouseCommand.CurrentState },
                    { nameof(HitboxSimplified.Followers), HitboxSimplified.Followers },
                    { nameof(SafeCommand.DisallowUnsafeInput), SafeCommand.DisallowUnsafeInput },
                    { nameof(BetterInvincible), Manager.Running && BetterInvincible.Invincible },
                };
                AttributeUtils.Invoke<SaveStateAttribute>(saveData);

                savedValues[typeof(SpeedrunToolInterop)] = saveData.DeepClone();

                // Store ourselves to be able to clear it, when the user asks to
                InfoWatchEntity.WatchedEntities_Save = InfoWatchEntity.WatchedEntities.DeepClone();
            },
            (savedValues, _) => {
                var saveData = savedValues[typeof(SpeedrunToolInterop)].DeepClone();

                GameInfo.LastPos = (Vector2Double) saveData[nameof(GameInfo.LastPos)]!;
                GameInfo.LastDiff = (Vector2Double) saveData[nameof(GameInfo.LastDiff)]!;
                GameInfo.LastPlayerSeekerPos = (Vector2Double) saveData[nameof(GameInfo.LastPlayerSeekerPos)]!;
                GameInfo.LastPlayerSeekerDiff = (Vector2Double) saveData[nameof(GameInfo.LastPlayerSeekerDiff)]!;

                CycleHitboxColor.GroupCounter = (int) saveData[nameof(CycleHitboxColor.GroupCounter)]!;
                StunPauseCommand.SimulatePauses = (bool) saveData[nameof(StunPauseCommand.SimulatePauses)]!;
                StunPauseCommand.PauseOnCurrentFrame = (bool) saveData[nameof(StunPauseCommand.PauseOnCurrentFrame)]!;
                StunPauseCommand.SkipFrames = (int) saveData[nameof(StunPauseCommand.SkipFrames)]!;
                StunPauseCommand.WaitingFrames = (int) saveData[nameof(StunPauseCommand.WaitingFrames)]!;
                StunPauseCommand.LocalMode = (StunPauseCommand.StunPauseMode?) saveData[nameof(StunPauseCommand.LocalMode)];
                StunPauseCommand.GlobalModeRuntime = (StunPauseCommand.StunPauseMode?) saveData[nameof(StunPauseCommand.GlobalModeRuntime)];

                PressCommand.PressKeys.Clear();
                PressCommand.PressKeys.AddRange((HashSet<Keys>) saveData[nameof(PressCommand.PressKeys)]!);

                MouseCommand.CurrentState = (MouseState) saveData[nameof(MouseCommand.CurrentState)]!;
                HitboxSimplified.Followers = (Dictionary<Follower, bool>) saveData[nameof(HitboxSimplified.Followers)]!;
                SafeCommand.DisallowUnsafeInput = (bool) saveData[nameof(SafeCommand.DisallowUnsafeInput)]!;
                BetterInvincible.Invincible = Manager.Running && (bool) saveData[nameof(BetterInvincible)]!;

                InfoWatchEntity.WatchedEntities = InfoWatchEntity.WatchedEntities_Save.DeepClone();

                AttributeUtils.Invoke<LoadStateAttribute>(saveData);
            },
            () => {
                InfoWatchEntity.WatchedEntities_Save.Clear();

                AttributeUtils.Invoke<ClearStateAttribute>();
            },
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearSaveLoadAction() {
        if (Installed) {
            SpeedrunToolSaveLoadImport.Unregister!(saveLoadHandle);
        }
    }

    internal static void TryInputDeregister() {
        SpeedrunToolTasActionImports.InputDeregister?.Invoke();
    }
}

// Fields will be assigned through ModInterop
#pragma warning disable CS0649

[ModImportName("SpeedrunTool.SaveLoad")]
internal static class SpeedrunToolSaveLoadImport {
    public delegate object RegisterSaveLoadActionDelegate(
        Action<Dictionary<Type, Dictionary<string, object?>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object?>>, Level> loadState,
        Action clearState,
        Action<Level>? beforeSaveState,
        Action<Level>? beforeLoadState,
        Action? preCloneEntities);
    public delegate object RegisterStaticTypesDelegate(Type type, string[] memberNames);
    public delegate object DeepCloneDelegate(object from);

    /// Registers a new save-load action
    /// Provides an opaque handle to unregister later
    public static RegisterSaveLoadActionDelegate? RegisterSaveLoadAction;
    /// Specifies which static members should be cloned
    /// Provides an opaque handle to unregister later
    public static RegisterStaticTypesDelegate? RegisterStaticTypes;

    /// Unregisters a previously registered object with the returned handle
    public static Action<object>? Unregister;

    /// Creates a deep recursive clone of the object and returns it
    public static DeepCloneDelegate? DeepClone;
}

[ModImportName("SpeedrunTool.TasAction")]
internal static class SpeedrunToolTasActionImports {
    public static Func<string, bool>? SaveState;
    public static Func<string, bool>? LoadState;
    public static Action<string>? ClearState;
    public static Func<string, bool>? TasIsSaved;
    public static Action? InputDeregister;
    public static Action? OnTasDisableRun;
}
