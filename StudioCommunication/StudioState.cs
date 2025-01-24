﻿using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial struct StudioState() {
    public int CurrentLine = -1;
    public string CurrentLineSuffix = string.Empty;
    public int CurrentFrameInTas = -1;
    public int CurrentFrameInInput = -1;
    public int TotalFrames = 0;
    public int SaveStateLine = -1;

    public States tasStates = States.None;
    public string GameInfo = string.Empty;
    public string LevelName = string.Empty;
    public string ChapterTime = string.Empty;

    public (float X, float Y) PlayerPosition;
    public (float X, float Y) PlayerPositionRemainder;
    public (float X, float Y) PlayerSpeed;
    public bool ShowSubpixelIndicator = false;
}
