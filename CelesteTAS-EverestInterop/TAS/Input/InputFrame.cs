using System;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using TAS.Utils;

namespace TAS.Input {
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
        DemoDash2 = 1 << 15
    }

    public record InputFrame {
        public Actions Actions { get; private set; }
        public float Angle { get; private set; }
        public float UpperLimit { get; private set; } = 1f;
        public Vector2 AngleVector2 { get; private set; }
        public Vector2Short AngleVector2Short { get; private set; }
        public int Frames { get; private set; }
        public int Line { get; private set; }
        public InputFrame Previous { get; private set; }
        public InputFrame Next { get; private set; }
        public int RepeatCount { get; set; }
        public int RepeatIndex { get; set; }
        public string RepeatString => RepeatCount > 1 ? $" {RepeatIndex}/{RepeatCount}" : "";

        public bool HasActions(Actions actions) =>
            (Actions & actions) != 0;

        public float GetX() =>
            (float) Math.Sin(Angle * Math.PI / 180.0);

        public float GetY() =>
            (float) Math.Cos(Angle * Math.PI / 180.0);

        public override string ToString() {
            return Frames + ToActionsString();
        }

        public string ToActionsString() {
            StringBuilder sb = new();
            if (HasActions(Actions.Left)) {
                sb.Append(",L");
            }

            if (HasActions(Actions.Right)) {
                sb.Append(",R");
            }

            if (HasActions(Actions.Up)) {
                sb.Append(",U");
            }

            if (HasActions(Actions.Down)) {
                sb.Append(",D");
            }

            if (HasActions(Actions.Jump)) {
                sb.Append(",J");
            }

            if (HasActions(Actions.Jump2)) {
                sb.Append(",K");
            }

            if (HasActions(Actions.DemoDash)) {
                sb.Append(",Z");
            }

            if (HasActions(Actions.DemoDash2)) {
                sb.Append(",V");
            }

            if (HasActions(Actions.Dash)) {
                sb.Append(",X");
            }

            if (HasActions(Actions.Dash2)) {
                sb.Append(",C");
            }

            if (HasActions(Actions.Grab)) {
                sb.Append(",G");
            }

            if (HasActions(Actions.Start)) {
                sb.Append(",S");
            }

            if (HasActions(Actions.Restart)) {
                sb.Append(",Q");
            }

            if (HasActions(Actions.Journal)) {
                sb.Append(",N");
            }

            if (HasActions(Actions.Confirm)) {
                sb.Append(",O");
            }

            if (HasActions(Actions.Feather)) {
                sb.Append(",F,").Append(Angle == 0 ? string.Empty : Angle.ToString(CultureInfo.InvariantCulture));
                if (Math.Abs(UpperLimit - 1f) > 1e-10) {
                    sb.Append($",{UpperLimit}");
                }
            }

            return sb.ToString();
        }

        public static bool TryParse(string line, int studioLine, InputFrame prevInputFrame, out InputFrame inputFrame, int repeatIndex = 0,
            int repeatCount = 0) {
            int index = line.IndexOf(",", StringComparison.Ordinal);
            string framesStr;
            if (index == -1) {
                framesStr = line;
                index = 0;
            } else {
                framesStr = line.Substring(0, index);
            }

            if (!int.TryParse(framesStr, out int frames)) {
                inputFrame = null;
                return false;
            }

            frames = Math.Min(frames, 9999);
            inputFrame = new InputFrame {
                Line = studioLine,
                Frames = frames,
                RepeatIndex = repeatIndex,
                RepeatCount = repeatCount
            };

            while (index < line.Length) {
                char c = line[index];

                switch (char.ToUpper(c)) {
                    case 'L':
                        inputFrame.Actions ^= Actions.Left;
                        break;
                    case 'R':
                        inputFrame.Actions ^= Actions.Right;
                        break;
                    case 'U':
                        inputFrame.Actions ^= Actions.Up;
                        break;
                    case 'D':
                        inputFrame.Actions ^= Actions.Down;
                        break;
                    case 'J':
                        inputFrame.Actions ^= Actions.Jump;
                        break;
                    case 'X':
                        inputFrame.Actions ^= Actions.Dash;
                        break;
                    case 'G':
                        inputFrame.Actions ^= Actions.Grab;
                        break;
                    case 'S':
                        inputFrame.Actions ^= Actions.Start;
                        break;
                    case 'Q':
                        inputFrame.Actions ^= Actions.Restart;
                        break;
                    case 'N':
                        inputFrame.Actions ^= Actions.Journal;
                        break;
                    case 'K':
                        inputFrame.Actions ^= Actions.Jump2;
                        break;
                    case 'C':
                        inputFrame.Actions ^= Actions.Dash2;
                        break;
                    case 'O':
                        inputFrame.Actions ^= Actions.Confirm;
                        break;
                    case 'Z':
                        inputFrame.Actions ^= Actions.DemoDash;
                        break;
                    case 'V':
                        inputFrame.Actions ^= Actions.DemoDash2;
                        break;
                    case 'F':
                        inputFrame.Actions ^= Actions.Feather;
                        index++;
                        string angleAndUpperLimit = line.Substring(index + 1).Trim();
                        if (angleAndUpperLimit.IsNotNullOrEmpty()) {
                            string[] args = angleAndUpperLimit.Split(',');
                            string angle = args[0];
                            if (float.TryParse(angle, out float angleFloat)) {
                                inputFrame.Angle = angleFloat;
                            }

                            if (args.Length >= 2 && float.TryParse(args[1], out float upperLimitFloat)) {
                                inputFrame.UpperLimit = upperLimitFloat;
                            }
                        }

                        inputFrame.AngleVector2 = AnalogHelper.ComputeAngleVector2(inputFrame, out Vector2Short angleVector2Short);
                        inputFrame.AngleVector2Short = angleVector2Short;
                        continue;
                }

                index++;
            }

            if (prevInputFrame != null) {
                prevInputFrame.Next = inputFrame;
                inputFrame.Previous = prevInputFrame;
            }

            LibTasHelper.WriteLibTasFrame(inputFrame);

            return true;
        }
    }
}