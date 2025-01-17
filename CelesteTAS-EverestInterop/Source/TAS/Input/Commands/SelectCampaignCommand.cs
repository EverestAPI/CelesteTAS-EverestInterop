using Celeste;
using Celeste.Mod.Core;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Prepares a new save-file with the specified campaign selected, hovering over the "Begin" button
internal static class SelectCampaignCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"SelectCampaign{CommandInfo.Separator}[0;Celeste]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length > 1) {
                yield break;
            }

            foreach (string levelSet in AreaData.Areas.Select(area => area.LevelSet).Distinct()) {
                if (string.IsNullOrWhiteSpace(levelSet)) {
                    continue;
                }

                yield return levelSet;
            }
        }
    }

    /// Next available save-file slot
    private static int EmptyFileSlot {
        get {
            if (LibTasHelper.Exporting) {
                return -1;
            }

            // Taken from Everest
            int maxSaveFile;
            if (CoreModule.Settings.MaxSaveSlots != null) {
                maxSaveFile = Math.Max(3, CoreModule.Settings.MaxSaveSlots.Value);
            } else {
                // first load: we want to check how many slots there are by checking which files exist in the Saves folder.
                maxSaveFile = 1; // we're adding 2 later, so there will be at least 3 slots.
                string saveFilePath = UserIO.GetSaveFilePath();
                if (Directory.Exists(saveFilePath)) {
                    foreach (string filePath in Directory.GetFiles(saveFilePath)) {
                        string fileName = Path.GetFileName(filePath);
                        // is the file named [number].celeste?
                        if (fileName.EndsWith(".celeste") && int.TryParse(fileName.Substring(0, fileName.Length - 8), out int fileIndex)) {
                            maxSaveFile = Math.Max(maxSaveFile, fileIndex);
                        }
                    }
                }

                // if 2.celeste exists, slot 3 is the last slot filled, therefore we want 4 slots (2 + 2) to always have the latest one empty.
                maxSaveFile += 2;
            }

            bool hasSlots = false;
            int firstEmpty = int.MaxValue;

            for (int slot = 0; slot < maxSaveFile; slot++) {
                if (!UserIO.Exists(SaveData.GetFilename(slot))) {
                    firstEmpty = Math.Min(slot, firstEmpty);
                } else {
                    hasSlots = true;
                }
            }

            if (hasSlots) {
                return firstEmpty;
            }

            // Having no slots requires additional selection logic
            return -1;
        }
    }

    // SelectCampaign, CampaignName, FileName
    [TasCommand("SelectCampaign", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    public static void SelectCampaign(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        var controller = Manager.Controller;

        if (!ParsingCommand) {
            // Ensure inputs are up-to-date
            controller.RefreshInputs(forceRefresh: true);
            return;
        }

        if (Engine.Scene is GameLoader { loaded: false }) {
            return; // Wait until loading is done
        }

        if (commandLine.Arguments.Length == 0) {
            AbortTas("No campaign specified");
            return;
        }

        string campaignName = commandLine.Arguments[0];
        string saveFileName = commandLine.Arguments.Length >= 2 ? commandLine.Arguments[1] : "TAS";

        if (AreaData.Areas.All(area => area.LevelSet != campaignName)) {
            AbortTas($"Unknown campaign '{campaignName}'");
            return;
        }
        if (saveFileName.Length < OuiFileNaming.MinNameLength || saveFileName.Length > OuiFileNaming.MaxNameLengthNormal) {
            AbortTas($"Save-File name must be between {OuiFileNaming.MinNameLength} and {OuiFileNaming.MaxNameLengthNormal} characters long");
            return;
        }

        if (controller.CurrentParsingFrame != 0) {
            AbortTas("SelectCampaign command must be at beginning of file");
            return;
        }

        controller.ReadLine("Unsafe", filePath, fileLine, studioLine);
        controller.ReadLine("console overworld", filePath, fileLine, studioLine);
        controller.AddFrames("2", studioLine);
        LibTasHelper.AddInputFrame("1,O");
        LibTasHelper.AddInputFrame("89");
        controller.AddFrames("1,O", studioLine);
        controller.AddFrames("94", studioLine);
        controller.AddFrames("1,O", studioLine);

        int slot = EmptyFileSlot;
        if (slot == -1) {
            controller.AddFrames("62", studioLine);
        } else {
            controller.AddFrames("56", studioLine);
            for (int i = 0; i < slot; i++) {
                controller.AddFrames(i % 2 == 0 ? "1,D" : "1,F,180", studioLine);
            }
            controller.AddFrames("1,O", studioLine);
            controller.AddFrames("15", studioLine);
        }

        // Runtime-assert for additional safety
        controller.ReadLine("Assert,Equal,True,[[local ui = scene.Current; return ui ~= nil and ui.SlotSelected and not ui.Slots[ui.SlotIndex].Exists and getValue(ui.Slots[ui.SlotIndex], \"buttonIndex\") == 0]]", filePath, fileLine, studioLine);

        controller.AddFrames("1,D", studioLine);
        InputName(controller, saveFileName, studioLine);

        SelectCampaign(controller, campaignName, studioLine);

        // Runtime-assert for even more additional safety
        controller.ReadLine("Assert,Equal,True,[[local ui = scene.Current; return ui ~= nil and ui.SlotSelected and not ui.Slots[ui.SlotIndex].Exists and getValue(ui.Slots[ui.SlotIndex], \"buttonIndex\") == 0]]", filePath, fileLine, studioLine);
    }

    private static void InputName(InputController controller, string saveFileName, int studioLine) {
        // Use real OuiFileNaming as reference
        var fileNaming = new OuiFileNaming();
        fileNaming.ReloadLetters(Dialog.Clean("name_letters"));
        fileNaming.optionsScale = 0.75f;
        fileNaming.cancel = Dialog.Clean("name_back");
        fileNaming.space = Dialog.Clean("name_space");
        fileNaming.backspace = Dialog.Clean("name_backspace");
        fileNaming.accept = Dialog.Clean("name_accept");
        fileNaming.cancelWidth = ActiveFont.Measure(fileNaming.cancel).X * fileNaming.optionsScale;
        fileNaming.spaceWidth = ActiveFont.Measure(fileNaming.space).X * fileNaming.optionsScale;
        fileNaming.backspaceWidth = ActiveFont.Measure(fileNaming.backspace).X * fileNaming.optionsScale;
        fileNaming.beginWidth = ActiveFont.Measure(fileNaming.accept).X * fileNaming.optionsScale * 1.25f;
        fileNaming.optionsWidth = fileNaming.cancelWidth + fileNaming.spaceWidth + fileNaming.backspaceWidth + fileNaming.beginWidth + fileNaming.widestLetter * 3.0f;

        if (fileNaming.Japanese) {
            AbortTas("Japanese language is currently not supported for inputting a file name");
            return;
        }

        controller.AddFrames("1,O", studioLine);
        controller.AddFrames("41", studioLine);

        // Is a BFS for writing out a name overcomplicating it?
        foreach (char targetChar in saveFileName) {
            int targetLine = Array.FindIndex(fileNaming.letters, line => line.Contains(targetChar));
            if (targetLine == -1) {
                AbortTas($"Character '{targetChar}' not available in current language");
                return;
            }

            int targetIndex = fileNaming.letters[targetLine].IndexOf(targetChar);
            if (targetIndex == -1) {
                AbortTas($"Character '{targetChar}' not available in current language");
                return;
            }

            // Search for optimal path
            var start = (X: fileNaming.index, Y: fileNaming.line);
            var end = (X: targetIndex, Y: targetLine);

            var queue = new Queue<(int X, int Y)>();
            var visited = new HashSet<(int X, int Y)>();
            var parent = new Dictionary<(int X, int Y), (int X, int Y)>();

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.TryDequeue(out var current)) {
                if (current == end) {
                    break; // Done
                }

                // Check neighbours
                foreach ((int dx, int dy) in (ReadOnlySpan<(int, int)>)[(-1, 0), (1, 0), (0, 1), (0, -1)]) {
                    int nx = current.X;
                    int ny = current.Y;

                    // Skip whitespace while moving
                    do {
                        // Handle horizontal wrapping. Avoid snapping while moving vertically
                        if (dx != 0) {
                            nx = (nx + dx).Mod(fileNaming.letters[ny].Length);
                        }

                        ny += dy;

                        if (ny < 0 || ny >= fileNaming.letters.Length) {
                            goto NextNeighbour; // Current neighbour has gone out-of-bounds
                        }
                    } while (nx >= fileNaming.letters[ny].Length || char.IsWhiteSpace(fileNaming.letters[ny][nx]));

                    var next = (nx, ny);

                    if (visited.Add(next)) {
                        queue.Enqueue(next);
                        parent[next] = current; // Track how we reached this cell
                    }

                    NextNeighbour:;
                }
            }

            fileNaming.index = targetIndex;
            fileNaming.line = targetLine;

            if (!visited.Contains(end)) {
                AbortTas($"Failed to write out name '{saveFileName}'");
                return;
            }

            // Reconstruct path
            var path = new List<(int MoveX, int MoveY)>();
            var currentCell = end;

            while (currentCell != start) {
                var prevCell = parent[currentCell];

                // Manually handle horizontal wrapping
                if (prevCell.X == 0 && currentCell.X == fileNaming.letters[currentCell.Y].Length - 1) {
                    path.Add((-1, currentCell.Y - prevCell.Y));
                } else if (currentCell.X == 0 && prevCell.X == fileNaming.letters[currentCell.Y].Length - 1) {
                    path.Add((1, currentCell.Y - prevCell.Y));
                } else {
                    path.Add((currentCell.X - prevCell.X, currentCell.Y - prevCell.Y));
                }
                currentCell = prevCell;
            }

            path.Reverse();

            // Write inputs
            foreach ((int moveX, int moveY) in path) {
                if (moveX > 0) {
                    controller.AddFrames(controller.Inputs.Count % 2 == 0 ? "1,R" : "1,F,90", studioLine);
                } else if (moveX < 0) {
                    controller.AddFrames(controller.Inputs.Count % 2 == 0 ? "1,L" : "1,F,270", studioLine);
                }

                if (moveY > 0) {
                    controller.AddFrames(controller.Inputs.Count % 2 == 0 ? "1,D" : "1,F,180", studioLine);
                } else if (moveY < 0) {
                    controller.AddFrames(controller.Inputs.Count % 2 == 0 ? "1,U" : "1,F,0", studioLine);
                }
            }

            controller.AddFrames(controller.Inputs[^1].Actions.Has(Actions.Confirm) ? "1,J" : "1,O", studioLine);
        }

        // Pressing pause finishes editing
        controller.AddFrames("1,S", studioLine);
        controller.AddFrames("48", studioLine);
    }

    private static void SelectCampaign(InputController controller, string campaignName, int studioLine) {
        string[] levelSets = AreaData.Areas.Select(area => area.LevelSet).Distinct().ToArray();

        int currentIndex = Array.FindIndex(levelSets, set => set == CoreModule.Settings.DefaultStartingLevelSet);
        if (currentIndex == -1) {
            currentIndex = Array.FindIndex(levelSets, set => set == "Celeste");
        }

        int targetIndex = Array.FindIndex(levelSets, set => set == campaignName);

        int shiftRight = currentIndex < targetIndex ? targetIndex - currentIndex : currentIndex - targetIndex;
        int shiftLeft = (levelSets.Length + shiftRight) % levelSets.Length;

        if (shiftRight == 0 && shiftLeft == 0) {
            // No need to change campaign. Return back to "Begin"
            controller.AddFrames("1,U", studioLine);
            return;
        }

        // "Rename" is currently selected
        controller.AddFrames("1,D", studioLine);
        controller.AddFrames("1,F,180", studioLine);

        if (shiftRight >= shiftLeft) {
            for (int i = 0; i < shiftRight; i++) {
                controller.AddFrames(i % 2 == 0 ? "1,R" : "1,F,90", studioLine);
            }
        } else {
            for (int i = 0; i < shiftLeft; i++) {
                controller.AddFrames(i % 2 == 0 ? "1,L" : "1,F,270", studioLine);
            }
        }

        // Return back to "Begin"
        controller.AddFrames("1,U", studioLine);
        controller.AddFrames("1,F,0", studioLine);
        controller.AddFrames("1,U", studioLine);
    }
}
