using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TAS.Input {
    public class FastForward {
        public const int DefaultFastForwardSpeed = 400;

        public bool SaveState { get; set; }
        public bool HasSavedState { get; set; }
        public int speed;
        public int frame;
        public FastForward(int frame, string modifiers) {
            this.frame = frame;
            if (modifiers.EndsWith("s", StringComparison.OrdinalIgnoreCase)) {
                SaveState = true;
                modifiers = modifiers.Substring(1, modifiers.Length - 1);
            }
            if (int.TryParse(modifiers, out int speed))
                this.speed = speed;
            else
                this.speed = DefaultFastForwardSpeed;
        }


        public FastForward Clone() => (FastForward)MemberwiseClone();
    }
}
