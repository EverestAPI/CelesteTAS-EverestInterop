using System;

namespace TAS.Input {
[AttributeUsage(AttributeTargets.Method)]
public class TASCommandAttribute : Attribute {
    public string[] args;
    public bool executeAtStart;
    public bool illegalInMaingame;
}
}