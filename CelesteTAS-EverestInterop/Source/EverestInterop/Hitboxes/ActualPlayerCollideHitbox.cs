using System;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static partial class ActualEntityCollideHitbox {
    private static readonly Color PlayerHitboxColor = Color.Red.Invert();
    private static readonly Color PlayerHurtboxColor = Color.Lime.Invert();

    [Load]
    private static void LoadPlayerHook() {
        On.Celeste.Player.DebugRender += PlayerOnDebugRender;
        typeof(Player).GetMethod("orig_Update").IlHook(ModPlayerOrigUpdate);
    }

    [Unload]
    private static void UnloadPlayerHook() {
        On.Celeste.Player.DebugRender -= PlayerOnDebugRender;
    }

    private static void ModPlayerOrigUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt &&
                       ins.Operand.ToString().Contains("Monocle.Tracker::GetComponents<Celeste.PlayerCollider>()"))) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Player>>(SavePlayerPosition);
        }
    }

    private static void SavePlayerPosition(Player player) {
        if (Manager.FastForwarding || !TasSettings.ShowHitboxes || TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off ||
            playerUpdated) {
            return;
        }

        player.SaveActualCollidePosition();
    }

    private static void PlayerOnDebugRender(On.Celeste.Player.orig_DebugRender orig, Player player, Camera camera) {
        if (Manager.FastForwarding
            || !TasSettings.ShowHitboxes
            || TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Off
            || player.Scene is Level {Transitioning: true}
            || player.LoadActualCollidePosition() is not { } actualCollidePosition
            || actualCollidePosition == player.Position
           ) {
            orig(player, camera);
            return;
        }

        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Override) {
            DrawAssistedHitbox(player, actualCollidePosition);
        }

        orig(player, camera);
        if (TasSettings.ShowActualCollideHitboxes == ActualCollideHitboxType.Append) {
            DrawAssistedHitbox(player, actualCollidePosition);
        }
    }

    private static void DrawAssistedHitbox(Player player, Vector2 hitboxPosition) {
        Collider origCollider = player.Collider;
        Collider hurtbox = player.hurtbox;
        Vector2 origPosition = player.Position;

        player.Position = hitboxPosition;

        Draw.HollowRect(origCollider, PlayerHitboxColor);
        player.Collider = hurtbox;
        Draw.HollowRect(hurtbox, PlayerHurtboxColor);

        player.Collider = origCollider;
        player.Position = origPosition;
    }
}
