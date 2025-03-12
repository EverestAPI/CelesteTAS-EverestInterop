using CelesteStudio.Communication;
using CelesteStudio.Data;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CelesteStudio.Editing;

/// Manages refactoring of files, including semantic and stylistic changes
public static class FileRefactor {

    private static readonly SemaphoreSlim RefactorSemaphore = new(1);

    /// Caches the file contents in lines of external files
    private static readonly Dictionary<string, string[]> FileCache = [];
    private static readonly HashSet<string> lockedFiles = [];

    private static readonly Dictionary<string, string> projectRootCache = [];

    private static readonly Dictionary<string, FileSystemWatcher> watchers = [];

    public static void Initialize(Editor editor) {
        editor.DocumentChanged += (_, document) => EnsureFileWatched(document.FilePath);
    }

    #region Style Fixing

    private static readonly Regex RoomLabelRegex = new(@"^#lvl_([^\(\)]*)(?:\s\((\d+)\))?$", RegexOptions.Compiled);

    /// Applies correct room label indices to the file, according to the style-configuration
    public static void FixRoomLabelIndices(string filePath, StyleConfig config, int? caretRow = null) {
        FixRoomLabelIndices(filePath, config.RoomLabelIndexing ?? Settings.Instance.AutoIndexRoomLabels, Math.Max(config.RoomLabelStartingIndex ?? 0, 0), caretRow);
    }

    /// Applies formatting rules to the file
    public static void FormatFile(string filePath, bool forceCommandCasing, string? commandSeparator) {
        LockFile(filePath);
        string[] lines = ReadLines(filePath);

        FormatLines(lines, Enumerable.Range(0, lines.Length), forceCommandCasing, commandSeparator);

        WriteLines(filePath, lines);
        UnlockFile(filePath);
    }
    /// Applies formatting rules to the specified lines
    public static void FormatLines(string[] lines, IEnumerable<int> rows, bool forceCommandCasing, string? commandSeparator) {
        foreach (int row in rows) {
            string line = lines[row];

            // Convert to action lines, if possible
            if (ActionLine.TryParse(line, out var actionLine)) {
                lines[row] = actionLine.ToString();
            } else if (CommandLine.TryParse(line, out var commandLine)) {
                lines[row] = commandLine.Format(CommunicationWrapper.Commands, forceCommandCasing, commandSeparator);
            }
        }
    }

    /// Applies correct room label indices to the file
    public static void FixRoomLabelIndices(string filePath, AutoRoomIndexing roomIndexing, int startingIndex, int? caretRow = null) {
        if (roomIndexing == AutoRoomIndexing.Disabled) {
            return;
        }

        // room label without indexing -> lines of all occurrences
        Dictionary<string, List<(int Row, bool Update)>> roomLabels = [];

        // Allows the user to edit labels without them being auto-trimmed
        string untrimmedLabel = string.Empty;

        LockFile(filePath);
        foreach ((string line, int row, string path, _) in IterateLines(filePath, followReadCommands: roomIndexing == AutoRoomIndexing.IncludeReads)) {
            if (RoomLabelRegex.Match(line) is not { Success: true } match) {
                continue;
            }

            bool isCurrentFile = path == filePath;

            string label = match.Groups[1].Value.Trim();
            if (isCurrentFile && row == caretRow) {
                untrimmedLabel = match.Groups[1].Value;
            }

            if (roomLabels.TryGetValue(label, out var list)) {
                list.Add((row, Update: isCurrentFile));
            } else {
                roomLabels[label] = [(row, Update: isCurrentFile)];
            }
        }

        List<(string OldLabel, string NewLabel)> refactors = [];

        string[] lines = ReadLines(filePath);
        foreach ((string label, var occurrences) in roomLabels) {
            if (occurrences.Count == 1) {
                if (!occurrences[0].Update) {
                    continue;
                }

                string writtenLabel = occurrences[0].Row == caretRow
                    ? untrimmedLabel
                    : label;

                string oldLabel = lines[occurrences[0].Row]["#".Length..];
                string newLabel = $"lvl_{label}";
                refactors.Add((oldLabel, newLabel));

                lines[occurrences[0].Row] = $"#lvl_{writtenLabel}";
                continue;
            }

            for (int i = 0; i < occurrences.Count; i++) {
                if (!occurrences[i].Update) {
                    continue;
                }

                string writtenLabel = occurrences[i].Row == caretRow
                    ? untrimmedLabel
                    : label;

                string oldLabel = lines[occurrences[i].Row]["#".Length..];
                string newLabel = $"lvl_{label} ({i + startingIndex})";
                refactors.Add((oldLabel, newLabel));

                lines[occurrences[i].Row] = $"#lvl_{writtenLabel} ({i + startingIndex})";
            }
        }
        WriteLines(filePath, lines, raiseEvents: false);
        UnlockFile(filePath);

        foreach ((string oldLabel, string newLabel) in refactors) {
            RefactorLabelName(filePath, oldLabel, newLabel);
        }
    }

    #endregion

