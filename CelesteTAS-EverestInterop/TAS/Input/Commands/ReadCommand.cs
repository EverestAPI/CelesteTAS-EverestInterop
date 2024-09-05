using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ReadCommand {
    private static readonly List<string> readCommandStack = new();

    [ClearInputs]
    private static void Clear() {
        readCommandStack.Clear();
    }

    // "Read, Path",
    // "Read, Path, StartLine",
    // "Read, Path, StartLine, EndLine"
    [TasCommand("Read", ExecuteTiming = ExecuteTiming.Parse)]
    private static void Read(string[] args, int studioLine, string currentFilePath, int fileLine) {
        if (args.Length == 0) {
            return;
        }

        string commandName = $"Read, {string.Join(", ", args)}";

        string fileDirectory = Path.GetDirectoryName(currentFilePath);
        if (string.IsNullOrWhiteSpace(fileDirectory)) {
            fileDirectory = Directory.GetCurrentDirectory();
        }
        if (string.IsNullOrWhiteSpace(fileDirectory)) {
            "Failed to get current directory for Read command".Log(LogLevel.Error);
            return;
        }

        if (FindTargetFile(commandName, fileDirectory, args[0]) is not { } path) {
            return;
        }

        if (Path.GetFullPath(path) == Path.GetFullPath(currentFilePath)) {
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

        string readCommandDetail = $"{commandName}: line {fileLine} of the file \"{currentFilePath}\"";
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

        static string? FindTargetFile(string commandName, string fileDirectory, string filePath) {
            if (!filePath.EndsWith(".tas", StringComparison.InvariantCulture)) {
                filePath += ".tas";
            }

            string path = Path.Combine(fileDirectory, filePath);
            if (File.Exists(path)) {
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
                    AbortTas($"""
                              "{commandName}" failed
                              Ambiguous match for directory '{directory}'
                              """, true);
                    return null;
                }
                if (directories.Length == 0) {
                    AbortTas($"""
                              "{commandName}" failed
                              Couldn't find directory '{directory}'
                              """, true);
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
                AbortTas($"""
                          "{commandName}" failed
                          Ambiguous match for file '{file}'
                          """, true);
                return null;
            }
            if (files.Length == 1) {
                path = Path.Combine(realDirectory, files[0]);
                if (File.Exists(path)) {
                    return path;
                }
            }

            // Allow an optional suffix on file names. Example: 9D_04 -> 9D_04_Curiosity.tas
            if (Directory.GetParent(Path.Combine(realDirectory, file)) is { Exists: true } info) {
                var suffixFiles = info.GetFiles()
                    .Where(f => f.Name.StartsWith(Path.GetFileNameWithoutExtension(file), StringComparison.InvariantCultureIgnoreCase))
                    .ToArray();

                if (suffixFiles.Length > 1) {
                    AbortTas($"""
                              "{commandName}" failed
                              Ambiguous match for file '{file}'
                              """, true);
                    return null;
                }
                if (suffixFiles.Length == 1 && suffixFiles[0].Exists) {
                    return suffixFiles[0].FullName;
                }
            }

            AbortTas($"""
                      "{commandName}" failed
                      File not found
                      """, true);
            return null;
        }
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
