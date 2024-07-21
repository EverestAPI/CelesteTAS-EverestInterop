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
    
    public record struct PlayerState {
        public (float X, float Y) Position;
        public (float X, float Y) PositionRemainder;

        public (float X, float Y) Speed;
        
        public float starFlySpeedLerp;
    }
    
    public record struct LevelState {
        public (int X, int Y, int W, int H) Bounds;
        
        public (float X, float Y) WindDirection;
    }
    
    public PlayerState Player;
    public LevelState Level;
    
    public string SolidsData;
    public (float X, float Y, float W, float H)[] StaticSolids;
    
    // Entities
    public (float X, float Y)[] Spinners;
    public (float X, float Y, float W, float H)[] Lightning;
    public (float X, float Y, float W, float H, Direction Direction)[] Spikes;
    
    public (float X, float Y, float W, float H, WindPattern Pattern)[] WindTriggers;
    
    public (float X, float Y, float W, float H, Direction Direction, bool PullsPlayer)[] JumpThrus;
}