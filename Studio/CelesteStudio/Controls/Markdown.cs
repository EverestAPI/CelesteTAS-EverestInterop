using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Markdig;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SkiaSharp;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CelesteStudio.Controls;

public class Markdown : SkiaDrawable {
    private readonly MarkdownDocument Document;
    private readonly SkiaRenderer Renderer;

    public Markdown(string content) {
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .Build();

        Document = Markdig.Markdown.Parse(content, pipeline);

        Renderer = new SkiaRenderer();
        Renderer.ObjectWriteBefore += (_, obj) => Console.WriteLine($"Render: '{obj}' ({obj.GetType()})");
        // Renderer.Render(Document);

        Padding = 10;
    }

    public override void Draw(SKSurface surface) {
        // surface.Canvas.DrawText("Hallo Welt", 0, 0 + FontManager.SKStatusFont.Offset(), FontManager.SKStatusFont, new SKPaint(FontManager.SKStatusFont) { Color = SKColors.White });

        Renderer.Reset(surface, Width - Padding.Horizontal);
        Renderer.Render(Document);
        //
        // Size = new Size((int) Renderer.Width, (int) Renderer.Height);
        // Size = new Size(100, 200);

        // Width = (int) Renderer.Width;
        // Height = (int) Renderer.Height;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        foreach (var entry in Renderer.ActionBoxes) {
            if (entry.Region.Contains(e.Location.X - Padding.Left, e.Location.Y - Padding.Top)) {
                Cursor = Cursors.Pointer;
                return;
            }
        }

