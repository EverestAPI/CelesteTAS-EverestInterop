using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.Input {
    public static class InputCommands {
        /* Additional commands can be added by giving them the TASCommand attribute and naming them (CommandName)Command.
         * The execute at start field indicates whether a command should be executed while building the input list (read, play)
         * or when playing the file (console).
         * The args field should list formats the command takes. This is not currently used but may be implemented into Studio
         * in the future.
         * Commands that execute can be void Command(InputController, string[], int) or void Command(string[]).
         */
        private static readonly Regex SpaceRegex = new Regex(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);

        private static string[] Split(string line) {
            string trimLine = line.Trim();
            // Determined by the first separator
            string[] args = SpaceRegex.IsMatch(trimLine) ? trimLine.Split() : trimLine.Split(',');
            return args.Select(text => text.Trim()).ToArray();
        }

        public static bool TryExecuteCommand(InputController inputController, string lineText, int frame, int lineNumber) {
            try {
                if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
                    string[] args = Split(lineText);
                    string commandType = args[0] + "Command";
                    MethodInfo method =
                        typeof(InputCommands).GetMethod(commandType, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (method == null) {
                        return false;
                    }

                    string[] commandArgs = args.Skip(1).ToArray();
                    TasCommandAttribute attribute = (TasCommandAttribute) method.GetCustomAttribute(typeof(TasCommandAttribute));
                    if (!(Manager.EnforceLegal && attribute.IllegalInMaingame)) {
                        object[] parameters;
                        if (method.GetParameters().Length == 3) {
                            parameters = new object[] {inputController, commandArgs, lineNumber};
                        } else {
                            parameters = new object[] {commandArgs};
                        }

                        if (attribute.ExecuteAtStart) {
                            method.Invoke(null, parameters);
                            //the play command needs to stop reading the current file when it's done to prevent recursion
                            return commandType.ToLower() == "playcommand";
                        }

                        if (!inputController.Commands.ContainsKey(frame)) {
                            inputController.Commands[frame] = new List<Command>();
                        }

                        inputController.Commands[frame].Add(new Command(frame, () => method.Invoke(null, parameters), lineText));
                    }
                }

                return false;
            } catch (Exception e) {
                e.Log();
                return false;
            }
        }


        [TasCommand(ExecuteAtStart = true, Args = new string[] {
            "Read, Path",
            "Read, Path, StartLine",
            "Read, Path, StartLine, EndLine"
        })]
        private static void ReadCommand(InputController state, string[] args, int studioLine) {
            string filePath = args[0];
            string origFilePath = Path.GetDirectoryName(InputController.StudioTasFilePath);
            // Check for full and shortened Read versions for absolute path
            if (origFilePath != null) {
                string altFilePath = origFilePath + Path.DirectorySeparatorChar + filePath;
                if (File.Exists(altFilePath)) {
                    filePath = altFilePath;
                } else {
                    string[] files = Directory.GetFiles(origFilePath, $"{filePath}*.tas");
                    if (files.Length != 0) {
                        filePath = files[0].ToString();
                    }
                }
            }

            // Check for full and shortened Read versions for relative path
            if (!File.Exists(filePath)) {
                string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(), $"{filePath}*.tas");
                if (files.Length > 0) {
                    filePath = files[0].ToString();
                    if (!File.Exists(filePath)) {
                        return;
                    }
                }
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

        [TasCommand(ExecuteAtStart = true, Args = new string[] {
            "Play, StartLine",
            "Play, StartLine, FramesToWait"
        })]
        private static void PlayCommand(InputController state, string[] args, int studioLine) {
            GetLine(args[0], state.TasFilePath, out int startLine);
            if (args.Length > 1 && int.TryParse(args[1], out _)) {
                state.AddFrames(args[1], studioLine);
            }

            state.ReadFile(state.TasFilePath, startLine, int.MaxValue, startLine - 1);
        }

        private static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
            if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
                int curLine = 0;
                using (StreamReader sr = new StreamReader(path)) {
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

        [TasCommand(IllegalInMaingame = true, Args = new string[] {
            "Console CommandType",
            "Console CommandType CommandArgs",
            "Console LoadCommand IDorSID",
            "Console LoadCommand IDorSID Screen",
            "Console LoadCommand IDorSID Screen Checkpoint",
            "Console LoadCommand IDorSID X Y"
        })]
        private static void ConsoleCommand(string[] args) {
            ConsoleHandler.ExecuteCommand(args);
        }

        [TasCommand(Args = new string[] {
            "StartExport",
            "StartExport Path",
            "StartExport EntitiesToTrack",
            "StartExport Path EntitiesToTrack"
        })]
        private static void StartExportCommand(string[] args) {
            string path = "dump.txt";
            if (args.Length > 0) {
                if (args[0].Contains(".")) {
                    path = args[0];
                    args = args.Skip(1).ToArray();
                }
            }

            PlayerInfo.BeginExport(path, args);
            PlayerInfo.ExportSyncData = true;
        }

        [TasCommand(Args = new string[] {"FinishExport"})]
        private static void FinishExportCommand(string[] args) {
            PlayerInfo.EndExport();
            PlayerInfo.ExportSyncData = false;
        }

        [TasCommand(Args = new string[] {"EnforceLegal"})]
        private static void EnforceLegalCommand(string[] args) {
            Manager.EnforceLegal = true;
        }

        [TasCommand(ExecuteAtStart = true, Args = new string[] {"Unsafe"})]
        private static void UnsafeCommand(InputController state, string[] args, int studioLine) {
            Manager.AllowUnsafeInput = true;
        }

        [TasCommand(IllegalInMaingame = true, Args = new string[] {"Set, Setting, Value", "Set, Mod.Setting, Value"})]
        private static void SetCommand(string[] args) {
            if (args.Length < 2) {
                return;
            }

            try {
                Type settingsType;
                Type valueType;
                object settings;
                string settingName;
                int index = args[0].IndexOf(".", StringComparison.Ordinal);
                if (index != -1) {
                    string moduleName = args[0].Substring(0, index);
                    settingName = args[0].Substring(index + 1);
                    foreach (EverestModule module in Everest.Modules) {
                        if (module.Metadata.Name == moduleName) {
                            settingsType = module.SettingsType;
                            settings = module._Settings;
                            PropertyInfo property = settingsType.GetProperty(settingName);
                            if (property != null) {
                                valueType = property.PropertyType;
                                property.SetValue(settings, ConvertType(args[1], valueType));
                            }

                            return;
                        }
                    }
                } else {
                    settingName = args[0];

                    settingsType = typeof(Settings);
                    FieldInfo field = settingsType.GetField(settingName);
                    if (field != null) {
                        settings = Settings.Instance;
                        valueType = field.FieldType;
                    } else {
                        settingsType = typeof(Assists);
                        field = settingsType.GetField(settingName);
                        if (field == null) {
                            return;
                        }

                        settings = SaveData.Instance.Assists;
                        valueType = field.FieldType;
                    }

                    object value = ConvertType(args[1], valueType);

                    if (SettingsSpecialCases(settingName, value)) {
                        return;
                    }

                    field.SetValue(settings, value);

                    if (settings is Assists assists) {
                        SaveData.Instance.Assists = assists;
                    }
                }
            } catch (Exception e) {
                e.Log();
            }
        }

        private static object ConvertType(string value, Type type) {
            try {
                return type.IsEnum ? Enum.Parse(type, value, true) : Convert.ChangeType(value, type);
            } catch {
                return value;
            }
        }

        private static bool SettingsSpecialCases(string setting, object value) {
            Player player;
            switch (setting) {
                case "GameSpeed":
                    SaveData.Instance.Assists.GameSpeed = (int) value;
                    Engine.TimeRateB = SaveData.Instance.Assists.GameSpeed / 10f;
                    break;
                case "MirrorMode":
                    SaveData.Instance.Assists.MirrorMode = (bool) value;
                    Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = (bool) value;
                    break;
                case "PlayAsBadeline":
                    SaveData.Instance.Assists.PlayAsBadeline = (bool) value;
                    player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
                    if (player != null) {
                        PlayerSpriteMode mode = SaveData.Instance.Assists.PlayAsBadeline
                            ? PlayerSpriteMode.MadelineAsBadeline
                            : player.DefaultSpriteMode;
                        if (player.Active) {
                            player.ResetSpriteNextFrame(mode);
                        } else {
                            player.ResetSprite(mode);
                        }
                    }

                    break;
                case "DashMode":
                    SaveData.Instance.Assists.DashMode = (Assists.DashModes) value;
                    player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
                    if (player != null) {
                        player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                    }

                    break;
                case "DashAssist":
                    break;
                case "SpeedrunClock":
                    Settings.Instance.SpeedrunClock = (SpeedrunType) value;
                    break;
                default:
                    return false;
            }

            return true;
        }

        [TasCommand(IllegalInMaingame = true, Args = new string[] {"Gun, x, y"})]
        private static void GunCommand(string[] args) {
            int x = int.Parse(args[0]);
            int y = int.Parse(args[1]);
            Player player = Engine.Scene.Tracker.GetEntity<Player>();
            Vector2 pos = new Vector2(x, y);
            foreach (EverestModule module in Everest.Modules) {
                if (module.Metadata.Name == "Guneline") {
                    module.GetType().Assembly.GetType("Guneline.GunInput").GetProperty("CursorPosition").SetValue(null, pos);
                    //typeof(MouseState).GetProperty("LeftButton").SetValue(MInput.Mouse.CurrentState, ButtonState.Pressed);
                    module.GetType().Assembly.GetType("Guneline.Guneline").GetMethod("Gunshot")
                        .Invoke(null, new object[] {player, pos, false, null});
                }
            }
        }

        [TasCommand(Args = new string[] {"AnalogMode, Mode",}, ExecuteAtStart = true)]
        private static void AnalogModeCommand(string[] args) => AnalogueModeCommand(args);

        [TasCommand(Args = new string[] {"AnalogueMode, Mode",}, ExecuteAtStart = true)]
        private static void AnalogueModeCommand(string[] args) {
            if (args.Length > 0 && Enum.TryParse(args[0], true, out AnalogueMode mode)) {
                AnalogHelper.AnalogModeChange(mode);
            }
        }

        [TasCommand(Args = new string[] {"StartExportLibTAS (Optional Path)"}, ExecuteAtStart = true)]
        private static void StartExportLibTasCommand(string[] args) {
            string path = "libTAS_inputs.txt";
            if (args.Length > 0) {
                path = args[0];
            }

            LibTasHelper.StartExport(path);
        }

        [TasCommand(Args = new string[] {"FinishExportLibTAS"}, ExecuteAtStart = true)]
        private static void FinishExportLibTasCommand(string[] args) {
            LibTasHelper.FinishExport();
        }

        [TasCommand(Args = new string[] {"Add, input"}, ExecuteAtStart = true)]
        private static void AddCommand(string[] args) {
            if (args.Length > 0) {
                LibTasHelper.AddInputFrame(string.Join(",", args));
            }
        }

        [TasCommand(Args = new string[] {"Skip"}, ExecuteAtStart = true)]
        private static void SkipCommand(string[] args) {
            LibTasHelper.SkipNextInput();
        }
    }
}