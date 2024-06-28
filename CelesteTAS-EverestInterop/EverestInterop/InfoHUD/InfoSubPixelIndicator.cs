using System;
using System.Linq;
using Celeste;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static class InfoSubPixelIndicator {
    private static float PixelScale => Engine.ViewWidth / 320f;

    public static void DrawIndicator(float y, float padding, float alpha) {
        if (!TasSettings.InfoSubpixelIndicator) {
            return;
        }

        float subPixelLeft = 0.5f;
        float subPixelRight = 0.5f;
        float subPixelTop = 0.5f;
        float subPixelBottom = 0.5f;
        int decimals = TasSettings.SubpixelIndicatorDecimals;

        Vector2 remainder = Engine.Scene.Tracker.GetEntity<Player>()?.movementCounter ?? Vector2.Zero;
        if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
            remainder = player.movementCounter;
        } else if (Engine.Scene is Emulator emulator && emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            remainder = classicPlayer.rem;
        }

        subPixelLeft = (float) Math.Round(remainder.X + 0.5f, decimals, MidpointRounding.AwayFromZero);
        subPixelTop = (float) Math.Round(remainder.Y + 0.5f, decimals, MidpointRounding.AwayFromZero);
        subPixelRight = 1f - subPixelLeft;
        subPixelBottom = 1f - subPixelTop;

        Vector2 textSize = GetSubPixelTextSize();
        float textWidth = textSize.X;
        float textHeight = textSize.Y;
        float rectSide = GetSubPixelRectSize();
        float x = TasSettings.InfoPosition.X + textWidth + padding * 2;
        y = y - rectSide - padding * 1.5f - textHeight;
        int thickness = Math.Max(1, (int) Math.Round(PixelScale * TasSettings.InfoSubpixelIndicatorSize / 20f));
        DrawHollowRect(x, y, rectSide, rectSide, Color.Green * alpha, thickness);
        Draw.Rect(x + (rectSide - thickness) * subPixelLeft, y + (rectSide - thickness) * subPixelTop, thickness, thickness,
            Color.Red * alpha);

        int hDecimals = Math.Abs(remainder.X) switch {
            0.5f => 0,
            _ => decimals
        };
        int vDecimals = Math.Abs(remainder.Y) switch {
            0.5f => 0,
            _ => decimals
        };

        string left = subPixelLeft.ToFormattedString(hDecimals).PadLeft(TasSettings.SubpixelIndicatorDecimals + 2, ' ');
        string right = subPixelRight.ToFormattedString(hDecimals);
        string top = subPixelTop.ToFormattedString(vDecimals).PadLeft(TasSettings.SubpixelIndicatorDecimals / 2 + 2, ' ');
        string bottom = subPixelBottom.ToFormattedString(vDecimals).PadLeft(TasSettings.SubpixelIndicatorDecimals / 2 + 2, ' ');

        JetBrainsMonoFont.Draw(left, new Vector2(x - textWidth - padding / 2f, y + (rectSide - textHeight) / 2f),
            Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
        JetBrainsMonoFont.Draw(right, new Vector2(x + rectSide + padding / 2f, y + (rectSide - textHeight) / 2f),
            Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);

        float tweakX = vDecimals == 0 && decimals % 2 == 0 ? padding / 2 : 0;
        JetBrainsMonoFont.Draw(top, new Vector2(x + (rectSide - textWidth) / 2f - tweakX, y - textHeight - padding / 2f),
            Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
        JetBrainsMonoFont.Draw(bottom, new Vector2(x + (rectSide - textWidth) / 2f - tweakX, y + rectSide + padding / 2f),
            Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
    }

    public static Vector2 TryExpandSize(Vector2 size, float padding) {
        if (TasSettings.InfoSubpixelIndicator) {
            if (size.Y == 0) {
                size.Y = -padding;
            }

            size.Y += GetSubPixelRectSize() + GetSubPixelTextSize().Y * 2 + padding * 2;
            size.X = Math.Max(size.X, GetSubPixelRectSize() + GetSubPixelTextSize().X * 2 + padding * 2);
        }

        return size;
    }

    private static float GetSubPixelRectSize() {
        if (TasSettings.InfoSubpixelIndicator) {
            return PixelScale * TasSettings.InfoSubpixelIndicatorSize;
        } else {
            return 0f;
        }
    }

    private static Vector2 GetSubPixelTextSize() {
        if (TasSettings.InfoSubpixelIndicator) {
            return JetBrainsMonoFont.Measure("0.".PadRight(TasSettings.SubpixelIndicatorDecimals + 2, '0')) * GetSubPixelFontSize();
        } else {
            return default;
        }
    }

    private static float GetSubPixelFontSize() {
        return 0.15f * PixelScale * TasSettings.InfoTextSize / 10f;
    }

    private static void DrawHollowRect(float left, float top, float width, float height, Color color, float thickness) {
        Draw.Rect(left, top, width, thickness, color);
        Draw.Rect(left, top, thickness, height, color);
        Draw.Rect(left + width - thickness, top, thickness, height, color);
        Draw.Rect(left, top + height - thickness, width, thickness, color);
    }
}