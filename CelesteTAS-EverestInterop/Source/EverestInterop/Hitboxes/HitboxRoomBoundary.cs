using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxRoomBoundary {
    [Load]
    private static void Load() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);
        if (TasSettings.ShowHitboxes && TasSettings.CenterCamera && self.Scene is Level level &&
            level.GetPlayer() is { } player) {
            Rectangle bounds = level.Bounds;
            float topExtra = (float) (Math.Floor(player.CenterY - player.Top) + 1);
            Draw.HollowRect(bounds.X - 1, bounds.Y - topExtra, bounds.Width + 2, bounds.Height + topExtra + 1, HitboxColor.RespawnTriggerColor);
        }
    }
}