    #region Refactoring

    /// Changes the name of a label, updating all references to it in the project
    public static void RefactorLabelName(string filePath, string oldLabel, string newLabel) {
        if (oldLabel == newLabel) {
            return; // Already has that name
        }

        string projectRoot = FindProjectRoot(filePath);

        RefactorSemaphore.Wait();
        Console.WriteLine($"Performing label refactor for '{oldLabel}' => '{newLabel}' file '{filePath}'");

        try {
            // External Read-commands
            foreach (string file in Directory.GetFiles(projectRoot, "*.tas", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden })) {
                if (file == filePath || Directory.Exists(file)) {
                    continue;
                }

                LockFile(file);
                string[] lines = ReadLines(file);

                for (int row = 0; row < lines.Length; row++) {
                    string line = lines[row];
                    if (!CommandLine.TryParse(line, out var commandLine) || !commandLine.IsCommand("Read")) {
                        continue;
                    }

                    // Verify command points to our file
                    if (commandLine.Arguments.Length == 0 || !string.Equals(
                            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, $"{commandLine.Arguments[0]}.tas")),
                            Path.GetFullPath(filePath),
                            StringComparison.OrdinalIgnoreCase
                    )) {
                        continue;
                    }

                    // Start label
                    if (commandLine.Arguments.Length >= 2 && commandLine.Arguments[1] == oldLabel) {
                        commandLine.Arguments[1] = newLabel;
                    }
                    // End label
                    if (commandLine.Arguments.Length >= 3 && commandLine.Arguments[2] == oldLabel) {
                        commandLine.Arguments[2] = newLabel;
                    }

                    lines[row] = commandLine.ToString();
                }

