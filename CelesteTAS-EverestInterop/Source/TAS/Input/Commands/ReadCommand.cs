using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod;
using TAS.Entities;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ReadCommand {
    private static readonly List<string> readCommandStack = new();

    public static void ClearReadCommandStack() {
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
        FindTheFile();

        if (!File.Exists(filePath)) {
            // for compatibility with tas files downloaded from discord
            // discord will replace spaces in the file name with underscores
            filePath = args[0].Replace(" ", "_");
            FindTheFile();
        }

        if (!File.Exists(filePath)) {
            ToastAndLog($"\"Read, {string.Join(", ", args)}\" failed\nFile not found");
            Manager.DisableRunLater();
            return;
        } else if (Path.GetFullPath(filePath) == Path.GetFullPath(currentFilePath)) {
            ToastAndLog($"\"Read, {string.Join(", ", args)}\" failed\nDo not allow reading the file itself");
            Manager.DisableRunLater();
            return;
        }

        // Find starting and ending lines
        int startLine = 0;
        int endLine = int.MaxValue;
        if (args.Length > 1) {
            GetLine(args[1], filePath, out startLine);
            if (args.Length > 2) {
                GetLine(args[2], filePath, out endLine);
            }
        }

        string readCommandDetail = $"Read, {string.Join(", ", args)}: line {fileLine} of the file \"{currentFilePath}\"";
        if (readCommandStack.Contains(readCommandDetail)) {
            $"Multiple read commands lead to dead loops:\n{string.Join("\n", readCommandStack)}".Log(LogLevel.Warn);
            Toast.Show("Multiple read commands lead to dead loops\nPlease check log.txt for more details");
            Manager.DisableRunLater();
            return;
        }

        readCommandStack.Add(readCommandDetail);
        Manager.Controller.ReadFile(filePath, startLine, endLine, studioLine);
        if (readCommandStack.Count > 0) {
            readCommandStack.RemoveAt(readCommandStack.Count - 1);
        }

        void ToastAndLog(string text) {
            Toast.Show(text);
            text.Log();
        }

        void FindTheFile() {
            // Check for full and shortened Read versions
            if (fileDirectory != null) {
                // Path.Combine can handle the case when filePath is an absolute path
                string absoluteOrRelativePath = Path.Combine(fileDirectory, filePath);
                if (File.Exists(absoluteOrRelativePath)) {
                    filePath = absoluteOrRelativePath;
                } else {
                    string[] files = Directory.GetFiles(fileDirectory, $"{filePath}*.tas");
                    if (files.FirstOrDefault() is { } shortenedFilePath) {
                        filePath = shortenedFilePath;
                    }
                }
            }
        }
    }

    public static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
        if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
            int curLine = 0;
            foreach (string readLine in File.ReadLines(path)) {
                curLine++;
                string line = readLine.Trim();
                if (line == $"#{labelOrLineNumber}") {
                    lineNumber = curLine;
                    return;
                }
            }

            lineNumber = int.MaxValue;
        }
    }
}