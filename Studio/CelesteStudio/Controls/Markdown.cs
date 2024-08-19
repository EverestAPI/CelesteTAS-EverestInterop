using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CelesteStudio.Controls;

public class Markdown : Drawable {
    private interface TextComponent {
        public void Draw(Graphics graphics);
        public void Flush(string text, TextStyle style, PointF position, bool newLine);
        public SizeF Measure(string text, TextStyle style);
    }
    private struct TextPart(string text, TextStyle style, PointF position) {
        public string Text = text;
        public TextStyle Style = style;
        public PointF Position = position;

        public void Deconstruct(out string text, out TextStyle style, out PointF position) {
            text = Text;
            style = Style;
            position = Position;
        }
    }

    private class HeaderComponent(int level) : TextComponent {
        private const float H1_Size = 2.0f;
        private const float H2_Size = 1.5f;
        private const float H3_Size = 1.25f;
        private const float H4_Size = 1.0f;
        private const float H5_Size = 0.875f;
        private const float H6_Size = 0.75f;

        private readonly float scale = Math.Clamp(level, 1, 6) switch {
            1 => H1_Size,
            2 => H2_Size,
            3 => H3_Size,
            4 => H4_Size,
            5 => H5_Size,
            6 => H6_Size,
            _ => throw new ArgumentOutOfRangeException()
        };

        private readonly List<List<TextPart>> lines = [];
        private float lineY;

        public void Draw(Graphics graphics) {
            var baseFont = SystemFonts.Default();

            foreach (var line in lines) {
                foreach ((string text, var style, var position) in line) {
                    var (fontStyle, fontDecoration) = style.Resolve();
                    var font = new Font(baseFont.Family, baseFont.Size * scale, fontStyle, fontDecoration);

                    graphics.DrawText(font, SystemColors.ControlText, position, text);
                }
            }

            if (level is 1 or 2) {
                using var pen = new Pen(SystemColors.ControlText with { A = 0.5f }, 1.0f);
                graphics.DrawLine(pen, graphics.ClipBounds.Left, lineY, graphics.ClipBounds.Right, lineY);
            }
        }

        public void Flush(string text, TextStyle style, PointF position, bool newLine) {
            List<TextPart> line;
            if (newLine || lines.Count == 0) {
                line = [];
                lines.Add(line);
            } else {
                line = lines.Last();
            }

            line.Add(new(text, style, position));
        }

        public void Finish(float pageWidth) {
            if (level is not (1 or 2)) {
                return;
            }

            var baseFont = SystemFonts.Default();
            lineY = 0.0f;

            foreach (var line in lines) {
                float width = 0.0f;

                foreach ((string text, var style, var position) in line) {
                    var (fontStyle, fontDecoration) = style.Resolve();
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
                        line[i] = line[i] with {
                            Position = pos with { X = pos.X + xOffset }
                        };
                    }
                }
            }

            // Add a separator line for H1 and H2 headings
            lineY += baseFont.LineHeight() / 2.0f;
        }

