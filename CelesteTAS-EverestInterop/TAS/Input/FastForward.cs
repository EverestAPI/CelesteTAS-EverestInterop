using System;

namespace TAS.Input {
    public class FastForward {
        public const int DefaultFastForwardSpeed = 400;
        public int Frame;
        public int Speed;

        public FastForward(int frame, string modifiers) {
            this.Frame = frame;
            if (modifiers.EndsWith("s", StringComparison.OrdinalIgnoreCase)) {
                SaveState = true;
                modifiers = modifiers.Substring(1, modifiers.Length - 1);
            }

            if (int.TryParse(modifiers, out int speed)) {
                this.Speed = speed;
            } else {
                this.Speed = DefaultFastForwardSpeed;
            }
        }

        public bool SaveState { get; set; }
        public bool HasSavedState { get; set; }


        public FastForward Clone() => (FastForward) MemberwiseClone();
    }
}