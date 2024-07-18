using System;
using System.IO;
using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial struct StudioState() {
    public int CurrentLine = -1;
    public string CurrentLineSuffix = string.Empty;
    public int CurrentFrameInTas = -1;
    public int TotalFrames = 0;
    public int SaveStateLine = -1;
    public States tasStates = States.None;
    public string GameInfo = string.Empty;
    public string LevelName = string.Empty;
    public string ChapterTime = string.Empty;
#if REWRITE
    public bool ShowSubpixelIndicator = false;
    public (float X, float Y) SubpixelRemainder;
#else
    public bool ShowSubpixelIndicator => false;
    public (float X, float Y) SubpixelRemainder => (0.0f, 0.0f);
#endif
    
    public void Serialize(BinaryWriter writer) => MemoryPackSerializer.SerializeAsync(writer.BaseStream, this).AsTask().Wait();
    public static StudioState Deserialize(BinaryReader reader) => MemoryPackSerializer.DeserializeAsync<StudioState>(reader.BaseStream).AsTask().Result;
    
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