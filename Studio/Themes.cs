using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CelesteStudio.RichText;
using Tommy.Serializer;

namespace CelesteStudio;

public enum ThemesType {
    Light,
    Dark,
    Custom
}

public abstract class Themes {
    public static Themes Light = new LightThemes();
    public static Themes Dark = new DarkThemes();
    public static Themes Custom = new CustomThemes();

    public abstract List<string> Action { get; set; }
    public abstract List<string> Angle { get; set; }
    public abstract List<string> Background { get; set; }
    public abstract List<string> Breakpoint { get; set; }
    public abstract List<string> Caret { get; set; }
    public abstract List<string> ChangedLine { get; set; }
    public abstract List<string> Comma { get; set; }
    public abstract List<string> Command { get; set; }
    public abstract List<string> Comment { get; set; }
    public abstract List<string> CurrentLine { get; set; }
    public abstract List<string> Frame { get; set; }
    public abstract List<string> LineNumber { get; set; }
    public abstract List<string> PlayingFrame { get; set; }
    public abstract List<string> PlayingLine { get; set; }
    public abstract List<string> SaveState { get; set; }
    public abstract List<string> Selection { get; set; }
    public abstract List<string> ServiceLine { get; set; }
    public abstract List<string> Status { get; set; }
    public abstract bool DarkTitlebar { get; set; }

    public static void Load(string path) {
        if (File.Exists(path)) {
            try {
                Light = TommySerializer.FromTomlFile<LightThemes>(path);
                Dark = TommySerializer.FromTomlFile<DarkThemes>(path);
                Custom = TommySerializer.FromTomlFile<CustomThemes>(path);
            } catch {
                // ignore
            }
        }

        ResetThemes();
    }

    public static void ResetThemes() {
        Themes themes = Settings.Instance.ThemesType switch {
            ThemesType.Dark => Dark,
            ThemesType.Custom => Custom,
            _ => Light
        };

        RichText.RichText richText = Studio.Instance.richText;

        SyntaxHighlighter.ActionStyle = ColorUtils.CreateTextStyle(themes.Action);
        SyntaxHighlighter.AngleStyle = ColorUtils.CreateTextStyle(themes.Angle);
        SyntaxHighlighter.BreakpointAsteriskStyle = ColorUtils.CreateTextStyle(themes.Breakpoint);
        SyntaxHighlighter.CommaStyle = ColorUtils.CreateTextStyle(themes.Comma);
        SyntaxHighlighter.CommandStyle = ColorUtils.CreateTextStyle(themes.Command);
        SyntaxHighlighter.CommentStyle = ColorUtils.CreateTextStyle(themes.Comment);
        SyntaxHighlighter.FrameStyle = ColorUtils.CreateTextStyle(themes.Frame);
        SyntaxHighlighter.SaveStateStyle = ColorUtils.CreateTextStyle(themes.SaveState);

        richText.SaveStateTextColor = ColorUtils.HexToColor(themes.SaveState);
        richText.SaveStateBgColor = ColorUtils.HexToColor(themes.SaveState, 1);
        richText.PlayingLineTextColor = ColorUtils.HexToColor(themes.PlayingLine);
        richText.PlayingLineBgColor = ColorUtils.HexToColor(themes.PlayingLine, 1);
        richText.BackColor = ColorUtils.HexToColor(themes.Background);
        richText.PaddingBackColor = ColorUtils.HexToColor(themes.Background);
        richText.IndentBackColor = ColorUtils.HexToColor(themes.Background);
        richText.CaretColor = ColorUtils.HexToColor(themes.Caret);
        richText.CurrentTextColor = ColorUtils.HexToColor(themes.PlayingFrame);
        richText.LineNumberColor = ColorUtils.HexToColor(themes.LineNumber);
        richText.SelectionColor = ColorUtils.HexToColor(themes.Selection);
        richText.CurrentLineColor = ColorUtils.HexToColor(themes.CurrentLine);
        richText.ChangedLineTextColor = ColorUtils.HexToColor(themes.ChangedLine);
        richText.ChangedLineBgColor = ColorUtils.HexToColor(themes.ChangedLine, 1);
        richText.ServiceLinesColor = ColorUtils.HexToColor(themes.ServiceLine);

        Studio.Instance.SetControlsColor(themes);
        Studio.Instance.UseImmersiveDarkMode(themes.DarkTitlebar);

        richText.ClearStylesBuffer();
        richText.SyntaxHighlighter.TASSyntaxHighlight(richText.Range);
    }
}

