using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Communication;
using TAS.Utils;

namespace TAS.Input {
    public static class MetadataCommands {
        [TasCommand(Name = "RecordCount", SavestateChecksum = false)]
        [TasCommand(Name = "RecordCount:", SavestateChecksum = false)]
        private static void RecordCountCommand(string[] args) {
            // dummy
        }

        [TasCommand(Name = "FileTime", SavestateChecksum = false)]
        [TasCommand(Name = "FileTime:", SavestateChecksum = false)]
        private static void FileTimeCommand(InputController inputController, string[] args, int lineNumber) {
            WriteMetadata(inputController, args, lineNumber, "FileTime", GameInfo.FileTime);
        }

        [TasCommand(Name = "ChapterTime", SavestateChecksum = false)]
        [TasCommand(Name = "ChapterTime:", SavestateChecksum = false)]
        private static void ChapterTimeCommand(InputController inputController, string[] args, int lineNumber) {
            WriteMetadata(inputController, args, lineNumber, "ChapterTime", GameInfo.ChapterTime);
        }

        [TasCommand(Name = "RoomName", SavestateChecksum = false)]
        [TasCommand(Name = "RoomName:", SavestateChecksum = false)]
        private static void RoomNameCommand(InputController inputController, string[] args, int lineNumber) {
            WriteMetadata(inputController, args, lineNumber, "RoomName", GameInfo.LevelName.IsNullOrEmpty() ? string.Empty : $"lvl_{GameInfo.LevelName}");
        }

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

        public static void UpdateRecordCount(InputController inputController) {
            string tasFilePath = InputController.TasFilePath;
            IEnumerable<Command> recordCountCommands = inputController.Commands.SelectMany(pair => pair.Value)
                .Where(command => (command.IsName("RecordCount") || command.IsName("RecordCount:")) &&
                    command.FilePath == tasFilePath &&
                    int.TryParse(command.Args.FirstOrDefault() ?? "0", out int _))
                .ToList();
            if (recordCountCommands.IsEmpty()) {
                return;
            }

            Dictionary<int, string> recordCountLines = new();
            string[] allLines = File.ReadAllLines(tasFilePath);
            foreach (Command command in recordCountCommands) {
                int lineNumber = command.LineNumber;
                int recordCount = int.Parse(command.Args.FirstOrDefault() ?? "0");
                allLines[lineNumber] = "RecordCount: " + (recordCount + 1);
                recordCountLines[lineNumber] = allLines[lineNumber];
            }

            File.WriteAllLines(tasFilePath, allLines);
            if (inputController.UsedFiles.ContainsKey(tasFilePath)) {
                inputController.UsedFiles[tasFilePath] = File.GetLastWriteTime(tasFilePath);
            }

            StudioCommunicationClient.Instance?.UpdateLines(recordCountLines);
        }
    }
}