using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CelesteStudio.Dialog;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;
using WrapLine = (string Line, int Index);
using WrapEntry = (int StartOffset, (string Line, int Index)[] Lines);

namespace CelesteStudio.Editing;

public sealed class Editor : Drawable {
    private Document document;
    public Document Document {
        get => document;
        set {
            document = value;
            
            // Jump to end when file only 10 lines, else the start
            document.Caret = document.Lines.Count <= 10 
                ? new CaretPosition(document.Lines.Count - 1, document.Lines[^1].Length) 
                : new CaretPosition(0, 0);
            
            // Try to reparse into action lines on change
            document.TextChanged += (_, min, max) => {
                ConvertToActionLines(min, max);
                Recalc();
            };
            
            Recalc();
        }
    }
    
    private readonly Scrollable scrollable;
    private Point scrollablePosition;
    
    private Font Font => FontManager.EditorFontRegular;
    private SyntaxHighlighter highlighter;
    private const float LineNumberPadding = 5.0f;
    
    private readonly AutoCompleteMenu autoCompleteMenu;
    private readonly List<AutoCompleteMenu.Entry> commandEntries = [];
    
    // Quick-edits are anchors to switch through with tab and edit
    // Used by auto-complete snippets
    private int quickEditIndex;
    
    // Offset from the left accounting for line numbers
    private float textOffsetX;
    
    // When editing a long line and moving to a short line, "remember" the column on the long line, unless the caret has been moved. 
    private int desiredVisualCol;
    
    private readonly Dictionary<int, WrapEntry> commentLineWraps = new();
    // Wrapping causes the internal vs. visual rows to change
    private int[] visualRows = [];
    
    private static readonly Regex UncommentedBreakpointRegex = new(@"^\s*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex CommentedBreakpointRegex = new(@"^\s*#+\*\*\*", RegexOptions.Compiled);
    private static readonly Regex AllBreakpointRegex = new(@"^\s*#*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new(@"^\s*#+\s*(\d+:)?\d{1,2}:\d{2}\.\d{3}\(\d+\)", RegexOptions.Compiled);
    
    public Editor(Document document, Scrollable scrollable) {
        this.document = document;
        this.scrollable = scrollable;
        
        // Reflect setting changes
        Settings.Changed += Recalc;
        
        highlighter = new(FontManager.EditorFontRegular, FontManager.EditorFontBold, FontManager.EditorFontItalic, FontManager.EditorFontBoldItalic);
        Settings.FontChanged += () => {
            highlighter = new(FontManager.EditorFontRegular, FontManager.EditorFontBold, FontManager.EditorFontItalic, FontManager.EditorFontBoldItalic);
            Recalc();
        };
        
        autoCompleteMenu = new();
        foreach (var command in CommandInfo.AllCommands) {
            if (command == null)
                continue;
            
            var quickEdit = ParseQuickEdit(command.Value.Insert);
            
            commandEntries.Add(new AutoCompleteMenu.Entry {
                DisplayText = command.Value.Name,
                OnUse = () => {
                    Document.ReplaceLine(Document.Caret.Row, quickEdit.ActualText);
                    Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
                    
                    ClearQuickEdits();
                    for (int i = 0; i < quickEdit.Selections.Length; i++) {
                        Selection selection = quickEdit.Selections[i];
                        
                        var defaultText = quickEdit.ActualText.SplitDocumentLines()[selection.Min.Row][selection.Min.Col..selection.Max.Col];
                        
                        // Quick-edit selections are relative, not absolute
                        Document.AddAnchor(new Anchor {
                            Row = selection.Min.Row + Document.Caret.Row,
                            MinCol = selection.Min.Col, MaxCol = selection.Max.Col,
                            UserData = new QuickEditData { Index = i, DefaultText = defaultText },
                        });
                    }
                    SelectQuickEdit(0);
                    
                    if (command.Value.AutoCompleteEntires.Length != 0) {
                        // Keep open for argument
                        UpdateAutoComplete();
                    } else {
                        autoCompleteMenu.Visible = false;                    
                    }
                },
            });
        }
        
        BackgroundColor = Settings.Instance.Theme.Background;
        Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.Background;
        
        CanFocus = true;
        Cursor = Cursors.IBeam;
        
        // Need to redraw the line numbers when scrolling horizontally
        scrollable.Scroll += (_, e) => {
            scrollablePosition = e.ScrollPosition;
            Invalidate();
        };
        // Update wrapped lines
        scrollable.SizeChanged += (_, _) => Recalc();

        Studio.CommunicationWrapper.Server.StateUpdated += (prevState, state) => {
            if (state.CurrentLine != -1 && prevState.CurrentLine != state.CurrentLine) {
                Document.Caret.Row = state.CurrentLine;
                Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                Document.Caret = ClampCaret(Document.Caret, wrapLine: false);
                
                // Need to redraw the current state
                Application.Instance.InvokeAsync(() => {
                    ScrollCaretIntoView(center: true);
                    Invalidate();
                });
            }
        };
        
        var commandsMenu = new SubMenuItem { Text = "Insert Other Command" };        
        foreach (var command in CommandInfo.AllCommands) {
            if (command == null) {
                commandsMenu.Items.Add(new SeparatorMenuItem());
            } else {
                commandsMenu.Items.Add(CreateCommandInsert(command.Value));
            }
        }
        
        ContextMenu = new ContextMenu {
            Items = {
                MenuUtils.CreateAction("Cut", Application.Instance.CommonModifier | Keys.X, OnCut),
                MenuUtils.CreateAction("Copy", Application.Instance.CommonModifier | Keys.C, OnCopy),
                MenuUtils.CreateAction("Paste", Application.Instance.CommonModifier | Keys.V, OnPaste),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Undo", Application.Instance.CommonModifier | Keys.Z, OnUndo),
                MenuUtils.CreateAction("Redo", Application.Instance.CommonModifier | Keys.Z | Keys.Shift, OnRedo),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Select All", Application.Instance.CommonModifier | Keys.A, OnSelectAll),
                MenuUtils.CreateAction("Select Block", Application.Instance.CommonModifier | Keys.W, OnSelectBlock),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Find...", Application.Instance.CommonModifier | Keys.F, OnFind),
                MenuUtils.CreateAction("Go To...", Application.Instance.CommonModifier | Keys.G, OnGoTo),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Delete Selected Lines", Application.Instance.CommonModifier | Keys.Y, OnDeleteSelectedLines),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Insert/Remove Breakpoint", Application.Instance.CommonModifier | Keys.Period, () => InsertOrRemoveText(UncommentedBreakpointRegex, "***")),
                MenuUtils.CreateAction("Insert/Remove Savestate Breakpoint", Application.Instance.CommonModifier | Keys.Shift | Keys.Period, () => InsertOrRemoveText(UncommentedBreakpointRegex, "***S")),
                MenuUtils.CreateAction("Remove All Uncommented Breakpoints", Application.Instance.CommonModifier | Keys.P, () => RemoveLinesMatching(UncommentedBreakpointRegex)),
                MenuUtils.CreateAction("Remove All Breakpoints", Application.Instance.CommonModifier | Keys.Shift | Keys.P, () => RemoveLinesMatching(AllBreakpointRegex)),
                MenuUtils.CreateAction("Comment/Uncomment All Breakpoints", Application.Instance.CommonModifier | Keys.Alt | Keys.P, OnToggleCommentBreakpoints),
                MenuUtils.CreateAction("Comment/Uncomment Inputs", Application.Instance.CommonModifier | Keys.K, OnToggleCommentInputs),
                MenuUtils.CreateAction("Comment/Uncomment Text", Application.Instance.CommonModifier | Keys.K | Keys.Shift, OnToggleCommentText),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Insert Room Name", Application.Instance.CommonModifier | Keys.R, OnInsertRoomName),
                MenuUtils.CreateAction("Insert Current In-Game Time", Application.Instance.CommonModifier | Keys.T, OnInsertTime),
                MenuUtils.CreateAction("Remove All Timestamps", Application.Instance.CommonModifier | Keys.Shift | Keys.T, () => RemoveLinesMatching(TimestampRegex)),
                MenuUtils.CreateAction("Insert Mod Info", Keys.None, OnInsertModInfo),
                MenuUtils.CreateAction("Insert Console Load Command", Application.Instance.CommonModifier | Keys.Shift | Keys.R, OnInsertConsoleLoadCommand),
                MenuUtils.CreateAction("Insert Simple Console Load Command", Application.Instance.CommonModifier | Keys.Alt | Keys.R, OnInsertSimpleConsoleLoadCommand),
                commandsMenu,
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Swap Selected L and R", Keys.None, () => SwapSelectedActions(Actions.Left, Actions.Right)),
                MenuUtils.CreateAction("Swap Selected J and K", Keys.None, () => SwapSelectedActions(Actions.Jump, Actions.Jump2)),
                MenuUtils.CreateAction("Swap Selected X and C", Keys.None, () => SwapSelectedActions(Actions.Dash, Actions.Dash2)),
                MenuUtils.CreateAction("Combine Consecutive Same Inputs", Application.Instance.CommonModifier | Keys.L, () => CombineInputs(sameActions: true)),
                MenuUtils.CreateAction("Force Combine Input Frames", Application.Instance.CommonModifier | Keys.Shift | Keys.L, () => CombineInputs(sameActions: false)),
                // TODO: Is this feature even unused?
                // MenuUtils.CreateAction("Convert Dash to Demo Dash"),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Open Read File / Go to Play Line"),
            }
        };
        
        Recalc();
        
        MenuItem CreateCommandInsert(CommandInfo info) {
            var cmd = new Command { Shortcut = Keys.None };
            cmd.Executed += (_, _) => {
                Document.InsertLineAbove(info.Insert);
                // TODO: Support quick-edits here
                Recalc();
            };
            
            return new ButtonMenuItem(cmd) { Text = info.Name, ToolTip = info.Description };
        }
    }
    
    #region General Helper Methods
    
    /// <summary>
    /// Recalculates all values and invalidates the paint.
    /// </summary>
    private void Recalc() {
        // Ensure there is always at least 1 line
        if (Document.Lines.Count == 0)
            Document.InsertNewLine(0, string.Empty);
        
        // Snap caret
        Document.Caret.Row = Math.Clamp(Document.Caret.Row, 0, Document.Lines.Count - 1);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);
        
        textOffsetX = Font.CharWidth() * Document.Lines.Count.Digits() + LineNumberPadding * 3.0f;
        
        // Calculate bounds and apply wrapping
        commentLineWraps.Clear();
        Array.Resize(ref visualRows, Document.Lines.Count);
        
        float width = 0.0f, height = 0.0f;
        
        for (int row = 0, visualRow = 0; row < Document.Lines.Count; row++) {
            string line = Document.Lines[row];
            visualRows[row] = visualRow;

            if (Settings.Instance.WordWrapComments && line.TrimStart().StartsWith("#")) {
                // Wrap comments into multiple lines when hitting the left edge
                var wrappedLines = new List<WrapLine>();

                const int charPadding = 1;
                float charWidth = (scrollable.Width - Studio.BorderRightOffset - Studio.WidthRightOffset) / Font.CharWidth() - 1 - charPadding; // -1 because we overshoot by 1 while iterating
                
                int idx = 0;
                int startOffset = -1;
                while (idx < line.Length) {
                    int subIdx = 0;
                    int startIdx = idx;
                    int endIdx = -1;
                    for (; idx < line.Length; idx++, subIdx++) {
                        char c = line[idx];
                        
                        // Skip first #'s and whitespace
                        if (startOffset == -1) {
                            if (c == '#' || char.IsWhiteSpace(c))
                                continue;
                            startOffset = idx;
                            charWidth -= startOffset;
                        }
                        
                        // End the line if we're beyond the width and have reached whitespace
                        if (char.IsWhiteSpace(c)) {
                            endIdx = idx;
                        }
                        if (idx == line.Length - 1) {
                            endIdx = line.Length;
                        }
                        
                        if (endIdx != -1 && subIdx >= charWidth) {
                            break;
                        }
                    }
                    
                    // The comment only contains #'s and whitespace. Abort wrapping
                    if (endIdx == -1) {
                        wrappedLines = [(line, 0)];
                        break;
                    }
                    
                    if (idx != line.Length) {
                        // Snap index back to line break
                        idx = endIdx + 1;
                    }
                    
                    var subLine = line[startIdx..endIdx];
                    wrappedLines.Add((subLine, startIdx));

                    width = Math.Max(width, Font.MeasureWidth(subLine));
                    height += Font.LineHeight();
                }
                
                commentLineWraps.Add(row, (startOffset, wrappedLines.ToArray()));
                visualRow += wrappedLines.Count;
            } else {
                width = Math.Max(width, Font.MeasureWidth(line));
                height += Font.LineHeight();
                visualRow += 1;
            }
        }
        
        const float paddingRight = 50.0f;
        const float paddingBottom = 100.0f;

        // Apparently you need to set the size from the parent on WPF?
        if (Eto.Platform.Instance.IsWpf)
            scrollable.ScrollSize = new((int)(width + textOffsetX + paddingRight), (int)(height + paddingBottom));
        else
            Size = new((int)(width + textOffsetX + paddingRight), (int)(height + paddingBottom));
        
        Invalidate();
    }
    
