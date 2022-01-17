using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input {
    public static class MetadataCommands {
        private static long? tasStartFileTime;

        [Load]
        private static void Load() {
            Everest.Events.Level.OnComplete += UpdateChapterTime;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected += OuiFileSelectSlotOnOnNewGameSelected;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        }

        [Unload]
        private static void Unload() {
            Everest.Events.Level.OnComplete -= UpdateChapterTime;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected -= OuiFileSelectSlotOnOnNewGameSelected;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        }

        private static void OuiFileSelectSlotOnOnNewGameSelected(On.Celeste.OuiFileSelectSlot.orig_OnNewGameSelected orig, OuiFileSelectSlot self) {
            orig(self);
            if (Manager.Running) {
                tasStartFileTime = 0;
            }
        }

        private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session, Vector2? startPosition) {
            orig(self, session, startPosition);

            if (Manager.Running && !Savestates.IsSaved_Safe() && tasStartFileTime == null) {
                tasStartFileTime = SaveData.Instance?.Time;
            }
        }

        [EnableRun]
        private static void StartFileTime() {
            tasStartFileTime = Savestates.IsSaved_Safe() ? null : SaveData.Instance?.Time;
        }

        [DisableRun]
        private static void UpdateFileTime() {
            if (tasStartFileTime != null && SaveData.Instance != null && !Manager.Controller.CanPlayback) {
                UpdateAllMetadata("FileTime", _ => GameInfo.FormatTime(SaveData.Instance.Time - tasStartFileTime.Value));
            }
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

            File.WriteAllLines(tasFilePath, allLines);
            if (inputController.UsedFiles.ContainsKey(tasFilePath)) {
                inputController.UsedFiles[tasFilePath] = File.GetLastWriteTime(tasFilePath);
            }

            StudioCommunicationClient.Instance?.UpdateLines(updateLines);
        }
    }
}