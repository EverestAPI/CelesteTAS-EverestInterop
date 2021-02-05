using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace CelesteStudio.RichText {
public class SyntaxHighlighter : IDisposable {
    public static readonly Regex CommentRegex = new Regex(@"^\s*#.*");
    public static readonly Regex BreakPointRegex = new Regex(@"^\s*\*\*\*");
    public static readonly Regex InputRecordRegex = new Regex(@"^( {3}\d| {2}\d{2}| \d{3}|\d{4})");
    public readonly Style AquaStyle = new TextStyle(Brushes.Aqua, null, FontStyle.Regular);

    public readonly Style BlueBoldStyle = new TextStyle(Brushes.Blue, null, FontStyle.Bold);

    //styles
    public readonly Style BlueStyle = new TextStyle(Brushes.Blue, null, FontStyle.Regular);
    public readonly Style BoldStyle = new TextStyle(null, null, FontStyle.Bold);
    public readonly Style BrownStyle = new TextStyle(Brushes.Brown, null, FontStyle.Regular);
    public readonly Style ChocolateBgStyle = new TextStyle(Brushes.White, Brushes.Chocolate, FontStyle.Regular);
    public readonly Style ChocolateStyle = new TextStyle(Brushes.Chocolate, null, FontStyle.Regular);

    readonly Dictionary<string, SyntaxDescriptor> descByXMLfileNames = new Dictionary<string, SyntaxDescriptor>();
    public readonly Style GoldenrodBgStyle = new TextStyle(Brushes.White, Brushes.Goldenrod, FontStyle.Regular);
    public readonly Style GrayStyle = new TextStyle(Brushes.Gray, null, FontStyle.Regular);
    public readonly Style GreenStyle = new TextStyle(Brushes.Green, null, FontStyle.Regular);
    public readonly Style MagentaStyle = new TextStyle(Brushes.Magenta, null, FontStyle.Regular);
    public readonly Style MaroonStyle = new TextStyle(Brushes.Maroon, null, FontStyle.Regular);
    public readonly Style OrangeStyle = new TextStyle(Brushes.Orange, null, FontStyle.Regular);
    public readonly Style PinkStyle = new TextStyle(new SolidBrush(Color.FromArgb(255, 0, 255)), null, FontStyle.Regular);
    public readonly Style RedBgStyle = new TextStyle(Brushes.White, new SolidBrush(Color.FromArgb(224, 64, 64)), FontStyle.Regular);
    public readonly Style RedStyle = new TextStyle(Brushes.Red, null, FontStyle.Regular);
    public readonly Style SteelBlueBgStyle = new TextStyle(Brushes.White, Brushes.SteelBlue, FontStyle.Regular);

    public static RegexOptions RegexCompiledOption {
        get {
            if (!Environment.Is64BitOperatingSystem) {
                return RegexOptions.Compiled;
            } else {
                return RegexOptions.None;
            }
        }
    }

    public void Dispose() {
        foreach (var desc in descByXMLfileNames.Values) {
            desc.Dispose();
        }
    }

    /// <summary>
    /// Highlights syntax for given language
    /// </summary>
    public virtual void HighlightSyntax(Language language, Range range) {
        switch (language) {
            case Language.TAS:
                TASSyntaxHighlight(range);
                break;
            default: break;
        }
    }

    /// <summary>
    /// Highlights syntax for given XML description file
    /// </summary>
    public virtual void HighlightSyntax(string XMLdescriptionFile, Range range) {
        SyntaxDescriptor desc = null;
        if (!descByXMLfileNames.TryGetValue(XMLdescriptionFile, out desc)) {
            var doc = new XmlDocument();
            string file = XMLdescriptionFile;
            if (!File.Exists(file)) {
                file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(file));
            }

            doc.LoadXml(File.ReadAllText(file));
            desc = ParseXmlDescription(doc);
            descByXMLfileNames[XMLdescriptionFile] = desc;
        }

        HighlightSyntax(desc, range);
    }

    public virtual void AutoIndentNeeded(object sender, AutoIndentEventArgs args) {
        args.TabLength = 0;
    }

    public static SyntaxDescriptor ParseXmlDescription(XmlDocument doc) {
        SyntaxDescriptor desc = new SyntaxDescriptor();
        XmlNode brackets = doc.SelectSingleNode("doc/brackets");
        if (brackets != null) {
            if (brackets.Attributes["left"] == null || brackets.Attributes["right"] == null || brackets.Attributes["left"].Value == "" ||
                brackets.Attributes["right"].Value == "") {
                desc.leftBracket = '\x0';
                desc.rightBracket = '\x0';
            } else {
                desc.leftBracket = brackets.Attributes["left"].Value[0];
                desc.rightBracket = brackets.Attributes["right"].Value[0];
            }

            if (brackets.Attributes["left2"] == null || brackets.Attributes["right2"] == null || brackets.Attributes["left2"].Value == "" ||
                brackets.Attributes["right2"].Value == "") {
                desc.leftBracket2 = '\x0';
                desc.rightBracket2 = '\x0';
            } else {
                desc.leftBracket2 = brackets.Attributes["left2"].Value[0];
                desc.rightBracket2 = brackets.Attributes["right2"].Value[0];
            }
        }

        Dictionary<string, Style> styleByName = new Dictionary<string, Style>();

        foreach (XmlNode style in doc.SelectNodes("doc/style")) {
            var s = ParseStyle(style);
            styleByName[style.Attributes["name"].Value] = s;
            desc.styles.Add(s);
        }

        foreach (XmlNode rule in doc.SelectNodes("doc/rule")) {
            desc.rules.Add(ParseRule(rule, styleByName));
        }

        foreach (XmlNode folding in doc.SelectNodes("doc/folding")) {
            desc.foldings.Add(ParseFolding(folding));
        }

        return desc;
    }

    private static FoldingDesc ParseFolding(XmlNode foldingNode) {
        FoldingDesc folding = new FoldingDesc();
        //regex
        folding.startMarkerRegex = foldingNode.Attributes["start"].Value;
        folding.finishMarkerRegex = foldingNode.Attributes["finish"].Value;
        //options
        var optionsA = foldingNode.Attributes["options"];
        if (optionsA != null) {
            folding.options = (RegexOptions) Enum.Parse(typeof(RegexOptions), optionsA.Value);
        }

        return folding;
    }

    private static RuleDesc ParseRule(XmlNode ruleNode, Dictionary<string, Style> styles) {
        RuleDesc rule = new RuleDesc();
        rule.pattern = ruleNode.InnerText;
        var styleA = ruleNode.Attributes["style"];
        var optionsA = ruleNode.Attributes["options"];
        //Style
        if (styleA == null) {
            throw new Exception("Rule must contain style name.");
        }

        if (!styles.ContainsKey(styleA.Value)) {
            throw new Exception("Style '" + styleA.Value + "' is not found.");
        }

        rule.style = styles[styleA.Value];
        //options
        if (optionsA != null) {
            rule.options = (RegexOptions) Enum.Parse(typeof(RegexOptions), optionsA.Value);
        }

        return rule;
    }

    private static Style ParseStyle(XmlNode styleNode) {
        var typeA = styleNode.Attributes["type"];
        var colorA = styleNode.Attributes["color"];
        var backColorA = styleNode.Attributes["backColor"];
        var fontStyleA = styleNode.Attributes["fontStyle"];
        var nameA = styleNode.Attributes["name"];
        //colors
        SolidBrush foreBrush = null;
        if (colorA != null) {
            foreBrush = new SolidBrush(ParseColor(colorA.Value));
        }

        SolidBrush backBrush = null;
        if (backColorA != null) {
            backBrush = new SolidBrush(ParseColor(backColorA.Value));
        }

        //fontStyle
        FontStyle fontStyle = FontStyle.Regular;
        if (fontStyleA != null) {
            fontStyle = (FontStyle) Enum.Parse(typeof(FontStyle), fontStyleA.Value);
        }

        return new TextStyle(foreBrush, backBrush, fontStyle);
    }

    private static Color ParseColor(string s) {
        if (s.StartsWith("#")) {
            if (s.Length <= 7) {
                return Color.FromArgb(255, Color.FromArgb(Int32.Parse(s.Substring(1), System.Globalization.NumberStyles.AllowHexSpecifier)));
            } else {
                return Color.FromArgb(Int32.Parse(s.Substring(1), System.Globalization.NumberStyles.AllowHexSpecifier));
            }
        } else {
            return Color.FromName(s);
        }
    }

    public void HighlightSyntax(SyntaxDescriptor desc, Range range) {
        //set style order
        range.tb.ClearStylesBuffer();
        for (int i = 0; i < desc.styles.Count; i++) {
            range.tb.Styles[i] = desc.styles[i];
        }

        //brackets
        range.tb.LeftBracket = desc.leftBracket;
        range.tb.RightBracket = desc.rightBracket;
        range.tb.LeftBracket2 = desc.leftBracket2;
        range.tb.RightBracket2 = desc.rightBracket2;
        //clear styles of range
        range.ClearStyle(desc.styles.ToArray());
        //highlight syntax
        foreach (var rule in desc.rules) {
            range.SetStyle(rule.style, rule.Regex);
        }

        //clear folding
        range.ClearFoldingMarkers();
        //folding markers
        foreach (var folding in desc.foldings) {
            range.SetFoldingMarkers(folding.startMarkerRegex, folding.finishMarkerRegex, folding.options);
        }
    }

    public virtual void TASSyntaxHighlight(Range range) {
        RichText tb = range.tb;
        tb.CommentPrefix = "#";
        tb.LeftBracket = '\x0';
        tb.RightBracket = '\x0';
        tb.LeftBracket2 = '\x0';
        tb.RightBracket2 = '\x0';
        //clear style of changed range
        range.ClearStyle(GrayStyle, GreenStyle, RedStyle, BlueStyle, PinkStyle,
            RedBgStyle, ChocolateStyle, ChocolateBgStyle, SteelBlueBgStyle, GoldenrodBgStyle);

        int start = range.Start.iLine;
        int end = range.End.iLine;
        if (start > end) {
            int temp = start;
            start = end;
            end = temp;
        }

        while (start <= end) {
            int charEnd = tb[start].Count;
            Range line = new Range(tb, 0, start, charEnd, start);

            if (InputRecordRegex.IsMatch(line.Text)) {
                Range sub = new Range(tb, 0, start, 4, start);
                sub.SetStyle(RedStyle);

                int charStart = 4;
                while (charStart < charEnd) {
                    sub = new Range(tb, charStart, start, charStart + 1, start);

                    if (char.IsDigit(tb[start][charStart].c)) {
                        sub.SetStyle(PinkStyle);
                    } else if ((charStart & 1) == 0) {
                        sub.SetStyle(GrayStyle);
                    } else {
                        sub.SetStyle(BlueStyle);
                    }

                    charStart++;
                }
            } else if (BreakPointRegex.IsMatch(line.Text)) {
                int index = line.Text.IndexOf("***", StringComparison.Ordinal);
                Range sub = new Range(tb, index, start, index + 3, start);
                sub.SetStyle(RedBgStyle);

                if (tb[start].Count >= index + 4) {
                    int charStart = index + 3;
                    if (tb[start][charStart].c.ToString().ToLower() == "s") {
                        sub = new Range(tb, charStart, start, charStart + 1, start);
                        sub.SetStyle(SteelBlueBgStyle);
                        charStart++;
                    } else if (tb[start][charStart].c == '!') {
                        sub = new Range(tb, charStart, start, charStart + 1, start);
                        sub.SetStyle(GoldenrodBgStyle);
                        charStart++;
                    }

                    sub = new Range(tb, charStart, start, charEnd, start);
                    sub.SetStyle(RedBgStyle);
                }
            } else {
                line.SetStyle(GreenStyle, CommentRegex);
                line.SetStyle(ChocolateStyle);
            }

            start++;
        }
    }
}

/// <summary>
/// Language
/// </summary>
public enum Language {
    Custom,
    TAS
}
}