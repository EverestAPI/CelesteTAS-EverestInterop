using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Monocle;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input {
    public class InputController {
        private static string studioTasFilePath = string.Empty;

        public readonly SortedDictionary<int, List<Command>> Commands = new();
        public readonly List<Command> ExecuteAtStartCommands = new();
        public readonly SortedDictionary<int, FastForward> FastForwards = new();
        public readonly SortedDictionary<int, FastForward> FastForwardComments = new();
        public readonly List<InputFrame> Inputs = new();
        public readonly Dictionary<string, DateTime> UsedFiles = new();

        private string checksum;
        private int initializationFrameCount;
        private string savestateChecksum;

        private static readonly string DefaultTasFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Celeste.tas");

        public static string StudioTasFilePath {
            get => studioTasFilePath;
            set {
                if (studioTasFilePath != value) {
                    studioTasFilePath = value;

                    string path = string.IsNullOrEmpty(value) ? DefaultTasFilePath : value;
                    try {
                        if (!File.Exists(path)) {
                            File.WriteAllText(path, string.Empty);
                        }
                    } catch {
                        studioTasFilePath = DefaultTasFilePath;
                    }

                    if (Manager.Running) {
                        Manager.NextStates |= States.Disable;
                    }

                    Manager.Controller.Reset();
                    Manager.Controller.RefreshInputs(true);
                }
            }
        }

        public static string TasFilePath => string.IsNullOrEmpty(StudioTasFilePath) ? DefaultTasFilePath : StudioTasFilePath;

        // start from 1
        public int CurrentFrameInInput { get; private set; }

        // start from 0
        public int CurrentFrameInTas { get; private set; }

        public InputFrame Previous => Inputs.GetValueOrDefault(CurrentFrameInTas - 1);
        public InputFrame Current => Inputs.GetValueOrDefault(CurrentFrameInTas);
        public InputFrame Next => Inputs.GetValueOrDefault(CurrentFrameInTas + 1);
        public FastForward CurrentFastForward => FastForwards.GetValueOrDefault(CurrentFrameInTas);
        public List<Command> CurrentCommands => Commands.GetValueOrDefault(CurrentFrameInTas);
        private bool NeedsReload => UsedFiles.IsEmpty() || UsedFiles.Any(file => File.GetLastWriteTime(file.Key) != file.Value);
        public bool CanPlayback => CurrentFrameInTas < Inputs.Count;
        public bool NeedsToWait => Manager.IsLoading();

        public FastForward NextCommentFastForward;
        private FastForward LastFastForward => NextCommentFastForward ?? FastForwards.LastValueOrDefault();
        public bool HasFastForward => LastFastForward != null && LastFastForward.Frame > CurrentFrameInTas;
        public int FastForwardSpeed => LastFastForward == null ? 1 : Calc.Clamp(LastFastForward.Frame - CurrentFrameInTas, 1, LastFastForward.Speed);
        public bool Break => LastFastForward?.Frame == CurrentFrameInTas;

        private string Checksum => string.IsNullOrEmpty(checksum) ? checksum = CalcChecksum(Inputs.Count - 1) : checksum;

        public string SavestateChecksum {
            get => string.IsNullOrEmpty(savestateChecksum) ? savestateChecksum = CalcChecksum(CurrentFrameInTas) : savestateChecksum;
            private set => savestateChecksum = value;
        }

        public void RefreshInputs(bool enableRun) {
            if (enableRun) {
                Stop();
            }

            string lastChecksum = Checksum;
            bool firstRun = UsedFiles.IsEmpty();
            if (NeedsReload) {
                int tryCount = 5;
                while (tryCount > 0) {
                    Reset();
                    if (ReadFile(TasFilePath)) {
                        LibTasHelper.FinishExport();
                        if (!firstRun && lastChecksum != Checksum) {
                            MetadataCommands.UpdateRecordCount(this);
                        }

                        break;
                    }

                    // read file failed, rewrite the libtas inputs file.
                    LibTasHelper.RestartExport();

                    System.Threading.Thread.Sleep(50);
                    tryCount--;
                }

                CurrentFrameInTas = Math.Min(Inputs.Count, CurrentFrameInTas);
            }

            if (enableRun) {
                ExecuteAtStartCommands.ForEach(command => command.Invoke());
            }
        }

        public void Stop() {
            CurrentFrameInInput = 0;
            CurrentFrameInTas = 0;
            NextCommentFastForward = null;
        }

        private void Reset() {
            initializationFrameCount = 0;
            checksum = string.Empty;
            savestateChecksum = string.Empty;
            Inputs.Clear();
            FastForwards.Clear();
            FastForwardComments.Clear();
            Commands.Clear();
            ExecuteAtStartCommands.Clear();
            UsedFiles.Clear();
            AnalogHelper.AnalogModeChange(AnalogueMode.Ignore);
            RepeatCommand.Clear();
            InputCommands.ClearReadCommandStack();
        }

        public void AdvanceFrame(out bool canPlayback) {
            if (!Manager.UltraFastForwarding) {
                RefreshInputs(false);
            }

            canPlayback = CanPlayback;

            if (NeedsToWait) {
                return;
            }

            CurrentCommands?.ForEach(command => {
                if (command.Attribute.ExecuteTiming == ExecuteTiming.Runtime && (!Manager.EnforceLegal || command.Attribute.LegalInMainGame)) {
                    command.Invoke();
                }
            });

            if (!CanPlayback) {
                return;
            }

            ExportGameInfo.ExportInfo();

            Manager.SetInputs(Current);

            if (CurrentFrameInInput == 0 || Current.Line == Previous.Line && Current.RepeatIndex == Previous.RepeatIndex) {
                CurrentFrameInInput++;
            } else {
                CurrentFrameInInput = 1;
            }

            CurrentFrameInTas++;
        }

        public void InitializeRecording() { }

        // studioLine start from 0, startLine start from 1;
        public bool ReadFile(string filePath, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0, int repeatIndex = 0,
            int repeatCount = 0) {
            try {
                if (!File.Exists(filePath)) {
                    return false;
                }

                UsedFiles[filePath] = File.GetLastWriteTime(filePath);
                IEnumerable<string> lines = File.ReadLines(filePath).Take(endLine);
                ReadLines(lines, filePath, startLine, studioLine, repeatIndex, repeatCount);
                return true;
            } catch (Exception e) {
                e.Log();
                return false;
            }
        }

        public void ReadLines(IEnumerable<string> lines, string filePath, int startLine, int studioLine, int repeatIndex, int repeatCount) {
            int subLine = 0;
            foreach (string readLine in lines) {
                string lineText = readLine.Trim();

                subLine++;
                if (subLine < startLine) {
                    continue;
                }

                if (InputCommands.TryParseCommand(this, filePath, subLine, lineText, initializationFrameCount, studioLine)) {
                    //workaround for the play command
                    return;
                }

                if (lineText.StartsWith("***")) {
                    FastForward fastForward = new(initializationFrameCount, lineText.Substring(3), studioLine);
                    if (FastForwards.TryGetValue(initializationFrameCount, out FastForward oldFastForward) && oldFastForward.SaveState &&
                        !fastForward.SaveState) {
                        // ignore 
                    } else {
                        FastForwards[initializationFrameCount] = fastForward;
                    }
                } else if (lineText.StartsWith("#")) {
                    FastForwardComments[initializationFrameCount] = new FastForward(initializationFrameCount, "", studioLine);
                } else {
                    AddFrames(lineText, studioLine, repeatIndex, repeatCount);
                }

                if (filePath == TasFilePath) {
                    studioLine++;
                }
            }

            if (filePath == TasFilePath) {
                FastForwardComments[initializationFrameCount] = new FastForward(initializationFrameCount, "", studioLine);
            }
        }

        public void AddFrames(string line, int studioLine, int repeatIndex = 0, int repeatCount = 0) {
            if (!InputFrame.TryParse(line, studioLine, Inputs.LastOrDefault(), out InputFrame inputFrame, repeatIndex, repeatCount)) {
                return;
            }

            for (int i = 0; i < inputFrame.Frames; i++) {
                Inputs.Add(inputFrame);
            }

            initializationFrameCount += inputFrame.Frames;
        }

        public InputController Clone() {
            InputController clone = new();

            clone.Inputs.AddRange(Inputs);
            clone.FastForwards.AddRange((IDictionary) FastForwards);
            clone.FastForwardComments.AddRange((IDictionary) FastForwardComments);
            clone.ExecuteAtStartCommands.AddRange(ExecuteAtStartCommands);
            foreach (int frame in Commands.Keys) {
                clone.Commands[frame] = new List<Command>(Commands[frame]);
            }

            clone.UsedFiles.AddRange(UsedFiles);
            clone.CurrentFrameInTas = CurrentFrameInTas;
            clone.CurrentFrameInInput = CurrentFrameInInput;
            clone.SavestateChecksum = clone.CalcChecksum(CurrentFrameInTas);

            return clone;
        }

        public void FastForwardToNextComment() {
            NextCommentFastForward = null;
            RefreshInputs(false);
            FastForward next = FastForwardComments.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value;
            if (next != null && LastFastForward is { } last && HasFastForward && next.Frame > last.Frame) {
                // NextCommentFastForward = last;
            } else {
                NextCommentFastForward = next;
            }

            Manager.States &= ~States.FrameStep;
            Manager.NextStates &= ~States.FrameStep;
        }

        public void CopyFrom(InputController controller) {
            CurrentFrameInInput = controller.CurrentFrameInInput;
            CurrentFrameInTas = controller.CurrentFrameInTas;
        }

        private string CalcChecksum(int toInputFrame) {
            StringBuilder result = new(TasFilePath);
            result.AppendLine();

            int checkInputFrame = 0;

            while (checkInputFrame < toInputFrame) {
                InputFrame currentInput = Inputs[checkInputFrame];
                result.AppendLine(currentInput.ToActionsString());

                if (Commands.GetValueOrDefault(checkInputFrame) is { } commands) {
                    foreach (Command command in commands.Where(command => command.Attribute.CalcChecksum)) {
                        result.AppendLine(command.LineText);
                    }
                }

                checkInputFrame++;
            }

            return HashHelper.ComputeHash(result.ToString());
        }

        public string CalcChecksum(InputController controller) => CalcChecksum(controller.CurrentFrameInTas);

        // for hot loading
        // ReSharper disable once UnusedMember.Local
        [Unload]
        private static void SaveStudioTasFilePath() {
            Engine.Instance.GetDynDataInstance().Set(nameof(studioTasFilePath), studioTasFilePath);
        }

        // ReSharper disable once UnusedMember.Local
        [Load]
        private static void RestoreStudioTasFilePath() {
            studioTasFilePath = Engine.Instance.GetDynDataInstance().Get<string>(nameof(studioTasFilePath));
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