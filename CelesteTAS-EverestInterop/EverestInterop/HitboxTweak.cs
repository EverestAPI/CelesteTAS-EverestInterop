using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
    public class HitboxTweak {
        public static HitboxTweak instance;
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        private static readonly List<Type> UselessTypes = new List<Type> {
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
            On.Monocle.Hitbox.Render += ModWallBoosterHitbox;
        }

        public void Unload() {
            On.Monocle.Entity.DebugRender -= HideHitbox;
            On.Monocle.Grid.Render -= CombineHitbox;
            On.Monocle.Hitbox.Render -= ModWallBoosterHitbox;
        }

        private static void HideHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (Settings.HideTriggerHitbox && self is Trigger) {
                return;
            }

            if (Settings.HideUselessHitbox && UselessTypes.Contains(self.GetType())) {
                return;
            }

            orig(self, camera);
        }

        private static void CombineHitbox(On.Monocle.Grid.orig_Render orig, Grid self, Camera camera, Color color) {
            if (!Settings.HideUselessHitbox) {
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

        private static void DrawCombineHollowRect(Grid grid, Color color, int x, int y, int left, int right, int top, int bottom) {
            float topLeftX = grid.AbsoluteLeft + x * grid.CellWidth;
            float topLeftY = grid.AbsoluteTop + y * grid.CellHeight;
            Vector2 vector2Width = Vector2.UnitX * grid.CellWidth;
            Vector2 vector2Height = Vector2.UnitY * grid.CellHeight;

            Vector2 topLeft = new Vector2(topLeftX, topLeftY);
            Vector2 topRight = topLeft + vector2Width;
            Vector2 bottomLeft = topLeft + vector2Height;
            Vector2 bottomRight = topRight + vector2Height;

            VirtualMap<bool> data = grid.Data;

            if (data[x, y]) {
                // left
                if (x != left && !data[x - 1, y]) {
                    Draw.Line(topLeft + Vector2.UnitX, bottomLeft + Vector2.UnitX, color);
                }

                // right
                if (x == right || x + 1 <= right && !data[x + 1, y]) {
                    Draw.Line(topRight, bottomRight, color);
                }

                // top
                if (y != top && !data[x, y - 1]) {
                    Draw.Line(topLeft, topRight, color);
                }

                // bottom
                if (y == bottom || y + 1 <= bottom && !data[x, y + 1]) {
                    Draw.Line(bottomLeft - Vector2.UnitY, bottomRight - Vector2.UnitY, color);
                }
            } else {
                // top left point
                if (x - 1 >= left && y - 1 >= top && data[x - 1, y - 1] && data[x - 1, y] && data[x, y - 1]) {
                    Draw.Point(topLeft - Vector2.One, color);
                }

                // top right point
                if (x + 1 <= right && y - 1 >= top && data[x + 1, y - 1] && data[x + 1, y] && data[x, y - 1]) {
                    Draw.Point(topRight - Vector2.UnitY, color);
                }

                // bottom left point
                if (x - 1 >= left && y + 1 <= bottom && data[x - 1, y + 1] && data[x - 1, y] && data[x, y + 1]) {
                    Draw.Point(bottomLeft - Vector2.UnitX, color);
                }

                // bottom right point
                if (x + 1 <= right && y + 1 >= top && data[x + 1, y + 1] && data[x + 1, y] && data[x, y + 1]) {
                    Draw.Point(bottomRight, color);
                }
            }
        }

        private void ModWallBoosterHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox hitbox, Camera camera, Color color) {
            if (hitbox.Entity is WallBooster) {
                Draw.Rect(hitbox.AbsolutePosition, hitbox.Width, hitbox.Height, color.Invert() * 0.5f);
                return;
            }
            orig(hitbox, camera, color);
        }
    }
}