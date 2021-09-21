using System;

namespace StudioCommunication {
    [Flags]
    public enum State {
        None = 0,
        Enable = 1,
        Record = 2,
        FrameStep = 4,
        Disable = 8
    }

    internal static class StateExtension {
        public static bool HasFlag(this State state, State flag) => (state & flag) == flag;
    }
}