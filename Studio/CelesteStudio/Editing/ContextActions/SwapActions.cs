using CelesteStudio.Data;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class SwapActions(Actions a, Actions b) : ContextAction {
    public override string Name => $"Swap {a.CharForAction()} and {b.CharForAction()}";

    public override PopupMenu.Entry? Check() {
        int startRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Min.Row;
        int endRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Max.Row;

        bool hasAction = false;
        for (int row = startRow; row <= endRow; row++) {
            if (ActionLine.TryParse(Document.Lines[row], out var actionLine)) {
                hasAction |= (actionLine.Actions & (a | b)) != 0;
            }
        }

        if (!hasAction) {
            return null;
        }

        return CreateEntry("", () => {
            Editor.SwapSelectedActions(a, b);
        });
    }
}