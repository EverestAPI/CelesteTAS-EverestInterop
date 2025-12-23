using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Markdig;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SkiaSharp;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CelesteStudio.Controls;

/// Markdown renderer supporting regular text styling
/// TODO: Natively support images, instead of requiring additional support
/// TODO: Improve performance for longer documents
public class Markdown : SkiaDrawable {
    public event Action? PostDraw;
    public int RequiredHeight { get; private set; }

    private readonly MarkdownDocument Document;
    private readonly SkiaRenderer Renderer;
    private readonly Scrollable? Scrollable;

    // Render everything onto a big image once and then sample from that
    private SKBitmap? cacheBitmap;
    private SKSurface? cacheSurface;

    public override int DrawX => Scrollable?.ScrollPosition.X ?? 0;
    public override int DrawY => Scrollable?.ScrollPosition.Y ?? 0;
    public override int DrawWidth => (Scrollable?.Width ?? Width) - Padding.Horizontal;
    public override int DrawHeight => (Scrollable?.Height ?? Height) - Padding.Vertical;

    public Markdown(string content, Scrollable? scrollable) {
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .Build();

        Document = Markdig.Markdown.Parse(content, pipeline);
        Renderer = new SkiaRenderer();
        Scrollable = scrollable;

        if (Scrollable != null && !Eto.Platform.Instance.IsGtk) {
            Scrollable.Scroll += (_, _) => Invalidate();
        }
    }

    public override void Draw(SKSurface surface) {
        int width = Width - Padding.Horizontal, height = Height - Padding.Vertical;
        if (cacheSurface == null || width != cacheBitmap?.Width || height != cacheBitmap?.Height) {
            var colorType = SKImageInfo.PlatformColorType;

            cacheBitmap?.Dispose();
            cacheBitmap = new SKBitmap(width, height, colorType, SKAlphaType.Premul);
            IntPtr pixels = cacheBitmap.GetPixels();

            cacheSurface?.Dispose();
            cacheSurface = SKSurface.Create(new SKImageInfo(cacheBitmap.Info.Width, cacheBitmap.Info.Height, colorType, SKAlphaType.Premul), pixels, cacheBitmap.Info.RowBytes, new SKSurfaceProperties(SKPixelGeometry.RgbHorizontal));

            cacheSurface.Canvas.Clear(SKColor.Empty);
            Renderer.Reset(cacheSurface, width);
            Renderer.Render(Document);
            cacheSurface.Canvas.Flush();
        }

        surface.Canvas.DrawBitmap(cacheBitmap, 0.0f, 0.0f);

        RequiredHeight = (int) Renderer.Height;
        MinimumSize = new Size(0, (int) Renderer.Height);
        PostDraw?.Invoke();
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
        public float Width { get; set; }
        public float Height { get; set; }

        public float MaxLineWidth { get; private set; }

        public SKCanvas Canvas { get; private set; } = null!;

        public readonly List<(SKRect Region, Action OnClick)> ActionBoxes = [];

        private readonly Stack<StyleConfig> styleStack = new();
        public StyleConfig CurrentStyle => styleStack.Peek();

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

            Canvas = surface.Canvas;

            ActionBoxes.Clear();

            var textColor = Eto.Platform.Instance.IsWpf && Settings.Instance.Theme.DarkMode
                ? new Color(1.0f - SystemColors.ControlText.R, 1.0f - SystemColors.ControlText.G, 1.0f - SystemColors.ControlText.B)
                : SystemColors.ControlText;

            styleStack.Clear();
            styleStack.Push(new StyleConfig(new SKFont(SKTypeface.Default, 10.0f * FontManager.DPI), textColor.ToSkiaPacked(), SKTextAlign.Left, FontStyle.None, FontDecoration.None));
        }

        public override object Render(MarkdownObject markdownObject) {
            Write(markdownObject);
            return null!;
        }

        /// Pushes a new style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushStyle(StyleConfig config) => styleStack.Push(config);

        /// Pops the current style
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopStyle() => styleStack.Pop();

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

                    Y += GetLineSpacing(style);
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

