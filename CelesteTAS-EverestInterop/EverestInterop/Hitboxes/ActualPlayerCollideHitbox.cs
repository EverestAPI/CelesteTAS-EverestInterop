using System;
using System.Collections;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop.Hitboxes {
public static class ActualPlayerCollideHitbox {
    private static readonly FieldInfo PlayerHurtbox = typeof(Player).GetFieldInfo("hurtbox");
    private static readonly Color hitboxColor = Color.Red.Invert();
    private static readonly Color hurtboxColor = Color.Lime.Invert();
    private static ILHook IlHookPlayerOrigUpdate;
    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public static void Load() {
        On.Monocle.Hitbox.Render += HitboxOnRender;
        On.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
        IlHookPlayerOrigUpdate = new ILHook(typeof(Player).GetMethod("orig_Update"), ModPlayerOrigUpdate);
    }

    public static void Unload() {
        On.Monocle.Hitbox.Render -= HitboxOnRender;
        On.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
        IlHookPlayerOrigUpdate?.Dispose();
    }

    private static void ModPlayerOrigUpdate(ILContext il) {
        ILCursor ilCursor = new ILCursor(il);
        if (ilCursor.TryGotoNext(MoveType.After,
            ins => ins.OpCode == OpCodes.Callvirt &&
                   ins.Operand.ToString().Contains("Monocle.Tracker::GetComponents<Celeste.PlayerCollider>()"))) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Player>>(player => {
                if (!Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.OFF || Manager.FrameLoops > 1) {
                    return;
                }

                player.SaveActualCollidePosition();
            });
        }
    }

    private static IEnumerator LevelOnTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self, LevelData next,
        Vector2 direction) {
        IEnumerator enumerator = orig(self, next, direction);
        while (enumerator.MoveNext()) {
            yield return enumerator.Current;
        }

        if (self.GetPlayer() is Player player) {
            player.ClearActualCollidePosition();
        }
    }

    private static void HitboxOnRender(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        if (!(self.Entity is Player player) || !Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.OFF
            || Manager.FrameLoops > 1
            || player.LoadActualCollidePosition() == null
            || player.LoadActualCollidePosition().Value == player.Position
            || player.Scene is Level level && level.Transitioning
        ) {
            orig(self, camera, color);
            return;
        }

        DrawAssistedHitbox(orig, self, camera, player, player.LoadActualCollidePosition().Value);

        orig(self, camera, color);
    }

    private static void DrawAssistedHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Player player,
        Vector2 hitboxPosition) {
        Collider origCollider = player.Collider;
        Collider hurtbox = (Collider) PlayerHurtbox.GetValue(player);
        Vector2 origPosition = player.Position;

        player.Position = hitboxPosition;

        orig(self, camera, hitboxColor);
        player.Collider = hurtbox;
        Draw.HollowRect(hurtbox, hurtboxColor);
        player.Collider = origCollider;

        player.Position = origPosition;
    }
}
}