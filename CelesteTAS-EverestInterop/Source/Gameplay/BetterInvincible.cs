using MonoMod.Cil;
using Celeste;
using Celeste.Mod;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Custom invisibility setting which prevents dying, but doesn't alter any other gameplay (like bouncing of the bottom of the screen)
/// It is only active while a TAS is running and not persistant between runs
internal static class BetterInvincible {
    // Manually store state, so Assists.Invincible isn't altered
    public static bool Invincible = false;

    [Initialize]
    private static void Initialize() {
        typeof(Player).GetMethodInfo("orig_Die")!.IlHook(il => {
            var cursor = new ILCursor(il);
            if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchLdfld<Assists>(nameof(Assists.Invincible)))) {
                cursor.EmitDelegate(ModifyInvincible);
            } else {
                $"Failed to apply {nameof(BetterInvincible)} hook!".Log(LogLevel.Error);
            }
        });
        return;

        static bool ModifyInvincible(bool origValue) {
            return origValue || (Manager.Running && Invincible && TasSettings.BetterInvincible);
        }
    }

    [EnableRun]
    private static void EnableRun() {
        Invincible = false; // Reset back to default
    }
}
