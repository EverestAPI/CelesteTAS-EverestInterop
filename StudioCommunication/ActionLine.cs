using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StudioCommunication;

public struct ActionLine() {
    public const char Delimiter = ',';
    public const int MaxFrames = 9999;
    public const int MaxFramesDigits = 4;

    public Actions Actions;

    private string frames = "";
    private int frameCount;

    public string Frames {
        get => frames;
        set {
            frames = value.Trim();
            frameCount = int.TryParse(frames, out int x) ? x : 0;
        }
    }
    public int FrameCount {
        get => frameCount;
        set {
            frameCount = value;
            frames = frameCount.ToString();
        }
    }

    public string? FeatherAngle;
    public string? FeatherMagnitude;

    public HashSet<char> CustomBindings = [];

    public static ActionLine? Parse(string line, bool ignoreInvalidFloats = true) => TryParseStrict(line, out var actionLine, ignoreInvalidFloats) ? actionLine : null;
    public static bool TryParse(string line, out ActionLine value, bool ignoreInvalidFloats = true) => TryParseStrict(line, out value, ignoreInvalidFloats) || TryParseLoose(line, out value, ignoreInvalidFloats);

    /// Parses action-lines, which mostly follow the correct formatting (for example: "  15,R,Z")
    public static bool TryParseStrict(string line, out ActionLine actionLine, bool ignoreInvalidFloats = true) {
        actionLine = default;
        actionLine.CustomBindings = new HashSet<char>();

#if NET5_0_OR_GREATER
        string[] tokens = line.Trim().Split(Delimiter, StringSplitOptions.TrimEntries);
#else
        string[] tokens = line.Trim().Split(Delimiter).Select(token => token.Trim()).ToArray();
#endif
        if (tokens.Length == 0) return false;

        if (string.IsNullOrWhiteSpace(tokens[0]) || int.TryParse(tokens[0], out _)) {
            actionLine.Frames = tokens[0];
        } else {
            return false;
        }

        for (int i = 1; i < tokens.Length; i++) {
            if (string.IsNullOrWhiteSpace(tokens[i])) continue;

            var action = tokens[i][0].ActionForChar();
            actionLine.Actions |= action;

            // Parse dash-only/move-only/custom bindings
            if (action is Actions.DashOnly) {
                for (int j = 1; j < tokens[i].Length; j++) {
                    actionLine.Actions |= tokens[i][j].ActionForChar().ToDashOnlyActions();
                }
                continue;
            }
            if (action is Actions.MoveOnly) {
                for (int j = 1; j < tokens[i].Length; j++) {
                    actionLine.Actions |= tokens[i][j].ActionForChar().ToMoveOnlyActions();
                }
                continue;
            }
            if (action is Actions.PressedKey) {
                actionLine.CustomBindings = tokens[i][1..].Select(char.ToUpper).ToHashSet();
                continue;
            }
            if (tokens[i].Length != 1) {
                // This token isn't allowed to have multiple actions
                return false;
            }

            // Parse feather angle/magnitude
            bool validAngle = true;
            if (action == Actions.Feather && i + 1 < tokens.Length && (validAngle = float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))) {
                if (angle > 360.0f)
                    actionLine.FeatherAngle = "360";
                else if (angle < 0.0f)
                    actionLine.FeatherAngle = "0";
                else
                    actionLine.FeatherAngle = tokens[i + 1];
                i++;

                // Allow empty magnitude, so the comma won't get removed
                bool validMagnitude = true;
                if (i + 1 < tokens.Length && (string.IsNullOrWhiteSpace(tokens[i + 1]) || (validMagnitude = float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float _)))) {
                    // Parse again since it might be an empty string
                    if (float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float magnitude)) {
                        if (magnitude > 1.0f)
                            actionLine.FeatherMagnitude = "1";
                        else if (magnitude < 0.0f)
                            actionLine.FeatherMagnitude = "0";
                        else
                            actionLine.FeatherMagnitude = tokens[i + 1];
                    } else {
                        actionLine.FeatherMagnitude = tokens[i + 1];
                    }

                    i++;
                } else if (!validMagnitude && !ignoreInvalidFloats) {
                    return false;
                }
            } else if (!validAngle && i + 2 < tokens.Length && string.IsNullOrEmpty(tokens[i + 1]) && (validAngle = float.TryParse(tokens[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out angle))) {
                // Empty angle, treat magnitude as angle
                if (angle > 360.0f)
                    actionLine.FeatherAngle = "360";
                else if (angle < 0.0f)
                    actionLine.FeatherAngle = "0";
                else
                    actionLine.FeatherAngle = tokens[i + 1];
                i += 2;
            } else if (!validAngle && !ignoreInvalidFloats) {
                return false;
            }
        }

        if (actionLine.Frames.Length == 0 &&
            actionLine.Actions == Actions.None &&
            actionLine.CustomBindings.Count == 0 &&
            actionLine.FeatherAngle == null &&
            actionLine.FeatherMagnitude == null)
        {
            // Frameless action lines require some other actions
            return false;
        }

        return true;
    }

    /// Parses action-lines, which mostly are correct (for example: "1gd")
    private enum ParseState { Frame, Action, DashOnly, MoveOnly, PressedKey, FeatherAngle, FeatherMagnitude }
    public static bool TryParseLoose(string line, out ActionLine actionLine, bool ignoreInvalidFloats = true) {
        actionLine = default;
        actionLine.CustomBindings = new HashSet<char>();

        ParseState state = ParseState.Frame;
        string currValue = "";

        foreach (char c in line) {
            if (char.IsWhiteSpace(c)) {
                continue;
            }

            switch (state) {
                case ParseState.Frame:
                {
                    if (c == Delimiter) {
                        if (!string.IsNullOrWhiteSpace(currValue) && !int.TryParse(currValue, out _)) {
                            // Invalid action-line
                            return false;
                        }
                        actionLine.Frames = currValue;

                        currValue = "";
                        state = ParseState.Action;

                        continue;
                    }

                    if (char.IsDigit(c)) {
                        currValue += c;
                    } else {
                        if (!int.TryParse(currValue, out _)) {
                            // Invalid action-line
                            return false;
                        }
                        actionLine.Frames = currValue;

                        currValue = "";
                        goto case ParseState.Action;
                    }
                    break;
                }

                case ParseState.Action:
                {
                    if (c == Delimiter) {
                        continue;
                    }

                    var action = c.ActionForChar();
                    actionLine.Actions |= action;
                    state = action switch {
                        Actions.DashOnly => ParseState.DashOnly,
                        Actions.MoveOnly => ParseState.MoveOnly,
                        Actions.PressedKey => ParseState.PressedKey,
                        Actions.Feather => ParseState.FeatherAngle,
                        _ => ParseState.Action,
                    };
                    break;
                }

                case ParseState.DashOnly:
                {
                    if (c == Delimiter) {
                        state = ParseState.Action;
                        continue;
                    }

                    var action = c.ActionForChar();
                    if (action is not (Actions.Left or Actions.Right or Actions.Up or Actions.Down)) {
                        goto case ParseState.Action;
                    }
                    actionLine.Actions |= action.ToDashOnlyActions();
                    break;
                }

                case ParseState.MoveOnly:
                {
                    if (c == Delimiter) {
                        state = ParseState.Action;
                        continue;
                    }

                    var action = c.ActionForChar();
                    if (action is not (Actions.Left or Actions.Right or Actions.Up or Actions.Down)) {
                        goto case ParseState.Action;
                    }
                    actionLine.Actions |= action.ToMoveOnlyActions();
                    break;
                }

                case ParseState.PressedKey:
                {
                    if (c == Delimiter) {
                        state = ParseState.Action;
                        continue;
                    }

                    actionLine.CustomBindings.Add(char.ToUpper(c));
                    break;
                }

                case ParseState.FeatherAngle:
                {
                    if (c == Delimiter) {
                        state = ParseState.FeatherMagnitude;
                        continue;
                    }

                    if (char.IsDigit(c) || c == '.') {
                        actionLine.FeatherAngle ??= string.Empty;
                        actionLine.FeatherAngle += c;
                    } else {
                        goto case ParseState.Action;
                    }
                    break;
                }

                case ParseState.FeatherMagnitude:
                {
                    if (c == Delimiter) {
                        state = ParseState.Action;
                        continue;
                    }

                    if (char.IsDigit(c) || c == '.') {
                        actionLine.FeatherMagnitude ??= string.Empty;
                        actionLine.FeatherMagnitude += c;
                    } else {
                        goto case ParseState.Action;
                    }
                    break;
                }
            }
        }

        // Clamp angle / magnitude
        if (actionLine.FeatherAngle is { } angleString) {
            if (float.TryParse(angleString, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle)) {
                actionLine.FeatherAngle = Math.Clamp(angle, 0.0f, 360.0f).ToString(CultureInfo.InvariantCulture);
            } else if (!ignoreInvalidFloats) {
                return false;
            }
        }
        if (actionLine.FeatherMagnitude is { } magnitudeString) {
            if (float.TryParse(magnitudeString, NumberStyles.Float, CultureInfo.InvariantCulture, out float magnitude)) {
                actionLine.FeatherMagnitude = Math.Clamp(magnitude, 0.0f, 1.0f).ToString(CultureInfo.InvariantCulture);
            } else if (!ignoreInvalidFloats) {
                return false;
            }
        }

        return state != ParseState.Frame;
    }

    public override string ToString() {
        var tasActions = Actions;
        var customBindings = CustomBindings.ToList();
        customBindings.Sort();

        string actions = Actions.Sorted().Aggregate("", (s, a) => $"{s}{Delimiter}{a switch {
            Actions.DashOnly => $"{Actions.DashOnly.CharForAction()}{string.Join("", tasActions.GetDashOnly().Select(ActionsUtils.CharForAction))}",
            Actions.MoveOnly => $"{Actions.MoveOnly.CharForAction()}{string.Join("", tasActions.GetMoveOnly().Select(ActionsUtils.CharForAction))}",
            Actions.PressedKey => $"{Actions.PressedKey.CharForAction()}{string.Join("", customBindings)}",
            _ => a.CharForAction().ToString(),
        }}");
        string featherAngle = Actions.HasFlag(Actions.Feather) ? $"{Delimiter}{FeatherAngle ?? ""}" : string.Empty;
        string featherMagnitude = Actions.HasFlag(Actions.Feather) && FeatherMagnitude != null ? $"{Delimiter}{FeatherMagnitude}" : string.Empty;

        return $"{Frames,MaxFramesDigits}{actions}{featherAngle}{featherMagnitude}";
    }
}
