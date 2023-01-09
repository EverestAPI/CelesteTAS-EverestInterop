using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class AutoInputCommand {
    public record Arguments {
        public readonly int StartLine;
        public readonly int CycleLength;
        public int CycleOffset;
        public string[] Inputs;
        public bool Inserting;
        public bool SkipNextInput;
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

    [TasCommand("AutoInput", ExecuteTiming = ExecuteTiming.Parse)]
    private static void AutoInput(string[] args, int _, string filePath, int fileLine) {
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
    private static void StartAutoInput(string[] _, int __, string filePath, int fileLine) {
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

        arguments.Inputs = File.ReadLines(filePath).Take(fileLine - 1).ToArray();
    }

    [TasCommand("EndAutoInput", ExecuteTiming = ExecuteTiming.Parse)]
    private static void EndAutoInput(string[] _, int __, string filePath, int fileLine) {
        EndAutoInputImpl(filePath, fileLine, "EndAutoInput", "AutoInput");
    }

    public static void EndAutoInputImpl(string filePath, int fileLine, string name, string pairedName) {
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (!AutoInputArgs.TryGetValue(filePath, out Arguments arguments)) {
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

    [TasCommand("SkipAutoInput", ExecuteTiming = ExecuteTiming.Parse)]
    private static void SkipAutoInput(string[] _, int __, string filePath, int fileLine) {
        if (AutoInputArgs.TryGetValue(filePath, out var arguments)) {
            arguments.SkipNextInput = true;
        }
    }

    public static bool TryInsert(string filePath, string lineText, int studioLine, int repeatIndex, int repeatCount) {
        if (!InputFrame.TryParse(lineText, studioLine, null, out InputFrame inputFrame)) {
            return false;
        }

        if (!AutoInputArgs.TryGetValue(filePath, out Arguments arguments) || arguments.Inputs == null || arguments.Inputs.IsEmpty()) {
            return false;
        }

        if (arguments.Inserting) {
            return false;
        }

        if (arguments.SkipNextInput) {
            arguments.SkipNextInput = false;
            return false;
        }

        bool mainFile = filePath == InputController.TasFilePath;

        int frames = 0;
        for (int i = 0; i < inputFrame.Frames; i++) {
            bool lastFrame = i == inputFrame.Frames - 1;

            if (arguments.CycleOffset == 0) {
                frames = 0;
                ParseInsertedLines(arguments, filePath, studioLine, repeatIndex, repeatCount);
                arguments.CycleOffset = arguments.CycleLength;
            }

            frames++;
            arguments.CycleOffset--;

            if (arguments.CycleOffset == 0) {
                Manager.Controller.AddFrames(frames + inputFrame.ToActionsString(), studioLine, repeatIndex, repeatCount,
                    mainFile ? Math.Max(0, i + 1 - arguments.CycleLength) : 0);
            } else if (lastFrame) {
                Manager.Controller.AddFrames(frames + inputFrame.ToActionsString(), studioLine, repeatIndex, repeatCount,
                    mainFile ? inputFrame.Frames - frames : 0);
            }
        }

        return true;
    }

    public static void ParseInsertedLines(Arguments arguments, string filePath, int studioLine, int repeatIndex, int repeatCount) {
        arguments.Inserting = true;
        Manager.Controller.ReadLines(
            arguments.Inputs,
            filePath,
            arguments.StartLine,
            filePath == InputController.TasFilePath ? arguments.StartLine - 1 : studioLine,
            repeatIndex,
            repeatCount,
            arguments.LockStudioLine
        );
        arguments.Inserting = false;
    }
}