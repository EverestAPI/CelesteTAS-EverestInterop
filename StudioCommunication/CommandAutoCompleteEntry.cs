using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial struct CommandAutoCompleteEntry() {
    /// Entry name, displayed in the auto-complete menu
    public required string Name;
    /// Previous parts of the same argument which is prepended to the name to get the full argument name
    public string Prefix = string.Empty;
    /// Additional text displayed for the entry
    public string Extra = string.Empty;

    /// Full entry name, used for filtering entries based on the entire command argument
    public string FullName => Prefix + Name;

    /// Whether to jump to the next argument on completion
    public bool IsDone = true;
    /// Dynamically determine if the command has further arguments
    public bool? HasNext = null;

    public static implicit operator CommandAutoCompleteEntry(string entry) => new() { Name = entry, Extra = string.Empty, IsDone = true };
}
