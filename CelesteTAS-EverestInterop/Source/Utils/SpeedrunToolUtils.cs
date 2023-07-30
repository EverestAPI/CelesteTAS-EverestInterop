using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.EverestInterop.InfoHUD;
using TAS.Input.Commands;

namespace TAS.Utils;

internal static class SpeedrunToolUtils {
    private static object saveLoadAction;
    private static Dictionary<Entity, EntityData> savedEntityData;
    private static int groupCounter;
    private static bool simulatePauses;
    private static bool pauseOnCurrentFrame;
    private static int skipFrames;
    private static int waitingFrames;
    private static StunPauseCommand.StunPauseMode? localMode;
    private static StunPauseCommand.StunPauseMode? globalModeRuntime;
    private static HashSet<Keys> pressKeys;
    private static long? tasStartFileTime;
    private static MouseState mouseState;
    private static Dictionary<Follower, bool> followers;

    public static void AddSaveLoadAction() {
        Action<Dictionary<Type, Dictionary<string, object>>, Level> save = (_, _) => {
            savedEntityData = EntityDataHelper.CachedEntityData.DeepCloneShared();
            groupCounter = CycleHitboxColor.GroupCounter;
            simulatePauses = StunPauseCommand.SimulatePauses;
            pauseOnCurrentFrame = StunPauseCommand.PauseOnCurrentFrame;
            skipFrames = StunPauseCommand.SkipFrames;
            waitingFrames = StunPauseCommand.WaitingFrames;
            localMode = StunPauseCommand.LocalMode;
            globalModeRuntime = StunPauseCommand.GlobalModeRuntime;
            pressKeys = PressCommand.PressKeys.DeepCloneShared();
            tasStartFileTime = MetadataCommands.TasStartFileTime;
            mouseState = MouseCommand.CurrentState;
            followers = HitboxSimplified.Followers.DeepCloneShared();
        };
        Action<Dictionary<Type, Dictionary<string, object>>, Level> load = (_, _) => {
            EntityDataHelper.CachedEntityData = savedEntityData.DeepCloneShared();
            InfoWatchEntity.RefreshWatchEntities();
            CycleHitboxColor.GroupCounter = groupCounter;
            StunPauseCommand.SimulatePauses = simulatePauses;
            StunPauseCommand.PauseOnCurrentFrame = pauseOnCurrentFrame;
            StunPauseCommand.SkipFrames = skipFrames;
            StunPauseCommand.WaitingFrames = waitingFrames;
            StunPauseCommand.LocalMode = localMode;
            StunPauseCommand.GlobalModeRuntime = globalModeRuntime;
            PressCommand.PressKeys.Clear();
            foreach (Keys keys in pressKeys) {
                PressCommand.PressKeys.Add(keys);
            }

            MetadataCommands.TasStartFileTime = tasStartFileTime;
            MouseCommand.CurrentState = mouseState;
            HitboxSimplified.Followers = followers.DeepCloneShared();
        };
        Action clear = () => {
            savedEntityData = null;
            pressKeys = null;
            followers = null;
        };

        ConstructorInfo constructor = typeof(SaveLoadAction).GetConstructors()[0];
        Type delegateType = constructor.GetParameters()[0].ParameterType;

        saveLoadAction = constructor.Invoke(new object[] {
                save.Method.CreateDelegate(delegateType, save.Target),
                load.Method.CreateDelegate(delegateType, load.Target),
                clear,
                null,
                null
            }
        );
        SaveLoadAction.Add((SaveLoadAction) saveLoadAction);
    }

    public static void ClearSaveLoadAction() {
        if (saveLoadAction != null) {
            SaveLoadAction.Remove((SaveLoadAction) saveLoadAction);
        }
    }

    public static void InputDeregister() {
        Dictionary<Hotkey, HotkeyConfig> hotkeyConfigs = typeof(HotkeyConfigUi).GetFieldValue<Dictionary<Hotkey, HotkeyConfig>>("HotkeyConfigs");
        foreach (HotkeyConfig config in hotkeyConfigs.Values) {
            config.VirtualButton.Value.Deregister();
        }
    }
}