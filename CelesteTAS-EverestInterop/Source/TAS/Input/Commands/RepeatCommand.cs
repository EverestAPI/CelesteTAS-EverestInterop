using StudioCommunication;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Repeats everything between "Repeat, N" and "EndRepeat" N times
internal static class RepeatCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"""
                                 Repeat{CommandInfo.Separator}[0;2]
                                     [1]
                                 EndRepeat
                                 """;
        public bool HasArguments => true;
    }

    private readonly record struct Arguments(int StartFrame, int Count, string StartFilePath, int StartFileLine);
    private static readonly Stack<Arguments> repeatStack = new();

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (repeatStack.IsEmpty()) {
            return;
        }

        foreach (var arguments in repeatStack) {
            string errorText = $"{Path.GetFileName(arguments.StartFilePath)} line {arguments.StartFileLine}\n";
            AbortTas($"{errorText}Repeat command does not have a paired EndRepeat command");
        }
    }

    [ClearInputs]
    private static void Clear() {
        repeatStack.Clear();
    }

    // "Repeat, Count"
    [TasCommand("Repeat", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(Meta))]
    private static void Repeat(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;

        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (args.IsEmpty()) {
            AbortTas($"{errorText}Repeat command has no count specified");
            return;
        }
        if (!int.TryParse(args[0], out int count)) {
            AbortTas($"{errorText}Repeat command's count is not an integer");
            return;
        }
        if (count < 1) {
            AbortTas($"{errorText}Repeat command's count must be greater than 0");
            return;
        }

        repeatStack.Push(new Arguments(Manager.Controller.CurrentParsingFrame, count, filePath, fileLine));
    }

    // "EndRepeat"
    [TasCommand("EndRepeat", ExecuteTiming = ExecuteTiming.Parse)]
    private static void EndRepeat(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (!repeatStack.TryPop(out var arguments)) {
            AbortTas($"{errorText}EndRepeat command does not have a paired Repeat command");
            return;
        }

        // Exclude Repeat/EndRepeat from the lines
        int startLine = arguments.StartFileLine + 1;
        int endLine = fileLine - 1;
        int count = arguments.Count;
        int startFrame = arguments.StartFrame;

        // Should be impossible, but just to be sure
        if (count <= 1 || endLine < startLine || !File.Exists(filePath)) {
#if DEBUG
            throw new UnreachableException();
#else
            return;
#endif
        }

        var inputController = Manager.Controller;
        bool mainFile = filePath == inputController.FilePath;

        // Update repeat info for inputs of first loop
        // Only included main TAS file, since Read files shouldn't show repeat info
        if (mainFile) {
            for (int frame = startFrame; frame < inputController.CurrentParsingFrame; frame++) {
                inputController.Inputs[frame].RepeatIndex = 1;
                inputController.Inputs[frame].RepeatCount = count;
            }
        }

        string[] lines = File.ReadLines(filePath).Take(endLine).ToArray();
        for (int i = 2; i <= count; i++) {
            inputController.ReadLines(
                lines,
                filePath,
                startLine,
                // Only display repeat info for main file
                mainFile ? startLine - 1 : studioLine,
                mainFile ? i : 0,
                mainFile ? count : 0
            );
        }
    }
}
