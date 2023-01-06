using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RepeatCommand {
    // <filePath, Tuple<fileLine, count, startFrame>>
    private static readonly Dictionary<string, Tuple<int, int, int>> RepeatArgs = new();

    [ClearInputs]
    private static void Clear() {
        RepeatArgs.Clear();
    }

    // "Repeat, Count"
    [TasCommand("Repeat", ExecuteTiming = ExecuteTiming.Parse)]
    private static void Repeat(string[] args, int _, string filePath, int fileLine) {
        string errorText = $"On line {fileLine} of the {Path.GetFileName(filePath)} file\n";
        if (args.IsEmpty()) {
            AbortTas($"{errorText}Repeat command no count given");
        } else if (!int.TryParse(args[0], out int count)) {
            AbortTas($"{errorText}Repeat command's count is not an integer");
        } else if (RepeatArgs.ContainsKey(filePath)) {
            AbortTas($"{errorText}Nesting repeat commands are not supported");
        } else {
            RepeatArgs[filePath] = Tuple.Create(fileLine, count, Manager.Controller.Inputs.Count);
        }
    }

    // "EndRepeat"
    [TasCommand("EndRepeat", ExecuteTiming = ExecuteTiming.Parse)]
    private static void EndRepeat(string[] _, int studioLine, string filePath, int fileLine) {
        string errorText = $"On line {fileLine} of the {Path.GetFileName(filePath)} file\n";
        if (!RepeatArgs.ContainsKey(filePath)) {
            AbortTas($"{errorText} EndRepeat command does not have a paired Repeat command");
            return;
        }

        int endLine = fileLine - 1;
        int startLine = RepeatArgs[filePath].Item1 + 1;
        int count = RepeatArgs[filePath].Item2;
        int repeatStartFrame = RepeatArgs[filePath].Item3;
        RepeatArgs.Remove(filePath);

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