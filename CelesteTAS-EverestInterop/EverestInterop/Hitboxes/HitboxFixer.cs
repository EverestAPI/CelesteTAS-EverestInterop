using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
public static class HitboxFixer {
    private static bool drawingHitboxes;
    public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public static void Load() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
        On.Monocle.Draw.HollowRect_float_float_float_float_Color += modDrawHollowRect;
        On.Monocle.Draw.Circle_Vector2_float_Color_int += modDrawCircle;
    }

    public static void Unload() {
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
        On.Monocle.Draw.HollowRect_float_float_float_float_Color -= modDrawHollowRect;
        On.Monocle.Draw.Circle_Vector2_float_Color_int -= modDrawCircle;
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, Monocle.EntityList self, Camera camera) {
        drawingHitboxes = true;
        orig(self, camera);
        drawingHitboxes = false;
    }

    private static void modDrawHollowRect(On.Monocle.Draw.orig_HollowRect_float_float_float_float_Color orig, float x, float y, float width,
        float height, Color color) {
        if (!Settings.ShowHitboxes || !drawingHitboxes) {
            orig(x, y, width, height, color);
            return;
        }

        float fx = (float) Math.Floor(x);
        float fy = (float) Math.Floor(y);
        float cw = (float) Math.Ceiling(width + x - fx);
        float cy = (float) Math.Ceiling(height + y - fy);
        orig(fx, fy, cw, cy, color);
    }

    private static void modDrawCircle(On.Monocle.Draw.orig_Circle_Vector2_float_Color_int orig, Vector2 center, float radius, Color color,
        int resolution) {
        // Adapted from John Kennedy, "A Fast Bresenham Type Algorithm For Drawing Circles"
        // https://web.engr.oregonstate.edu/~sllu/bcircle.pdf
        // Not as fast though because we are forced to use floating point arithmetic anyway
        // since the center and radius aren't necessarily integral.
        // For similar reasons, we can't just assume the circle has 8-fold symmetry.
        // Modified so that instead of minimizing error, we include exactly those pixels which intersect the circle.

        if (!Settings.ShowHitboxes || !drawingHitboxes) {
            orig(center, radius, color, resolution);
            return;
        }

        CircleOctant(center, radius, color, 1, 1, false);
        CircleOctant(center, radius, color, 1, -1, false);
        CircleOctant(center, radius, color, -1, 1, false);
        CircleOctant(center, radius, color, -1, -1, false);
        CircleOctant(center, radius, color, 1, 1, true);
        CircleOctant(center, radius, color, 1, -1, true);
        CircleOctant(center, radius, color, -1, 1, true);
        CircleOctant(center, radius, color, -1, -1, true);
    }

    private static void CircleOctant(Vector2 center, float radius, Color color, float flipX, float flipY, bool interchangeXY) {
        // when flipX = flipY = 1 and interchangeXY = false, we are drawing the [0, pi/4] octant.

        float cx, cy;
        if (interchangeXY) {
            cx = center.Y;
            cy = center.X;
        } else {
            cx = center.X;
            cy = center.Y;
        }

        float x, y;
        if (flipX > 0) {
            x = (float) Math.Ceiling(cx + radius - 1);
        } else {
            x = (float) Math.Floor(cx - radius + 1);
        }

        if (flipY > 0) {
            y = (float) Math.Floor(cy);
        } else {
            y = (float) Math.Ceiling(cy);
        }

        float starty = y;
        float E = (x - cx) * (x - cx) + (y - cy) * (y - cy) - radius * radius;
        float YC = flipY * 2 * (y - cy) + 1;
        float XC = flipX * -2 * (x - cx) + 1;
        while (flipY * (y - cy) <= flipX * (x - cx)) {
            // Slower than using DrawLine, but more obviously correct:
            //DrawPoint((int)x + (flipX < 0 ? -1 : 0), (int)y + (flipY < 0 ? -1 : 0), interchangeXY, color);
            E += YC;
            y += flipY;
            YC += 2;
            if (E >= 0) {
                // We would have a 1px correction for flipY here (as we do for flipX) except for
                // the fact that our lines always include the top pixel and exclude the bottom one.
                // Because of this we would have to make two corrections which cancel each other out,
                // so we just don't do either of them.
                DrawLine((int) x + (flipX < 0 ? -1 : 0), (int) starty, (int) y, interchangeXY, color);
                starty = y;
                E += XC;
                x -= flipX;
                XC += 2;
            }
        }

        DrawLine((int) x + (flipX < 0 ? -1 : 0), (int) starty, (int) y, interchangeXY, color);
    }

    private static void DrawLine(int x, int y0, int y1, bool interchangeXY, Color color) {
        // x, y0, and y1 must all be integers
        int length = (int) (y1 - y0);
        Rectangle rect;
        if (interchangeXY) {
            rect.X = (int) y0;
            rect.Y = (int) x;
            rect.Width = length;
            rect.Height = 1;
        } else {
            rect.X = (int) x;
            rect.Y = (int) y0;
            rect.Width = 1;
            rect.Height = length;
        }

        Draw.SpriteBatch.Draw(Draw.Pixel.Texture.Texture, rect, Draw.Pixel.ClipRect, color);
    }
}
}