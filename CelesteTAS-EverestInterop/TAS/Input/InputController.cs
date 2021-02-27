using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TAS.EverestInterop;

namespace TAS.Input {
    public class InputController {
        public static string StudioTasFilePath = Celeste.Celeste.Instance.GetDynDataInstance().Get<string>("lastStudioTasFilePath");
        public readonly SortedDictionary<int, List<Command>> Commands = new SortedDictionary<int, List<Command>>();
        public readonly SortedDictionary<int, FastForward> FastForwards = new SortedDictionary<int, FastForward>();
        public readonly List<InputFrame> Inputs = new List<InputFrame>();

        private string checksum;
        private int initializationFrameCount;

        public int StudioFrameCount;

        private Dictionary<string, DateTime> usedFiles = new Dictionary<string, DateTime>();

        public string TasFilePath {
            get {
                string path = string.IsNullOrEmpty(StudioTasFilePath) ? "Celeste.tas" : StudioTasFilePath;
                try {
                    if (!File.Exists(path)) {
                        File.WriteAllText(path, string.Empty);
                    }
                } catch {
                    return "Celeste.tas";
                }

                return path;
            }
        }

        public int CurrentFrame { get; private set; }

        public InputFrame Previous => CurrentFrame - 1 >= 0 ? Inputs[CurrentFrame - 1] : null;
        public InputFrame Current => Inputs[CurrentFrame];
        public InputFrame Next => CurrentFrame + 1 < Inputs.Count ? Inputs[CurrentFrame + 1] : null;
        public FastForward CurrentFastForward => FastForwards.GetValueOrDefault(CurrentFrame);
        public List<Command> CurrentCommands => Commands.GetValueOrDefault(CurrentFrame);

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

        public bool HasFastForward => (FastForwards.LastValueOrDefault()?.Frame ?? -1) > CurrentFrame;
        public int FastForwardSpeed => FastForwards.LastValueOrDefault()?.Speed ?? 1;
        public bool Break => FastForwards.LastValueOrDefault()?.Frame == CurrentFrame;

        public string SavedChecksum {
            get => string.IsNullOrEmpty(checksum) ? Checksum() : checksum;
            private set => checksum = value;
        }

        public void RefreshInputs(bool fromStart) {
            if (fromStart) {
                initializationFrameCount = 0;
                StudioFrameCount = 0;
                CurrentFrame = 0;
            }

            if (NeedsReload || fromStart) {
                int tryCount = 5;
                while (tryCount > 0) {
                    initializationFrameCount = 0;
                    Inputs.Clear();
                    FastForwards.Clear();
                    Commands.Clear();
                    usedFiles.Clear();
                    AnalogHelper.AnalogModeChange(AnalogueMode.Ignore);
                    if (ReadFile(TasFilePath)) {
                        LibTasHelper.FinishExport();
                        break;
                    }

                    // read file failed, rewrite the libtas inputs file.
                    LibTasHelper.RestartExport();

                    System.Threading.Thread.Sleep(50);
                    tryCount--;
                }

                CurrentFrame = Math.Min(Inputs.Count, CurrentFrame);
            }
        }

        public void AdvanceFrame() {
            RefreshInputs(false);

            if (NeedsToWait) {
                return;
            }

            CurrentCommands?.ForEach(command => command.Invoke());

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
                            FastForwards[initializationFrameCount] = new FastForward(initializationFrameCount, line.Substring(3), studioLine);
                        } else {
                            AddFrames(line, studioLine);
                        }

                        if (filePath == TasFilePath) {
                            studioLine++;
                        }
                    }
                }

                return true;
            } catch (Exception e) {
                e.Log();
                return false;
            }
        }

        public void AddFrames(string line, int studioLine) {
            if (!InputFrame.TryParse(line, studioLine, out InputFrame inputFrame)) {
                return;
            }

            for (int i = 0; i < inputFrame.Frames; i++) {
                Inputs.Add(inputFrame);
            }

            initializationFrameCount += inputFrame.Frames;
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

            foreach (int frame in FastForwards.Keys) {
                clone.FastForwards[frame] = FastForwards[frame].Clone();
            }

            foreach (int frame in Commands.Keys) {
                clone.Commands[frame] = new List<Command>(Commands[frame]);
            }

            clone.CurrentFrame = CurrentFrame;
            clone.StudioFrameCount = StudioFrameCount;
            clone.usedFiles = new Dictionary<string, DateTime>(usedFiles);

            return clone;
        }

        public string Checksum(int toInputFrame) {
            StringBuilder result = new StringBuilder(TasFilePath);
            result.AppendLine();

            try {
                int checkInputFrame = 0;

                while (checkInputFrame < toInputFrame) {
                    InputFrame currentInput = Inputs[checkInputFrame];
                    result.AppendLine(currentInput.ToString());

                    if (Commands.GetValueOrDefault(checkInputFrame) is List<Command> commands) {
                        foreach (Command command in commands) {
                            result.Append(command.LineText);
                        }
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

        public static void SaveStudioTasFilePath() {
            Celeste.Celeste.Instance.GetDynDataInstance().Set("lastStudioTasFilePath", StudioTasFilePath);
        }

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