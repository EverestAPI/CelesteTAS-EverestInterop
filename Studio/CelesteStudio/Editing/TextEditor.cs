using CelesteStudio.Editing.AutoCompletion;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CelesteStudio.Editing;

public class TextEditor(Document document, Scrollable scrollable) : TextViewer(document, scrollable) {

    #region Bindings

    private static InstanceActionBinding<TextEditor> CreateAction(string identifier, string displayName, Hotkey defaultHotkey, Action<TextEditor> action)
        => new(identifier, displayName, Binding.Category.Editor, defaultHotkey, action);

    protected static readonly InstanceBinding Cut = CreateAction("Editor_Cut", "Cut", Hotkey.KeyCtrl(Keys.X), editor => editor.OnCut());
    protected static readonly InstanceBinding Paste = CreateAction("Editor_Paste", "Paste", Hotkey.KeyCtrl(Keys.V), editor => editor.OnPaste());

    protected static readonly InstanceBinding Undo = CreateAction("Editor_Undo", "Undo", Hotkey.KeyCtrl(Keys.Z), editor => editor.OnUndo());
    protected static readonly InstanceBinding Redo = CreateAction("Editor_Redo", "Redo", Hotkey.KeyCtrl(Keys.Z | Keys.Shift), editor => editor.OnRedo());

    protected static readonly InstanceBinding DeleteSelectedLines = CreateAction("Editor_DeleteSelectedLines", "Delete Selected Lines", Hotkey.KeyCtrl(Keys.Y), editor => editor.OnDeleteSelectedLines());

    protected static readonly InstanceBinding OpenAutoCompleteMenu = CreateAction("Editor_OpenAutoCompleteMenu", "Open Auto-Complete Menu...", Hotkey.KeyCtrl(Keys.Space), editor => {
        editor.autoCompleteMenu?.Refresh();
        editor.Recalc();
    });

    public static new readonly InstanceBinding[] AllBindings = [
        Cut, Copy, Paste,
        Undo, Redo,
        SelectAll, SelectBlock,
        Find, GoTo, ToggleFolding,
        DeleteSelectedLines,
        OpenAutoCompleteMenu,
    ];

    #endregion

    // Customizable auto-complete setup
    protected AutoCompleteMenu? autoCompleteMenu = null;

