using MonoMod.Cil;
using Celeste;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;
internal static class BetterInvincible {
    // merged from TAS Helper

    // make you invincible while still make tas sync
    // it will not persist after SL, and that's what we want!

    // if it (before savepoint) gets deleted, then tas file changes, so it should be detected and disable run will be invoked, and savestate will be cleared
    // if it (after savepoint) gets deleted, .... yeah it just gets deleted, when restart from savestate, Invincible = false will be loaded (as it's saved as such)

    // note that if you use RESTART hotkey ("=" by default), then LoadState will be invoked (if it's saved), but DisableRun won't!!

    public static bool Invincible = false;

    [Initialize]

    private static void Initialize() {
        typeof(Player).GetMethod("orig_Die").IlHook(il => {
            ILCursor cursor = new ILCursor(il);
            if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchLdfld<Assists>("Invincible"))) {
                cursor.EmitDelegate(ModifyInvincible);
            }
        });
    }


    [DisableRun]
    private static void OnDisableRun() {
        Invincible = false;
    }

    private static bool ModifyInvincible(bool origValue) {
        // Manager.Running may be redundant..
        return origValue || (Invincible && Manager.Running && TasSettings.BetterInvincible); // safe guard, in case that disable run thing doesn't work somehow
    }

    public static bool Handle(Assists assist, bool value) {
        if (!Manager.Running || !TasSettings.BetterInvincible) {
            return false;
        }
        bool beforeInvincible = assist.Invincible;
        if (beforeInvincible == value) {
            return true;
        }
        if (!beforeInvincible) {
            assist.Invincible = false;
            SaveData.Instance.Assists = assist;
            // Assists is a struct, so it needs to be re-assign
        }
        Invincible = !beforeInvincible;
        // if originally invincible = true, but set to false, then betterInv = false
        // if originally inv = false, but set to true, then inv = false, and betterInv = true
        return true;
    }
}
