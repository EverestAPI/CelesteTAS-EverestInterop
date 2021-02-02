using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Input = Celeste.Input;

namespace TAS {
	public class InputController {
		public List<InputRecord> inputs = new List<InputRecord>();
		private int frameToNext;
		public string filePath;
		public List<InputRecord> fastForwards = new List<InputRecord>();
		public Vector2? resetSpawn;
		public InputController(string filePath) {
			this.filePath = filePath;
		}
		public int InputIndex { get; private set; }
		public bool CanPlayback => InputIndex < inputs.Count;
		public bool HasFastForward => fastForwards.Count > 0 && !(Current.FastForward && fastForwards.Count == 1 && CurrentInputFrame == Current.Frames);
		public int FastForwardSpeed => fastForwards.Count == 0 ? 1 : fastForwards[0].FastForwardSpeed;
		public int CurrentFrame { get; private set; }
		public int CurrentInputFrame => CurrentFrame - frameToNext + Current.Frames;
		private string _checksum;
		public string SavedChecksum {
			get => string.IsNullOrEmpty(_checksum) ? Checksum() : _checksum;
			private set => _checksum = value;
		}
		public bool NeedsToWait => Manager.IsLoading();

		private Dictionary<string, DateTime> usedFiles = new Dictionary<string, DateTime>();
		private bool NeedsReload {
			get {
				foreach (var file in usedFiles) {
					if (File.GetLastWriteTime(file.Key) != file.Value)
						return true;
				}
				return false;
			}
		}

		public InputRecord Current { get; set; }
		public InputRecord Previous {
			get {
				if (frameToNext != 0 && InputIndex - 1 >= 0 && inputs.Count > 0)
					return inputs[InputIndex - 1];
				return null;
			}
		}
		public InputRecord Next {
			get {
				if (frameToNext != 0 && InputIndex + 1 < inputs.Count)
					return inputs[InputIndex + 1];
				return null;
			}
		}

		public bool HasInput(Actions action) {
			InputRecord input = Current;
			return input.HasActions(action);
		}

		public bool HasInputPressed(Actions action) {
			InputRecord input = Current;
			return input.HasActions(action) && CurrentInputFrame == 1;
		}

		public bool HasInputReleased(Actions action) {
			InputRecord current = Current;
			InputRecord previous = Previous;
			return !current.HasActions(action) && previous != null && previous.HasActions(action) && CurrentInputFrame == 1;
		}

		public override string ToString() {
			if (frameToNext == 0 && Current != null) {
				return Current.ToString() + "(" + CurrentFrame.ToString() + ")";
			} else if (InputIndex < inputs.Count && Current != null) {
				int inputFrames = Current.Frames;
				int startFrame = frameToNext - inputFrames;
				return Current.ToString() + "(" + (CurrentFrame - startFrame).ToString() + " / " + inputFrames + " : " + CurrentFrame + ")";
			}
			return string.Empty;
		}

		public string NextInput() {
			if (frameToNext != 0 && InputIndex + 1 < inputs.Count)
				return inputs[InputIndex + 1].ToString();
			return string.Empty;
		}

		public void InitializePlayback() {
			int trycount = 5;
			while (!ReadFile() && trycount >= 0) {
				System.Threading.Thread.Sleep(50);
				trycount--;
			}

			CurrentFrame = 0;
			InputIndex = 0;
			if (inputs.Count > 0) {
				Current = inputs[0];
				frameToNext = Current.Frames;
			} else {
				Current = new InputRecord();
				frameToNext = 1;
			}
		}

		//honestly i don't know what this method does anymore
		//it should be two separate ones but i don't want to separate the logic
		//if there's weirdness with inputs being skipped or repeating this is why
		public void AdvanceFrame(bool reload) {
			//there's a reason i'm rewriting how inputs work. this line is the reason.
			//there are 20 million checks to prevent inputs being skipped and i'm sick of it
			if (reload && !NeedsToWait)
				CurrentFrame--;
			if (NeedsReload) {
				//Reinitialize the file and simulate a replay of the TAS file up to the current point.
				int currentFrame = CurrentFrame;
				InitializePlayback();
				//Prevents time travel.
				CurrentFrame = currentFrame;

				while (CurrentFrame > frameToNext) {
					if (InputIndex + 1 >= inputs.Count) {
						InputIndex++;
						return;
					}
					if (Current.FastForward) {
						fastForwards.RemoveAt(0);
					}
					Current = inputs[++InputIndex];
					frameToNext += Current.Frames;
				}
				//prevents duplicating commands
				if (Current.Command != null) {
					Current = inputs[++InputIndex];
				}
			}

			if (NeedsToWait) {
				return;
			}

			do {
				if (Current.Command != null) {
					Current.Command.Invoke();
				}
				if (InputIndex < inputs.Count) {
					if (CurrentFrame >= frameToNext) {
						if (InputIndex + 1 >= inputs.Count) {
							InputIndex++;
							return;
						}
						if (Current.FastForward) {
							fastForwards.RemoveAt(0);
						}
						Current = inputs[++InputIndex];
						frameToNext += Current.Frames;
					}
				}
			} while (Current.Command != null);
			CurrentFrame++;
			if (Manager.ExportSyncData)
				Manager.ExportPlayerInfo();
			if (!reload)
				Manager.SetInputs(Current);
		}

