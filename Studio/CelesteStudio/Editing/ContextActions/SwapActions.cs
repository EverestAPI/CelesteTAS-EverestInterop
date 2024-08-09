using CelesteStudio.Data;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class SwapActions(Actions a, Actions b, MenuEntry entry) : ContextAction {
    public override MenuEntry Entry => entry;

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

        return CreateEntry("", () => Editor.SwapSelectedActions(a, b));
    }
}