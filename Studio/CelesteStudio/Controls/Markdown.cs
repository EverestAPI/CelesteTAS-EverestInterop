using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CelesteStudio.Controls;

public class Markdown : SkiaDrawable {
    private readonly MarkdownDocument Document;
    private readonly SkiaRenderer Renderer;

    public Markdown(string content) {
        Document = Markdig.Markdown.Parse(content);

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

    #region Renderers

    private class SkiaRenderer : RendererBase {
        public readonly struct StyleConfig(SKFont font, SKColor color, SKTextAlign align) {
            public readonly SKFont Font = font;
            public readonly SKPaint Paint = new(font) {
                Color = color,
                TextAlign = align,
            };

            public StyleConfig WithFont(SKFont font) {
                return new StyleConfig(font, Paint.Color, Paint.TextAlign);
            }

            public StyleConfig WithColor(SKColor color) {
                Paint.Color = color;
                return this;
            }
            public StyleConfig WithAlign(SKTextAlign align) {
                Paint.TextAlign = align;
                return this;
            }
        }

        public float X { get; set; }
        public float Y { get; set; }

        public SKSurface Surface { get; private set; } = null!;
        public SKCanvas Canvas { get; private set; } = null!;

        public float Width { get; set; }
        public float Height { get; set; }

        public float MaxLineWidth { get; private set; }

        public StyleConfig CurrentStyle => styleStack.Peek();
        private Stack<StyleConfig> styleStack = new();

        public SkiaRenderer() {
            // Block renderers
            ObjectRenderers.Add(new ParagraphBlockRenderer());
            ObjectRenderers.Add(new HeadingBlockRenderer());

            // Inline renderers
            ObjectRenderers.Add(new LiteralInlineRenderer());
            ObjectRenderers.Add(new LineBreakInlineRenderer());
        }

        public void Reset(SKSurface surface, float maxWidth) {
            X = 0.0f;
            Y = 0.0f;
            Width = 0.0f;
            Height = 0.0f;
            MaxLineWidth = maxWidth;

            Surface = surface;
            Canvas = surface.Canvas;

            styleStack.Clear();
            styleStack.Push(new StyleConfig(new SKFont(SKTypeface.Default, Settings.Instance.EditorFontSize * FontManager.DPI), SKColors.White, SKTextAlign.Left));
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

            float advance = style.Paint.MeasureText(text);
            if (X + advance > MaxLineWidth) {
                // Warp lines
                var iterator = new WrapLineIterator(text, X, MaxLineWidth, style.Paint);

                // First iteration shouldn't start at a new line
                iterator.MoveNext();
                float firstRenderX = style.Paint.TextAlign switch {
                    SKTextAlign.Left => X,
                    SKTextAlign.Right => X + MaxLineWidth - style.Paint.MeasureText(iterator.Current),
                    SKTextAlign.Center => X + (MaxLineWidth - style.Paint.MeasureText(iterator.Current)) / 2.0f,
                    _ => throw new ArgumentOutOfRangeException()
                };
                Canvas.DrawText(iterator.Current, firstRenderX, Y + style.Font.Offset(), style.Font, style.Paint);

                // Iterate remaining lines
                while (iterator.MoveNext()) {
                    float wrapRenderX = style.Paint.TextAlign switch {
                        SKTextAlign.Left => 0.0f,
                        SKTextAlign.Right => MaxLineWidth - style.Paint.MeasureText(iterator.Current),
                        SKTextAlign.Center => (MaxLineWidth - style.Paint.MeasureText(iterator.Current)) / 2.0f,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    Y += style.Font.LineHeight();
                    Canvas.DrawText(iterator.Current, wrapRenderX, Y + style.Font.Offset(), style.Font, style.Paint);
                }

                X = style.Paint.MeasureText(iterator.Current);
                Width = MaxLineWidth;
            } else {
                float renderX = style.Paint.TextAlign switch {
                    SKTextAlign.Left => X,
                    SKTextAlign.Right => X + MaxLineWidth - advance,
                    SKTextAlign.Center => X + (MaxLineWidth - advance) / 2.0f,
                    _ => throw new ArgumentOutOfRangeException()
                };

                Canvas.DrawText(text, renderX, Y + style.Font.Offset(), style.Font, style.Paint);

                X += advance;
                Width = Math.Max(Width, X);
            }

            Height = Y + style.Font.LineHeight();
        }

        /// Advances to the start of the next line
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextLine() {
            X = 0.0f;
            Y += CurrentStyle.Font.LineHeight();
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
        private ref struct WrapLineIterator(ReadOnlySpan<char> text, float startOffset, float maxLineWidth, SKPaint paint) {
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

                    float width = paint.MeasureText(text[startIdx..i]);
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

            renderer.Y = renderer.Height + HeadingMarginTop;
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

    private class LiteralInlineRenderer : SkiaObjectRenderer<LiteralInline> {
        protected override void Write(SkiaRenderer renderer, LiteralInline obj) {
            renderer.WriteText(obj.Content);
        }
    }
    private class LineBreakInlineRenderer : SkiaObjectRenderer<LineBreakInline> {
        protected override void Write(SkiaRenderer renderer, LineBreakInline obj) {
            Console.WriteLine($" - Line Break: Hard {obj.IsHard} Backslash {obj.IsBackslash} NewLine {obj.NewLine}");
            if (obj.IsHard) {
                renderer.NextLine();
            } else {
                renderer.WriteText(" ");
            }
        }
    }


    #endregion
}
