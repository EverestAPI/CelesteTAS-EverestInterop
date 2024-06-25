using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio;

public sealed class Editor : Drawable {
    private Document document;
    public Document Document {
        get => document;
        set {
            document = value;
            
            // Jump to end when file only 10 lines, else the start
            if (document.Lines.Count <= 10)
                document.Caret = new CaretPosition(document.Lines.Count - 1, document.Lines[^1].Length);
            else
                document.Caret = new CaretPosition(0, 0);
            
            // Try to reparse into action lines on change
            document.TextChanged += (_, min, max) => {
                ConvertToActionLines(min, max);
                Recalc();
            };
            
            Recalc();
        }
    }
    
    private readonly Font font = new(new FontFamily("JetBrains Mono"), 12.0f);
    private readonly Scrollable scrollable;
    
    private const float LineNumberPadding = 5.0f;
    private float textOffsetX;
    
    public Editor(Document document, Scrollable scrollable) {
        this.document = document;
        this.scrollable = scrollable;

        CanFocus = true;
        BackgroundColor = Colors.Black;
        Cursor = Cursors.IBeam;
        
        // Need to redraw the line numbers
        scrollable.Scroll += (_, _) => Invalidate();

        Studio.CelesteService.Server.StateUpdated += state => {
            if (state.CurrentLine != -1) {
                Document.Caret.Row = state.CurrentLine;
                Document.Caret.Col = ActionLine.MaxFramesDigits;
                Document.Caret = ClampCaret(Document.Caret, wrapLine: false);
            }
            
            // Need to redraw the current state
            Application.Instance.InvokeAsync(() => {
                ScrollCaretIntoView(center: true);
                Invalidate();
            });
        };
        
        ContextMenu = new ContextMenu {
            Items = {
                CreateAction("Cut", Application.Instance.CommonModifier | Keys.X, OnCut),
                CreateAction("Copy", Application.Instance.CommonModifier | Keys.C, OnCopy),
                CreateAction("Paste", Application.Instance.CommonModifier | Keys.V, OnPaste),
                new SeparatorMenuItem(),
                CreateAction("Undo", Application.Instance.CommonModifier | Keys.Z, OnUndo),
                CreateAction("Redo", Application.Instance.CommonModifier | Keys.Z | Keys.Shift, OnRedo),
                new SeparatorMenuItem(),
                CreateAction("Select All", Application.Instance.CommonModifier | Keys.A, OnSelectAll),
                CreateAction("Select Block", Application.Instance.CommonModifier | Keys.W, OnSelectBlock),
                new SeparatorMenuItem(),
                CreateAction("Insert/Remove Breakpoint"),
                CreateAction("Insert/Remove Savestate Breakpoint"),
                CreateAction("Remove All Uncommented Breakpoints"),
                CreateAction("Remove All Breakpoints"),
                CreateAction("Comment/Uncomment All Breakpoints"),
                CreateAction("Comment/Uncomment Inputs", Application.Instance.CommonModifier | Keys.K, OnToggleCommentInputs),
                CreateAction("Comment/Uncomment Text", Application.Instance.CommonModifier | Keys.K | Keys.Shift, OnToggleCommentText),
                new SeparatorMenuItem(),
                CreateAction("Insert Room Name", Application.Instance.CommonModifier | Keys.R, OnInsertRoomName),
                CreateAction("Insert Time", Application.Instance.CommonModifier | Keys.T, OnInsertTime),
                CreateAction("Insert Mod Info", Keys.None, OnInsertModInfo),
                CreateAction("Insert Console Load Command", Keys.None, OnInsertConsoleLoadCommand),
                CreateAction("Insert Simple Console Load Command", Keys.None, OnInsertSimpleConsoleLoadCommand),
                new SubMenuItem {Text = "Insert Other Command", Items = {
                    CreateCommandInsert("EnforceLegal", "EnforceLegal"),
                    CreateCommandInsert("Unsafe", "Unsafe"),
                    CreateCommandInsert("Safe", "Safe"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("Read", "Read, File Name, Starting Line, (Ending Line)"),
                    CreateCommandInsert("Player", "Play, Starting Line"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("Repeat", "Repeat 2\n\nEndRepeat"),
                    CreateCommandInsert("EndRepeat", "EndRepeat"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("Set", "Set, (Mod).Setting, Value"),
                    CreateCommandInsert("Invoke", "Invoke, Entity.Method, Parameter"),
                    CreateCommandInsert("EvalLua", "EvalLua, Code"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("Press", "Press, Key1, Key2..."),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("AnalogMode", "AnalogMode, Ignore/Circle/Square/Precise"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("StunPause", "StunPause\n\nEndStunPause"),
                    CreateCommandInsert("EndStunPause", "EndStunPause"),
                    CreateCommandInsert("StunPauseMode", "StunPauseMode, Simulate/Input"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("AutoInput", "AutoInput, 2\n   1,S,N\n  10,O\nStartAutoInput\n\nEndAutoInput"),
                    CreateCommandInsert("StartAutoInput", "StartAutoInput"),
                    CreateCommandInsert("EndAutoInput", "EndAutoInput"),
                    CreateCommandInsert("SkipInput", "SkipInput"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("SaveAndQuitReenter", "SaveAndQuitReenter"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("ExportGameInfo", "ExportGameInfo dump.txt"),
                    CreateCommandInsert("EndExportGameInfo", "EndExportGameInfo"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("StartRecording", "StartRecording"),
                    CreateCommandInsert("StopRecording", "StopRecording"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("Add", "Add, (input line"),
                    CreateCommandInsert("Skip", "Skip"),
                    CreateCommandInsert("Marker", "Marker"),
                    CreateCommandInsert("ExportLibTAS", "ExportLibTAS Celeste.ltm"),
                    CreateCommandInsert("EndExportLibTAS", "EndExportLibTAS"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("CompleteInfo", "CompleteInfo A 1"),
                    CreateCommandInsert("RecordCount", "RecordCount: 1"),
                    CreateCommandInsert("FileTime", "FileTime:"),
                    CreateCommandInsert("ChapterTime", "ChapterTime:"),
                    CreateCommandInsert("MidwayFileTime", "MidwayFileTime:"),
                    CreateCommandInsert("MidwayChapterTime", "MidwayChapterTime:"),
                    new SeparatorMenuItem(),
                    CreateCommandInsert("ExitGame", "ExitGame"),
                }},
                new SeparatorMenuItem(),
                CreateAction("Swap Selected X and C"),
                CreateAction("Swap Selected J and K"),
                CreateAction("Combine Consecutive Same Inputs"),
                CreateAction("Force Combine Input Frames"),
                CreateAction("Convert Dash to Demo Dash"),
                new SeparatorMenuItem(),
                CreateAction("Open Read File / Go to Play Line"),
            }
        };
        
        Recalc();
        
        static MenuItem CreateAction(string text, Keys shortcut = Keys.None, Action? action = null) {
            var cmd = new Command { MenuText = text, Shortcut = shortcut, Enabled = action != null };
            cmd.Executed += (_, _) => action?.Invoke();

            return cmd; 
        }
        
        MenuItem CreateCommandInsert(string commandName, string commandInsert) {
            var cmd = new Command { MenuText = commandName, Shortcut = Keys.None };
            cmd.Executed += (_, _) => Document.InsertLineAbove(commandInsert);
            
            return cmd;
        }
    }
    
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
        
        textOffsetX = font.MeasureString("X").Width * Document.Lines.Count.Digits() + LineNumberPadding * 3.0f;
        
        // Calculate bounds
        float width = 0.0f, height = 0.0f;

        foreach (var line in Document.Lines) {
            var size = font.MeasureString(line);
            width = Math.Max(width, size.Width);
            height += size.Height;
        }
        
        const float paddingRight = 50.0f;
        const float paddingBottom = 100.0f;
        Size = new((int)(width + textOffsetX + paddingRight), (int)(height + paddingBottom));

        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (Settings.Instance.SendInputsToCeleste && Studio.CelesteService.Connected && Studio.CelesteService.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
            e.Handled = true;
            return;
        }
        
        switch (e.Key) {
            case Keys.Backspace:
                OnDelete(e.Control ? CaretMovementType.WordLeft : CaretMovementType.CharLeft);
                break;
            case Keys.Delete:
                OnDelete(e.Control ? CaretMovementType.WordRight : CaretMovementType.CharRight);
                break;
            case Keys.Enter:
                OnEnter();
                break;
            case Keys.Left:
                MoveCaret(e.Control ? CaretMovementType.WordLeft : CaretMovementType.CharLeft, updateSelection: e.Shift);
                break;
            case Keys.Right:
                MoveCaret(e.Control ? CaretMovementType.WordRight : CaretMovementType.CharRight, updateSelection: e.Shift);
                break;
            case Keys.Up:
                MoveCaret(CaretMovementType.LineUp, updateSelection: e.Shift);
                break;
            case Keys.Down:
                MoveCaret(CaretMovementType.LineDown, updateSelection: e.Shift);
                break;
            case Keys.PageUp:
                MoveCaret(CaretMovementType.PageUp, updateSelection: e.Shift);
                break;
            case Keys.PageDown:
                MoveCaret(CaretMovementType.PageDown, updateSelection: e.Shift);
                break;
            case Keys.Home:
                MoveCaret(CaretMovementType.LineStart, updateSelection: e.Shift);
                break;
            case Keys.End:
                MoveCaret(CaretMovementType.LineEnd, updateSelection: e.Shift);
                break;
            default:
                var key = e.Key;
                if (e.Shift) key |= Keys.Shift;
                if (e.Control) key |= Keys.Control;
                if (e.Alt) key |= Keys.Alt;
                
                // Search through context menu for hotkeys
                foreach (var item in ContextMenu.Items)
                {
                    if (item.Shortcut == key) {
                        item.PerformClick();
                        break;
                    }
                }
                
                base.OnKeyDown(e);
                break;
        }
        
        Recalc();
    }
    
    protected override void OnKeyUp(KeyEventArgs e) {
        if (Settings.Instance.SendInputsToCeleste && Studio.CelesteService.Connected && Studio.CelesteService.SendKeyEvent(e.Key, e.Modifiers, released: true)) {
            e.Handled = true;
            return;
        }
        
        base.OnKeyUp(e);
    }
    
    #region Editing Actions

    private void ConvertToActionLines(CaretPosition start, CaretPosition end) {
        // Convert to action lines if possible
        int minRow = Math.Min(start.Row, end.Row);
        int maxRow = Math.Max(start.Row, end.Row);
        
        for (int row = minRow; row <= Math.Min(maxRow, Document.Lines.Count - 1); row++) {
            if (ActionLine.TryParse(Document.Lines[row], out var newActionLine)) {
                Document.ReplaceLine(row, newActionLine.ToString(), raiseEvents: false);
                if (Document.Caret.Row == row)
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
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
        bool startOfLine = Document.Caret.Col <= leadingSpaces + 1;
        
        // If it's an action line, handle it ourselves
        if (ActionLine.TryParse(line, out var actionLine) && e.Text.Length == 1) {
            // Handle custom bindings
            int customBindStart = GetColumnOfAction(actionLine, Actions.PressedKey);
            int customBindEnd = customBindStart + actionLine.CustomBindings.Count;
            if (customBindStart != -1 && Document.Caret.Col >= customBindStart && Document.Caret.Col <= customBindEnd && typedCharacter is >= 'A' and <= 'Z') {
                bool alreadyExists = !actionLine.CustomBindings.Add(typedCharacter);
                if (alreadyExists) {
                    actionLine.CustomBindings.Remove(typedCharacter);
                    Document.Caret.Col = customBindEnd - 1;
                } else {
                    Document.Caret.Col = customBindEnd + 1;
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
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction);
                Document.Caret.Col = GetColumnOfAction(actionLine, typedAction);
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
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction);

                // Warp the cursor after the number
                if (typedAction == Actions.Feather && actionLine.Actions.HasFlag(Actions.Feather)) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.Feather) + 1;
                } else if (typedAction == Actions.Feather && !actionLine.Actions.HasFlag(Actions.Feather)) {
                    actionLine.FeatherAngle = null;
                    actionLine.FeatherMagnitude = null;
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                } else if (typedAction is Actions.LeftDashOnly or Actions.RightDashOnly or Actions.UpDashOnly or Actions.DownDashOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.DashOnly) + actionLine.Actions.GetDashOnly().Count();
                } else if (typedAction is Actions.LeftMoveOnly or Actions.RightMoveOnly or Actions.UpMoveOnly or Actions.DownMoveOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.MoveOnly) + actionLine.Actions.GetMoveOnly().Count();
                } else {
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                }
            }
            // If the key we entered is a number
            else if (typedCharacter is >= '0' and <= '9') {
                int cursorPosition = Document.Caret.Col - leadingSpaces;

                // Entering a zero at the start should do nothing but format
                if (cursorPosition == 0 && typedCharacter == '0') {
                    Document.Caret.Col = ActionLine.MaxFramesDigits - actionLine.Frames.Digits();
                }
                // If we have a 0, just force the new number
                else if (actionLine.Frames == 0) {
                    actionLine.Frames = int.Parse(typedCharacter.ToString());
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
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
                        Document.Caret.Col = ActionLine.MaxFramesDigits;
                    } else {
                        Document.Caret.Col = ActionLine.MaxFramesDigits - actionLine.Frames.Digits() + cursorPosition + 1;
                    }
                }
            }

            FinishEdit:
            Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());
        }
        // Just write it as text
        else {
            Document.Insert(e.Text);
            
            // But turn it into an action line if possible
            if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out var newActionLine)) {
                Document.ReplaceLine(Document.Caret.Row, newActionLine.ToString());
                Document.Caret.Col = ActionLine.MaxFramesDigits;
            }
        }
        
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
                Document.ReplaceRangeInLine(caret.Row, caret.Col, newCaret.Col, string.Empty);
                newCaret.Col = Math.Min(newCaret.Col, caret.Col);
            } else {
                var min = newCaret < caret ? newCaret : caret;
                var max = newCaret < caret ? caret : newCaret;
                
                Document.RemoveRange(min, max);
                newCaret = min;
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
            Document.Caret.Col = 0;
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
    
    private void OnToggleCommentInputs() {
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
            } else {
                Document.ReplaceLine(row, $"#{line}", raiseEvents: false);
            }
        }
        Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
    }
    
    private void OnToggleCommentText() {
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
            } else {
                Document.ReplaceLine(row, $"#{line}", raiseEvents: false);
            }
        }
        Document.OnTextChanged(new CaretPosition(minRow, 0), new CaretPosition(maxRow, Document.Lines[maxRow].Length));
    }
    
