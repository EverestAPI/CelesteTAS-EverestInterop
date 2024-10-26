using System;
using System.Collections.Generic;
using System.Diagnostics;
using CelesteStudio.Data;
using CelesteStudio.Util;
using Eto.Drawing;
using SkiaSharp;

namespace CelesteStudio.Editing;

public enum StyleType : byte {
    Action,
    Angle,
    Breakpoint,
    Savestate,
    Delimiter,
    Command,
    Comment,
    Frame,
}

public struct LineStyle {
    public struct Segment {
        public int StartIdx, EndIdx;
        public StyleType Type;
    }

    public Segment[] Segments;
}

public class SyntaxHighlighter {
    private const int MaxCacheSize = 32767;

    private readonly Dictionary<string, LineStyle> cache = new();
    private Style[] styles = [];

    private readonly Font regularFont;
    private readonly Font boldFont;
    private readonly Font italicFont;
    private readonly Font boldItalicFont;

    public SyntaxHighlighter(Font regularFont, Font boldFont, Font italicFont, Font boldItalicFont) {
        LoadTheme(Settings.Instance.Theme);
        Settings.ThemeChanged += () => LoadTheme(Settings.Instance.Theme);

        this.regularFont = regularFont;
        this.boldFont = boldFont;
        this.italicFont = italicFont;
        this.boldItalicFont = boldItalicFont;
    }

    private void LoadTheme(Theme theme) {
        cache.Clear();
        // IMPORTANT: Must be the same order as the StyleType enum!
        styles = [theme.Action, theme.Angle, theme.Breakpoint, theme.SavestateBreakpoint, theme.Delimiter, theme.Command, theme.Comment, theme.Frame];
    }

    public struct DrawLineOptions() {
        // Required to prevent a weird macOS font crash
        public bool MeasureReal = false;

        // Region which should be underlined
        public int UnderlineStart = -1;
        public int UnderlineEnd = -1;
    }

    public void DrawLine(SKCanvas canvas, float x, float y, string line, DrawLineOptions? options = null) {
        options ??= new();

        float xOff = 0.0f;

        foreach (var segment in GetLineStyle(line).Segments) {
            // Some ranges are out-of-bounds for easier generation
            if (segment.StartIdx >= line.Length) {
                continue;
            }

            var style = styles[(int)segment.Type];

            // var font = style.FontStyle switch {
            //     FontStyle.None => regularFont,
            //     FontStyle.Bold => boldFont,
            //     FontStyle.Italic => italicFont,
            //     FontStyle.Bold | FontStyle.Italic => boldItalicFont,
            //     _ => throw new UnreachableException(),
            // };
            // TODO: Respect font style
            var font = FontManager.SKEditorFontRegular;

            string str = line[segment.StartIdx..(segment.EndIdx + 1)];
            float width = font.MeasureWidth(str);

            if (style.BackgroundColor is { } bgColor) {
                using var paint = new SKPaint();
                paint.Color = bgColor.ToSkia();
                paint.Style = SKPaintStyle.Fill;
                paint.IsAntialias = true;
                canvas.DrawRect(x + xOff, y, width, font.LineHeight(), paint);
            }

            int underlineStart = Math.Max(segment.StartIdx, options.Value.UnderlineStart);
            int underlineEnd = Math.Min(segment.EndIdx, options.Value.UnderlineEnd);

            // TODO: Handle underlines
            // if (underlineStart <= underlineEnd && underlineStart >= segment.StartIdx && underlineEnd <= segment.EndIdx)
            // {
            //     var underlineFont = font.WithFontDecoration(FontDecoration.Underline);
            //
            //     string left = line[..underlineStart];
            //     string middle = line[underlineStart..(underlineEnd + 1)];
            //     string right = line[(underlineEnd + 1)..];
            //
            //     canvas.DrawText(font,          style.ForegroundColor, x + xOff, y, left);
            //     canvas.DrawText(underlineFont, style.ForegroundColor, x + xOff + font.MeasureWidth(left), y, middle);
            //     canvas.DrawText(font,          style.ForegroundColor, x + xOff + font.MeasureWidth(left) + underlineFont.MeasureWidth(middle), y, right);
            // } else {
                using var textPaint = new SKPaint();
                textPaint.Color = style.ForegroundColor.ToSkia();
                // textPaint.Style = SKPaintStyle.Fill;
                textPaint.IsAntialias = true;
                canvas.DrawText(str, x + xOff, y - font.Metrics.Ascent, font, textPaint);
            // }

            xOff += width;
        }
    }

    private LineStyle GetLineStyle(string line) {
        if (cache.TryGetValue(line, out var style))
            return style;

        // Don't do any fancy invalidation, just clear and regenerate once it's too large
        if (cache.Count >= MaxCacheSize)
            cache.Clear();

        style = ComputeLineStyle(line);
        cache.Add(line, style);

        return style;
    }

    private LineStyle ComputeLineStyle(string line) {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("#"))
            return new LineStyle { Segments = [new LineStyle.Segment { StartIdx = 0, EndIdx = line.Length - 1, Type = StyleType.Comment } ] };

        if (trimmed.StartsWith("***")) {
            int idx = line.IndexOf("***", StringComparison.Ordinal);
            if (idx + 3 < line.Length && char.ToLower(line[idx + 3]) == 's') {
                // Savestate breakpoint
                return new LineStyle { Segments = [
                    new LineStyle.Segment { StartIdx = 0, EndIdx = idx + 2, Type = StyleType.Breakpoint },
                    new LineStyle.Segment { StartIdx = idx + 3, EndIdx = idx + 3, Type = StyleType.Savestate },
                    new LineStyle.Segment { StartIdx = idx + 4, EndIdx = line.Length - 1, Type = StyleType.Breakpoint },
                ]};
            } else {
                // Regular breakpoint
                return new LineStyle { Segments = [
                    new LineStyle.Segment { StartIdx = 0, EndIdx = line.Length - 1, Type = StyleType.Breakpoint },
                ]};
            }
        }

        if (!ActionLine.TryParse(line, out _))
            return new LineStyle { Segments = [new LineStyle.Segment { StartIdx = 0, EndIdx = line.Length - 1, Type = StyleType.Command } ] };

        var segments = new List<LineStyle.Segment> {
            new() { StartIdx = 0, EndIdx = Math.Min(line.Length - 1, ActionLine.MaxFramesDigits - 1), Type = StyleType.Frame }
        };

        for (int idx = ActionLine.MaxFramesDigits; idx < line.Length; idx++) {
            StyleType style;
            char c = line[idx];

            if (c == ActionLine.Delimiter)
                style = StyleType.Delimiter;
            else if (char.IsDigit(c) || c == '.')
                style = StyleType.Angle;
            else
                style = StyleType.Action;

            segments.Add(new LineStyle.Segment { StartIdx = idx, EndIdx = idx, Type = style });
        }

        return new LineStyle { Segments = segments.ToArray() };
    }
}
