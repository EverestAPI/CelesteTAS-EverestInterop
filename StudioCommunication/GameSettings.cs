using MemoryPack;
using System;

namespace StudioCommunication;

/// In-Studio configurable game settings
[MemoryPackable]
public partial class GameSettings {
    public const int MinDecimals = 0;
    public const int MaxDecimals = 12;

    public bool Hitboxes = false;
    public bool TriggerHitboxes = true;
    public bool UnloadedRoomsHitboxes = true;
    public bool CameraHitboxes = true;
    public bool SimplifiedHitboxes = true;
    public ActualCollideHitboxType ActualCollideHitboxes = ActualCollideHitboxType.Off;

    public bool SimplifiedGraphics = false;
    public bool Gameplay = true;

    public bool CenterCamera = false;
    public bool CenterCameraHorizontallyOnly = false;
    public bool EnableExCameraDynamicsForCenterCamera = true;

    public bool InfoHud = false;
    public bool InfoGame = true;
    public bool InfoTasInput = true;
    public bool InfoSubpixelIndicator = true;
    public HudOptions InfoCustom = HudOptions.Off;
    public WatchEntityType InfoWatchEntityHudType = WatchEntityType.Position;
    public WatchEntityType InfoWatchEntityStudioType = WatchEntityType.All;

    public int PositionDecimals = 2;
    public int SpeedDecimals = 2;
    public int VelocityDecimals = 2;
    public int AngleDecimals = 5;
    public int CustomInfoDecimals = 2;
    public int SubpixelIndicatorDecimals = 2;

    public SpeedUnit SpeedUnit = SpeedUnit.PixelPerSecond;
    public SpeedUnit VelocityUnit = SpeedUnit.PixelPerSecond;

    public int FastForwardSpeed = 10;
    public float SlowForwardSpeed = 0.1f;
}

public enum ActualCollideHitboxType {
    Off,
    Override,
    Append
}

[Flags]
public enum HudOptions {
    Off = 0,
    HudOnly = 1,
    StudioOnly = 2,
    Both = HudOnly | StudioOnly
}

public enum WatchEntityType {
    None,
    Position,
    DeclaredOnly,
    All
}

public enum SpeedUnit {
    PixelPerSecond,
    PixelPerFrame
}

/// Condition for when to enable certain features, related to Studio connection
public enum StudioEnableCondition {
    Never,
    Always,
    WhileStudioConnected,
    ForCurrentSession,
    AfterCasualPlaythrough,
}
/// Condition for when to enable certain features, related to gameplay state
public enum GameplayEnableCondition {
    Never,
    Always,
    DuringTAS,
}
