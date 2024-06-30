using Eto.Forms;

namespace CelesteStudio.Editing;

public class Snippet {
    public bool Enabled { get; set; } = true;
    public string Text { get; set; } = string.Empty;
    public Keys Shortcut { get; set; } = Keys.None;
    
    public Snippet Clone() => new() { Text = Text, Shortcut = Shortcut, Enabled = Enabled };
}