using CelesteStudio.Data;
using CelesteStudio.Util;

namespace CelesteStudio.Editing.ContextActions;

public class InlineRepeatCommand : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_InlineRepeatCommand;

    public override PopupMenu.Entry? Check() {
        int minRow = Document.Caret.Row;
        int maxRow = Document.Caret.Row;
        
        // Find repeat command bounds
        CommandLine repeatStart = default;
        CommandLine repeatEnd = default;
        
        while (minRow >= 0 && !(CommandLine.TryParse(Document.Lines[minRow], out repeatStart) && repeatStart.IsCommand("Repeat")))
            minRow--;
        while (maxRow < Document.Lines.Count && !(CommandLine.TryParse(Document.Lines[maxRow], out repeatEnd) && repeatEnd.IsCommand("EndRepeat")))
            maxRow++;
        
        if (minRow >= maxRow || !repeatStart.IsCommand("Repeat") || !repeatEnd.IsCommand("EndRepeat")) {
            // Not inside a 'Repeat' block
            return null;
        }
        
        if (repeatStart.Args!.Length < 1 || !int.TryParse(repeatStart.Args[0], out int repeatCount)) {
            return null;
        }
        
        var inputLines = Document.Lines.GetArrayRange((minRow + 1)..maxRow);
        
        return CreateEntry($"{inputLines.Length * repeatCount} lines", () => {
            using var __ = Document.Update();
            
            Document.RemoveLines(minRow, maxRow);
            for (int i = 0; i < repeatCount; i++) {
                Document.InsertLines(minRow, inputLines);
            }
            
            Document.Selection.Clear();
            Document.Caret.Row = minRow + inputLines.Length * repeatCount - 1;
            Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
        });
    }
}