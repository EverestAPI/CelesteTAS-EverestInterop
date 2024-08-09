using CelesteStudio.Data;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class SwapActions(Actions a, Actions b, MenuEntry entry) : ContextAction {
    public override MenuEntry Entry => entry;

    public override PopupMenu.Entry? Check() {
        int minRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Min.Row;
        int maxRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Max.Row;

        bool hasAction = false;
        for (int row = minRow; row <= maxRow; row++) {
            if (ActionLine.TryParse(Document.Lines[row], out var actionLine) && (actionLine.Actions & (a | b)) != 0) {
                hasAction = true;
                break;
            }
        }

        if (!hasAction) {
            return null;
        }

        return CreateEntry("", () => {
            using var __ = Document.Update();
            
            for (int row = minRow; row <= maxRow; row++) {
                if (!Editor.TryParseAndFormatActionLine(row, out var actionLine)) {
                    continue;
                }
                if (actionLine.Actions.HasFlag(a) && actionLine.Actions.HasFlag(b)) {
                    continue; // Nothing to do
                }
                
                if (actionLine.Actions.HasFlag(a)) {
                    actionLine.Actions = actionLine.Actions & ~a | b;
                } else if (actionLine.Actions.HasFlag(b)) {
                    actionLine.Actions = actionLine.Actions & ~b | a;
                }
                
                Document.ReplaceLine(row, actionLine.ToString());
            }
        });
    }
}