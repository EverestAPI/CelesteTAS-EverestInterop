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
        if (ModUtils.GetType("CommunalHelper", "Celeste.Mod.CommunalHelper.Entities.Melvin") is not { } t_Melvin) {
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
                Draw.Point(player.Center.Floor(), Color.Red);
            }
        };
    }

    private static unsafe void RenderSightLines(Player player, Solid melvin) {
        var levelBounds = melvin.SceneAs<Level>().Bounds;

        int left = (int) melvin.Left;
        int right = (int) melvin.Right;
        int top = (int) melvin.Top;
        int bottom = (int) melvin.Bottom;

        var hitboxColor = melvin.GetFieldValue<bool>("triggered")
            ? Color.Orchid * HitboxColor.UnCollidableAlpha
            : Color.Orchid;

        Span<int> verticalDists = stackalloc int[bottom - top];
        Span<int> horizontalDists = stackalloc int[right - left];

        // The check is X > Left, which means if X == Left it's not yet triggered
        // ReSharper disable CompareOfFloatsByEqualityOperator
        int playerCenterXOffset = player.CenterX == (int) player.CenterX ? 1 : 0;
        int playerCenterYOffset = player.CenterY == (int) player.CenterY ? 1 : 0;
        // ReSharper restore CompareOfFloatsByEqualityOperator

        // Pixel distances from the floored center position
        float halfWidth = player.Width / 2.0f;
        float halfHeight = player.Height / 2.0f;
        int centerToLeft = (int) -Math.Floor(halfWidth);
        int centerToRight = (int) Math.Ceiling(halfWidth);
        int centerToTop = (int) -Math.Floor(halfHeight);
        int centerToBottom = (int) Math.Ceiling(halfHeight);

        // Left
        int leftStop = levelBounds.Left;
        for (int i = 0; i < verticalDists.Length; i++) {
            verticalDists[i] = CollideBisectLeft(melvin.Scene, leftStop, left, top + i);
        }

        int prevX = left;
        int prevY = top + playerCenterYOffset;
        int prevLineY = prevY;
        bool prevLineRight = false;
        for (int currY = prevY; currY <= bottom; currY++) {
            int maxLeft = left;
            if (currY != bottom) {
                int currIdx = currY - top;
                int checkTop = Math.Max(0, currIdx + centerToTop);
                int checkBottom = Math.Min(verticalDists.Length, currIdx + centerToBottom);
                for (int i = checkTop; i < checkBottom; i++) {
                    maxLeft = Math.Min(maxLeft, verticalDists[i] + centerToLeft + 1);
                }
            }

            if (maxLeft != prevX) {
                // If the distance is now closer to the Melvin, the edge was on the previous pixel
                int currLineY = prevX < maxLeft ? currY - 1 : currY;
                Draw.Line(Math.Min(prevX, maxLeft), currLineY, Math.Max(prevX, maxLeft), currLineY, hitboxColor);

                bool currLineRight = prevX < maxLeft;
                if (currY != prevY) {
                    // Need to shift up/down depending on, if the other line is going left/right
                    Draw.Line(prevX, currLineY + (currLineRight ? 0 : 1), prevX, prevLineY + (prevLineRight ? 0 : 1), hitboxColor);
                }

                prevX = maxLeft;
                prevY = currY;
                prevLineY = currLineY;
                prevLineRight = currLineRight;
            }
        }
        Draw.Line(left - 1, bottom, left - 1, top + playerCenterYOffset, hitboxColor);

        // Right
        int rightStop = levelBounds.Right;
        for (int i = 0; i < verticalDists.Length; i++) {
            verticalDists[i] = CollideBisectRight(melvin.Scene, right, rightStop, top + i);
        }

        prevX = right + playerCenterXOffset;
        prevY = top + playerCenterYOffset;
        prevLineY = prevY;
        prevLineRight = false;
        for (int currY = prevY; currY <= bottom; currY++) {
            int maxRight = right + playerCenterXOffset;
            if (currY != bottom) {
                int currIdx = currY - top;
                int checkTop = Math.Max(0, currIdx + centerToTop);
                int checkBottom = Math.Min(verticalDists.Length, currIdx + centerToBottom);
                for (int i = checkTop; i < checkBottom; i++) {
                    maxRight = Math.Max(maxRight, verticalDists[i] + centerToRight - 1);
                }
            }

            if (maxRight != prevX) {
                // If the distance is now closer to the Melvin, the edge was on the previous pixel
                int currLineY = prevX > maxRight ? currY - 1 : currY;
                Draw.Line(Math.Min(prevX, maxRight), currLineY, Math.Max(prevX, maxRight), currLineY, hitboxColor);

                bool currLineRight = prevX < maxRight;
                if (currY != prevY) {
                    // Need to shift up/down depending on, if the other line is going left/right
                    Draw.Line(prevX, currLineY + (currLineRight ? 0 : 1), prevX, prevLineY + (prevLineRight ? 0 : 1), hitboxColor);
                }

                prevX = maxRight;
                prevY = currY;
                prevLineY = currLineY;
                prevLineRight = currLineRight;
            }
        }
        Draw.Line(right + playerCenterXOffset, bottom, right + playerCenterXOffset, top + playerCenterYOffset, hitboxColor);

        // Top
        int topStop = levelBounds.Top;
        for (int i = 0; i < horizontalDists.Length; i++) {
            horizontalDists[i] = CollideBisectTop(melvin.Scene, left + i, topStop, top);
        }

        prevX = left + playerCenterXOffset;
        prevY = top;
        int prevLineX = prevX;
        bool prevLineUp = false;
        for (int currX = prevX; currX <= right; currX++) {
            int maxTop = top;
            if (currX != right) {
                int currIdx = currX - left;
                int checkLeft = Math.Max(0, currIdx + centerToLeft);
                int checkRight = Math.Min(horizontalDists.Length, currIdx + centerToRight);
                for (int i = checkLeft; i < checkRight; i++) {
                    maxTop = Math.Min(maxTop, horizontalDists[i] + centerToTop + 1);
                }
            }

            if (maxTop != prevY) {
                // If the distance is now closer to the Melvin, the edge was on the previous pixel
                int currLineX = prevY < maxTop ? currX - 1 : currX;
                Draw.Line(currLineX, Math.Max(prevY, maxTop), currLineX, Math.Min(prevY, maxTop), hitboxColor);

                bool currLineUp = prevY > maxTop;
                if (currX != prevX) {
                    // Need to shift left/right depending on, if the other line is going up/down
                    Draw.Line(currLineX + (currLineUp ? 0 : 1), prevY, prevLineX + (prevLineUp ? 0 : 1), prevY, hitboxColor);
                }

                prevX = currX;
                prevY = maxTop;
                prevLineX = currLineX;
                prevLineUp = currLineUp;
            }
        }
        Draw.Line(left + playerCenterXOffset, top - 1, right, top - 1, hitboxColor);

        // Bottom
        int bottomStop = levelBounds.Bottom;
        for (int i = 0; i < horizontalDists.Length; i++) {
            horizontalDists[i] = CollideBisectBottom(melvin.Scene, left + i, bottom, bottomStop);
        }

        prevX = left + playerCenterXOffset;
        prevY = bottom + playerCenterYOffset;
        prevLineX = prevX;
        prevLineUp = false;
        for (int currX = prevX; currX <= right; currX++) {
            int maxBottom = bottom + playerCenterYOffset;
            if (currX != right) {
                int currIdx = currX - left;
                int checkLeft = Math.Max(0, currIdx + centerToLeft);
                int checkRight = Math.Min(horizontalDists.Length, currIdx + centerToRight);
                for (int i = checkLeft; i < checkRight; i++) {
                    maxBottom = Math.Max(maxBottom, horizontalDists[i] + centerToBottom - 1);
                }
            }

            if (maxBottom != prevY) {
                // If the distance is now closer to the Melvin, the edge was on the previous pixel
                int currLineX = prevY > maxBottom ? currX - 1 : currX;
                Draw.Line(currLineX, Math.Max(prevY, maxBottom), currLineX, Math.Min(prevY, maxBottom), hitboxColor);

                bool currLineUp = prevY > maxBottom;
                if (currX != prevX) {
                    // Need to shift left/right depending on, if the other line is going up/down
                    Draw.Line(currLineX + (currLineUp ? 0 : 1), prevY, prevLineX + (prevLineUp ? 0 : 1), prevY, hitboxColor);
                }

                prevX = currX;
                prevY = maxBottom;
                prevLineX = currLineX;
                prevLineUp = currLineUp;
            }
        }
        Draw.Line(left + playerCenterXOffset, bottom + playerCenterYOffset, right, bottom + playerCenterYOffset, hitboxColor);
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
