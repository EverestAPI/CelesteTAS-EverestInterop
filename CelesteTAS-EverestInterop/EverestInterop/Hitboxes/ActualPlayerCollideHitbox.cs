using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop.Hitboxes {
    public static partial class ActualEntityCollideHitbox {
        private static readonly FieldInfo PlayerHurtbox = typeof(Player).GetFieldInfo("hurtbox");
        private static readonly Color HitboxColor = Color.Red.Invert();
        private static readonly Color HurtboxColor = Color.Lime.Invert();
        private static ILHook ilHookPlayerOrigUpdate;

        private static void LoadPlayerHook() {
            On.Monocle.Hitbox.Render += HitboxOnRender;
            On.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
            ilHookPlayerOrigUpdate = new ILHook(typeof(Player).GetMethod("orig_Update"), ModPlayerOrigUpdate);
        }

        private static void UnloadPlayerHook() {
            On.Monocle.Hitbox.Render -= HitboxOnRender;
            On.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
            ilHookPlayerOrigUpdate?.Dispose();
        }

        private static void ModPlayerOrigUpdate(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt &&
                       ins.Operand.ToString().Contains("Monocle.Tracker::GetComponents<Celeste.PlayerCollider>()"))) {
                ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Player>>(player => {
                    if (!Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Off || Manager.FrameLoops > 1) {
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
            if (!(self.Entity is Player player) || !Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Off
                || Manager.FrameLoops > 1
                || player.Scene is Level level && level.Transitioning
                || player.LoadActualCollidePosition() == null
                || player.LoadActualCollidePosition().Value == player.Position
            ) {
                orig(self, camera, color);
                return;
            }

            if (Settings.ShowActualCollideHitboxes == ActualCollideHitboxTypes.Override && player.Scene?.Tracker is Tracker tracker) {
                Platform platform = null;
                if (tracker.GetEntities<Solid>().Cast<Solid>().FirstOrDefault(entity => player.IsRiding(entity)) is Solid solid) {
                    platform = solid;
                } else if (tracker.GetEntities<JumpThru>().Cast<JumpThru>().FirstOrDefault(entity => player.IsRiding(entity)) is JumpThru jumpThru) {
                    platform = jumpThru;
                }

                if (platform?.LoadActualCollidePosition() != null && platform.Position - platform.LoadActualCollidePosition() ==
                    player.Position - player.LoadActualCollidePosition()) {
                    orig(self, camera, color);
                    return;
                }
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

            orig(self, camera, HitboxColor);
            player.Collider = hurtbox;
            Draw.HollowRect(hurtbox, HurtboxColor);
            player.Collider = origCollider;

            player.Position = origPosition;
        }
    }
}