    private void OnInsertRoomName() => Document.InsertLineAbove($"#{Studio.CelesteService.LevelName}");

    private void OnInsertTime() => Document.InsertLineAbove($"#{Studio.CelesteService.ChapterTime}");
    
    private void OnInsertModInfo() {
        if (Studio.CelesteService.Server.GetDataFromGame(GameDataType.ModInfo) is { } modInfo)
            Document.InsertLineAbove(modInfo);
    }
    
    private void OnInsertConsoleLoadCommand() {
        if (Studio.CelesteService.Server.GetDataFromGame(GameDataType.ConsoleCommand, false) is { } command)
            Document.InsertLineAbove(command);
    }
    
    private void OnInsertSimpleConsoleLoadCommand() {
        if (Studio.CelesteService.Server.GetDataFromGame(GameDataType.ConsoleCommand, true) is { } command)
            Document.InsertLineAbove(command);
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
        
        return position;
    }
    
    private void ScrollCaretIntoView(bool center = false) {
        // Clamp just to be sure
        Document.Caret = ClampCaret(Document.Caret, wrapLine: false);
        
        // Minimum distance to the edges
        const float xLookAhead = 50.0f;
        const float yLookAhead = 50.0f;
        
        float carX = font.MeasureString(Document.Lines[Document.Caret.Row][..Document.Caret.Col]).Width;
        float carY = font.LineHeight * Document.Caret.Row;
        
        float top = scrollable.ScrollPosition.Y;
        float bottom = (scrollable.Size.Height - Studio.BorderBottomOffset) + scrollable.ScrollPosition.Y;
        
        // Always scroll horizontally, since we want to stay as left as possible
        int scrollX = (int)((carX + xLookAhead) - (scrollable.Size.Width - Studio.BorderRightOffset));
        
        int scrollY = scrollable.ScrollPosition.Y;
        
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
                CaretMovementType.LineStart => ClampCaret(new CaretPosition(newCaret.Row, leadingSpaces + 1), wrapLine: false),
                CaretMovementType.LineEnd   => ClampCaret(new CaretPosition(newCaret.Row, line.Length + 1), wrapLine: false),
                _ => GetNewTextCaretPosition(direction),
            };
        } else {
            // Regular text movement
            newCaret = GetNewTextCaretPosition(direction);
        }
        
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
            CaretMovementType.LineUp => ClampCaret(new CaretPosition(Document.Caret.Row - 1, Document.Caret.Col), wrapLine: false),
            CaretMovementType.LineDown => ClampCaret(new CaretPosition(Document.Caret.Row + 1, Document.Caret.Col), wrapLine: false),
            // TODO: Page Up / Page Down
            CaretMovementType.PageUp => ClampCaret(new CaretPosition(Document.Caret.Row - 1, Document.Caret.Col), wrapLine: false),
            CaretMovementType.PageDown => ClampCaret(new CaretPosition(Document.Caret.Row + 1, Document.Caret.Col), wrapLine: false),
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
    
    private void SetCaretPosition(PointF location) {
        location.X -= textOffsetX;
        
        int row = Math.Clamp((int) MathF.Floor(location.Y / font.LineHeight), 0, Document.Lines.Count - 1);
        var line = Document.Lines[row];
        
        // Since we use a monospace font, we can just calculate the column
        int col = Math.Clamp((int) MathF.Floor(location.X / font.MeasureString("X").Width), 0, line.Length);
        
        Document.Caret = ClampCaret(new CaretPosition(row, col), wrapLine: false);
        
        var newLine = Document.Lines[Document.Caret.Row];
        if (ActionLine.TryParse(newLine, out var actionLine)) {
            Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
        }
    }
    
    #endregion
    
    #region Drawing
    
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.AntiAlias = true;
        
        // Draw text
        float yPos = 0.0f;
        foreach (var line in Document.Lines) {
            e.Graphics.DrawText(font, Colors.White, textOffsetX, yPos, line);
            yPos += font.LineHeight;
        }
        
        // Draw suffix text
        if (Studio.CelesteService.Connected) {
            const float padding = 10.0f;
            float suffixWidth = font.MeasureString(Studio.CelesteService.CurrentLineSuffix).Width; 
            
            e.Graphics.DrawText(font, Colors.Orange,
                x: scrollable.ScrollPosition.X + scrollable.Width - suffixWidth - padding,
                y: Studio.CelesteService.CurrentLine * font.LineHeight,
                Studio.CelesteService.CurrentLineSuffix);
        }
        
        float carX = font.MeasureString(Document.Lines[Document.Caret.Row][..Document.Caret.Col]).Width + textOffsetX;
        float carY = font.LineHeight * Document.Caret.Row;

        // Highlight caret line
        e.Graphics.FillRectangle(Color.FromGrayscale(0.9f, 0.1f), 0.0f, carY, scrollable.Width, font.LineHeight);
        
        // Draw caret
        if (HasFocus) {
            e.Graphics.DrawLine(Colors.Red, carX, carY, carX, carY + font.LineHeight - 1);
        }
        
        // Draw selection
        if (!Document.Selection.Empty) {
            var min = Document.Selection.Min;
            var max = Document.Selection.Max;
            
            var color = Color.FromArgb(0x00, 0x00, 0xFF, 0x7F);
            
            if (min.Row == max.Row) {
                float x = font.MeasureString(Document.Lines[min.Row][..min.Col]).Width + textOffsetX;
                float w = font.MeasureString(Document.Lines[min.Row][min.Col..max.Col]).Width;
                float y = font.LineHeight * min.Row;
                float h = font.LineHeight;
                e.Graphics.FillRectangle(color, x, y, w, h);
            } else {
                float x = font.MeasureString(Document.Lines[min.Row][..min.Col]).Width + textOffsetX;
                float w = font.MeasureString(Document.Lines[min.Row][min.Col..]).Width;
                float y = font.LineHeight * min.Row;
                e.Graphics.FillRectangle(color, x, y, w, font.LineHeight);
                
                for (int i = min.Row + 1; i < max.Row; i++) {
                    w = font.MeasureString(Document.Lines[i]).Width;
                    y = font.LineHeight * i;
                    e.Graphics.FillRectangle(color, textOffsetX, y, w, font.LineHeight);
                }
                
                w = font.MeasureString(Document.Lines[max.Row][..max.Col]).Width;
                y = font.LineHeight * max.Row;
                e.Graphics.FillRectangle(color, textOffsetX, y, w, font.LineHeight);
            }
        }
        
        // Draw line numbers
        {
            e.Graphics.FillRectangle(BackgroundColor,
                x: scrollable.ScrollPosition.X,
                y: scrollable.ScrollPosition.Y,
                width: textOffsetX - LineNumberPadding,
                height: scrollable.Size.Height);
            
            // Highlight playing / savestate line
            if (Studio.CelesteService.Connected) {
                if (Studio.CelesteService.CurrentLine != -1) {
                    e.Graphics.FillRectangle(Colors.Green,
                        x: scrollable.ScrollPosition.X,
                        y: Studio.CelesteService.CurrentLine * font.LineHeight,
                        width: textOffsetX - LineNumberPadding,
                        height: font.LineHeight);
                }
                if (Studio.CelesteService.SaveStateLine != -1) {
                    if (Studio.CelesteService.SaveStateLine == Studio.CelesteService.CurrentLine) {
                        e.Graphics.FillRectangle(Colors.Blue,
                            x: scrollable.ScrollPosition.X,
                            y: Studio.CelesteService.CurrentLine * font.LineHeight,
                            width: 15.0f,
                            height: font.LineHeight);
                    } else {
                        e.Graphics.FillRectangle(Colors.Blue,
                            x: scrollable.ScrollPosition.X,
                            y: Studio.CelesteService.CurrentLine * font.LineHeight,
                            width: textOffsetX - LineNumberPadding,
                            height: font.LineHeight);
                    }
                }
            }
            
            yPos = 0.0f;
            for (int i = 1; i <= Document.Lines.Count; i++) {
                e.Graphics.DrawText(font, Colors.White, scrollable.ScrollPosition.X + LineNumberPadding, yPos, i.ToString());
                yPos += font.LineHeight;
            }
            
            e.Graphics.DrawLine(Colors.White,
                scrollable.ScrollPosition.X + textOffsetX - LineNumberPadding, 0.0f,
                scrollable.ScrollPosition.X + textOffsetX - LineNumberPadding, yPos + scrollable.Size.Height);
        }
        
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
            // Actions
            GetColumnOfAction(actionLine, actionLine.Actions.Sorted().Last()) + actionLine.CustomBindings.Count
        ];

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
        }

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