using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ReadCommand {
    private class ReadMeta : ITasCommandMeta {
        public string Insert => $"Read{CommandInfo.Separator}[0;File Name]{CommandInfo.Separator}[1;Starting Label]{CommandInfo.Separator}[2;(Ending Label)]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            int hash = args[..Math.Max(0, args.Length - 1)]
                .Aggregate(17, (current, arg) => 31 * current + 17 * arg.GetStableHashCode());

            // Auto-complete entries are based on current file path
            hash = 31 * hash + 17 * Manager.Controller.FilePath.GetStableHashCode();

            if (args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0])) {
                if (Path.GetDirectoryName(filePath) is not { } fileDir) {
                    return hash;
                }

                string subDir = Path.GetDirectoryName(args[0]) ?? string.Empty;
                string targetDir = Path.Combine(fileDir, subDir);

                if (Directory.Exists(targetDir)) {
                    hash = Directory.GetDirectories(targetDir)
                        .Aggregate(hash, (current, arg) => 31 * current + 17 * arg.GetStableHashCode());
                    hash = Directory.GetFiles(targetDir)
                        .Aggregate(hash, (current, arg) => 31 * current + 17 * arg.GetStableHashCode());
                }
            }

            return hash;
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (Path.GetDirectoryName(filePath) is not { } fileDir) {
                yield break;
            }

            if (args.Length == 1) {
                // Filename
                string subDir = Path.GetDirectoryName(args[0]) ?? string.Empty;
                string targetDir = Path.Combine(fileDir, subDir);

                if (!Directory.Exists(targetDir)) {
                    yield break;
                }

                string prefix = Path.GetRelativePath(fileDir, targetDir).Replace('\\', '/') + "/";
                if (prefix.StartsWith("./")) {
                    prefix = prefix["./".Length..];
                }

                yield return new CommandAutoCompleteEntry { Name = "../", Prefix = prefix, IsDone = false };

                foreach (string dir in Directory.GetDirectories(targetDir).OrderBy(Path.GetFileName)) {
                    string dirName = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(dirName) || dirName.StartsWith(".")) {
                        continue; // Ignore hidden directories
                    }

                    yield return new CommandAutoCompleteEntry { Name = $"{dirName}/", Prefix = prefix, IsDone = false };
                }
                foreach (string file in Directory.GetFiles(targetDir).OrderBy(Path.GetFileName)) {
                    string fileName = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith(".") || Path.GetExtension(fileName) != ".tas") {
                        continue; // Ignore hidden / non-TAS files
                    }

                    yield return new CommandAutoCompleteEntry { Name = Path.GetFileNameWithoutExtension(fileName), Prefix = prefix, IsDone = true, HasNext = true };
                }

            } else if (args.Length is 2 or 3) {
                // Starting / ending labels
                string fullPath = Path.Combine(fileDir, $"{args[0]}.tas");
                if (!File.Exists(fullPath)) {
                    yield break;
                }

                // Don't include labels before the starting one for the ending label
                bool afterStartingLabel = args.Length == 2;
                foreach (string line in File.ReadAllText(fullPath).ReplaceLineEndings("\n").Split('\n')) {
                    if (!StudioCommunication.CommentLine.IsLabel(line)) {
                        continue;
                    }

                    string label = line[1..]; // Remove the #
                    if (!afterStartingLabel) {
                        afterStartingLabel = label == args[1];
                        continue;
                    }

                    yield return new CommandAutoCompleteEntry { Name = label, IsDone = true, HasNext = args.Length == 2 };
                }
            }
        }
    }

    private static readonly List<string> readCommandStack = new();

    [ClearInputs]
    private static void Clear() {
        readCommandStack.Clear();
    }

    // "Read, Path",
    // "Read, Path, StartLabel",
    // "Read, Path, StartLabel, EndLabel"
    [TasCommand("Read", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(ReadMeta))]
    private static void Read(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.Length == 0) {
            return;
        }

        string commandName = $"Read, {string.Join(", ", args)}";

        string fileDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(fileDirectory)) {
            fileDirectory = Directory.GetCurrentDirectory();
        }
        if (string.IsNullOrWhiteSpace(fileDirectory)) {
            "Failed to get current directory for Read command".Log(LogLevel.Error);
            return;
        }

        if (FindTargetFile(commandName, fileDirectory, args[0], out string errorMessage) is not { } path) {
            AbortTas(errorMessage, true);
            return;
        }

        if (Path.GetFullPath(path) == Path.GetFullPath(filePath)) {
            AbortTas($"\"{commandName}\" failed\nDo not allow reading the file itself", true);
            return;
        }

        // Find starting and ending lines
        int startLine = 0;
        int endLine = int.MaxValue;
        if (args.Length > 1) {
            if (!TryGetLine(args[1], path, out startLine)) {
                AbortTas($"\"{commandName}\" failed\n{args[1]} is invalid", true);
                return;
            }

            if (args.Length > 2) {
                if (!TryGetLine(args[2], path, out endLine)) {
                    AbortTas($"\"{commandName}\" failed\n{args[2]} is invalid", true);
                    return;
                }
            }
        }

        string readCommandDetail = $"{commandName}: line {fileLine} of the file \"{filePath}\"";
        if (readCommandStack.Contains(readCommandDetail)) {
            $"Multiple read commands lead to dead loops:\n{string.Join("\n", readCommandStack)}".Log(LogLevel.Warn);
            AbortTas("Multiple read commands lead to dead loops\nPlease check log.txt for more details");
            return;
        }

        readCommandStack.Add(readCommandDetail);
        Manager.Controller.ReadFile(path, startLine, endLine, studioLine);
        if (readCommandStack.Count > 0) {
            readCommandStack.RemoveAt(readCommandStack.Count - 1);
        }

        return;
    }

    private static string? FindTargetFile(string commandName, string fileDirectory, string filePath, out string errorMessage) {
        if (!filePath.EndsWith(".tas", StringComparison.InvariantCulture)) {
            filePath += ".tas";
        }

        string path = Path.Combine(fileDirectory, filePath);
        if (File.Exists(path)) {
            errorMessage = string.Empty;
            return path;
        }

        // Windows allows case-insensitive names, but Linux/macOS don't...
        string[] components = filePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        string realDirectory = fileDirectory;
        for (int i = 0; i < components.Length - 1; i++) {
            string directory = components[i];
            string[] directories = Directory.EnumerateDirectories(realDirectory)
                .Where(d => d.Equals(directory, StringComparison.InvariantCultureIgnoreCase))
                .ToArray();

            if (directories.Length > 1) {
                errorMessage = $"""
                                "{commandName}" failed
                                Ambiguous match for directory '{directory}'
                                """;
                return null;
            }
            if (directories.Length == 0) {
                errorMessage = $"""
                                "{commandName}" failed
                                Couldn't find directory '{directory}'
                                """;
                return null;
            }

            realDirectory = Path.Combine(realDirectory, directories[0]);
        }

        // Can't merge this into the above loop since a bit more is done with the file name
        string file = components[^1];
        string[] files = Directory.EnumerateFiles(realDirectory)
            .Where(f => f.Equals(file, StringComparison.InvariantCultureIgnoreCase))
            .ToArray();

        if (files.Length > 1) {
            errorMessage = $"""
                            "{commandName}" failed
                            Ambiguous match for file '{file}'
                            """;
            return null;
        }
        if (files.Length == 1) {
            path = Path.Combine(realDirectory, files[0]);
            if (File.Exists(path)) {
                errorMessage = string.Empty;
                return path;
            }
        }

        // Allow an optional suffix on file names. Example: 9D_04 -> 9D_04_Curiosity.tas
        if (Directory.GetParent(Path.Combine(realDirectory, file)) is { Exists: true } info) {
            var suffixFiles = info.GetFiles()
                .Where(f => f.Name.StartsWith(Path.GetFileNameWithoutExtension(file), StringComparison.InvariantCultureIgnoreCase))
                .ToArray();

            if (suffixFiles.Length > 1) {
                errorMessage = $"""
                                "{commandName}" failed
                                Ambiguous match for file '{file}'
                                """;
                return null;
            }
            if (suffixFiles.Length == 1 && suffixFiles[0].Exists) {
                errorMessage = string.Empty;
                return suffixFiles[0].FullName;
            }
        }

        errorMessage = $"""
                        "{commandName}" failed
                        File not found
                        """;
        return null;
    }

    public static bool TryGetLine(string labelOrLineNumber, string path, out int lineNumber) {
        if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
            int curLine = 0;
            Regex labelRegex = new(@$"^\s*#\s*{Regex.Escape(labelOrLineNumber)}\s*$");
            foreach (string readLine in File.ReadLines(path)) {
                curLine++;
                string line = readLine.Trim();
                if (labelRegex.IsMatch(line)) {
                    lineNumber = curLine;
                    return true;
                }
            }

            return false;
        }

        return true;
    }
}
