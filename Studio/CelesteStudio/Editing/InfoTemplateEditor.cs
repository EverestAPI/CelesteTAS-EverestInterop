using CelesteStudio.Editing.AutoCompletion;
using CelesteStudio.Util;
using Eto.Forms;
using SkiaSharp;
using System;

namespace CelesteStudio.Editing;

public class InfoTemplateEditor : TextEditor {
    public InfoTemplateEditor(Document document, Scrollable scrollable) : base(document, scrollable) {
        autoCompleteMenu = new InfoTemplateAutoCompleteMenu(this);
    }

    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0])) {
            return;
        }

        Document.Caret = ClampCaret(Document.Caret);

        // Attempt to auto-close special syntax pairs
        if (e.Text == "{") {
            string line = Document.Lines[Document.Caret.Row];
            int nextOpenCurly  = line.IndexOf('{', startIndex: Document.Caret.Col);
            int nextCloseCurly = line.IndexOf('}', startIndex: Document.Caret.Col);

            if (nextCloseCurly == -1 || nextCloseCurly > nextOpenCurly) {
                using var __ = Document.Update();

                if (!Document.Selection.Empty) {
                    RemoveRange(Document.Selection.Min, Document.Selection.Max);
                    Document.Caret = Document.Selection.Min;
                    Document.Selection.Clear();
                }

                Document.Insert("{}");
                DesiredVisualCol = Document.Caret.Col -= 1;

                Recalc();
                autoCompleteMenu?.Refresh();
                return;
            }
        } else if (e.Text == "[") {
            string line = Document.Lines[Document.Caret.Row];
            int nextOpenCurly         = line.IndexOf('{', startIndex: Document.Caret.Col);
            int nextCloseCurly        = line.IndexOf('}', startIndex: Document.Caret.Col);
            int nextOpenDoubleSquare  = line.IndexOf("[[", startIndex: Document.Caret.Col, StringComparison.Ordinal);
            int nextCloseDoubleSquare = line.IndexOf("]]", startIndex: Document.Caret.Col, StringComparison.Ordinal);
            bool nextToExisting = Document.Caret.Col > 0 && line[Document.Caret.Col - 1] == '[' || Document.Caret.Col + 1 < line.Length && line[Document.Caret.Col + 1] == '[';

            if (nextCloseCurly != -1 && (nextOpenCurly == -1 || nextOpenCurly > nextCloseCurly)) {
                // We're inside a query-block, so use single square brackets
                int nextOpenSquare  = line.IndexOf('[', startIndex: Document.Caret.Col);
                int nextCloseSquare = line.IndexOf(']', startIndex: Document.Caret.Col);

                if (nextCloseSquare == -1 || nextCloseSquare > nextOpenSquare) {
                    using var __ = Document.Update();

                    if (!Document.Selection.Empty) {
                        RemoveRange(Document.Selection.Min, Document.Selection.Max);
                        Document.Caret = Document.Selection.Min;
                        Document.Selection.Clear();
                    }

                    Document.Insert("[]");
                    DesiredVisualCol = Document.Caret.Col -= 1;

                    Recalc();
                    autoCompleteMenu?.Refresh();
                } else {
                    base.OnTextInput(e);
                }
                return;
            }

            if ((nextCloseDoubleSquare == -1 || nextCloseDoubleSquare > nextOpenDoubleSquare) && !nextToExisting) {
                using var __ = Document.Update();

                if (!Document.Selection.Empty) {
                    RemoveRange(Document.Selection.Min, Document.Selection.Max);
                    Document.Caret = Document.Selection.Min;
                    Document.Selection.Clear();
                }

                Document.Insert("[[]]");
                DesiredVisualCol = Document.Caret.Col -= 2;

                Recalc();
                autoCompleteMenu?.Refresh();
                return;
            }
        } else if (e.Text == "|") {
            string line = Document.Lines[Document.Caret.Row];
            bool nextToExisting = Document.Caret.Col > 0 && line[Document.Caret.Col - 1] == '|' || Document.Caret.Col + 1 < line.Length && line[Document.Caret.Col + 1] == '|';

            int amountBefore = 0;
            for (int i = Document.Caret.Col - 2; i >= 0; i--) {
                if (line[i] == '|' && line[i + 1] == '|') {
                    amountBefore++;
                    i--;
                }
            }

            if (amountBefore % 2 == 0 && !nextToExisting) {
                using var __ = Document.Update();

                if (!Document.Selection.Empty) {
                    RemoveRange(Document.Selection.Min, Document.Selection.Max);
                    Document.Caret = Document.Selection.Min;
                    Document.Selection.Clear();
                }

                Document.Insert("||||");
                DesiredVisualCol = Document.Caret.Col -= 2;

                Recalc();
                autoCompleteMenu?.Refresh();
                return;
            }
        }

        base.OnTextInput(e);
    }

    private enum LineMode { Regular, TargetQuery, Lua, Table }
    protected override void DrawLine(SKCanvas canvas, string line, float x, float y) {
        var lineSpan = line.AsSpan();

        var mode = LineMode.Regular;
        int modeStart = 0;
        bool inTable = false;
        int tableStart = 0;

        for (int i = 0; i < lineSpan.Length; i++) {
            char c = lineSpan[i];
            char n = i + 1 != lineSpan.Length ? lineSpan[i + 1] : '\0';
            char p = i > 0 ? lineSpan[i - 1] : '\0';
            char pp = i > 1 ? lineSpan[i - 2] : '\0';

            var nextMode = mode;
            if (c == '{') {
                nextMode = LineMode.TargetQuery;
            } else if (c == '[' && n == '[') {
                nextMode = LineMode.Lua;
            } else if (!inTable && c == '|' && n == '|') {
                nextMode = LineMode.Table;
                inTable = true;
                tableStart = i;
            } else if (mode == LineMode.TargetQuery && p == '}') {
                nextMode = inTable ? LineMode.Table :LineMode.Regular;
            } else if (mode == LineMode.Lua && p == ']' && pp == ']') {
                nextMode = inTable ? LineMode.Table : LineMode.Regular;
            } else if (inTable && tableStart + "||||".Length <= i && p == '|' && pp == '|') {
                nextMode = LineMode.Regular;
                inTable = false;
            }

            if (nextMode != mode) {
                var subSpan = lineSpan[modeStart..i];
                var paint = mode switch {
                    LineMode.Regular => Settings.Instance.Theme.StatusFgPaint,
                    LineMode.TargetQuery => Settings.Instance.Theme.CommandPaint.ForegroundColor,
                    LineMode.Lua => Settings.Instance.Theme.ActionPaint.ForegroundColor,
                    LineMode.Table => Settings.Instance.Theme.FramePaint.ForegroundColor,
                    _ => throw new ArgumentOutOfRangeException()
                };

                canvas.DrawText(subSpan, x + modeStart * Font.CharWidth(), y + Font.Offset(), Font, paint);
                mode = nextMode;
                modeStart = i;
            }
        }

        var lastSpan = lineSpan[modeStart..];
        var lastPaint = mode switch {
            LineMode.Regular => Settings.Instance.Theme.StatusFgPaint,
            LineMode.TargetQuery => Settings.Instance.Theme.CommandPaint.ForegroundColor,
            LineMode.Lua => Settings.Instance.Theme.ActionPaint.ForegroundColor,
            LineMode.Table => Settings.Instance.Theme.FramePaint.ForegroundColor,
            _ => throw new ArgumentOutOfRangeException()
        };
        canvas.DrawText(lastSpan, x + modeStart * Font.CharWidth(), y + Font.Offset(), Font, lastPaint);
    }
}
