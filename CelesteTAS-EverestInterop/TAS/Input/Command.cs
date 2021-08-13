using System;

namespace TAS.Input {
    public record Command {
        private readonly Action commandCall; // null if ExecuteAtStart = true
        public readonly TasCommandAttribute Attribute;
        public readonly string[] Args;
        public readonly int Frame;
        public readonly string FilePath;
        public readonly int LineNumber; // form zero
        public readonly string LineText;

        public Command(TasCommandAttribute attribute, int frame, Action commandCall, string[] args, string filePath, int lineNumber, string lineText) {
            Attribute = attribute;
            Frame = frame;
            this.commandCall = commandCall;
            Args = args;
            FilePath = filePath;
            LineNumber = lineNumber;
            LineText = lineText;
        }

        public void Invoke() => commandCall?.Invoke();

        public bool IsName(string name) {
            return Attribute.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}