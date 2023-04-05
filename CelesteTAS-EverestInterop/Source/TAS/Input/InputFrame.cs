using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Utils;

namespace TAS.Input;

public record InputFrame {
    private static readonly Regex DashOnlyDirectionRegex = new(@"[A]([LRUD]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveOnlyDirectionRegex = new(@"[M]([LRUD]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PressedKeyRegex = new(@"[P]([A-Z]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Actions Actions { get; private set; }
    public float Angle { get; private set; }
    public float UpperLimit { get; private set; } = 1f;
    public Vector2 AngleVector2 { get; private set; }

    public Vector2 DashOnlyVector2 {
        get {
            Vector2 result = Vector2.Zero;
            if (Actions.HasFlag(Actions.LeftDashOnly)) {
                result.X = -1;
            }

            if (Actions.HasFlag(Actions.RightDashOnly)) {
                result.X = 1;
            }

            if (Actions.HasFlag(Actions.UpDashOnly)) {
                result.Y = 1;
            }

            if (Actions.HasFlag(Actions.DownDashOnly)) {
                result.Y = -1;
            }

            return result;
        }
    }

    public Vector2Short AngleVector2Short { get; private set; }
    public Vector2Short DashOnlyVector2Short { get; private set; }
    public HashSet<Keys> PressedKeys { get; } = new();

    public int Frames { get; private set; }
    public int Line { get; private set; }
    public InputFrame Previous { get; private set; }
    public InputFrame Next { get; private set; }
    public int RepeatCount { get; set; }
    public int RepeatIndex { get; set; }
    public string RepeatString => RepeatCount > 1 ? $" {RepeatIndex}/{RepeatCount}" : "";
    public int FrameOffset { get; private set; }

    public bool HasActions(Actions actions) =>
        (Actions & actions) != 0;

    public float GetX() {
        return Angle switch {
            0f => 0,
            90f => 1,
            180f => 0,
            270f => -1,
            360f => 0,
            _ => (float) Math.Sin(Angle * Math.PI / 180.0)
        };
    }

    public float GetY() {
        return Angle switch {
            0f => 1,
            90f => 0,
            180f => -1,
            270f => 0,
            360f => 1,
            _ => (float) Math.Cos(Angle * Math.PI / 180.0)
        };
    }

    public override string ToString() {
        return Frames + ToActionsString();
    }

    public string ToActionsString() {
        StringBuilder sb = new();

        foreach (KeyValuePair<char, Actions> pair in ActionsUtils.Chars) {
            Actions actions = pair.Value;
            if (HasActions(actions)) {
                sb.Append($",{pair.Key}");

                if (actions == Actions.DashOnly) {
                    foreach (KeyValuePair<char, Actions> dashOnlyPair in ActionsUtils.DashOnlyChars) {
                        if (HasActions(dashOnlyPair.Value)) {
                            sb.Append($"{dashOnlyPair.Key}");
                        }
                    }
                } else if (actions == Actions.MoveOnly) {
                    foreach (KeyValuePair<char, Actions> moveOnlyPair in ActionsUtils.MoveOnlyChars) {
                        if (HasActions(moveOnlyPair.Value)) {
                            sb.Append($"{moveOnlyPair.Key}");
                        }
                    }
                } else if (actions == Actions.PressedKey) {
                    foreach (Keys key in PressedKeys) {
                        sb.Append((char) key);
                    }
                } else if (actions == Actions.Feather) {
                    sb.Append(",").Append(Angle == 0 ? string.Empty : Angle.ToString(CultureInfo.InvariantCulture));
                    if (Math.Abs(UpperLimit - 1f) > 1e-10) {
                        sb.Append($",{UpperLimit}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    public static bool TryParse(string line, int studioLine, InputFrame prevInputFrame, out InputFrame inputFrame, int repeatIndex = 0,
        int repeatCount = 0, int frameOffset = 0) {
        int index = line.IndexOf(",", StringComparison.Ordinal);
        string framesStr;
        if (index == -1) {
            framesStr = line;
            index = 0;
        } else {
            framesStr = line.Substring(0, index);
        }

        if (!int.TryParse(framesStr, out int frames) || frames <= 0) {
            inputFrame = null;
            return false;
        }

        frames = Math.Min(frames, 9999);
        inputFrame = new InputFrame {
            Line = studioLine,
            Frames = frames,
            RepeatIndex = repeatIndex,
            RepeatCount = repeatCount,
            FrameOffset = frameOffset,
        };

        while (index < line.Length) {
            char c = char.ToUpper(line[index]);

            if (c is >= 'A' and <= 'Z' && IsPressedKey()) {
                inputFrame.PressedKeys.Add((Keys) c); // enum values for letter keys match ASCII uppercase letters
            } else if (ActionsUtils.TryParse(c, out Actions actions)) {
                if (IsDashOnlyDirection()) {
                    actions = actions.ToDashOnlyActions();
                } else if (IsMoveOnlyDirection()) {
                    actions = actions.ToMoveOnlyActions();
                } else if (actions == Actions.Feather) {
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
                            inputFrame.UpperLimit = Calc.Clamp(upperLimitFloat, 0.26f, 1f);
                        }
                    }

                    inputFrame.AngleVector2 = AnalogHelper.ComputeAngleVector2(inputFrame, out Vector2Short angleVector2Short);
                    inputFrame.AngleVector2Short = angleVector2Short;
                    continue;
                }

                inputFrame.Actions ^= actions;
            }

            index++;
        }

        Vector2 v = inputFrame.DashOnlyVector2;
        inputFrame.DashOnlyVector2Short = new Vector2Short((short) (v.X * 32767), (short) (v.Y * 32767));

        if (prevInputFrame != null) {
            prevInputFrame.Next = inputFrame;
            inputFrame.Previous = prevInputFrame;
        }

        LibTasHelper.WriteLibTasFrame(inputFrame);

        return true;

        bool IsDashOnlyDirection() {
            string subLine = line.Substring(0, index + 1);
            return DashOnlyDirectionRegex.IsMatch(subLine);
        }

        bool IsMoveOnlyDirection() {
            string subLine = line.Substring(0, index + 1);
            return MoveOnlyDirectionRegex.IsMatch(subLine);
        }

        bool IsPressedKey() {
            string subLine = line.Substring(0, index + 1);
            return PressedKeyRegex.IsMatch(subLine);
        }
    }
}