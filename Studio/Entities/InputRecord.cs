using System;
using System.Text;
using System.Text.RegularExpressions;
using CelesteStudio.Properties;

namespace CelesteStudio.Entities {
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
        Restart = 1 << 8,
        Feather = 1 << 9,
        Journal = 1 << 10,
        Jump2 = 1 << 11,
        Dash2 = 1 << 12,
        Confirm = 1 << 13,
        DemoDash = 1 << 14,
        DemoDash2 = 1 << 15,
    }

    public class InputRecord {
        private const char Delimiter = ',';
        private static readonly Regex DuplicateZeroRegex = new(@"^0+([^.])", RegexOptions.Compiled);
        private static readonly Regex FloatRegex = new(@"^,-?([0-9.]+)", RegexOptions.Compiled);

        private static readonly Actions[][] ExclusiveActions = {
            new[] {Actions.Dash, Actions.Dash2, Actions.DemoDash, Actions.DemoDash2},
            new[] {Actions.Jump, Actions.Jump2},
            new[] {Actions.Up, Actions.Down, Actions.Feather},
            new[] {Actions.Left, Actions.Right, Actions.Feather},
        };

        public InputRecord(string line) {
            Notes = string.Empty;

            int index = 0;
            Frames = ReadFrames(line, ref index);
            if (Frames == 0) {
                Notes = line;
                return;
            }

            while (index < line.Length) {
                char c = line[index];

                switch (char.ToUpper(c)) {
                    case 'L':
                        Actions ^= Actions.Left;
                        break;
                    case 'R':
                        Actions ^= Actions.Right;
                        break;
                    case 'U':
                        Actions ^= Actions.Up;
                        break;
                    case 'D':
                        Actions ^= Actions.Down;
                        break;
                    case 'J':
                        Actions ^= Actions.Jump;
                        break;
                    case 'X':
                        Actions ^= Actions.Dash;
                        break;
                    case 'G':
                        Actions ^= Actions.Grab;
                        break;
                    case 'S':
                        Actions ^= Actions.Start;
                        break;
                    case 'Q':
                        Actions ^= Actions.Restart;
                        break;
                    case 'N':
                        Actions ^= Actions.Journal;
                        break;
                    case 'K':
                        Actions ^= Actions.Jump2;
                        break;
                    case 'C':
                        Actions ^= Actions.Dash2;
                        break;
                    case 'O':
                        Actions ^= Actions.Confirm;
                        break;
                    case 'Z':
                        Actions ^= Actions.DemoDash;
                        break;
                    case 'V':
                        Actions ^= Actions.DemoDash2;
                        break;
                    case 'F':
                        Actions ^= Actions.Feather;
                        index++;
                        ReadAngle(line, ref index);
                        if (string.IsNullOrEmpty(AngleStr)) {
                            UpperLimitStr = string.Empty;
                            continue;
                        }

                        ReadUpperLimit(line, ref index);
                        continue;
                }

                index++;
            }

            if (!Settings.Default.AutoRemoveMutuallyExclusiveActions && HasActions(Actions.Feather)) {
                Actions &= ~Actions.Right & ~Actions.Left & ~Actions.Up & ~Actions.Down;
            }
        }

        public int ZeroPadding { get; set; }
        public int Frames { get; set; }
        public Actions Actions { get; set; }
        public string AngleStr { get; set; }
        public string UpperLimitStr { get; set; }
        public string Notes { get; set; }

        private int ReadFrames(string line, ref int start) {
            bool foundFrames = false;
            int frames = 0;

            while (start < line.Length) {
                char c = line[start];

                if (!foundFrames) {
                    if (char.IsDigit(c)) {
                        foundFrames = true;
                        frames = c ^ 0x30;
                        if (c == '0') {
                            ZeroPadding = 1;
                        }
                    } else if (c != ' ') {
                        return frames;
                    }
                } else if (char.IsDigit(c)) {
                    if (frames < 9999) {
                        frames = frames * 10 + (c ^ 0x30);
                        if (c == '0' && frames == 0) {
                            ZeroPadding++;
                        }
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
            string angleStr = line.Substring(start).Trim();
            if (FloatRegex.Match(angleStr) is {Success: true} match && float.TryParse(match.Groups[1].Value, out float angle)) {
                AngleStr = DuplicateZeroRegex.Replace(match.Groups[1].Value, "$1");
                start += match.Groups[0].Value.Length;
                if (angle < 0f) {
                    AngleStr = "0";
                } else if (angle > 360f) {
                    AngleStr = "360";
                }

                return angle;
            } else {
                AngleStr = string.Empty;
                return 0;
            }
        }

        private float ReadUpperLimit(string line, ref int start) {
            string upperLimitStr = line.Substring(start).Trim();
            if (FloatRegex.Match(upperLimitStr) is {Success: true} match && float.TryParse(match.Groups[1].Value, out float upperLimit)) {
                UpperLimitStr = DuplicateZeroRegex.Replace(match.Groups[1].Value, "$1");
                start += match.Groups[0].Value.Length;
                if (upperLimit != 0 && upperLimit < 0.5f) {
                    UpperLimitStr = "0.5";
                } else if (upperLimit > 1f) {
                    UpperLimitStr = "1";
                }

                return upperLimit;
            } else {
                UpperLimitStr = string.Empty;
                return 0;
            }
        }

        public bool HasActions(Actions actions) {
            return (Actions & actions) != 0;
        }

        public override string ToString() {
            return Frames == 0 ? Notes : Frames.ToString().PadLeft(ZeroPadding, '0').PadLeft(4, ' ') + ActionsToString();
        }

        private string ActionsToString() {
            StringBuilder sb = new();
            if (HasActions(Actions.Left)) {
                sb.Append($"{Delimiter}L");
            }

            if (HasActions(Actions.Right)) {
                sb.Append($"{Delimiter}R");
            }

            if (HasActions(Actions.Up)) {
                sb.Append($"{Delimiter}U");
            }

            if (HasActions(Actions.Down)) {
                sb.Append($"{Delimiter}D");
            }

            if (HasActions(Actions.Jump)) {
                sb.Append($"{Delimiter}J");
            }

            if (HasActions(Actions.Jump2)) {
                sb.Append($"{Delimiter}K");
            }

            if (HasActions(Actions.DemoDash)) {
                sb.Append($"{Delimiter}Z");
            }

            if (HasActions(Actions.DemoDash2)) {
                sb.Append($"{Delimiter}V");
            }

            if (HasActions(Actions.Dash)) {
                sb.Append($"{Delimiter}X");
            }

            if (HasActions(Actions.Dash2)) {
                sb.Append($"{Delimiter}C");
            }

            if (HasActions(Actions.Grab)) {
                sb.Append($"{Delimiter}G");
            }

            if (HasActions(Actions.Start)) {
                sb.Append($"{Delimiter}S");
            }

            if (HasActions(Actions.Restart)) {
                sb.Append($"{Delimiter}Q");
            }

            if (HasActions(Actions.Journal)) {
                sb.Append($"{Delimiter}N");
            }

            if (HasActions(Actions.Confirm)) {
                sb.Append($"{Delimiter}O");
            }

            if (HasActions(Actions.Feather)) {
                sb.Append($"{Delimiter}F{Delimiter}");
                if (!string.IsNullOrEmpty(AngleStr)) {
                    sb.Append($"{AngleStr}");

                    if (!string.IsNullOrEmpty(UpperLimitStr)) {
                        sb.Append($"{Delimiter}{UpperLimitStr}");
                    }
                }
            }

            return sb.ToString();
        }

        public override bool Equals(object obj) {
            return obj is InputRecord && (InputRecord) obj == this;
        }

        public override int GetHashCode() {
            return Frames ^ (int) Actions + AngleStr.GetHashCode() + UpperLimitStr.GetHashCode();
        }

        public static bool operator ==(InputRecord one, InputRecord two) {
            bool oneNull = (object) one == null;
            bool twoNull = (object) two == null;
            if (oneNull != twoNull) {
                return false;
            } else if (oneNull) {
                return true;
            }

            return one.Actions == two.Actions && one.AngleStr == two.AngleStr && one.UpperLimitStr == two.UpperLimitStr;
        }

        public static bool operator !=(InputRecord one, InputRecord two) {
            return !(one == two);
        }

        public static void ProcessExclusiveActions(InputRecord oldInput, InputRecord newInput) {
            if (!Settings.Default.AutoRemoveMutuallyExclusiveActions) {
                return;
            }

            foreach (Actions[] exclusiveActions in ExclusiveActions) {
                foreach (Actions action in exclusiveActions) {
                    if (!oldInput.HasActions(action) && newInput.HasActions(action)) {
                        foreach (Actions exclusiveAction in exclusiveActions) {
                            if (exclusiveAction == action) {
                                continue;
                            }

                            newInput.Actions &= ~exclusiveAction;
                        }
                    }
                }
            }
        }
    }
}