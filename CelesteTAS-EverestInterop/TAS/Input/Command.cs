using System;

namespace TAS.Input {
    public class Command {
        private readonly Action commandCall; // null if ExecuteAtStart = true
        public readonly int Frame;
        public readonly string FilePath;
        public readonly int LineNumber; // form zero
        public readonly string LineText;

        public Command(int frame, Action commandCall, string filePath, int lineNumber, string lineText) {
            Frame = frame;
            this.commandCall = commandCall;
            FilePath = filePath;
            LineNumber = lineNumber;
            LineText = lineText;
        }

        public void Invoke() => commandCall?.Invoke();
    }
}