    private CaretPosition GetVisualPosition(CaretPosition position) {
        if (!commentLineWraps.TryGetValue(position.Row, out var wrap))
            return new CaretPosition(visualRows[position.Row], position.Col);
        
        // TODO: Maybe don't use LINQ here for performance?
        var (line, lineIdx) = wrap.Lines
            .Select((line, idx) => (line, idx))
            .Reverse()
            .FirstOrDefault(line => line.line.Index <= position.Col);
        
        int xIdent = lineIdx == 0 ? 0 : wrap.StartOffset;
        
        return new CaretPosition(
            visualRows[position.Row] + lineIdx,
            position.Col - line.Index + xIdent);
    }
    private CaretPosition GetActualPosition(CaretPosition position) {
        int row = GetActualRow(position.Row);
        
        int col = position.Col;
        if (commentLineWraps.TryGetValue(row, out var wrap)) {
            int idx = position.Row - visualRows[row];
            if (idx < wrap.Lines.Length) {
                int xIdent = idx == 0 ? 0 : wrap.StartOffset;
                col += wrap.Lines[idx].Index - xIdent;       
            }
        }
        
        return new CaretPosition(row, col);
    }
    
    private int GetActualRow(int visualRow) {
        // There is no good way to find the reverse other than just iterating all lines
        // TODO: Maybe improve this?
        int row = 0;
        for (; row < Document.Lines.Count; row++) {
            if (visualRows[row] > visualRow) {
                // We just overshot it by 1
                return row - 1;
            }
        }
        return row - 1;
    }
    private string GetVisualLine(int visualRow) {
        int row = GetActualRow(visualRow);
        
        if (commentLineWraps.TryGetValue(row, out var wrap)) {
            int idx = visualRow - visualRows[row];
            if (idx == 0) {
                return wrap.Lines[idx].Line;
            } else {
                return $"{new string(' ', wrap.StartOffset)}{wrap.Lines[idx].Line}";
            }            
        }
        
        return Document.Lines[row];
    }
    
    // Matches against command or space or both as a separator
    private static readonly Regex SeparatorRegex = new(@"(?:\s+)|(?:\s*,\s*)", RegexOptions.Compiled);
    
    private void UpdateAutoComplete() {
        var line = Document.Lines[Document.Caret.Row];
        
        // Don't auto-complete on comments or action lines
        if (line.StartsWith('#') || ActionLine.TryParse(line, out _)) {
            autoCompleteMenu.Visible = false;
            return;
        }
        
        autoCompleteMenu.Visible = true;
        
        // Use auto-complete entries for current command

        // Split by the first separator
        var separatorMatch = SeparatorRegex.Match(line);
        var args = line.Split(separatorMatch.Value);
        
        if (args.Length <= 1) {
            autoCompleteMenu.Entries = commandEntries;
            autoCompleteMenu.Filter = line;    
        } else {
            var command = CommandInfo.AllCommands.FirstOrDefault(cmd => cmd?.Name == args[0]);
            var commandArgs = args[1..];
            
            if (command != null && command.Value.AutoCompleteEntires.Length >= commandArgs.Length) {
                int lastArgStart = line.LastIndexOf(args[^1], StringComparison.Ordinal);
                var entries = command.Value.AutoCompleteEntires[commandArgs.Length - 1](commandArgs);
                
                autoCompleteMenu.Entries = entries.Select(entry => new AutoCompleteMenu.Entry {
                    DisplayText = entry,
                    OnUse = () => {
                        var commandLine = Document.Lines[Document.Caret.Row];
                        
                        if (command.Value.AutoCompleteEntires.Length != commandArgs.Length) {
                            // Include separator for next argument
                            Document.ReplaceRangeInLine(Document.Caret.Row, lastArgStart, commandLine.Length, entry + separatorMatch.Value);
                            UpdateAutoComplete();
                        } else {
                            Document.ReplaceRangeInLine(Document.Caret.Row, lastArgStart, commandLine.Length, entry);
                            autoCompleteMenu.Visible = false;
                        }
                        
                        Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
                        Document.Selection.Clear();
                    },
                }).ToList();
            } else {
                autoCompleteMenu.Entries = [];
            }
            
            if (GetSelectedQuickEdit() is { } quickEdit && args[^1] == quickEdit.DefaultText) {
                // Display all entries which quick-edit still contains default
                autoCompleteMenu.Filter = string.Empty;
            } else {
                autoCompleteMenu.Filter = args[^1];
            }
        }
    }
    
    #endregion
    
    protected override void OnKeyDown(KeyEventArgs e) {
        if (autoCompleteMenu.OnKeyDown(e)) {
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
                
                UpdateAutoComplete();

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
                SelectQuickEdit(GetQuickEdits().Count() - 1);
                ClearQuickEdits();
                Document.Caret = Document.Selection.Max;
                Document.Selection.Clear();
                
                e.Handled = true;
                Recalc();
                return;
            }
        }
        
