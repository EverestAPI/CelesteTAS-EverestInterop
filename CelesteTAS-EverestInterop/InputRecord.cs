using System;
using System.Text;
namespace TAS {
	[Flags]
	public enum Actions {
		None,
		Left = 1,
		Right = 2,
		Up = 4,
		Down = 8,
		Jump = 16,
		Dash = 32,
		Grab = 64,
		Start = 128,
		Restart = 256,
		Feather = 512,
		Journal = 1024,
		Jump2 = 2048,
		Dash2 = 4096,
		Confirm = 8192
	}
	public class InputRecord {
		public int Line { get; set; }
		public int Frames { get; set; }
		public Actions Actions { get; set; }
		public float Angle { get; set; }
		public bool FastForward { get; set; }
		public bool ForceBreak { get; set; }
		public string[] Command { get; set; }
		public InputRecord() { }
		public InputRecord(string[] command) {
			Command = command;
		}
		public InputRecord(int number, string line) {
			Line = number;

			int index = 0;
			Frames = ReadFrames(line, ref index);
			if (Frames == 0) {

				// allow whitespace before the breakpoint
				line = line.Trim();
				if (line.StartsWith("***")) {
					FastForward = true;
					index = 3;

					if (line.Length >= 4 && line[3] == '!') {
						ForceBreak = true;
						index = 4;
					}

					Frames = ReadFrames(line, ref index);
				}
				return;
			}

			while (index < line.Length) {
				char c = line[index];

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
						Angle = ReadAngle(line, ref index);
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
		private int ReadFrames(string line, ref int start) {
			bool foundFrames = false;
			int frames = 0;

			while (start < line.Length) {
				char c = line[start];

				if (!foundFrames) {
					if (char.IsDigit(c)) {
						foundFrames = true;
						frames = c ^ 0x30;
					} else if (c != ' ') {
						return frames;
					}
				} else if (char.IsDigit(c)) {
					if (frames < 9999) {
						frames = frames * 10 + (c ^ 0x30);
					} else {
						frames = 9999;
					}
				} else if (c != ' ') {
					return frames;
				}

				start++;
			}

			return frames;
		}
		private float ReadAngle(string line, ref int start) {
			bool foundAngle = false;
			bool foundDecimal = false;
			int decimalPlaces = 1;
			int angle = 0;
			bool negative = false;

			while (start < line.Length) {
				char c = line[start];

				if (!foundAngle) {
					if (char.IsDigit(c)) {
						foundAngle = true;
						angle = c ^ 0x30;
					} else if (c == ',') {
						foundAngle = true;
					} else if (c == '.') {
						foundAngle = true;
						foundDecimal = true;
					} else if (c == '-') {
						negative = true;
					}
				} else if (char.IsDigit(c)) {
					angle = angle * 10 + (c ^ 0x30);
					if (foundDecimal) {
						decimalPlaces *= 10;
					}
				} else if (c == '.') {
					foundDecimal = true;
				} else if (c != ' ') {
					return (negative ? (float)-angle : (float)angle) / (float)decimalPlaces;
				}

				start++;
			}

			return (negative ? (float)-angle : (float)angle) / (float)decimalPlaces;
		}
		public float GetX() {
			if (HasActions(Actions.Right)) {
				return 1f;
			} else if (HasActions(Actions.Left)) {
				return -1f;
			} else if (!HasActions(Actions.Feather)) {
				return 0f;
			}
			return (float)Math.Sin(Angle * Math.PI / 180.0);
		}
		public float GetY() {
			if (HasActions(Actions.Up)) {
				return 1f;
			} else if (HasActions(Actions.Down)) {
				return -1f;
			} else if (!HasActions(Actions.Feather)) {
				return 0f;
			}
			return (float)Math.Cos(Angle * Math.PI / 180.0);
		}
		public bool HasActions(Actions actions) {
			return (Actions & actions) != 0;
		}
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
	}
}