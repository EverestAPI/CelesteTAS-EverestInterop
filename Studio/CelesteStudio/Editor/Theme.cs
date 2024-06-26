using Eto.Drawing;

namespace CelesteStudio;

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
    public Color PlayingLine;
    public Color Selection;
    public Color Savestate;
    public Color ServiceLine;
    public Color StatusFg;
    public Color StatusBg;
    
    // Text styles
    public Style Action;
    public Style Angle;
    public Style Breakpoint;
    public Style SavestateBreakpoint;
    public Style Delimiter;
    public Style Command;
    public Style Comment;
    public Style Frame;
    
    public static readonly Theme Dark = new() {
        Background = Color.FromRgb(0x282A36),
        Caret = Color.FromRgb(0xAEAFAD),
        CurrentLine = Color.FromArgb(0x29B4B6C7),
        LineNumber = Color.FromRgb(0x6272A4),
        PlayingFrame = Color.FromRgb(0xF1FA8C),
        PlayingLine = Color.FromRgb(0xF1FA8C),
        Selection = Color.FromArgb(0x20B4B6C7),
        Savestate = Color.FromRgb(0x4682B4),
        ServiceLine = Color.FromRgb(0x44475A),
        StatusFg = Color.FromRgb(0xF8F8F2),
        StatusBg = Color.FromRgb(0x383A46),
        
        Action = new Style(Color.FromRgb(0x8BE9FD)),
        Angle = new Style(Color.FromRgb(0xFF79C6)),
        Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
        SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
        Delimiter = new Style(Color.FromRgb(0x6272A4)),
        Command = new Style(Color.FromRgb(0xFFB86C)),
        Comment = new Style(Color.FromRgb(0x95B272)),
        Frame = new Style(Color.FromRgb(0xBD93F9)),
    };
}