namespace StudioCommunication {
    public record StudioInfo {
        public readonly string ChapterTime;
        public readonly int CurrentFrame;
        public readonly int CurrentLine;
        public readonly string CurrentLineText;
        public readonly string GameInfo;
        public readonly string LevelName;
        public readonly int SaveStateLine;
        public readonly State TasState;
        public readonly int TotalFrames;

        public StudioInfo(
            int currentLine, string currentLineText, int currentFrame, int totalFrames, int saveStateLine, State tasState,
            string gameInfo, string levelName, string chapterTime) {
            CurrentLine = currentLine;
            CurrentLineText = currentLineText;
            CurrentFrame = currentFrame;
            TotalFrames = totalFrames;
            SaveStateLine = saveStateLine;
            TasState = tasState;
            GameInfo = gameInfo;
            LevelName = levelName;
            ChapterTime = chapterTime;
        }

        public byte[] ToByteArray() {
            return BinaryFormatterHelper.ToByteArray(new object[] {
                CurrentLine,
                CurrentLineText,
                CurrentFrame,
                TotalFrames,
                SaveStateLine,
                (int) TasState,
                GameInfo,
                LevelName,
                ChapterTime,
            });
        }

        public static StudioInfo FromByteArray(byte[] data) {
            object[] values = BinaryFormatterHelper.FromByteArray<object[]>(data);
            return new StudioInfo(
                (int) values[0],
                values[1] as string,
                (int) values[2],
                (int) values[3],
                (int) values[4],
                (State) values[5],
                values[6] as string,
                values[7] as string,
                values[8] as string
            );
        }
    }
}