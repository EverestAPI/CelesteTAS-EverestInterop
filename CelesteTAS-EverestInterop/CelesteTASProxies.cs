using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;
using MonoMod.Detour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Logger = Celeste.Mod.Logger;

namespace TAS.EverestInterop {
    // Proxies for any non-public fields that are set to public by DevilSquirrel's modified .exe
    public static class CelesteTASProxies {

        public readonly static Type t_Player = typeof(Player);
        public readonly static FieldInfo f_Player_dashCooldownTimer = t_Player.GetField("dashCooldownTimer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        [CelesteTASProxy("System.Single Celeste.Player::dashCooldownTimer")]
        public static float Player_get_dashCooldownTimer(Player self)
            => (float) f_Player_dashCooldownTimer.GetValue(self);

    }
    public class CelesteTASProxyAttribute : Attribute {
        public string FindableID;
        public CelesteTASProxyAttribute(string findableID) {
            FindableID = findableID;
        }
    }
}
