using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.Input;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class MonocleCommands {
    private const string PlayTAS = "playtas";
    private static readonly Regex SeparatorRegex = new(@$"^{PlayTAS}[ |,]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Monocle.Command("clrsav", "clears save data on debug file (CelesteTAS)")]
    private static void CmdClearSave() {
        SaveData.TryDelete(-1);
        SaveData.Start(new SaveData {Name = "debug"}, -1);
        // Pretend that we've beaten Prologue.
        LevelSetStats stats = SaveData.Instance.GetLevelSetStatsFor("Celeste");
        stats.UnlockedAreas = 1;
        stats.AreasIncludingCeleste[0].Modes[0].Completed = true;
    }

    [Monocle.Command("hearts",
        "sets the amount of obtained hearts for the specified level set to a given number (default all hearts and current level set) (support mini heart door via CelesteTAS)")]
    private static void CmdHearts(int amount = int.MaxValue, string? levelSet = null) {
        SaveData saveData = SaveData.Instance;
        if (saveData == null) {
            return;
        }

        if (string.IsNullOrEmpty(levelSet)) {
            const string miniHeartDoorFullName = "Celeste.Mod.CollabUtils2.Entities.MiniHeartDoor";

            if (Engine.Scene.Entities.FirstOrDefault(e => e.GetType().FullName == miniHeartDoorFullName) is HeartGemDoor door) {
                levelSet = door.GetFieldValue<string>("levelSet");
                if (door.Scene is Level level && amount < door.Requires) {
                    level.Session.SetFlag($"opened_mini_heart_door_{door.GetEntityData()!.ToEntityId()}", false);
                    ModUtils.GetModule("CollabUtils2")
                        ?.GetPropertyValue<object>("SaveData")
                        ?.GetPropertyValue<HashSet<string>>("OpenedMiniHeartDoors")
                        ?.Remove(door.InvokeMethod<string>("GetDoorSaveDataID", level)!);
                }
            } else {
                levelSet = saveData.LevelSet;
            }
        }

        int num = 0;
        foreach (AreaStats areaStats in saveData.Areas_Safe.Where(stats => stats.LevelSet == levelSet)) {
            for (int i = 0; i < areaStats.Modes.Length; i++) {
                if (AreaData.Get(areaStats.ID).Mode is not { } mode || mode.Length <= i || mode[i]?.MapData == null)
                    continue;

                AreaModeStats areaModeStats = areaStats.Modes[i];
                if (num < amount) {
                    areaModeStats.HeartGem = true;
                    num++;
                } else {
                    areaModeStats.HeartGem = false;
                }
            }
        }
    }

    [Monocle.Command(PlayTAS, "play the specified tas file (CelesteTAS)")]
    private static void CmdPlayTas(string filePath) {
        filePath = SeparatorRegex.Replace(Engine.Commands.commandHistory.First(), "");

        if (filePath.IsNullOrEmpty()) {
            Engine.Commands.Log("Please specified tas file.");
            return;
        }

        if (!File.Exists(filePath)) {
            Engine.Commands.Log("File does not exist.");
            return;
        }

        Manager.DisableRun();
        Manager.Controller.FilePath = filePath;
        Manager.EnableRun();
    }
}
