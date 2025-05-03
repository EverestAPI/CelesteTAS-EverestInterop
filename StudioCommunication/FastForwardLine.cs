using System;

namespace StudioCommunication;

/// A parsed line representing a fast-forward point in the TAS
public record struct FastForwardLine(bool SaveState, string SpeedText, float? PlaybackSpeed) {

    public static FastForwardLine? Parse(string line) => TryParse(line, out var fastForwardLine) ? fastForwardLine : null;
    public static bool TryParse(string line, out FastForwardLine fastForwardLine) {
        fastForwardLine = default;

        string lineTrimmed = line.TrimStart();
        if (!lineTrimmed.StartsWith("***")) {
            return false;
        }

        string modifiers = lineTrimmed["***".Length..];
        if (modifiers.StartsWith("s", StringComparison.OrdinalIgnoreCase)) {
            fastForwardLine.SaveState = true;
            modifiers = modifiers.Substring(1, modifiers.Length - 1);
        } else {
            fastForwardLine.SaveState = false;
        }

        fastForwardLine.SpeedText = modifiers;
        fastForwardLine.PlaybackSpeed = float.TryParse(fastForwardLine.SpeedText, out float x) ? x : null;
        return true;
    }

    public override string ToString() {
        return $"***{(SaveState ? "S" : "")}{SpeedText}";
    }
    public string Format() {
        return $"***{(SaveState ? "S" : "")}{(PlaybackSpeed != null ? PlaybackSpeed.Value : "")}";
    }
}