        if (e is { Key: Keys.Space, Control: true}) {
            UpdateAutoComplete();

            e.Handled = true;
            Recalc();
            return;
        }
        
        if (Settings.Instance.SendInputsToCeleste && Studio.CommunicationWrapper.Connected && Studio.CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
            e.Handled = true;
            return;
        }
        
        switch (e.Key) {
            case Keys.Backspace:
                OnDelete(e.Control ? CaretMovementType.WordLeft : CaretMovementType.CharLeft);
                e.Handled = true;
                break;
            case Keys.Delete:
                OnDelete(e.Control ? CaretMovementType.WordRight : CaretMovementType.CharRight);
                e.Handled = true;
                break;
            case Keys.Enter:
                OnEnter();
                e.Handled = true;
                break;
            case Keys.Left:
                MoveCaret(e.Control ? CaretMovementType.WordLeft : CaretMovementType.CharLeft, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(e.Control ? CaretMovementType.WordRight : CaretMovementType.CharRight, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Up:
                MoveCaret(CaretMovementType.LineUp, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Down:
                MoveCaret(CaretMovementType.LineDown, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.PageUp:
                MoveCaret(CaretMovementType.PageUp, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.PageDown:
                MoveCaret(CaretMovementType.PageDown, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(e.Control ? CaretMovementType.DocumentStart : CaretMovementType.LineStart, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.End:
                MoveCaret(e.Control ? CaretMovementType.DocumentEnd : CaretMovementType.LineEnd, updateSelection: e.Shift);
                e.Handled = true;
                break;
            default:
                if (e.Key != Keys.None) {
                    // Search through context menu for hotkeys
                    foreach (var item in ContextMenu.Items) {
                        if (item.Shortcut != e.KeyData) {
                            continue;
                        }
                        
                        item.PerformClick();
                        e.Handled = true;
                        break;
                    }
                    
                    // Try to paste snippets
                    foreach (var snippet in Settings.Snippets) {
                        if (!snippet.Enabled || snippet.Shortcut != e.KeyData) {
                            continue;
                        }
                        
                        if (Document.Lines[Document.Caret.Row].Trim().Length == 0) {
                            Document.ReplaceLine(Document.Caret.Row, snippet.Text);
                        } else {
                            Document.InsertLineBelow(snippet.Text);
                            Document.Caret.Row++;
                        }
                        
                        Document.Caret.Col = desiredVisualCol = snippet.Text.Length;
                    }
                }
                
                base.OnKeyDown(e);
                break;
        }
        
        Recalc();
    }
    
    protected override void OnKeyUp(KeyEventArgs e) {
        if (Settings.Instance.SendInputsToCeleste && Studio.CommunicationWrapper.Connected && Studio.CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: true)) {
            e.Handled = true;
            return;
        }
        
        base.OnKeyUp(e);
    }
    
    #region Quick Edit
    
    private record struct QuickEdit { public required string ActualText; public Selection[] Selections; }
    private record struct QuickEditData { public required int Index; public required string DefaultText; }
    
    private readonly Dictionary<string, QuickEdit> quickEditCache = new();  
    private QuickEdit ParseQuickEdit(string text) {
        if (quickEditCache.TryGetValue(text, out var quickEdit)) {
            return quickEdit;
        }
        
        var actualText = new StringBuilder(capacity: text.Length);
        var quickEditSpots = new Dictionary<int, Selection>();
        
        int row = 0;
        int col = 0;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == Document.NewLine) {
                actualText.Append(c);
                row++;
                col = 0;
                continue;
            }
            if (c != '[') {
                actualText.Append(c);
                col++;
                continue;
            }
            
            int endIdx = text.IndexOf(']', i);
            var quickEditText = text[(i + 1)..endIdx];
            
            int delimIdx = quickEditText.IndexOf(';');
            if (delimIdx < 0) {
                int idx = int.Parse(quickEditText);
                quickEditSpots[idx] = new Selection { Start = new CaretPosition(row, col), End = new CaretPosition(row, col) };
            } else {
                int idx = int.Parse(quickEditText[..delimIdx]);
                var editableText = quickEditText[(delimIdx + 1)..];
                quickEditSpots[idx] = new Selection { Start = new CaretPosition(row, col), End = new CaretPosition(row, col + editableText.Length) };
                actualText.Append(editableText);
                col += editableText.Length;
            }
            
            i = endIdx;
        }
        
        // Convert to actual array
        var quickEditSelections = new Selection[quickEditSpots.Count];
        for (int i = 0; i < quickEditSelections.Length; i++) {
            quickEditSelections[i] = quickEditSpots[i];
        }
        
        quickEdit = new QuickEdit { ActualText = actualText.ToString(), Selections = quickEditSelections };
        quickEditCache[text] = quickEdit;
        return quickEdit;
    }
    
    private void SelectNextQuickEdit() => SelectQuickEdit((quickEditIndex + 1).Mod(GetQuickEdits().Count()));
    private void SelectPrevQuickEdit() => SelectQuickEdit((quickEditIndex - 1).Mod(GetQuickEdits().Count()));
    private void SelectQuickEdit(int index) {
        quickEditIndex = index;

        var quickEdit = Document.FindFirstAnchor(anchor => anchor.UserData is QuickEditData idx && idx.Index == index);
        if (quickEdit == null) {
            ClearQuickEdits();
            return;
        }
        
        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = desiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }
    
    private QuickEditData? GetSelectedQuickEdit() => GetQuickEdits().FirstOrDefault(anchor => anchor.IsPositionInside(Document.Caret))?.UserData as QuickEditData?;
    private IEnumerable<Anchor> GetQuickEdits() => Document.FindAnchors(anchor => anchor.UserData is QuickEditData);
    private void ClearQuickEdits() => Document.RemoveAnchorsIf(anchor => anchor.UserData is QuickEditData);
    
    #endregion
    
    #region Editing Actions

    private void ConvertToActionLines(CaretPosition start, CaretPosition end) {
        // Convert to action lines if possible
        int minRow = Math.Min(start.Row, end.Row);
        int maxRow = Math.Max(start.Row, end.Row);
        
        for (int row = minRow; row <= Math.Min(maxRow, Document.Lines.Count - 1); row++) {
            if (ActionLine.TryParse(Document.Lines[row], out var actionLine)) {
                Document.ReplaceLine(row, actionLine.ToString(), raiseEvents: false);
                
                if (Document.Caret.Row == row)
                    Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
            }
        }
    }
    
    protected override void OnTextInput(TextInputEventArgs e) {
        if (!Document.Selection.Empty) {
            Document.RemoveSelectedText();
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }
        
        var line = Document.Lines[Document.Caret.Row];
        
        char typedCharacter = char.ToUpper(e.Text[0]);
        int leadingSpaces = line.Length - line.TrimStart().Length;
        
        // If it's an action line, handle it ourselves
        if (ActionLine.TryParse(line, out var actionLine) && e.Text.Length == 1) {
            ClearQuickEdits();
            
            // Handle custom bindings
            int customBindStart = GetColumnOfAction(actionLine, Actions.PressedKey);
            int customBindEnd = customBindStart + actionLine.CustomBindings.Count;
            if (customBindStart != -1 && Document.Caret.Col >= customBindStart && Document.Caret.Col <= customBindEnd && typedCharacter is >= 'A' and <= 'Z') {
                bool alreadyExists = !actionLine.CustomBindings.Add(typedCharacter);
                if (alreadyExists) {
                    actionLine.CustomBindings.Remove(typedCharacter);
                    Document.Caret.Col = desiredVisualCol = customBindEnd - 1;
                } else {
                    Document.Caret.Col = desiredVisualCol = customBindEnd + 1;
                }

                goto FinishEdit; // Skip regular logic
            }

            var typedAction = typedCharacter.ActionForChar();

            // Handle feather inputs
            int featherStart = GetColumnOfAction(actionLine, Actions.Feather);
            if (featherStart != -1 && Document.Caret.Col > featherStart && (typedCharacter is '.' or ',' or (>= '0' and <= '9'))) {
                line = line.Insert(Document.Caret.Col, typedCharacter.ToString());
                if (ActionLine.TryParse(line, out var newActionLine, ignoreInvalidFloats: false)) {
                    actionLine = newActionLine;
                    Document.Caret.Col++;
                }
            }
            // Handle dash-only/move-only/custom bindings
            else if (typedAction is Actions.DashOnly or Actions.MoveOnly or Actions.PressedKey) {
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction, Settings.Instance.AutoRemoveMutuallyExclusiveActions);
                Document.Caret.Col = desiredVisualCol = GetColumnOfAction(actionLine, typedAction);
            }
            // Handle regular inputs
            else if (typedAction != Actions.None) {
                int dashOnlyStart = GetColumnOfAction(actionLine, Actions.DashOnly);
                int dashOnlyEnd = dashOnlyStart + actionLine.Actions.GetDashOnly().Count();
                if (dashOnlyStart != -1 && Document.Caret.Col >= dashOnlyStart && Document.Caret.Col <= dashOnlyEnd)
                    typedAction = typedAction.ToDashOnlyActions();

                int moveOnlyStart = GetColumnOfAction(actionLine, Actions.MoveOnly);
                int moveOnlyEnd = moveOnlyStart + actionLine.Actions.GetMoveOnly().Count();
                if (moveOnlyStart != -1 && Document.Caret.Col >= moveOnlyStart && Document.Caret.Col <= moveOnlyEnd)
                    typedAction = typedAction.ToMoveOnlyActions();

                // Toggle it
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction, Settings.Instance.AutoRemoveMutuallyExclusiveActions);

                // Warp the cursor after the number
                if (typedAction == Actions.Feather && actionLine.Actions.HasFlag(Actions.Feather)) {
                    Document.Caret.Col = desiredVisualCol = GetColumnOfAction(actionLine, Actions.Feather) + 1;
                } else if (typedAction == Actions.Feather && !actionLine.Actions.HasFlag(Actions.Feather)) {
                    actionLine.FeatherAngle = null;
                    actionLine.FeatherMagnitude = null;
                    Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                } else if (typedAction is Actions.LeftDashOnly or Actions.RightDashOnly or Actions.UpDashOnly or Actions.DownDashOnly) {
                    Document.Caret.Col = desiredVisualCol = GetColumnOfAction(actionLine, Actions.DashOnly) + actionLine.Actions.GetDashOnly().Count();
                } else if (typedAction is Actions.LeftMoveOnly or Actions.RightMoveOnly or Actions.UpMoveOnly or Actions.DownMoveOnly) {
                    Document.Caret.Col = desiredVisualCol = GetColumnOfAction(actionLine, Actions.MoveOnly) + actionLine.Actions.GetMoveOnly().Count();
                } else {
                    Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                }
            }
            // If the key we entered is a number
            else if (typedCharacter is >= '0' and <= '9') {
                int cursorPosition = Document.Caret.Col - leadingSpaces;

                // Entering a zero at the start should do nothing but format
                if (cursorPosition == 0 && typedCharacter == '0') {
                    Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits - actionLine.Frames.Digits();
                }
                // If we have a 0, just force the new number
                else if (actionLine.Frames == 0) {
                    actionLine.Frames = int.Parse(typedCharacter.ToString());
                    Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                } else {
                    // Jam the number into the current position
                    string leftOfCursor = line[..(Document.Caret.Col)];
                    string rightOfCursor = line[(Document.Caret.Col)..];
                    line = $"{leftOfCursor}{typedCharacter}{rightOfCursor}";

                    // Reparse
                    ActionLine.TryParse(line, out actionLine);

                    // Cap at max frames
                    if (actionLine.Frames > ActionLine.MaxFrames) {
                        actionLine.Frames = ActionLine.MaxFrames;
                        Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                    } else {
                        Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits - actionLine.Frames.Digits() + cursorPosition + 1;
                    }
                }
            }

            FinishEdit:
            Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());
        }
        // Just write it as text
        else {
            // Encourage having a space before comments (so they aren't labels)
            // However still allow easily inserting multiple #'s
            if (e.Text == "#") {
                var currLine = Document.Lines[Document.Caret.Row];
                bool onlyComment = currLine.All(c => char.IsWhiteSpace(c) || c == '#');

                if (onlyComment) {
                    var newLine = $"{currLine.TrimEnd()}# ";
                    Document.ReplaceLine(Document.Caret.Row, newLine);
                    Document.Caret.Col = desiredVisualCol = newLine.Length;
                } else {
                    Document.Insert("#");    
                }
            } else {
                Document.Insert(e.Text);
            }
            
            // But turn it into an action line if possible
            if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out var newActionLine)) {
                ClearQuickEdits();
                
                Document.ReplaceLine(Document.Caret.Row, newActionLine.ToString());
                Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
            }
        }
        
        UpdateAutoComplete();
        ScrollCaretIntoView();
        Recalc();
    }

    private void OnDelete(CaretMovementType direction) {
        if (!Document.Selection.Empty) {
            var oldCaret = Document.Caret;
            
            Document.RemoveSelectedText();
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
            
            ConvertToActionLines(oldCaret, Document.Caret);
            ScrollCaretIntoView();
            return;
        }
        
        var caret = Document.Caret;
        var line = Document.Lines[Document.Caret.Row];
        
        if (ActionLine.TryParse(line, out var actionLine)) {
            caret.Col = SnapColumnToActionLine(actionLine, caret.Col);
            
            var lineStartPosition = new CaretPosition(caret.Row, 0);
            
            // Handle frame count
            if (caret.Col == ActionLine.MaxFramesDigits && direction is CaretMovementType.WordLeft or CaretMovementType.CharLeft ||
                caret.Col < ActionLine.MaxFramesDigits) {
                int leadingSpaces = line.Length - line.TrimStart().Length;
                int cursorIndex = Math.Clamp(caret.Col - leadingSpaces, 0, actionLine.Frames.Digits());
                
                string framesString = actionLine.Frames.ToString();
                string leftOfCursor = framesString[..cursorIndex];
                string rightOfCursor = framesString[cursorIndex..];
                
                if (actionLine.Frames == 0) {
                    line = string.Empty;
                } else if (leftOfCursor.Length == 0 && direction is CaretMovementType.WordLeft or CaretMovementType.CharLeft ||
                           rightOfCursor.Length == 0 && direction is CaretMovementType.WordRight or CaretMovementType.CharRight) {
                    line = string.Empty;
                } else {
                    string newFramesString = string.Empty;
                    if (direction == CaretMovementType.WordLeft) {
                        newFramesString = rightOfCursor;
                        cursorIndex = 0;
                    } else if (direction == CaretMovementType.WordRight) {
                        newFramesString = leftOfCursor;
                    } else if (direction == CaretMovementType.CharLeft) {
                        newFramesString = $"{leftOfCursor[..^1]}{rightOfCursor}";
                        cursorIndex--;
                    } else if (direction == CaretMovementType.CharRight) {
                        newFramesString = $"{leftOfCursor}{rightOfCursor[1..]}";
                    }
                    
                    actionLine.Frames = Math.Clamp(int.TryParse(newFramesString, out int value) ? value : 0, 0, ActionLine.MaxFrames);
                    line = actionLine.ToString();
                    caret.Col = actionLine.Frames == 0
                        ? ActionLine.MaxFramesDigits
                        : ActionLine.MaxFramesDigits - actionLine.Frames.Digits() + cursorIndex;
                }
                
                goto FinishDeletion; // Skip regular deletion behaviour
            }
            
            // Handle feather angle/magnitude
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            if (featherColumn != -1 && caret.Col >= featherColumn) {
                int angleMagnitudeCommaColumn = featherColumn + 2;
                while (angleMagnitudeCommaColumn <= line.Length + 1 && line[angleMagnitudeCommaColumn - 2] != ActionLine.Delimiter) {
                    angleMagnitudeCommaColumn++;
                }
                
                if (caret.Col == featherColumn + 1 && direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft) {
                    var actions = GetActionsFromColumn(actionLine, caret.Col - 1, direction);
                    actionLine.Actions &= ~actions;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                } else if (caret.Col == featherColumn && direction is CaretMovementType.CharRight or CaretMovementType.WordRight ||
                           caret.Col == angleMagnitudeCommaColumn && direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft) {
                    actionLine.FeatherAngle = actionLine.FeatherMagnitude;
                    actionLine.FeatherMagnitude = null;
                    caret.Col = featherColumn;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                } else if (caret.Col == angleMagnitudeCommaColumn - 1 &&
                           direction is CaretMovementType.CharRight or CaretMovementType.WordRight) {
                    actionLine.FeatherMagnitude = null;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                }
            }
            
            int newColumn = direction switch {
                CaretMovementType.CharLeft => GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < caret.Col, caret.Col),
                CaretMovementType.WordLeft => GetHardSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < caret.Col, caret.Col),
                CaretMovementType.CharRight => GetSoftSnapColumns(actionLine).FirstOrDefault(c => c > caret.Col, caret.Col),
                CaretMovementType.WordRight => GetHardSnapColumns(actionLine).FirstOrDefault(c => c > caret.Col, caret.Col),
                _ => caret.Col,
            };
            
            line = line.Remove(Math.Min(newColumn, caret.Col), Math.Abs(newColumn - caret.Col));
            caret.Col = Math.Min(newColumn, caret.Col);
            
            FinishDeletion:
            if (ActionLine.TryParse(line, out var newActionLine)) {
                line = newActionLine.ToString();
            } else if (string.IsNullOrWhiteSpace(line)) {
                line = string.Empty;
                caret = lineStartPosition;
            }
            
            Document.ReplaceLine(caret.Row, line);
            Document.Caret = ClampCaret(caret);
        } else {
            var newCaret = GetNewTextCaretPosition(direction);
            
            if (caret.Row == newCaret.Row) {
                Document.RemoveRangeInLine(caret.Row, caret.Col, newCaret.Col);
                newCaret.Col = Math.Min(newCaret.Col, caret.Col);
                
                UpdateAutoComplete();
            } else {
                var min = newCaret < caret ? newCaret : caret;
                var max = newCaret < caret ? caret : newCaret;
                
                Document.RemoveRange(min, max);
                newCaret = min;
                
                autoCompleteMenu.Visible = false;
            }
            
            Document.Caret = ClampCaret(newCaret);
        }
    }
    
    private void OnEnter() {
        var line = Document.Lines[Document.Caret.Row];
        
        if (ActionLine.TryParse(line, out _)) {
            // Don't split frame count and action
            Document.InsertLineBelow(string.Empty);
            Document.Caret.Row++;
            Document.Caret.Col = desiredVisualCol = 0;
        } else {
            Document.Insert(Document.NewLine.ToString());
        }
        
        ScrollCaretIntoView();
    }
    
    private void OnUndo() {
        var oldCaret = Document.Caret;
        Document.Undo();
        
        ConvertToActionLines(oldCaret, Document.Caret);
        ScrollCaretIntoView();
    }
    
    private void OnRedo() {
        var oldCaret = Document.Caret;
        Document.Redo();
        
        ConvertToActionLines(oldCaret, Document.Caret);
        ScrollCaretIntoView();
    }
    
    private void OnCut() {
        if (Document.Selection.Empty)
            return;
        
        OnCopy();
        OnDelete(CaretMovementType.None);
    }
    
    private void OnCopy() {
        if (Document.Selection.Empty)
            return;
        
        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = Document.GetSelectedText();
    }
    
    private void OnPaste() {
        if (!Clipboard.Instance.ContainsText)
            return;
        
        if (!Document.Selection.Empty) {
            Document.RemoveSelectedText();
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }
        
        var oldCaret = Document.Caret;
        Document.Insert(Clipboard.Instance.Text);
        
        ConvertToActionLines(oldCaret, Document.Caret);
        ScrollCaretIntoView();
    }
    
    private void OnSelectAll() {
        Document.Selection.Start = new CaretPosition(0, 0);
        Document.Selection.End = new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length);
    }
    
    private void OnSelectBlock() {
        // Search first empty line above/below caret
        int above = Document.Caret.Row;
        while (above > 0 && !string.IsNullOrWhiteSpace(Document.Lines[above]))
            above--;

        int below = Document.Caret.Row;
        while (below < Document.Lines.Count - 1 && !string.IsNullOrWhiteSpace(Document.Lines[below]))
            below++;
        
        Document.Selection.Start = new CaretPosition(above, 0);
        Document.Selection.End = new CaretPosition(below, Document.Lines[below].Length);
    }
    
    private void OnFind() {
        FindDialog.Show(this);
    }
    
    private void OnGoTo() {
        Document.Caret.Row = GoToDialog.Show(Document);
        Document.Caret = ClampCaret(Document.Caret, wrapLine: false);
        Document.Selection.Clear();
        
        if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out var actionLine))
            Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
        
        ScrollCaretIntoView();
    }
    
    private void OnDeleteSelectedLines() {
        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }
        
        Document.RemoveLines(minRow, maxRow);
        Document.Selection.Clear();
        Document.Caret.Row = minRow;
        
        ScrollCaretIntoView();
    }
    
    private void OnToggleCommentBreakpoints() {
        Document.Selection.Normalize();
        
        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = 0;
            maxRow = Document.Lines.Count - 1;
        }
        
        Document.PushUndoState();
        for (int row = minRow; row <= maxRow; row++) {
            var line = Document.Lines[row];
            if (CommentedBreakpointRegex.IsMatch(line)) {
                int hashIdx = line.IndexOf('#');
                Document.ReplaceLine(row, line.Remove(hashIdx, 1), raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else if (UncommentedBreakpointRegex.IsMatch(line)) {
                Document.ReplaceLine(row, $"#{line}", raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }
        Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
        
        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length); 
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length); 
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length); 
    }
    
    private void OnToggleCommentInputs() {
        Document.Selection.Normalize();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }
        
        Document.PushUndoState();
        for (int row = minRow; row <= maxRow; row++) {
            var line = Document.Lines[row];

            if (line.TrimStart().StartsWith('#')) {
                int hashIdx = line.IndexOf('#');
                Document.ReplaceLine(row, line.Remove(hashIdx, 1), raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else {
                Document.ReplaceLine(row, $"#{line}", raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }
        Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
        
        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length); 
    }
    
    private void OnToggleCommentText() {
        Document.Selection.Normalize();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }
        
        // Only remove # when all lines start with it. Otherwise, add another
        bool allCommented = true;
        for (int row = minRow; row <= maxRow; row++) {
            var line = Document.Lines[row];

            if (!line.TrimStart().StartsWith('#')) {
                allCommented = false;
                break;
            }
        }
        
        Document.PushUndoState();
        for (int row = minRow; row <= maxRow; row++) {
            var line = Document.Lines[row];

            if (allCommented) {
                int hashIdx = line.IndexOf('#');
                Document.ReplaceLine(row, line.Remove(hashIdx, 1), raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else {
                Document.ReplaceLine(row, $"#{line}", raiseEvents: false);
                
                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }
        Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
        
        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length); 
    }
    
    private void OnInsertRoomName() => Document.InsertLineAbove($"#lvl_{Studio.CommunicationWrapper.LevelName}");

    private void OnInsertTime() => Document.InsertLineAbove($"#{Studio.CommunicationWrapper.ChapterTime}");
    
    private void OnInsertModInfo() {
        if (Studio.CommunicationWrapper.Server.GetDataFromGame(GameDataType.ModInfo) is { } modInfo)
            Document.InsertLineAbove(modInfo);
    }
    
    private void OnInsertConsoleLoadCommand() {
        if (Studio.CommunicationWrapper.Server.GetDataFromGame(GameDataType.ConsoleCommand, false) is { } command)
            Document.InsertLineAbove(command);
    }
    
    private void OnInsertSimpleConsoleLoadCommand() {
        if (Studio.CommunicationWrapper.Server.GetDataFromGame(GameDataType.ConsoleCommand, true) is { } command)
            Document.InsertLineAbove(command);
    }
    
    private void InsertOrRemoveText(Regex regex, string text) {
        // Check current line
        if (regex.IsMatch(Document.Lines[Document.Caret.Row])) {
            Document.RemoveLine(Document.Caret.Row);
        }
        // Check line above as well
        else if (Document.Caret.Row > 0 && regex.IsMatch(Document.Lines[Document.Caret.Row - 1])) {
            Document.RemoveLine(Document.Caret.Row - 1);
        }
        // Otherwise insert new breakpoint
        else {
            Document.InsertLineAbove(text);
        }
        
        ScrollCaretIntoView();
    }
    
    private void RemoveLinesMatching(Regex regex) {
        bool changed = false;
        
        for (int row = Document.Lines.Count - 1; row >= 0; row--) {
            if (!regex.IsMatch(Document.Lines[row]))
                continue;
            
            if (!changed)
                Document.PushUndoState();
            changed = true;
            
            Document.RemoveLine(row, raiseEvents: false);
        }
        
        if (changed)
            Document.OnTextChanged(new CaretPosition(0, 0), new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length));
    }
    
    private void SwapSelectedActions(Actions a, Actions b) {
        if (Document.Selection.Empty)
            return;
        
        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        
        bool changed = false;
        
        for (int row = minRow; row <= maxRow; row++) {
            if (!ActionLine.TryParse(Document.Lines[row], out var actionLine))
                continue;
            
            if (actionLine.Actions.HasFlag(a) && actionLine.Actions.HasFlag(b))
                continue; // Nothing to do
            
            if (actionLine.Actions.HasFlag(a))
                actionLine.Actions = actionLine.Actions & ~a | b;
            else if (actionLine.Actions.HasFlag(b))
                actionLine.Actions = actionLine.Actions & ~b | a;
            
            if (!changed)
                Document.PushUndoState();
            changed = true;
            
            Document.ReplaceLine(row, actionLine.ToString(), raiseEvents: false);
        }
        
        if (changed)
            Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
    }
    
    private void CombineInputs(bool sameActions) {
        if (Document.Selection.Empty) {
            // Merge current input with surrounding inputs
            // Don't allow this without sameActions
            if (!sameActions) return;
            
            int curr = Document.Caret.Row;
            if (!ActionLine.TryParse(Document.Lines[curr], out var currActionLine))
                return;
            
            // Above
            int above = curr - 1;
            for (; above >= 0; above--) {
                if (!ActionLine.TryParse(Document.Lines[above], out var otherActionLine))
                    break;
                
                if (currActionLine.Actions != otherActionLine.Actions ||
                     currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                     currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude) 
                {
                    break;
                }
                
                currActionLine.Frames += otherActionLine.Frames;
            }
            
            // Below
            int below = curr + 1;
            for (; below < Document.Lines.Count; below++) {
                if (!ActionLine.TryParse(Document.Lines[below], out var otherActionLine))
                    break;
                
                if (currActionLine.Actions != otherActionLine.Actions ||
                    currActionLine.FeatherAngle != otherActionLine.FeatherAngle ||
                    currActionLine.FeatherMagnitude != otherActionLine.FeatherMagnitude)
                {
                    break;
                }
                
                currActionLine.Frames += otherActionLine.Frames;
            }
            
            // Account for overshoot by 1
            above = Math.Min(Document.Lines.Count, above + 1);
            below = Math.Max(0, below - 1);
            
            Document.PushUndoState();
            Document.RemoveLines(above, below, raiseEvents: false);
            Document.InsertNewLine(above, currActionLine.ToString(), raiseEvents: false);
            Document.OnTextChanged(new CaretPosition(above, 0), new CaretPosition(above, Document.Lines[above].Length));
            
            Document.Caret.Row = above;
            Document.Caret.Col = SnapColumnToActionLine(currActionLine, Document.Caret.Col);
        } else {
            // Merge everything inside the selection
            int minRow = Document.Selection.Min.Row;
            int maxRow = Document.Selection.Max.Row;
            
            Document.PushUndoState();
            
            ActionLine? activeActionLine = null;
            int activeRowStart = -1;
            
            for (int row = minRow; row <= maxRow; row++) {
                if (!ActionLine.TryParse(Document.Lines[row], out var currActionLine))
                    continue; // Skip non-input lines
                
                if (activeActionLine == null) {
                    activeActionLine = currActionLine;
                    activeRowStart = row;
                    continue;
                }
                
                if (!sameActions) {
                    // Just merge them, regardless if they are the same actions
                    activeActionLine = activeActionLine.Value with { Frames = activeActionLine.Value.Frames + currActionLine.Frames };
                    continue;
                }
                
                if (currActionLine.Actions == activeActionLine.Value.Actions &&
                    currActionLine.FeatherAngle == activeActionLine.Value.FeatherAngle &&
                    currActionLine.FeatherMagnitude == activeActionLine.Value.FeatherMagnitude) 
                {
                    activeActionLine = activeActionLine.Value with { Frames = activeActionLine.Value.Frames + currActionLine.Frames };
                    continue;
                }
                
                // Current line is different, so change the active one
                Document.RemoveLines(activeRowStart, row - 1, raiseEvents: false);
                Document.InsertNewLine(activeRowStart, activeActionLine.Value.ToString(), raiseEvents: false);
                
                activeActionLine = currActionLine;
                activeRowStart++;
                
                // Account for changed line counts
                maxRow -= row - activeRowStart;
                row = activeRowStart;
            }
            
            // "Flush" the remaining line
            if (activeActionLine != null) {
                Document.RemoveLines(activeRowStart, maxRow, raiseEvents: false);
                Document.InsertNewLine(activeRowStart, activeActionLine.Value.ToString(), raiseEvents: false);
                
                maxRow = activeRowStart;
            }
            
            Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
            Document.Selection.Clear();
            
            Document.Caret.Row = maxRow;
            if (ActionLine.TryParse(Document.Lines[maxRow], out var actionLine))
                Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
            
            ScrollCaretIntoView();
        }
    }

    #endregion
    
    #region Caret Movement
    
    private CaretPosition ClampCaret(CaretPosition position, bool wrapLine = true) {
        // Wrap around to prev/next line
        if (wrapLine && position.Row > 0 && position.Col < 0) {
            position.Row--;
            position.Col = Document.Lines[position.Row].Length;
        } else if (wrapLine && position.Row < Document.Lines.Count && position.Col > Document.Lines[position.Row].Length) {
            position.Row++;
            position.Col = 0;
        }
        
        // Clamp to document
        position.Row = Math.Clamp(position.Row, 0, Document.Lines.Count - 1);
        position.Col = Math.Clamp(position.Col, 0, Document.Lines[position.Row].Length);
        
        // Clamp to action line if possible
        if (ActionLine.TryParse(Document.Lines[position.Row], out var actionLine))
            position.Col = SnapColumnToActionLine(actionLine, position.Col);
        
        return position;
    }
    
    public void ScrollCaretIntoView(bool center = false) {
        // Clamp just to be sure
        Document.Caret = ClampCaret(Document.Caret, wrapLine: false);
        
        // Minimum distance to the edges
        const float xLookAhead = 50.0f;
        const float yLookAhead = 50.0f;
        
        var caretPos = GetVisualPosition(Document.Caret);
        float carX = Font.CharWidth() * caretPos.Col;
        float carY = Font.LineHeight() * caretPos.Row;
        
        float top = scrollablePosition.Y;
        float bottom = (scrollable.Size.Height - Studio.BorderBottomOffset) + scrollablePosition.Y;
        
        // Always scroll horizontally, since we want to stay as left as possible
        const float scrollStopPadding = 10.0f;
        int scrollX = Font.MeasureWidth(GetVisualLine(caretPos.Row)) < (scrollable.Width - Studio.BorderRightOffset - Studio.WidthRightOffset - scrollStopPadding)
            ? 0 // Don't scroll when the line is shorter anyway
            : (int)((carX + xLookAhead) - (scrollable.Size.Width - Studio.BorderRightOffset - Studio.WidthRightOffset));
        int scrollY = scrollablePosition.Y;
        
        if (center) {
            // Keep line in the center
            scrollY = (int)(carY - scrollable.Size.Height / 2.0f);
        } else {
            // Scroll up/down when near the top/bottom
            if (top - carY > -yLookAhead)
                scrollY = (int)(carY - yLookAhead);
            else if (bottom - carY < yLookAhead)
                scrollY = (int)(carY + yLookAhead - (scrollable.Size.Height - Studio.BorderBottomOffset));
        }

        scrollable.ScrollPosition = new Point(
            Math.Max(0, scrollX),
            Math.Max(0, scrollY));
    }
    
    private void MoveCaret(CaretMovementType direction, bool updateSelection) {
        var newCaret = Document.Caret;
        
        var line = Document.Lines[Document.Caret.Row];
        if (ActionLine.TryParse(line, out var actionLine)) {
            newCaret.Col = SnapColumnToActionLine(actionLine, newCaret.Col);
            int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Digits();
            
            newCaret = direction switch {
                CaretMovementType.CharLeft  => ClampCaret(new CaretPosition(newCaret.Row, GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < newCaret.Col, newCaret.Col))),
                CaretMovementType.CharRight => ClampCaret(new CaretPosition(newCaret.Row, GetSoftSnapColumns(actionLine).FirstOrDefault(c => c > newCaret.Col, newCaret.Col))),
                CaretMovementType.WordLeft  => ClampCaret(new CaretPosition(newCaret.Row, GetHardSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < newCaret.Col, newCaret.Col))),
                CaretMovementType.WordRight => ClampCaret(new CaretPosition(newCaret.Row, GetHardSnapColumns(actionLine).FirstOrDefault(c => c > newCaret.Col, newCaret.Col))),
                CaretMovementType.LineStart => ClampCaret(new CaretPosition(newCaret.Row, leadingSpaces), wrapLine: false),
                CaretMovementType.LineEnd   => ClampCaret(new CaretPosition(newCaret.Row, line.Length), wrapLine: false),
                _ => GetNewTextCaretPosition(direction),
            };
        } else {
            // Regular text movement
            newCaret = GetNewTextCaretPosition(direction);
        }
        
        // Apply / Update desired column
        var oldVisualPos = GetVisualPosition(Document.Caret);
        var newVisualPos = GetVisualPosition(newCaret);
        if (oldVisualPos.Row != newVisualPos.Row) {
            newVisualPos.Col = desiredVisualCol;
        } else {
            desiredVisualCol = newVisualPos.Col;
        }
        newCaret = ClampCaret(GetActualPosition(newVisualPos), wrapLine: false);
        
        var newLine = Document.Lines[newCaret.Row];
        if (ActionLine.TryParse(newLine, out var newActionLine)) {
            newCaret.Col = SnapColumnToActionLine(newActionLine, newCaret.Col);
        }
        
        if (updateSelection) {
            if (Document.Selection.Empty)
                Document.Selection.Start = Document.Caret;    
            
            Document.Selection.End = newCaret;
        } else {
            Document.Selection.Clear();
        }
        
        autoCompleteMenu.Visible = false;
        
        Document.Caret = newCaret;
        ScrollCaretIntoView();
    }
    
    // For regular text movement
    private CaretPosition GetNewTextCaretPosition(CaretMovementType direction) =>
        direction switch {
            CaretMovementType.None => Document.Caret,
            CaretMovementType.CharLeft => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Caret.Col - 1)),
            CaretMovementType.CharRight => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Caret.Col + 1)),
            CaretMovementType.WordLeft => ClampCaret(GetNextWordCaretPosition(-1)),
            CaretMovementType.WordRight => ClampCaret(GetNextWordCaretPosition(1)),
            CaretMovementType.LineUp => ClampCaret(GetNextVisualLinePosition(-1), wrapLine: false),
            CaretMovementType.LineDown => ClampCaret(GetNextVisualLinePosition(1), wrapLine: false),
            // TODO: Page Up / Page Down
            CaretMovementType.PageUp => ClampCaret(GetNextVisualLinePosition(-1), wrapLine: false),
            CaretMovementType.PageDown => ClampCaret(GetNextVisualLinePosition(1), wrapLine: false),
            CaretMovementType.LineStart => ClampCaret(new CaretPosition(Document.Caret.Row, 0), wrapLine: false),
            CaretMovementType.LineEnd => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Lines[Document.Caret.Row].Length), wrapLine: false),
            CaretMovementType.DocumentStart => ClampCaret(new CaretPosition(0, 0), wrapLine: false),
            CaretMovementType.DocumentEnd => ClampCaret(new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length), wrapLine: false),
            _ => throw new UnreachableException()
        };
    
    private enum CharType { Alphanumeric, Symbol, Whitespace }
    private CaretPosition GetNextWordCaretPosition(int dir) {
        var newPosition = Document.Caret;
        var line = Document.Lines[newPosition.Row];
        
        // Prepare wrap-around for ClampCaret()
        if (dir == -1 && Document.Caret.Col == 0)
            return new CaretPosition(Document.Caret.Row, -1);
        if (dir == 1 && Document.Caret.Col == line.Length)
            return new CaretPosition(Document.Caret.Row, line.Length + 1);
        
        // The caret is to the left of the character. So offset 1 to the left when going that direction 
        int offset = dir == -1 ? -1 : 0;
        
        CharType type;
        if (char.IsLetterOrDigit(line[newPosition.Col + offset]))
            type = CharType.Alphanumeric;
        else if (char.IsWhiteSpace(line[newPosition.Col + offset]))
            type = CharType.Whitespace;
        else
            // Probably a symbol  
            type = CharType.Symbol;
        
        while (newPosition.Col + offset >= 0 && newPosition.Col + offset < line.Length && IsSame(line[newPosition.Col + offset], type))
            newPosition.Col += dir;
        
        return newPosition;
        
        static bool IsSame(char c, CharType type) {
            return type switch {
                CharType.Alphanumeric => char.IsLetterOrDigit(c),
                CharType.Whitespace => char.IsWhiteSpace(c),
                CharType.Symbol => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c), // Everything not alphanumeric of whitespace is considered a symbol
                _ => throw new UnreachableException(),
            };
        }
    }
    
    private CaretPosition GetNextVisualLinePosition(int dist) {
        var visualPos = GetVisualPosition(Document.Caret);
        return GetActualPosition(new CaretPosition(visualPos.Row + dist, visualPos.Col));
    }

    #endregion
    
    #region Mouse Interactions
    
    private bool primaryMouseButtonDown = false;
    
    protected override void OnMouseDown(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Primary)) {
            primaryMouseButtonDown = true;
            
            var oldCaret = Document.Caret;
            SetCaretPosition(e.Location);
            ScrollCaretIntoView();
            
            if (e.Modifiers.HasFlag(Keys.Shift)) {
                if (Document.Selection.Empty)
                    Document.Selection.Start = oldCaret;
                Document.Selection.End = Document.Caret;
            } else {
                Document.Selection.Start = Document.Selection.End = Document.Caret;
            }
            
            Recalc();
        }
        if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
            ContextMenu.Show();
        }
        
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Primary)) {
            primaryMouseButtonDown = false;
            
            Recalc();
        }

        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        if (primaryMouseButtonDown) {
            SetCaretPosition(e.Location);
            ScrollCaretIntoView();

            Document.Selection.End = Document.Caret;
            
            Recalc();
        }
        
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        if (e.Modifiers.HasFlag(Keys.Control)) {
            const float scrollSpeed = 0.1f;
            if (e.Delta.Height < 0.0f) {
                Settings.Instance.FontZoom *= 1.0f - scrollSpeed;
            } else if (e.Delta.Height > 0.0f) {
                Settings.Instance.FontZoom *= 1.0f + scrollSpeed;
            }
            Settings.Instance.OnFontChanged();
            
            e.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }
    
    private void SetCaretPosition(PointF location) {
        location.X -= textOffsetX;
        
        int visualRow = (int)(location.Y / Font.LineHeight());
        // Since we use a monospace font, we can just calculate the column
        int visualCol = (int)(location.X / Font.CharWidth());
        
        Document.Caret = ClampCaret(GetActualPosition(new CaretPosition(visualRow, visualCol)), wrapLine: false);
        desiredVisualCol = Document.Caret.Col;
        
        var newLine = Document.Lines[Document.Caret.Row];
        if (ActionLine.TryParse(newLine, out var actionLine)) {
            Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
        }
        
        autoCompleteMenu.Visible = false;
    }
    
    #endregion
    
    #region Drawing
    
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.AntiAlias = true;
        
        // To be reused below. Kinda annoying how C# handles out parameter conflicts
        WrapEntry wrap;
        
        const int offscreenLinePadding = 3;

        int topRow = Math.Max(0, GetActualRow((int)(scrollablePosition.Y / Font.LineHeight()) - offscreenLinePadding));
        int bottomRow = Math.Min(Document.Lines.Count - 1, GetActualRow((int)((scrollablePosition.Y + scrollable.Height) / Font.LineHeight()) + offscreenLinePadding));
        
        // Draw text
        using var commentBrush = new SolidBrush(Settings.Instance.Theme.Comment.ForegroundColor);
        
        float yPos = visualRows[topRow] * Font.LineHeight();
        for (int row = topRow; row <= bottomRow; row++) {
            string line = Document.Lines[row];
            
            if (commentLineWraps.TryGetValue(row, out wrap)) {
                for (int i = 0; i < wrap.Lines.Length; i++) {
                    var subLine = wrap.Lines[i].Line;
                    float xIdent = i == 0 ? 0 : wrap.StartOffset * Font.CharWidth();
                    
                    e.Graphics.DrawText(Font, commentBrush, textOffsetX + xIdent, yPos, subLine);
                    yPos += Font.LineHeight();
                }
            } else {
                highlighter.DrawLine(e.Graphics, textOffsetX, yPos, line);
                yPos += Font.LineHeight();
            }
        }
        
        // Draw quick-edits
        foreach (var anchor in GetQuickEdits()) {
            const float padding = 1.0f;
            
            float y = Font.LineHeight() * anchor.Row;
            float x = Font.CharWidth() * anchor.MinCol;
            float w = Font.CharWidth() * anchor.MaxCol - x;
            
            bool selected = Document.Caret.Row == anchor.Row && 
                            Document.Caret.Col >= anchor.MinCol &&
                            Document.Caret.Col <= anchor.MaxCol;
            
            using var pen = new Pen(selected ? Colors.White : Colors.Gray, selected ? 2.0f : 1.0f);
            e.Graphics.DrawRectangle(pen, x + textOffsetX - padding, y - padding, w + padding * 2.0f, Font.LineHeight() + padding * 2.0f);
        }
        
        // Draw suffix text
        if (Studio.CommunicationWrapper.Connected && Studio.CommunicationWrapper.CurrentLine != -1 && Studio.CommunicationWrapper.CurrentLine < visualRows.Length) {
            const float padding = 10.0f;
            float suffixWidth = Font.MeasureWidth(Studio.CommunicationWrapper.CurrentLineSuffix); 
            
            e.Graphics.DrawText(Font, Settings.Instance.Theme.PlayingFrame,
                x: scrollablePosition.X + scrollable.Width - Studio.WidthRightOffset - suffixWidth - padding,
                y: visualRows[Studio.CommunicationWrapper.CurrentLine] * Font.LineHeight(),
                Studio.CommunicationWrapper.CurrentLineSuffix);
        }
        
        var caretPos = GetVisualPosition(Document.Caret);
        float carX = Font.CharWidth() * caretPos.Col + textOffsetX;
        float carY = Font.LineHeight() * caretPos.Row;
        
        // Highlight caret line
        e.Graphics.FillRectangle(Settings.Instance.Theme.CurrentLine, 0.0f, carY, scrollable.Width, Font.LineHeight());
        
        // Draw caret
        if (HasFocus) {
            e.Graphics.DrawLine(Settings.Instance.Theme.Caret, carX, carY, carX, carY + Font.LineHeight() - 1);
        }
        
        // Draw selection
        if (!Document.Selection.Empty) {
            var min = GetVisualPosition(Document.Selection.Min);
            var max = GetVisualPosition(Document.Selection.Max);
            
            if (min.Row == max.Row) {
                float x = Font.CharWidth() * min.Col + textOffsetX;
                float w = Font.CharWidth() * (max.Col - min.Col);
                float y = Font.LineHeight() * min.Row;
                float h = Font.LineHeight();
                e.Graphics.FillRectangle(Settings.Instance.Theme.Selection, x, y, w, h);
            } else {
                var visualLine = GetVisualLine(min.Row);
                float x = Font.CharWidth() * min.Col + textOffsetX;
                float w = visualLine.Length == 0 ? 0.0f : Font.MeasureWidth(visualLine[min.Col..]);
                float y = Font.LineHeight() * min.Row;
                e.Graphics.FillRectangle(Settings.Instance.Theme.Selection, x, y, w, Font.LineHeight());
                
                for (int i = min.Row + 1; i < max.Row; i++) {
                    w = Font.MeasureWidth(GetVisualLine(i));
                    y = Font.LineHeight() * i;
                    e.Graphics.FillRectangle(Settings.Instance.Theme.Selection, textOffsetX, y, w, Font.LineHeight());
                }
                
                w = Font.MeasureWidth(GetVisualLine(max.Row)[..max.Col]);
                y = Font.LineHeight() * max.Row;
                e.Graphics.FillRectangle(Settings.Instance.Theme.Selection, textOffsetX, y, w, Font.LineHeight());
            }
        }
        
        // Draw line numbers
        {
            e.Graphics.FillRectangle(BackgroundColor,
                x: scrollablePosition.X,
                y: scrollablePosition.Y,
                width: textOffsetX - LineNumberPadding,
                height: scrollable.Size.Height);
            
            // Highlight playing / savestate line
            if (Studio.CommunicationWrapper.Connected) {
                if (Studio.CommunicationWrapper.CurrentLine != -1 && Studio.CommunicationWrapper.CurrentLine < visualRows.Length) {
                    e.Graphics.FillRectangle(Settings.Instance.Theme.PlayingLine,
                        x: scrollablePosition.X,
                        y: visualRows[Studio.CommunicationWrapper.CurrentLine] * Font.LineHeight(),
                        width: textOffsetX - LineNumberPadding,
                        height: Font.LineHeight());
                }
                if (Studio.CommunicationWrapper.SaveStateLine != -1 && Studio.CommunicationWrapper.SaveStateLine < visualRows.Length) {
                    if (Studio.CommunicationWrapper.SaveStateLine == Studio.CommunicationWrapper.CurrentLine) {
                        e.Graphics.FillRectangle(Settings.Instance.Theme.Savestate,
                            x: scrollablePosition.X,
                            y: visualRows[Studio.CommunicationWrapper.SaveStateLine] * Font.LineHeight(),
                            width: 15.0f,
                            height: Font.LineHeight());
                    } else {
                        e.Graphics.FillRectangle(Settings.Instance.Theme.Savestate,
                            x: scrollablePosition.X,
                            y: visualRows[Studio.CommunicationWrapper.SaveStateLine] * Font.LineHeight(),
                            width: textOffsetX - LineNumberPadding,
                            height: Font.LineHeight());
                    }
                }
            }
            
            yPos = visualRows[topRow] * Font.LineHeight();
            for (int row = topRow; row <= bottomRow; row++) {
                e.Graphics.DrawText(Font, Settings.Instance.Theme.LineNumber, scrollablePosition.X + LineNumberPadding, yPos, (row + 1).ToString());
                
                if (commentLineWraps.TryGetValue(row, out wrap)) {
                    yPos += Font.LineHeight() * wrap.Lines.Length;
                } else {
                    yPos += Font.LineHeight();
                }
            }
            
            e.Graphics.DrawLine(Settings.Instance.Theme.ServiceLine,
                scrollablePosition.X + textOffsetX - LineNumberPadding, 0.0f,
                scrollablePosition.X + textOffsetX - LineNumberPadding, yPos + scrollable.Size.Height);
        }
        
        // Draw autocomplete popup
        const float autocompleteXPos = 8.0f;
        const float autocompleteYOffset = 7.0f;
        
        autoCompleteMenu.Draw(e.Graphics, Font,
            scrollablePosition.X + textOffsetX + autocompleteXPos,
            carY + Font.LineHeight() + autocompleteYOffset);
        
        base.OnPaint(e);
    }
    
    #endregion
    
    #region Helper Methods
    
    // For movement without Ctrl
    private static IReadOnlyList<int> GetSoftSnapColumns(ActionLine actionLine) {
        int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Digits();

        List<int> softSnapColumns = [];
        // Frame count
        softSnapColumns.AddRange(Enumerable.Range(leadingSpaces, actionLine.Frames.Digits() + 1));
        // Actions
        foreach (var action in actionLine.Actions.Sorted()) {
            int column = GetColumnOfAction(actionLine, action);
            softSnapColumns.Add(column);

            if (action == Actions.DashOnly)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.Actions.GetDashOnly().Count()));
            if (action == Actions.MoveOnly)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.Actions.GetMoveOnly().Count()));
            if (action == Actions.PressedKey)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.CustomBindings.Count));
        }
        // Feather angle/magnitude
        if (actionLine.Actions.HasFlag(Actions.Feather)) {
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            softSnapColumns.AddRange(Enumerable.Range(featherColumn, actionLine.ToString().Length + 1 - featherColumn));
        }

        return softSnapColumns.AsReadOnly();
    }

    // For movement with Ctrl
    private static IReadOnlyList<int> GetHardSnapColumns(ActionLine actionLine) {
        int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Digits();

        List<int> hardSnapColumns =
        [
            leadingSpaces,
            ActionLine.MaxFramesDigits,
        ];
        
        // Actions
        if (actionLine.Actions != Actions.None) {
            hardSnapColumns.Add(GetColumnOfAction(actionLine, actionLine.Actions.Sorted().Last()) + actionLine.CustomBindings.Count);

        // Feather angle/magnitude
        if (actionLine.Actions.HasFlag(Actions.Feather)) {
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            string line = actionLine.ToString();

            int decimalColumn = featherColumn + 1;
            while (decimalColumn <= line.Length && line[decimalColumn - 1] != '.') {
                decimalColumn++;
            }
            hardSnapColumns.Add(decimalColumn);
            hardSnapColumns.Add(decimalColumn + 1);

            if (actionLine.FeatherMagnitude != null) {
                hardSnapColumns.Add(featherColumn + 1);
                int borderColumn = featherColumn + 1;
                while (borderColumn <= line.Length && line[borderColumn - 1] != ',') {
                    borderColumn++;
                }
                hardSnapColumns.Add(borderColumn);
                hardSnapColumns.Add(borderColumn + 1);

                decimalColumn = borderColumn + 1;
                while (decimalColumn <= line.Length && line[decimalColumn - 1] != '.') {
                    decimalColumn++;
                }
                hardSnapColumns.Add(decimalColumn);
                hardSnapColumns.Add(decimalColumn + 1);
            }
            hardSnapColumns.Add(line.Length + 1);
        }}

        return hardSnapColumns.AsReadOnly();
    }
    
    private static int GetColumnOfAction(ActionLine actionLine, Actions action) {
        int index = actionLine.Actions.Sorted().IndexOf(action);
        if (index < 0) return -1;

        int dashOnlyIndex = actionLine.Actions.Sorted().IndexOf(Actions.DashOnly);
        int moveOnlyIndex = actionLine.Actions.Sorted().IndexOf(Actions.MoveOnly);
        int customBindingIndex = actionLine.Actions.Sorted().IndexOf(Actions.PressedKey);

        int additionalOffset = 0;

        if (dashOnlyIndex != -1 && index > dashOnlyIndex)
            additionalOffset += actionLine.Actions.GetDashOnly().Count();
        if (moveOnlyIndex != -1 && index > moveOnlyIndex)
            additionalOffset += actionLine.Actions.GetMoveOnly().Count();
        if (customBindingIndex != -1 && index > customBindingIndex)
            additionalOffset += actionLine.CustomBindings.Count;

        return ActionLine.MaxFramesDigits + (index + 1) * 2 + additionalOffset;
    }

    private static Actions GetActionsFromColumn(ActionLine actionLine, int column, CaretMovementType direction) {
        var lineText = actionLine.ToString();

        if ((column <= ActionLine.MaxFramesDigits + 1) &&
            direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft) {
            return Actions.None; // There are no actions to the left of the caret
        }
        if ((column <= ActionLine.MaxFramesDigits || column >= lineText.Length) &&
            direction is CaretMovementType.CharRight or CaretMovementType.WordRight) {
            return Actions.None; // There are no actions to the right of the caret
        }

        if (direction == CaretMovementType.CharLeft) {
            //  15,R|,X => R
            return lineText[column - 2].ActionForChar();
        } else if (direction == CaretMovementType.CharRight) {
            //  15,R|,X => X
            return lineText[column].ActionForChar();
        } else if (direction == CaretMovementType.WordLeft) {
            //  15,R,D|,X => R,D
            var actions = Actions.None;
            while (column > ActionLine.MaxFramesDigits + 1) {
                actions |= lineText[column - 2].ActionForChar();
                column -= 2;
            }
            return actions;
        } else {
            //  15,R|,D,X => D,X
            var actions = Actions.None;
            while (column < lineText.Length) {
                actions |= lineText[column].ActionForChar();
                column += 2;
            }
            return actions;
        }
    }

    private static int SnapColumnToActionLine(ActionLine actionLine, int column) {
        // Snap to the closest valid column
        int nextLeft = GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c <= column, -1);
        int nextRight = GetSoftSnapColumns(actionLine).FirstOrDefault(c => c >= column, -1);

        if (nextLeft == column || nextRight == column) return column;

        if (nextLeft == -1 && nextRight == -1) return column;
        if (nextLeft == -1) return nextRight;
        if (nextRight == -1) return nextLeft;

        return column - nextLeft < nextRight - column 
            ? nextLeft 
            : nextRight;
    }
    
    #endregion
}