            Height = Y + GetLineSpacing(style);
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
            X = 0.0f;
            Y += GetLineSpacing(CurrentStyle);
        }

        /// Calculates the current effective line height
        public float GetLineSpacing(StyleConfig style) => style.Font.LineHeight() * 1.25f;

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
            // ReSharper disable once UnusedMember.Local
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
                .WithFont(new SKFont(SKFontManager.Default.MatchTypeface(style.Font.Typeface, style.FontStyle switch {
                    FontStyle.None or FontStyle.Bold => SKFontStyle.Bold,
                    FontStyle.Italic or FontStyle.Bold | FontStyle.Italic => SKFontStyle.BoldItalic,
                    _ => throw new ArgumentOutOfRangeException(nameof(style.FontStyle), style.FontStyle, null)
                }), style.Font.Size * scale))
                .WithAlign(obj.Level == 1 ? SKTextAlign.Center : SKTextAlign.Left));

            if (renderer.Height > 0.0f) {
                renderer.Y = Math.Max(renderer.Y, renderer.Height + HeadingMarginTop);
            }
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

            renderer.PopStyle();
        }
    }
    private class ListBlockRenderer : SkiaObjectRenderer<ListBlock> {
        protected override void Write(SkiaRenderer renderer, ListBlock block) {
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

            renderer.PushStyle(renderer.CurrentStyle
                .WithFont(FontManager.SKEditorFontRegular)
                .WithCallback(ModifyDraw, ModifyMeasure));

            var style = renderer.CurrentStyle;
            var backgroundPaint = new SKPaint {
                Color = style.Paint.Color.WithAlpha(byte.MaxValue / 8),
                IsAntialias = true,
            };
            renderer.Canvas.DrawRoundRect(renderer.X, renderer.Y + style.Font.Metrics.Descent / 4.0f + vSpacing, renderer.MaxLineWidth, actualLines * renderer.GetLineSpacing(style) + vPadding * 2.0f, cornerRadius, cornerRadius, backgroundPaint);

            renderer.Y += vPadding + vSpacing;
            foreach (StringLine line in code.Lines) {
                renderer.WriteText(line.Slice);
                renderer.NextLine();
            }
            renderer.Y += vPadding + vSpacing;
            renderer.Height += vPadding + vSpacing;

            renderer.PopStyle();

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
            renderer.WriteChildren(emphasis);
            renderer.PopStyle();
        }
    }
    private class CodeInlineRenderer : SkiaObjectRenderer<CodeInline> {
        protected override void Write(SkiaRenderer renderer, CodeInline code) {
            const float hPadding = 5.0f;
            const float vPadding = 2.5f;
            const float cornerRadius = 7.5f;

            // macOS requires double DPI division?
            float scale = Eto.Platform.Instance.IsMac ? 1.0f / (FontManager.DPI * FontManager.DPI) : 1.0f / FontManager.DPI;

            var style = renderer.CurrentStyle;
            renderer.PushStyle(style
                .WithFont(FontManager.CreateSKFont(Settings.Instance.FontFamily, style.Font.Size * scale, style.FontStyle))
                .WithCallback(ModifyDraw, ModifyMeasure));
            renderer.WriteText(code.Content);
            renderer.PopStyle();

            return;

            void ModifyDraw(ReadOnlySpan<char> text, ref float x, ref float y) {
                if (text.Length == 0) {
                    return;
                }

                var currStyle = renderer.CurrentStyle;
                var backgroundPaint = new SKPaint {
                    Color = currStyle.Paint.Color.WithAlpha(byte.MaxValue / 8),
                    IsAntialias = true,
                };

                float width = currStyle.Paint.MeasureText(text);
                renderer.Canvas.DrawRoundRect(x, y + currStyle.Font.Metrics.Descent / 4.0f - vPadding, width + hPadding * 2.0f, currStyle.Font.LineHeight() + vPadding * 2.0f, cornerRadius, cornerRadius, backgroundPaint);
                x += hPadding;
            }
            void ModifyMeasure(ReadOnlySpan<char> text, ref float width) {
                width += hPadding * 2.0f;
            }
        }
    }
    private class LinkInlineRenderer : SkiaObjectRenderer<LinkInline> {
        protected override void Write(SkiaRenderer renderer, LinkInline link) {
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
            renderer.PopStyle();

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