    public override ContextMenu CreateContextMenu() {
        return new() {
            Items = {
                Cut.CreateItem(this),
                Copy.CreateItem(this),
                Paste.CreateItem(this),
                new SeparatorMenuItem(),
                Undo.CreateItem(this),
                Redo.CreateItem(this),
                new SeparatorMenuItem(),
                SelectAll.CreateItem(this),
                SelectBlock.CreateItem(this),
                new SeparatorMenuItem(),
                Find.CreateItem(this),
                GoTo.CreateItem(this),
                ToggleFolding.CreateItem(this),
                new SeparatorMenuItem(),
                DeleteSelectedLines.CreateItem(this),
                new SeparatorMenuItem(),
                OpenAutoCompleteMenu.CreateItem(this),
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (ActivePopupMenu is { } menu && menu.HandleKeyDown(e)) {
            e.Handled = true;
            Recalc();
            return;
        }

        if (GetQuickEdits().Any()) {
            // Cycle
            if (e.Key == Keys.Tab) {
                if (e.Shift) {
                    SelectPrevQuickEdit();
                } else {
                    SelectNextQuickEdit();
                }

                // Don't start a new base auto-complete. Only arguments
                if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                    autoCompleteMenu?.Refresh();
                } else {
                    ClosePopupMenu();
                }

                e.Handled = true;
                Recalc();
                return;
            }
            // Cancel
            if (e.Key == Keys.Escape) {
                ClearQuickEdits();
                Document.Selection.Clear();

                e.Handled = true;
                Recalc();
                return;
            }
            // Finish + Go to end
            if (e.Key == Keys.Enter) {
                SelectQuickEditIndex(GetQuickEdits().Count() - 1);
                ClearQuickEdits();
                Document.Caret = Document.Selection.Max;
                Document.Selection.Clear();

                e.Handled = true;
                Recalc();
                return;
            }
        }

        switch (e.Key) {
            case Keys.Backspace:
                OnDelete(e.HasCommonModifier() ? CaretMovementType.WordLeft : CaretMovementType.CharLeft);
                goto Handled;
            case Keys.Delete:
                OnDelete(e.HasCommonModifier() ? CaretMovementType.WordRight : CaretMovementType.CharRight);
                goto Handled;
            case Keys.Enter:
                OnEnter(e.HasCommonModifier(), up: e.Shift);
                goto Handled;

            case Keys.Up when e.HasAlternateModifier(): {
                // Move lines
                using var __ = Document.Update();
                if (Document.Caret.Row > 0 && Document.Selection is { Empty: false, Min.Row: > 0 }) {
                    var line = Document.Lines[Document.Selection.Min.Row - 1];
                    Document.RemoveLine(Document.Selection.Min.Row - 1);
                    Document.InsertLine(Document.Selection.Max.Row, line);

                    Document.Selection.Start.Row--;
                    Document.Selection.End.Row--;
                    Document.Caret.Row--;
                } else if (Document.Caret.Row > 0 && Document.Selection.Empty) {
                    Document.SwapLines(Document.Caret.Row, Document.Caret.Row - 1);
                    Document.Caret.Row--;
                }

                goto Handled;
            }
            case Keys.Down when e.HasAlternateModifier(): {
                // Move lines
                using var __ = Document.Update();
                if (Document.Caret.Row < Document.Lines.Count - 1 && !Document.Selection.Empty && Document.Selection.Max.Row < Document.Lines.Count - 1) {
                    var line = Document.Lines[Document.Selection.Max.Row + 1];
                    Document.RemoveLine(Document.Selection.Max.Row + 1);
                    Document.InsertLine(Document.Selection.Min.Row, line);

                    Document.Selection.Start.Row++;
                    Document.Selection.End.Row++;
                } else if (Document.Caret.Row < Document.Lines.Count - 1 && Document.Selection.Empty) {
                    Document.SwapLines(Document.Caret.Row, Document.Caret.Row + 1);
                    Document.Caret.Row++;
                }

                goto Handled;
            }
        }

        base.OnKeyDown(e);
        if (e.Handled) {
            return;
        }

        // macOS will make a beep sounds when the event isn't handled
        // ..that also means OnTextInput won't be called..
        if (Eto.Platform.Instance.IsMac) {
            e.Handled = true;
            if (e.KeyChar != char.MaxValue) {
                OnTextInput(new TextInputEventArgs(e.KeyChar.ToString()));
            }
        } else {
            BaseOnKeyDown(e);
        }

        return;

        Handled:
        e.Handled = true;
        Recalc();
        ScrollCaretIntoView();
    }

    protected override bool CheckHotkey(Hotkey hotkey) {
        // Handle bindings
        foreach (var binding in AllBindings) {
            foreach (var entry in binding.InstanceEntries) {
                if (Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey) == hotkey && entry.Action(this)) {
                    Recalc();
                    ScrollCaretIntoView();

                    return true;
                }
            }
        }

        return false;
    }

