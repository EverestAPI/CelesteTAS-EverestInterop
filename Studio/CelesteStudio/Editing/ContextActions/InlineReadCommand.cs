using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using CelesteStudio.Data;
using CelesteStudio.Editing.AutoCompletion;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class InlineReadCommand : ContextAction {
    public override string Name => "Inline Read command";

    public override PopupMenu.Entry? Check() {
        string currentLine = Document.Lines[Document.Caret.Row];

        if (!CommandLine.TryParse(currentLine, out var commandLine) || !commandLine.IsCommand("Read")) {
            return null;
        }

        string? subPath = commandLine.Args.GetValueOrDefault(0);
        string? startLabel = commandLine.Args.GetValueOrDefault(1);
        string? endLabel = commandLine.Args.GetValueOrDefault(2);

        if (subPath == null ||
            Document.FilePath == Document.ScratchFile ||
            Path.GetDirectoryName(Document.FilePath) is not { } documentDir) 
        {
            return null;
        }
        
        var fullPath = Path.Combine(documentDir, $"{subPath}.tas");
        if (!File.Exists(fullPath)) {
            return null;
        }

        string[] lines = File.ReadAllText(fullPath)
            .ReplaceLineEndings(Document.NewLine.ToString())
            .SplitDocumentLines();

        string replacement = ReadTasRange(lines, startLabel?.Trim(), endLabel?.Trim());
        
        return CreateEntry($"{lines.Length} lines", () => {
            try {
                if (!replacement.EndsWith(Document.NewLine)) {
                    replacement += Document.NewLine;
                }
                Document.ReplaceLine(Document.Caret.Row, replacement);
            } catch (Exception) {
                // ignored
            }
        });
    }

    private string ReadTasRange(string[] lines, string? startLabel, string? endLabel) {
        int startLine = 0;
        int endLineInclusive = lines.Length;
        
        if (startLabel != null && TryGetLine(startLabel, out int line)) {
            startLine = line;
        }
        if (endLabel != null && TryGetLine(endLabel, out line)) {
            endLineInclusive = line;
        }

        return string.Join(Document.NewLine, lines[(startLine - 1)..(endLineInclusive - 1)]);
    }
    
    // 1-indexed line number
    private static bool TryGetLine(string labelOrLineNumber, out int lineNumber) {
        if (int.TryParse(labelOrLineNumber, out lineNumber)) {
            return true;
        }
        
        // All labels need to start with a # and immediately follow with the text
        var labels = Document.Lines
            .Select((line, row) => (line, row))
            .Where(pair => pair.line.Length >= 2 && pair.line[0] == '#' && char.IsLetter(pair.line[1]))
            .Select(pair => pair with { line = pair.line[1..] }) // Remove the #
            .ToArray();
        
        // Find label
        foreach (var (label, line) in labels) {
            if (label == labelOrLineNumber) {
                lineNumber = line;
                return true;
            }
        }
        
        return false;
    }
}