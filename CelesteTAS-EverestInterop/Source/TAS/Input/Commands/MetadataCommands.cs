using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Monocle;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class MetadataCommands {
    public static long? TasStartFileTime;

    [Load]
    private static void Load() {
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Level.UpdateTime += LevelOnUpdateTime;
        Everest.Events.Level.OnComplete += UpdateChapterTime;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Begin -= LevelOnBegin;
        On.Celeste.Level.UpdateTime -= LevelOnUpdateTime;
        Everest.Events.Level.OnComplete -= UpdateChapterTime;
    }

    private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
        orig(self);
        StartFileTime();
    }

    private static void LevelOnUpdateTime(On.Celeste.Level.orig_UpdateTime orig, Level self) {
        orig(self);
        StartFileTime();
    }

    private static void StartFileTime() {
        if (Manager.Running && TasStartFileTime == null) {
            TasStartFileTime = SaveData.Instance?.Time;
        }
    }

    [DisableRun]
    private static void UpdateFileTime() {
        if (TasStartFileTime != null && SaveData.Instance != null && !Manager.Controller.CanPlayback) {
            UpdateAllMetadata("FileTime", _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartFileTime.Value));
        }

        TasStartFileTime = null;
    }

    [TasCommand("RecordCount", AliasNames = new[] {"RecordCount:", "RecordCount："}, CalcChecksum = false)]
    private static void RecordCountCommand() {
        // dummy
    }

    [TasCommand("FileTime", AliasNames = new[] {"FileTime:", "FileTime："}, CalcChecksum = false)]
    private static void FileTimeCommand() {
        // dummy
    }

    [TasCommand("ChapterTime", AliasNames = new[] {"ChapterTime:", "ChapterTime："}, CalcChecksum = false)]
    private static void ChapterTimeCommand() {
        // dummy
    }

    [TasCommand("MidwayFileTime", AliasNames = new[] {"MidwayFileTime:", "MidwayFileTime："}, CalcChecksum = false)]
    private static void MidwayFileTimeCommand() {
        if (TasStartFileTime != null && SaveData.Instance != null) {
            UpdateAllMetadata("MidwayFileTime", 
                _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartFileTime.Value), 
                command => Manager.Controller.CurrentCommands.Contains(command));
        }
    }

    [TasCommand("MidwayChapterTime", AliasNames = new[] {"MidwayChapterTime:", "MidwayChapterTime："}, CalcChecksum = false)]
    private static void MidwayChapterTimeCommand() {
        if (!Manager.Running || Engine.Scene is not Level level || !level.Session.StartedFromBeginning) {
            return;
        }
        UpdateAllMetadata("MidwayChapterTime", 
            _ => GameInfo.GetChapterTime(level), 
            command => Manager.Controller.CurrentCommands.Contains(command));
    }

    private static void UpdateChapterTime(Level level) {
        if (!Manager.Running || !level.Session.StartedFromBeginning) {
            return;
        }

        UpdateAllMetadata("ChapterTime", _ => GameInfo.GetChapterTime(level));
    }

    public static void UpdateRecordCount(InputController inputController) {
        UpdateAllMetadata(
            "RecordCount",
            command => (int.Parse(command.Args.FirstOrDefault() ?? "0") + 1).ToString(),
            command => int.TryParse(command.Args.FirstOrDefault() ?? "0", out int _));
    }

    private static void UpdateAllMetadata(string commandName, Func<Command, string> getMetadata, Func<Command, bool> predicate = null) {
        InputController inputController = Manager.Controller;
        string tasFilePath = InputController.TasFilePath;
        IEnumerable<Command> metadataCommands = inputController.Commands.SelectMany(pair => pair.Value)
            .Where(command => command.Is(commandName) && command.FilePath == InputController.TasFilePath)
            .Where(predicate ?? (_ => true))
            .ToList();

        Dictionary<int, string> updateLines = metadataCommands.Where(command => {
            string metadata = getMetadata(command);
            if (metadata.IsNullOrEmpty()) {
                return false;
            }

            if (command.Args.Length > 0 && command.Args[0] == metadata) {
                return false;
            }

            return true;
        }).ToDictionary(command => command.StudioLineNumber, command => $"{command.Attribute.Name}: {getMetadata(command)}");

        if (updateLines.IsEmpty()) {
            return;
        }

        string[] allLines = File.ReadAllLines(tasFilePath);
        foreach (int lineNumber in updateLines.Keys) {
            allLines[lineNumber] = updateLines[lineNumber];
        }

        bool needsReload = Manager.Controller.NeedsReload;
        File.WriteAllLines(tasFilePath, allLines);
        Manager.Controller.NeedsReload = needsReload;
        StudioCommunicationClient.Instance?.UpdateLines(updateLines);
    }
}