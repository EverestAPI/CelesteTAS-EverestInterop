using Celeste;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class DisableAchievements {
    [Load]
    private static void Load() {
        // Optional: Disable achievements, stats and terminal.

        // Before hooking Stats.Increment, check if the method is empty.
        // Hooking empty methods causes issues on Linux versions notably, and Stats.Increment is empty in non-Steam versions of the game.
        using (DynamicMethodDefinition statsDmd = new(typeof(Stats).GetMethodInfo("Increment")!)) {
            int instructionCount = statsDmd.Definition.Body.Instructions.Count;
            if (instructionCount > 1) {
                // the method has more than a lonely "ret", so hook it.
                On.Celeste.Stats.Increment += Stats_Increment;
            }
        }

        // Before hooking Achievements.Register, check the size of the method.
        // If it is 4 instructions long, hooking it is unnecessary and even causes issues.
        using (DynamicMethodDefinition statsDmd = new(typeof(Achievements).GetMethodInfo("Register")!)) {
            int instructionCount = statsDmd.Definition.Body.Instructions.Count;
            if (instructionCount > 4) {
                On.Celeste.Achievements.Register += Achievements_Register;
            }
        }
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Achievements.Register -= Achievements_Register;
        On.Celeste.Stats.Increment -= Stats_Increment;
    }

    private static void Achievements_Register(On.Celeste.Achievements.orig_Register orig, Achievement achievement) {
        if (Manager.Running) {
            return;
        }

        orig(achievement);
    }

    private static void Stats_Increment(On.Celeste.Stats.orig_Increment orig, Stat stat, int increment) {
        if (Manager.Running) {
            return;
        }

        orig(stat, increment);
    }
}
