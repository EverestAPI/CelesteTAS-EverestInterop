using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
    public class HitboxTweak {
        public static HitboxTweak instance;
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;
        private static readonly FieldInfo RectFieldInfo = typeof(Draw).GetField("rect", BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly List<Type> UselessTypes = new List<Type> {
            typeof(ClutterBlockBase),
            typeof(CrystalDebris),
            typeof(Debris),
            typeof(Door),
            typeof(FloatingDebris),
            typeof(HangingLamp),
            typeof(MoonCreature),
            typeof(MoveBlock).GetNestedType("Debris", BindingFlags.NonPublic),
            typeof(PlaybackBillboard),
            typeof(ResortLantern),
            typeof(Torch),
        };

        public void Load() {
            On.Monocle.Entity.DebugRender += HideHitbox;
            On.Monocle.Grid.Render += CombineHitbox;
            On.Monocle.Hitbox.Render += ModHitbox;
            On.Monocle.Draw.HollowRect_float_float_float_float_Color += AvoidRedrawCorners;
            HitboxTriggerSpikes.Load();
        }

        public void Unload() {
            On.Monocle.Entity.DebugRender -= HideHitbox;
            On.Monocle.Grid.Render -= CombineHitbox;
            On.Monocle.Hitbox.Render -= ModHitbox;
            On.Monocle.Draw.HollowRect_float_float_float_float_Color -= AvoidRedrawCorners;
            HitboxTriggerSpikes.Unload();
        }

        private static void HideHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (Settings.HideTriggerHitboxes && self is Trigger) {
                return;
            }

            if (Settings.SimplifiedHitboxes && UselessTypes.Contains(self.GetType())) {
                return;
            }

            orig(self, camera);
        }

        private static void CombineHitbox(On.Monocle.Grid.orig_Render orig, Grid self, Camera camera, Color color) {
            if (!Settings.SimplifiedHitboxes) {
                orig(self, camera, color);
                return;
            }

            if (camera == null) {
                for (int x = 0; x < self.CellsX; ++x) {
                    for (int y = 0; y < self.CellsY; ++y) {
                        DrawCombineHollowRect(self, color, x, y, 0, self.CellsX - 1, 0, self.CellsY - 1);
                    }
                }
            } else {
                int left = (int) Math.Max(0.0f, (camera.Left - self.AbsoluteLeft) / self.CellWidth);
                int right = (int) Math.Min(self.CellsX - 1, Math.Ceiling((camera.Right - (double) self.AbsoluteLeft) / self.CellWidth));
                int top = (int) Math.Max(0.0f, (camera.Top - self.AbsoluteTop) / self.CellHeight);
                int bottom = (int) Math.Min(self.CellsY - 1,
                    Math.Ceiling((camera.Bottom - (double) self.AbsoluteTop) / self.CellHeight));

                for (int x = left; x <= right; ++x) {
                    for (int y = top; y <= bottom; ++y) {
                        DrawCombineHollowRect(self, color, x, y, left, right, top, bottom);
                    }
                }
            }
        }

        private static void AvoidRedrawCorners(On.Monocle.Draw.orig_HollowRect_float_float_float_float_Color orig, float x,
            float y, float width, float height, Color color) {
            if (!CelesteTASModule.Settings.SimplifiedHitboxes) {
                orig(x, y, width, height, color);
                return;
            }

            var rect = (Rectangle) RectFieldInfo.GetValue(null);
            rect.X = (int) x;
            rect.Y = (int) y;
            rect.Width = (int) width;
            rect.Height = 1;
            Draw.SpriteBatch.Draw(Draw.Pixel.Texture.Texture_Safe, rect, Draw.Pixel.ClipRect, color);

            rect.Y += (int) height - 1;
            Draw.SpriteBatch.Draw(Draw.Pixel.Texture.Texture_Safe, rect, Draw.Pixel.ClipRect, color);

            rect.Y -= (int) height - 2;
            rect.Width = 1;
            rect.Height = (int) height - 2;
            Draw.SpriteBatch.Draw(Draw.Pixel.Texture.Texture_Safe, rect, Draw.Pixel.ClipRect, color);

            rect.X += (int) width - 1;
            Draw.SpriteBatch.Draw(Draw.Pixel.Texture.Texture_Safe, rect, Draw.Pixel.ClipRect, color);
        }

        private static void DrawCombineHollowRect(Grid grid, Color color, int x, int y, int left, int right, int top, int bottom) {
            float topLeftX = grid.AbsoluteLeft + x * grid.CellWidth;
            float topLeftY = grid.AbsoluteTop + y * grid.CellHeight;
            Vector2 width = Vector2.UnitX * grid.CellWidth;
            Vector2 height = Vector2.UnitY * grid.CellHeight;

            Vector2 topLeft = new Vector2(topLeftX, topLeftY);
            Vector2 topRight = topLeft + width;
            Vector2 bottomLeft = topLeft + height;
            Vector2 bottomRight = topRight + height;

            bool drawnLeft = false, drawnRight = false, drawnTop = false, drawnBottom = false;

            VirtualMap<bool> data = grid.Data;

            if (data[x, y]) {
                // left
                if (x != left && !data[x - 1, y]) {
                    Draw.Line(topLeft + Vector2.One, bottomLeft + Vector2.UnitX - Vector2.UnitY, color);
                    drawnLeft = true;
                }

                // right
                if (x == right || x + 1 <= right && !data[x + 1, y]) {
                    Draw.Line(topRight + Vector2.UnitY, bottomRight - Vector2.UnitY, color);
                    drawnRight = true;
                }

                // top
                if (y != top && !data[x, y - 1]) {
                    Draw.Line(topLeft + Vector2.UnitX, topRight - Vector2.UnitX, color);
                    drawnTop = true;
                }

                // bottom
                if (y == bottom || y + 1 <= bottom && !data[x, y + 1]) {
                    Draw.Line(bottomLeft - Vector2.UnitY + Vector2.UnitX, bottomRight - Vector2.One, color);
                    drawnBottom = true;
                }

                // top left point
                if (drawnTop || drawnLeft) {
                    Draw.Point(topLeft, color);
                }

                // top right point
                if (drawnTop || drawnRight) {
                    Draw.Point(topRight - Vector2.UnitX, color);
                }

                // bottom left point
                if (drawnBottom || drawnLeft) {
                    Draw.Point(bottomLeft - Vector2.UnitY, color);
                }

                // bottom right point
                if (drawnBottom || drawnRight) {
                    Draw.Point(bottomRight - Vector2.One, color);
                }
            } else {
                // inner hollow top left point
                if (x - 1 >= left && y - 1 >= top && data[x - 1, y - 1] && data[x - 1, y] && data[x, y - 1]) {
                    Draw.Point(topLeft - Vector2.One, color);
                }

                // inner hollow top right point
                if (x + 1 <= right && y - 1 >= top && data[x + 1, y - 1] && data[x + 1, y] && data[x, y - 1]) {
                    Draw.Point(topRight - Vector2.UnitY, color);
                }

                // inner hollow bottom left point
                if (x - 1 >= left && y + 1 <= bottom && data[x - 1, y + 1] && data[x - 1, y] && data[x, y + 1]) {
                    Draw.Point(bottomLeft - Vector2.UnitX, color);
                }

                // inner hollow bottom right point
                if (x + 1 <= right && y + 1 >= top && data[x + 1, y + 1] && data[x + 1, y] && data[x, y + 1]) {
                    Draw.Point(bottomRight, color);
                }
            }
        }

        private static void ModHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox hitbox, Camera camera, Color color) {
            Entity entity = hitbox.Entity;
            if (entity is WallBooster) {
                Draw.Rect(hitbox.AbsolutePosition, hitbox.Width, hitbox.Height, HitboxColor.EntityColorInverselyLessAlpha);
                return;
            }

            orig(hitbox, camera, color);
        }
    }
}