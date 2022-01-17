using System;

namespace TAS.Input {
    public record Command {
        public readonly string[] Args;
        public readonly TasCommandAttribute Attribute;
        private readonly Action commandCall; // null if ExecuteAtStart = true
        public readonly string FilePath;
        public readonly int Frame;
        public readonly int StudioLineNumber; // form zero

        public Command(TasCommandAttribute attribute, int frame, Action commandCall, string[] args, string filePath, int studioLineNumber) {
            Attribute = attribute;
            Frame = frame;
            this.commandCall = commandCall;
            Args = args;
            FilePath = filePath;
            StudioLineNumber = studioLineNumber;
        }

        public string LineText => Args.Length == 0 ? Attribute.Name : $"{Attribute.Name}, {string.Join(", ", Args)}";

        public void Invoke() => commandCall?.Invoke();
    }
}