using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Input = Celeste.Input;

namespace TAS {
	public class InputController {
		public List<InputRecord> inputs = new List<InputRecord>();
		private int inputIndex, frameToNext;
		public string filePath;
		public List<InputRecord> fastForwards = new List<InputRecord>();
		public Vector2? resetSpawn;
		public InputController(string filePath) {
			this.filePath = filePath;
		}

		public bool CanPlayback => inputIndex < inputs.Count;
		public bool HasFastForward => fastForwards.Count > 0;
		public int FastForwardSpeed => fastForwards.Count == 0 ? 1 : fastForwards[0].Frames == 0 ? 400 : fastForwards[0].Frames;
		public int CurrentFrame { get; private set; }
		public int CurrentInputFrame => CurrentFrame - frameToNext + Current.Frames;
		private long _checksum;
		public long SavedChecksum {
			get => _checksum == 0 ? Checksum() : _checksum;
			private set => _checksum = value;
		}
		public bool NeedsToWait => Manager.IsLoading() || Manager.forceDelayTimer > 0 || Manager.forceDelay;

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
				if (frameToNext != 0 && inputIndex - 1 >= 0 && inputs.Count > 0)
					return inputs[inputIndex - 1];
				return null;
			}
		}
		public InputRecord Next {
			get {
				if (frameToNext != 0 && inputIndex + 1 < inputs.Count)
					return inputs[inputIndex + 1];
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
			} else if (inputIndex < inputs.Count && Current != null) {
				int inputFrames = Current.Frames;
				int startFrame = frameToNext - inputFrames;
				return Current.ToString() + "(" + (CurrentFrame - startFrame).ToString() + " / " + inputFrames + " : " + CurrentFrame + ")";
			}
			return string.Empty;
		}

		public string NextInput() {
			if (frameToNext != 0 && inputIndex + 1 < inputs.Count)
				return inputs[inputIndex + 1].ToString();
			return string.Empty;
		}

		public void InitializePlayback() {
			int trycount = 5;
			while (!ReadFile() && trycount >= 0) {
				System.Threading.Thread.Sleep(50);
				trycount--;
			}

			CurrentFrame = 0;
			inputIndex = 0;
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
		public void AdvanceFrame(bool reload, bool forceReload = false) {
			//there's a reason i'm rewriting how inputs work. this line is the reason.
			//there are 20 million checks to prevent inputs being skipped and i'm sick of it
			if (reload && !NeedsToWait)
				CurrentFrame--;
			if (NeedsReload || forceReload) {
				//Reinitialize the file and simulate a replay of the TAS file up to the current point.
				int previousFrame = CurrentFrame - 1;
				InitializePlayback();
				//Prevents time travel.
				CurrentFrame = previousFrame + 1;

				while (CurrentFrame > frameToNext) {
					if (inputIndex + 1 >= inputs.Count) {
						inputIndex++;
						return;
					}
					if (Current.FastForward) {
						fastForwards.RemoveAt(0);
					}
					Current = inputs[++inputIndex];
					frameToNext += Current.Frames;
				}
				//prevents duplicating commands
				if (Current.Command != null) {
					Current = inputs[++inputIndex];
				}
			}

			if (NeedsToWait) {
				if (!reload && Manager.forceDelayTimer > 0)
					Manager.forceDelayTimer--;
				return;
			}

			do {
				if (Current.Command != null) {
					Current.Command.Invoke();
				}
				if (inputIndex < inputs.Count) {
					if (CurrentFrame >= frameToNext) {
						if (inputIndex + 1 >= inputs.Count) {
							inputIndex++;
							return;
						}
						if (Current.FastForward) {
							fastForwards.RemoveAt(0);
						}
						Current = inputs[++inputIndex];
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

		public void DryAdvanceFrames(int frames) {
			for (int i = 0; i < frames; i++) {
				do {
					if (inputIndex < inputs.Count) {
						if (CurrentFrame >= frameToNext) {
							if (inputIndex + 1 >= inputs.Count) {
								inputIndex++;
								return;
							}
							Current = inputs[++inputIndex];
							frameToNext += Current.Frames;
						}
					}
				} while (Current.Command != null);
				CurrentFrame++;
				Manager.SetInputs(Current);
			}

			for (var i = fastForwards.Count - 1; i >= 0; i--) {
				if (fastForwards[i].Line < Current.Line)
					fastForwards.RemoveAt(i);
			}
		}

		public void ReverseFrames(int frames) {
			if (frames > CurrentFrame)
				return;
			for (int i = 0; i < frames; i++) {
				if (CurrentInputFrame == 1) {
					Current = inputs[--inputIndex];
					frameToNext -= Current.Frames;
				}
				CurrentFrame--;
			}
		}

		public void InitializeRecording() {
			CurrentFrame = 0;
			inputIndex = 0;
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

						if (input.FastForward) {
							fastForwards.Add(input);

							if (inputs.Count > 0) {
								inputs[inputs.Count - 1].ForceBreak = input.ForceBreak;
								inputs[inputs.Count - 1].FastForward = true;
							}
						}
						else if (input.Frames != 0) {
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
			if (inputIndex <= clone.inputs.Count)
				clone.inputIndex = inputIndex;
			clone.Current = clone.inputs[clone.inputIndex];
			clone.usedFiles = new Dictionary<string, DateTime>(usedFiles);

			return clone;
		}

		public long Checksum(int toFrame) {
			try {
				// the checksum behaves very weirdly if you don't subtract a few frames
				toFrame -= 10;

				long output = 0;
				int inputIndex = 0;
				int frames = 0;
				InputRecord current = inputs[inputIndex];
				while (frames < toFrame) {
					//if (!(current.FastForward || current.Command != null)) {
						for (int i = 0; i < current.Frames && frames < toFrame; i++, frames++) {
							output += (long)current.Actions * frames;
						}
					//}
					current = inputs[inputIndex++];
				}
				SavedChecksum = output;
				return output;
			}
			catch { SavedChecksum = 0; return 0; }
		}

		public long Checksum() => Checksum(CurrentFrame);
	}
}
