using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace TAS.Input {
    public class InputController {
        private string checksum;
        private int commandIndex;
        public List<Command> Commands = new List<Command>();
        public List<FastForward> FastForwards = new List<FastForward>();
        public int FfIndex;
        private int initializationFrameCount;

        public List<InputFrame> Inputs = new List<InputFrame>();

        public Vector2? ResetSpawn;
        public int StudioFrameCount;

        private Dictionary<string, DateTime> usedFiles = new Dictionary<string, DateTime>();


        public InputController() { }

        public string TasFilePath {
            get {
                string path = string.IsNullOrEmpty(Manager.Settings.TasFilePath) ? "Celeste.tas" : Manager.Settings.TasFilePath;
                if (!File.Exists(path)) {
                    File.WriteAllText(path, string.Empty);
                }

                return path;
            }
        }

        public int CurrentFrame { get; private set; }

        public InputFrame Previous => CurrentFrame - 1 >= 0 ? Inputs[CurrentFrame - 1] : null;
        public InputFrame Current => Inputs[CurrentFrame];
        public InputFrame Next => CurrentFrame + 1 < Inputs.Count ? Inputs[CurrentFrame + 1] : null;
        public FastForward CurrentFf => FastForwards[FfIndex];
        public Command CurrentCommand => Commands[commandIndex];

        private bool NeedsReload {
            get {
                if (usedFiles.Count == 0) {
                    return true;
                }

                foreach (var file in usedFiles) {
                    if (File.GetLastWriteTime(file.Key) != file.Value) {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool CanPlayback => CurrentFrame < Inputs.Count;
        public bool NeedsToWait => Manager.IsLoading();

        public bool HasFastForward => FastForwards.Count > FfIndex && !Break;
        public int FastForwardSpeed => CurrentFf.Speed;
        public bool Break => FfIndex + 1 == FastForwards.Count && CurrentFf.Frame == CurrentFrame;

        public string SavedChecksum {
            get => string.IsNullOrEmpty(checksum) ? Checksum() : checksum;
            private set => checksum = value;
        }

        public void RefreshInputs(bool fromStart) {
            if (fromStart) {
                initializationFrameCount = 0;
                StudioFrameCount = 0;
                CurrentFrame = 0;
                FfIndex = 0;
                commandIndex = 0;
            }

            if (NeedsReload || fromStart) {
                int trycount = 5;
                while (trycount > 0) {
                    initializationFrameCount = 0;
                    FfIndex = 0;
                    commandIndex = 0;
                    Inputs.Clear();
                    FastForwards.Clear();
                    Commands.Clear();
                    usedFiles.Clear();
                    if (ReadFile(TasFilePath)) {
                        break;
                    }

                    System.Threading.Thread.Sleep(50);
                    trycount--;
                }

                CurrentFrame = Math.Min(Inputs.Count, CurrentFrame);
            }
        }

        public void AdvanceFrame() {
            RefreshInputs(false);

            if (NeedsToWait) {
                return;
            }

            while (Commands.Count > commandIndex && CurrentCommand.Frame <= CurrentFrame) {
                if (CurrentCommand.Frame == CurrentFrame) {
                    CurrentCommand.Invoke();
                }

                commandIndex++;
            }

            while (FastForwards.Count > FfIndex && CurrentFf.Frame <= CurrentFrame) {
                FfIndex++;
            }

            if (!CanPlayback) {
                return;
            }

            if (Manager.ExportSyncData) {
                Manager.ExportPlayerInfo();
            }

            Manager.SetInputs(Current);
            if (StudioFrameCount == 0 || Current.Line == Previous.Line) {
                StudioFrameCount++;
            } else {
                StudioFrameCount = 1;
            }

            CurrentFrame++;
        }

        public void InitializeRecording() { }

        public bool ReadFile(string filePath, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0) {
            try {
                if (filePath == TasFilePath && startLine == 0) {
                    if (!File.Exists(filePath)) {
                        return false;
                    }
                }

                if (!usedFiles.ContainsKey(filePath)) {
                    usedFiles.Add(filePath, File.GetLastWriteTime(filePath));
                }

                int subLine = 0;
                using (StreamReader sr = new StreamReader(filePath)) {
                    while (!sr.EndOfStream) {
                        string line = sr.ReadLine().Trim();

                        subLine++;
                        if (subLine < startLine) {
                            continue;
                        }

                        if (subLine > endLine) {
                            break;
                        }

                        if (InputCommands.TryExecuteCommand(this, line, initializationFrameCount, studioLine))
                            //workaround for the play command
                        {
                            return true;
                        }

                        if (line.StartsWith("***")) {
                            FastForwards.Add(new FastForward(initializationFrameCount, line.Substring(3)));
                        } else {
                            AddFrames(line, studioLine);
                        }

                        if (filePath == TasFilePath) {
                            studioLine++;
                        }
                    }
                }

                return true;
            } catch {
                return false;
            }
        }

        public void AddFrames(string line, int studioLine) {
            InputFrame frame = new InputFrame();
            frame.Line = studioLine;
            int index = line.IndexOf(",", StringComparison.Ordinal);
            int frames = 0;
            string framesStr;
            if (index == -1) {
                framesStr = line;
                index = 0;
            } else {
                framesStr = line.Substring(0, index);
            }

            if (!int.TryParse(framesStr, out frames)) {
                return;
            }

            frames = Math.Min(frames, 9999);
            frame.Frames = frames;
            while (index < line.Length) {
                char c = line[index];

                switch (char.ToUpper(c)) {
                    case 'L':
                        frame.Actions ^= Actions.Left;
                        break;
                    case 'R':
                        frame.Actions ^= Actions.Right;
                        break;
                    case 'U':
                        frame.Actions ^= Actions.Up;
                        break;
                    case 'D':
                        frame.Actions ^= Actions.Down;
                        break;
                    case 'J':
                        frame.Actions ^= Actions.Jump;
                        break;
                    case 'X':
                        frame.Actions ^= Actions.Dash;
                        break;
                    case 'G':
                        frame.Actions ^= Actions.Grab;
                        break;
                    case 'S':
                        frame.Actions ^= Actions.Start;
                        break;
                    case 'Q':
                        frame.Actions ^= Actions.Restart;
                        break;
                    case 'N':
                        frame.Actions ^= Actions.Journal;
                        break;
                    case 'K':
                        frame.Actions ^= Actions.Jump2;
                        break;
                    case 'C':
                        frame.Actions ^= Actions.Dash2;
                        break;
                    case 'O':
                        frame.Actions ^= Actions.Confirm;
                        break;
                    case 'Z':
                        frame.Actions ^= Actions.DemoDash;
                        break;
                    case 'F':
                        frame.Actions ^= Actions.Feather;
                        index++;
                        string angle = line.Substring(index + 1);
                        if (angle == "") {
                            frame.Angle = 0;
                        } else {
                            frame.Angle = float.Parse(angle.Trim());
                        }

                        continue;
                }

                index++;
            }

            for (int i = 0; i < frames; i++) {
                Inputs.Add(frame);
            }

            initializationFrameCount += frames;
        }

        public InputController Clone() {
            InputController clone = new InputController();

            for (int i = 0; i < Inputs.Count; i++) {
                if (i == 0 || !object.ReferenceEquals(Inputs[i], Inputs[i - 1])) {
                    clone.Inputs.Add(Inputs[i].Clone());
                } else {
                    clone.Inputs.Add(clone.Inputs[i - 1]);
                }
            }

            foreach (FastForward ff in FastForwards) {
                clone.FastForwards.Add(ff.Clone());
            }

            foreach (Command command in Commands) {
                clone.Commands.Add(command.Clone());
            }

            clone.CurrentFrame = CurrentFrame;
            clone.FfIndex = FfIndex;
            clone.StudioFrameCount = StudioFrameCount;
            clone.commandIndex = commandIndex;
            clone.usedFiles = new Dictionary<string, DateTime>(usedFiles);

            return clone;
        }

        public string Checksum(int toInputFrame) {
            StringBuilder result = new StringBuilder(TasFilePath);
            result.AppendLine();

            try {
                int checkInputFrame = 0;

                while (checkInputFrame <= toInputFrame) {
                    InputFrame currentInput = Inputs[checkInputFrame];
                    result.AppendLine(currentInput.ToString());

                    if (Commands.FirstOrDefault(command => command.Frame == checkInputFrame) is Command currentCommand) {
                        result.Append(currentCommand.LineText);
                    }

                    checkInputFrame++;
                }

                return SavedChecksum = Md5Helper.ComputeHash(result.ToString());
            } catch {
                return SavedChecksum = Md5Helper.ComputeHash(result.ToString());
            }
        }

        public string Checksum(InputController controller) => Checksum(controller.CurrentFrame);
        public string Checksum() => Checksum(CurrentFrame);

        #region ignore

        /*
        public void RecordPlayer() {
            InputRecord input = new InputRecord() { Line = inputIndex + 1, Frames = currentFrame };
            GetCurrentInputs(input);

            if (currentFrame == 0 && input == Current) {
                return;
            } else if (input != Current && !Manager.IsLoading()) {
                Current.Frames = currentFrame - Current.Frames;
                inputIndex++;
                if (Current.Frames != 0) {
                    inputs.Add(Current);
                }
                Current = input;
            }
            currentFrame++;
        }


        private static void GetCurrentInputs(InputRecord record) {
            if (Input.Jump.Check || Input.MenuConfirm.Check) {
                record.Actions |= Actions.Jump;
            }

            if (Input.Dash.Check || Input.MenuCancel.Check || Input.Talk.Check) {
                record.Actions |= Actions.Dash;
            }

            if (Input.Grab.Check) {
                record.Actions |= Actions.Grab;
            }

            if (Input.MenuJournal.Check) {
                record.Actions |= Actions.Journal;
            }

            if (Input.Pause.Check) {
                record.Actions |= Actions.Start;
            }

            if (Input.QuickRestart.Check) {
                record.Actions |= Actions.Restart;
            }

            if (Input.MenuLeft.Check || Input.MoveX.Value < 0) {
                record.Actions |= Actions.Left;
            }

            if (Input.MenuRight.Check || Input.MoveX.Value > 0) {
                record.Actions |= Actions.Right;
            }

            if (Input.MenuUp.Check || Input.MoveY.Value < 0) {
                record.Actions |= Actions.Up;
            }

            if (Input.MenuDown.Check || Input.MoveY.Value > 0) {
                record.Actions |= Actions.Down;
            }
        }

        /*
        public void WriteInputs() {
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
                for (int i = 0; i < inputs.Count; i++) {
                    InputRecord record = inputs[i];
                    byte[] data = Encoding.ASCII.GetBytes(record.ToString() + "\r\n");
                    fs.Write(data, 0, data.Length);
                }
                fs.Close();
            }
        }
        */

        #endregion
    }
}