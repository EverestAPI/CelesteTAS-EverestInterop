using System;
using System.IO;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using CelesteStudio.Data;
using CelesteStudio.Editing.AutoCompletion;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class InlineReadCommand : ContextAction {
    public override string Name => "Inline read command";

    public override PopupMenu.Entry? Check() {
        string currentLine = Document.Lines[Document.Caret.Row];

        if (!(CommandLine.TryParse(currentLine, out var commandLine) && commandLine.IsCommand("Read"))) {
            return null;
        }

        string? filename = commandLine.Args.GetValueOrDefault(0);
        string? startLabel = commandLine.Args.GetValueOrDefault(1);
        string? endLabel = commandLine.Args.GetValueOrDefault(2);

        if (filename == null) {
            return null;
        }

        string path = Path.Combine(Path.GetDirectoryName(Document.FilePath) ?? string.Empty, filename);
        if (!Path.HasExtension(path)) {
            path = Path.ChangeExtension(path, ".tas");
        }

        string[] lines = File.ReadAllText(path).SplitDocumentLines();
        string replacement = ReadTasRange(lines, startLabel, endLabel);
        
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

        if (startLabel != null) TryGetLine(startLabel, lines, out startLine);
        if (endLabel != null) TryGetLine(endLabel, lines, out endLineInclusive);

        return string.Join(Document.NewLine, lines[(startLine-1)..(endLineInclusive-1)]);
    }
    
    // 1-indexed line number
    public static bool TryGetLine(string labelOrLineNumber, string[] lines, out int lineNumber) {
        if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
            int curLine = 0;
            Regex labelRegex = new(@$"^\s*#\s*{Regex.Escape(labelOrLineNumber)}\s*$");
            foreach (string readLine in lines) {
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