using CelesteStudio.Data;
using CelesteStudio.Editing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CelesteStudio.Tool;

/// Creates a single standalone .tas file with no other file dependencies
public static class IntegrateReadFiles {
    private static readonly Regex recordCountRegex = new(@"^\s*RecordCount:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Generate(string sourceFile, string targetFile) {
        HashSet<string> files = [sourceFile];
        List<string> integratedLines = [];
        int totalFrameCount = 0;

        string currentTargetCommand = string.Empty;
        foreach ((string line, _, string filePath, var targetCommand) in Studio.Instance.Editor.IterateDocumentLines(includeReads: true, sourceFile)) {
            // Write original read command as comment
            if (targetCommand != null && currentTargetCommand != targetCommand.Value.OriginalText) {
                integratedLines.Add($"# {targetCommand.Value.OriginalText}");
                currentTargetCommand = targetCommand.Value.OriginalText;
            }

            if (ActionLine.TryParse(line, out var actionLine)) {
                totalFrameCount += actionLine.FrameCount;
            }

            integratedLines.Add(line);
            files.Add(filePath);
        }

        // Sum up RecordCount
        int totalRecordCount = 0;
        foreach (string file in files) {
            if (!Editor.FileCache.TryGetValue(file, out string[]? lines)) {
                Editor.FileCache[file] = lines = File.ReadAllLines(file);
            }

            foreach (string line in lines) {
                if (recordCountRegex.Match(line) is { Success: true } match && int.TryParse(match.Groups[1].Value, out int count)) {
                    totalRecordCount += count;
                    break;
                }
            }
        }

        integratedLines.Insert(0, $"FrameCount: {totalFrameCount}");
        integratedLines.Insert(1, $"TotalRecordCount: {totalRecordCount}");
        integratedLines.Insert(2, string.Empty);

        File.WriteAllText(targetFile, Document.FormatLinesToText(integratedLines));
    }
}
