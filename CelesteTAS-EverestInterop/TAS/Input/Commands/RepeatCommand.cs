using StudioCommunication;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RepeatCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"""
                                 Repeat{CommandInfo.Separator}[0;2]
                                     [1]
                                 EndRepeat
                                 """;
        public bool HasArguments => true;
    }

    private record struct Arguments(int StartLine, int Count, int StartFrame) {
        public readonly int StartLine = StartLine;
        public readonly int Count = Count;
        public readonly int StartFrame = StartFrame;
    }

    private static readonly Dictionary<string, Arguments> RepeatArgs = new();

    [ClearInputs]
    private static void Clear() {
        RepeatArgs.Clear();
    }

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (RepeatArgs.IsEmpty()) {
            return;
        }

        foreach (KeyValuePair<string, Arguments> pair in RepeatArgs) {
            string errorText = $"{Path.GetFileName(pair.Key)} line {pair.Value.StartLine - 1}\n";
            AbortTas($"{errorText}Repeat command does not have a paired EndRepeat command");
        }

        Manager.Controller.Clear();
    }

    // "Repeat, Count"
    [TasCommand("Repeat", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(Meta))]
    private static void Repeat(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (args.IsEmpty()) {
            AbortTas($"{errorText}Repeat command no count given");
        } else if (!int.TryParse(args[0], out int count)) {
            AbortTas($"{errorText}Repeat command's count is not an integer");
        } else if (RepeatArgs.ContainsKey(filePath)) {
            AbortTas($"{errorText}Nesting repeat commands are not supported");
        } else {
            if (count < 1) {
                AbortTas($"{errorText}Repeat command's count must be greater than 0");
            }

            RepeatArgs[filePath] = new Arguments(fileLine + 1, count, Manager.Controller.Inputs.Count);
        }
    }

    // "EndRepeat"
    [TasCommand("EndRepeat", ExecuteTiming = ExecuteTiming.Parse)]
    private static void EndRepeat(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (!RepeatArgs.TryGetValue(filePath, out var arguments)) {
            AbortTas($"{errorText}EndRepeat command does not have a paired Repeat command");
            return;
        }

        RepeatArgs.Remove(filePath);

        int endLine = fileLine - 1;
        int startLine = arguments.StartLine;
        int count = arguments.Count;
        int repeatStartFrame = arguments.StartFrame;

        if (count <= 1 || endLine < startLine || !File.Exists(filePath)) {
            return;
        }

        InputController inputController = Manager.Controller;
        bool mainFile = filePath == InputController.TasFilePath;

        // first loop needs to set repeat index and repeat count
        if (mainFile) {
            for (int i = repeatStartFrame; i < inputController.Inputs.Count; i++) {
                inputController.Inputs[i].RepeatIndex = 1;
                inputController.Inputs[i].RepeatCount = count;
            }
        }

        IEnumerable<string> lines = File.ReadLines(filePath).Take(endLine).ToList();
        for (int i = 2; i <= count; i++) {
            inputController.ReadLines(
                lines,
                filePath,
                startLine,
                mainFile ? startLine - 1 : studioLine,
                mainFile ? i : 0,
                mainFile ? count : 0
            );
        }
    }
}
