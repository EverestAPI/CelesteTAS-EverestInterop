namespace CelesteStudio.Editing;

public struct CommandInfo {
    public string Name;
    public string Description;
    public string Insert;
    
    // nulls are visual separators in the insert menu
    // [] are "quick-edit" positions, with the format [index;text]. The text part is optional
    public static readonly CommandInfo?[] AllCommands = [
        new CommandInfo { Name = "EnforceLegal", Insert = "EnforceLegal" },
        new CommandInfo { Name = "Unsafe", Insert = "Unsafe" },
        new CommandInfo { Name = "Safe", Insert = "Safe" },
        null,
        new CommandInfo { Name = "Read", Insert = "Read, [0;File Name], [1;Starting Line], [2;(Ending Line)]" },
        new CommandInfo { Name = "Play", Insert = "Play, [0;Starting Line]" },
        null,
        new CommandInfo { Name = "Repeat", Insert = $"Repeat, 2{Document.NewLine}    [0]{Document.NewLine}EndRepeat" },
        new CommandInfo { Name = "EndRepeat", Insert = "EndRepeat" },
        null,
        new CommandInfo { Name = "Set", Insert = "Set, [0;(Mod).Setting], [1;Value]" },
        new CommandInfo { Name = "Invoke", Insert = "Invoke, [0;Entity.Method], [1;Parameter]" },
        new CommandInfo { Name = "EvalLua", Insert = "EvalLua, [0;Code]" },
        null,
        new CommandInfo { Name = "Press", Insert = "Press, [0;Key1, Key2...]" },
        null,
        new CommandInfo { Name = "AnalogMode", Insert = "AnalogMode, [0;Ignore/Circle/Square/Precise]" },
        null,
        new CommandInfo { Name = "StunPause", Insert = $"StunPause{Document.NewLine}    [0]{Document.NewLine}EndStunPause" },
        new CommandInfo { Name = "EndStunPause", Insert = "EndStunPause" },
        new CommandInfo { Name = "StunPauseMode", Insert = "StunPauseMode, [0;Simulate/Input]" },
        null,
        new CommandInfo { Name = "AutoInput", Insert = $"AutoInput, 2{Document.NewLine}   1,S,N{Document.NewLine}  10,O{Document.NewLine}StartAutoInput{Document.NewLine}    [0]{Document.NewLine}EndAutoInput" },
        new CommandInfo { Name = "StartAutoInput", Insert = "StartAutoInput" },
        new CommandInfo { Name = "EndAutoInput", Insert = "EndAutoInput" },
        new CommandInfo { Name = "SkipInput", Insert = "SkipInput" },
        null,
        new CommandInfo { Name = "SaveAndQuitReenter", Insert = "SaveAndQuitReenter" },
        null,
        new CommandInfo { Name = "ExportGameInfo", Insert = "ExportGameInfo [0;dump.txt]" },
        new CommandInfo { Name = "EndExportGameInfo", Insert = "EndExportGameInfo" },
        null,
        new CommandInfo { Name = "StartRecording", Insert = "StartRecording" },
        new CommandInfo { Name = "StopRecording", Insert = "StopRecording" },
        null,
        new CommandInfo { Name = "Add", Insert = "Add, [0;(input line)]" },
        new CommandInfo { Name = "Skip", Insert = "Skip" },
        new CommandInfo { Name = "Marker", Insert = "Marker" },
        new CommandInfo { Name = "ExportLibTAS", Insert = "ExportLibTAS [0;Celeste.ltm]" },
        new CommandInfo { Name = "EndExportLibTAS", Insert = "EndExportLibTAS" },
        null,
        new CommandInfo { Name = "CompleteInfo", Insert = "CompleteInfo [0;A 1]" },
        new CommandInfo { Name = "RecordCount", Insert = "RecordCount: 1" },
        new CommandInfo { Name = "FileTime", Insert = "FileTime:" },
        new CommandInfo { Name = "ChapterTime", Insert = "ChapterTime:" },
        new CommandInfo { Name = "MidwayFileTime", Insert = "MidwayFileTime:" },
        new CommandInfo { Name = "MidwayChapterTime", Insert = "MidwayChapterTime:" },
        null,
        new CommandInfo { Name = "ExitGame", Insert = "ExitGame" },
    ];
}