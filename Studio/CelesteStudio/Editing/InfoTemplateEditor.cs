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

    private enum LineMode { Regular, TargetQuery, Lua }
    protected override void DrawLine(SKCanvas canvas, string line, float x, float y) {
        var lineSpan = line.AsSpan();

        var mode = LineMode.Regular;
        int modeStart = 0;

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
            } else if (mode == LineMode.TargetQuery && p == '}') {
                nextMode = LineMode.Regular;
            } else if (mode == LineMode.Lua && p == ']' && pp == ']') {
                nextMode = LineMode.Regular;
            }

            if (nextMode != mode) {
                var subSpan = lineSpan[modeStart..i];
                var paint = mode switch {
                    LineMode.Regular => Settings.Instance.Theme.StatusFgPaint,
                    LineMode.TargetQuery => Settings.Instance.Theme.CommandPaint.ForegroundColor,
                    LineMode.Lua => Settings.Instance.Theme.ActionPaint.ForegroundColor,
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
            _ => throw new ArgumentOutOfRangeException()
        };
        canvas.DrawText(lastSpan, x + modeStart * Font.CharWidth(), y + Font.Offset(), Font, lastPaint);
    }
}
