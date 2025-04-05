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

    /// Groups command into a meaningful order with null as a separator
    public static readonly string?[] CommandOrder = [
        "console", "Set", "Invoke", "EvalLua",
        null,
        "Repeat", "EndRepeat", "StunPause", "EndStunPause", "SkipInput", "StunPauseMode", "AnalogMode",
        null,
        "SaveAndQuitReenter", "SelectCampaign",
        null,
        "Read", "Play", "Press", "Mouse", "Gun",
        null,
        "AutoInput", "StartAutoInput", "EndAutoInput",
        null,
        "StartRecording", "StopRecording",
        null,
        "Assert", "Unsafe", "Safe", "EnforceLegal",
        null,
        "RecordCount", "FileTime", "ChapterTime", "MidwayFileTime", "MidwayChapterTime", "CompleteInfo",
        null,
        "ExportGameInfo", "EndExportGameInfo", "ExportRoomInfo", "EndExportRoomInfo",
    ];

    /// Commands which don't make sense to display to the user
    public static readonly string[] HiddenCommands = [
        "Author:", "FrameCount:", "TotalRecordCount:",
        "ExportLibTAS", "EndExportLibTAS", "Add", "Skip", "Marker",
        "ExitGame"
    ];

    /// Commands which should always be separated by spaces, regardless of the specified preference
    public static readonly string[] SpaceSeparatedCommands = [
        // Mimic in-game console
        "console",
        // Formatted as "Command: Value"
        "RecordCount:", "ChapterTime:", "MidwayChapterTime:", "FileTime:", "MidwayFileTime:", "Author:", "FrameCount:", "TotalRecordCount:",
        // Arguments are inputs, i.e contain commas
        "Add"
    ];
}
