using System;

namespace TAS.Input {
    public class Command {
        public readonly int frame;
        private readonly Action commandCall;
        public readonly string lineText;
        public Command(int frame, Action commandCall, string lineText) {
            this.frame = frame;
            this.commandCall = commandCall;
            this.lineText = lineText;
        }

        public void Invoke() => commandCall.Invoke();

        public Command Clone() => new Command(frame, commandCall, lineText);
    }
}
