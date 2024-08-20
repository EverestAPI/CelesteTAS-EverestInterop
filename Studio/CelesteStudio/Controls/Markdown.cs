using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CelesteStudio.Controls;

/// Displays basic markdown inside a dialog
/// This markdown renderer supports the following elements:
/// - Bold,
/// - Italic,
/// - Inline-Code
/// - Headers 1-6,
/// - Hyperlinks
/// - Images
/// - Page breaks
public class Markdown : Drawable {
    // TODO: Please refactor. Thanks :)

    private static Color TextColor => Eto.Platform.Instance.IsWpf && Settings.Instance.Theme.DarkMode
        ? new Color(1.0f - SystemColors.ControlText.R, 1.0f - SystemColors.ControlText.G, 1.0f - SystemColors.ControlText.B)
        : SystemColors.ControlText;

    /// Current state on how text should look / behave
    private struct TextState {
        public bool Bold, Italic;
        public bool Code;

        public string? Link;
        public bool Image;

        public (FontStyle, FontDecoration) Resolve() {
            var fontStyle = FontStyle.None;
            var fontDecoration = FontDecoration.None;
            if (Bold) {
                fontStyle |= FontStyle.Bold;
            }
            if (Italic) {
                fontStyle |= FontStyle.Italic;
            }

            return (fontStyle, fontDecoration);
        }

        public Font GetFont() {
            var (fontStyle, fontDecoration) = Resolve();
            var sysFont = SystemFonts.Default();

            if (Code) {
                var codeFont = FontManager.EditorFontRegular;
                return new Font(codeFont.Family, sysFont.Size, fontStyle, fontDecoration);
            }

            return new Font(sysFont.Family, sysFont.Size, fontStyle, fontDecoration);
        }
    }
    /// Fully resolved part of the text
    private readonly struct TextPart(string text, TextState state, PointF position) {
        public readonly string Text = text;
        public readonly TextState State = state;
        public readonly PointF Position = position;

        public void Deconstruct(out string text, out TextState state, out PointF position) {
            text = Text;
            state = State;
            position = Position;
        }
    }

    /// Describes a combination of text of a certain kind, for example paragraphs, headings, etc.
    private interface TextComponent {
        public void Draw(Graphics graphics, Markdown markdown);
        public void Flush(string text, TextState state, PointF position, bool newLine);
        public SizeF Measure(string text, TextState state);
    }

    /// Handles headers of size 1-6
    private class HeaderComponent(int level) : TextComponent {
        private const float H1_Scale = 2.0f;
        private const float H2_Scale = 1.5f;
        private const float H3_Scale = 1.25f;
        private const float H4_Scale = 1.0f;
        private const float H5_Scale = 0.875f;
        private const float H6_Scale = 0.75f;

        private readonly float scale = Math.Clamp(level, 1, 6) switch {
            1 => H1_Scale,
            2 => H2_Scale,
            3 => H3_Scale,
            4 => H4_Scale,
            5 => H5_Scale,
            6 => H6_Scale,
            _ => throw new ArgumentOutOfRangeException()
        };

        private readonly List<List<TextPart>> lines = [];
        private float lineY;

        public void Draw(Graphics graphics, Markdown markdown) {
            var baseFont = SystemFonts.Default();

            foreach (var line in lines) {
                foreach ((string text, var style, var position) in line) {
                    var (fontStyle, fontDecoration) = style.Resolve();
                    var font = new Font(baseFont.Family, baseFont.Size * scale, fontStyle, fontDecoration);

                    graphics.DrawText(font, TextColor, position, text);
                }
            }

            if (level is 1 or 2) {
                using var pen = new Pen(TextColor with { A = 0.5f }, 1.0f);
                graphics.DrawLine(pen, graphics.ClipBounds.Left, lineY, graphics.ClipBounds.Right, lineY);
            }
        }

        public void Flush(string text, TextState state, PointF position, bool newLine) {
            List<TextPart> line;
            if (newLine || lines.Count == 0) {
                line = [];
                lines.Add(line);
            } else {
                line = lines.Last();
            }

            line.Add(new(text, state, position));
        }

