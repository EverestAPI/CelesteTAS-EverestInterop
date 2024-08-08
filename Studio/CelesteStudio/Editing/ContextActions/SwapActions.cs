using System;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class SwapActions(Actions a, Actions b) : ContextAction {
    public override string Name => $"Swap {a.CharForAction()} and {b.CharForAction()}";

    public override AutoCompleteMenu.Entry? Check() {
        int startRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Min.Row;
        int endRow = Document.Selection.Empty ? Document.Caret.Row : Document.Selection.Max.Row;

        bool hasLeftRight = false;
        for (int row = startRow; row <= endRow; row++) {
            if (ActionLine.TryParse(Document.Lines[row], out var actionLine)) {
                hasLeftRight |= (actionLine.Actions & (a | b)) != 0;
            }
        }

        if (!hasLeftRight) {
            return null;
        }

        return CreateEntry("", () => {
            Editor.SwapSelectedActions(a, b);
        });
    }
}