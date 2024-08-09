using System;
using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class SplitFrames : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_SplitFrames;

    public override PopupMenu.Entry? Check() {
        (int minRow, int maxRow) = Document.Selection.Empty
            ? (Document.Caret.Row, Document.Caret.Row)
            : (Document.Selection.Min.Row, Document.Selection.Max.Row);
        
        for (int row = minRow; row <= maxRow; row++) {
            if (ActionLine.TryParse(Document.Lines[row], out _)) {
                // Found something to split 
                return CreateEntry("", () => Editor.SplitLines());
            }
        }

        // No action-lines inside selection
        return null;
    }
}