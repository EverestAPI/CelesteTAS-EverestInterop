using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxNpc {
        private static readonly FieldInfo Npc05BadelineShadow = typeof(NPC05_Badeline).GetFieldInfo("shadow");

        public static void Load() {
            On.Monocle.Entity.DebugRender += EntityOnDebugRender;
        }

        public static void Unload() {
            On.Monocle.Entity.DebugRender -= EntityOnDebugRender;
        }

        private static void EntityOnDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity entity, Camera camera) {
            orig(entity, camera);

            if (CelesteTasModule.Settings.ShowHitboxes && CelesteTasModule.Settings.ShowTriggerHitboxes && Engine.Scene is Level level) {
                if (entity.GetEntityData()?.Level?.Name != level.Session.Level) {
                    return;
                }

                Rectangle levelBounds = level.Bounds;
                int left = levelBounds.Left;
                int top = levelBounds.Top;
                int bottom = levelBounds.Bottom;
                Color color = HitboxColor.TriggerColor;
                string levelName = level.Session.Level;

                if (entity is NPC00_Granny) {
                    float x = left + 100;
                    Draw.HollowRect(x - 1, entity.Y - 3, entity.X + 16 - left - 102, 3, color);
                } else if (entity is NPC03_Oshiro_Hallway1 or NPC03_Oshiro_Hallway2 or NPC06_Granny) {
                    float x = entity.X - 55;
                    Draw.Line(x, top, x, bottom, color);
                } else if (entity is NPC03_Oshiro_Cluttter) {
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
                } else if (entity is NPC05_Badeline) {
                    if (levelName == "c-00") {
                        float x = entity.X - 57;
                        Draw.Line(x, top, x, bottom, color);
                    } else if (levelName == "c-01" && Npc05BadelineShadow.GetValue(entity) is BadelineDummy badelineDummy) {
                        Draw.Circle(badelineDummy.Position, 69, color, 4);
                        if (level.GetPlayer() is { } player) {
                            Draw.Line(player.Position, badelineDummy.Position, Color.Aqua);
                        }
                    }
                }
            }
        }
    }
}