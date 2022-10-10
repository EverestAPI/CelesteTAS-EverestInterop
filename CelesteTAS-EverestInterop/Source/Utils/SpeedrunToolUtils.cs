using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;

namespace TAS.Utils;

internal static class SpeedrunToolUtils {
    private static object saveLoadAction;
    private static Dictionary<Entity, EntityData> savedEntityData = new();

    public static void AddSaveLoadAction() {
        Action<Dictionary<Type, Dictionary<string, object>>, Level> save = (_, _) => {
            savedEntityData = EntityDataHelper.CachedEntityData.DeepCloneShared();
            InfoWatchEntity.SavedRequireWatchEntities = InfoWatchEntity.RequireWatchEntities.DeepCloneShared();
        };
        Action<Dictionary<Type, Dictionary<string, object>>, Level> load = (_, _) => {
            EntityDataHelper.CachedEntityData = savedEntityData.DeepCloneShared();
            InfoWatchEntity.RequireWatchEntities = InfoWatchEntity.SavedRequireWatchEntities.DeepCloneShared();
        };
        Action clear = () => {
            savedEntityData.Clear();
            InfoWatchEntity.SavedRequireWatchEntities.Clear();
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