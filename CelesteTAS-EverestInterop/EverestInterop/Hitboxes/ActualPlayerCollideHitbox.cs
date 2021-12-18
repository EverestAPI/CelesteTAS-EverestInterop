using System;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static partial class ActualEntityCollideHitbox {
        private static readonly Func<Player, Hitbox>
            PlayerHurtbox = typeof(Player).GetFieldInfo("hurtbox").CreateDelegate_Get<Func<Player, Hitbox>>();

        private static readonly Color HitboxColor = Color.Red.Invert();
        private static readonly Color HurtboxColor = Color.Lime.Invert();
        private static ILHook ilHookPlayerOrigUpdate;

        [Load]
        private static void LoadPlayerHook() {
            On.Celeste.Player.DebugRender += PlayerOnDebugRender;
            ilHookPlayerOrigUpdate = new ILHook(typeof(Player).GetMethod("orig_Update"), ModPlayerOrigUpdate);
        }

        [Unload]
        private static void UnloadPlayerHook() {
            On.Celeste.Player.DebugRender -= PlayerOnDebugRender;
            ilHookPlayerOrigUpdate?.Dispose();
        }

        private static void ModPlayerOrigUpdate(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After,
                    ins => ins.OpCode == OpCodes.Callvirt &&
                           ins.Operand.ToString().Contains("Monocle.Tracker::GetComponents<Celeste.PlayerCollider>()"))) {
                ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Player>>(player => {
                    if (!Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off || Manager.FrameLoops > 2) {
                        return;
                    }

                    player.SaveActualCollidePosition();
                });
            }
        }

        private static void PlayerOnDebugRender(On.Celeste.Player.orig_DebugRender orig, Player player, Camera camera) {
            if (!Settings.ShowHitboxes || Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off
                                       || Manager.FrameLoops > 2
                                       || player.Scene is Level {Transitioning: true} || player.LoadActualCollidePosition() == null
                                       || player.LoadActualCollidePosition().Value == player.Position
               ) {
                orig(player, camera);
                return;
            }

            Vector2 actualCollidePosition = player.LoadActualCollidePosition().Value;
            if (Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Override) {
                DrawAssistedHitbox(orig, player, camera, actualCollidePosition);
            }

            orig(player, camera);
            if (Settings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
                DrawAssistedHitbox(orig, player, camera, actualCollidePosition);
            }
        }

        private static void DrawAssistedHitbox(On.Celeste.Player.orig_DebugRender orig, Player player, Camera camera,
            Vector2 hitboxPosition) {
            Collider origCollider = player.Collider;
            Collider hurtbox = PlayerHurtbox(player);
            Vector2 origPosition = player.Position;

            player.Position = hitboxPosition;

            Draw.HollowRect(origCollider, HitboxColor);
            player.Collider = hurtbox;
            Draw.HollowRect(hurtbox, HurtboxColor);

            player.Collider = origCollider;
            player.Position = origPosition;
        }
    }
}