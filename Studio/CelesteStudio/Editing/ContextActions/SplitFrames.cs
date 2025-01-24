using System.Linq;
using CelesteStudio.Data;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class SplitFrames : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_SplitFrames;

    public override PopupMenu.Entry? Check() {
        (int minRow, int maxRow) = Document.Selection.Empty
            ? (Document.Caret.Row, Document.Caret.Row)
            : (Document.Selection.Min.Row, Document.Selection.Max.Row);

        bool valid = false;
        for (int row = minRow; row <= maxRow; row++) {
            if (!ActionLine.TryParse(Document.Lines[row], out var actionLine) || actionLine.FrameCount <= 1) {
                continue;
            }

            valid = true;
            break;
        }

        if (!valid) {
            return null;
        }

        return CreateEntry("", () => {
            using var __ = Document.Update();

            int extraLines = 0;

            for (int row = maxRow; row >= minRow; row--) {
                if (!ActionLine.TryParse(Document.Lines[row], out var actionLine) || actionLine.FrameCount == 0) {
                    continue;
                }

                extraLines += actionLine.FrameCount - 1;
                Document.ReplaceLines(row, Enumerable.Repeat((actionLine with { FrameCount = 1 }).ToString(), actionLine.FrameCount).ToArray());
            }

            if (!Document.Selection.Empty) {
                int endRow = maxRow + extraLines;
                Document.Selection = new Selection {
                    Start = new CaretPosition(minRow, 0),
                    End = new CaretPosition(endRow, Document.Lines[endRow].Length),
                };
            }

            Document.Caret.Row = maxRow;
            Document.Caret = Editor.ClampCaret(Document.Caret);
        });
    }
}