    #region Editing Actions

    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0])) {
            return;
        }

        if (e.Text.Length == 1 && CheckHotkey(Hotkey.Char(e.Text[0]))) {
            return;
        }

        using var __ = Document.Update();

        Document.Caret = ClampCaret(Document.Caret);

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }

        Document.Insert(e.Text);
        DesiredVisualCol = Document.Caret.Col;

        Recalc();
        ScrollCaretIntoView();

        autoCompleteMenu?.Refresh();
    }

    protected virtual void OnDelete(CaretMovementType direction) {
        using var __ = Document.Update();

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
            return;
        }

        var oldCaret = Document.Caret;
        Document.Caret = GetNewTextCaretPosition(direction);

        if (oldCaret.Row == Document.Caret.Row) {
            Document.RemoveRangeInLine(oldCaret.Row, oldCaret.Col, Document.Caret.Col);
            Document.Caret.Col = Math.Min(Document.Caret.Col, oldCaret.Col);
            Document.Caret = ClampCaret(Document.Caret, wrapLine: true);

            autoCompleteMenu?.Refresh(open: false);
        } else {
            var min = Document.Caret < oldCaret ? Document.Caret : oldCaret;
            var max = Document.Caret < oldCaret ? oldCaret : Document.Caret;

            RemoveRange(min, max);
            Document.Caret = min;

            ClosePopupMenu();
        }

        DesiredVisualCol = Document.Caret.Col;
    }

    protected virtual void OnEnter(bool splitLines, bool up) {
        using var __ = Document.Update();

        string line = Document.Lines[Document.Caret.Row];
        string lineTrimmedStart = line.TrimStart();
        int leadingSpaces = line.Length - lineTrimmedStart.Length;

        // Auto-split on first and last column since nothing is broken there
        bool autoSplit = Document.Caret.Col <= leadingSpaces || Document.Caret.Col == line.Length;

        int offset = up ? 0 : 1;
        if (autoSplit || splitLines) {
            if (!Document.Selection.Empty) {
                RemoveRange(Document.Selection.Min, Document.Selection.Max);
                Document.Caret = Document.Selection.Min;
                Document.Selection.Clear();
            }

            Document.Insert($"{Document.NewLine}");
        } else {
            int newRow = Document.Caret.Row + offset;
            if (GetCollapse(Document.Caret.Row) is { } collapse) {
                newRow = (up ? collapse.MinRow : collapse.MaxRow) + offset;
            }

            Document.InsertLine(newRow, string.Empty);
            Document.Caret.Row = newRow;
            Document.Caret.Col = DesiredVisualCol = 0;
        }

        Document.Selection.Clear();
    }

    private void OnUndo() {
        Document.Selection.Clear();
        Document.Undo();

        // Don't start a new base auto-complete. Only arguments
        if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
            autoCompleteMenu?.Refresh();
        } else {
            ClosePopupMenu();
        }
    }

    private void OnRedo() {
        Document.Selection.Clear();
        Document.Redo();

        // Don't start a new base auto-complete. Only arguments
        if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
            autoCompleteMenu?.Refresh();
        } else {
            ClosePopupMenu();
        }
    }

    private void OnCut() {
        using var __ = Document.Update();

        OnCopy();

        if (Document.Selection.Empty) {
            Document.RemoveLine(Document.Caret.Row);
        } else if (Document.Selection.Min.Col == 0 && Document.Selection.Max.Col == Document.Lines[Document.Selection.Max.Row].Length) {
            // Remove the lines entirely when the selection is the full range
            Document.RemoveLines(Document.Selection.Min.Row, Document.Selection.Max.Row);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        } else {
            OnDelete(CaretMovementType.None);
        }
    }

    protected virtual void OnPaste() {
        if (!Clipboard.Instance.ContainsText) {
            return;
        }

        using var __ = Document.Update();

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }

        string clipboardText = Clipboard.Instance.Text.ReplaceLineEndings(Document.NewLine.ToString());
        Document.Insert(clipboardText);
    }

    private void OnDeleteSelectedLines() {
        using var __ = Document.Update();

        ClosePopupMenu();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }

        Document.RemoveLines(minRow, maxRow);
        Document.Selection.Clear();
        Document.Caret.Row = minRow;
    }

    protected void InsertLine(string text) {
        using var __ = Document.Update();

        CollapseSelection();

        if (Settings.Instance.InsertDirection == InsertDirection.Above) {
            Document.InsertLineAbove(text);

            if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
                Document.Caret.Row--;
                Document.Caret.Col = DesiredVisualCol = Document.Lines[Document.Caret.Row].Length;
            }
        } else if (Settings.Instance.InsertDirection == InsertDirection.Below) {
            Document.InsertLineBelow(text);

            if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
                int newLines = text.Count(c => c == Document.NewLine) + 1;
                Document.Caret.Row += newLines;
                Document.Caret.Col = DesiredVisualCol = Document.Lines[Document.Caret.Row].Length;
            }
        }
    }

    /// Deletes text in the specified range, while accounting for collapsed
    protected void RemoveRange(CaretPosition min, CaretPosition max) {
        if (GetCollapse(min.Row) is { } collapse) {
            var foldMin = new CaretPosition(collapse.MinRow, 0);
            var foldMax = new CaretPosition(collapse.MinRow, Document.Lines[collapse.MinRow].Length);
            if (min <= foldMin && max >= foldMax) {
                // Entire folding is selected, so just remove it entirely
                Document.RemoveRange(min, max);
                return;
            }

            // Only partially selected, so don't delete the collapsed content, only the stuff around it
            if (min.Row == max.Row) {
                Document.RemoveRange(min, max);
            } else {
                Document.RemoveRange(min, new CaretPosition(collapse.MinRow, Document.Lines[collapse.MinRow].Length));
                Document.RemoveRange(new CaretPosition(collapse.MaxRow, Document.Lines[collapse.MaxRow].Length), max);
            }
        } else {
            Document.RemoveRange(min, max);
        }
    }

    #endregion

    #region Quick Edit

    /*
     * Quick-edits are anchors to switch through with tab and edit
     * They are used by auto-complete snippets
     */

    public record struct QuickEditAnchorData {
        public required int Index;
        public required string DefaultText;
    }

    /// Creates an action, which will insert the quick edit when invoked
    public Action? CreateQuickEditAction(string insert, bool hasArguments) {
        var quickEdit = QuickEdit.Parse(insert);
        if (quickEdit == null) {
            return null;
        }

        return () => {
            var oldCaret = Document.Caret;

            using var __ = Document.Update();
            Document.ReplaceLine(oldCaret.Row, quickEdit.Value.ActualText);

            ClearQuickEdits();

            if (quickEdit.Value.Selections.Length > 0) {
                for (int i = 0; i < quickEdit.Value.Selections.Length; i++) {
                    var selection = quickEdit.Value.Selections[i];
                    var defaultText = quickEdit.Value.ActualText.SplitDocumentLines()[selection.Min.Row][selection.Min.Col..selection.Max.Col];

                    // Quick-edit selections are relative, not absolute
                    Document.AddAnchor(new Anchor {
                        Row = selection.Min.Row + oldCaret.Row,
                        MinCol = selection.Min.Col, MaxCol = selection.Max.Col,
                        UserData = new QuickEditAnchorData { Index = i, DefaultText = defaultText },
                        OnRemoved = ClearQuickEdits,
                    });
                }
                SelectQuickEditIndex(0);
            } else {
                DesiredVisualCol = Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
            }

            // Keep open for arguments (but not a new base auto-complete)
            if (hasArguments && !string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                autoCompleteMenu?.Refresh();
            } else {
                ClosePopupMenu();
            }
        };
    }

    public void SelectNextQuickEdit() {
        var quickEdits = GetQuickEdits().ToArray();
        // Sort linearly inside the document
        Array.Sort(quickEdits, (a, b) => a.Row == b.Row
            ? a.MinCol - b.MinCol
            : a.Row - b.Row);

        // Try to goto the next index
        if (GetSelectedQuickEdit() is { } currQuickEdit) {
            SelectQuickEditIndex((currQuickEdit.Index + 1).Mod(quickEdits.Length));
            return;
        }

        // We aren't inside a quick-edit and don't have enough info to goto the next index
        // Therefore just go to the next selection linearly, ignoring the index
        var quickEdit = quickEdits
            .FirstOrDefault(anchor => anchor.Row == Document.Caret.Row && anchor.MinCol > Document.Caret.Col ||
                                      anchor.Row > Document.Caret.Row);

        if (quickEdit == null) {
            // Wrap to first one
            SelectQuickEditIndex(0);
            return;
        }

        if (Document.Caret.Row != quickEdit.Row) {
            OnCaretMoved(new CaretPosition(quickEdit.Row), Document.Caret);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = DesiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    private void SelectPrevQuickEdit() {
        var quickEdits = GetQuickEdits().ToArray();
        // Sort linearly inside the document
        Array.Sort(quickEdits, (a, b) => a.Row == b.Row
            ? a.MinCol - b.MinCol
            : a.Row - b.Row);

        // Try to goto the prev index
        if (GetSelectedQuickEdit() is { } currQuickEdit) {
            SelectQuickEditIndex((currQuickEdit.Index - 1).Mod(quickEdits.Length));
            return;
        }

        // We aren't inside a quick-edit and don't have enough info to goto the prev index
        // Therefore just go to the prev selection linearly, ignoring the index
        var quickEdit = quickEdits
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(anchor => anchor.Row == Document.Caret.Row && anchor.MinCol < Document.Caret.Col ||
                                      anchor.Row < Document.Caret.Row);

        if (quickEdit == null) {
            // Wrap to last one
            SelectQuickEditIndex(quickEdits.Length - 1);
            return;
        }

        if (Document.Caret.Row != quickEdit.Row) {
            OnCaretMoved(new CaretPosition(quickEdit.Row), Document.Caret);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = DesiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    private void SelectQuickEditIndex(int index) {
        var quickEdit = Document.FindFirstAnchor(anchor => anchor.UserData is QuickEditAnchorData idx && idx.Index == index);
        if (quickEdit == null) {
            ClearQuickEdits();
            return;
        }

        SelectQuickEdit(quickEdit);
    }
    private void SelectQuickEdit(Anchor quickEdit) {
        if (Document.Caret.Row != quickEdit.Row) {
            OnCaretMoved(new CaretPosition(quickEdit.Row), Document.Caret);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = DesiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    /// Returns the quick-edit which is currently under the caret
    public QuickEditAnchorData? GetSelectedQuickEdit() =>
        GetQuickEdits().FirstOrDefault(anchor => anchor.IsPositionInside(Document.Caret))?.UserData as QuickEditAnchorData?;

    public IEnumerable<Anchor> GetQuickEdits() => Document.FindAnchors(anchor => anchor.UserData is QuickEditAnchorData);
    public void ClearQuickEdits() => Document.RemoveAnchorsIf(anchor => anchor.UserData is QuickEditAnchorData);

    /// Inserts a new quick-edit text at the current row
    protected void InsertQuickEdit(string insert) {
        if (QuickEdit.Parse(insert) is not { } quickEdit) {
            return;
        }

        using var __ = Document.Update();

        var oldCaret = Document.Caret;

        if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
            // Create a new empty line for the quick-edit to use
            CollapseSelection();

            if (Settings.Instance.InsertDirection == InsertDirection.Above) {
                Document.InsertLineAbove(string.Empty);
                Document.Caret.Row--;
                oldCaret.Row++;
            } else if (Settings.Instance.InsertDirection == InsertDirection.Below) {
                Document.InsertLineBelow(string.Empty);
                Document.Caret.Row++;
            }
        }

        int row = Document.Caret.Row;
        Document.ReplaceLine(row, quickEdit.ActualText);
        OnCaretMoved(oldCaret, Document.Caret);

        if (quickEdit.Selections.Length > 0) {
            for (int i = 0; i < quickEdit.Selections.Length; i++) {
                var selection = quickEdit.Selections[i];
                var defaultText = quickEdit.ActualText.SplitDocumentLines()[selection.Min.Row][selection.Min.Col..selection.Max.Col];

                // Quick-edit selections are relative, not absolute
                Document.AddAnchor(new Anchor {
                    Row = selection.Min.Row + row,
                    MinCol = selection.Min.Col, MaxCol = selection.Max.Col,
                    UserData = new QuickEditAnchorData { Index = i, DefaultText = defaultText },
                    OnRemoved = ClearQuickEdits,
                });
            }
            SelectQuickEditIndex(0);
        } else if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
            int newLines = quickEdit.ActualText.Count(c => c == Document.NewLine);
            Document.Caret.Row = row + newLines;
            Document.Caret.Col = DesiredVisualCol = Document.Lines[Document.Caret.Row].Length;
        } else {
            Document.Caret = oldCaret;
        }
    }

    #endregion
}
