using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod;
using MonoMod.Detour;
using MonoMod.InlineRT;
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

        public readonly static MethodInfo m_Player_WallJumpCheck = t_Player.GetMethod("WallJumpCheck", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        [CelesteTASProxy("System.Boolean Celeste.Player::WallJumpCheck(System.Int32)")]
        public static bool Player_WallJumpCheck(Player self, int dir)
            => (bool) m_Player_WallJumpCheck.GetDelegate().Invoke(self, dir);


        public readonly static Type t_MInput = typeof(MInput);

        public readonly static MethodInfo m_UpdateVirualInputs = t_MInput.GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        [CelesteTASProxy("System.Void Monocle.MInput::UpdateVirtualInputs()")]
        public static void MInput_UpdateVirtualInputs()
            => m_UpdateVirualInputs.GetDelegate().Invoke(null);

    }
    public class CelesteTASProxyAttribute : Attribute {
        public string FindableID;
        public CelesteTASProxyAttribute(string findableID) {
            FindableID = findableID;
        }
    }
}
