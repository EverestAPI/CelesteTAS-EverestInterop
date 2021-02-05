using Celeste;
using MonoMod.Utils;

namespace TAS.EverestInterop {
class DisableAchievements {
    public static DisableAchievements instance;

    public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public void Load() {
        // Optional: Disable achievements, stats and terminal.

        // Before hooking Stats.Increment, check if the method is empty.
        // Hooking empty methods causes issues on Linux versions notably, and Stats.Increment is empty in non-Steam versions of the game.
        using (DynamicMethodDefinition statsDMD = new DynamicMethodDefinition(typeof(Stats).GetMethod("Increment"))) {
            int instructionCount = statsDMD.Definition.Body.Instructions.Count;
            if (instructionCount > 1) {
                // the method has more than a lonely "ret", so hook it.
                On.Celeste.Stats.Increment += Stats_Increment;
            }
        }

        // Before hooking Achievements.Register, check the size of the method.
        // If it is 4 instructions long, hooking it is unnecessary and even causes issues.
        using (DynamicMethodDefinition statsDMD = new DynamicMethodDefinition(typeof(Achievements).GetMethod("Register"))) {
            int instructionCount = statsDMD.Definition.Body.Instructions.Count;
            if (instructionCount > 4) {
                On.Celeste.Achievements.Register += Achievements_Register;
            }
        }
    }

    public void Unload() {
        On.Celeste.Achievements.Register -= Achievements_Register;
        On.Celeste.Stats.Increment -= Stats_Increment;
    }

    public static void Achievements_Register(On.Celeste.Achievements.orig_Register orig, Achievement achievement) {
        if (Settings.DisableAchievements) {
            return;
        }

        orig(achievement);
    }

    public static void Stats_Increment(On.Celeste.Stats.orig_Increment orig, Stat stat, int increment) {
        if (Settings.DisableAchievements) {
            return;
        }

        orig(stat, increment);
    }
}
}