        public void Finish(float pageWidth) {
            if (level is not (1 or 2)) {
                return;
            }

            var baseFont = SystemFonts.Default();
            lineY = 0.0f;

            foreach (var line in lines) {
                float width = 0.0f;

                foreach ((string text, var state, var position) in line) {
                    var (fontStyle, fontDecoration) = state.Resolve();
                    var font = new Font(baseFont.Family, baseFont.Size * scale, fontStyle, fontDecoration);
                    var textSize = font.MeasureString(text);

                    width += textSize.Width;
                    lineY = Math.Max(lineY, position.Y + textSize.Height);
                }

                // Center H1 headings
                if (level == 1) {
                    float xOffset = (pageWidth - width) / 2.0f;

                    for (var i = 0; i < line.Count; i++) {
                        var pos = line[i].Position;
                        line[i] = new TextPart(
                            line[i].Text,
                            line[i].State,
                            pos with { X = pos.X + xOffset });
                    }
                }
            }

            // Add a separator line for H1 and H2 headings
            lineY += baseFont.LineHeight() / 2.0f;
        }

        public SizeF Measure(string text, TextState state) {
            var baseFont = SystemFonts.Default();
            var (fontStyle, fontDecoration) = state.Resolve();
            var font = new Font(baseFont.Family, baseFont.Size * scale, fontStyle, fontDecoration);

            var fontSize = font.MeasureString(text);
            return new SizeF(fontSize.Width, fontSize.Height + baseFont.LineHeight());
        }
    }

    /// Handles regular paragraphs of text
    private class ParagraphComponent : TextComponent {
        private const float CodePaddingX = 3.0f;
        private const float CodePaddingY = 1.5f;

        public readonly List<(string Text, TextState State, PointF Position)> Parts = [];

        public void Draw(Graphics graphics, Markdown markdown) {
            foreach ((string text, var state, var position) in Parts) {
                var font = state.GetFont();

                var textPos = position;
                var size = graphics.MeasureString(font, text);

                if (state.Code) {
                    // Without this offset it just kinda looks wrong?
                    const float codeBgYOffset = 1.5f;

                    graphics.FillPath(Colors.Black with { A = 0.25f }, GraphicsPath.GetRoundRect(
                        new RectangleF(position.X, position.Y - CodePaddingY + codeBgYOffset, size.Width + CodePaddingX * 2.0f, size.Height + CodePaddingY * 2.0f),
                        5.0f));

                    textPos.X += CodePaddingX;
                }

                if (state.Link != null) {
                    if (state.Image) {
                        graphics.DrawImage(Bitmap.FromResource(state.Link), textPos);
                    } else {
                        var mousePosition = markdown.PointFromScreen(Mouse.Position);
                        bool hovered = mousePosition.X >= textPos.X && mousePosition.X <= textPos.X + size.Width &&
                                       mousePosition.Y >= textPos.Y && mousePosition.Y <= textPos.Y + size.Height;
                        if (hovered) {
                            font = font.WithFontDecoration(FontDecoration.Underline);
                            markdown.cursor = Cursors.Pointer;
                        }

                        graphics.DrawText(font, Color.FromRgb(0x4CACFC), textPos, text);
                    }
                } else {
                    graphics.DrawText(font, TextColor, textPos, text);
                }
            }
        }

        public void Flush(string text, TextState state, PointF position, bool newLine) {
            Parts.Add((text, state, position));
        }

        public SizeF Measure(string text, TextState state) {
            if (state.Link != null && state.Image) {
                return Bitmap.FromResource(state.Link).Size;
            }

            var size = state.GetFont().MeasureString(text);

            if (state.Code) {
                return new(size.Width + CodePaddingX * 2.0f, size.Height);
            }

            return size;
        }
    }

    private readonly List<TextComponent> components = [];
    private Cursor? cursor;

    protected override void OnMouseMove(MouseEventArgs e) => Invalidate();

