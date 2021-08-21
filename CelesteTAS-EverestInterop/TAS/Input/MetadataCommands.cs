using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.Input {
    public static class MetadataCommands {
        private static long? tasStartFileTime;

        [Load]
        private static void Load() {
            Everest.Events.Level.OnComplete += UpdateChapterTime;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected += OuiFileSelectSlotOnOnNewGameSelected;
        }

        [Unload]
        private static void Unload() {
            Everest.Events.Level.OnComplete -= UpdateChapterTime;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected -= OuiFileSelectSlotOnOnNewGameSelected;
        }

        private static void OuiFileSelectSlotOnOnNewGameSelected(On.Celeste.OuiFileSelectSlot.orig_OnNewGameSelected orig, OuiFileSelectSlot self) {
            orig(self);
            if (Manager.Running) {
                tasStartFileTime = 0;
            }
        }

        [EnableRun]
        private static void StartFileTime() {
            tasStartFileTime = Savestates.IsSaved_Safe() ? null : SaveData.Instance?.Time;
        }

        [DisableRun]
        private static void UpdateFileTime() {
            if (tasStartFileTime != null && SaveData.Instance != null && !Manager.Controller.CanPlayback) {
                UpdateAllMetadata("FileTime", command => GameInfo.FormatTime(SaveData.Instance.Time - tasStartFileTime.Value));
            }
        }

        [TasCommand(Name = "RecordCount", AliasNames = new[] {"RecordCount:"}, SavestateChecksum = false)]
        private static void RecordCountCommand(string[] args) {
            // dummy
        }

        [TasCommand(Name = "FileTime", AliasNames = new[] {"FileTime:"}, SavestateChecksum = false)]
        private static void FileTimeCommand(InputController inputController, string[] args, int lineNumber) {
            // dummy
        }

        [TasCommand(Name = "ChapterTime", AliasNames = new[] {"ChapterTime:"}, SavestateChecksum = false)]
        private static void ChapterTimeCommand(InputController inputController, string[] args, int lineNumber) {
            // dummy
        }

        private static void UpdateChapterTime(Level level) {
            if (!Manager.Running || !level.Session.StartedFromBeginning) {
                return;
            }

            UpdateAllMetadata("ChapterTime", command => GameInfo.GetChapterTime(level));
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
                .Where(command => command.Attribute.IsName(commandName) && command.FilePath == InputController.TasFilePath)
                .Where(predicate ?? (_ => true))
                .ToList();
            if (metadataCommands.IsEmpty()) {
                return;
            }

            Dictionary<int, string> updateLines = new();
            string[] allLines = File.ReadAllLines(tasFilePath);
            foreach (Command command in metadataCommands) {
                string metadata = getMetadata(command);
                if (metadata.IsNullOrEmpty()) {
                    continue;
                }

                if (command.Args.Length > 0 && command.Args[0] == metadata) {
                    continue;
                }

                int lineNumber = command.LineNumber;
                allLines[lineNumber] = $"{command.Attribute.Name}: {getMetadata(command)}";
                updateLines[lineNumber] = allLines[lineNumber];
            }

            File.WriteAllLines(tasFilePath, allLines);
            if (inputController.UsedFiles.ContainsKey(tasFilePath)) {
                inputController.UsedFiles[tasFilePath] = File.GetLastWriteTime(tasFilePath);
            }

            StudioCommunicationClient.Instance?.UpdateLines(updateLines);
        }
    }
}