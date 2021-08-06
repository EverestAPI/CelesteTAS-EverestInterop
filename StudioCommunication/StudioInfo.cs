namespace StudioCommunication {
    public record StudioInfo {
        public readonly int CurrentFrame;
        public readonly int CurrentLine;
        public readonly string CurrentLineText;
        public readonly int SaveStateLine;
        public readonly State TasState;
        public readonly int TotalFrames;
        public readonly string GameInfo;

        public StudioInfo(
            int currentLine, string currentLineText, int currentFrame, int totalFrames, int saveStateLine, State tasState,
            string gameInfo) {
            CurrentLine = currentLine;
            CurrentLineText = currentLineText;
            CurrentFrame = currentFrame;
            TotalFrames = totalFrames;
            SaveStateLine = saveStateLine;
            TasState = tasState;
            GameInfo = gameInfo;
        }

        public static StudioInfo FromArray(string[] values) {
            return new StudioInfo(
                int.Parse(values[0]),
                values[1],
                int.Parse(values[2]),
                int.Parse(values[3]),
                int.Parse(values[4]),
                (State) int.Parse(values[5]),
                values[6]
            );
        }

        public static string[] ToArray(StudioInfo studioInfo) {
            return new[] {
                studioInfo.CurrentLine.ToString(),
                studioInfo.CurrentLineText,
                studioInfo.CurrentFrame.ToString(),
                studioInfo.TotalFrames.ToString(),
                studioInfo.SaveStateLine.ToString(),
                ((int)studioInfo.TasState).ToString(),
                studioInfo.GameInfo,
            };
        }
    }
}