[TommyTableName("LightThemes")]
public class LightThemes : Themes {
    public override List<string> Action { get; set; } = new() { "2222FF" };
    public override List<string> Angle { get; set; } = new() { "EE22EE" };
    public override List<string> Background { get; set; } = new() { "FFFFFF" };
    public override List<string> Breakpoint { get; set; } = new() { "FFFFFF", "FF5555" };
    public override List<string> Caret { get; set; } = new() { "000000" };
    public override List<string> ChangedLine { get; set; } = new() { "000000", "FF8C00" };
    public override List<string> Comma { get; set; } = new() { "808080" };
    public override List<string> Command { get; set; } = new() { "D2691E" };
    public override List<string> Comment { get; set; } = new() { "00A000" };
    public override List<string> CurrentLine { get; set; } = new() { "20000000" };
    public override List<string> Frame { get; set; } = new() { "FF2222" };
    public override List<string> LineNumber { get; set; } = new() { "000000" };
    public override List<string> PlayingFrame { get; set; } = new() { "22A022" };
    public override List<string> PlayingLine { get; set; } = new() { "000000", "55FF55" };
    public override List<string> SaveState { get; set; } = new() { "FFFFFF", "4682B4" };
    public override List<string> Selection { get; set; } = new() { "20000000" };
    public override List<string> ServiceLine { get; set; } = new() { "C0C0C0" };
    public override List<string> Status { get; set; } = new() { "000000", "F2F2F2" };
    public override bool DarkTitlebar { get; set; } = false;
}

[TommyTableName("DarkThemes")]
public class DarkThemes : Themes {
    public override List<string> Action { get; set; } = new() { "8BE9FD" };
    public override List<string> Angle { get; set; } = new() { "FF79C6" };
    public override List<string> Background { get; set; } = new() { "282A36" };
    public override List<string> Breakpoint { get; set; } = new() { "F8F8F2", "FF5555" };
    public override List<string> Caret { get; set; } = new() { "AEAFAD" };
    public override List<string> ChangedLine { get; set; } = new() { "6272A4", "FFB86C" };
    public override List<string> Comma { get; set; } = new() { "6272A4" };
    public override List<string> Command { get; set; } = new() { "FFB86C" };
    public override List<string> Comment { get; set; } = new() { "95B272" };
    public override List<string> CurrentLine { get; set; } = new() { "20B4B6C7" };
    public override List<string> Frame { get; set; } = new() { "BD93F9" };
    public override List<string> LineNumber { get; set; } = new() { "6272A4" };
    public override List<string> PlayingFrame { get; set; } = new() { "F1FA8C" };
    public override List<string> PlayingLine { get; set; } = new() { "6272A4", "F1FA8C" };
    public override List<string> SaveState { get; set; } = new() { "F8F8F2", "4682B4" };
    public override List<string> Selection { get; set; } = new() { "20B4B6C7" };
    public override List<string> ServiceLine { get; set; } = new() { "44475A" };
    public override List<string> Status { get; set; } = new() { "F8F8F2", "383A46" };
    public override bool DarkTitlebar { get; set; } = true;
}

[TommyTableName("CustomThemes")]
public class CustomThemes : Themes {
    public override List<string> Action { get; set; } = new() { "268BD2" };
    public override List<string> Angle { get; set; } = new() { "D33682" };
    public override List<string> Background { get; set; } = new() { "FDF6E3" };
    public override List<string> Breakpoint { get; set; } = new() { "FDF6E3", "DC322F" };
    public override List<string> Caret { get; set; } = new() { "6B7A82" };
    public override List<string> ChangedLine { get; set; } = new() { "F8F8F2", "CB4B16" };
    public override List<string> Comma { get; set; } = new() { "808080" };
    public override List<string> Command { get; set; } = new() { "B58900" };
    public override List<string> Comment { get; set; } = new() { "859900" };
    public override List<string> CurrentLine { get; set; } = new() { "201A1300" };
    public override List<string> Frame { get; set; } = new() { "DC322F" };
    public override List<string> LineNumber { get; set; } = new() { "93A1A1" };
    public override List<string> PlayingFrame { get; set; } = new() { "6C71C4" };
    public override List<string> PlayingLine { get; set; } = new() { "FDF6E3", "6C71C4" };
    public override List<string> SaveState { get; set; } = new() { "FDF6E3", "268BD2" };
    public override List<string> Selection { get; set; } = new() { "201A1300" };
    public override List<string> ServiceLine { get; set; } = new() { "44475A" };
    public override List<string> Status { get; set; } = new() { "073642", "EEE8D5" };
    public override bool DarkTitlebar { get; set; } = false;

}

