using System;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class ForceCombineInputFrames : ContextAction {
    public override string Identifier => "ContextActions_ForceCombineInputFrames";
    public override string DisplayName => "Force Combine Input Frames";
    public override Hotkey DefaultHotkey => Hotkey.KeyCtrl(Keys.L | Keys.Shift);

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
    public override string Identifier => "ContextActions_CombineConsecutiveSameInputs";
    public override string DisplayName => "Combine Consecutive Same Inputs";
    public override Hotkey DefaultHotkey => Hotkey.KeyCtrl(Keys.L);

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
        using var __ = Document.Update(raiseEvents: !dryRun);

        bool hasChanges = false;

        if (Document.Selection.Empty) {
            // Merge current input with surrounding inputs
            int curr = Document.Caret.Row;
            if (!ActionLine.TryParse(Document.Lines[curr], out var currActionLine)) {
                return false;
            }

            // Above
            int above = curr - 1;
            for (; above >= 0; above--) {
                if (!ActionLine.TryParse(Document.Lines[above], out var otherActionLine)) {
                    break;
                }

                if (currActionLine.Actions != otherActionLine.Actions ||
                    currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                    currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude ||
                    IsScreenTransition(above))
                {
                    break;
                }

                currActionLine.FrameCount += otherActionLine.FrameCount;
                hasChanges = true;
            }

            // Below
            int below = curr + 1;
            for (; below < Document.Lines.Count; below++) {
                if (!ActionLine.TryParse(Document.Lines[below], out var otherActionLine)) {
                    break;
                }

                if (currActionLine.Actions != otherActionLine.Actions ||
                    currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                    currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude ||
                    IsScreenTransition(below))
                {
                    break;
                }

                currActionLine.FrameCount += otherActionLine.FrameCount;
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

            ActionLine? forceMergeTarget = null;
            ActionLine? currentActionLine = null;
            int currentActionLineRow = -1;

            if (!sameActions) {
                // The algorithm goes backwards, but the target actions are the first valid line forwards
                for (int row = minRow; row <= maxRow; row++) {
                    if (ActionLine.TryParse(Document.Lines[row], out var nextActionLine)) {
                        forceMergeTarget = nextActionLine;
                        break;
                    }
                }

                if (forceMergeTarget == null) {
                    return false;
                }
            }

            for (int row = maxRow; row >= minRow; row--) {
                if (string.IsNullOrWhiteSpace(Document.Lines[row])) {
                    continue;
                }

                var nextActionLine = ActionLine.Parse(Document.Lines[row]);
                if (nextActionLine != null) {
                    if (currentActionLine == null) {
                        currentActionLine = nextActionLine;
                        currentActionLineRow = row;
                        continue;
                    }

                    if (!sameActions) {
                        // Just merge them, regardless if they are the same actions
                        currentActionLine = forceMergeTarget!.Value with {
                            FrameCount = currentActionLine.Value.FrameCount + nextActionLine.Value.FrameCount
                        };
                        if (!dryRun) {
                            Document.RemoveLines(row + 1, currentActionLineRow);
                            Document.ReplaceLine(row, currentActionLine.Value.ToString());
                            maxRow -= currentActionLineRow - row;
                        }

                        currentActionLineRow = row;
                        hasChanges = true;

                        continue;
                    }

                    if (currentActionLine.Value.Actions == nextActionLine.Value.Actions &&
                        currentActionLine.Value.FeatherAngle == nextActionLine.Value.FeatherAngle &&
                        currentActionLine.Value.FeatherMagnitude == nextActionLine.Value.FeatherMagnitude &&
                        !IsScreenTransition(row))
                    {
                        // Merge them, if they are the same kind
                        currentActionLine = currentActionLine.Value with {
                            FrameCount = currentActionLine.Value.FrameCount + nextActionLine.Value.FrameCount
                        };
                        if (!dryRun) {
                            Document.RemoveLines(row + 1, currentActionLineRow);
                            Document.ReplaceLine(row, currentActionLine.Value.ToString());
                            maxRow -= currentActionLineRow - row;
                        }

                        currentActionLineRow = row;
                        hasChanges = true;

                        continue;
                    }
                }

                // Prevent merges over commands, comments, etc.
                currentActionLine = nextActionLine;
                currentActionLineRow = row;
            }

            if (!dryRun) {
                Document.Selection.Clear();
                Document.Caret.Row = maxRow;
                Document.Caret = Editor.ClampCaret(Document.Caret);
            }
        }

        return hasChanges;
    }

    private static bool IsScreenTransition(int row) {
        // Screen transition frames don't have actions (except for buffering!)
        // And are followed by a #lvl_ label

        const Actions allowedActions = Actions.Jump | Actions.Jump2 | Actions.Dash | Actions.Dash2 | Actions.DemoDash | Actions.DemoDash2;

        if (!ActionLine.TryParse(Document.Lines[row], out var actionLine) ||
            (actionLine.Actions & ~allowedActions) != Actions.None)
        {
            return false;
        }

        for (int checkRow = row + 1; checkRow < Document.Lines.Count; checkRow++) {
            string line = Document.Lines[checkRow];
            if (line.StartsWith("#lvl_")) {
                return true;
            }

            // Other comments / action-lines in between aren't allowed
            if (line.StartsWith("#") || ActionLine.TryParse(line, out _)) {
                return false;
            }
        }

        return false;
    }
}
