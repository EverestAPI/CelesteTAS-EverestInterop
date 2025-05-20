using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eto.Drawing;
using SkiaSharp;
using StudioCommunication;

namespace CelesteStudio.Editing;

public enum StyleType : byte {
    Action,
    Angle,
    Breakpoint,
    ForceStop,
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

    private readonly Dictionary<string, LineStyle> cache = [];
    private StylePaint[] styles = [];

    private readonly SKFont regularFont;
    private readonly SKFont boldFont;
    private readonly SKFont italicFont;
    private readonly SKFont boldItalicFont;

    public SyntaxHighlighter(SKFont regularFont, SKFont boldFont, SKFont italicFont, SKFont boldItalicFont) {
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
        styles = [theme.ActionPaint, theme.AnglePaint, theme.BreakpointPaint, theme.ForceStopBreakpointPaint, theme.SavestateBreakpointPaint, theme.DelimiterPaint, theme.CommandPaint, theme.CommentPaint, theme.FramePaint];
    }

    public void DrawLine(SKCanvas canvas, float x, float y, string line) {
        float xOff = 0.0f;

        foreach (var segment in GetLineStyle(line).Segments) {
            // Some ranges are out-of-bounds for easier generation
            if (segment.StartIdx >= line.Length) {
                continue;
            }

            var style = styles[(int)segment.Type];

            var font = style.FontStyle switch {
                FontStyle.None => regularFont,
                FontStyle.Bold => boldFont,
                FontStyle.Italic => italicFont,
                FontStyle.Bold | FontStyle.Italic => boldItalicFont,
                _ => throw new UnreachableException(),
            };

            string str = line[segment.StartIdx..(segment.EndIdx + 1)];
            float width = font.MeasureWidth(str);

            if (style.BackgroundColor is { } bgColor) {
                canvas.DrawRect(x + xOff, y, width, font.LineHeight(), bgColor);
            }

            canvas.DrawText(str, x + xOff, y + font.Offset(), font, style.ForegroundColor);

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
        string trimmed = line.TrimStart();

        if (trimmed.StartsWith("#")) {
            return new LineStyle { Segments = [new LineStyle.Segment { StartIdx = 0, EndIdx = line.Length - 1, Type = StyleType.Comment } ] };
        }

        if (trimmed.StartsWith("***")) {
            int idx = line.IndexOf("***", StringComparison.Ordinal);
            int currIdx = idx + "***".Length;

            int? forceIdx = currIdx < line.Length && char.ToUpper(line[currIdx]) == '!' ? currIdx++ : null;
            int? saveIdx  = currIdx < line.Length && char.ToUpper(line[currIdx]) == 'S' ? currIdx++ : null;

            var segments = new LineStyle.Segment[1
                + (forceIdx != null ? 1 : 0)
                + (saveIdx != null ? 1 : 0)
                + (line.Length > currIdx ? 1 : 0)];

            segments[0] = new LineStyle.Segment { StartIdx = 0, EndIdx = (forceIdx ?? saveIdx ?? line.Length) - 1, Type = StyleType.Breakpoint };

            int segmentIdx = 1;
            if (forceIdx != null) {
                segments[segmentIdx++] = new LineStyle.Segment { StartIdx = forceIdx.Value, EndIdx = forceIdx.Value, Type = StyleType.ForceStop };
            }
            if (saveIdx != null) {
                segments[segmentIdx++] = new LineStyle.Segment { StartIdx = saveIdx.Value, EndIdx = saveIdx.Value, Type = StyleType.Savestate };
            }
            if (line.Length > currIdx) {
                segments[segmentIdx] = new LineStyle.Segment { StartIdx = currIdx, EndIdx = line.Length - 1, Type = StyleType.Breakpoint };
            }

            return new LineStyle { Segments = segments };
        }

        if (ActionLine.TryParse(line, out _)) {
            var segments = new List<LineStyle.Segment> {
                new() { StartIdx = 0, EndIdx = Math.Min(line.Length - 1, ActionLine.MaxFramesDigits - 1), Type = StyleType.Frame }
            };

            for (int idx = ActionLine.MaxFramesDigits; idx < line.Length; idx++) {
                StyleType style;
                char c = line[idx];

                if (c == ActionLine.Delimiter) {
                    style = StyleType.Delimiter;
                } else if (char.IsDigit(c) || c == '.') {
                    style = StyleType.Angle;
                } else {
                    style = StyleType.Action;
                }

                segments.Add(new LineStyle.Segment { StartIdx = idx, EndIdx = idx, Type = style });
            }

            return new LineStyle { Segments = segments.ToArray() };
        }

        return new LineStyle { Segments = [new LineStyle.Segment { StartIdx = 0, EndIdx = line.Length - 1, Type = StyleType.Command } ] };
    }
}
