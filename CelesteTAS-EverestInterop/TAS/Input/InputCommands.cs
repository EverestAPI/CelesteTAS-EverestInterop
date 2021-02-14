using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;

namespace TAS.Input {
    public class InputCommands {
        /* Additional commands can be added by giving them the TASCommand attribute and naming them (CommandName)Command.
         * The execute at start field indicates whether a command should be executed while building the input list (read, play)
         * or when playing the file (console).
         * The args field should list formats the command takes. This is not currently used but may be implemented into Studio
         * in the future.
         * Commands that execute at start must be void Command(InputController, string[], int).
         * Commands that execute during playback must be void Command(string[])
         */
        public static string[] Split(string line) {
            string[] args = line.Contains(",") ? line.Trim().Split(',') : line.Trim().Split();
            return args.Select(text => text.Trim()).ToArray();
        }

        public static bool TryExecuteCommand(InputController state, string lineText, int frame, int lineNumber) {
            try {
                if (char.IsLetter(lineText[0])) {
                    string[] args = Split(lineText);
                    string commandType = args[0] + "Command";
                    MethodInfo method =
                        typeof(InputCommands).GetMethod(commandType, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (method == null) {
                        return false;
                    }

                    string[] commandArgs = args.Skip(1).ToArray();
                    TASCommandAttribute attribute = (TASCommandAttribute)method.GetCustomAttribute(typeof(TASCommandAttribute));
                    if (!(Manager.enforceLegal && attribute.illegalInMaingame)) {
                        if (attribute.executeAtStart) {
                            method.Invoke(null, new object[] { state, commandArgs, lineNumber });
                            //the play command needs to stop reading the current file when it's done to prevent recursion
                            return commandType.ToLower() == "playcommand";
                        }

                        object[] parameters = { commandArgs };
                        state.commands.Add(new Command(frame, () => method.Invoke(null, parameters), lineText));
                    }
                }

                return false;
            } catch {
                return false;
            }
        }


        [TASCommand(executeAtStart = true, args = new string[] {
            "Read, Path",
            "Read, Path, StartLine",
            "Read, Path, StartLine, EndLine"
        })]
        private static void ReadCommand(InputController state, string[] args, int studioLine) {
            string filePath = args[0];
            string origFilePath = Path.GetDirectoryName(Manager.settings.TasFilePath);
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
                filePath = files[0].ToString();
                if (!File.Exists(filePath)) {
                    return;
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

        [TASCommand(illegalInMaingame = true, args = new string[] {
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

        [TASCommand(executeAtStart = true, args = new string[] {
            "Play, StartLine",
            "Play, StartLine, FramesToWait"
        })]
        private static void PlayCommand(InputController state, string[] args, int studioLine) {
            GetLine(args[0], state.tasFilePath, out int startLine);
            if (args.Length > 1 && int.TryParse(args[1], out _)) {
                state.AddFrames(args[1], studioLine);
            }

            state.ReadFile(state.tasFilePath, startLine, int.MaxValue, startLine - 1);
        }

        [TASCommand(args = new string[] {
            "StartExport",
            "StartExport Path",
            "StartExport EntitiesToTrack",
            "StartExport Path EntitiesToTrack" })]
        private static void StartExportCommand(string[] args) {
            string path = "dump.txt";
            if (args.Length > 0) {
                if (args[0].Contains(".")) {
                    path = args[0];
                    args = args.Skip(1).ToArray();
                }
            }

            Manager.BeginExport(path, args);
            Manager.ExportSyncData = true;
        }

        [TASCommand(args = new string[] { "FinishExport" })]
        private static void FinishExportCommand(string[] args) {
            Manager.EndExport();
            Manager.ExportSyncData = false;
        }

        [TASCommand(args = new string[] { "EnforceLegal" })]
        private static void EnforceLegalCommand(string[] args) {
            Manager.enforceLegal = true;
        }

        [TASCommand(executeAtStart = true, args = new string[] { "Unsafe" })]
        private static void UnsafeCommand(InputController state, string[] args, int studioLine) {
            Manager.allowUnsafeInput = true;
        }

        [TASCommand(illegalInMaingame = true, args = new string[] { "Set, Setting, Value", "Set, Mod.Setting, Value" })]
        private static void SetCommand(string[] args) {
            try {
                Type settings;
                object settingsObj;
                string setting;
                Type settingType;
                object value;
                int index = args[0].IndexOf(".", StringComparison.Ordinal);
                if (index != -1) {
                    string moduleName = args[0].Substring(0, index);
                    setting = args[0].Substring(index + 1);
                    foreach (EverestModule module in Everest.Modules) {
                        if (module.Metadata.Name == moduleName) {
                            settings = module.SettingsType;
                            settingsObj = module._Settings;
                            PropertyInfo property = settings.GetProperty(setting);
                            if (property != null) {
                                settingType = property.PropertyType;
                                property.SetValue(settingsObj, Convert.ChangeType(args[1], settingType));
                            }

                            return;
                        }
                    }
                } else {
                    setting = args[0];

                    settings = typeof(Settings);
                    FieldInfo field = settings.GetField(setting);
                    if (field != null) {
                        settingsObj = Settings.Instance;
                        settingType = field.FieldType;
                    } else {
                        settings = typeof(Assists);
                        field = settings.GetField(setting);
                        if (field == null) {
                            return;
                        }

                        settingsObj = SaveData.Instance.Assists;
                        settingType = field.FieldType;
                    }

                    try {
                        value = Convert.ChangeType(args[1], settingType);
                    } catch {
                        value = args[1];
                    }

                    if (SettingsSpecialCases(setting, settingsObj, value)) {
                        return;
                    }

                    field.SetValue(settingsObj, value);

                    if (settingsObj is Assists assists) {
                        SaveData.Instance.Assists = assists;
                    }
                }
            } catch (Exception e) {
                e.Log();
            }
        }

        private static bool SettingsSpecialCases(string setting, object obj, object value) {
            Player player;
            switch (setting) {
                case "GameSpeed":
                    SaveData.Instance.Assists.GameSpeed = (int)value;
                    Engine.TimeRateB = SaveData.Instance.Assists.GameSpeed / 10f;
                    break;
                case "MirrorMode":
                    SaveData.Instance.Assists.MirrorMode = (bool)value;
                    Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = (bool)value;
                    break;
                case "PlayAsBadeline":
                    SaveData.Instance.Assists.PlayAsBadeline = (bool)value;
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
                    SaveData.Instance.Assists.DashMode = (Assists.DashModes)Convert.ToInt32((string)value);
                    player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
                    if (player != null) {
                        player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                    }

                    break;
                case "DashAssist":
                    break;
                case "SpeedrunClock":
                    Settings.Instance.SpeedrunClock = (SpeedrunType)Convert.ToInt32((string)value);
                    break;
                default:
                    return false;
            }

            return true;
        }

        [TASCommand(illegalInMaingame = true, args = new string[] { "Gun, x, y" })]
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
                        .Invoke(null, new object[] { player, pos, false, null });
                }
            }
        }

        [TASCommand(args = new string[] { "AnalogMode, Mode" })]
        private static void AnalogModeCommand(string[] args) => AnalogueModeCommand(args);

        [TASCommand(args = new string[] { "AnalogueMode, Mode" })]
        private static void AnalogueModeCommand(string[] args) {
            if (Enum.TryParse<Manager.AnalogueMode>(args[0], true, out var mode)) {
                Manager.analogueMode = mode;
            }
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

        private static Settings origSettings;
        private static Assists? origAssists;
        private static Dictionary<EverestModule, object> origModSettings;

        [TASCommand(args = new string[] { "RestoreSettings" })]
        private static void RestoreSettingsCommand(string[] args) {
            origSettings = null;
            origAssists = null;
            origModSettings = null;

            origSettings  = Settings.Instance.ShallowClone();

            if (SaveData.Instance != null) {
                origAssists = SaveData.Instance.Assists;
            } else {
                On.Celeste.SaveData.Start -= SaveDataOnStart;
                On.Celeste.SaveData.Start += SaveDataOnStart;
            }

            origModSettings = new Dictionary<EverestModule, object>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Settings != null && module.SettingsType != null) {
                    origModSettings.Add(module, module._Settings.ShallowClone());
                }
            }
        }

        public static void TryRestoreSettings() {
            On.Celeste.SaveData.Start -= SaveDataOnStart;

            if (origSettings != null) {
                Settings.Instance.CopyAllFields(origSettings);
                origSettings = null;
            }
            if (origAssists != null) {
                SaveData.Instance.Assists = origAssists.Value;
                origAssists = null;
            }
            if (origModSettings != null) {
                foreach (EverestModule module in Everest.Modules) {
                    if (module != CelesteTASModule.Instance && module._Settings != null && origModSettings.TryGetValue(module, out object modSettings) && modSettings != null) {
                        module._Settings.CopyAllProperties(modSettings);
                        module._Settings.CopyAllFields(modSettings);
                    }
                }
                origModSettings = null;
            }
        }

        private static void SaveDataOnStart(On.Celeste.SaveData.orig_Start orig, SaveData data, int slot) {
            orig(data, slot);
            if (origAssists == null) {
                On.Celeste.SaveData.Start -= SaveDataOnStart;
                origAssists = SaveData.Instance.Assists;
            }
        }
    }
}