        public SizeF Measure(string text, TextStyle style) {
            var baseFont = SystemFonts.Default();
            var (fontStyle, fontDecoration) = style.Resolve();
            var font = new Font(baseFont.Family, baseFont.Size * scale, fontStyle, fontDecoration);

            var fontSize = font.MeasureString(text);
            return new SizeF(fontSize.Width, fontSize.Height + baseFont.LineHeight());
        }
    }

    private class ParagraphComponent : TextComponent {
        private readonly List<(string Text, TextStyle Style, PointF Position)> parts = [];

        public void Draw(Graphics graphics) {
            foreach ((string text, var style, var position) in parts) {
                graphics.DrawText(style.GetFont(), SystemColors.ControlText, position, text);
            }
        }

        public void Flush(string text, TextStyle style, PointF position, bool newLine) {
            parts.Add((text, style, position));
        }

        public SizeF Measure(string text, TextStyle style) {
            return style.GetFont().MeasureString(text);
        }
    }

    //private enum TextStyle { Regular, Bold, Italic }
    private struct TextStyle {
        public bool Bold, Italic, Underline, Strikethrough;

        public (FontStyle, FontDecoration) Resolve() {
            var fontStyle = FontStyle.None;
            var fontDecoration = FontDecoration.None;
            if (Bold) {
                fontStyle |= FontStyle.Bold;
            }
            if (Italic) {
                fontStyle |= FontStyle.Italic;
            }
            if (Underline) {
                fontDecoration |= FontDecoration.Underline;
            }
            if (Strikethrough) {
                fontDecoration |= FontDecoration.Strikethrough;
            }

            return (fontStyle, fontDecoration);
        }

        public Font GetFont() {
            var (fontStyle, fontDecoration) = Resolve();
            var font = SystemFonts.Default();
            return new Font(font.Family, font.Size, fontStyle, fontDecoration);
        }
    }

    //private readonly List<(string Text, TextStyle Style, PointF Position)> textComponents = [];
    private readonly List<TextComponent> components = [];

    public Markdown() {


    }

    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.AntiAlias = true;

        // foreach ((string text, var style, var position) in textComponents) {
        //     e.Graphics.DrawText(style.GetFont(), Colors.White, position, text);
        // }
        foreach (var component in components) {
            component.Draw(e.Graphics);
        }
    }

    public static List<Markdown> Parse(string markdownContent, Size pageSize) {
        var markdown = Markdig.Markdown.Parse(markdownContent, trackTrivia: true);

        var pages = new List<Markdown>();
        var currentPage = new Markdown { Size = pageSize };
        var currentPosition = new PointF(0.0f, 0.0f);

        TextComponent? currentComponent;
        var currentStyle = new TextStyle();
        string currentLine = string.Empty;
        float maxLineHeight = 0.0f;

        Console.WriteLine("===");

        foreach (var item in markdown.Descendants<Block>()) {
            Console.WriteLine($" * {item} ({item.GetType()})");
            if (item is HeadingBlock heading) {
                currentComponent = new HeaderComponent(heading.Level);
                foreach (var inline in heading.Inline!) {
                    currentStyle.Bold = true;
                    ProcessInline(inline);
                    currentStyle.Bold = false;
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
            } else {
                Console.WriteLine($"Unhandled item: {item} ({item.GetType()})");
            }
        }

        void ProcessInline(Inline inline) {
            Console.WriteLine($"   -> {inline} ({inline.GetType()})");
            if (inline is LiteralInline literal) {
                ProcessLiteral(literal);
            } else if (inline is EmphasisInline emphasis) {
                foreach (var emphasisLiteral in emphasis) {
                    if (emphasis.DelimiterCount == 2) {
                        currentStyle.Bold = true;
                        ProcessInline(emphasisLiteral);
                        currentStyle.Bold = false;
                    } else  if (emphasis.DelimiterCount == 1) {
                        currentStyle.Italic = true;
                        ProcessInline(emphasisLiteral);
                        currentStyle.Italic = false;
                    }
                }
            } else if (inline is LineBreakInline lineBreak) {
                FlushLine(newLine: true);
            } else {
                Console.WriteLine($"Unhandled inline: {inline} ({inline.GetType()})");
            }
        }
        void ProcessLiteral(LiteralInline literal) {
            string text = literal.ToString();
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
                float totalWidth = currentPosition.X + currentComponent.Measure(nextLine, currentStyle).Width;

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
            var size = currentComponent.Measure(currentLine, currentStyle);
            maxLineHeight = Math.Max(maxLineHeight, size.Height);

            if (currentPosition.X <= 0.01f) {
                currentLine = currentLine.TrimStart();
            }

            currentComponent.Flush(currentLine, currentStyle, currentPosition, newLine);
            currentLine = string.Empty;

            if (newLine) {
                currentPosition.X = 0.0f;
                currentPosition.Y += maxLineHeight;
            } else {
                currentPosition.X += size.Width;
            }

            maxLineHeight = 0.0f;
        }

        /*
        foreach (var entry in markdown) {
            switch (entry) {
                case ParagraphBlock paragraph:
                    foreach (var line in paragraph.Lines) {
                        Console.WriteLine($"PL {line}");
                    }
                    foreach (var inline in paragraph.Inline!) {
                        switch (inline) {
                            case LiteralInline literal:
                            {
                                string text = literal.Content.ToString();

                                var size = ParagraphComponent.Measure(text);
                                currentPage.components.Add(new ParagraphComponent(currentPosition, text));
                                break;
                            }
                            case EmphasisInline emphasis:
                            {
                                // string text = emphasis..Content.ToString();
                                //
                                // var size = ParagraphComponent.Measure(text);
                                // currentPage.components.Add(new ParagraphComponent(currentPosition, text));
                                break;
                            }

                        }
                        Console.WriteLine($"IC {inline} {inline.Ch} {inline.GetType()}");
                    }

                    break;
            }
            Console.WriteLine($"E {entry}");
        }
        */

        return [currentPage];
    }
}
