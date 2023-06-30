using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CelesteStudio;

internal static class IntegrateReadFiles {
    public static void Generate() {
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
            string integratedText = ReadAllFiles(mainFilePath);
            File.WriteAllText(saveFilePath, integratedText);
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

    private static string ReadAllFiles(string filePath, IEnumerable<string> lines = null) {
        StringBuilder result = new();
        lines ??= File.ReadLines(filePath);
        foreach (string lineText in lines) {
            if (TryParseReadCommand(filePath, lineText, out string readText)) {
                result.AppendLine($"#{lineText.Trim()}");
                result.AppendLine(readText);
            } else {
                result.AppendLine(lineText);
            }
        }

        return result.ToString();
    }

    private static bool TryParseReadCommand(string filePath, string readCommand, out string readText) {
        readText = null;
        readCommand = readCommand.Trim();
        if (!readCommand.StartsWith("read", StringComparison.InvariantCultureIgnoreCase)) {
            return false;
        }

        Regex spaceRegex = new(@"^[^,]+?\s+[^,]");
        string[] args = spaceRegex.IsMatch(readCommand) ? readCommand.Split() : readCommand.Split(',');
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

        readText = ReadAllFiles(filePath, File.ReadLines(readFilePath).Take(endLine).Skip(startLine - 1));
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
}

class ReadFileNotExistException : Exception {
    public ReadFileNotExistException(string readCommand, string filePath) : base($"{readCommand}\n{filePath}") { }
}