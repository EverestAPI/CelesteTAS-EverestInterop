using System;
using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class ForceCombineInputFrames : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_ForceCombineInputFrames;

    public override PopupMenu.Entry? Check() {
        if (Document.Selection.Empty) {
            // Can't force merge without a selection
            return null;
        }

        if (!CombineConsecutiveSameInputs.CombineInputs(sameActions: false, dryRun: true)) {
            return null;
        }

        return CreateEntry("", () => CombineConsecutiveSameInputs.CombineInputs(sameActions: false));
    }
}

public class CombineConsecutiveSameInputs : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_CombineConsecutiveSameInputs;

    public override PopupMenu.Entry? Check() {
        if (Document.Selection.Empty && !ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            // No merge target
            return null;
        } else if (!Document.Selection.Empty) {
            for (int row = Document.Selection.Min.Row; row <= Document.Selection.Max.Row; row++) {
                if (ActionLine.TryParse(Document.Lines[row], out _)) {
                    goto BreakLoop;
                }
            }
            // No action-lines inside selection
            return null;

            BreakLoop:;
        }

        if (!CombineInputs(sameActions: true, dryRun: true)) {
            return null;
        }

        return CreateEntry("", () => CombineInputs(sameActions: true));
    }

    public static bool CombineInputs(bool sameActions, bool dryRun = false) {
        using var __ = Document.Update();

        bool hasChanges = false;

        if (Document.Selection.Empty) {
            // Merge current input with surrounding inputs
            int curr = Document.Caret.Row;
            if (!Editor.TryParseAndFormatActionLine(curr, out var currActionLine)) {
                return false;
            }

            // Above
            int above = curr - 1;
            for (; above >= 0; above--) {
                if (!Editor.TryParseAndFormatActionLine(above, out var otherActionLine)) {
                    break;
                }

                if (currActionLine.Actions != otherActionLine.Actions ||
                    currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                    currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude)
                {
                    break;
                }

                currActionLine.Frames += otherActionLine.Frames;
                hasChanges = true;
            }

            // Below
            int below = curr + 1;
            for (; below < Document.Lines.Count; below++) {
                if (!Editor.TryParseAndFormatActionLine(below, out var otherActionLine)) {
                    break;
                }

                if (currActionLine.Actions != otherActionLine.Actions ||
                    currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                    currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude)
                {
                    break;
                }

                currActionLine.Frames += otherActionLine.Frames;
                hasChanges = true;
            }

            // Account for overshoot by 1
            above = Math.Min(Document.Lines.Count, above + 1);
            below = Math.Max(0, below - 1);

            if (!dryRun) {
                Document.RemoveLines(above, below);
                Document.InsertLine(above, currActionLine.ToString());

                Document.Caret.Row = above;
                Document.Caret.Col = Editor.SnapColumnToActionLine(currActionLine, Document.Caret.Col);
            }
        } else {
            // Merge everything inside the selection
            int minRow = Document.Selection.Min.Row;
            int maxRow = Document.Selection.Max.Row;

            ActionLine? activeActionLine = null;
            int activeRowStart = -1;

            for (int row = minRow; row <= maxRow; row++) {
                if (!Editor.TryParseAndFormatActionLine(row, out var currActionLine)) {
                    // "Flush" the previous lines
                    if (activeActionLine != null) {
                        Document.RemoveLines(activeRowStart, row - 1);
                        Document.InsertLine(activeRowStart, activeActionLine.Value.ToString());

                        maxRow -= row - 1 - activeRowStart;
                        row = activeRowStart + 1;

                        activeActionLine = null;
                        activeRowStart = -1;
                    }
                    continue; // Skip non-input lines
                }

                if (activeActionLine == null) {
                    activeActionLine = currActionLine;
                    activeRowStart = row;
                    continue;
                }

                if (!sameActions) {
                    // Just merge them, regardless if they are the same actions
                    activeActionLine = activeActionLine.Value with { Frames = activeActionLine.Value.Frames + currActionLine.Frames };
                    hasChanges = true;
                    continue;
                }

                if (currActionLine.Actions == activeActionLine.Value.Actions &&
                    currActionLine.FeatherAngle == activeActionLine.Value.FeatherAngle &&
                    currActionLine.FeatherMagnitude == activeActionLine.Value.FeatherMagnitude)
                {
                    activeActionLine = activeActionLine.Value with { Frames = activeActionLine.Value.Frames + currActionLine.Frames };
                    hasChanges = true;
                    continue;
                }

                if (!dryRun) {
                    // Current line is different, so change the active one
                    Document.RemoveLines(activeRowStart, row - 1);
                    Document.InsertLine(activeRowStart, activeActionLine.Value.ToString());
                }

                activeActionLine = currActionLine;
                activeRowStart++;

                // Account for changed line counts
                maxRow -= row - activeRowStart;
                row = activeRowStart;
            }

            // "Flush" the remaining line
            if (activeActionLine != null) {
                if (!dryRun) {
                    Document.RemoveLines(activeRowStart, maxRow);
                    Document.InsertLine(activeRowStart, activeActionLine.Value.ToString());
                }

                maxRow = activeRowStart;
            }

            if (!dryRun) {
                Document.Selection.Clear();
                Document.Caret.Row = maxRow;
                Document.Caret = Editor.ClampCaret(Document.Caret);
            }
        }

        return hasChanges;
    }
}