public class ThemesColorTable : ProfessionalColorTable {
    private readonly Themes themes;

    public ThemesColorTable(Themes themes) {
        this.themes = themes;
    }

    public override Color ToolStripDropDownBackground => ColorUtils.HexToColor(themes.Status, 1);
    public override Color ImageMarginGradientBegin => ColorUtils.HexToColor(themes.Status, 1);
    public override Color ImageMarginGradientMiddle => ColorUtils.HexToColor(themes.Status, 1);
    public override Color ImageMarginGradientEnd => ColorUtils.HexToColor(themes.Status, 1);
    public override Color MenuBorder => ColorUtils.HexToColor(themes.CurrentLine);
    public override Color MenuItemBorder => ColorUtils.HexToColor(themes.CurrentLine);
    public override Color MenuItemSelected => ColorUtils.HexToColor(themes.Selection);
    public override Color MenuStripGradientBegin => ColorUtils.HexToColor(themes.Status, 1);
    public override Color MenuStripGradientEnd => ColorUtils.HexToColor(themes.Status, 1);
    public override Color MenuItemSelectedGradientBegin => ColorUtils.HexToColor(themes.Selection);
    public override Color MenuItemSelectedGradientEnd => ColorUtils.HexToColor(themes.Selection);
    public override Color MenuItemPressedGradientBegin => ColorUtils.HexToColor(themes.Status, 1);
    public override Color MenuItemPressedGradientEnd => ColorUtils.HexToColor(themes.Status, 1);
}

public class ThemesRenderer : ToolStripProfessionalRenderer {
    private readonly Themes themes;

    public ThemesRenderer(Themes themes) : base(new ThemesColorTable(themes)) {
        this.themes = themes;
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
        if (e.Item is ToolStripMenuItem) {
            e.ArrowColor = ColorUtils.HexToColor(themes.Status);
        }

        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        e.TextColor = ColorUtils.HexToColor(themes.Status);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e) {
        e.Item.BackColor = ColorUtils.HexToColor(themes.Status, 1);
        base.OnRenderItemBackground(e);
    }
}

public static class ColorUtils {
    private static readonly Regex HexChar = new(@"^[0-9a-f]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Color ErrorColor = Color.FromArgb(128, 255, 0, 0);
    private static readonly TextStyle ErrorTextStyle = new(Brushes.White, new SolidBrush(ErrorColor), FontStyle.Regular);

    public static TextStyle CreateTextStyle(List<string> colors) {
        if (colors == null || colors.Count == 0) {
            return ErrorTextStyle;
        }

        if (TryHexToColor(colors[0], out Color color)) {
            if (colors.Count == 1) {
                return new TextStyle(new SolidBrush(color), null, FontStyle.Regular);
            } else if (TryHexToColor(colors[1], out Color backgroundColor)) {
                return new TextStyle(new SolidBrush(color), new SolidBrush(backgroundColor), FontStyle.Regular);
            } else {
                return ErrorTextStyle;
            }
        } else {
            return ErrorTextStyle;
        }
    }

    public static Color HexToColor(List<string> colors, int index = 0) {
        if (colors == null || colors.Count <= index) {
            return ErrorColor;
        }

        return TryHexToColor(colors[index], out Color color) ? color : ErrorColor;
    }

    public static bool TryHexToColor(string hex, out Color color) {
        color = ErrorColor;
        if (string.IsNullOrWhiteSpace(hex)) {
            return false;
        }

        hex = hex.Replace("#", "");
        if (!HexChar.IsMatch(hex)) {
            return false;
        }

        // 123456789 => 12345678
        if (hex.Length > 8) {
            hex = hex.Substring(0, 8);
        }

        // 123 => 112233
        // 1234 => 11223344
        if (hex.Length == 3 || hex.Length == 4) {
            hex = hex.ToCharArray().Select(c => $"{c}{c}").Aggregate((s, s1) => s + s1);
        }

        // 123456 => FF123456
        hex = hex.PadLeft(8, 'F');

        try {
            long number = Convert.ToInt64(hex, 16);
            byte a = (byte) (number >> 24);
            byte r = (byte) (number >> 16);
            byte g = (byte) (number >> 8);
            byte b = (byte) number;
            color = Color.FromArgb(a, r, g, b);
            return true;
        } catch (FormatException) {
            return false;
        }
    }
}