using System;

namespace CelesteStudio.Editing;

public struct CommandInfo() {
    public string Name;
    public string Description;
    public string Insert;
    
    public Func<string[], string[]>[] AutoCompleteEntires = [];
    
    // nulls are visual separators in the insert menu
    // [] are "quick-edit" positions, with the format [index;text]. The text part is optional
    public static readonly CommandInfo?[] AllCommands = [
        new CommandInfo { Name = "EnforceLegal", Insert = "EnforceLegal", Description = "This is used at the start of fullgame files.\nIt prevents the use of commands which would not be legal in a run."},
        new CommandInfo { Name = "Unsafe", Insert = "Unsafe", Description = "The TAS will normally only run inside levels.\nConsole load normally forces the TAS to load the debug save.\nUnsafe allows the TAS to run anywhere, on any save."},
        new CommandInfo { Name = "Safe", Insert = "Safe", Description = "The TAS will only run inside levels.\nConsole load forces the TAS to load the debug save."},
        null,
        new CommandInfo { Name = "Read", Insert = "Read, [0;File Name], [1;Starting Line], [2;(Ending Line)]", Description = "Will read inputs from the specified file." },
        new CommandInfo { Name = "Play", Insert = "Play, [0;Starting Line]", Description = "A simplified Read command which skips to the starting line in the current file.\nUseful for splitting a large level into larger chunks."},
        null,
        new CommandInfo { Name = "Repeat", Insert = $"Repeat, [0;2]{Document.NewLine}    [1]{Document.NewLine}EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported."  },
        new CommandInfo { Name = "EndRepeat", Insert = "EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported." },
        null,
        new CommandInfo { Name = "Set", Insert = "Set, [0;(Mod).Setting], [1;Value]", Description = "Sets the specified setting to the specified value." },
        new CommandInfo { Name = "Invoke", Insert = "Invoke, [0;Entity.Method], [1;Parameter]", Description = "Similar to the set command, but used to invoke the method"},
        new CommandInfo { Name = "EvalLua", Insert = "EvalLua, [0;Code]", Description = "Evaluate Lua code"},
        null,
        new CommandInfo { Name = "Press", Insert = "Press, [0;Key1, Key2...]", Description = "Press the specified keys with the next input." },
        null,
        new CommandInfo {
            Name = "AnalogMode", 
            Description = """
                          Circle, Square and Precise are make sure the analogue inputs sent to the game are actually possible,
                          locking it to a circular or square deadzone, or calculating the closest position possible on a controller.
                          Odds are you don't need to worry about this.
                          """,
            Insert = "AnalogMode, ",
            AutoCompleteEntires = [
                _ => ["Ignore", "Circle", "Square", "Precise"]
            ]
        },
        null,
        new CommandInfo { Name = "StunPause", Insert = $"StunPause{Document.NewLine}    [0]{Document.NewLine}EndStunPause", Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes." },
        new CommandInfo { Name = "EndStunPause", Insert = "EndStunPause", Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes."  },
        new CommandInfo { Name = "StunPauseMode", Insert = "StunPauseMode, [0;Simulate/Input]", Description = "Specify the default mode for StunPause command." },
        null,
        new CommandInfo { Name = "AutoInput", Insert = $"AutoInput, 2{Document.NewLine}   1,S,N{Document.NewLine}  10,O{Document.NewLine}StartAutoInput{Document.NewLine}    [0]{Document.NewLine}EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new CommandInfo { Name = "StartAutoInput", Insert = "StartAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs." },
        new CommandInfo { Name = "EndAutoInput", Insert = "EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new CommandInfo { Name = "SkipInput", Insert = "SkipInput", Description = "Prevents the next input from being calculated in the AutoInput/StunPause cycle. Usually used to mark the freeze frames." },
        null,
        new CommandInfo { Name = "SaveAndQuitReenter", Insert = "SaveAndQuitReenter", Description = "Perform a save & quit and reenter the current save file.\nThis command must be placed directly after pressing the \"Save & Quit\" button" },
        null,
        new CommandInfo { Name = "ExportGameInfo", Insert = "ExportGameInfo [0;dump.txt]", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        new CommandInfo { Name = "EndExportGameInfo", Insert = "EndExportGameInfo", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        null,
        new CommandInfo { Name = "ExportRoomInfo", Insert = "ExportRoomInfo [0;dump_room_info.txt]", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        new CommandInfo { Name = "EndExportRoomInfo", Insert = "EndExportRoomInfo", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        null,
        new CommandInfo { Name = "StartRecording", Insert = "StartRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        new CommandInfo { Name = "StopRecording", Insert = "StopRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        null,
        new CommandInfo { Name = "Add", Insert = "Add, [0;(input line)]", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new CommandInfo { Name = "Skip", Insert = "Skip", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new CommandInfo { Name = "Marker", Insert = "Marker", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this."  },
        new CommandInfo { Name = "ExportLibTAS", Insert = "ExportLibTAS [0;Celeste.ltm]", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        new CommandInfo { Name = "EndExportLibTAS", Insert = "EndExportLibTAS", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        null,
        new CommandInfo { Name = "CompleteInfo", Insert = "CompleteInfo [0;A 1]", Description = "The successive comments immediately following this command will be displayed to the specified chapter complete screen."},
        new CommandInfo { Name = "RecordCount", Insert = "RecordCount: 1", Description = "Every time you run tas after modifying the current input file, the record count auto-increases by one." },
        new CommandInfo { Name = "FileTime", Insert = "FileTime:", Description = "Auto-update the file time when TAS has finished running, the file time is equal to the elapsed time during the TAS run." },
        new CommandInfo { Name = "ChapterTime", Insert = "ChapterTime:", Description = "After completing the whole level from the beginning, auto-updating the chapter time." },
        new CommandInfo { Name = "MidwayFileTime", Insert = "MidwayFileTime:", Description = "Same as FileTime, except it updates when the command is executed." },
        new CommandInfo { Name = "MidwayChapterTime", Insert = "MidwayChapterTime:", Description = "Same as ChapterTime, except it updates when the command is executed." },
        null,
        new CommandInfo { Name = "ExitGame", Insert = "ExitGame", Description = "Used to force the game when recording video with .kkapture to finish recording." },
    ];
}