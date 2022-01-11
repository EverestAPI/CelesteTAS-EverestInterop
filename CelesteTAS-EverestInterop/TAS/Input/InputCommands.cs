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
        private static readonly object[] EmptyParameters = { };
        private static readonly Regex CheckSpaceRegex = new(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);
        private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

        private static readonly Lazy<PropertyInfo> GunInputCursorPosition =
            new(() => Type.GetType("Guneline.GunInput, Guneline")?.GetProperty("CursorPosition"));

        private static readonly Lazy<MethodInfo> GunlineGunshot = new(() => Type.GetType("Guneline.Guneline, Guneline")?.GetMethod("Gunshot"));

        private static string[] Split(string line) {
            string trimLine = line.Trim();
            // Determined by the first separator
            string[] args = CheckSpaceRegex.IsMatch(trimLine) ? SpaceRegex.Split(trimLine) : trimLine.Split(',');
            return args.Select(text => text.Trim()).ToArray();
        }

        public static bool TryParseCommand(InputController inputController, string filePath, string lineText, int frame, int lineNumber) {
            try {
                if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
                    string[] args = Split(lineText);
                    string commandName = args[0];

                    KeyValuePair<TasCommandAttribute, MethodInfo> pair = TasCommandAttribute.FindMethod(commandName);
                    if (pair.Equals(default)) {
                        return false;
                    }

                    MethodInfo method = pair.Value;
                    TasCommandAttribute attribute = pair.Key;

                    string[] commandArgs = args.Skip(1).ToArray();

                    object[] parameters = method.GetParameters().Length switch {
                        3 => new object[] {commandArgs, inputController, lineNumber},
                        1 => new object[] {commandArgs},
                        0 => EmptyParameters,
                        _ => throw new ArgumentException()
                    };

                    Action commandCall = () => method.Invoke(null, parameters);
                    Command command = new(attribute, frame, commandCall, commandArgs, filePath, lineNumber);

                    if (attribute.ExecuteTiming == ExecuteTiming.Parse) {
                        commandCall.Invoke();
                    }

                    if (!inputController.Commands.ContainsKey(frame)) {
                        inputController.Commands[frame] = new List<Command>();
                    }

                    inputController.Commands[frame].Add(command);

                    //the play command needs to stop reading the current file when it's done to prevent recursion
                    return command.Attribute.IsName("Play");
                }

                return false;
            } catch (Exception e) {
                e.LogException("Failed to parse command.");
                return false;
            }
        }

        // "Read, Path",
        // "Read, Path, StartLine",
        // "Read, Path, StartLine, EndLine"
        [TasCommand("Read", ExecuteTiming = ExecuteTiming.Parse)]
        private static void ReadCommand(string[] args, InputController state, int studioLine) {
            string filePath = args[0];
            string fileDirectory = Path.GetDirectoryName(InputController.TasFilePath);
            filePath = FindTheFile();

            if (!File.Exists(filePath)) {
                // for compatibility with tas files downloaded from discord
                // discord will replace spaces in the file name with underscores
                filePath = args[0].Replace(" ", "_");
                FindTheFile();
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

            string FindTheFile() {
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

                return filePath;
            }
        }

        // "Play, StartLine",
        // "Play, StartLine, FramesToWait"
        [TasCommand("Play", ExecuteTiming = ExecuteTiming.Parse)]
        private static void PlayCommand(string[] args, InputController state, int studioLine) {
            GetLine(args[0], InputController.TasFilePath, out int startLine);
            if (args.Length > 1 && int.TryParse(args[1], out _)) {
                state.AddFrames(args[1], studioLine);
            }

            state.ReadFile(InputController.TasFilePath, startLine, int.MaxValue, startLine - 1);
        }

        private static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
            if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
                int curLine = 0;
                foreach (string readLine in File.ReadLines(path)) {
                    curLine++;
                    string line = readLine.Trim();
                    if (line == $"#{labelOrLineNumber}") {
                        lineNumber = curLine;
                        return;
                    }
                }

                lineNumber = int.MaxValue;
            }
        }

        [TasCommand("EnforceLegal", AliasNames = new[] {"EnforceMainGame"})]
        private static void EnforceLegalCommand() {
            Manager.EnforceLegal = true;
        }

        [TasCommand("Safe")]
        private static void SafeCommand() {
            Manager.AllowUnsafeInput = false;
        }

        [TasCommand("Unsafe")]
        private static void UnsafeCommand() {
            Manager.AllowUnsafeInput = true;
        }

        // Gun, x, y
        [TasCommand("Gun", LegalInMainGame = false)]
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