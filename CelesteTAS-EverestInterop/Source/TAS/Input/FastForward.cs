using StudioCommunication;

namespace TAS.Input;

/// A breakpoint to which the TAS will fast-forward at a high speed
public record FastForward(int Frame, int StudioLine, bool ForceStop = false, bool SaveState = false, float Speed = FastForward.DefaultSpeed) {
    private const float DefaultSpeed = 400.0f;

    public FastForward(int frame, int studioLine, FastForwardLine fastForwardLine)
        : this(frame, studioLine, fastForwardLine.ForceStop, fastForwardLine.SaveState, fastForwardLine.PlaybackSpeed ?? DefaultSpeed) { }

    public override string ToString() {
        return $"***{(ForceStop ? "!" : "")}{(SaveState ? "S" : "")}{Speed}";
    }
}
