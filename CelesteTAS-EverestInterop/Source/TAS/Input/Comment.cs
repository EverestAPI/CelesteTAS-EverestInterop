using TAS.Utils;

namespace TAS.Input;

public record Comment {
    public readonly string FilePath;
    public readonly int Frame;
    public readonly int Line;
    public readonly string Text;

    public Comment(string filePath, int frame, int line, string text) {
        FilePath = filePath;
        Frame = frame;
        Line = line;

        if (text.IsNotNullOrEmpty()) {
            text = text.Substring(1, text.Length - 1).Trim();
        }

        Text = text;
    }
}