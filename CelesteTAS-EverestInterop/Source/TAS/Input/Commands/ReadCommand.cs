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

        string filePath = args[0];
        string fileDirectory = Path.GetDirectoryName(currentFilePath);
        if (fileDirectory.IsEmpty()) {
            fileDirectory = Directory.GetCurrentDirectory();
        }

        FindTheFile();

        if (!File.Exists(filePath)) {
            // for compatibility with tas files downloaded from discord
            // discord will replace spaces in the file name with underscores
            filePath = args[0].Replace(" ", "_");
            FindTheFile();
        }

        if (!File.Exists(filePath)) {
            AbortTas($"\"Read, {string.Join(", ", args)}\" failed\nFile not found", true);
            return;
        } else if (Path.GetFullPath(filePath) == Path.GetFullPath(currentFilePath)) {
            AbortTas($"\"Read, {string.Join(", ", args)}\" failed\nDo not allow reading the file itself", true);
            return;
        }

        // Find starting and ending lines
        int startLine = 0;
        int endLine = int.MaxValue;
        if (args.Length > 1) {
            if (!TryGetLine(args[1], filePath, out startLine)) {
                AbortTas($"\"Read, {string.Join(", ", args)}\" failed\n{args[1]} is invalid", true);
                return;
            }

            if (args.Length > 2) {
                if (!TryGetLine(args[2], filePath, out endLine)) {
                    AbortTas($"\"Read, {string.Join(", ", args)}\" failed\n{args[2]} is invalid", true);
                    return;
                }
            }
        }

        string readCommandDetail = $"Read, {string.Join(", ", args)}: line {fileLine} of the file \"{currentFilePath}\"";
        if (readCommandStack.Contains(readCommandDetail)) {
            $"Multiple read commands lead to dead loops:\n{string.Join("\n", readCommandStack)}".Log(LogLevel.Warn);
            AbortTas("Multiple read commands lead to dead loops\nPlease check log.txt for more details");
            return;
        }

        readCommandStack.Add(readCommandDetail);
        Manager.Controller.ReadFile(filePath, startLine, endLine, studioLine);
        if (readCommandStack.Count > 0) {
            readCommandStack.RemoveAt(readCommandStack.Count - 1);
        }

        void FindTheFile() {
            // Check for full and shortened Read versions
            if (fileDirectory != null) {
                // Path.Combine can handle the case when filePath is an absolute path
                string absoluteOrRelativePath = Path.Combine(fileDirectory, filePath);
                if (!absoluteOrRelativePath.EndsWith(".tas", StringComparison.InvariantCulture)) {
                    absoluteOrRelativePath += ".tas";
                }
                if (File.Exists(absoluteOrRelativePath)) {
                    filePath = absoluteOrRelativePath;
                } else if (Directory.GetParent(absoluteOrRelativePath) is { } directoryInfo && Directory.Exists(directoryInfo.ToString())) {
                    List<string> files = Directory.GetFiles(directoryInfo.ToString(), $"{Path.GetFileNameWithoutExtension(filePath)}*.tas").ToList();
                    files.Sort((s1, s2) => string.Compare(s1, s2, StringComparison.InvariantCulture));
                    if (files.FirstOrDefault() is { } shortenedFilePath) {
                        filePath = shortenedFilePath;
                    }
                }
            }
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