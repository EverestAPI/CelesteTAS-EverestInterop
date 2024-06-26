using System.Collections.Generic;
using Eto.Drawing;

namespace CelesteStudio;

public enum StyleType : byte {
    Action,
    Angle,
    Breakpoint,
    Savestate,
    Delimiter,
    Command,
    Comment,
    Frame,
}

public struct LineStyle {
    public struct Segment {
        public int StartCol, EndCol;
        public StyleType Type;
    }
    
    public Segment[] Segments;
}

public class SyntaxHighlighter {
    private Dictionary<string, LineStyle> cache = new();
    private Style[] styles = [];
    
    public SyntaxHighlighter(Theme theme)
    {
        LoadTheme(theme);
    }
    
    public void LoadTheme(Theme theme) {
        // IMPORTANT: Must be the same order as the StyleType enum!
        styles = [theme.Action, theme.Angle, theme.Breakpoint, theme.Savestate, theme.Delimiter, theme.Command, theme.Comment, theme.Frame];
    }
}