		public void InitializeRecording() {
			CurrentFrame = 0;
			InputIndex = 0;
			Current = new InputRecord();
			frameToNext = 0;
			inputs.Clear();
			fastForwards.Clear();
			usedFiles.Clear();
		}

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
		*/

		private static void GetCurrentInputs(InputRecord record) {
			if (Input.Jump.Check || Input.MenuConfirm.Check) { record.Actions |= Actions.Jump; }
			if (Input.Dash.Check || Input.MenuCancel.Check || Input.Talk.Check) { record.Actions |= Actions.Dash; }
			if (Input.Grab.Check) { record.Actions |= Actions.Grab; }
			if (Input.MenuJournal.Check) { record.Actions |= Actions.Journal; }
			if (Input.Pause.Check) { record.Actions |= Actions.Start; }
			if (Input.QuickRestart.Check) { record.Actions |= Actions.Restart; }
			if (Input.MenuLeft.Check || Input.MoveX.Value < 0) { record.Actions |= Actions.Left; }
			if (Input.MenuRight.Check || Input.MoveX.Value > 0) { record.Actions |= Actions.Right; }
			if (Input.MenuUp.Check || Input.MoveY.Value < 0) { record.Actions |= Actions.Up; }
			if (Input.MenuDown.Check || Input.MoveY.Value > 0) { record.Actions |= Actions.Down; }
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

		public bool ReadFile(string filePath = "Celeste.tas", int startLine = 0, int endLine = int.MaxValue, int studioLine = 0) {
			try {
				if (filePath == "Celeste.tas" && startLine == 0) {
					inputs.Clear();
					fastForwards.Clear();
					usedFiles.Clear();
					if (!File.Exists(filePath))
						return false;
				}
				if (!usedFiles.ContainsKey(filePath)) {
					usedFiles.Add(filePath, File.GetLastWriteTime(filePath));
				}
				int subLine = 0;
				using (StreamReader sr = new StreamReader(filePath)) {
					while (!sr.EndOfStream) {
						string line = sr.ReadLine();

						if (filePath == "Celeste.tas")
							studioLine++;
						subLine++;
						if (subLine < startLine)
							continue;
						if (subLine > endLine)
							break;

						if (InputCommands.TryExecuteCommand(this, line, studioLine))
							return true;

						InputRecord input = new InputRecord(studioLine, line);

						if (input.FastForward && inputs.LastOrDefault(record => record.Frames > 0) is InputRecord previous) {
							// Only the last one of the consecutive breakpoints takes effect
							if (previous.FastForward && fastForwards.Count > 0) {
								fastForwards.RemoveAt(fastForwards.Count - 1);
							}
							fastForwards.Add(input);

							previous.ForceBreak = input.ForceBreak;
							previous.SaveState = input.SaveState;
							previous.FastForward = true;
						} else if (input.Frames > 0) {
							inputs.Add(input);
						}
					}
				}
				return true;
			} catch {
				return false;
			}
		}

		public InputController Clone() {
			InputController clone = new InputController(filePath);

			clone.inputs = new List<InputRecord>();
			foreach (InputRecord record in inputs) {
				clone.inputs.Add(record.Clone());
			}

			clone.fastForwards = new List<InputRecord>();
			foreach (InputRecord record in fastForwards) {
				clone.fastForwards.Add(record.Clone());
			}

			clone.CurrentFrame = CurrentFrame;
			clone.frameToNext = frameToNext;
			if (InputIndex <= clone.inputs.Count)
				clone.InputIndex = InputIndex;
			clone.Current = clone.inputs[clone.InputIndex];
			clone.usedFiles = new Dictionary<string, DateTime>(usedFiles);

			return clone;
		}

		public string Checksum(int toInputIndex) {
			StringBuilder result = new StringBuilder(filePath);
			result.AppendLine();

			try {
				int checkInputIndex = 0;

				while (checkInputIndex <= toInputIndex) {
					InputRecord current = inputs[checkInputIndex];
					result.AppendLine(current.Command != null ? current.LineText : current.ToString());
					checkInputIndex++;
				}

				return SavedChecksum = MD5Helper.ComputeHash(result.ToString());
			} catch {
				return SavedChecksum = MD5Helper.ComputeHash(result.ToString());
			}
		}

		public string Checksum(InputController controller) => Checksum(controller.InputIndex);
		public string Checksum() => Checksum(InputIndex);
	}
}
