using Celeste.Mod;
using MonoMod.Detour;
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

        [SettingInGame(false)]
        public bool Enabled { get; set; } = true;

    }
}
