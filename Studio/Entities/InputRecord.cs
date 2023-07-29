using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using StudioCommunication;

namespace CelesteStudio.Entities;

public class InputRecord {
    private const char Delimiter = ',';
    private static readonly Regex DuplicateZeroRegex = new(@"^0+([^.])", RegexOptions.Compiled);
    private static readonly Regex FloatRegex = new(@"^,-?([0-9.]+)", RegexOptions.Compiled);
    private static readonly Regex EmptyLineRegex = new(@"^\s*$", RegexOptions.Compiled);
    public static readonly Regex CommentSymbolRegex = new(@"^\s*#", RegexOptions.Compiled);
    private static readonly Regex CommentRoomRegex = new(@"^\s*#lvl_", RegexOptions.Compiled);
    private static readonly Regex CommentTimeRegex = new(@"^\s*#(\d+:)?\d{1,2}:\d{2}\.\d{3}", RegexOptions.Compiled);
    public static readonly Regex CommentLineRegex = new(@"^\s*#.*", RegexOptions.Compiled);
    public static readonly Regex BreakpointRegex = new(@"^\s*\*\*\*", RegexOptions.Compiled);
    public static readonly Regex InputFrameRegex = new(@"^(\s*\d+)", RegexOptions.Compiled);
    private static readonly Regex DashOnlyDirectionRegex = new(@"[A]([LRUD]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveOnlyDirectionRegex = new(@"[M]([LRUD]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PressedKeyRegex = new(@"[P]([A-Z]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Actions[][] ExclusiveActions = {
        new[] {Actions.Dash, Actions.Dash2, Actions.DemoDash, Actions.DemoDash2},
        new[] {Actions.Jump, Actions.Jump2},
        new[] {Actions.Grab, Actions.Grab2},
        new[] {Actions.Up, Actions.Down, Actions.Feather},
        new[] {Actions.Left, Actions.Right, Actions.Feather},
        new[] {Actions.UpDashOnly, Actions.DownDashOnly},
        new[] {Actions.LeftDashOnly, Actions.RightDashOnly},
        new[] {Actions.UpMoveOnly, Actions.DownMoveOnly},
        new[] {Actions.LeftMoveOnly, Actions.RightMoveOnly},
    };

    public int Frames { get; set; }
    public Actions Actions { get; set; }
    public string AngleStr { get; set; }
    public string UpperLimitStr { get; set; }
    public SortedSet<char> PressedKeys { get; } = new();
    public string LineText { get; }
    public bool IsInput { get; }
    public bool IsComment { get; }
    public bool IsCommentRoom { get; }
    public bool IsCommentTime { get; }
    public bool IsCommand { get; }
    public bool IsBreakpoint { get; }
    public bool IsEmpty { get; }
    public bool IsEmptyOrZeroFrameInput => IsEmpty || IsInput && Frames == 0;

    public InputRecord(int frames, string actions) : this($"{frames},{actions}") { }

    public InputRecord(string line) {
        LineText = line;

        int index = 0;
        Frames = ReadFrames(line, ref index);
        if (Frames <= 0) {
            if (CommentSymbolRegex.IsMatch(line)) {
                IsComment = true;
                if (CommentRoomRegex.IsMatch(line)) {
                    IsCommentRoom = true;
                } else if (CommentTimeRegex.IsMatch(line)) {
                    IsCommentTime = true;
                }
            } else if (BreakpointRegex.IsMatch(line)) {
                IsBreakpoint = true;
            } else if (InputFrameRegex.IsMatch(line)) {
                IsInput = true;
            } else if (EmptyLineRegex.IsMatch(line)) {
                IsEmpty = true;
            } else {
                IsCommand = true;
            }
        } else {
            IsInput = true;
        }

        if (!IsInput) {
            return;
        }

        while (index < line.Length) {
            char c = char.ToUpper(line[index]);

            if (c is >= 'A' and <= 'Z' && IsPressedKey()) {
                PressedKeys.Add(c);
            } else if (ActionsUtils.TryParse(c, out Actions actions)) {
                if (IsDashOnlyDirection()) {
                    actions = actions.ToDashOnlyActions();
                } else if (IsMoveOnlyDirection()) {
                    actions = actions.ToMoveOnlyActions();
                } else if (actions == Actions.Feather) {
                    Actions ^= Actions.Feather;
                    index++;
                    ClampAngle(line, ref index);
                    if (string.IsNullOrEmpty(AngleStr)) {
                        UpperLimitStr = string.Empty;
                        continue;
                    }

                    ClampUpperLimit(line, ref index);
                    continue;
                }

                Actions ^= actions;
            }

            index++;
        }

        if (!Settings.Instance.AutoRemoveMutuallyExclusiveActions && HasActions(Actions.Feather)) {
            Actions &= ~Actions.Right & ~Actions.Left & ~Actions.Up & ~Actions.Down;
        }

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
                    break;
                }
            } else if (char.IsDigit(c)) {
                if (frames < 9999) {
                    frames = frames * 10 + (c ^ 0x30);
                } else {
                    frames = 9999;
                }
            } else if (c != ' ') {
                break;
            }

            start++;
        }

