using System;
using System.Collections.Generic;
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
        scrollable.ScrollPosition = new Point((int)carX + 50, (int)carY);
        scrollable.Padding = new(0);
        
        Console.WriteLine($"w: {Width} h: {Height} x: {scrollable.ScrollPosition.X}");
        
        // needRecalc = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Keys.A)
        {
            e.Handled = true;
            return;
        } 
            
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e) {
        var line = Document.Lines[Document.Caret.Row];
        
        char typedCharacter = char.ToUpper(e.Text[0]);
        int leadingSpaces = line.Length - line.TrimStart().Length;
        bool startOfLine = Document.Caret.Col <= leadingSpaces + 1;
        
        // If it's an action line, handle it ourselves
        if (ActionLine.TryParse(line, out var actionLine) && e.Text.Length == 1) 
        {
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
            int featherStartColumn = GetColumnOfAction(actionLine, Actions.Feather);
            if (featherStartColumn >= 1 && Document.Caret.Col > featherStartColumn && (typedCharacter is '.' or ',' or (>= '0' and <= '9'))) {
                line = line.Insert(Document.Caret.Col - 1, typedCharacter.ToString());
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
                    Document.Caret.Col = ActionLine.MaxFramesDigits + 1;
                } else if (typedAction is Actions.LeftDashOnly or Actions.RightDashOnly or Actions.UpDashOnly or Actions.DownDashOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.DashOnly) + actionLine.Actions.GetDashOnly().Count();
                } else if (typedAction is Actions.LeftMoveOnly or Actions.RightMoveOnly or Actions.UpMoveOnly or Actions.DownMoveOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.MoveOnly) + actionLine.Actions.GetMoveOnly().Count();
                } else {
                    Document.Caret.Col = ActionLine.MaxFramesDigits + 1;
                }
            }
            // If the key we entered is a number
            else if (typedCharacter is >= '0' and <= '9') {
                int cursorPosition = Document.Caret.Col - leadingSpaces - 1;

                // Entering a zero at the start should do nothing but format
                if (cursorPosition == 0 && typedCharacter == '0') {
                    Document.Caret.Col = ActionLine.MaxFramesDigits - actionLine.Frames.Digits() + 1;
                }
                // If we have a 0, just force the new number
                else if (actionLine.Frames == 0) {
                    actionLine.Frames = int.Parse(typedCharacter.ToString());
                    Document.Caret.Col = ActionLine.MaxFramesDigits + 1;
                } else {
                    // Jam the number into the current position
                    string leftOfCursor = line[..(Document.Caret.Col - 1)];
                    string rightOfCursor = line[(Document.Caret.Col - 1)..];
                    line = $"{leftOfCursor}{typedCharacter}{rightOfCursor}";

                    // Reparse
                    ActionLine.TryParse(line, out actionLine);

                    // Cap at max frames
                    if (actionLine.Frames > ActionLine.MaxFrames) {
                        actionLine.Frames = ActionLine.MaxFrames;
                        Document.Caret.Col = ActionLine.MaxFramesDigits + 1;
                    } else {
                        Document.Caret.Col = ActionLine.MaxFramesDigits - actionLine.Frames.Digits() + cursorPosition + 2;
                    }
                }
            }

            FinishEdit:
            Document.Replace(Document.Caret.Row, actionLine.ToString());

        }
        // Start an action line if we should
        else if (startOfLine && e.Text.Length == 1 && e.Text[0] is >= '0' and <= '9') 
        {
            string newLine = typedCharacter.ToString().PadLeft(ActionLine.MaxFramesDigits);
            if (line.Trim().Length == 0)
                Document.Replace(Document.Caret.Row, newLine);
            else
                Document.Insert(new Document.CaretPosition(Document.Caret.Row, 0), newLine + "\n");
            Document.Caret.Col = ActionLine.MaxFramesDigits + 1;

        }
        // Just write it as text
        else
        {
            Document.Insert(e.Text);
        }
        
        Recalc();
    }
    
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
        float y = 0.0f;
        foreach (var line in Document.Lines) {
            e.Graphics.DrawText(font, Colors.White, 0.0f, y, line);
            y += font.LineHeight;
        }
        
        if (HasFocus) {
            // Draw caret
            float carX = font.MeasureString(Document.Lines[Document.Caret.Row][..Document.Caret.Col]).Width;
            float carY = font.LineHeight * Document.Caret.Row;
            using (Pen pen = new(Colors.Red)) {
                e.Graphics.DrawLine(pen, carX, carY, carX, carY + font.LineHeight - 1);
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

        Console.WriteLine($"Line '{actionLine}' Action: '{action}' = {ActionLine.MaxFramesDigits + (index + 1) * 2 + additionalOffset}");
        return ActionLine.MaxFramesDigits + (index + 1) * 2 + additionalOffset;
    }

}