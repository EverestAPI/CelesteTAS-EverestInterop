using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace TAS {
	public class InputController {
		private List<InputRecord> inputs = new List<InputRecord>();
		private int currentFrame, inputIndex, frameToNext;
		private string filePath;
		private List<InputRecord> fastForwards = new List<InputRecord>();
		public Vector2? resetSpawn;
		public InputController(string filePath) {
			this.filePath = filePath;
		}

		public bool CanPlayback { get { return inputIndex < inputs.Count; } }
		public bool HasFastForward { get { return fastForwards.Count > 0; } }
		public int FastForwardSpeed { get { return fastForwards.Count == 0 ? 1 : fastForwards[0].Frames == 0 ? 400 : fastForwards[0].Frames; } }
		public int CurrentFrame { get { return currentFrame; } }
		public int CurrentInputFrame { get { return currentFrame - frameToNext + Current.Frames; } }
		public InputRecord Current { get; set; }
		public InputRecord Previous {
			get {
				if (frameToNext != 0 && inputIndex - 1 >= 0 && inputs.Count > 0) {
					return inputs[inputIndex - 1];
				}
				return null;
			}
		}
		public InputRecord Next {
			get {
				if (frameToNext != 0 && inputIndex + 1 < inputs.Count) {
					return inputs[inputIndex + 1];
				}
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
				return Current.ToString() + "(" + currentFrame.ToString() + ")";
			} else if (inputIndex < inputs.Count && Current != null) {
				int inputFrames = Current.Frames;
				int startFrame = frameToNext - inputFrames;
				return Current.ToString() + "(" + (currentFrame - startFrame).ToString() + " / " + inputFrames + " : " + currentFrame + ")";
			}
			return string.Empty;
		}
		public string NextInput() {
			if (frameToNext != 0 && inputIndex + 1 < inputs.Count) {
				return inputs[inputIndex + 1].ToString();
			}
			return string.Empty;
		}
		public void InitializePlayback() {
			int trycount = 5;
			while (!ReadFile() && trycount >= 0) {
				System.Threading.Thread.Sleep(50);
				trycount--;
			}

			currentFrame = 0;
			inputIndex = 0;
			if (inputs.Count > 0) {
				Current = inputs[0];
				frameToNext = Current.Frames;
			} else {
				Current = new InputRecord();
				frameToNext = 1;
			}
		}
		public void ReloadPlayback() {
			int playedBackFrames = currentFrame;
			InitializePlayback();
			currentFrame = playedBackFrames;

			while (currentFrame >= frameToNext) {
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
		public void InitializeRecording() {
			currentFrame = 0;
			inputIndex = 0;
			Current = new InputRecord();
			frameToNext = 0;
			inputs.Clear();
			fastForwards.Clear();
		}
		public void PlaybackPlayer() {
			if (Current.Command != null) {
				CommandHandler.ExecuteCommand(Current.Command);
			}

			if (inputIndex < inputs.Count && !Manager.IsLoading()) {
				if (currentFrame >= frameToNext) {
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

				currentFrame++;
			}
			Manager.SetInputs(Current);
		}
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
		private bool ReadFile() {
			try {
				inputs.Clear();
				fastForwards.Clear();
				if (!File.Exists(filePath)) { return false; }

				int lines = 0;
				using (StreamReader sr = new StreamReader(filePath)) {
					while (!sr.EndOfStream) {
						string line = sr.ReadLine();

						if (line.IndexOf("Read", System.StringComparison.OrdinalIgnoreCase) == 0 && line.Length > 5) {
							lines++;
							ReadFile(line.Substring(5), lines);
							lines--;
						}

						if (line.ToLower().StartsWith("console") && line.Length > 8)
							ConsoleCommand(line.Substring(8));

						InputRecord input = new InputRecord(++lines, line);
						if (input.FastForward) {
							fastForwards.Add(input);

							if (inputs.Count > 0) {
								inputs[inputs.Count - 1].ForceBreak = input.ForceBreak;
								inputs[inputs.Count - 1].FastForward = true;
							}
						} else if (input.Frames != 0) {
							inputs.Add(input);
						}
					}
				}
				return true;
			} catch {
				return false;
			}
		}
		private void ReadFile(string extraFile, int lines) {
			int index = extraFile.IndexOf(',');
			string filePath = index > 0 ? extraFile.Substring(0, index) : extraFile;
			string origFilePath = Manager.settings.DefaultPath;
			// Check for full and shortened Read versions for absolute path
			if (origFilePath != null) {
				string altFilePath = origFilePath + Path.DirectorySeparatorChar + filePath;
				if (File.Exists(altFilePath))
					filePath = altFilePath;
				else {
					string[] files = Directory.GetFiles(origFilePath, $"{filePath}*.tas");
					if (files.Length != 0)
						filePath = files[0].ToString();
				}
			}
			// Check for full and shortened Read versions for relative path
			if (!File.Exists(filePath)) {
				string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(), $"{filePath}*.tas");
				filePath = files[0].ToString();
				if (!File.Exists(filePath)) { return; }
			}
			// Find starting and ending lines
			int skipLines = 0;
			int lineLen = int.MaxValue;
			if (index > 0) {
				int indexLen = extraFile.IndexOf(',', index + 1);
				if (indexLen > 0) {
					string startLine = extraFile.Substring(index + 1, indexLen - index - 1);
					string endLine = extraFile.Substring(indexLen + 1);
					GetLine(startLine, filePath, out skipLines);
					GetLine(endLine, filePath, out lineLen);
				} else {
					string startLine = extraFile.Substring(index + 1);
					GetLine(startLine, filePath, out skipLines);
				}
			}

			int subLine = 0;
			using (StreamReader sr = new StreamReader(filePath)) {
				while (!sr.EndOfStream) {
					string line = sr.ReadLine();

					subLine++;
					if (subLine <= skipLines) { continue; }
					if (subLine > lineLen) { break; }

					if (line.IndexOf("Read", System.StringComparison.OrdinalIgnoreCase) == 0 && line.Length > 5) {
						ReadFile(line.Substring(5), lines);
					}

					InputRecord input = new InputRecord(lines, line);
					if (input.FastForward) {
						fastForwards.Add(input);

						if (inputs.Count > 0) {
							inputs[inputs.Count - 1].ForceBreak = input.ForceBreak;
							inputs[inputs.Count - 1].FastForward = true;
						}
					} else if (input.Frames != 0) {
						inputs.Add(input);
					}
				}
			}
		}
		private void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
            if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
				int curLine = 0;
				using (StreamReader sr = new StreamReader(path)) {
					while (!sr.EndOfStream) {
						curLine++;
						string line = sr.ReadLine();
						if (line == ("#" + labelOrLineNumber)) {
							lineNumber = curLine;
							return;
						}
					}
					lineNumber = int.MaxValue;
				}
			}
		}
		private void ConsoleCommand(string command) {
			inputs.Add(new InputRecord(command));
		}
	}
}
