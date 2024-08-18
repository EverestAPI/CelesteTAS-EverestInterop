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
    }

    private readonly struct ParagraphComponent(PointF position, string text, FontStyle style) : TextComponent {
        public void Draw(Graphics graphics) => graphics.DrawText(SystemFonts.Default().WithFontStyle(style), SystemColors.ControlText, position, text);
    }

    //private enum TextStyle { Regular, Bold, Italic }
    private struct TextStyle {
        public bool Bold, Italic, Underline, Strikethrough;

    }

    private readonly List<(string Text, TextStyle Style, PointF Position)> textComponents = [];

    public Markdown() {


    }

    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.AntiAlias = true;

        foreach ((string text, var style, var position) in textComponents) {
            e.Graphics.DrawText(GetFont(style), Colors.White, position, text);
        }
    }

    private static SizeF Measure(string text, TextStyle style) {
        return GetFont(style).MeasureString(text);
    }
    private static Font GetFont(TextStyle style) {
        var fontStyle = FontStyle.None;
        var fontDecoration = FontDecoration.None;
        if (style.Bold) {
            fontStyle |= FontStyle.Bold;
        }
        if (style.Italic) {
            fontStyle |= FontStyle.Italic;
        }
        if (style.Underline) {
            fontDecoration |= FontDecoration.Underline;
        }
        if (style.Strikethrough) {
            fontDecoration |= FontDecoration.Strikethrough;
        }

        var font = SystemFonts.Default();
        return new Font(font.Family, font.Size, fontStyle, fontDecoration);
    }

    public static List<Markdown> Parse(Size pageSize) {
        var markdown = Markdig.Markdown.Parse("Hallo **Welt** Hallo **W**elt _italic_ and **_bold italic_** which is very very very very very very very very very very very very");

        var pages = new List<Markdown>();
        var currentPage = new Markdown { Size = pageSize };
        var currentPosition = new PointF(0.0f, 0.0f);

        var currentStyle = new TextStyle();
        string currentLine = string.Empty;
        float maxLineHeight = 0.0f;

        Console.WriteLine("===");

        foreach (var item in markdown.Descendants<Block>()) {
            Console.WriteLine($"Item: {item} ({item.GetType()})");
            if (item is ParagraphBlock paragraph) {
                foreach (var inline in paragraph.Inline!) {
                    ProcessInline(inline);
                }
            } else {
                Console.WriteLine($"Unhandled item: {item} ({item.GetType()})");
            }
        }

        void ProcessInline(Inline inline) {
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
            } else {
                Console.WriteLine($"Unhandled inline: {inline} ({inline.GetType()})");
            }
        }
        void ProcessLiteral(LiteralInline literal) {
            var font = SystemFonts.Default();
            string text = literal.ToString();
            var splitPoints = text
                .Select((c, i) => (c, i))
                .Where(pair => char.IsWhiteSpace(pair.c))
                .Select(pair => pair.i);

            int start = 0;
            int prevSplit = 0;
            foreach (int split in splitPoints) {
                Again:
                var nextLine = currentLine + text[start..split];
                float totalWidth = currentPosition.X + font.MeasureString(nextLine).Width;

                // Split remaining words onto next line
                if (totalWidth > pageSize.Width) {
                    currentLine += text[..prevSplit];
                    start = prevSplit;
                    FlushLine(newLine: true);

                    goto Again;
                }

                prevSplit = split;
            }

            currentLine += text;
            FlushLine(newLine: false);
        }
        void FlushLine(bool newLine) {
            var size = Measure(currentLine, currentStyle);
            maxLineHeight = Math.Max(maxLineHeight, size.Height);

            if (currentPosition.X <= 0.01f) {
                currentLine = currentLine.TrimStart();
            }

            Console.WriteLine($"Flush: '{currentLine}' with {currentStyle} ({newLine})");
            currentPage.textComponents.Add((currentLine, currentStyle, currentPosition));
            currentLine = string.Empty;

            if (newLine) {
                currentPosition.X = 0.0f;
                currentPosition.Y += maxLineHeight;
            } else {
                currentPosition.X += size.Width;
            }
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
