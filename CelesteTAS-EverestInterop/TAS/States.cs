using System;

namespace TAS;

[Flags]
public enum States {
    None = 0,
    Enable = 1,
    Record = 2,
    FrameStep = 4,
    Disable = 8
}