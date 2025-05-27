using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.ModInterop;
using StudioCommunication.Util;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;

namespace TAS.ModInterop;

/// Mod-Interop with Speedrun Tool
internal static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }
    public static bool MultipleSaveSlotsSupported { get; private set; }

    private static object saveLoadHandle = null!;

    [Initialize]
    private static void Initialize() {
        typeof(SpeedrunToolSaveLoadImport).ModInterop();
        typeof(SpeedrunToolTasActionImports).ModInterop();

        Installed = CheckInstalled();

        // NOTE: SpeedrunToolTasActionImports first appeared in SRT v3.24.4
        //       In v3.24.4, everything is same as before, except that this mod-interop is added for compatibility issue
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
                savedValues[typeof(SpeedrunToolInterop)] = new Dictionary<string, object?> {
                    { nameof(GameInfo.LastPos), GameInfo.LastPos },
                    { nameof(GameInfo.LastDiff), GameInfo.LastDiff },
                    { nameof(GameInfo.LastPlayerSeekerPos), GameInfo.LastPlayerSeekerPos },
                    { nameof(GameInfo.LastPlayerSeekerDiff), GameInfo.LastPlayerSeekerDiff },

                    { nameof(EntityDataHelper.CachedEntityData), EntityDataHelper.CachedEntityData },
                    { nameof(CycleHitboxColor.GroupCounter), CycleHitboxColor.GroupCounter },
                    { nameof(StunPauseCommand.SimulatePauses), StunPauseCommand.SimulatePauses },
                    { nameof(StunPauseCommand.PauseOnCurrentFrame), StunPauseCommand.PauseOnCurrentFrame },
                    { nameof(StunPauseCommand.SkipFrames), StunPauseCommand.SkipFrames },
                    { nameof(StunPauseCommand.WaitingFrames), StunPauseCommand.WaitingFrames },
                    { nameof(StunPauseCommand.LocalMode), StunPauseCommand.LocalMode },
                    { nameof(StunPauseCommand.GlobalModeRuntime), StunPauseCommand.GlobalModeRuntime },
                    { nameof(PressCommand.PressKeys), PressCommand.PressKeys },
                    { nameof(MetadataCommands.TasStartInfo), MetadataCommands.TasStartInfo },
                    { nameof(MouseCommand.CurrentState), MouseCommand.CurrentState },
                    { nameof(HitboxSimplified.Followers), HitboxSimplified.Followers },
                    { nameof(SafeCommand.DisallowUnsafeInput), SafeCommand.DisallowUnsafeInput },
                    { nameof(DesyncFixer.AuraHelperSharedRandom), DesyncFixer.AuraHelperSharedRandom },
                    { nameof(BetterInvincible), Manager.Running && BetterInvincible.Invincible },
                }.DeepClone();

                // Store ourselves to be able to clear it, when the user asks to
                InfoWatchEntity.WatchedEntities_Save = InfoWatchEntity.WatchedEntities.DeepClone();
            },
            (savedValues, _) => {
                var clonedValues = savedValues[typeof(SpeedrunToolInterop)].DeepClone();

                GameInfo.LastPos = (Vector2Double) clonedValues[nameof(GameInfo.LastPos)]!;
                GameInfo.LastDiff = (Vector2Double) clonedValues[nameof(GameInfo.LastDiff)]!;
                GameInfo.LastPlayerSeekerPos = (Vector2Double) clonedValues[nameof(GameInfo.LastPlayerSeekerPos)]!;
                GameInfo.LastPlayerSeekerDiff = (Vector2Double) clonedValues[nameof(GameInfo.LastPlayerSeekerDiff)]!;

                EntityDataHelper.CachedEntityData = (Dictionary<Entity, EntityData>) clonedValues[nameof(EntityDataHelper.CachedEntityData)]!;
                CycleHitboxColor.GroupCounter = (int) clonedValues[nameof(CycleHitboxColor.GroupCounter)]!;
                StunPauseCommand.SimulatePauses = (bool) clonedValues[nameof(StunPauseCommand.SimulatePauses)]!;
                StunPauseCommand.PauseOnCurrentFrame = (bool) clonedValues[nameof(StunPauseCommand.PauseOnCurrentFrame)]!;
                StunPauseCommand.SkipFrames = (int) clonedValues[nameof(StunPauseCommand.SkipFrames)]!;
                StunPauseCommand.WaitingFrames = (int) clonedValues[nameof(StunPauseCommand.WaitingFrames)]!;
                StunPauseCommand.LocalMode = (StunPauseCommand.StunPauseMode?) clonedValues[nameof(StunPauseCommand.LocalMode)];
                StunPauseCommand.GlobalModeRuntime = (StunPauseCommand.StunPauseMode?) clonedValues[nameof(StunPauseCommand.GlobalModeRuntime)];

                PressCommand.PressKeys.Clear();
                PressCommand.PressKeys.AddRange((HashSet<Keys>) clonedValues[nameof(PressCommand.PressKeys)]!);

                MetadataCommands.TasStartInfo = ((long FileTimeTicks, int FileSlot)?) clonedValues[nameof(MetadataCommands.TasStartInfo)];
                MouseCommand.CurrentState = (MouseState) clonedValues[nameof(MouseCommand.CurrentState)]!;
                HitboxSimplified.Followers = (Dictionary<Follower, bool>) clonedValues[nameof(HitboxSimplified.Followers)]!;
                SafeCommand.DisallowUnsafeInput = (bool) clonedValues[nameof(SafeCommand.DisallowUnsafeInput)]!;
                DesyncFixer.AuraHelperSharedRandom = (Random) clonedValues[nameof(DesyncFixer.AuraHelperSharedRandom)]!;
                BetterInvincible.Invincible = Manager.Running && (bool) clonedValues[nameof(BetterInvincible)]!;

                InfoWatchEntity.WatchedEntities = InfoWatchEntity.WatchedEntities_Save.DeepClone();
            },
            () => {
                InfoWatchEntity.WatchedEntities_Save.Clear();
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
}
