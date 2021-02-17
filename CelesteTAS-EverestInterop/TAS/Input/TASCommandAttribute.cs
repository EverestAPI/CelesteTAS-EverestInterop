using System;

namespace TAS.Input {
    [AttributeUsage(AttributeTargets.Method)]
    public class TasCommandAttribute : Attribute {
        public string[] Args;
        public bool ExecuteAtStart;
        public bool IllegalInMaingame;
    }
}