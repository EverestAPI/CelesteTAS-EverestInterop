using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Communication;
using CelesteStudio.Util;

namespace CelesteStudio.Editing;

public struct CommandInfo() {
    public struct AutoCompleteEntry() {
        public string Prefix = string.Empty;
        public string Arg;
        public string Extra = string.Empty;

        public bool Done = true;
        public bool? HasNextArg = null; 
        
        public static implicit operator AutoCompleteEntry(string arg) => new() { Arg = arg, Done = true };
    }
    
    public string Name;
    public string Description;
    public string Insert;
    
    public Func<string[], AutoCompleteEntry[]>[] AutoCompleteEntries = [];
    
    // nulls are visual separators in the insert menu
    // [] are "quick-edit" positions, with the format [index;text]. The text part is optional
    public static CommandInfo?[] AllCommands { get; private set; } = [];
    
    public static void GenerateCommandInfos(string separator) => AllCommands = [
        new CommandInfo { Name = "EnforceLegal", Insert = "EnforceLegal", Description = "This is used at the start of fullgame files.\nIt prevents the use of commands which would not be legal in a run."},
        new CommandInfo { Name = "Unsafe", Insert = "Unsafe", Description = "The TAS will normally only run inside levels.\nConsole load normally forces the TAS to load the debug save.\nUnsafe allows the TAS to run anywhere, on any save."},
        new CommandInfo { Name = "Safe", Insert = "Safe", Description = "The TAS will only run inside levels.\nConsole load forces the TAS to load the debug save."},
        null,
        new CommandInfo {
            Name = "Read", 
            Description = "Will read inputs from the specified file.",
            Insert = $"Read{separator}[0;File Name]{separator}[1;Starting Label]{separator}[2;(Ending Label)]",
            AutoCompleteEntries = [
                args => GetFilePathEntries(args[0]),
                args => GetLabelEntries(args[0]),
                args => GetLabelEntries(args[0], args[1]),
            ]
        },
        new CommandInfo { Name = "Play", Insert = $"Play{separator}[0;Starting Line]", Description = "A simplified Read command which skips to the starting line in the current file.\nUseful for splitting a large level into larger chunks."},
        null,
        new CommandInfo { Name = "Repeat", Insert = $"Repeat{separator}[0;2]{Document.NewLine}    [1]{Document.NewLine}EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported."  },
        new CommandInfo { Name = "EndRepeat", Insert = "EndRepeat", Description = "Repeat the inputs between \"Repeat\" and \"EndRepeat\" several times, nesting is not supported." },
        null,
        new CommandInfo { 
            Name = "Set", 
            Description = "Sets the specified setting to the specified value.",
            Insert = $"Set{separator}[0;(Mod).Setting]{separator}[1;Value]",
            AutoCompleteEntries = [
                args => GetSetEntries(args[0]),
                args => GetParameterEntries("Set", args[0]),
            ]
        },
        new CommandInfo { 
            Name = "Invoke", 
            Description = "Similar to the set command, but used to invoke the method",
            Insert = $"Invoke{separator}[0;Entity.Method]{separator}[1;Parameter]",
            AutoCompleteEntries = [
                args => GetInvokeEntries(args[0]),
                args => GetParameterEntries("Invoke", args[0]),
            ]
        },
        new CommandInfo { Name = "EvalLua", Insert = $"EvalLua{separator}[0;Code]", Description = "Evaluate Lua code"},
        null,
        new CommandInfo { Name = "Press", Insert = $"Press{separator}[0;Key1{separator}Key2...]", Description = "Press the specified keys with the next input." },
        null,
        new CommandInfo {
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
        new CommandInfo { 
            Name = "StunPause", 
            Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes.",
            Insert = $"StunPause [1]{Document.NewLine}    [0]{Document.NewLine}EndStunPause", 
            AutoCompleteEntries = [
                _ => ["Simulate", "Input"]
            ]
        },
        new CommandInfo { Name = "EndStunPause", Insert = "EndStunPause", Description = "Automate pausing every other frame without doing the actual pause inputs.\nThe Simulate mode should only be used to test routes."  },
        new CommandInfo {
            Name = "StunPauseMode", 
            Description = "Specify the default mode for StunPause command.",
            Insert = $"StunPauseMode{separator}[0;Simulate/Input]",
            AutoCompleteEntries = [
                _ => ["Simulate", "Input"]
            ]
        },
        null,
        new CommandInfo { Name = "AutoInput", Insert = $"AutoInput{separator}2{Document.NewLine}   1,S,N{Document.NewLine}  10,O{Document.NewLine}StartAutoInput{Document.NewLine}    [0]{Document.NewLine}EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new CommandInfo { Name = "StartAutoInput", Insert = "StartAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs." },
        new CommandInfo { Name = "EndAutoInput", Insert = "EndAutoInput", Description = "Inserts the auto inputs every cycle length frames that is played through inputs."  },
        new CommandInfo { Name = "SkipInput", Insert = "SkipInput", Description = "Prevents the next input from being calculated in the AutoInput/StunPause cycle. Usually used to mark the freeze frames." },
        null,
        new CommandInfo { Name = "SaveAndQuitReenter", Insert = "SaveAndQuitReenter", Description = "Perform a save & quit and reenter the current save file.\nThis command must be placed directly after pressing the \"Save & Quit\" button" },
        null,
        new CommandInfo { Name = "ExportGameInfo", Insert = $"ExportGameInfo{separator}[0;dump.txt]", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        new CommandInfo { Name = "EndExportGameInfo", Insert = "EndExportGameInfo", Description = "Dumps data to a file, which can be used to analyze desyncs." },
        null,
        new CommandInfo { Name = "ExportRoomInfo", Insert = $"ExportRoomInfo{separator}[0;dump_room_info.txt]", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        new CommandInfo { Name = "EndExportRoomInfo", Insert = "EndExportRoomInfo", Description = "Dumps the elapsed time of each room to a file, which can be used to compare improvements." },
        null,
        new CommandInfo { Name = "StartRecording", Insert = "StartRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        new CommandInfo { Name = "StopRecording", Insert = "StopRecording", Description = "Creates frame-perfect recordings, no matter what hardware is used. Requires TAS Recorder" },
        null,
        new CommandInfo { Name = "Add", Insert = $"Add{separator}[0;(input line)]", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new CommandInfo { Name = "Skip", Insert = "Skip", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this." },
        new CommandInfo { Name = "Marker", Insert = "Marker", Description = "Serve as instructions to the libTAS converter.\nOdds are you don't need to worry about this."  },
        new CommandInfo { Name = "ExportLibTAS", Insert = $"ExportLibTAS{separator}[0;Celeste.ltm]", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        new CommandInfo { Name = "EndExportLibTAS", Insert = "EndExportLibTAS", Description = "Converts the TAS to the inputs portion of a LibTAS movie file." },
        null,
        new CommandInfo { Name = "CompleteInfo", Insert = $"CompleteInfo{separator}[0;A 1]", Description = "The successive comments immediately following this command will be displayed to the specified chapter complete screen."},
        new CommandInfo { Name = "RecordCount", Insert = "RecordCount: 1", Description = "Every time you run tas after modifying the current input file, the record count auto-increases by one." },
        new CommandInfo { Name = "FileTime", Insert = "FileTime:", Description = "Auto-update the file time when TAS has finished running, the file time is equal to the elapsed time during the TAS run." },
        new CommandInfo { Name = "ChapterTime", Insert = "ChapterTime:", Description = "After completing the whole level from the beginning, auto-updating the chapter time." },
        new CommandInfo { Name = "MidwayFileTime", Insert = "MidwayFileTime:", Description = "Same as FileTime, except it updates when the command is executed." },
        new CommandInfo { Name = "MidwayChapterTime", Insert = "MidwayChapterTime:", Description = "Same as ChapterTime, except it updates when the command is executed." },
        null,
        new CommandInfo { Name = "ExitGame", Insert = "ExitGame", Description = "Used to force the game when recording video with .kkapture to finish recording." },
    ];
    
    public static void ResetCache() {
        setCommandCache.Clear();
        invokeCommandCache.Clear();
        parameterCache.Clear();
    }
    
    private static AutoCompleteEntry[] GetFilePathEntries(string arg) {
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
        
        return ((AutoCompleteEntry[])[new AutoCompleteEntry { Arg = Path.Combine(subDir, "../").Replace('\\', '/'), Done = false }])
            .Concat(Directory.GetDirectories(dir)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Select(d => new AutoCompleteEntry {Arg = d[(documentDir.Length + "/".Length)..].Replace('\\', '/') + "/", Done = false})
                .OrderBy(entry => entry.Arg))
            .Concat(Directory.GetFiles(dir)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && Path.GetExtension(f) == ".tas")
                .Select(f => new AutoCompleteEntry { Arg = f[(documentDir.Length + "/".Length)..^".tas".Length].Replace('\\', '/'), Done = true })
                .OrderBy(entry => entry.Arg))
            .ToArray();
    }
    
    private static AutoCompleteEntry[] GetLabelEntries(string subPath, string after = "") {
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
            .Where(line => line.Length >= 2 && line[0] == '#' && char.IsLetter(line[1]))
            .Select(line => new AutoCompleteEntry { Arg = line[1..], Done = true }) 
            .ToArray();
        
        if (after != string.Empty) {
            for (int i = 0; i < labels.Length - 1; i++) {
                if (labels[i].Arg == after) {
                    return labels[(i + 1)..];
                }
            }
            
        }
        
        return labels;
    }
    
    // NOTE: Probably read GameData in the Celeste part, since this is using the data from there.
    private static readonly Dictionary<string, AutoCompleteEntry[]> setCommandCache = [];
    private static AutoCompleteEntry[] GetSetEntries(string argsText) {
        var args = string.Join('.', argsText.Split('.').SkipLast(1));
        if (setCommandCache.TryGetValue(args, out var entries)) {
            return entries;
        }
        
        var prefix = args.Length > 0 ? args + '.' : args;
        entries = CommunicationWrapper.GetSetCommandAutoCompleteEntries(argsText)
            .Select(entry => {
                bool final = entry[0] == '!';
                var parts = entry.Split('#');
                return new AutoCompleteEntry {
                    Prefix = prefix, 
                    Arg = parts[0][1..] + (final ? "" : "."),
                    Extra = parts[1],
                    Done = final,
                    HasNextArg = !final,
                };
            })
            .ToArray();
        setCommandCache[args] = entries;
        
        return entries;
    }
    
    private static readonly Dictionary<string, AutoCompleteEntry[]> invokeCommandCache = [];
    private static AutoCompleteEntry[] GetInvokeEntries(string argsText) {
        var args = string.Join('.', argsText.Split('.').SkipLast(1));
        if (invokeCommandCache.TryGetValue(args, out var entries)) {
            return entries;
        }
        
        var prefix = args.Length > 0 ? args + '.' : args;
        entries = CommunicationWrapper.GetInvokeCommandAutoCompleteEntries(argsText)
            .Select(entry => {
                var parts = entry.Split('#');
                return new AutoCompleteEntry {
                    Prefix = prefix,
                    Arg = parts[0][1..] + (args.Length == 0 ? "." : ""),
                    Extra = parts[1],
                    Done = args.Length != 0,
                    HasNextArg = entry[0] == '.',
                };
            })
            .ToArray();
        invokeCommandCache[args] = entries;
        
        return entries;
    }
    
    private static readonly Dictionary<string, AutoCompleteEntry[]> parameterCache = [];
    private static AutoCompleteEntry[] GetParameterEntries(string commandType, string argsText, int index = 0) {
        var args = string.Join('.', argsText.Split('.'));
        if (parameterCache.TryGetValue(args, out var entries)) {
            return entries;
        }
        
        entries = CommunicationWrapper.GetParameterAutoCompleteEntries($"{commandType};{argsText};{index}")
            .Select(entry => {
                var parts = entry.Split('#');
                return new AutoCompleteEntry {
                    Arg = parts[0][1..],
                    Extra = parts[1],
                    Done = true,
                    HasNextArg = entry[0] == '.',
                };
            })
            .ToArray();
        parameterCache[args] = entries;
        
        return entries;
    }
}