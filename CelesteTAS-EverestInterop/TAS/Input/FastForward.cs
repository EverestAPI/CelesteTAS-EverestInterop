using System;

namespace TAS.Input {
    public class FastForward {
        public const int DefaultSpeed = 400;
        public readonly int Frame;
        public readonly int Line;
        public readonly int Speed;

        public FastForward(int frame, string modifiers, int line) {
            Frame = frame;
            Line = line;
            if (modifiers.StartsWith("s", StringComparison.OrdinalIgnoreCase)) {
                SaveState = true;
                modifiers = modifiers.Substring(1, modifiers.Length - 1);
            }

            if (int.TryParse(modifiers, out int speed)) {
                Speed = speed;
            } else {
                Speed = DefaultSpeed;
            }
        }

        public bool SaveState { get; }
        public bool HasSavedState { get; set; }

        public FastForward Clone() => (FastForward) MemberwiseClone();

        public override string ToString() {
            return "***" + (SaveState ? "S" : "") + Speed;
        }
    }
}