using MemoryPack;

namespace StudioCommunication;

/// Describes a TAS command
[MemoryPackable]
public partial record struct CommandInfo(
    string Name,
    string Description,
    string Insert,

    bool HasArguments
) {
    public const string Separator = "##SEPARATOR##"; // Placeholder to-be replaced by the actual value
}
