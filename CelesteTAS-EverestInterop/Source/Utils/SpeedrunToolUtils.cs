using System.Collections.Generic;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;

namespace TAS.Utils;

internal static class SpeedrunToolUtils {
    private static SaveLoadAction saveLoadAction;
    private static Dictionary<Entity, EntityData> savedEntityData = new();

    public static void AddSaveLoadAction() {
        saveLoadAction = new SaveLoadAction(
            (_, _) => {
                savedEntityData = EntityDataHelper.CachedEntityData.DeepCloneShared();
                InfoWatchEntity.SavedRequireWatchEntities = InfoWatchEntity.RequireWatchEntities.DeepCloneShared();
            },
            (_, _) => {
                EntityDataHelper.CachedEntityData = savedEntityData.DeepCloneShared();
                InfoWatchEntity.RequireWatchEntities = InfoWatchEntity.SavedRequireWatchEntities.DeepCloneShared();
            },
            () => {
                savedEntityData.Clear();
                InfoWatchEntity.SavedRequireWatchEntities.Clear();
            });
        SaveLoadAction.Add(saveLoadAction);
    }

    public static void ClearSaveLoadAction() {
        if (saveLoadAction != null) {
            SaveLoadAction.Remove(saveLoadAction);
        }
    }
}