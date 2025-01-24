using TAS.Utils;

namespace TAS.Input;

/// /// Represents a commented line in a TAS file
public readonly record struct Comment {
    public readonly int Frame;

    public readonly string FilePath;
    public readonly int FileLine;

    public readonly string Text;

    public Comment(int frame, string filePath, int fileLine, string text) {
        Frame = frame;
        FilePath = filePath;
        FileLine = fileLine;
        Text = string.IsNullOrEmpty(text)
            ? string.Empty
            : text["#".Length..].Trim();
    }
}
