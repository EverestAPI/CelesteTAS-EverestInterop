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
        }

        [Unload]
        private static void Unload() {
            Everest.Events.Level.OnComplete -= UpdateChapterTime;
        }

        [EnableRun]
        private static void StartFileTime() {
            tasStartFileTime = Savestates.IsSaved_Safe() ? null : SaveData.Instance?.Time;
        }

        [DisableRun]
        private static void StopFileTime() {
            if (tasStartFileTime != null && !Manager.Controller.CanPlayback) {
                UpdateAllMetadata(
                    Manager.Controller,
                    command => command.Attribute.IsName("FileTime") && command.FilePath == InputController.TasFilePath,
                    command => GameInfo.FormatTime(SaveData.Instance.Time - tasStartFileTime.Value)
                );
                tasStartFileTime = null;
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
        private static void ChapterCompleteTimeCommand(InputController inputController, string[] args, int lineNumber) {
            // dummy
        }

        private static void UpdateChapterTime(Level level) {
            if (!Manager.Running || !level.Session.StartedFromBeginning) {
                return;
            }

            UpdateAllMetadata(
                Manager.Controller,
                command => command.Attribute.IsName("ChapterTime") && command.FilePath == InputController.TasFilePath,
                command => GameInfo.GetChapterTime(level)
            );
        }

        public static void UpdateRecordCount(InputController inputController) {
            UpdateAllMetadata(
                inputController,
                command => command.Attribute.IsName("RecordCount") &&
                           command.FilePath == InputController.TasFilePath &&
                           int.TryParse(command.Args.FirstOrDefault() ?? "0", out int _),
                command => (int.Parse(command.Args.FirstOrDefault() ?? "0") + 1).ToString()
            );
        }

        private static void UpdateAllMetadata(InputController inputController, Func<Command, bool> predicate, Func<Command, string> getMetadata) {
            string tasFilePath = InputController.TasFilePath;
            IEnumerable<Command> metadataCommands = inputController.Commands.SelectMany(pair => pair.Value)
                .Where(predicate)
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

        #region ignore

        private static void WriteMetadata(InputController inputController, string[] args, int lineNumber, string name, string metadata) {
            if (metadata.IsNullOrEmpty()) {
                return;
            }

            if (args.Length > 0 && args[0] == metadata) {
                return;
            }

            string tasFilePath = InputController.TasFilePath;

            Dictionary<int, string> chapterTimeLines = new();
            string[] allLines = File.ReadAllLines(tasFilePath);
            allLines[lineNumber] = $"{name}: {metadata}";
            chapterTimeLines[lineNumber] = allLines[lineNumber];

            File.WriteAllLines(tasFilePath, allLines);
            if (inputController.UsedFiles.ContainsKey(tasFilePath)) {
                inputController.UsedFiles[tasFilePath] = File.GetLastWriteTime(tasFilePath);
            }

            StudioCommunicationClient.Instance?.UpdateLines(chapterTimeLines);
        }

        #endregion
    }
}