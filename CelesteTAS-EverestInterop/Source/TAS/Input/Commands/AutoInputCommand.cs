using StudioCommunication;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class AutoInputCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"""
                                 AutoInput{CommandInfo.Separator}[0;2]
                                    1,S,N
                                   10,O
                                 StartAutoInput
                                     [1]
                                 EndAutoInput
                                 """;
        public bool HasArguments => true;
    }

    public record Arguments {
        public readonly int StartLine;
        public readonly int CycleLength;
        public int CycleOffset;
        public List<string>? Inputs;
        public bool Inserting;
        public bool SkipNextInput;
        public int SkipFrames;
        public int SkipWaitingFrames;
        public bool LockStudioLine;
        public bool StunPause;

        public Arguments(int startLine, int cycleLength) {
            StartLine = startLine;
            CycleOffset = CycleLength = cycleLength;
        }
    }

    public static readonly Dictionary<string, Arguments> AutoInputArgs = new();

    [ClearInputs]
    private static void Clear() {
        AutoInputArgs.Clear();
    }

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (AutoInputArgs.IsEmpty()) {
            return;
        }

        foreach (KeyValuePair<string, Arguments> pair in AutoInputArgs) {
            string errorText = $"{Path.GetFileName(pair.Key)} line {pair.Value.StartLine - 1}\n";
            string name = pair.Value.StunPause ? "StunPause" : "StartAutoInput";
            string pairedName = pair.Value.StunPause ? "EndStunPause" : "EndAutoInput";
            AbortTas(pair.Value.Inputs == null
                ? $"{errorText}AutoInput command does not have a paired StartAutoInput command"
                : $"{errorText}{name} command does not have a paired {pairedName} command");
        }

        Manager.Controller.Clear();
    }

    [TasCommand("AutoInput", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(Meta))]
    private static void AutoInput(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (args.IsEmpty()) {
            AbortTas($"{errorText}AutoInput command no cycle length given");
        } else if (!int.TryParse(args[0], out int cycleLength)) {
            AbortTas($"{errorText}AutoInput command's cycle length is not an integer");
        } else if (AutoInputArgs.ContainsKey(filePath)) {
            AbortTas($"{errorText}Nesting AutoInput commands are not supported");
        } else {
            if (cycleLength <= 0) {
                AbortTas($"{errorText}AutoInput command's cycle length must be greater than 0");
            }

            AutoInputArgs[filePath] = new Arguments(fileLine + 1, cycleLength);
        }
    }

    [TasCommand("StartAutoInput", ExecuteTiming = ExecuteTiming.Parse)]
    private static void StartAutoInput(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (!AutoInputArgs.TryGetValue(filePath, out var arguments)) {
            AbortTas($"{errorText}StartAutoInput command does not have a paired AutoInput command");
            return;
        }

        if (arguments.Inputs != null) {
            AutoInputArgs.Remove(filePath);
            AbortTas($"{errorText}StartAutoInput command already exists");
            return;
        }

        arguments.Inputs = File.ReadLines(filePath).Take(fileLine - 1).ToList();
    }

    [TasCommand("EndAutoInput", ExecuteTiming = ExecuteTiming.Parse)]
    private static void EndAutoInput(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        EndAutoInputImpl(filePath, fileLine, "EndAutoInput", "AutoInput");
    }

    public static void EndAutoInputImpl(string filePath, int fileLine, string name, string pairedName) {
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (!AutoInputArgs.TryGetValue(filePath, out var arguments)) {
            AbortTas($"{errorText}{name} command does not have a paired {pairedName} command");
            return;
        }

        if (arguments.Inputs == null) {
            AutoInputArgs.Remove(filePath);
            AbortTas($"{errorText}EndAutoInput command does not have a paired StartAutoInput command");
            return;
        }

        AutoInputArgs.Remove(filePath);
    }

    [TasCommand("SkipInput", Aliases = ["SkipAutoInput"], ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void SkipInput(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (Command.Parsing && AutoInputArgs.TryGetValue(filePath, out var arguments)) {
            string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\nSkipInput command's ";
            if (args.IsEmpty()) {
                arguments.SkipNextInput = true;
                arguments.SkipFrames = 0;
                arguments.SkipWaitingFrames = 0;
            } else if (!int.TryParse(args[0], out int frames)) {
                AbortTas($"{errorText}first parameter is not an integer");
            } else {
                if (frames <= 0) {
                    AbortTas($"{errorText}first parameter must be greater than 0");
                    return;
                }

                arguments.SkipNextInput = false;
                arguments.SkipFrames = frames;

                if (args.Length >= 2) {
                    if (int.TryParse(args[1], out int waitFrames)) {
                        if (waitFrames < 0) {
                            AbortTas($"{errorText}second parameter must be greater than or equal 0");
                            return;
                        }

                        arguments.SkipWaitingFrames = waitFrames;
                    } else {
                        AbortTas($"{errorText}second parameter is not an integer");
                    }
                } else {
                    arguments.SkipWaitingFrames = 0;
                }
            }
        } else if (!Command.Parsing) {
            StunPauseCommand.SkipInput(args, filePath, fileLine);
        }
    }

    public static bool TryInsert(string filePath, string lineText, int studioLine, int repeatIndex, int repeatCount) {
        if (!InputFrame.TryParse(lineText, studioLine, null, out var inputFrame)) {
            return false;
        }

        if (!AutoInputArgs.TryGetValue(filePath, out var arguments) || arguments.Inputs == null || arguments.Inputs.IsEmpty()) {
            return false;
        }

        if (arguments.Inserting) {
            return false;
        }

        if (arguments.SkipNextInput) {
            arguments.SkipNextInput = false;
            return false;
        }

        bool mainFile = filePath == Manager.Controller.FilePath;

        int frames = 0;
        int parsedFrames = 0;
        for (int i = 0; i < inputFrame.Frames; i++) {
            if (arguments.SkipFrames > 0) {
                arguments.SkipWaitingFrames--;
                if (arguments.SkipWaitingFrames == -1) {
                    arguments.CycleOffset += arguments.SkipFrames;
                    arguments.SkipFrames = 0;
                    arguments.SkipWaitingFrames = 0;
                }
            }

            if (arguments.CycleOffset == 0) {
                ParseInsertedLines(arguments, filePath, studioLine, repeatIndex, repeatCount);
                arguments.CycleOffset = arguments.CycleLength;
            }

            frames++;
            arguments.CycleOffset--;

            if (arguments.CycleOffset == 0 || i == inputFrame.Frames - 1) {
                Manager.Controller.AddFrames(inputFrame with {
                    Frames = frames,
                    Line = studioLine,
                    RepeatCount = repeatCount,
                    RepeatIndex = repeatIndex,
                    FrameOffset = mainFile ? parsedFrames : 0,
                });

                parsedFrames += frames;
                frames = 0;
            }
        }

        return true;
    }

    public static void ParseInsertedLines(Arguments arguments, string filePath, int studioLine, int repeatIndex, int repeatCount) {
        if (arguments.StunPause) {
            StunPauseCommand.UpdatePauseInputs(arguments);
        }

        arguments.Inserting = true;
        Manager.Controller.ReadLines(
            arguments.Inputs!,
            filePath,
            arguments.StartLine,
            filePath == Manager.Controller.FilePath ? arguments.StartLine - 1 : studioLine,
            repeatIndex,
            repeatCount,
            arguments.LockStudioLine
        );
        arguments.Inserting = false;
    }
}