                WriteLines(file, lines);
                UnlockFile(file);
            }

            // Internal Play-commands
            {
                LockFile(filePath);
                string[] lines = ReadLines(filePath);

                for (int row = 0; row < lines.Length; row++) {
                    string line = lines[row];
                    if (!CommandLine.TryParse(line, out var commandLine) || !commandLine.IsCommand("Play")) {
                        continue;
                    }

                    // Start label
                    if (commandLine.Arguments.Length >= 1 && commandLine.Arguments[0] == oldLabel) {
                        commandLine.Arguments[0] = newLabel;
                    }
                    // End label
                    if (commandLine.Arguments.Length >= 2 && commandLine.Arguments[1] == oldLabel) {
                        commandLine.Arguments[1] = newLabel;
                    }

                    lines[row] = commandLine.ToString();
                }

                WriteLines(filePath, lines);
                UnlockFile(filePath);
            }

            Console.WriteLine($"Label refactor for '{oldLabel}' => '{newLabel}' done");
        } catch (Exception ex) {
            Console.WriteLine($"Failed to refactor room label: {ex}");
        } finally {
            RefactorSemaphore.Release();
        }
    }

    #endregion

    #region Utilities

    /// Locates a probable root directory for the current TAS project
    public static string FindProjectRoot(string filePath) {
        if (projectRootCache.TryGetValue(filePath, out string? projectRoot)) {
            return projectRoot;
        }

        // 1st approach: Search for a Git repository
        for (string? path = Path.GetDirectoryName(filePath); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path)) {
            if (Directory.Exists(Path.Combine(path, ".git")) &&
                // Require at least 75% of files in the repo to be TAS files
                GetTasFilePercentage(path) >= 0.75f)
            {
                projectRootCache[filePath] = path;
                return path;
            }
        }

        // 2nd approach: Go up until there is a sudden drop in the percentage
        const int maxDepth = 5;
        int currentDepth = 0;
        float previousPercentage = 1.0f;
        string previousPath = string.Empty;

        for (string? path = Path.GetDirectoryName(filePath); currentDepth <= maxDepth && !string.IsNullOrEmpty(path); previousPath = path, path = Path.GetDirectoryName(path), currentDepth++) {
            float currentPercentage = GetTasFilePercentage(path);
            if (previousPercentage - currentPercentage >= 0.2f || currentPercentage <= 0.75f) {
                return previousPath;
            }
        }

        // No good solution could be found
        return Path.GetDirectoryName(filePath)!;

        static float GetTasFilePercentage(string path) {
            string[] allFiles = Directory.GetFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Directory | FileAttributes.Hidden });

            return allFiles.Count(file => file.EndsWith(".tas")) /
                   // Ignore documentation in the total
                   Math.Max(1.0f, allFiles.Count(file => !file.EndsWith(".md") && !file.EndsWith(".txt")));
        }
    }

    /// Iterates over all lines of the document, optionally following Read-commands
    public static IEnumerable<(string Line, int Row, string File, CommandLine? TargetCommand)> IterateLines(string filePath, bool followReadCommands) {
        string[] fileLines = ReadLines(filePath);

        if (!followReadCommands) {
            for (int row = 0; row < fileLines.Length; row++) {
                yield return (fileLines[row], row, filePath, null);
            }

            yield break;
        }

        Stack<(string Path, int CurrRow, int EndRow, CommandLine? TargetCommand)> fileStack = [];
        fileStack.Push((filePath, 0, fileLines.Length - 1, null));

        while (fileStack.TryPop(out var file)) {
            (string path, int currRow, int endRow, var targetCommand) = file;
            string[] lines = ReadLines(path);

            for (int row = currRow; row <= endRow && row < lines.Length; row++) {
                string line = lines[row];

                if (!CommandLine.TryParse(line, out var commandLine)
                    || !commandLine.IsCommand("Read")
                    || commandLine.Arguments.Length == 0
                ) {
                    yield return (line, row, path, targetCommand);
                    continue;
                }

                // Follow Read-command
                if (Path.GetDirectoryName(path) is not { } documentDir) {
                    continue;
                }

                string fullPath = Path.Combine(documentDir, $"{commandLine.Arguments[0]}.tas");
                if (!File.Exists(fullPath)) {
                    continue;
                }

                var readLines = ReadLines(fullPath)
                    .Select((readLine, i) => (line: readLine, i))
                    .ToArray();

                int? startLabelRow = null;
                if (commandLine.Arguments.Length > 1) {
                    (string label, startLabelRow) = readLines
                        .FirstOrDefault(pair => pair.line == $"#{commandLine.Arguments[1]}");
                    if (label == null) {
                        continue;
                    }
                }
                int? endLabelRow = null;
                if (commandLine.Arguments.Length > 2) {
                    (string label, endLabelRow) = readLines
                        .FirstOrDefault(pair => pair.line == $"#{commandLine.Arguments[2]}");
                    if (label == null) {
                        continue;
                    }
                }

                startLabelRow ??= 0;
                endLabelRow ??= readLines.Length - 1;

                fileStack.Push((path, row + 1, endRow, targetCommand)); // Store current state
                fileStack.Push((fullPath, startLabelRow.Value + 1, endLabelRow.Value - 1, commandLine)); // Setup next state (skip start / end labels)
                break;
            }
        }
    }

    public static string[] ReadLines(string filePath) {
        EnsureFileWatched(filePath);

        var document = Studio.Instance.Editor.Document;
        if (filePath == document.FilePath) {
            return document.Lines.ToArray();
        }

        if (!FileCache.TryGetValue(filePath, out string[]? lines)) {
            FileCache[filePath] = lines = File.ReadAllLines(filePath);
        }
        return lines;
    }
    public static void WriteLines(string filePath, string[] lines, bool raiseEvents = true) {
        EnsureFileWatched(filePath);

        lock(lockedFiles) {
            if (!lockedFiles.Contains(filePath)) {
                Console.WriteLine($"File '{filePath}' was modified without being locked");
            }
        }

        var document = Studio.Instance.Editor.Document;
        if (filePath == document.FilePath) {
            // Check for changes to avoid pushing empty undo-states
            if (document.Lines.Count == lines.Length && lines.Zip(document.Lines).All(pair => pair.First == pair.Second)) {
                return;
            }

            // No unsaved changes are discarded since this is just a regular, undo-able update
            using var __ = document.Update(raiseEvents);
            using var patch = new Document.Patch(document);

            patch.DeleteRange(0, document.Lines.Count - 1);
            patch.InsertRange(0, lines);
        } else {
            FileCache[filePath] = lines;
            File.WriteAllText(filePath, Document.FormatLinesToText(lines));
        }
    }

    private static void LockFile(string filePath) {
        lock(lockedFiles) {
            lockedFiles.Add(filePath);
        }
    }

    private static void UnlockFile(string filePath) {
        lock(lockedFiles) {
            lockedFiles.Remove(filePath);
        }
    }

    private static void EnsureFileWatched(string filePath) {
        if (string.IsNullOrEmpty(filePath) || Path.GetDirectoryName(filePath) is not { } directoryName || watchers.ContainsKey(directoryName)) {
            return;
        }

        var watcher = new FileSystemWatcher();
        watcher.Changed += OnFileChanged;
        watcher.Path = directoryName;
        watcher.Filter = "*.tas";
        watcher.EnableRaisingEvents = true;

        watchers[directoryName] = watcher;

        return;

        static async void OnFileChanged(object sender, FileSystemEventArgs e) {
            try {
                lock(lockedFiles) {
                    if (lockedFiles.Contains(e.FullPath)) {
                        return;
                    }
                }

                FileCache[e.FullPath] = await ReadFileWithRetryAsync(e.FullPath).ConfigureAwait(false);
            } catch (Exception ex) {
                Console.WriteLine($"Failed to update file cache for '{e.FullPath}'");
                Console.WriteLine(ex);

                FileCache.Remove(e.FullPath);
            }
        }
    }

    private static async Task<string[]> ReadFileWithRetryAsync(string path, int maxRetries = 3, int delayMs = 10) {
        for (int i = 0; i < maxRetries; i++) {
            try {
                return await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            } catch (IOException) {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        throw new IOException($"Failed to read file {path} after {maxRetries} retries.");
    }

    #endregion
}
