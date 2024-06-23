using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio;

public class Editor : Drawable {
    public readonly Document Document;
    
    private Font font = new(new FontFamily("JetBrains Mono"), 12.0f);
    private readonly Scrollable scrollable;
    
    public Editor(Document document, Scrollable scrollable) {
        this.Document = document;
        this.scrollable = scrollable;

        CanFocus = true;
        BackgroundColor = Colors.Black;
        Cursor = Cursors.IBeam;
        
        Recalc();
    }
    
    // private bool needRecalc = true;
    private void Recalc() {
        // Snap caret
        Document.Caret.Row = Math.Clamp(Document.Caret.Row, 0, Document.Lines.Count - 1);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);
        
        // Calculate bounds
        float width = 0.0f, height = 0.0f;

        foreach (var line in Document.Lines) {
            var size = font.MeasureString(line);
            Console.WriteLine($"{line}: {size} ({width}x{height})");
            width = Math.Max(width, size.Width);
            height += size.Height;
            Console.WriteLine($"{line}: {size} ({width}x{height})");
        }
        
        Size = new((int)width, (int)height);
        Invalidate();
        
        // Bring caret into view
        const float xLookAhead = 1;
        const float yLookAhead = 1;
        
        float carX = font.MeasureString(Document.Lines[Document.Caret.Row][..Document.Caret.Col]).Width;
        float carY = font.LineHeight * Document.Caret.Row;
        // scrollable.ScrollPosition = new Point(
        //     Math.Clamp(scrollable.ScrollPosition.X, (int)(carX - xLookAhead), (int)(carX + xLookAhead)),
        //     Math.Clamp(scrollable.ScrollPosition.Y, (int)(carY - yLookAhead), (int)(carY + yLookAhead)));
        // TODO: Properly scroll caret into view (NOTE: macOS doesn't clamp on it's own!)
        // scrollable.ScrollPosition = new Point((int)carX + 50, (int)carY);
        // scrollable.Padding = new(0);
        
        Console.WriteLine($"w: {Width} h: {Height} x: {scrollable.ScrollPosition.X}");
        
        // needRecalc = false;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
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
                // Hotkeys
                if (e is { Key: Keys.Z, Control: true}) {
                    Document.Undo();
                    break;
                }
                if (e is { Key: Keys.Z, Control: true, Shift: true} or { Key: Keys.Y, Control: true}) {
                    Document.Redo();
                    break;
                }
                
