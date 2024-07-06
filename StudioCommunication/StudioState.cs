using System.IO;

namespace StudioCommunication;

public record struct StudioState() {
    public required int CurrentLine = -1;
    public required string CurrentLineSuffix = string.Empty;
    public required int CurrentFrameInTas = -1;
    public required int TotalFrames = 0;
    public required int SaveStateLine = -1;
    public required States tasStates = States.None;
    public required string GameInfo = string.Empty;
    public required string LevelName = string.Empty;
    public required string ChapterTime = string.Empty;
    
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
}