using System;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using StudioCommunication.Util;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

/// Fixes incorrect hitbox shapes
internal static class HitboxFixer {
    public static bool DrawingHitboxes { get; set; }

    [Load]
    private static void Load() {
        IL.Celeste.GameplayRenderer.Render += GameplayRendererOnRender;
        On.Monocle.Draw.HollowRect_float_float_float_float_Color += ModDrawHollowRect;
        On.Monocle.Draw.Circle_Vector2_float_Color_int += ModDrawCircle;
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.GameplayRenderer.Render -= GameplayRendererOnRender;
        On.Monocle.Draw.HollowRect_float_float_float_float_Color -= ModDrawHollowRect;
        On.Monocle.Draw.Circle_Vector2_float_Color_int -= ModDrawCircle;
    }

    private static void GameplayRendererOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(i => i.MatchCallvirt<EntityList>("DebugRender"))) {
            ilCursor.EmitDelegate<Action>(SetDrawingHitboxesTrue);
            ilCursor.Index++;
            ilCursor.EmitDelegate<Action>(SetDrawingHitboxesFalse);
        }
    }

    private static void SetDrawingHitboxesTrue() {
        DrawingHitboxes = true;
    }

    private static void SetDrawingHitboxesFalse() {
        DrawingHitboxes = false;
    }

    private static void ModDrawHollowRect(On.Monocle.Draw.orig_HollowRect_float_float_float_float_Color orig, float x, float y, float width,
        float height, Color color) {
        if (!TasSettings.ShowHitboxes || !DrawingHitboxes) {
            orig(x, y, width, height, color);
            return;
        }

        float fx = (float) Math.Floor(x);
        float fy = (float) Math.Floor(y);
        float cw = (float) Math.Ceiling(width + x - fx);
        float cy = (float) Math.Ceiling(height + y - fy);
        orig(fx, fy, cw, cy, color);
    }

    private static void ModDrawCircle(On.Monocle.Draw.orig_Circle_Vector2_float_Color_int orig, Vector2 center, float radius, Color color,
        int resolution) {
        // Adapted from John Kennedy, "A Fast Bresenham Type Algorithm For Drawing Circles"
        // https://web.engr.oregonstate.edu/~sllu/bcircle.pdf
        // Not as fast though because we are forced to use floating point arithmetic anyway
        // since the center and radius aren't necessarily integral.
        // For similar reasons, we can't just assume the circle has 8-fold symmetry.
        // Modified so that instead of minimizing error, we include exactly those pixels which intersect the circle.

        if (!TasSettings.ShowHitboxes || !DrawingHitboxes) {
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

    private static void CircleOctant(Vector2 center, float radius, Color color, float flipX, float flipY, bool interchangeXy) {
        // when flipX = flipY = 1 and interchangeXY = false, we are drawing the [0, pi/4] octant.

        float cx, cy;
        if (interchangeXy) {
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
        float e = (x - cx) * (x - cx) + (y - cy) * (y - cy) - radius * radius;
        float yc = flipY * 2 * (y - cy) + 1;
        float xc = flipX * -2 * (x - cx) + 1;
        while (flipY * (y - cy) <= flipX * (x - cx)) {
            // Slower than using DrawLine, but more obviously correct:
            //DrawPoint((int)x + (flipX < 0 ? -1 : 0), (int)y + (flipY < 0 ? -1 : 0), interchangeXY, color);
            e += yc;
            y += flipY;
            yc += 2;
            if (e >= 0) {
                // We would have a 1px correction for flipY here (as we do for flipX) except for
                // the fact that our lines always include the top pixel and exclude the bottom one.
                // Because of this we would have to make two corrections which cancel each other out,
                // so we just don't do either of them.
                DrawLine((int) x + (flipX < 0 ? -1 : 0), (int) starty, (int) y, interchangeXy, color);
                starty = y;
                e += xc;
                x -= flipX;
                xc += 2;
            }
        }

        DrawLine((int) x + (flipX < 0 ? -1 : 0), (int) starty, (int) y, interchangeXy, color);
    }

    private static void DrawLine(int x, int y0, int y1, bool interchangeXy, Color color) {
        // x, y0, and y1 must all be integers
        int length = (int) (y1 - y0);
        Rectangle rect;
        if (interchangeXy) {
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

    private delegate void orig_ShapeHitbox_Render(Collider self, Camera camera, Color color);

    [ModOnHook("FrostHelper", "FrostHelper.Colliders.ShapeHitbox", "Render")]
    private static void AccurateShapeHitbox(orig_ShapeHitbox_Render orig, Collider self, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes || !DrawingHitboxes) {
            orig(self, camera, color);
            return;
        }

        var points = self.GetFieldValue<Vector2[]>("Points")!;

        for (int i = 0; i < points.Length - 1; i++) {
            DrawExactLine(points[i], points[i + 1], color);
        }
        if (self.GetFieldValue<bool>("Fill")) {
            DrawExactLine(points[0], points[^1], color);
        }
    }

    /// Draws an exact line, filling all pixels the line actually intersects
    /// Based on the logic of Collide.LineToRect and with the assumption that other colliders are grid-aligned
    internal static void DrawExactLine(Vector2 from, Vector2 to, Color color) {
        // The line endings can touch the right / bottom side of the collider without colliding,
        // because Collide.GetSector uses >= instead of > for those
        int minX = (int) MathF.Floor(Math.Min(from.X, to.X));
        int minY = (int) MathF.Floor(Math.Min(from.Y, to.Y));
        Draw.Point(new Vector2(MathF.Floor(from.X), MathF.Floor(from.Y)), color);
        Draw.Point(new Vector2(MathF.Floor(to.X),   MathF.Floor(to.Y)),   color);

        // Straight lines
        if ((int) Math.Floor(from.X) == (int) Math.Floor(to.X)) {
            int top    = Math.Max(minY, (int)MathF.Floor  (MathF.BitDecrement(Math.Min(from.Y, to.Y))));
            int bottom = Math.Max(minY, (int)MathF.Ceiling(MathF.BitIncrement(Math.Max(from.Y, to.Y))));
            Draw.Rect(MathF.Floor(Math.Min(from.X, to.X)), top, 1, bottom - top, color);
            return;
        }
        if ((int) Math.Floor(from.Y) == (int) Math.Floor(to.Y)) {
            int left   = Math.Max(minX, (int)MathF.Floor  (MathF.BitDecrement(Math.Min(from.X, to.X))));
            int right  = Math.Max(minX, (int)MathF.Ceiling(MathF.BitIncrement(Math.Max(from.X, to.X))));
            Draw.Rect(left, MathF.Floor(Math.Min(from.Y, to.Y)), right - left, 1, color);
            return;
        }

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
        float step = Math.Min(Math.Abs(dx), Math.Abs(dy));
        dx /= step;
        dy /= step;

        // Starting / Ending point might not be an integer coordinates
        float startDist = horizontal
            ? Math.Sign(dy) * 0.5f + 0.5f - from.Y.Mod(1.0f)
            : Math.Sign(dx) * 0.5f + 0.5f - from.X.Mod(1.0f);
        float endDist = horizontal
            ? Math.Sign(dy) * 0.5f + 0.5f - (1.0f - to.Y).Mod(1.0f)
            : Math.Sign(dx) * 0.5f + 0.5f - (1.0f - to.X).Mod(1.0f);

        startDist = Math.Abs(startDist).Mod(1.0f);
        endDist   = Math.Abs(endDist).Mod(1.0f);

        float startX = from.X + startDist * dx;
        float startY = from.Y + startDist * dy;
        float endX   = to.X   - endDist   * dx;
        float endY   = to.Y   - endDist   * dy;

        DrawSegment(from.X, from.Y, startX, startY);
        DrawSegment(to.X,   to.Y,   endX,   endY);

        int steps = (int) Math.Min(MathF.Round(Math.Abs(endX - startX)), MathF.Round(Math.Abs(endY - startY)));

        float x = startX, y = startY;
        for (int i = 0; i < steps; i++, x += dx, y += dy) {
            DrawSegment(x, y, x + dx, y + dy);
        }
        void DrawSegment(float ax, float ay, float bx, float by) {
            if (horizontal) {
                int left   = Math.Max(minX, (int)MathF.Floor  (MathF.BitDecrement(Math.Min(ax, bx))));
                int right  = Math.Max(minX, (int)MathF.Ceiling(MathF.BitIncrement(Math.Max(ax, bx))));
                Draw.Rect(left, MathF.Floor(Math.Min(ay, by)), right - left, 1, color);
            } else {
                int top    = Math.Max(minY, (int)MathF.Floor  (MathF.BitDecrement(Math.Min(ay, by))));
                int bottom = Math.Max(minY, (int)MathF.Ceiling(MathF.BitIncrement(Math.Max(ay, by))));
                Draw.Rect(MathF.Floor(Math.Min(ax, bx)), top, 1, bottom - top, color);
            }
        }
    }
}
