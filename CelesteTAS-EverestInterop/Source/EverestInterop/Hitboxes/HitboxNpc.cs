﻿using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxNpc {
    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += EntityOnDebugRender;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= EntityOnDebugRender;
    }

    private static void EntityOnDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity entity, Camera camera) {
        bool drawOriginal = true;

        if (TasSettings.ShowHitboxes && TasSettings.ShowTriggerHitboxes && Engine.Scene is Level level) {
            if (entity.GetEntityData()?.Level?.Name != level.Session.Level) {
                orig(entity, camera);
                return;
            }

            Rectangle levelBounds = level.Bounds;
            int left = levelBounds.Left;
            int top = levelBounds.Top;
            int bottom = levelBounds.Bottom;
            Color color = HitboxColor.TriggerColor;
            string levelName = level.Session.Level;
            Player player = level.GetPlayer();

            if (entity is NPC00_Granny) {
                float x = left + 100;
                Draw.HollowRect(x - 1, entity.Y - 3, entity.X + 16 - left - 102, 3, color);
            } else if (entity is NPC03_Oshiro_Hallway1 or NPC03_Oshiro_Hallway2 or NPC06_Granny) {
                float x = entity.X - 55;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC03_Oshiro_Cluttter {sectionsComplete: 3}) {
                float x = entity.X + 28;
                Draw.Line(x, top, x, entity.Y, color);
            } else if (entity is NPC04_Granny) {
                float x = entity.X - 35;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC05_Theo_Mirror) {
                float x = entity.X - 59;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC06_Badeline_Crying) {
                float x = entity.X - 27;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC09_Granny_Inside or NPC09_Granny_Outside) {
                float x = entity.X - 43;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC03_Theo_Escaping && levelName == "11-b") {
                float x = 5477;
                Draw.Line(x, top, x, bottom, color);
            } else if (level.Session.Area.ID == 4 && entity is Gondola) {
                float x = entity.Left - 11;
                Draw.Line(x, top, x, bottom, color);
            } else if (entity is NPC05_Badeline badelineNpc) {
                if (levelName == "c-00") {
                    float x = entity.X - 59;
                    Draw.Line(x, top, x, bottom, color);
                } else if (levelName == "c-01" && badelineNpc.shadow is { } badelineDummy) {
                    Draw.Circle(badelineDummy.Position, 70, color, 4);
                    if (player is { } && Vector2.Distance(player.Position, badelineDummy.Position) <= 150) {
                        Draw.Line(player.Position, badelineDummy.Position, Color.Aqua);
                    }
                }
            } else if (entity is BirdNPC birdNpc && birdNpc.mode == BirdNPC.Modes.DashingTutorial) {
                Vector2 offset = ((player is { }) ? player.Collider : player.normalHitbox).BottomRight;
                Vector2 position = birdNpc.StartPosition;
                float x1 = position.X - 91f + offset.X;
                float x2 = level.Bounds.Right;
                float y1 = position.Y - 19f + offset.Y;
                float y2 = position.Y - 11f + offset.Y;
                Draw.HollowRect(x1 - 1f, y1 - 1f, x2 - x1, y2 - y1, Color.Aqua);
                drawOriginal = false;
            }
        }

        if (drawOriginal) {
            orig(entity, camera);
        }
    }
}