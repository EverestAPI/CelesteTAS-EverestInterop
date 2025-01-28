using MemoryPack;

namespace StudioCommunication;

[MemoryPackable]
public partial record struct LevelInfo {
    public int? WakeupTime;
    public string ModUrl;
}
