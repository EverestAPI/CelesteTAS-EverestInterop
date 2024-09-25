using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Data;

public struct LegacyCommandInfo() {
    public string Name;
    public string Description;
    public string Insert;

    public Func<string[], CommandAutoCompleteEntry[]>[] AutoCompleteEntries = [];

    // nulls are visual separators in the insert menu
    // [] are "quick-edit" positions, with the format [index;text]. The text part is optional
    public static LegacyCommandInfo?[] AllCommands { get; private set; } = [];

    public static void GenerateCommandInfos(string separator) => AllCommands = [
        new LegacyCommandInfo { Name = "EnforceLegal", Insert = "EnforceLegal", Description = "This is used at the start of fullgame files.\nIt prevents the use of commands which would not be legal in a run."},
        new LegacyCommandInfo { Name = "Unsafe", Insert = "Unsafe", Description = "The TAS will normally only run inside levels.\nConsole load normally forces the TAS to load the debug save.\nUnsafe allows the TAS to run anywhere, on any save."},
        new LegacyCommandInfo { Name = "Safe", Insert = "Safe", Description = "The TAS will only run inside levels.\nConsole load forces the TAS to load the debug save."},
        null,
        new LegacyCommandInfo {
            Name = "Read",
            Description = "Will read inputs from the specified file.",
            Insert = $"Read{separator}[0;File Name]{separator}[1;Starting Label]{separator}[2;(Ending Label)]",
            AutoCompleteEntries = [
                args => GetFilePathEntries(args[0]),
                args => GetLabelEntries(args[0]),
                args => GetLabelEntries(args[0], args[1]),
            ]
        },
        new LegacyCommandInfo { Name = "Play", Insert = $"Play{separator}[0;Starting Line]", Description = "A simplified Read command which skips to the starting line in the current file.\nUseful for splitting a large level into larger chunks."},
        null,
        new LegacyCommandInfo { Name = "Repeat", Insert = $"Repeat{separator}[0;2]{Document.NewLine}    [1]{Document.NewLine}EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported."  },
        new LegacyCommandInfo { Name = "EndRepeat", Insert = "EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported." },
        null,
        // new LegacyCommandInfo {
        //     Name = "Set",
        //     Description = "Sets the specified setting to the specified value.",
        //     Insert = $"Set{separator}[0;(Mod).Setting]{separator}[1;Value]",
        // },
        // new LegacyCommandInfo {
        //     Name = "Invoke",
        //     Description = "Similar to the set command, but used to invoke the method",
        //     Insert = $"Invoke{separator}[0;Entity.Method]{separator}[1;Parameter]",
        // },
        new LegacyCommandInfo { Name = "EvalLua", Insert = $"EvalLua{separator}[0;Code]", Description = "Evaluate Lua code"},
        null,
        // new CommandInfo { Name = "Press", Insert = $"Press{separator}[0;Key1{separator}Key2...]", Description = "Press the specified keys with the next input." },
        null,
        new LegacyCommandInfo {
            Name = "AnalogMode",
            Description = """
                          Circle, Square and Precise are make sure the analogue inputs sent to the game are actually possible,
                          locking it to a circular or square deadzone, or calculating the closest position possible on a controller.
                          Odds are you don't need to worry about this.
                          """,
            Insert = $"AnalogMode{separator}",
            AutoCompleteEntries = [
                _ => ["Ignore", "Circle", "Square", "Precise"]
            ]
        },
        null,
        new LegacyCommandInfo {
            Name = "StunPause",
            Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes.",
            Insert = $"StunPause [1]{Document.NewLine}    [0]{Document.NewLine}EndStunPause",
            AutoCompleteEntries = [
                _ => ["Simulate", "Input"]
            ]
        },
        new LegacyCommandInfo { Name = "EndStunPause", Insert = "EndStunPause", Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes."  },
        new LegacyCommandInfo {
            Name = "StunPauseMode",
            Description = "Specify the default mode for StunPause command.",
            Insert = $"StunPauseMode{separator}[0;Simulate/Input]",
            AutoCompleteEntries = [
                _ => ["Simulate", "Input"]
            ]
        },
        null,
        new LegacyCommandInfo { Name = "AutoInput", Insert = $"AutoInput{separator}2{Document.NewLine}   1,S,N{Document.NewLine}  10,O{Document.NewLine}StartAutoInput{Document.NewLine}    [0]{Document.NewLine}EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new LegacyCommandInfo { Name = "StartAutoInput", Insert = "StartAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs." },
        new LegacyCommandInfo { Name = "EndAutoInput", Insert = "EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new LegacyCommandInfo { Name = "SkipInput", Insert = "SkipInput", Description = "Prevents the next input from being calculated in the AutoInput/StunPause cycle. Usually used to mark the freeze frames." },
        null,
        new LegacyCommandInfo { Name = "SaveAndQuitReenter", Insert = "SaveAndQuitReenter", Description = "Perform a save & quit and reenter the current save file.\nThis command must be placed directly after pressing the \"Save & Quit\" button" },
        null,
        new LegacyCommandInfo { Name = "ExportGameInfo", Insert = $"ExportGameInfo{separator}[0;dump.txt]", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        new LegacyCommandInfo { Name = "EndExportGameInfo", Insert = "EndExportGameInfo", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        null,
        new LegacyCommandInfo { Name = "ExportRoomInfo", Insert = $"ExportRoomInfo{separator}[0;dump_room_info.txt]", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        new LegacyCommandInfo { Name = "EndExportRoomInfo", Insert = "EndExportRoomInfo", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        null,
        new LegacyCommandInfo { Name = "StartRecording", Insert = "StartRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        new LegacyCommandInfo { Name = "StopRecording", Insert = "StopRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        null,
        new LegacyCommandInfo { Name = "Add", Insert = $"Add{separator}[0;(input line)]", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new LegacyCommandInfo { Name = "Skip", Insert = "Skip", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new LegacyCommandInfo { Name = "Marker", Insert = "Marker", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this."  },
        new LegacyCommandInfo { Name = "ExportLibTAS", Insert = $"ExportLibTAS{separator}[0;Celeste.ltm]", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        new LegacyCommandInfo { Name = "EndExportLibTAS", Insert = "EndExportLibTAS", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        null,
        new LegacyCommandInfo { Name = "CompleteInfo", Insert = $"CompleteInfo{separator}[0;A 1]", Description = "The successive comments immediately following this command will be displayed to the specified chapter complete screen."},
        new LegacyCommandInfo { Name = "RecordCount", Insert = "RecordCount: 1", Description = "Every time you run tas after modifying the current input file, the record count auto-increases by one." },
        new LegacyCommandInfo { Name = "FileTime", Insert = "FileTime:", Description = "Auto-update the file time when TAS has finished running, the file time is equal to the elapsed time during the TAS run." },
        new LegacyCommandInfo { Name = "ChapterTime", Insert = "ChapterTime:", Description = "After completing the whole level from the beginning, auto-updating the chapter time." },
        new LegacyCommandInfo { Name = "MidwayFileTime", Insert = "MidwayFileTime:", Description = "Same as FileTime, except it updates when the command is executed." },
        new LegacyCommandInfo { Name = "MidwayChapterTime", Insert = "MidwayChapterTime:", Description = "Same as ChapterTime, except it updates when the command is executed." },
        null,
        new LegacyCommandInfo { Name = "ExitGame", Insert = "ExitGame", Description = "Used to force the game when recording video with .kkapture to finish recording." },
    ];

    private static CommandAutoCompleteEntry[] GetFilePathEntries(string arg) {
        var documentPath = Studio.Instance.Editor.Document.FilePath;
        if (documentPath == Document.ScratchFile) {
            return [];
        }

        if (Path.GetDirectoryName(documentPath) is not { } documentDir) {
            return [];
        }
        var subDir = Path.GetDirectoryName(arg) ?? string.Empty;

        var dir = Path.Combine(documentDir, subDir);
        if (!Directory.Exists(dir)) {
            return [];
        }

        return ((CommandAutoCompleteEntry[])[new CommandAutoCompleteEntry { Name = Path.Combine(subDir, "../").Replace('\\', '/'), IsDone = false }])
            .Concat(Directory.GetDirectories(dir)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Select(d => new CommandAutoCompleteEntry { Name = d[(documentDir.Length + "/".Length)..].Replace('\\', '/') + "/", IsDone = false })
                .OrderBy(entry => entry.Name))
            .Concat(Directory.GetFiles(dir)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && Path.GetExtension(f) == ".tas")
                .Select(f => new CommandAutoCompleteEntry { Name = f[(documentDir.Length + "/".Length)..^".tas".Length].Replace('\\', '/'), IsDone = true })
                .OrderBy(entry => entry.Name))
            .ToArray();
    }

    private static CommandAutoCompleteEntry[] GetLabelEntries(string subPath, string after = "") {
        var documentPath = Studio.Instance.Editor.Document.FilePath;
        if (documentPath == Document.ScratchFile) {
            return [];
        }
        if (Path.GetDirectoryName(documentPath) is not { } documentDir) {
            return [];
        }

        var fullPath = Path.Combine(documentDir, $"{subPath}.tas");
        if (!File.Exists(fullPath)) {
            return [];
        }

        var labels = File.ReadAllText(fullPath)
            .ReplaceLineEndings(Document.NewLine.ToString())
            .SplitDocumentLines()
            .Where(Comment.IsLabel)
            .Select(line => new CommandAutoCompleteEntry { Name = line[1..], IsDone = true })
            .ToArray();

        if (after != string.Empty) {
            for (int i = 0; i < labels.Length - 1; i++) {
                if (labels[i].Name == after) {
                    return labels[(i + 1)..];
                }
            }

        }

        return labels;
    }
}
