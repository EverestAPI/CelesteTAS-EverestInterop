using System;

namespace StudioCommunication;

// ReSharper disable once StructCanBeMadeReadOnly
public record struct StudioInfo {
    public readonly int CurrentLine;
    public readonly string CurrentLineSuffix;
    public readonly int CurrentFrameInTas;
    public readonly int TotalFrames;
    public readonly int SaveStateLine;
    public readonly int tasStates;
    public readonly string GameInfo;
    public readonly string LevelName;
    public readonly string ChapterTime;

    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once ConvertToPrimaryConstructor
    public StudioInfo(
        int currentLine, string currentLineSuffix, int currentFrameInTas, int totalFrames, int saveStateLine, int tasStates,
        string gameInfo, string levelName, string chapterTime) {
        CurrentLine = currentLine;
        CurrentLineSuffix = currentLineSuffix;
        CurrentFrameInTas = currentFrameInTas;
        TotalFrames = totalFrames;
        SaveStateLine = saveStateLine;
        this.tasStates = tasStates;
        GameInfo = gameInfo;
        LevelName = levelName;
        ChapterTime = chapterTime;
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
        });
    }

    // ReSharper disable once UnusedMember.Global
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
            values[8] as string
        );
    }
    
    public readonly bool Equals(StudioInfo other) => 
        CurrentLine == other.CurrentLine && 
        CurrentLineSuffix == other.CurrentLineSuffix && 
        CurrentFrameInTas == other.CurrentFrameInTas && 
        TotalFrames == other.TotalFrames && 
        SaveStateLine == other.SaveStateLine && 
        tasStates == other.tasStates && 
        GameInfo == other.GameInfo && 
        LevelName == other.LevelName && 
        ChapterTime == other.ChapterTime;
    
    public override readonly int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(CurrentLine);
        hashCode.Add(CurrentLineSuffix);
        hashCode.Add(CurrentFrameInTas);
        hashCode.Add(TotalFrames);
        hashCode.Add(SaveStateLine);
        hashCode.Add(tasStates);
        hashCode.Add(GameInfo);
        hashCode.Add(LevelName);
        hashCode.Add(ChapterTime);
        return hashCode.ToHashCode();
    }
}