using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial struct StudioState() {
    public int CurrentLine = -1;
    public string CurrentLineSuffix = string.Empty;
    public int CurrentFrameInTas = -1;
    public int CurrentFrameInInput = -1;
    public int[] SaveStateLines = [];

    /// Whether a TAS is actively playing (i.e. not paused)
    public bool PlaybackRunning;

    /// Indicates the CelesteTAS detected changes to the file, but hasn't updated the following variables yet
    public bool FileNeedsReload;
    public int TotalFrames = 0;

    public string GameInfo = string.Empty;
    public string LevelName = string.Empty;
    public string ChapterTime = string.Empty;

    public (float X, float Y) PlayerPosition;
    public (float X, float Y) PlayerPositionRemainder;
    public (float X, float Y) PlayerSpeed;
    public bool ShowSubpixelIndicator = false;
}
