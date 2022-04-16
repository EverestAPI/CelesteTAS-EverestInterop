using Celeste.Mod.SpeedrunTool.SaveLoad;
using TAS.EverestInterop;

namespace TAS.Utils;

internal static class SpeedrunToolUtils {
    private static SaveLoadAction saveLoadAction;

    public static void AddSaveLoadAction() {
        saveLoadAction = new SaveLoadAction(
            (_, _) => EntityDataHelper.SavedEntityData = EntityDataHelper.CachedEntityData.DeepCloneShared(),
            (_, _) => EntityDataHelper.CachedEntityData = EntityDataHelper.SavedEntityData.DeepCloneShared(),
            () => EntityDataHelper.SavedEntityData.Clear());
        SaveLoadAction.Add(saveLoadAction);
    }

    public static void ClearSaveLoadAction() {
        if (saveLoadAction != null) {
            SaveLoadAction.Remove(saveLoadAction);
        }
    }
}