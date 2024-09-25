using MemoryPack;

namespace StudioCommunication;

/// Describes a TAS command
[MemoryPackable]
public partial record struct CommandInfo(
    // Name of the command
    string Name,
    // Snippet to insert when auto-completing the command
    string Insert,

    // Whether to automatically open the auto-complete menu for arguments
    bool HasArguments
) {
    public const string Separator = "##SEPARATOR##"; // Placeholder to-be replaced by the actual value
}
