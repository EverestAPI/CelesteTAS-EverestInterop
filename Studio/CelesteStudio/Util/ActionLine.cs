using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StudioCommunication;

namespace CelesteStudio.Util;

public struct ActionLine {
    private const char Delimiter = ',';
    public const int MaxFrames = 9999;
    public const int MaxFramesDigits = 4;

    public Actions Actions;
    public int Frames;

    public string? FeatherAngle;
    public string? FeatherMagnitude;

    public HashSet<char> CustomBindings;

    public static bool TryParse(string line, out ActionLine value, bool ignoreInvalidFloats = true) {
        value = default;
        value.CustomBindings = new HashSet<char>();

        string[] tokens = line.Trim().Split(Delimiter, StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        if (!int.TryParse(tokens[0], CultureInfo.InvariantCulture, out value.Frames)) return false;

        for (int i = 1; i < tokens.Length; i++) {
            if (string.IsNullOrWhiteSpace(tokens[i])) continue;

            var action = tokens[i][0].ActionForChar();
            value.Actions |= action;

            // Parse dash-only/move-only/custom bindings
            if (action is Actions.DashOnly) {
                for (int j = 1; j < tokens[i].Length; j++) {
                    value.Actions |= tokens[i][j].ActionForChar().ToDashOnlyActions();
                }
                continue;
            }
            if (action is Actions.MoveOnly) {
                for (int j = 1; j < tokens[i].Length; j++) {
                    value.Actions |= tokens[i][j].ActionForChar().ToMoveOnlyActions();
                }
                continue;
            }
            if (action is Actions.PressedKey) {
                value.CustomBindings = tokens[i][1..].ToHashSet();
                continue;
            }

            // Parse feather angle/magnitude
            bool validAngle = true;
            if (action == Actions.Feather && i + 1 < tokens.Length && (validAngle = float.TryParse(tokens[i + 1], CultureInfo.InvariantCulture, out float angle))) {
                if (angle > 360.0f)
                    value.FeatherAngle = "360";
                else if (angle < 0.0f)
                    value.FeatherAngle = "0";
                else
                    value.FeatherAngle = tokens[i + 1];
                i++;

                // Allow empty magnitude, so the comma won't get removed
                bool validMagnitude = true;
                if (i + 1 < tokens.Length && (string.IsNullOrWhiteSpace(tokens[i + 1]) || (validMagnitude = float.TryParse(tokens[i + 1], CultureInfo.InvariantCulture, out float _)))) {
                    // Parse again since it might be an empty string
                    if (float.TryParse(tokens[i + 1], CultureInfo.InvariantCulture, out float magnitude)) {
                        if (magnitude > 1.0f)
                            value.FeatherMagnitude = "1";
                        else if (magnitude < 0.0f)
                            value.FeatherMagnitude = "0";
                        else
                            value.FeatherMagnitude = tokens[i + 1];
                    } else {
                        value.FeatherMagnitude = tokens[i + 1];
                    }

                    i++;
                } else if (!validMagnitude && !ignoreInvalidFloats) {
                    return false;
                }
            } else if (!validAngle && i + 2 < tokens.Length && string.IsNullOrEmpty(tokens[i + 1]) && (validAngle = float.TryParse(tokens[i + 2], CultureInfo.InvariantCulture, out angle))) {
                // Empty angle, treat magnitude as angle
                if (angle > 360.0f)
                    value.FeatherAngle = "360";
                else if (angle < 0.0f)
                    value.FeatherAngle = "0";
                else
                    value.FeatherAngle = tokens[i + 1];
                i += 2;
            } else if (!validAngle && !ignoreInvalidFloats) {
                return false;
            }
        }

        return true;
    }

    public override string ToString() {
        var tasActions = Actions;
        var customBindings = CustomBindings.ToList();
        customBindings.Sort();

        string frames = Frames.ToString().PadLeft(MaxFramesDigits);
        string actions = Actions.Sorted().Aggregate("", (s, a) => $"{s}{Delimiter}{a switch {
            Actions.DashOnly => $"{Actions.DashOnly.CharForAction()}{string.Join("", tasActions.GetDashOnly().Select(ActionsUtils.CharForAction))}",
            Actions.MoveOnly => $"{Actions.MoveOnly.CharForAction()}{string.Join("", tasActions.GetMoveOnly().Select(ActionsUtils.CharForAction))}",
            Actions.PressedKey => $"{Actions.PressedKey.CharForAction()}{string.Join("", customBindings)}",
            _ => a.CharForAction().ToString(),
        }}");
        string featherAngle = Actions.HasFlag(Actions.Feather) ? $"{Delimiter}{FeatherAngle ?? ""}" : string.Empty;
        string featherMagnitude = Actions.HasFlag(Actions.Feather) && FeatherMagnitude != null ? $"{Delimiter}{FeatherMagnitude}" : string.Empty;

        return $"{frames}{actions}{featherAngle}{featherMagnitude}";
    }
}