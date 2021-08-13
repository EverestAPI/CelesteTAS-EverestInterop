using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

// ReSharper disable UnusedMember.Local

namespace TAS.Input {
    public static class InputCommands {
        /* Additional commands can be added by giving them the TASCommand attribute and naming them (CommandName)Command.
         * The execute at start field indicates whether a command should be executed while building the input list (read, play)
         * or when playing the file (console).
         * The args field should list formats the command takes. This is not currently used but may be implemented into Studio
         * in the future.
         * Commands that execute can be void Command(InputController, string[], int) or void Command(string[]).
         */

        private static readonly Regex SpaceRegex = new(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);

        private static readonly Lazy<PropertyInfo> GunInputCursorPosition =
            new(() => Type.GetType("Guneline.GunInput, Guneline")?.GetProperty("CursorPosition"));

        private static readonly Lazy<MethodInfo> GunlineGunshot = new(() => Type.GetType("Guneline.Guneline, Guneline")?.GetMethod("Gunshot"));

        private static string[] Split(string line) {
            string trimLine = line.Trim();
            // Determined by the first separator
            string[] args = SpaceRegex.IsMatch(trimLine) ? trimLine.Split() : trimLine.Split(',');
            return args.Select(text => text.Trim()).ToArray();
        }

        public static bool TryExecuteCommand(InputController inputController, string filePath, string lineText, int frame, int lineNumber) {
            try {
                if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
                    string[] args = Split(lineText);
                    string commandType = args[0];

                    KeyValuePair<TasCommandAttribute, MethodInfo> pair = TasCommandAttribute.FindMethod(commandType);
                    if (pair.Equals(default)) {
                        return false;
                    }

                    MethodInfo method = pair.Value;
                    TasCommandAttribute attribute = pair.Key;

                    if (Manager.EnforceLegal && !attribute.LegalInMainGame) {
                        return false;
                    }

                    string[] commandArgs = args.Skip(1).ToArray();

                    object[] parameters;
                    if (method.GetParameters().Length == 3) {
                        parameters = new object[] {inputController, commandArgs, lineNumber};
                    } else {
                        parameters = new object[] {commandArgs};
                    }

                    Action commandCall;
                    if (attribute.ExecuteAtStart) {
                        commandCall = null;
                        method.Invoke(null, parameters);
                    } else {
                        commandCall = () => method.Invoke(null, parameters);
                    }

                    if (!inputController.Commands.ContainsKey(frame)) {
                        inputController.Commands[frame] = new List<Command>();
                    }

                    inputController.Commands[frame].Add(new Command(frame, commandCall, filePath, lineNumber, lineText));

                    //the play command needs to stop reading the current file when it's done to prevent recursion
                    return commandType.Equals("play", StringComparison.InvariantCultureIgnoreCase);
                }

                return false;
            } catch (Exception e) {
                e.Log();
                return false;
            }
        }

        // "Read, Path",
        // "Read, Path, StartLine",
        // "Read, Path, StartLine, EndLine"
        [TasCommand(ExecuteAtStart = true, Name = "Read")]
        private static void ReadCommand(InputController state, string[] args, int studioLine) {
            string filePath = args[0];
            string fileDirectory = Path.GetDirectoryName(InputController.TasFilePath);
            // Check for full and shortened Read versions
            if (fileDirectory != null) {
                // Path.Combine can handle the case when filePath is an absolute path
                string absoluteOrRelativePath = Path.Combine(fileDirectory, filePath);
                if (File.Exists(absoluteOrRelativePath) && absoluteOrRelativePath != InputController.TasFilePath) {
                    filePath = absoluteOrRelativePath;
                } else {
                    string[] files = Directory.GetFiles(fileDirectory, $"{filePath}*.tas");
                    if (files.FirstOrDefault(path => path != InputController.TasFilePath) is { } shortenedFilePath) {
                        filePath = shortenedFilePath;
                    }
                }
            }

            if (!File.Exists(filePath)) {
                return;
            }

            // Find starting and ending lines
            int skipLines = 0;
            int lineLen = int.MaxValue;
            if (args.Length > 1) {
                string startLine = args[1];
                GetLine(startLine, filePath, out skipLines);
                if (args.Length > 2) {
                    string endLine = args[2];
                    GetLine(endLine, filePath, out lineLen);
                }
            }

            state.ReadFile(filePath, skipLines, lineLen, studioLine);
        }

        // "Play, StartLine",
        // "Play, StartLine, FramesToWait"
        [TasCommand(ExecuteAtStart = true, Name = "Play")]
        private static void PlayCommand(InputController state, string[] args, int studioLine) {
            GetLine(args[0], InputController.TasFilePath, out int startLine);
            if (args.Length > 1 && int.TryParse(args[1], out _)) {
                state.AddFrames(args[1], studioLine);
            }

            state.ReadFile(InputController.TasFilePath, startLine, int.MaxValue, startLine - 1);
        }

        private static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
            if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
                int curLine = 0;
                using (StreamReader sr = new(path)) {
                    while (!sr.EndOfStream) {
                        curLine++;
                        string line = sr.ReadLine().TrimEnd();
                        if (line == ("#" + labelOrLineNumber)) {
                            lineNumber = curLine;
                            return;
                        }
                    }

                    lineNumber = int.MaxValue;
                }
            }
        }

        [TasCommand(Name = "EnforceLegal")]
        private static void EnforceLegalCommand(string[] args) {
            Manager.EnforceLegal = true;
        }

        [TasCommand(ExecuteAtStart = true, Name = "Unsafe")]
        private static void UnsafeCommand(InputController state, string[] args, int studioLine) {
            Manager.AllowUnsafeInput = true;
        }

        // Gun, x, y
        [TasCommand(LegalInMainGame = false, Name = "Gun")]
        private static void GunCommand(string[] args) {
            if (args.Length < 2) {
                return;
            }

            if (float.TryParse(args[0], out float x)
                && float.TryParse(args[1], out float y)
                && Engine.Scene.Tracker.GetEntity<Player>() is { } player
                && GunInputCursorPosition.Value != null
                && GunlineGunshot.Value != null
            ) {
                Vector2 pos = new(x, y);
                GunInputCursorPosition.Value.SetValue(null, pos);
                GunlineGunshot.Value.Invoke(null, new object[] {player, pos, Facings.Left});
            }
        }
    }
}