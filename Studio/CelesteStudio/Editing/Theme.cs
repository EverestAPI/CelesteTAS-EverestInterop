using Eto.Drawing;
using System.Collections.Generic;

namespace CelesteStudio.Editing;

public struct Style(Color foregroundColor, Color? backgroundColor = null, FontStyle fontStyle = FontStyle.None) {
    public Color ForegroundColor = foregroundColor;
    public Color? BackgroundColor = backgroundColor;
    
    public FontStyle FontStyle = fontStyle;
}

public struct Theme {
    public Color Background;
    public Color Caret;
    public Color CurrentLine;
    public Color LineNumber;
    public Color PlayingFrame;
    public Color PlayingLineFg;
    public Color PlayingLineBg;
    public Color Selection;
    public Color SavestateFg;
    public Color SavestateBg;
    public Color ServiceLine;
    public Color StatusFg;
    public Color StatusBg;
    public Color CalculateFg;
    public Color CalculateBg;
    public Color AutoCompleteFg;
    public Color AutoCompleteFgExtra;
    public Color AutoCompleteBg;
    public Color AutoCompleteHovered;
    public Color AutoCompleteSelected;
    public Color PopoutButtonBg;
    public Color PopoutButtonHovered;
    public Color PopoutButtonSelected;
    public Color SubpixelIndicatorBox;
    public Color SubpixelIndicatorDot;
    
    // Text styles
    public Style Action;
    public Style Angle;
    public Style Breakpoint;
    public Style SavestateBreakpoint;
    public Style Delimiter;
    public Style Command;
    public Style Comment;
    public Style Frame;

    public bool DarkMode;

    public Theme() {}

