namespace CelesteStudio.Editing.AutoCompletion;

public class Snippet {
    public bool Enabled = true;
    public string Insert = string.Empty;

    public Hotkey Hotkey = Hotkey.None;
    public string Shortcut = string.Empty;
    
    public Snippet Clone() => new() { Enabled = Enabled, Insert = Insert, Hotkey = Hotkey, Shortcut = Shortcut };
}
