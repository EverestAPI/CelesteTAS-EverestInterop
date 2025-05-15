using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial record struct GameState {
    public enum Direction : byte {
        Up,
        Down,
        Left,
        Right,
    }
    public enum WindPattern : byte {
        None,
        Left,
        Right,
        LeftStrong,
        RightStrong,
        LeftOnOff,
        RightOnOff,
        LeftOnOffFast,
        RightOnOffFast,
        Alternating,
        LeftGemsOnly,
        RightCrazy,
        Down,
        Up,
        Space,
    }

    // We can't just use tuples since they lose their names when serializing to JSON
    public struct Vec2(float x, float y) { public readonly float X = x, Y = y; }
    public struct RectI(int x, int y, int w, int h) { public readonly int X = x, Y = y, W = w, H = h; }
    public struct RectF(float x, float y, float w, float h) { public readonly float X = x, Y = y, W = w, H = h; }

    public struct Spike(RectF bounds, Direction direction) {
        public readonly RectF Bounds = bounds;
        public readonly Direction Direction = direction;
    }
    public struct WindTrigger(RectF bounds, WindPattern pattern) {
        public readonly RectF Bounds = bounds;
        public readonly WindPattern Pattern = pattern;
    }
    public struct JumpThru(RectF bounds, Direction direction, bool pullsPlayer) {
        public readonly RectF Bounds = bounds;
        public readonly Direction Direction = direction;
        public readonly bool PullsPlayer = pullsPlayer;
    }

    public record struct PlayerState {
        public Vec2 Position;
        public Vec2 PositionRemainder;

        public Vec2 Speed;

        public float starFlySpeedLerp;

        public bool OnGround;
        public bool IsHolding;
        public float JumpTimer;
        public bool AutoJump;
        public float MaxFall;
    }

    public record struct LevelState {
        public RectI Bounds;

        public Vec2 WindDirection;
    }

    public float DeltaTime;

    public PlayerState Player;
    public LevelState Level;

    public string ChapterTime;
    public string RoomName;
    public string PlayerStateName;

    public string SolidsData;
    public RectF[] StaticSolids;

    // Entities
    public Vec2[] Spinners;
    public RectF[] Lightning;
    public Spike[] Spikes;
    public WindTrigger[] WindTriggers;
    public JumpThru[] JumpThrus;
}
