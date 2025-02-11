using System;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Commands which don't influence gameplay at all and just provide information to the user
internal static class MetadataCommands {
    // Track starting conditions for TAS to properly calculate (Midway)FileTime
    public static (long FileTimeTicks, int FileSlot)? TasStartInfo;

    [Load]
    private static void Load() {
        Everest.Events.Level.OnComplete += UpdateChapterTime;

        typeof(Level)
            .GetMethodInfo(nameof(Level.Begin))!
            .HookAfter(StartFileTime);
        typeof(Level)
            .GetMethodInfo(nameof(Level.UpdateTime))!
            .HookAfter(StartFileTime);

        static void StartFileTime() {
            if (!Manager.Running || SaveData.Instance is not { } saveData) {
                return;
            }

            if (TasStartInfo == null || TasStartInfo.Value.FileSlot != saveData.FileSlot) {
                TasStartInfo = (saveData.Time, saveData.FileSlot);
            }
        }
    }

    [Unload]
    private static void Unload() {
        Everest.Events.Level.OnComplete -= UpdateChapterTime;
    }

    [DisableRun]
    private static void UpdateFileTime() {
        if (TasStartInfo != null && SaveData.Instance != null && !Manager.Controller.CanPlayback) {
            UpdateAllMetadata("FileTime", _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartInfo.Value.FileTimeTicks));
        }

        TasStartInfo = null;
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

    private class RecordCountMeta : ITasCommandMeta {
        public string Insert => "RecordCount: 1";
        public bool HasArguments => false;
    }

    [TasCommand("RecordCount", Aliases = ["RecordCount:", "RecordCount："], CalcChecksum = false, MetaDataProvider = typeof(RecordCountMeta))]
    private static void RecordCountCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("FileTime", Aliases = ["FileTime:", "FileTime："], CalcChecksum = false)]
    private static void FileTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("ChapterTime", Aliases = ["ChapterTime:", "ChapterTime："], CalcChecksum = false)]
    private static void ChapterTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // dummy
    }

    [TasCommand("MidwayFileTime", Aliases = ["MidwayFileTime:", "MidwayFileTime："], CalcChecksum = false)]
    private static void MidwayFileTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (TasStartInfo == null || SaveData.Instance == null) {
            return;
        }

        UpdateAllMetadata("MidwayFileTime",
            _ => GameInfo.FormatTime(SaveData.Instance.Time - TasStartInfo.Value.FileTimeTicks),
            command => Manager.Controller.CurrentCommands.Contains(command));
    }

    [TasCommand("MidwayChapterTime", Aliases = ["MidwayChapterTime:", "MidwayChapterTime："], CalcChecksum = false)]
    private static void MidwayChapterTimeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (!Manager.Running || Engine.Scene is not Level level) {
            return;
        }

        UpdateAllMetadata("MidwayChapterTime",
            _ => GameInfo.GetChapterTime(level),
            command => Manager.Controller.CurrentCommands.Contains(command));
    }

    private static void UpdateAllMetadata(string commandName, Func<Command, string> getMetadata, Func<Command, bool>? predicate = null) {
        string tasFilePath = Manager.Controller.FilePath;
        var metadataCommands = Manager.Controller.Commands.SelectMany(pair => pair.Value)
            .Where(command => command.Is(commandName) && command.FilePath == Manager.Controller.FilePath)
            .Where(predicate ?? (_ => true))
            .ToList();

        var updateLines = metadataCommands
            .Where(command => {
                string metadata = getMetadata(command);
                if (metadata.IsNullOrEmpty()) {
                    return false;
                }

                if (command.Args.Length > 0 && command.Args[0] == metadata) {
                    return false;
                }

                return true;
            })
            .ToDictionary(command => command.StudioLine, command => $"{command.Attribute.Name}: {getMetadata(command)}");

        if (updateLines.IsEmpty()) {
            return;
        }

        string[] allLines = File.ReadAllLines(tasFilePath);
        int allLinesLength = allLines.Length;
        foreach ((int lineNumber, string replacement) in updateLines) {
            if (lineNumber >= 0 && lineNumber < allLinesLength) {
                allLines[lineNumber] = replacement;
            }
        }

        // Prevent a reload from being triggered by the file-system change
        bool needsReload = Manager.Controller.NeedsReload;
        File.WriteAllLines(tasFilePath, allLines);
        Manager.Controller.NeedsReload = needsReload;

        CommunicationWrapper.SendUpdateLines(updateLines);
    }
}