                base.OnKeyDown(e);
                break;
        }
        
        Recalc();
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
        // Start an action line if we should
        else if (startOfLine && e.Text.Length == 1 && e.Text[0] is >= '0' and <= '9') {
            string newLine = typedCharacter.ToString().PadLeft(ActionLine.MaxFramesDigits);
            if (line.Trim().Length == 0)
                Document.ReplaceLine(Document.Caret.Row, newLine);
            else
                Document.Insert(new CaretPosition(Document.Caret.Row, 0), newLine + "\n");
            Document.Caret.Col = ActionLine.MaxFramesDigits + 1;

        }
        // Just write it as text
        else {
            Document.Insert(e.Text);
        }
        
        Recalc();
    }
    
    #region Editing Actions
    
    private void OnDelete(CaretMovementType direction) {
        if (!Document.Selection.Empty) {
            Document.RemoveSelectedText();
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
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
            Console.WriteLine($"From '{line}' from at {Math.Min(newColumn, caret.Col)} by {Math.Abs(newColumn - caret.Col)}");
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
            }
            
            Document.Caret = ClampCaret(newCaret);
        }
    }
    
    private void OnEnter() {
        var line = Document.Lines[Document.Caret.Row];
        
        if (ActionLine.TryParse(line, out var actionLine)) {
            // Don't split frame count and action
            Document.InsertNewLine(Document.Caret.Row + 1, string.Empty);
            Document.Caret.Row++;
        } else {
            Document.Insert(Environment.NewLine);
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
        
        return position;
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
            
            if (e.Modifiers.HasFlag(Keys.Shift)) {
                if (Document.Selection.Empty)
                    Document.Selection.Start = oldCaret;
                Document.Selection.End = Document.Caret;
            } else {
                Document.Selection.Start = Document.Selection.End = Document.Caret;
            }
            
            Recalc();
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
            Document.Selection.End = Document.Caret;
            
            Recalc();
        }
        
        base.OnMouseMove(e);
    }
    
    private void SetCaretPosition(PointF location) {
        var abs = location + scrollable.ScrollPosition;
        
        int row = Math.Clamp((int) MathF.Floor(abs.Y / font.LineHeight), 0, Document.Lines.Count - 1);
        var line = Document.Lines[row];
        
        // Since we use a monospace font, we can just calculate the column
        int col = Math.Clamp((int) MathF.Floor(abs.X / font.MeasureString("X").Width), 0, line.Length);
        
        Document.Caret = ClampCaret(new CaretPosition(row, col), wrapLine: false);
        
        var newLine = Document.Lines[Document.Caret.Row];
        if (ActionLine.TryParse(newLine, out var actionLine)) {
            Document.Caret.Col = SnapColumnToActionLine(actionLine, Document.Caret.Col);
        }
    }
    
    #endregion
    
    protected override void OnPaint(PaintEventArgs e) {
        // if (needRecalc)
        //     Recalc();

        //
        // if (needRecalcFoldingLines) {
        //     RecalcFoldingLines();
        // }
        //
        // visibleMarkers.Clear();
        e.Graphics.AntiAlias = false;

        #if false
        var servicePen = new Pen(ServiceLinesColor);
        var changedLineBrush = new SolidBrush(ChangedLineBgColor);
        var activeLineBrush = new SolidBrush(PlayingLineBgColor);
        var saveStateLineBrush = new SolidBrush(SaveStateBgColor);
        var indentBrush = new SolidBrush(IndentBackColor);
        var paddingBrush = new SolidBrush(PaddingBackColor);
        var currentLineBrush = new SolidBrush(CurrentLineColor);
        #else
        var paddingBrush = new SolidBrush(Colors.Aqua);
        #endif
        
        // Draw text
        float yPos = 0.0f;
        foreach (var line in Document.Lines) {
            e.Graphics.DrawText(font, Colors.White, 0.0f, yPos, line);
            yPos += font.LineHeight;
        }
        
        if (HasFocus) {
            // Draw caret
            float carX = font.MeasureString(Document.Lines[Document.Caret.Row][..Document.Caret.Col]).Width;
            float carY = font.LineHeight * Document.Caret.Row;
            using (Pen pen = new(Colors.Red)) {
                e.Graphics.DrawLine(pen, carX, carY, carX, carY + font.LineHeight - 1);
            }
        }
        
        // Draw selection
        if (!Document.Selection.Empty) {
            var min = Document.Selection.Min;
            var max = Document.Selection.Max;
            Console.WriteLine($"Selection: {min.Row},{min.Col} -> {max.Row},{max.Col} @ {Document.Caret.Row},{Document.Caret.Col}");
            
            var color = Color.FromArgb(0x00, 0x00, 0xFF, 0x7F);
            
            if (min.Row == max.Row) {
                float x = font.MeasureString(Document.Lines[min.Row][..min.Col]).Width;
                float w = font.MeasureString(Document.Lines[min.Row][min.Col..max.Col]).Width;
                float y = font.LineHeight * min.Row;
                float h = font.LineHeight;
                e.Graphics.FillRectangle(color, x, y, w, h);
            } else {
                float x = font.MeasureString(Document.Lines[min.Row][..min.Col]).Width;
                float w = font.MeasureString(Document.Lines[min.Row][min.Col..]).Width;
                float y = font.LineHeight * min.Row;
                e.Graphics.FillRectangle(color, x, y, w, font.LineHeight);
                
                for (int i = min.Row + 1; i < max.Row; i++) {
                    w = font.MeasureString(Document.Lines[i]).Width;
                    y = font.LineHeight * i;
                    e.Graphics.FillRectangle(color, 0.0f, y, w, font.LineHeight);
                }
                
                w = font.MeasureString(Document.Lines[max.Row][..max.Col]).Width;
                y = font.LineHeight * max.Row;
                e.Graphics.FillRectangle(color, 0.0f, y, w, font.LineHeight);
            }
        }
        
        // Draw padding area
        
        // Top
        // e.Graphics.FillRectangle(paddingBrush, 0, -VerticalScroll, ClientSize.Width, Math.Max(0, Paddings.Top - 1));
        // //bottom
        // int bottomPaddingStartY = wordWrapLinesCount * charHeight + Paddings.Top;
        // e.Graphics.FillRectangle(paddingBrush, 0, bottomPaddingStartY - VerticalScroll.Value, ClientSize.Width, ClientSize.Height);
        // //right
        // int rightPaddingStartX = LeftIndent + maxLineLength * CharWidth + Paddings.Left + 1;
        // e.Graphics.FillRectangle(paddingBrush, rightPaddingStartX - HorizontalScroll.Value, 0, ClientSize.Width, ClientSize.Height);
        // //left
        // e.Graphics.FillRectangle(paddingBrush, LeftIndentLine, 0, LeftIndent - LeftIndentLine - 1, ClientSize.Height);
        // if (HorizontalScroll.Value <= Paddings.Left) {
        //     e.Graphics.FillRectangle(paddingBrush, LeftIndent - HorizontalScroll.Value - 2, 0, Math.Max(0, Paddings.Left - 1), ClientSize.Height);
        // }
        //
        // int leftTextIndent = Math.Max(LeftIndent, LeftIndent + Paddings.Left - HorizontalScroll.Value);
        // int textWidth = rightPaddingStartX - HorizontalScroll.Value - leftTextIndent;
        // //draw indent area
        // e.Graphics.FillRectangle(indentBrush, 0, 0, LeftIndentLine, ClientSize.Height);
        // if (LeftIndent > minLeftIndent) {
        //     e.Graphics.DrawLine(servicePen, LeftIndentLine, 0, LeftIndentLine, ClientSize.Height);
        // }
        //
        // //draw preferred line width
        // if (PreferredLineWidth > 0) {
        //     e.Graphics.DrawLine(servicePen,
        //         new Point(LeftIndent + Paddings.Left + PreferredLineWidth * CharWidth - HorizontalScroll.Value + 1, 0),
        //         new Point(LeftIndent + Paddings.Left + PreferredLineWidth * CharWidth - HorizontalScroll.Value + 1, Height));
        // }
        //
        // int firstChar = (Math.Max(0, HorizontalScroll.Value - Paddings.Left)) / CharWidth;
        // int lastChar = (HorizontalScroll.Value + ClientSize.Width) / CharWidth;
        //draw chars

        // int startLine = YtoLineIndex(VerticalScroll);
        // int iLine;
        // for (iLine = startLine; iLine < lines.Count; iLine++) {
        //     Line line = lines[iLine];
        //     LineInfo lineInfo = lineInfos[iLine];
        //
        //     if (lineInfo.startY > VerticalScroll.Value + ClientSize.Height) {
        //         break;
        //     }
        //
        //     if (lineInfo.startY + lineInfo.WordWrapStringsCount * CharHeight < VerticalScroll.Value) {
        //         continue;
        //     }
        //
        //     if (lineInfo.VisibleState == VisibleState.Hidden) {
        //         continue;
        //     }
        //
        //     int y = lineInfo.startY - VerticalScroll.Value;
        //
        //     e.Graphics.SmoothingMode = SmoothingMode.None;
        //     //draw line background
        //     if (lineInfo.VisibleState == VisibleState.Visible) {
        //         if (line.BackgroundBrush != null) {
        //             e.Graphics.FillRectangle(line.BackgroundBrush,
        //                 new Rectangle(leftTextIndent, y, textWidth, CharHeight * lineInfo.WordWrapStringsCount));
        //         }
        //     }
        //
        //     //draw current line background
        //     if (CurrentLineColor != Color.Transparent && iLine == Selection.Start.iLine) {
        //         e.Graphics.FillRectangle(currentLineBrush, new Rectangle(leftTextIndent, y, ClientSize.Width, CharHeight));
        //     }
        //
        //     //draw changed line marker
        //     if (ChangedLineBgColor != Color.Transparent && line.IsChanged) {
        //         e.Graphics.FillRectangle(changedLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
        //     }
        //
        //     if (PlayingLineBgColor != Color.Transparent && iLine == PlayingLine) {
        //         e.Graphics.FillRectangle(activeLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
        //     }
        //
        //     //draw savestate line background
        //     if (iLine == SaveStateLine) {
        //         if (SaveStateLine == PlayingLine) {
        //             e.Graphics.FillRectangle(saveStateLineBrush, new RectangleF(-10, y, 15, CharHeight + 1));
        //         } else {
        //             e.Graphics.FillRectangle(saveStateLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
        //         }
        //     }
        //
        //     if (!string.IsNullOrEmpty(currentLineSuffix) && iLine == PlayingLine) {
        //         using var lineNumberBrush = new SolidBrush(currentTextColor);
        //         int offset = PlatformUtils.Mono ? 20 : 10;
        //         SizeF size = e.Graphics.MeasureString(currentLineSuffix, Font, 0, StringFormat.GenericTypographic);
        //         e.Graphics.DrawString(currentLineSuffix, Font, lineNumberBrush,
        //             new RectangleF(ClientSize.Width - size.Width - offset, y, size.Width, CharHeight), StringFormat.GenericTypographic);
        //     }
        //
        //     e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        //     //OnPaint event
        //     if (lineInfo.VisibleState == VisibleState.Visible) {
        //         OnPaintLine(new PaintLineEventArgs(iLine, new Rectangle(LeftIndent, y, Width, CharHeight * lineInfo.WordWrapStringsCount),
        //             e.Graphics, e.ClipRectangle));
        //     }
        //
        //     //draw line number
        //     if (ShowLineNumbers) {
        //         Color lineNumberColor = LineNumberColor;
        //         if (iLine == PlayingLine) {
        //             lineNumberColor = PlayingLineTextColor;
        //         } else if (iLine == SaveStateLine) {
        //             lineNumberColor = SaveStateTextColor;
        //         } else if (line.IsChanged) {
        //             lineNumberColor = ChangedLineTextColor;
        //         }
        //
        //         using var lineNumberBrush = new SolidBrush(lineNumberColor);
        //         if (PlatformUtils.Wine) {
        //             e.Graphics.DrawString((iLine + lineNumberStartValue).ToString().PadLeft(LinesCount.ToString().Length, ' '), Font,
        //                 lineNumberBrush,
        //                 new RectangleF(4, y, LeftIndent + 8, CharHeight),
        //                 new StringFormat(StringFormatFlags.DirectionRightToLeft));
        //         } else {
        //             e.Graphics.DrawString((iLine + lineNumberStartValue).ToString(), Font, lineNumberBrush,
        //                 new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight),
        //                 new StringFormat(StringFormatFlags.DirectionRightToLeft));
        //         }
        //     }
        //
        //     //create markers
        //     if (lineInfo.VisibleState == VisibleState.StartOfHiddenBlock) {
        //         visibleMarkers.Add(new ExpandFoldingMarker(iLine, new Rectangle(LeftIndentLine - 4, y + CharHeight / 2 - 3, 8, 8)));
        //     }
        //
        //     if (!string.IsNullOrEmpty(line.FoldingStartMarker) && lineInfo.VisibleState == VisibleState.Visible &&
        //         string.IsNullOrEmpty(line.FoldingEndMarker)) {
        //         visibleMarkers.Add(new CollapseFoldingMarker(iLine, new Rectangle(LeftIndentLine - 4, y + CharHeight / 2 - 3, 8, 8)));
        //     }
        //
        //     if (lineInfo.VisibleState == VisibleState.Visible && !string.IsNullOrEmpty(line.FoldingEndMarker) &&
        //         string.IsNullOrEmpty(line.FoldingStartMarker)) {
        //         e.Graphics.DrawLine(servicePen, LeftIndentLine, y + CharHeight * lineInfo.WordWrapStringsCount - 1, LeftIndentLine + 4,
        //             y + CharHeight * lineInfo.WordWrapStringsCount - 1);
        //     }
        //
        //     //draw wordwrap strings of line
        //     for (int iWordWrapLine = 0; iWordWrapLine < lineInfo.WordWrapStringsCount; iWordWrapLine++) {
        //         y = lineInfo.startY + iWordWrapLine * CharHeight - VerticalScroll.Value;
        //         try {
        //             //draw chars
        //             DrawLineChars(e, firstChar, lastChar, iLine, iWordWrapLine, LeftIndent + Paddings.Left - HorizontalScroll.Value, y);
        //         } catch (ArgumentOutOfRangeException) {
        //             // ignore
        //         }
        //     }
        // }

        //
        // int endLine = iLine - 1;
        //
        // //draw folding lines
        // if (ShowFoldingLines) {
        //     DrawFoldingLines(e, startLine, endLine);
        // }
        //
        // //draw column selection
        // if (Selection.ColumnSelectionMode) {
        //     if (SelectionStyle.BackgroundBrush is SolidBrush) {
        //         var color = ((SolidBrush) SelectionStyle.BackgroundBrush).Color;
        //         var p1 = PlaceToPoint(Selection.Start);
        //         var p2 = PlaceToPoint(Selection.End);
        //         using (var pen = new Pen(color)) {
        //             e.Graphics.DrawRectangle(pen,
        //                 Rectangle.FromLTRB(Math.Min(p1.X, p2.X) - 1, Math.Min(p1.Y, p2.Y), Math.Max(p1.X, p2.X),
        //                     Math.Max(p1.Y, p2.Y) + CharHeight));
        //         }
        //     }
        // }
        //
        // //draw brackets highlighting
        // if (BracketsStyle != null && leftBracketPosition != null && rightBracketPosition != null) {
        //     BracketsStyle.Draw(e.Graphics, PlaceToPoint(leftBracketPosition.Start), leftBracketPosition);
        //     BracketsStyle.Draw(e.Graphics, PlaceToPoint(rightBracketPosition.Start), rightBracketPosition);
        // }
        //
        // if (BracketsStyle2 != null && leftBracketPosition2 != null && rightBracketPosition2 != null) {
        //     BracketsStyle2.Draw(e.Graphics, PlaceToPoint(leftBracketPosition2.Start), leftBracketPosition2);
        //     BracketsStyle2.Draw(e.Graphics, PlaceToPoint(rightBracketPosition2.Start), rightBracketPosition2);
        // }
        //
        // e.Graphics.SmoothingMode = SmoothingMode.None;
        // //draw folding indicator
        // if ((startFoldingLine >= 0 || endFoldingLine >= 0) && Selection.Start == Selection.End) {
        //     if (endFoldingLine < lineInfos.Count) {
        //         //folding indicator
        //         int startFoldingY = (startFoldingLine >= 0 ? lineInfos[startFoldingLine].startY : 0) -
        //             VerticalScroll.Value + CharHeight / 2;
        //         int endFoldingY = (endFoldingLine >= 0
        //             ? lineInfos[endFoldingLine].startY +
        //               (lineInfos[endFoldingLine].WordWrapStringsCount - 1) * CharHeight
        //             : (WordWrapLinesCount + 1) * CharHeight) - VerticalScroll.Value + CharHeight;
        //
        //         using (var indicatorPen = new Pen(Color.FromArgb(100, FoldingIndicatorColor), 4)) {
        //             e.Graphics.DrawLine(indicatorPen, LeftIndent - 5, startFoldingY, LeftIndent - 5, endFoldingY);
        //         }
        //     }
        // }
        //
        // //draw markers
        // foreach (VisualMarker m in visibleMarkers) {
        //     m.Draw(e.Graphics, servicePen);
        // }
        //
        // //draw caret
        // Point car = PlaceToPoint(Selection.Start);
        //
        // if ((Focused || IsDragDrop) && car.X >= LeftIndent && CaretVisible) {
        //     int carWidth = IsReplaceMode ? CharWidth : 1;
        //     //CreateCaret(Handle, 0, carWidth, CharHeight);
        //     NativeMethodsWrapper.SetCaretPos(car.X, car.Y);
        //     //ShowCaret(Handle);
        //     using (Pen pen = new(CaretColor)) {
        //         e.Graphics.DrawLine(pen, car.X, car.Y, car.X, car.Y + CharHeight - 1);
        //     }
        // } else {
        //     NativeMethodsWrapper.HideCaret(Handle);
        // }
        //
        // //draw disabled mask
        // if (!Enabled) {
        //     using (var brush = new SolidBrush(DisabledColor)) {
        //         e.Graphics.FillRectangle(brush, ClientRectangle);
        //     }
        // }
        //
        // //dispose resources
        // servicePen.Dispose();
        // changedLineBrush.Dispose();
        // activeLineBrush.Dispose();
        // indentBrush.Dispose();
        // currentLineBrush.Dispose();
        paddingBrush.Dispose();

        base.OnPaint(e);
    }
    
    // private int YtoLineIndex(int y) {
    //     int i = lineInfos.BinarySearch(new LineInfo(-10), new LineYComparer(y));
    //     i = i < 0 ? -i - 2 : i;
    //     if (i < 0) {
    //         return 0;
    //     }
    //     
    //     if (i > lines.Count - 1) {
    //         return lines.Count - 1;
    //     }
    //     
    //     return i;
    // }
    
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

        Console.WriteLine(string.Join("|",softSnapColumns));
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