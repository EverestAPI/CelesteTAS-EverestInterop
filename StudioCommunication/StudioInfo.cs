namespace StudioCommunication {
    public record StudioInfo {
        public readonly int CurrentLine;
        public readonly string CurrentLineSuffix;
        public readonly int CurrentFrameInTas;
        public readonly int TotalFrames;
        public readonly int SaveStateLine;
        public readonly int tasStates;
        public readonly string GameInfo;
        public readonly string LevelName;
        public readonly string ChapterTime;
        public readonly string ModVersion;

        // ReSharper disable once MemberCanBePrivate.Global
        public StudioInfo(
            int currentLine, string currentLineSuffix, int currentFrameInTas, int totalFrames, int saveStateLine, int tasStates,
            string gameInfo, string levelName, string chapterTime, string modVersion) {
            CurrentLine = currentLine;
            CurrentLineSuffix = currentLineSuffix;
            CurrentFrameInTas = currentFrameInTas;
            TotalFrames = totalFrames;
            SaveStateLine = saveStateLine;
            this.tasStates = tasStates;
            GameInfo = gameInfo;
            LevelName = levelName;
            ChapterTime = chapterTime;
            ModVersion = modVersion;
        }

        // ReSharper disable once UnusedMember.Global
        public byte[] ToByteArray() {
            return BinaryFormatterHelper.ToByteArray(new object[] {
                CurrentLine,
                CurrentLineSuffix,
                CurrentFrameInTas,
                TotalFrames,
                SaveStateLine,
                tasStates,
                GameInfo,
                LevelName,
                ChapterTime,
                ModVersion
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
                (int) values[5],
                values[6] as string,
                values[7] as string,
                values[8] as string,
                values[9] as string
            );
        }
    }
}