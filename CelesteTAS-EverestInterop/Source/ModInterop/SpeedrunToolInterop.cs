using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.ModInterop;
using System.Diagnostics.CodeAnalysis;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;

namespace TAS.ModInterop;

public static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }
    private static object saveLoadHandle = null!;

    [Initialize]
    private static void Initialize() {
        typeof(SpeedrunToolSaveLoadImport).ModInterop();
        typeof(SpeedrunToolTasActionImports).ModInterop();

        Installed = CheckInstalled();
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
                savedValues[typeof(SpeedrunToolInterop)] = new Dictionary<string, object> {
                    {"savedEntityData", EntityDataHelper.CachedEntityData },
                    {"groupCounter", CycleHitboxColor.GroupCounter },
                    {"simulatePauses", StunPauseCommand.SimulatePauses },
                    {"pauseOnCurrentFrame", StunPauseCommand.PauseOnCurrentFrame },
                    {"skipFrames", StunPauseCommand.SkipFrames },
                    {"waitingFrames",StunPauseCommand.WaitingFrames },
                    {"localMode", StunPauseCommand.LocalMode },
                    {"globalModeRuntime", StunPauseCommand.GlobalModeRuntime },
                    {"pressKeys", PressCommand.PressKeys },
                    {"tasStartInfo", MetadataCommands.TasStartInfo },
                    {"mouseState", MouseCommand.CurrentState },
                    {"followers", HitboxSimplified.Followers},
                    {"disallowUnsafeInput", SafeCommand.DisallowUnsafeInput },
                    {"auraRandom", DesyncFixer.AuraHelperSharedRandom },
                    {"betterInvincible", Manager.Running && BetterInvincible.Invincible },
                }.DeepClone();
                InfoWatchEntity.WatchedEntities_Save = InfoWatchEntity.WatchedEntities.DeepClone();
                // if cleared by user manually, then it should not appear after load state, even if you load from another saveslot?
                // i'm not sure
            },
            (savedValues, _) => {
                var clonedValues = savedValues[typeof(SpeedrunToolInterop)].DeepClone();

                EntityDataHelper.CachedEntityData = (Dictionary<Entity, EntityData>)clonedValues["savedEntityData"];
                CycleHitboxColor.GroupCounter = (int)clonedValues["groupCounter"];
                StunPauseCommand.SimulatePauses = (bool)clonedValues["simulatePauses"];
                StunPauseCommand.PauseOnCurrentFrame = (bool)clonedValues["pauseOnCurrentFrame"];
                StunPauseCommand.SkipFrames = (int)clonedValues["skipFrames"];
                StunPauseCommand.WaitingFrames = (int)clonedValues["waitingFrames"];
                StunPauseCommand.LocalMode = (StunPauseCommand.StunPauseMode?)clonedValues["localMode"];
                StunPauseCommand.GlobalModeRuntime = (StunPauseCommand.StunPauseMode?)clonedValues["globalModeRuntime"];
                PressCommand.PressKeys.Clear();
                foreach (var keys in (HashSet<Keys>)clonedValues["pressKeys"]) {
                    PressCommand.PressKeys.Add(keys);
                }

                MetadataCommands.TasStartInfo = ((long FileTimeTicks, int FileSlot)?)clonedValues["tasStartInfo"];
                MouseCommand.CurrentState = (MouseState)clonedValues["mouseState"];
                HitboxSimplified.Followers = (Dictionary<Follower, bool>)clonedValues["followers"];
                SafeCommand.DisallowUnsafeInput = (bool)clonedValues["disallowUnsafeInput"];
                DesyncFixer.AuraHelperSharedRandom = (Random)clonedValues["auraRandom"];
                BetterInvincible.Invincible = Manager.Running && (bool)clonedValues["betterInvincible"];

                InfoWatchEntity.WatchedEntities = (List<WeakReference>)SpeedrunToolSaveLoadImport.DeepClone(InfoWatchEntity.WatchedEntities_Save);
            },
            () => {
                InfoWatchEntity.WatchedEntities_Save.Clear();
            }, null, null, null
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearSaveLoadAction() {
        if (Installed) {
            SpeedrunToolSaveLoadImport.Unregister!(saveLoadHandle);
        }
    }
}

[ModImportName("SpeedrunTool.SaveLoad")]
internal static class SpeedrunToolSaveLoadImport {
    public delegate void SaveLoadDelegate(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);
    public delegate object RegisterSaveLoadActionDelegate(
        SaveLoadDelegate saveState,
        SaveLoadDelegate loadState,
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
}
