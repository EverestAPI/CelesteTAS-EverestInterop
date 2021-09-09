namespace StudioCommunication {
    public record StudioInfo {
        public readonly string ChapterTime;
        public readonly int CurrentFrameInInput;
        public readonly int CurrentFrameInTas;
        public readonly int CurrentLine;
        public readonly string ExactGameInfo;
        public readonly string GameInfo;
        public readonly string LevelName;
        public readonly string ModVersion;
        public readonly int SaveStateLine;
        public readonly State TasState;
        public readonly int TotalFrames;

        public StudioInfo(
            int currentLine, int currentFrameInInput, int currentFrameInTas, int totalFrames, int saveStateLine, State tasState,
            string gameInfo, string exactGameInfo, string levelName, string chapterTime, string modVersion) {
            CurrentLine = currentLine;
            CurrentFrameInInput = currentFrameInInput;
            CurrentFrameInTas = currentFrameInTas;
            TotalFrames = totalFrames;
            SaveStateLine = saveStateLine;
            TasState = tasState;
            GameInfo = gameInfo;
            ExactGameInfo = exactGameInfo;
            LevelName = levelName;
            ChapterTime = chapterTime;
            ModVersion = modVersion;
        }

        public byte[] ToByteArray() {
            return BinaryFormatterHelper.ToByteArray(new object[] {
                CurrentLine,
                CurrentFrameInInput,
                CurrentFrameInTas,
                TotalFrames,
                SaveStateLine,
                (int) TasState,
                GameInfo,
                ExactGameInfo,
                LevelName,
                ChapterTime,
                ModVersion
            });
        }

        public static StudioInfo FromByteArray(byte[] data) {
            object[] values = BinaryFormatterHelper.FromByteArray<object[]>(data);
            return new StudioInfo(
                (int) values[0],
                (int) values[1],
                (int) values[2],
                (int) values[3],
                (int) values[4],
                (State) values[5],
                values[6] as string,
                values[7] as string,
                values[8] as string,
                values[9] as string,
                values[10] as string
            );
        }
    }
}