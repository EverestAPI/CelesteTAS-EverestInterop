using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using TAS.EverestInterop.Hitboxes;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes.ModInterop;

/// Visualize sight-lines of Melvin blocks
internal static class MelvinHitbox {

    [Initialize]
    private static void Initialize() {
        var t_Melvin = ModUtils.GetType("CommunalHelper", "Celeste.Mod.CommunalHelper.Entities.Melvin");
        if (t_Melvin == null) {
            return;
        }

        Events.PostDebugRender += scene => {
            // Melvins support multiple targets, but let's just focus on the player
            if (scene.GetPlayer() is not { } player) {
                return;
            }

            bool any = false;
            foreach (var melvin in scene.Tracker.GetEntitiesTrackIfNeeded(t_Melvin)) {
                any = true;
                RenderSightLines(player, (Solid) melvin);
            }

            // Highlight player center
            if (any) {
                Draw.Point(player.Center.Ceiling(), Color.Pink);
            }
        };
    }

    private static unsafe void RenderSightLines(Player player, Solid melvin) {
        var levelBounds = melvin.SceneAs<Level>().Bounds;

        int left = (int) melvin.Left;
        int right = (int) melvin.Right;
        int top = (int) melvin.Top;
        int bottom = (int) melvin.Bottom;

        float halfWidth = player.Width / 2.0f;
        float halfHeight = player.Height / 2.0f;

        var hitboxColor = melvin.GetFieldValue<bool>("triggered")
            ? Color.Orchid * HitboxColor.UnCollidableAlpha
            : Color.Orchid;

        Span<int> verticalDists = stackalloc int[bottom - top];
        Span<int> horizontalDists = stackalloc int[right - left];

        // NOTE: Small offsets are sometimes applied for.. reason, sorry

        // Left
        int leftStop = levelBounds.Left;
        for (int i = 0; i < verticalDists.Length; i++) {
            verticalDists[i] = CollideBisectLeft(melvin.Scene, leftStop, left, top + i);
        }

        int prevX = left;
        int prevY = top;
        for (int currY = top; currY < bottom; currY++) {
            int maxLeft = left;
            if (currY != bottom - 1) {
                int checkTop = Math.Max(0, (int) (currY - halfHeight) - top);
                int checkBottom = Math.Min(verticalDists.Length, (int) (currY + halfHeight) - top);
                for (int i = checkTop; i < checkBottom; i++) {
                    maxLeft = Math.Min(maxLeft, verticalDists[i] - (int) halfWidth + 2);
                }
            }

            if (maxLeft != prevX) {
                if (currY != prevY) {
                    Draw.Line(prevX, prevY, prevX, currY, hitboxColor);
                }
                Draw.Line(maxLeft, currY, prevX, currY, hitboxColor);

                prevX = maxLeft;
                prevY = currY;
            }
        }

        // Right
        int rightStop = levelBounds.Right;
        for (int i = 0; i < verticalDists.Length; i++) {
            verticalDists[i] = CollideBisectRight(melvin.Scene, right, rightStop, top + i);
        }

        prevX = right;
        prevY = top;
        for (int currY = top; currY < bottom; currY++) {
            int maxRight = right;
            if (currY != bottom - 1) {
                int checkTop = Math.Max(0, (int) (currY - halfHeight) - top);
                int checkBottom = Math.Min(verticalDists.Length, (int) (currY + halfHeight) - top);
                for (int i = checkTop; i < checkBottom; i++) {
                    maxRight = Math.Max(maxRight, verticalDists[i] + (int) halfWidth - 1);
                }
            }

            if (maxRight != prevX) {
                if (currY != prevY) {
                    Draw.Line(prevX, currY, prevX, prevY, hitboxColor);
                }
                Draw.Line(prevX, currY, maxRight, currY, hitboxColor);

                prevX = maxRight;
                prevY = currY;
            }
        }

        // Top
        int topStop = levelBounds.Top;
        for (int i = 0; i < horizontalDists.Length; i++) {
            horizontalDists[i] = CollideBisectTop(melvin.Scene, left + i, topStop, top);
        }

        prevX = left + 1;
        prevY = top;
        for (int currX = left + 1; currX <= right; currX++) {
            int maxTop = top;
            if (currX != right) {
                int checkLeft = Math.Max(0, (int) (currX - halfWidth) - left);
                int checkRight = Math.Min(horizontalDists.Length, (int) (currX + halfWidth) - left);
                for (int i = checkLeft; i < checkRight; i++) {
                    maxTop = Math.Min(maxTop, horizontalDists[i] - (int) halfHeight + 2);
                }
            }

            if (maxTop != prevY) {
                if (currX != prevX) {
                    Draw.Line(currX, prevY, prevX, prevY, hitboxColor);
                }
                Draw.Line(currX, prevY, currX, maxTop, hitboxColor);

                prevX = currX;
                prevY = maxTop;
            }
        }

        // Bottom
        int bottomStop = levelBounds.Bottom;
        for (int i = 0; i < horizontalDists.Length; i++) {
            horizontalDists[i] = CollideBisectBottom(melvin.Scene, left + i, bottom, bottomStop);
        }

        prevX = left + 1;
        prevY = bottom;
        for (int currX = left + 1; currX <= right; currX++) {
            int maxBottom = bottom;
            if (currX != right) {
                int checkLeft = Math.Max(0, (int) (currX - halfWidth) - left);
                int checkRight = Math.Min(horizontalDists.Length, (int) (currX + halfWidth) - left);
                for (int i = checkLeft; i < checkRight; i++) {
                    maxBottom = Math.Max(maxBottom, horizontalDists[i] + (int) Math.Ceiling(halfHeight));
                }
            }

            if (maxBottom != prevY) {
                if (currX != prevX) {
                    Draw.Line(currX, prevY, prevX, prevY, hitboxColor);
                }
                Draw.Line(currX, maxBottom, currX, prevY, hitboxColor);

                prevX = currX;
                prevY = maxBottom;
            }
        }
    }