    public static readonly Dictionary<string, Theme> BuiltinThemes = new() {
        { "Light", new() {
            Background = Color.FromRgb(0xEBEBEB),
            Caret = Color.FromRgb(0x1B1B1B),
            CurrentLine = Color.FromArgb(0x27, 0x27, 0x27, 0x20),
            LineNumber = Color.FromRgb(0x363636),
            PlayingFrame = Color.FromArgb(0x24, 0x9D, 0x66, 0xE4),
            PlayingLineFg = Color.FromRgb(0x090909),
            PlayingLineBg = Color.FromRgb(0x47CB8E),
            Selection = Color.FromArgb(0x25, 0x63, 0xC0, 0x39),
            SavestateFg = Color.FromRgb(0xFFFFFF),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0xC6C6C6),
            StatusFg = Color.FromRgb(0x0F0F0F),
            StatusBg = Color.FromRgb(0xE1E1E1),
            CalculateFg = Color.FromRgb(0xCBCBCB),
            CalculateBg = Color.FromRgb(0x6C6C6C),
            AutoCompleteFg = Color.FromRgb(0x121212),
            AutoCompleteFgExtra = Color.FromRgb(0x595959),
            AutoCompleteBg = Color.FromRgb(0xD3D3D3),
            AutoCompleteHovered = Color.FromArgb(0x3E, 0x3E, 0x3E, 0x2E),
            AutoCompleteSelected = Color.FromArgb(0x25, 0x25, 0x25, 0x4A),
            PopoutButtonBg = Color.FromRgb(0xCFCFCF),
            PopoutButtonHovered = Color.FromRgb(0xC1C1C1),
            PopoutButtonSelected = Color.FromRgb(0xA9A9A9),
            SubpixelIndicatorBox = Color.FromRgb(0x159F15),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            Action = new Style(Color.FromRgb(0x1F7BEC)),
            Angle = new Style(Color.FromRgb(0xC835C8)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x727272)),
            Command = new Style(Color.FromRgb(0xBC6628)),
            Comment = new Style(Color.FromRgb(0x289628)),
            Frame = new Style(Color.FromRgb(0x9C32B8)),

            DarkMode = false,
        } },
        { "Dark", new() {
            Background = Color.FromRgb(0x202020),
            Caret = Color.FromRgb(0xDFDFDF),
            CurrentLine = Color.FromArgb(0x94, 0x94, 0x94, 0x2C),
            LineNumber = Color.FromRgb(0x8D8D8D),
            PlayingFrame = Color.FromArgb(0xDE, 0xB4, 0x65, 0xE7),
            PlayingLineFg = Color.FromRgb(0xFAFAFA),
            PlayingLineBg = Color.FromRgb(0xDEA73D),
            Selection = Color.FromArgb(0x25, 0x63, 0xC0, 0x47),
            SavestateFg = Color.FromRgb(0xFAFAFA),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0x4E4E4E),
            StatusFg = Color.FromRgb(0xF8F8F8),
            StatusBg = Color.FromRgb(0x303030),
            CalculateFg = Color.FromRgb(0xE8E8E8),
            CalculateBg = Color.FromRgb(0x4682B4),
            AutoCompleteFg = Color.FromRgb(0xDFDFDF),
            AutoCompleteFgExtra = Color.FromRgb(0x9F9F9F),
            AutoCompleteBg = Color.FromRgb(0x2C2C2C),
            AutoCompleteHovered = Color.FromArgb(0xC4, 0xC4, 0xC4, 0x2F),
            AutoCompleteSelected = Color.FromArgb(0xD5, 0xD5, 0xD5, 0x3D),
            PopoutButtonBg = Color.FromRgb(0x3B3B3B),
            PopoutButtonHovered = Color.FromRgb(0x4C4C4C),
            PopoutButtonSelected = Color.FromRgb(0x646464),
            SubpixelIndicatorBox = Color.FromRgb(0x29A229),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            Action = new Style(Color.FromRgb(0x8BE9FD)),
            Angle = new Style(Color.FromRgb(0xFF79C6)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x707996)),
            Command = new Style(Color.FromRgb(0xFFB86C)),
            Comment = new Style(Color.FromRgb(0xA6D76A)),
            Frame = new Style(Color.FromRgb(0xA485D0)),

            DarkMode = true,
        } },
        
        { "Legacy Light", new() {
            Background = Color.FromRgb(0xFFFFFF),
            Caret = Color.FromRgb(0x000000),
            CurrentLine = Color.FromArgb(0x20000000),
            LineNumber = Color.FromRgb(0x000000),
            PlayingFrame = Color.FromRgb(0x22A022),
            PlayingLineFg = Color.FromRgb(0x000000),
            PlayingLineBg = Color.FromRgb(0x55FF55),
            Selection = Color.FromArgb(0x20000000),
            SavestateFg = Color.FromRgb(0xFFFFFF),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0xC0C0C0),
            StatusFg = Color.FromRgb(0x000000),
            StatusBg = Color.FromRgb(0xF2F2F2),
            CalculateFg = Color.FromRgb(0xCBCBCB),
            CalculateBg = Color.FromRgb(0x6C6C6C),
            AutoCompleteFg = Color.FromRgb(0x121212),
            AutoCompleteFgExtra = Color.FromRgb(0x646464),
            AutoCompleteBg = Color.FromRgb(0xE9E9E9),
            AutoCompleteSelected = Color.FromArgb(0x44, 0x44, 0x44, 0x3F),
            AutoCompleteHovered = Color.FromArgb(0x44, 0x44, 0x44, 0x1F),
            PopoutButtonBg = Color.FromRgb(0xCFCFCF),
            PopoutButtonHovered = Color.FromRgb(0xC1C1C1),
            PopoutButtonSelected = Color.FromRgb(0xA9A9A9),
            SubpixelIndicatorBox = Color.FromRgb(0x159F15),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            Action = new Style(Color.FromRgb(0x2222FF)),
            Angle = new Style(Color.FromRgb(0xEE22EE)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x808080)),
            Command = new Style(Color.FromRgb(0xD2691E)),
            Comment = new Style(Color.FromRgb(0x00A000)),
            Frame = new Style(Color.FromRgb(0xFF2222)),

            DarkMode = false,
        } },
        { "Legacy Dark", new() {
            Background = Color.FromRgb(0x282A36),
            Caret = Color.FromRgb(0xAEAFAD),
            CurrentLine = Color.FromArgb(0x29B4B6C7),
            LineNumber = Color.FromRgb(0x6272A4),
            PlayingFrame = Color.FromRgb(0xF1FA8C),
            PlayingLineFg = Color.FromRgb(0x6272A4),
            PlayingLineBg = Color.FromRgb(0xF1FA8C),
            Selection = Color.FromArgb(0x20B4B6C7),
            SavestateFg = Color.FromRgb(0xF8F8F2),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0x44475A),
            StatusFg = Color.FromRgb(0xF8F8F2),
            StatusBg = Color.FromRgb(0x383A46),
            CalculateFg = Color.FromRgb(0xE8E8E8),
            CalculateBg = Color.FromRgb(0x4682B4),
            AutoCompleteFg = Color.FromRgb(0xDFDFDF),
            AutoCompleteFgExtra = Color.FromRgb(0x9F9F9F),
            AutoCompleteBg = Color.FromRgb(0x2F303B),
            AutoCompleteSelected = Color.FromArgb(0xBB, 0xBB, 0xC4, 0x4F),
            AutoCompleteHovered = Color.FromArgb(0xBB, 0xBB, 0xC4, 0x2F),
            PopoutButtonBg = Color.FromRgb(0x494C5F),
            PopoutButtonHovered = Color.FromRgb(0x595D74),
            PopoutButtonSelected = Color.FromRgb(0x6F738F),
            SubpixelIndicatorBox = Color.FromRgb(0x1AB353),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            Action = new Style(Color.FromRgb(0x8BE9FD)),
            Angle = new Style(Color.FromRgb(0xFF79C6)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x6272A4)),
            Command = new Style(Color.FromRgb(0xFFB86C)),
            Comment = new Style(Color.FromRgb(0x95B272)),
            Frame = new Style(Color.FromRgb(0xBD93F9)),

            DarkMode = true,
        } },
    };
}