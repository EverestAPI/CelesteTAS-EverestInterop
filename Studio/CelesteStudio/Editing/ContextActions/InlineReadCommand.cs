using System;
using System.IO;
using System.Linq;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class InlineReadCommand : ContextAction {
    public override string Identifier => "ContextActions_InlineReadCommand";
    public override string DisplayName => "Inline 'Read' command";
    public override Hotkey DefaultHotkey => Hotkey.None;

    public override PopupMenu.Entry? Check() {
        string currentLine = Document.Lines[Document.Caret.Row];

        if (!CommandLine.TryParse(currentLine, out var commandLine) || !commandLine.IsCommand("Read")) {
            return null;
        }

        string? subPath = commandLine.Arguments.GetValueOrDefault(0);
        string? startLabel = commandLine.Arguments.GetValueOrDefault(1);
        string? endLabel = commandLine.Arguments.GetValueOrDefault(2);

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

        int startLine = 0;
        int endLine = lines.Length;

        if (startLabel != null && TryGetLine(startLabel, lines, out int line)) {
            startLine = line;
        }
        if (endLabel != null && TryGetLine(endLabel, lines, out line)) {
            endLine = line;
        }

        return CreateEntry($"{lines.Length} lines", () => {
            using var __ = Document.Update();

            try {
                Document.RemoveLine(Document.Caret.Row);
                Document.InsertLines(Document.Caret.Row, lines[startLine..endLine]);

                Editor.ScrollCaretIntoView();
            } catch (Exception) {
                // ignored
            }
        });
    }

    private static bool TryGetLine(string labelOrLineNumber, string[] lines, out int lineNumber) {
        if (int.TryParse(labelOrLineNumber, out lineNumber)) {
            // 1-indexed line number
            lineNumber -= 1;
            return true;
        }

        var labels = lines
            .Select((line, row) => (line, row))
            .Where(pair => CommentLine.IsLabel(pair.line))
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
