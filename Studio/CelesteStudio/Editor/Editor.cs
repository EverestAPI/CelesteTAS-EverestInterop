using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio;

public class Editor : Drawable {
    
    private struct CaretPosition { public int Row, Col; }
    
    private CaretPosition Caret = new() { Row = 0, Col = 0 };
    private readonly List<string> Lines = [];
    
    private Font font = new Font(new FontFamily("JetBrains Mono"), 12.0f);
    private readonly Scrollable scrollable;
    
    public Editor(Scrollable scrollable) {
        this.scrollable = scrollable;

        CanFocus = true;
        BackgroundColor = Colors.Black;
        
        Recalc();
    }
    
    // private bool needRecalc = true;
    private void Recalc() {
        // Need at least 1 line
        if (Lines.Count == 0)
            Lines.Add("");
        
        // Snap caret
        Caret.Row = Math.Clamp(Caret.Row, 0, Lines.Count - 1);
        Caret.Col = Math.Clamp(Caret.Col, 0, Lines[Caret.Row].Length);
        
        // Calculate bounds
        Width = 0;
        Height = 0;

        foreach (var line in Lines) {
            var size = font.MeasureString(line);            
            Width = Math.Max(Width, (int)size.Width);
            Height += (int)size.Height;
        }
        
        Invalidate();
        
        // Bring caret into view
        const float xLookAhead = 1;
        const float yLookAhead = 1;
        
        float carX = font.MeasureString(Lines[Caret.Row][..Caret.Col]).Width;
        float carY = font.LineHeight * Caret.Row;
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
        Console.WriteLine(e.Text);
        
        Lines[Caret.Row] = Lines[Caret.Row].Insert(Caret.Col, e.Text);
        Caret.Col += e.Text.Length;
        
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
        foreach (var line in Lines) {
            e.Graphics.DrawText(font, Colors.White, 0.0f, y, line);
            y += font.LineHeight;
        }
        
        if (HasFocus) {
            // Draw caret
            float carX = font.MeasureString(Lines[Caret.Row][..Caret.Col]).Width;
            float carY = font.LineHeight * Caret.Row;
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
}