using System;

namespace TAS.Input;

/// A breakpoint to which the TAS will fast-forward at a high speed
public record FastForward {
    private const float DefaultSpeed = 400.0f;

    public readonly int Frame;
    public readonly int Line;
    public readonly bool SaveState;
    public readonly float Speed;

    public FastForward(int frame, string modifiers, int line) {
        Frame = frame;
        Line = line;
        if (modifiers.StartsWith("s", StringComparison.OrdinalIgnoreCase)) {
            SaveState = true;
            modifiers = modifiers.Substring(1, modifiers.Length - 1);
        } else {
            SaveState = false;
        }

        Speed = float.TryParse(modifiers, out float speed) ? speed : DefaultSpeed;
    }

    public override string ToString() {
        return "***" + (SaveState ? "S" : "") + Speed;
    }
}