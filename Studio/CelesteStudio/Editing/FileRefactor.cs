using CelesteStudio.Communication;
using StudioCommunication;
using StudioCommunication.Util;
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

    public static readonly SemaphoreSlim RefactorSemaphore = new(1);

    /// Caches the file contents in lines of external files
    private static readonly Dictionary<string, string[]> FileCache = [];
    private static readonly HashSet<string> lockedFiles = [];
    private static readonly HashSet<string> pendingFilesystemWrite = [];

    private static readonly Dictionary<(string FilePath, bool ReturnFirst), string> projectRootCache = [];

    private static readonly Dictionary<string, FileSystemWatcher> watchers = [];

    public static void Initialize(Editor editor) {
        editor.PostDocumentChanged += document => EnsureFileWatched(document.FilePath);
    }

    #region Style Fixing

    /// Applies correct room label indices to the file, according to the style-configuration
    public static void FixRoomLabelIndices(string filePath, StyleConfig config, int? caretRow = null, bool flushFiles = true) {
        FixRoomLabelIndices(filePath, config.RoomLabelIndexing ?? Settings.Instance.AutoIndexRoomLabels, Math.Max(config.RoomLabelStartingIndex ?? 0, 0), caretRow, flushFiles);
    }

    /// Applies formatting rules to the file
    public static void FormatFile(string filePath, bool forceCommandCasing, string? commandSeparator, bool flushFiles = true) {
        LockFile(filePath);
        try {
            string[] lines = ReadLines(filePath);

            FormatLines(lines, Enumerable.Range(0, lines.Length), forceCommandCasing, commandSeparator);

            WriteLines(filePath, lines);
        } finally {
            UnlockFile(filePath);
        }

        if (flushFiles) {
            FlushFiles();
        }
    }
    /// Applies formatting rules to the specified lines
    public static void FormatLines(string[] lines, IEnumerable<int> rows, bool forceCommandCasing, string? commandSeparator) {
        foreach (int row in rows) {
            lines[row] = FormatLine(lines[row], forceCommandCasing, commandSeparator);
        }
    }
    /// Applies formatting rules to the line
    public static string FormatLine(string line, bool? forceCommandCasing = null, string? commandSeparator = null) {
        if (ActionLine.TryParseStrict(line, out var actionLine)) {
            return actionLine.ToString();
        }
        if (CommandLine.TryParse(line, out var commandLine)) {
            return commandLine.Format(CommunicationWrapper.Commands, forceCommandCasing ?? StyleConfig.Current.ForceCorrectCommandCasing, commandSeparator ?? StyleConfig.Current.CommandArgumentSeparator);
        }
        if (FastForwardLine.TryParse(line, out var fastForwardLine)) {
            return fastForwardLine.Format();
        }

        return line.Trim();
    }

    /// Applies correct room label indices to the file
    public static void FixRoomLabelIndices(string filePath, AutoRoomIndexing roomIndexing, int startingIndex, int? caretRow = null, bool flushFiles = true) {
        if (roomIndexing == AutoRoomIndexing.Disabled) {
            return;
        }

        // room label without indexing -> lines of all occurrences
        Dictionary<string, List<(int Row, bool Update)>> roomLabels = [];
        List<(string OldLabel, string NewLabel)> refactors = [];

        // Allows the user to edit labels without them being auto-trimmed
        string untrimmedLabel = string.Empty;

        RefactorSemaphore.Wait();
        LockFile(filePath);

        try {
            foreach ((string line, int row, string path, _) in IterateLines(filePath, followReadCommands: roomIndexing == AutoRoomIndexing.IncludeReads)) {
                if (CommentLine.RoomLabelRegex.Match(line) is not { Success: true } match) {
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
        } finally {
            UnlockFile(filePath);
            RefactorSemaphore.Release();
        }

        foreach ((string oldLabel, string newLabel) in refactors) {
            RefactorLabelName(filePath, oldLabel, newLabel, flushFiles: false);
        }

        if (flushFiles) {
            FlushFiles();
        }
    }

    #endregion

    #region Refactoring

    /// Changes the name of a label, updating all references to it in the project
    public static void RefactorLabelName(string filePath, string oldLabel, string newLabel, bool flushFiles = true) {
        if (oldLabel == newLabel) {
            return; // Already has that name
        }

        string projectRoot = FindProjectRoot(filePath, returnSubmodules: false);

        RefactorSemaphore.Wait();
        Console.WriteLine($"Performing label refactor for '{oldLabel}' => '{newLabel}' file '{filePath}'");

        try {
            // External Read-commands
            foreach (string file in Directory.GetFiles(projectRoot, "*.tas", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden })) {
                if (file == filePath || Directory.Exists(file)) {
                    continue;
                }

                LockFile(file);
                try {
                    string[] lines = ReadLines(file);
                    bool changed = false;

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
                        changed = true;
                    }

                    if (changed) {
                        WriteLines(file, lines);
                    }
                } finally {
                    UnlockFile(file);
                }
            }

            // Internal Play-commands
            LockFile(filePath);
            try {
                string[] lines = ReadLines(filePath);
                bool changed = false;

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
                    changed = true;
                }

                if (changed) {
                    WriteLines(filePath, lines);
                }
            } finally {
                UnlockFile(filePath);
            }

            Console.WriteLine($"Label refactor for '{oldLabel}' => '{newLabel}' done");
        } catch (Exception ex) {
            Console.WriteLine($"Failed to refactor room label: {ex}");
        } finally {
            RefactorSemaphore.Release();
        }

        if (flushFiles) {
            FlushFiles();
        }
    }

    #endregion

    #region Utilities

    /// Locates a probable root directory for the current TAS project
    public static string FindProjectRoot(string filePath, bool returnSubmodules) {
        var cacheKey = (filePath, returnFirst: returnSubmodules);
        if (projectRootCache.TryGetValue(cacheKey, out string? projectRoot)) {
            return projectRoot;
        }

        // 1st approach: Search for a Git repository
        for (string? path = Path.GetDirectoryName(filePath); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path)) {
            string gitPath = Path.Combine(path, ".git");
            if (
                // Search for Git repository, or Git submodule if returnSubmodules is enabled
                (Directory.Exists(gitPath) || (returnSubmodules && File.Exists(gitPath))) &&
                // Require at least 75% of files in the repo/submodule to be TAS files
                GetTasFilePercentage(path) >= 0.75f
            ) {
                projectRootCache[cacheKey] = path;
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

    public const string ErrorCommentPrefix = "# ERROR: ";

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
                    yield return ($"{ErrorCommentPrefix}Couldn't find directory of current file '{path}'", row, path, commandLine);
                    continue;
                }

                if (Parsing.FindReadTargetFile(documentDir, commandLine.Arguments[0], out string errorMessage) is not { } targetPath) {
                    yield return ($"{ErrorCommentPrefix}{errorMessage}", row, path, commandLine);
                    continue;
                }

                if (Path.GetFullPath(path) == Path.GetFullPath(targetPath)) {
                    yield return ($"{ErrorCommentPrefix}File is not allowed to read itself", row, path, commandLine);
                    continue;
                }

                string fullPath = Path.Combine(documentDir, $"{commandLine.Arguments[0]}.tas");
                if (!File.Exists(fullPath)) {
                    yield return ($"{ErrorCommentPrefix}Couldn't find target file '{fullPath}'", row, path, commandLine);
                    continue;
                }

                string[] readLines = ReadLines(fullPath);

                int readStartRow = 0;
                if (commandLine.Arguments.Length > 1) {
                    if (!Parsing.TryGetLineTarget(commandLine.Arguments[1], readLines, out readStartRow, out bool isLabel)) {
                        yield return ($"{ErrorCommentPrefix}Start label '{commandLine.Arguments[1]}' not found in file '{fullPath}'", row, path, commandLine);
                        continue;
                    }
                    readStartRow--; // Convert to 0-indexed

                    if (isLabel) {
                        readStartRow++; // Skip over label
                    }
                }
                int readEndRow = readLines.Length - 1;
                if (commandLine.Arguments.Length > 2) {
                    if (!Parsing.TryGetLineTarget(commandLine.Arguments[2], readLines, out readEndRow, out bool isLabel)) {
                        yield return ($"{ErrorCommentPrefix}End label '{commandLine.Arguments[2]}' not found in file '{fullPath}'", row, path, commandLine);
                        continue;
                    }
                    readEndRow--; // Convert to 0-indexed

                    if (isLabel) {
                        readEndRow--; // Skip over label
                    }
                }

                // Clamp values
                readStartRow = Math.Max(0, readStartRow);
                readEndRow = Math.Min(readLines.Length - 1, readEndRow);

                fileStack.Push((path, row + 1, endRow, targetCommand)); // Store current state
                fileStack.Push((fullPath, readStartRow, readEndRow, commandLine)); // Setup next state (skip start / end labels)
                break;
            }
        }
    }

    public static void FlushFiles() {
        RefactorSemaphore.Wait();
        try {
            foreach (string filePath in pendingFilesystemWrite.Select(Path.GetFullPath)) {
                Task.Run(async () => {
                    const int numberOfRetries = 3;
                    const int delayOnRetry = 1000;
                    const int ERROR_SHARING_VIOLATION = unchecked((int) 0x80070020);

                    LockFile(filePath);
                    try {
                        for (int i = 1; i <= numberOfRetries; i++) {
                            try {
                                await File.WriteAllTextAsync(filePath, FileCache[filePath].FormatTasLinesToText());
                                Console.WriteLine($"Successfully flushed file '{filePath}'");
                            } catch (IOException ex) when (ex.HResult == ERROR_SHARING_VIOLATION || ex is FileNotFoundException) {
                                await Task.Delay(delayOnRetry);
                            } catch (Exception ex) {
                                // Something else failed
                                Console.WriteLine($"Failed to flush file '{filePath}': {ex}");
                            }
                        }
                    } finally {
                        UnlockFile(filePath);
                    }
                });
            }

            pendingFilesystemWrite.Clear();
        } finally {
            RefactorSemaphore.Release();
        }
    }

    public static string[] ReadLines(string filePath) {
        filePath = Path.GetFullPath(filePath);
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
        filePath = Path.GetFullPath(filePath);
        EnsureFileWatched(filePath);

        lock(lockedFiles) {
            if (!lockedFiles.Contains(filePath)) {
                Console.WriteLine($"File '{filePath}' was modified without being locked!");
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
            pendingFilesystemWrite.Add(filePath);
        }
    }

    private static void LockFile(string filePath) {
        filePath = Path.GetFullPath(filePath);
        lock (lockedFiles) {
            if (lockedFiles.Contains(filePath)) {
                Console.WriteLine($"File '{filePath}' was locked twice!");
            }

            lockedFiles.Add(filePath);
        }
    }

    private static void UnlockFile(string filePath) {
        filePath = Path.GetFullPath(filePath);
        lock (lockedFiles) {
            if (!lockedFiles.Contains(filePath)) {
                Console.WriteLine($"File '{filePath}' was never locked!");
            }

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
                lock (lockedFiles) {
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
