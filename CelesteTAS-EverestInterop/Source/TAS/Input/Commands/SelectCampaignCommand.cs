using Celeste;
using Celeste.Mod.Core;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.ModInterop;
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
                if (CollabUtils2Interop.Lobby.IsCollabLevelSet?.Invoke(levelSet) ?? false) {
                    continue;
                }

                yield return levelSet;
            }
        }
    }

    private static int MaxSaveFileSlots {
        get {
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

            return maxSaveFile;
        }
    }

    /// Next available save-file slot
    private static int EmptyFileSlot {
        get {
            if (LibTasHelper.Exporting) {
                return -1;
            }

            bool hasSlots = false;
            int firstEmpty = int.MaxValue;

            int maxSaveFile = MaxSaveFileSlots;
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

        if (!Command.Parsing) {
            if (EnforceLegalCommand.EnabledWhenRunning && Engine.Scene is not Overworld { Current: OuiTitleScreen }) {
                AbortTas("SelectCampaign command must start on title screen when using EnforceLegal");
                return;
            }

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
        if (CollabUtils2Interop.Lobby.IsCollabLevelSet?.Invoke(campaignName) ?? false) {
            AbortTas($"Invalid campaign '{campaignName}'");
            return;
        }
        if (saveFileName.Length < OuiFileNaming.MinNameLength || saveFileName.Length > OuiFileNaming.MaxNameLengthNormal) {
            AbortTas($"Save-File name must be between {OuiFileNaming.MinNameLength} and {OuiFileNaming.MaxNameLengthNormal} characters long");
            return;
        }
        if (saveFileName[0] == ' ') {
            AbortTas("Save-File name cannot start with a space");
            return;
        }

        controller.ReadLine("Unsafe", filePath, fileLine, studioLine);
        controller.ReadLine("console titlescreen", filePath, fileLine, studioLine);

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
        InputName(controller, slot, saveFileName, studioLine);

        ChangeSelectedCampaign(controller, campaignName, studioLine);

        // Runtime-assert for even more additional safety
        controller.ReadLine("Assert,Equal,True,[[local ui = scene.Current; return ui ~= nil and ui.SlotSelected and not ui.Slots[ui.SlotIndex].Exists and getValue(ui.Slots[ui.SlotIndex], \"buttonIndex\") == 0]]", filePath, fileLine, studioLine);
    }

    private static void InputName(InputController controller, int slot, string saveFileName, int studioLine) {
        if (Settings.Instance.Language == "japanese") {
            AbortTas("Japanese language is currently not supported for inputting a file name");
            return;
        }

        controller.AddFrames("1,O", studioLine);

        int maxSaveFile = MaxSaveFileSlots;
        for (int i = 0; i < maxSaveFile; i++) {
            if (Math.Abs(slot - i) <= 2) {
                // Each visible slots causes a delay of 3f
                controller.AddFrames("3", studioLine);
            }
        }
        controller.AddFrames("32", studioLine);

        // Prepare available letters
        string[] letters = Dialog.Clean("name_letters")
            .SplitLines()
            .ToArray();

        int maxLetterLength = letters
            .Select(line => line.Length)
            .Aggregate(Math.Max);

        // The "space" button is larger than a single char
        float widestLetter = letters
            .SelectMany(line => line)
            .Select(c => ActiveFont.Measure(c).X)
            .Aggregate(Math.Max);

        const float optionsScale = 0.75f;
        float cancelWidth = ActiveFont.Measure(Dialog.Clean("name_back")).X * optionsScale;
        float spaceWidth = ActiveFont.Measure(Dialog.Clean("name_space")).X * optionsScale;
        float backspaceWidth = ActiveFont.Measure(Dialog.Clean("name_backspace")).X * optionsScale;
        float beginWidth = ActiveFont.Measure(Dialog.Clean("name_accept")).X * optionsScale * 1.25f;
        float optionsWidth = cancelWidth + spaceWidth + backspaceWidth + beginWidth + widestLetter * 3f;

        float boxPadding = widestLetter;
        float boxWidth = Math.Max(maxLetterLength * widestLetter, optionsWidth) + boxPadding * 2f;
        float innerWidth = boxWidth - boxPadding * 2f;

        var spaceLocations = Enumerable.Range(0, maxLetterLength)
            // Based on OuiFileNaming code
            .Select(index => (RealX: index * widestLetter, Position: (index, letters.Length)))
            .Where(pair => !(pair.RealX < cancelWidth + (innerWidth - cancelWidth - beginWidth - backspaceWidth - spaceWidth - widestLetter * 3f) / 2f)
                               && (pair.RealX < innerWidth - beginWidth - backspaceWidth - widestLetter * 2f))
            .Select(pair => pair.Position)
            .ToArray();

        var spaceExitLocation = (X: (int)((innerWidth - beginWidth - backspaceWidth - spaceWidth / 2f - widestLetter * 2f) / widestLetter), Y: letters.Length);

        letters = letters
            .Select(line => line.Replace(' ', char.MaxValue))
            .Concat([new string(char.MaxValue, spaceExitLocation.X) + ' ' + new string(char.MaxValue, maxLetterLength - spaceExitLocation.X - 1)])
            .ToArray();

        // Is a BFS for writing out a name overcomplicating it?
        var start = (X: 0, Y: 0);

        foreach (char targetChar in saveFileName) {
            int targetLine = Array.FindIndex(letters, line => line.Contains(targetChar));
            if (targetLine == -1) {
                AbortTas($"Character '{targetChar}' not available in current language");
                return;
            }

            int targetIndex = letters[targetLine].IndexOf(targetChar);
            if (targetIndex == -1) {
                AbortTas($"Character '{targetChar}' not available in current language");
                return;
            }

            // Search for optimal path
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

                    // Skip blanks while moving
                    do {
                        // Handle horizontal wrapping. Avoid snapping while moving vertically
                        if (dx != 0) {
                            nx = (nx + dx).Mod(letters[ny].Length);
                        }

                        ny += dy;

                        // Special-case check for "space", since it's larger
                        if (spaceLocations.Contains<(int X, int Y)>((nx, ny))) {
                            nx = spaceExitLocation.X;
                            ny = spaceExitLocation.Y;
                            break;
                        }

                        if (ny < 0 || ny >= letters.Length) {
                            goto NextNeighbour; // Current neighbour has gone out-of-bounds
                        }
                    } while (nx >= letters[ny].Length || letters[ny][nx] == char.MaxValue);

                    var next = (nx, ny);

                    if (visited.Add(next)) {
                        queue.Enqueue(next);
                        parent[next] = current; // Track how we reached this cell
                    }

                    NextNeighbour:;
                }
            }

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
                if (prevCell.X == 0 && currentCell.X == letters[currentCell.Y].Length - 1) {
                    path.Add((-1, currentCell.Y - prevCell.Y));
                } else if (currentCell.X == 0 && prevCell.X == letters[currentCell.Y].Length - 1) {
                    path.Add((1, currentCell.Y - prevCell.Y));
                }
                // Manually handle space button
                else if (currentCell == spaceExitLocation) {
                    path.Add((0, 1));
                } else if (prevCell == spaceExitLocation) {
                    path.Add((0, -1));
                }
                // Normal movement
                else {
                    path.Add((currentCell.X - prevCell.X, currentCell.Y - prevCell.Y));
                }

                currentCell = prevCell;
            }

            path.Reverse();

            start = end;

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

    private static void ChangeSelectedCampaign(InputController controller, string campaignName, int studioLine) {
        string startingLevelSet = "Celeste";
        if (AreaData.Areas.Any(area => area.LevelSet == CoreModule.Settings.DefaultStartingLevelSet)) {
            startingLevelSet = CoreModule.Settings.DefaultStartingLevelSet;
        }

        // Check both movement directions - Repeat move until level set is valid
        int movesLeft = 0;
        string currentLevelSet = startingLevelSet;
        while (currentLevelSet != campaignName) {
            int id = AreaData.Areas.FindIndex(area => area.LevelSet == currentLevelSet) - 1;
            if (id >= AreaData.Areas.Count) {
                id = 0;
            }
            if (id < 0) {
                id = AreaData.Areas.Count - 1;
            }

            currentLevelSet = AreaData.Areas[id].LevelSet;

            // Collab level sets aren't selectable and shouldn't be counted
            if (CollabUtils2Interop.Lobby.IsCollabLevelSet?.Invoke(currentLevelSet) ?? false) {
                continue;
            }

            movesLeft++;
        }

        int movesRight = 0;
        currentLevelSet = startingLevelSet;
        while (currentLevelSet != campaignName) {
            int id = AreaData.Areas.FindLastIndex(area => area.LevelSet == currentLevelSet) + 1;
            if (id >= AreaData.Areas.Count) {
                id = 0;
            }
            if (id < 0) {
                id = AreaData.Areas.Count - 1;
            }

            currentLevelSet = AreaData.Areas[id].LevelSet;

            // Collab level sets aren't selectable and shouldn't be counted
            if (CollabUtils2Interop.Lobby.IsCollabLevelSet?.Invoke(currentLevelSet) ?? false) {
                continue;
            }

            movesRight++;
        }

        if (movesLeft == 0 && movesRight == 0) {
            // No need to change campaign. Return back to "Begin"
            controller.AddFrames("1,U", studioLine);
            return;
        }

        // "Rename" is currently selected
        controller.AddFrames("1,D", studioLine);
        controller.AddFrames("1,F,180", studioLine);
        if (Settings.Instance.VariantsUnlocked) {
            controller.AddFrames("1,D", studioLine);
        }

        if (movesRight <= movesLeft) {
            for (int i = 0; i < movesRight; i++) {
                controller.AddFrames(i % 2 == 0 ? "1,R" : "1,F,90", studioLine);
            }
        } else {
            for (int i = 0; i < movesLeft; i++) {
                controller.AddFrames(i % 2 == 0 ? "1,L" : "1,F,270", studioLine);
            }
        }

        // Return back to "Begin"
        controller.AddFrames("1,U", studioLine);
        controller.AddFrames("1,F,0", studioLine);
        controller.AddFrames("1,U", studioLine);
        if (Settings.Instance.VariantsUnlocked) {
            controller.AddFrames("1,F,0", studioLine);
        }
    }
}