    protected override void OnMouseUp(MouseEventArgs e) {
        // This could be passed down to the components, but not worth for just links
        foreach (var component in components) {
            if (component is not ParagraphComponent paragraph) {
                continue;
            }

            foreach ((string? text, var state, var position) in paragraph.Parts)
            {
                if (state.Link == null) {
                    continue;
                }

                var font = state.GetFont();
                var size = font.MeasureString(text);

                bool above = e.Location.X >= position.X && e.Location.X <= position.X + size.Width &&
                               e.Location.Y >= position.Y && e.Location.Y <= position.Y + size.Height;
                if (above) {
                    ProcessHelper.OpenInDefaultApp(state.Link);

                    e.Handled = true;
                    return;
                }
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.AntiAlias = true;

        cursor = null;
        foreach (var component in components) {
            component.Draw(e.Graphics, this);
        }
        Cursor = cursor;
    }

    public static List<Markdown> Parse(string markdownContent, Size pageSize) {
        var markdown = Markdig.Markdown.Parse(markdownContent, trackTrivia: true);

        const float lineSpacing = 1.2f;

        var pages = new List<Markdown>();
        var currentPage = new Markdown { Size = pageSize };
        var currentPosition = new PointF(0.0f, 0.0f);

        pages.Add(currentPage);

        TextComponent? currentComponent;
        var currentState = new TextState();
        string currentLine = string.Empty;
        float maxLineHeight = 0.0f;

        foreach (var item in markdown.Descendants<Block>()) {
            if (item is HeadingBlock heading) {
                currentComponent = new HeaderComponent(heading.Level);
                foreach (var inline in heading.Inline!) {
                    currentState.Bold = true;
                    ProcessInline(inline);
                    currentState.Bold = false;
                }
                FlushLine(newLine: true);
                ((HeaderComponent)currentComponent).Finish(pageSize.Width);
                currentPage.components.Add(currentComponent);
            } else if (item is ParagraphBlock paragraph) {
                currentComponent = new ParagraphComponent();
                foreach (var inline in paragraph.Inline!) {
                    ProcessInline(inline);
                }
                FlushLine(newLine: true);
                currentPage.components.Add(currentComponent);
            } else if (item is ThematicBreakBlock) {
                currentPage = new Markdown { Size = pageSize };
                currentPosition = new PointF(0.0f, 0.0f);
                pages.Add(currentPage);
            } else {
                Console.WriteLine($"Unhandled item: {item} ({item.GetType()})");
            }
        }

        return pages;

        void ProcessInline(Inline inline) {
            if (inline is LiteralInline literal) {
                ProcessText(literal.ToString());
            } else if (inline is EmphasisInline emphasis) {
                foreach (var emphasisLiteral in emphasis) {
                    if (emphasis.DelimiterCount == 2) {
                        currentState.Bold = true;
                        ProcessInline(emphasisLiteral);
                        currentState.Bold = false;
                    } else  if (emphasis.DelimiterCount == 1) {
                        currentState.Italic = true;
                        ProcessInline(emphasisLiteral);
                        currentState.Italic = false;
                    }
                }
            } else if (inline is CodeInline code) {
                currentState.Code = true;
                ProcessText(code.Content);
                currentState.Code = false;
            } else if (inline is LinkInline link) {
                currentState.Link = link.Url;
                currentState.Image = link.IsImage;
                foreach (var linkLiteral in link) {
                    ProcessInline(linkLiteral);
                }
                currentState.Link = null;
                currentState.Image = false;
            } else if (inline is LineBreakInline) {
                FlushLine(newLine: true);
            } else {
                Console.WriteLine($"Unhandled inline: {inline} ({inline.GetType()})");
            }
        }
        void ProcessText(string text) {
            var splitPoints = text
                .Select((c, i) => (c, i))
                .Where(pair => char.IsWhiteSpace(pair.c))
                .Select(pair => pair.i)
                .Concat([text.Length]);

            int start = 0;
            int prevSplit = 0;
            foreach (int split in splitPoints) {
                Again:
                var nextLine = currentLine + text[start..split];
                float totalWidth = currentPosition.X + currentComponent.Measure(nextLine, currentState).Width;

                // Split remaining words onto next line
                if (totalWidth > pageSize.Width) {
                    currentLine += text[start..prevSplit];
                    start = prevSplit;
                    FlushLine(newLine: true);

                    goto Again;
                }

                prevSplit = split;
            }

            currentLine += text[start..];
            FlushLine(newLine: false);
        }
        void FlushLine(bool newLine) {
            var size = currentComponent.Measure(currentLine, currentState);
            maxLineHeight = Math.Max(maxLineHeight, size.Height);

            if (currentPosition.X <= 0.01f) {
                currentLine = currentLine.TrimStart();
            }

            currentComponent.Flush(currentLine, currentState, currentPosition, newLine);
            currentLine = string.Empty;

            if (newLine) {
                currentPosition.X = 0.0f;
                currentPosition.Y += maxLineHeight * lineSpacing;
                maxLineHeight = 0.0f;
            } else {
                currentPosition.X += size.Width;
            }
        }
    }
}
