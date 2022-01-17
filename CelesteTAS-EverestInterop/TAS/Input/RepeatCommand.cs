using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod;
using TAS.Utils;

namespace TAS.Input {
    public static class RepeatCommand {
        // <filePath, Tuple<fileLine, count, startFrame>>
        private static readonly Dictionary<string, Tuple<int, int, int>> RepeatArgs = new();

        public static void Clear() {
            RepeatArgs.Clear();
        }

        // "Repeat, Count"
        [TasCommand("Repeat", ExecuteTiming = ExecuteTiming.Parse)]
        private static void Repeat(string[] args, int _, string filePath, int fileLine) {
            if (args.Length > 0 && int.TryParse(args[0], out int count)) {
                if (RepeatArgs.ContainsKey(filePath)) {
                    $"The Repeat command on line {fileLine} of the {filePath} file does not have a paired EndRepeat command".Log(LogLevel.Warn);
                }

                RepeatArgs[filePath] = Tuple.Create(fileLine, count, Manager.Controller.Inputs.Count);
            }
        }

        // "EndRepeat"
        [TasCommand("EndRepeat", ExecuteTiming = ExecuteTiming.Parse)]
        private static void EndRepeat(string[] args, int studioLine, string filePath, int fileLine) {
            if (!RepeatArgs.ContainsKey(filePath)) {
                $"The EndRepeat command on line {fileLine} of the {filePath} file does not have a paired Repeat command".Log(LogLevel.Warn);
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
}