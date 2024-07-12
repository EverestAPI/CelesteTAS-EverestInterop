using System;
using System.IO;

namespace StudioCommunication;

public record struct StudioState() {
    public int CurrentLine = -1;
    public string CurrentLineSuffix = string.Empty;
    public int CurrentFrameInTas = -1;
    public int TotalFrames = 0;
    public int SaveStateLine = -1;
    public States tasStates = States.None;
    public string GameInfo = string.Empty;
    public string LevelName = string.Empty;
    public string ChapterTime = string.Empty;
    
    public void Serialize(BinaryWriter writer) {
        writer.Write(CurrentLine);
        writer.Write(CurrentLineSuffix);
        writer.Write(CurrentFrameInTas);
        writer.Write(TotalFrames);
        writer.Write(SaveStateLine);
        writer.Write((int)tasStates);
        writer.Write(GameInfo);
        writer.Write(LevelName);
        writer.Write(ChapterTime);
    }
    public static StudioState Deserialize(BinaryReader reader) => new() {
        CurrentLine = reader.ReadInt32(),
        CurrentLineSuffix = reader.ReadString(),
        CurrentFrameInTas = reader.ReadInt32(),
        TotalFrames = reader.ReadInt32(),
        SaveStateLine = reader.ReadInt32(),
        tasStates = (States)reader.ReadInt32(),
        GameInfo = reader.ReadString(),
        LevelName = reader.ReadString(),
        ChapterTime = reader.ReadString()
    };
    
#if !REWRITE
    // ReSharper disable once UnusedMember.Global
    public byte[] ToByteArray() {
        return BinaryFormatterHelper.ToByteArray(new object[] {
            CurrentLine,
            CurrentLineSuffix,
            CurrentFrameInTas,
            TotalFrames,
            SaveStateLine,
            (int)tasStates,
            GameInfo,
            LevelName,
            ChapterTime,
        });
    }
    
    // ReSharper disable once UnusedMember.Global
    public static StudioState FromByteArray(byte[] data) {
        object[] values = BinaryFormatterHelper.FromByteArray<object[]>(data);
        return new StudioState {
            CurrentLine = (int) values[0],
            CurrentLineSuffix = values[1] as string,
            CurrentFrameInTas = (int) values[2],
            TotalFrames = (int) values[3],
            SaveStateLine = (int) values[4],
            tasStates = (States) values[5],
            GameInfo = values[6] as string,
            LevelName = values[7] as string,
            ChapterTime = values[8] as string
        };
    }
#endif
}