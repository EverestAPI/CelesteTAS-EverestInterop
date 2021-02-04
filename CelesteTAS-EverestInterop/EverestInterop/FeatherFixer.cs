using System.Reflection;
using Celeste.Mod;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Logger = On.Celeste.Mod.Logger;

namespace TAS.EverestInterop {
    public static class FeatherFixer {
        public static void Load() {
            IL.Monocle.VirtualJoystick.CheckBinds += VirtualJoystickOnCheckBinds;
        }

        private static void VirtualJoystickOnCheckBinds(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            LogUtil.Log(il.ToString());
            while (ilCursor.TryGotoNext(
                ins => ins.MatchLdcR4(0.05f)
                )) {
                ilCursor.Remove().Emit(OpCodes.Ldc_R4, 0f);
            }
        }

        public static void Unload() {

        }
    }
}