        return frames switch {
            < 0 => 0,
            > 9999 => 9999,
            _ => frames
        };
    }

    private void ClampAngle(string line, ref int start) {
        string angleStr = line.Substring(start).Trim();
        if (FloatRegex.Match(angleStr) is {Success: true} match && float.TryParse(match.Groups[1].Value, out float angle)) {
            AngleStr = DuplicateZeroRegex.Replace(match.Groups[1].Value, "$1");
            start += match.Groups[0].Value.Length;
            if (angle < 0f) {
                AngleStr = "0";
            } else if (angle > 360f) {
                AngleStr = "360";
            }
        } else {
            AngleStr = string.Empty;
        }
    }

    private void ClampUpperLimit(string line, ref int start) {
        string upperLimitStr = line.Substring(start).Trim();
        if (FloatRegex.Match(upperLimitStr) is {Success: true} match && float.TryParse(match.Groups[1].Value, out float upperLimit)) {
            UpperLimitStr = DuplicateZeroRegex.Replace(match.Groups[1].Value, "$1");
            start += match.Groups[0].Value.Length;
            if (upperLimit is > 0.2f and < 0.26f) {
                UpperLimitStr = "0.26";
            } else if (upperLimit != 0 && upperLimit < 0.26f) {
                UpperLimitStr = "0.2";
            } else if (upperLimit > 1f) {
                UpperLimitStr = "1";
            }
        } else {
            UpperLimitStr = string.Empty;
        }
    }

    public bool HasActions(Actions actions) {
        return (Actions & actions) != 0;
    }

    public override string ToString() {
        return !IsInput ? LineText : Frames.ToString().PadLeft(4, ' ') + ActionsToString();
    }

    public string ActionsToString() {
        StringBuilder sb = new();

        foreach (KeyValuePair<char, Actions> pair in ActionsUtils.Chars) {
            Actions actions = pair.Value;
            if (HasActions(actions)) {
                sb.Append($"{Delimiter}{pair.Key}");

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
                    foreach (char key in PressedKeys) {
                        sb.Append(key);
                    }
                } else if (actions == Actions.Feather) {
                    sb.Append(Delimiter);
                    if (!string.IsNullOrEmpty(AngleStr)) {
                        sb.Append($"{AngleStr}");

                        if (!string.IsNullOrEmpty(UpperLimitStr)) {
                            sb.Append($"{Delimiter}{UpperLimitStr}");
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }

    public bool IsScreenTransition() {
        if (!IsInput || Actions != Actions.None) {
            return false;
        }

        List<InputRecord> inputRecords = Studio.Instance.InputRecords;
        int index = inputRecords.IndexOf(this);
        if (index == -1) {
            return false;
        }

        while (++index < inputRecords.Count) {
            InputRecord next = inputRecords[index];
            if (next.IsEmptyOrZeroFrameInput) {
                continue;
            }

            return next.IsCommentRoom;
        }

        return false;
    }

    public InputRecord Previous(Func<InputRecord, bool> predicate = null) {
        predicate ??= _ => true;
        List<InputRecord> inputRecords = Studio.Instance.InputRecords;
        int index = inputRecords.IndexOf(this);
        if (index == -1) {
            return null;
        }

        while (--index >= 0) {
            InputRecord previous = inputRecords[index];
            if (predicate(previous)) {
                return previous;
            }
        }

        return null;
    }

    public InputRecord Next(Func<InputRecord, bool> predicate = null) {
        predicate ??= _ => true;
        List<InputRecord> inputRecords = Studio.Instance.InputRecords;
        int index = inputRecords.IndexOf(this);
        if (index == -1) {
            return null;
        }

        while (++index < inputRecords.Count) {
            InputRecord next = inputRecords[index];
            if (predicate(next)) {
                return next;
            }
        }

        return null;
    }

    public static void ProcessExclusiveActions(InputRecord oldInput, InputRecord newInput) {
        if (!Settings.Instance.AutoRemoveMutuallyExclusiveActions) {
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