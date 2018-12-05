using Celeste.Mod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TAS.EverestInterop {
    public class CelesteTASModuleSettings : EverestModuleSettings {

        public bool Enabled { get; set; } = true;
        public bool DisableAchievements { get; set; } = false;
        public bool DisableStats { get; set; } = false;
        public bool DisableTerminal { get; set; } = false;

    }
}
