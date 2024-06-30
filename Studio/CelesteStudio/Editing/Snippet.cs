using Eto.Forms;

namespace CelesteStudio.Editing;

public class Snippet {
    public string Text { get; set; } = string.Empty;
    public Keys Shortcut { get; set; } = Keys.None;
}