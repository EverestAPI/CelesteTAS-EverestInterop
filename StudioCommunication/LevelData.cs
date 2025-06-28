using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial record struct LevelInfo {
    /// URL to the GameBanana page of the mod
    public string ModUrl;

    /// Amount of frames which the intro animation takes, if it could be figured out
    public int? IntroTime;
    /// Name of the starting room without the lvl_ prefix
    public string StartingRoom;
}
