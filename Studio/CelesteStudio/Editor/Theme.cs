using Eto.Drawing;

namespace CelesteStudio;

public struct Style(Color foregroundColor, Color? backgroundColor = null, FontStyle fontStyle = FontStyle.None) {
    public Color ForegroundColor = foregroundColor;
    public Color? BackgroundColor = backgroundColor;
    
    public FontStyle FontStyle = fontStyle;
}

public struct Theme {
    public Style Action;
    public Style Angle;
    public Style Breakpoint;
    public Style Savestate;
    public Style Delimiter;
    public Style Command;
    public Style Comment;
    public Style Frame;
    
    public static readonly Theme Dark = new() {
        Action = new Style(Color.FromRgb(0x2222FF)),
        Angle = new Style(Color.FromRgb(0xEE22EE)),
        Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555)),
        Savestate = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4)),
        Delimiter = new Style(Color.FromRgb(0x808080)),
        Command = new Style(Color.FromRgb(0xD2691E)),
        Comment = new Style(Color.FromRgb(0x00A000)),
        Frame = new Style(Color.FromRgb(0xFF2222)),
    };
}