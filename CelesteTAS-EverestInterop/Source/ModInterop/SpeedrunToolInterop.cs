using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.ModInterop;

public static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }

    private static object? saveLoadAction;

    private static Dictionary<Entity, EntityData>? savedEntityData;
    private static int groupCounter;
    private static bool simulatePauses;
    private static bool pauseOnCurrentFrame;
    private static int skipFrames;
    private static int waitingFrames;
    private static StunPauseCommand.StunPauseMode? localMode;
    private static StunPauseCommand.StunPauseMode? globalModeRuntime;
    private static HashSet<Keys>? pressKeys;
    private static (long, int)? tasStartInfo;
    private static MouseState mouseState;
    private static Dictionary<Follower, bool>? followers;
    private static bool disallowUnsafeInput;
    private static Random? auraRandom;
    private static bool betterInvincible = false;

    [Initialize]
    private static void Initialize() {
        Installed = ModUtils.IsInstalled("SpeedrunTool");
        Everest.Events.AssetReload.OnBeforeReload += _ => {
            if (Installed) {
                ClearSaveLoadAction();
            }

            Installed = false;
        };
        Everest.Events.AssetReload.OnAfterReload += _ => {
            Installed = ModUtils.IsInstalled("SpeedrunTool");

            if (Installed) {
                AddSaveLoadAction();
            }
        };

        if (Installed) {
            AddSaveLoadAction();
        }
    }
    [Unload]
    private static void Unload() {
        if (Installed) {
            ClearSaveLoadAction();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddSaveLoadAction() {
        saveLoadAction = new SaveLoadAction(OnSave, OnLoad, OnClear);
        SaveLoadAction.Add((SaveLoadAction) saveLoadAction);

        return;

        static void OnSave(Dictionary<Type, Dictionary<string, object>> savedValues, Level level) {
            savedEntityData = EntityDataHelper.CachedEntityData.DeepCloneShared();
            InfoWatchEntity.WatchedEntities_Save = InfoWatchEntity.WatchedEntities.DeepCloneShared();
            groupCounter = CycleHitboxColor.GroupCounter;
            simulatePauses = StunPauseCommand.SimulatePauses;
            pauseOnCurrentFrame = StunPauseCommand.PauseOnCurrentFrame;
            skipFrames = StunPauseCommand.SkipFrames;
            waitingFrames = StunPauseCommand.WaitingFrames;
            localMode = StunPauseCommand.LocalMode;
            globalModeRuntime = StunPauseCommand.GlobalModeRuntime;
            pressKeys = PressCommand.PressKeys.DeepCloneShared();
            tasStartInfo = MetadataCommands.TasStartInfo.DeepCloneShared();
            mouseState = MouseCommand.CurrentState;
            followers = HitboxSimplified.Followers.DeepCloneShared();
            disallowUnsafeInput = SafeCommand.DisallowUnsafeInput;
            auraRandom = DesyncFixer.AuraHelperSharedRandom.DeepCloneShared();
            betterInvincible = Manager.Running && BetterInvincible.Invincible;
        }

        static void OnLoad(Dictionary<Type, Dictionary<string, object>> savedValues, Level level) {
            EntityDataHelper.CachedEntityData = savedEntityData!.DeepCloneShared();
            InfoWatchEntity.WatchedEntities = InfoWatchEntity.WatchedEntities_Save.DeepCloneShared();
            CycleHitboxColor.GroupCounter = groupCounter;
            StunPauseCommand.SimulatePauses = simulatePauses;
            StunPauseCommand.PauseOnCurrentFrame = pauseOnCurrentFrame;
            StunPauseCommand.SkipFrames = skipFrames;
            StunPauseCommand.WaitingFrames = waitingFrames;
            StunPauseCommand.LocalMode = localMode;
            StunPauseCommand.GlobalModeRuntime = globalModeRuntime;
            PressCommand.PressKeys.Clear();
            foreach (var keys in pressKeys!) {
                PressCommand.PressKeys.Add(keys);
            }

            MetadataCommands.TasStartInfo = tasStartInfo.DeepCloneShared();
            MouseCommand.CurrentState = mouseState;
            HitboxSimplified.Followers = followers!.DeepCloneShared();
            SafeCommand.DisallowUnsafeInput = disallowUnsafeInput;
            DesyncFixer.AuraHelperSharedRandom = auraRandom!.DeepCloneShared();
            BetterInvincible.Invincible = Manager.Running && betterInvincible;
        }

        static void OnClear() {
            savedEntityData = null;
            pressKeys = null;
            followers = null;
            InfoWatchEntity.WatchedEntities_Save.Clear();
            auraRandom = null;
            betterInvincible = false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearSaveLoadAction() {
        if (saveLoadAction != null) {
            SaveLoadAction.Remove((SaveLoadAction) saveLoadAction);
        }
    }

    public static void InputDeregister() {
        Dictionary<Hotkey, HotkeyConfig> hotkeyConfigs = typeof(HotkeyConfigUi).GetFieldValue<Dictionary<Hotkey, HotkeyConfig>>("HotkeyConfigs")!;
        foreach (HotkeyConfig config in hotkeyConfigs.Values) {
            config.VirtualButton.Value.Deregister();
        }
    }
}
