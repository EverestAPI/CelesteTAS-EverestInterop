using System;
using System.Text;

namespace TAS {
	[Flags]
	public enum Actions {
		None = 0,
		Left = 1 << 0,
		Right = 1 << 1,
		Up = 1 << 2,
		Down = 1 << 3,
		Jump = 1 << 4,
		Dash = 1 << 5,
		Grab = 1 << 6,
		Start = 1 << 7,
		Restart = 1 << 8 ,
		Feather = 1 << 9,
		Journal = 1 << 10,
		Jump2 = 1 << 11,
		Dash2 = 1 << 12,
		Confirm = 1 << 13
	}
	public class InputRecord {
		public const int DefaultFastForwardSpeed = 400;

		public int Line { get; private set; }
		public string LineText { get; private set; }
		public int Frames { get; private set; }
		public Actions Actions { get; set; }
		public float Angle { get; private set; }
		public bool FastForward { get; set; }
		public int FastForwardSpeed { get; private set; }
		public bool SaveState { get; set; }
		public bool HasSavedState { get; set; }
		public bool ForceBreak { get; set; }
		public Action Command { get; private set; }
		public InputRecord() { }
		public InputRecord(Action commandCall, int line, string lineText) {
			Command = commandCall;
			Line = line;
			LineText = lineText;
		}
		public InputRecord(int line, string lineText) {
			Line = line;
			LineText = lineText;

			int index = 0;
			Frames = ReadFrames(lineText);
			if (Frames == 0) {
				// allow whitespace before the breakpoint
				lineText = lineText.Trim();
				if (lineText.StartsWith("***")) {
					FastForward = true;
					index = 3;

					if (lineText.Length >= 4) {
						if (lineText[3] == '!') {
							ForceBreak = true;
							index = 4;
						} else if (lineText[3].ToString().ToLower() == "s") {
							SaveState = true;
							index = 4;
						}
					}

					if (int.TryParse(lineText.Substring(index), out int speed)) {
						FastForwardSpeed = speed;
					} else {
						FastForwardSpeed = DefaultFastForwardSpeed;
					}
				}
				return;
			}

			while (index < lineText.Length) {
				char c = lineText[index];

				switch (char.ToUpper(c)) {
					case 'L': Actions ^= Actions.Left; break;
					case 'R': Actions ^= Actions.Right; break;
					case 'U': Actions ^= Actions.Up; break;
					case 'D': Actions ^= Actions.Down; break;
					case 'J': Actions ^= Actions.Jump; break;
					case 'X': Actions ^= Actions.Dash; break;
					case 'G': Actions ^= Actions.Grab; break;
					case 'S': Actions ^= Actions.Start; break;
					case 'Q': Actions ^= Actions.Restart; break;
					case 'N': Actions ^= Actions.Journal; break;
					case 'K': Actions ^= Actions.Jump2; break;
					case 'C': Actions ^= Actions.Dash2; break;
					case 'O': Actions ^= Actions.Confirm; break;
					case 'F':
						Actions ^= Actions.Feather;
						index++;
						Angle = ReadAngle(lineText.Substring(index + 1));
						continue;
				}

				index++;
			}

			if (HasActions(Actions.Feather)) {
				Actions &= ~Actions.Right & ~Actions.Left & ~Actions.Up & ~Actions.Down;
			} else {
				Angle = 0;
			}
		}

		private int ReadFrames(string line) {
			line = line.Trim();
			if (line.Contains(","))
				line = line.Substring(0, line.IndexOf(","));
			if (int.TryParse(line, out int frames)) {
				return Math.Min(frames, 9999);
			}
			return 0;
			/*i'm commenting this out because y'all need to know exactly how awful this code used to be
			bool foundFrames = false;
			int frames = 0;

			while (start < line.Length) {
				char c = line[start];
				if (!foundFrames) {
					if (char.IsDigit(c)) {
						foundFrames = true;
						//EXCUSE ME WHAT THE FUCK
						//WHY WOULD YOU DO THIS
						//I AM IN PHYSICAL PAIN RIGHT NOW
						//TryParse IS A THING
						frames = c ^ 0x30;
					} else if (c != ' ')
						return frames;

				} else if (char.IsDigit(c)) {
					if (frames < 9999)
						frames = frames * 10 + (c ^ 0x30);
					else
						frames = 9999;
				} else if (c != ' ')
					return frames;
				start++;
			}
			return frames;
			*/
		}

		private float ReadAngle(string line) {
			if (line == "")
				return 0f;
			return float.Parse(line.Trim());
		}

		public float GetX() {
			return (float)Math.Sin(Angle * Math.PI / 180.0);
		}

		public float GetY() {
			return (float)Math.Cos(Angle * Math.PI / 180.0);
		}

		public bool HasActions(Actions actions) =>
			(Actions & actions) != 0;

		public override string ToString() {
			return Frames == 0 ? string.Empty : Frames.ToString().PadLeft(4, ' ') + ActionsToString();
		}

		public string ActionsToString() {
			StringBuilder sb = new StringBuilder();
			if (HasActions(Actions.Left)) { sb.Append(",L"); }
			if (HasActions(Actions.Right)) { sb.Append(",R"); }
			if (HasActions(Actions.Up)) { sb.Append(",U"); }
			if (HasActions(Actions.Down)) { sb.Append(",D"); }
			if (HasActions(Actions.Jump)) { sb.Append(",J"); }
			if (HasActions(Actions.Jump2)) { sb.Append(",K"); }
			if (HasActions(Actions.Dash)) { sb.Append(",X"); }
			if (HasActions(Actions.Dash2)) { sb.Append(",C"); }
			if (HasActions(Actions.Grab)) { sb.Append(",G"); }
			if (HasActions(Actions.Start)) { sb.Append(",S"); }
			if (HasActions(Actions.Restart)) { sb.Append(",Q"); }
			if (HasActions(Actions.Journal)) { sb.Append(",N"); }
			if (HasActions(Actions.Confirm)) { sb.Append(",O"); }
			if (HasActions(Actions.Feather)) { sb.Append(",F,").Append(Angle == 0 ? string.Empty : Angle.ToString("0")); }
			return sb.ToString();
		}

		public InputRecord Clone() {
			InputRecord clone = new InputRecord(Line, Frames.ToString() + ActionsToString());
			clone.Actions = Actions;
			clone.Angle = Angle;
			clone.Frames = Frames;
			clone.Line = Line;
			clone.LineText = LineText;
			clone.Command = Command;
			clone.FastForward = FastForward;
			clone.FastForwardSpeed = FastForwardSpeed;
			clone.ForceBreak = ForceBreak;
			clone.SaveState = SaveState;
			clone.HasSavedState = HasSavedState;
			return clone;
		}

		//none of these are used
		/*
		public override bool Equals(object obj) {
			return obj is InputRecord && ((InputRecord)obj) == this;
		}
		public override int GetHashCode() {
			return Frames ^ (int)Actions;
		}
		public static bool operator ==(InputRecord one, InputRecord two) {
			bool oneNull = (object)one == null;
			bool twoNull = (object)two == null;
			if (oneNull != twoNull) {
				return false;
			} else if (oneNull && twoNull) {
				return true;
			}
			return one.Actions == two.Actions && one.Angle == two.Angle;
		}
		public static bool operator !=(InputRecord one, InputRecord two) {
			bool oneNull = (object)one == null;
			bool twoNull = (object)two == null;
			if (oneNull != twoNull) {
				return true;
			} else if (oneNull && twoNull) {
				return false;
			}
			return one.Actions != two.Actions || one.Angle != two.Angle;
		}
		*/
	}
}
 