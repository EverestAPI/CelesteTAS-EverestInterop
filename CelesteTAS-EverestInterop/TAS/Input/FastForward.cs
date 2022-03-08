using System;

namespace TAS.Input;

public record FastForward {
    private const float DefaultSpeed = 400f;
    public const float MinSpeed = 1f / 60f;
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
        if (Speed < MinSpeed) {
            Speed = MinSpeed;
        } else if (Speed > 1f) {
            Speed = (int) Math.Round(Speed);
        }
    }

    public override string ToString() {
        return "***" + (SaveState ? "S" : "") + Speed;
    }
}