    private static int CollideBisectLeft(Scene scene, int x1, int x2, int y) {
        while (x1 != x2) {
            int mid = x1 / 2 + x2 / 2;
            if (x1 == mid || x2 == mid) {
                break;
            }

            var lineR = new Rectangle(mid, y, x2 - mid, 1);
            if (scene.CollideCheck<Solid>(lineR)) {
                x1 = mid;
                continue;
            }

            var lineL = new Rectangle(x1, y, mid - x1, 1);
            if (scene.CollideCheck<Solid>(lineL)) {
                x2 = mid;
                continue;
            }

            return x1;
        }

        return x1;
    }
    private static int CollideBisectRight(Scene scene, int x1, int x2, int y) {
        while (x1 != x2) {
            int mid = x1 / 2 + x2 / 2;
            if (x1 == mid || x2 == mid) {
                break;
            }

            var lineL = new Rectangle(x1, y, mid - x1, 1);
            if (scene.CollideCheck<Solid>(lineL)) {
                x2 = mid;
                continue;
            }

            var lineR = new Rectangle(mid, y, x2 - mid, 1);
            if (scene.CollideCheck<Solid>(lineR)) {
                x1 = mid;
                continue;
            }

            return x2;
        }

        return x2;
    }
    private static int CollideBisectTop(Scene scene, int x, int y1, int y2) {
        while (y1 != y2) {
            int mid = y1 / 2 + y2 / 2;
            if (y1 == mid || y2 == mid) {
                break;
            }

            var lineB = new Rectangle(x, mid, 1, y2 - mid);
            if (scene.CollideCheck<Solid>(lineB)) {
                y1 = mid;
                continue;
            }

            var lineT = new Rectangle(x, y1, 1, mid - y1);
            if (scene.CollideCheck<Solid>(lineT)) {
                y2 = mid;
                continue;
            }

            return y1;
        }

        return y1;
    }
    private static int CollideBisectBottom(Scene scene, int x, int y1, int y2) {
        while (y1 != y2) {
            int mid = y1 / 2 + y2 / 2;
            if (y1 == mid || y2 == mid) {
                break;
            }

            var lineT = new Rectangle(x, y1, 1, mid - y1);
            if (scene.CollideCheck<Solid>(lineT)) {
                y2 = mid;
                continue;
            }

            var lineB = new Rectangle(x, mid, 1, y2 - mid);
            if (scene.CollideCheck<Solid>(lineB)) {
                y1 = mid;
                continue;
            }

            return y2;
        }

        return y2;
    }
}
