using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CelesteStudio.Entities;

namespace CelesteStudio;

internal static class IntegrateReadFiles {
    private static readonly Regex readCommandRegex = new(@"^read( |,)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex spaceSeparatorRegex = new(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);
    private static readonly Regex recordCountRegex = new(@"^\s*RecordCount:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex authorRegex = new(@"^\s*Author:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> readFiles = new();

    public static void Generate() {
        readFiles.Clear();

        string mainFilePath = Studio.Instance.richText.CurrentFileName;
        if (!File.Exists(mainFilePath)) {
            MessageBox.Show(mainFilePath, "Opened file does not exist");
            return;
        }

        string saveFilePath = ShowSaveDialog(mainFilePath);
        if (saveFilePath == null) {
            return;
        }

        try {
            List<string> allLines = ReadAllFiles(mainFilePath);
            InsertHeader(allLines);
            File.WriteAllText(saveFilePath, string.Join("\n", allLines));
            Studio.Instance.OpenFile(saveFilePath);
        } catch (ReadFileNotExistException e) {
            MessageBox.Show(e.Message, "Read file does not exist");
        }
    }

    private static string ShowSaveDialog(string filePath) {
        using SaveFileDialog dialog = new();
        dialog.DefaultExt = ".tas";
        dialog.AddExtension = true;
        dialog.Filter = "TAS|*.tas";
        dialog.FilterIndex = 0;
        dialog.InitialDirectory = Path.GetDirectoryName(filePath);
        dialog.FileName = Path.GetFileNameWithoutExtension(filePath) + "_Integrated.tas";

        if (dialog.ShowDialog() == DialogResult.OK) {
            return dialog.FileName;
        } else {
            return null;
        }
    }

    private static List<string> ReadAllFiles(string filePath, IEnumerable<string> lines = null) {
        List<string> result = new();
        lines ??= File.ReadLines(filePath);

        foreach (string lineText in lines) {
            readFiles.Add(filePath);
            if (TryParseReadCommand(filePath, lineText, out List<string> readText)) {
                result.Add($"#{lineText.Trim()}");
                result.AddRange(readText);
            } else {
                result.Add(lineText);
            }
        }

        return result;
    }
    private static bool TryParseReadCommand(string filePath, string readCommand, out List<string> readText) {
        readText = null;
        readCommand = readCommand.Trim();
        if (!readCommandRegex.IsMatch(readCommand)) {
            return false;
        }

        string[] args = spaceSeparatorRegex.IsMatch(readCommand) ? readCommand.Split() : readCommand.Split(',');
        args = args.Select(text => text.Trim()).ToArray();
        if (!args[0].Equals("read", StringComparison.InvariantCultureIgnoreCase) || args.Length < 2) {
            return false;
        }

        string readFilePath = args[1];
        string fileDirectory = Path.GetDirectoryName(filePath);
        readFilePath = FindReadFile(filePath, fileDirectory, readFilePath);

        if (!File.Exists(readFilePath)) {
            // for compatibility with tas files downloaded from discord
            // discord will replace spaces in the file name with underscores
            readFilePath = args[1].Replace(" ", "_");
            readFilePath = FindReadFile(filePath, fileDirectory, readFilePath);
        }

        if (!File.Exists(readFilePath)) {
            throw new ReadFileNotExistException(readCommand, filePath);
        }

        int startLine = 1;
        int endLine = int.MaxValue;

        if (args.Length >= 3) {
            startLine = GetLineNumber(readFilePath, args[2]);
        }

        if (args.Length >= 4) {
            endLine = GetLineNumber(readFilePath, args[3]);
        }

        readText = ReadAllFiles(readFilePath, File.ReadLines(readFilePath).Take(endLine).Skip(startLine - 1));

        return true;
    }

    private static string FindReadFile(string filePath, string fileDirectory, string readFilePath) {
        // Check for full and shortened Read versions
        if (fileDirectory != null) {
            // Path.Combine can handle the case when filePath is an absolute path
            string absoluteOrRelativePath = Path.Combine(fileDirectory, readFilePath);
            if (File.Exists(absoluteOrRelativePath) && absoluteOrRelativePath != filePath) {
                readFilePath = absoluteOrRelativePath;
            } else if (Directory.GetParent(absoluteOrRelativePath) is { } directoryInfo && Directory.Exists(directoryInfo.ToString())) {
                string[] files = Directory.GetFiles(directoryInfo.ToString(), $"{Path.GetFileName(readFilePath)}*.tas");
                if (files.FirstOrDefault(path => path != filePath) is { } shortenedFilePath) {
                    readFilePath = shortenedFilePath;
                }
            }
        }

        return readFilePath;
    }

    private static int GetLineNumber(string path, string labelOrLineNumber) {
        if (int.TryParse(labelOrLineNumber, out int lineNumber)) {
            return lineNumber;
        }

        int currentLine = 1;
        foreach (string readLine in File.ReadLines(path)) {
            string line = readLine.Trim();
            if (line == $"#{labelOrLineNumber}") {
                return currentLine;
            }

            currentLine++;
        }

        return 1;
    }

    private static void InsertHeader(List<string> allLines) {
        int i = 0;
        allLines.Insert(i++, $"Author: {GetAuthor(allLines)}");
        allLines.Insert(i++, $"FrameCount: {GetFrameCount(allLines)}");
        allLines.Insert(i++, $"TotalRecordCount: {GetTotalRecordCount()}");
        allLines.Insert(i++, "");
    }

    private static string GetAuthor(List<string> lines) {
        foreach (string line in lines) {
            if (authorRegex.Match(line) is {Success: true} match) {
                return match.Groups[1].Value;
            }
        }

        return "";
    }

    private static int GetFrameCount(List<string> lines) {
        int result = 0;
        foreach (string line in lines) {
            InputRecord record = new(line);
            if (record.Frames > 0) {
                result += record.Frames;
            }
        }

        return result;
    }

    private static int GetTotalRecordCount() {
        int recordCount = 0;
        foreach (string filePath in readFiles) {
            foreach (string line in File.ReadLines(filePath)) {
                if (recordCountRegex.Match(line) is {Success: true} match && int.TryParse(match.Groups[1].Value, out int count)) {
                    recordCount += count;
                    break;
                }
            }
        }

        return recordCount;
    }
}

class ReadFileNotExistException : Exception {
    public ReadFileNotExistException(string readCommand, string filePath) : base($"{readCommand}\n{filePath}") { }
}