        Cursor = null;
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        foreach (var entry in Renderer.ActionBoxes) {
            if (entry.Region.Contains(e.Location.X - Padding.Left, e.Location.Y - Padding.Top)) {
                entry.OnClick();
                return;
            }
        }
    }

    private class SkiaRenderer : RendererBase {
        public struct StyleConfig(SKFont font, SKColor color, SKTextAlign align, FontStyle fontStyle, FontDecoration fontDecoration) {
            public readonly SKFont Font = font;
            public readonly FontStyle FontStyle = fontStyle;
            public FontDecoration FontDecoration = fontDecoration;

            public delegate void DrawTextDelegate(ReadOnlySpan<char> text, ref float x, ref float y);
            public delegate void MeasureTextDelegate(ReadOnlySpan<char> text, ref float width);
            public DrawTextDelegate? ModifyDrawText;
            public MeasureTextDelegate? ModifyMeasureText;

            public readonly SKPaint Paint = new(font) {
                Color = color,
                TextAlign = align,
                IsAntialias = true,
                SubpixelText = true,
            };

            public StyleConfig WithFont(SKFont font) {
                return new StyleConfig(font, Paint.Color, Paint.TextAlign, FontStyle, FontDecoration);
            }
            public StyleConfig WithFontStyle(FontStyle fontStyle) {
                return new StyleConfig(new SKFont(SKFontManager.Default.MatchTypeface(Font.Typeface, fontStyle switch {
                    FontStyle.None => SKFontStyle.Normal,
                    FontStyle.Bold => SKFontStyle.Bold,
                    FontStyle.Italic => SKFontStyle.Italic,
                    FontStyle.Bold | FontStyle.Italic => SKFontStyle.BoldItalic,
                    _ => throw new ArgumentOutOfRangeException(nameof(fontStyle), fontStyle, null)
                }), Font.Size, Font.ScaleX, Font.SkewX), Paint.Color, Paint.TextAlign, fontStyle, FontDecoration);
            }
            public StyleConfig WithFontDecoration(FontDecoration fontDecoration) {
                return this with { FontDecoration = fontDecoration };
            }

            public StyleConfig Clone() {
                return new StyleConfig(Font, Paint.Color, Paint.TextAlign, FontStyle, FontDecoration);
            }
            public StyleConfig WithColor(SKColor color) {
                Paint.Color = color;
                return this;
            }
            public StyleConfig WithColor(SKColorF color) {
                Paint.ColorF = color;
                return this;
            }
            public StyleConfig WithAlign(SKTextAlign align) {
                Paint.TextAlign = align;
                return this;
            }
            public StyleConfig WithCallback(DrawTextDelegate? modifyDraw, MeasureTextDelegate? modifyMeasure) {
                return this with { ModifyDrawText = ModifyDrawText + modifyDraw, ModifyMeasureText = ModifyMeasureText + modifyMeasure };
            }
        }

        public float X { get; set; }
        public float Y { get; set; }

        public SKSurface Surface { get; private set; } = null!;
        public SKCanvas Canvas { get; private set; } = null!;

        public float Width { get; set; }
        public float Height { get; set; }

        public float MaxLineWidth { get; private set; }

        public readonly List<(SKRect Region, Action OnClick)> ActionBoxes = [];

        public StyleConfig CurrentStyle => styleStack.Peek();
        private Stack<StyleConfig> styleStack = new();

        public SkiaRenderer() {
            // Block renderers
            ObjectRenderers.Add(new ParagraphBlockRenderer());
            ObjectRenderers.Add(new HeadingBlockRenderer());
            ObjectRenderers.Add(new ListBlockRenderer());
            ObjectRenderers.Add(new CodeBlockRenderer());

            // Inline renderers
            ObjectRenderers.Add(new LiteralInlineRenderer());
            ObjectRenderers.Add(new LineBreakInlineRenderer());
            ObjectRenderers.Add(new EmphasisInlineRenderer());
            ObjectRenderers.Add(new CodeInlineRenderer());
            ObjectRenderers.Add(new LinkInlineRenderer());
        }

        public void Reset(SKSurface surface, float maxWidth) {
            X = 0.0f;
            Y = 0.0f;
            Width = 0.0f;
            Height = 0.0f;
            MaxLineWidth = maxWidth;

            Surface = surface;
            Canvas = surface.Canvas;

            ActionBoxes.Clear();

            styleStack.Clear();
            styleStack.Push(new StyleConfig(new SKFont(SKTypeface.Default, Settings.Instance.EditorFontSize * FontManager.DPI), SKColors.White, SKTextAlign.Left, FontStyle.None, FontDecoration.None));
        }

        public override object Render(MarkdownObject markdownObject) {
            Console.WriteLine($"Start");
            Write(markdownObject);
            Console.WriteLine($"Finish: {X} {Y}");

            return null!;
        }

        /// Pushes a new style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushStyle(StyleConfig config) => styleStack.Push(config);
        /// Pushes a new font
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushFont(SKFont font) => PushStyle(CurrentStyle.WithFont(font));

        /// Pops the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pop() => styleStack.Pop();

        /// Writes the text in the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteText(StringSlice text) => WriteText(text.AsSpan());

        /// Writes the text in the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteText(ReadOnlySpan<char> text) {
            var style = CurrentStyle;

            float advance = MeasureTextWithStyle(text, style);
            if (X + advance > MaxLineWidth) {
                // Warp lines
                var iterator = new WrapLineIterator(this, text, X, MaxLineWidth, style);

                // First iteration shouldn't start at a new line
                iterator.MoveNext();
                float firstRenderX = style.Paint.TextAlign switch {
                    SKTextAlign.Left => X,
                    SKTextAlign.Right => X + MaxLineWidth - MeasureTextWithStyle(iterator.Current, style),
                    SKTextAlign.Center => X + (MaxLineWidth - MeasureTextWithStyle(iterator.Current, style)) / 2.0f,
                    _ => throw new ArgumentOutOfRangeException()
                };
                DrawTextWithStyle(iterator.Current, firstRenderX, Y, style);

                // Iterate remaining lines
                while (iterator.MoveNext()) {
                    float wrapRenderX = style.Paint.TextAlign switch {
                        SKTextAlign.Left => 0.0f,
                        SKTextAlign.Right => MaxLineWidth - MeasureTextWithStyle(iterator.Current, style),
                        SKTextAlign.Center => (MaxLineWidth - MeasureTextWithStyle(iterator.Current, style)) / 2.0f,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    Y += style.Font.LineHeight();
                    DrawTextWithStyle(iterator.Current, wrapRenderX, Y, style);
                }

                X = MeasureTextWithStyle(iterator.Current, style);
                Width = MaxLineWidth;
            } else {
                float renderX = style.Paint.TextAlign switch {
                    SKTextAlign.Left => X,
                    SKTextAlign.Right => X + MaxLineWidth - advance,
                    SKTextAlign.Center => X + (MaxLineWidth - advance) / 2.0f,
                    _ => throw new ArgumentOutOfRangeException()
                };

                DrawTextWithStyle(text, renderX, Y, style);

                X += advance;
                Width = Math.Max(Width, X);
            }

            Height = Y + style.Font.LineHeight();
        }
        private void DrawTextWithStyle(ReadOnlySpan<char> text, float x, float y, StyleConfig style) {
            style.ModifyDrawText?.Invoke(text, ref x, ref y);

            y += style.Font.Offset();
            Canvas.DrawText(text, x, y, style.Font, style.Paint);

            if (style.FontDecoration == FontDecoration.None) {
                return;
            }

            float oldStrokeWidth = style.Paint.StrokeWidth;

            float width = style.Paint.MeasureText(text);
            var metrics = style.Font.Metrics;

            if (style.FontDecoration.HasFlag(FontDecoration.Strikethrough)) {
                float lineY = y + (metrics.StrikeoutPosition ?? style.Font.LineHeight() / -4.0f);
                style.Paint.StrokeWidth = metrics.StrikeoutThickness ?? 1.0f;
                Canvas.DrawLine(x, lineY, x + width, lineY, style.Paint);
            }
            if (style.FontDecoration.HasFlag(FontDecoration.Underline)) {
                float lineY = y + (metrics.UnderlinePosition ?? style.Font.LineHeight() / 10.0f);
                style.Paint.StrokeWidth = metrics.UnderlineThickness ?? 1.0f;
                Canvas.DrawLine(x, lineY, x + width, lineY, style.Paint);
            }

            style.Paint.StrokeWidth = oldStrokeWidth;
        }

        /// Measures the text in the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float MeasureText(StringSlice text) => MeasureText(text.AsSpan());

        /// Measures the text in the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float MeasureText(ReadOnlySpan<char> text) => MeasureTextWithStyle(text, CurrentStyle);

        private float MeasureTextWithStyle(ReadOnlySpan<char> text, StyleConfig style) {
            float width = style.Paint.MeasureText(text);
            style.ModifyMeasureText?.Invoke(text, ref width);
            return width;
        }

        /// Advances to the start of the next line
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextLine() {
            var font = CurrentStyle.Font;

            X = 0.0f;
            Y += font.LineHeight() + font.Metrics.Leading;
        }

        /// Writes the inlines of a leaf inline.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLeafInline(LeafBlock leafBlock) {
            Inline? inline = leafBlock.Inline!;
            while (inline != null) {
                Write(inline);
                inline = inline.NextSibling;
            }
        }

        /// Wraps lines which are too long
        private ref struct WrapLineIterator(SkiaRenderer renderer, ReadOnlySpan<char> text, float startOffset, float maxLineWidth, StyleConfig style) {
            private ReadOnlySpan<char> text = text;
            private int startIdx = 0;
            private bool firstIteration = true;

            public ReadOnlySpan<char> Current { get; private set; } = ReadOnlySpan<char>.Empty;
            public WrapLineIterator GetEnumerator() => this;

            public bool MoveNext() {
                if (text.Length == 0) {
                    Current = ReadOnlySpan<char>.Empty;
                    return false;
                }

                float maxWidth = firstIteration
                    ? maxLineWidth - startOffset
                    : maxLineWidth;

                int lastValidEnd = -1;
                for (int i = startIdx; i <= text.Length; i++) {
                    if (i != text.Length && !char.IsWhiteSpace(text[i])) {
                        continue;
                    }

                    float width = renderer.MeasureTextWithStyle(text[startIdx..i], style);
                    if (width <= maxWidth) {
                        lastValidEnd = i;

                        if (i != text.Length) {
                            continue;
                        }
                    }

                    if (firstIteration && lastValidEnd == -1) {
                        // Doesn't fit on first line. Advance to second
                        Current = ReadOnlySpan<char>.Empty;
                        firstIteration = false;
                        return true;
                    }

                    int end = lastValidEnd == -1
                        ? i // Single word doesn't fit into line
                        : lastValidEnd;

                    Current = text[startIdx..end];
                    startIdx = end + 1;
                    firstIteration = false;
                    return true;
                }

                return false;
            }
        }
    }

    #region Renderers

    private abstract class SkiaObjectRenderer<TObject> : MarkdownObjectRenderer<SkiaRenderer, TObject> where TObject : MarkdownObject;

    private const float BlockMarginBottom = 16.0f;
    private const float HeadingMarginTop = 24.0f;

    private class ParagraphBlockRenderer : SkiaObjectRenderer<ParagraphBlock> {
        protected override void Write(SkiaRenderer renderer, ParagraphBlock obj) {
            renderer.WriteLeafInline(obj);
            renderer.NextLine();
            renderer.Y += BlockMarginBottom;
        }
    }
    private class HeadingBlockRenderer : SkiaObjectRenderer<HeadingBlock> {
        protected override void Write(SkiaRenderer renderer, HeadingBlock obj) {
            float scale = obj.Level switch {
                1 => 2.0f,
                2 => 1.5f,
                3 => 1.25f,
                4 => 1.0f,
                5 => 0.875f,
                6 => 0.85f,
                _ => 1.0f,
            };

            var style = renderer.CurrentStyle;
            renderer.PushStyle(style
                .WithFont(style.Font.WithSize(style.Font.Size * scale))
                .WithAlign(obj.Level == 1 ? SKTextAlign.Center : SKTextAlign.Left));

            renderer.Y = Math.Max(renderer.Y, renderer.Height + HeadingMarginTop);
            renderer.WriteLeafInline(obj);
            renderer.NextLine();
            renderer.Y += BlockMarginBottom;

            if (obj.Level is 1 or 2) {
                renderer.Height += 0.3f * style.Font.Size;
                float lineY = renderer.Height;

                var linePaint = new SKPaint {
                    Color = style.Paint.Color.WithAlpha((byte) (style.Paint.Color.Alpha / 4)),
                    StrokeWidth = 1.5f,
                };

                renderer.Canvas.DrawLine(0.0f, lineY, renderer.MaxLineWidth, lineY, linePaint);
            }

            renderer.Pop();
        }
    }
    private class ListBlockRenderer : SkiaObjectRenderer<ListBlock> {
        protected override void Write(SkiaRenderer renderer, ListBlock block) {
            Console.WriteLine($"Bullet '{block.BulletType}' Start '{block.OrderedStart}' DefaultStart '{block.DefaultOrderedStart}'");
            var style = renderer.CurrentStyle;

            string? startIdxText = block.OrderedStart ?? block.DefaultOrderedStart;
            if (string.IsNullOrEmpty(startIdxText) || !int.TryParse(startIdxText, out int startIdx)) {
                startIdx = 1;
            }

            for (int idx = 0; idx < block.Count; idx++) {
                if (block.IsOrdered) {
                    renderer.X = 5.0f;
                    renderer.WriteText($"{idx + startIdx}. ");
                } else {
                    renderer.Canvas.DrawCircle(10.0f, renderer.Y + style.Font.LineHeight() / 1.75f, 2.5f, style.Paint);
                    renderer.X = 20.0f;
                }

                renderer.Write(block[idx]);
                renderer.Y = renderer.Height;
            }
            renderer.NextLine();
        }
    }
    private class CodeBlockRenderer : SkiaObjectRenderer<CodeBlock> {
        protected override void Write(SkiaRenderer renderer, CodeBlock code) {
            const float hPadding = 5.0f;
            const float vPadding = 2.5f;
            const float vSpacing = 5.0f;
            const float cornerRadius = 7.5f;

            int actualLines = code.Lines.Lines
                .Select(line => (int) Math.Ceiling(renderer.MeasureText(line.Slice) / (renderer.MaxLineWidth - hPadding * 2.0f)))
                .Sum();

            renderer.WriteText($"{code.Lines.Count} | {actualLines}");
            renderer.NextLine();

            renderer.PushStyle(renderer.CurrentStyle
                .WithFont(FontManager.SKEditorFontRegular)
                .WithCallback(ModifyDraw, ModifyMeasure));

            var style = renderer.CurrentStyle;
            var backgroundPaint = new SKPaint {
                Color = style.Paint.Color.WithAlpha(byte.MaxValue / 8),
                IsAntialias = true,
            };
            renderer.Canvas.DrawRoundRect(renderer.X, renderer.Y + style.Font.Metrics.Descent / 4.0f + vSpacing, renderer.MaxLineWidth, actualLines * style.Font.LineHeight() + vPadding * 2.0f, cornerRadius, cornerRadius, backgroundPaint);

            renderer.Y += vPadding + vSpacing;
            foreach (StringLine line in code.Lines) {
                renderer.WriteText(line.Slice);
                renderer.NextLine();
            }
            renderer.Y += vPadding + vSpacing;
            renderer.Height += vPadding + vSpacing;

            renderer.Pop();

            return;

            static void ModifyDraw(ReadOnlySpan<char> text, ref float x, ref float y) {
                x += hPadding;
            }
            static void ModifyMeasure(ReadOnlySpan<char> text, ref float width) {
                width += hPadding * 2.0f;
            }
        }
    }

    private class LiteralInlineRenderer : SkiaObjectRenderer<LiteralInline> {
        protected override void Write(SkiaRenderer renderer, LiteralInline literal) {
            renderer.WriteText(literal.Content);
        }
    }
    private class LineBreakInlineRenderer : SkiaObjectRenderer<LineBreakInline> {
        protected override void Write(SkiaRenderer renderer, LineBreakInline lineBreak) {
            Console.WriteLine($" - Line Break: Hard {lineBreak.IsHard} Backslash {lineBreak.IsBackslash} NewLine {lineBreak.NewLine}");
            if (lineBreak.IsHard) {
                renderer.NextLine();
            } else {
                renderer.WriteText(" ");
            }
        }
    }
    private class EmphasisInlineRenderer : SkiaObjectRenderer<EmphasisInline> {
        protected override void Write(SkiaRenderer renderer, EmphasisInline emphasis) {
            var style = renderer.CurrentStyle;

            var fontStyle = style.FontStyle;
            var fontDecoration = style.FontDecoration;

            switch (emphasis.DelimiterChar) {
                case '*':
                case '_':
                    if (emphasis.DelimiterCount == 2) {
                        fontStyle |= FontStyle.Bold;
                    } else {
                        fontStyle |= FontStyle.Italic;
                    }
                    break;
                case '~':
                    fontDecoration |= FontDecoration.Strikethrough;
                    break;
                case '+':
                    fontDecoration |= FontDecoration.Underline;
                    break;
            }

            renderer.PushStyle(style
                .WithFontStyle(fontStyle)
                .WithFontDecoration(fontDecoration));
            // renderer.PushFont(new SKFont(SKFontManager.Default.MatchTypeface(style.Font.Typeface, SKFontStyle.Bold), style.Font.Size, style.Font.ScaleX, style.Font.SkewX));
            renderer.WriteChildren(emphasis);
            renderer.Pop();
        }
    }
    private class CodeInlineRenderer : SkiaObjectRenderer<CodeInline> {
        protected override void Write(SkiaRenderer renderer, CodeInline code) {
            const float hPadding = 5.0f;
            const float vPadding = 0.0f;
            const float cornerRadius = 7.5f;

            renderer.PushStyle(renderer.CurrentStyle
                .WithFont(FontManager.SKEditorFontRegular)
                .WithCallback(ModifyDraw, ModifyMeasure));
            renderer.WriteText(code.Content);
            renderer.Pop();

            return;

            void ModifyDraw(ReadOnlySpan<char> text, ref float x, ref float y) {
                var style = renderer.CurrentStyle;
                var backgroundPaint = new SKPaint {
                    Color = style.Paint.Color.WithAlpha(byte.MaxValue / 8),
                    IsAntialias = true,
                };

                float width = style.Paint.MeasureText(text);
                renderer.Canvas.DrawRoundRect(x, y + style.Font.Metrics.Descent / 4.0f, width + hPadding * 2.0f, style.Font.LineHeight() + vPadding * 2.0f, cornerRadius, cornerRadius, backgroundPaint);
                x += hPadding;
            }
            void ModifyMeasure(ReadOnlySpan<char> text, ref float width) {
                width += hPadding * 2.0f;
            }
        }
    }
    private class LinkInlineRenderer : SkiaObjectRenderer<LinkInline> {
        protected override void Write(SkiaRenderer renderer, LinkInline link) {
            Console.WriteLine($"Link: {link.Label} | {link.Url} | {link.Title}: {new LinkButton().TextColor}");
            // renderer.WriteText(link.LabelWithTrivia);

            if (link.Url is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
                // Invalid URL
                renderer.WriteChildren(link);
                return;
            }

            Action clickAction = () => ProcessHelper.OpenInDefaultApp(link.Url!);

            var style = renderer.CurrentStyle;
            renderer.PushStyle(style
                .Clone()
                .WithColor(new SKColor(0xFF3281EA))
                .WithFontDecoration(style.FontDecoration | FontDecoration.Underline)
                .WithCallback(ModifyDraw, null));
            renderer.WriteChildren(link);
            renderer.Pop();

            return;

            void ModifyDraw(ReadOnlySpan<char> text, ref float x, ref float y) {
                var currStyle = renderer.CurrentStyle;
                float width = currStyle.Paint.MeasureText(text);

                renderer.ActionBoxes.Add((new SKRect(x, y, x + width, y + currStyle.Font.LineHeight()), clickAction));
            }
        }
    }

    #endregion
}
