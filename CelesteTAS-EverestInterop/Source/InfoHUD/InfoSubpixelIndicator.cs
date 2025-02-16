using Celeste;
using Celeste.Pico8;
using Monocle;
using System;
using System.Linq;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Renderer for the Info HUD to display the player's current subpixels
internal static class InfoSubpixelIndicator {
    public static readonly WindowManager.Renderer Renderer = new(IsVisible, GetSize, Render);

    private static bool IsVisible() => TasSettings.InfoSubpixelIndicator;

    private static Vector2 GetSize() {
        var textScale = new Vector2(TasSettings.InfoSubpixelIndicatorSize / 10.0f);
        var textSize = WindowManager.MeasureText("0.".PadRight(TasSettings.SubpixelIndicatorDecimals + 2, '0')) * textScale;
        float rectSide = WindowManager.PixelSize * TasSettings.InfoSubpixelIndicatorSize;
        float padding = WindowManager.Padding;

        return new Vector2((textSize.X + padding) * 2.0f + rectSide, (textSize.Y + padding) * 2.0f + rectSide);
    }

    private static void Render(Vector2 position, float alpha) {
        var remainder = Vector2.Zero;
        if (Engine.Scene.GetPlayer() is { } player) {
            remainder = player.movementCounter;
        } else if (Engine.Scene is Emulator emulator && emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            remainder = classicPlayer.rem;
        }

        int decimals = TasSettings.SubpixelIndicatorDecimals;

        // Hide decimals when exactly on the edge
        int hDecimals = Math.Abs(remainder.X) switch {
            0.5f or -0.5f => 0,
            _ => decimals
        };
        int vDecimals = Math.Abs(remainder.Y) switch {
            0.5f or -0.5f => 0,
            _ => decimals
        };

        float subPixelLeft   = (float) Math.Round(remainder.X + 0.5f, hDecimals, MidpointRounding.AwayFromZero);
        float subPixelTop    = (float) Math.Round(remainder.Y + 0.5f, vDecimals, MidpointRounding.AwayFromZero);
        float subPixelRight  = 1.0f - subPixelLeft;
        float subPixelBottom = 1.0f - subPixelTop;

        string left   = subPixelLeft.FormatValue(hDecimals);
        string right  = subPixelRight.FormatValue(hDecimals);
        string top    = subPixelTop.FormatValue(vDecimals);
        string bottom = subPixelBottom.FormatValue(vDecimals);

        var textScale = new Vector2(TasSettings.InfoSubpixelIndicatorSize / 10.0f);
        var textSize = WindowManager.MeasureText("0.".PadRight(TasSettings.SubpixelIndicatorDecimals + 2, '0')) * textScale;
        float rectSide = WindowManager.PixelSize * TasSettings.InfoSubpixelIndicatorSize;
        float padding = WindowManager.Padding;

        WindowManager.DrawText(top,    new Vector2(position.X + textSize.X + rectSide / 2.0f + padding, position.Y + textSize.Y),                             new Vector2(0.5f, 1.0f), textScale, Color.White * alpha);
        WindowManager.DrawText(bottom, new Vector2(position.X + textSize.X + rectSide / 2.0f + padding, position.Y + textSize.Y + rectSide + padding * 2.0f), new Vector2(0.5f, 0.0f), textScale, Color.White * alpha);
        WindowManager.DrawText(left,   new Vector2(position.X + textSize.X,                             position.Y + textSize.Y + rectSide / 2.0f + padding), new Vector2(1.0f, 0.5f), textScale, Color.White * alpha);
        WindowManager.DrawText(right,  new Vector2(position.X + textSize.X + rectSide + padding * 2.0f, position.Y + textSize.Y + rectSide / 2.0f + padding), new Vector2(0.0f, 0.5f), textScale, Color.White * alpha);

        int thickness = Math.Max(1, (int) Math.Round(WindowManager.PixelSize * TasSettings.InfoSubpixelIndicatorSize / 20.0f));

        DrawHollowRect(
            (int) (position.X + textSize.X + padding),
            (int) (position.Y + textSize.Y + padding),
            (int) rectSide, (int) rectSide, thickness, Color.Green * alpha);
        Draw.Rect(
            position.X + textSize.X + padding + (rectSide - thickness) * subPixelLeft,
            position.Y + textSize.Y + padding + (rectSide - thickness) * subPixelTop,
            thickness, thickness,
            Color.Red * alpha);
    }

    private static void DrawHollowRect(int x, int y, int width, int height, int thickness, Color color) {
        Draw.Rect(x, y,                      width, thickness, color);
        Draw.Rect(x, y + height - thickness, width, thickness, color);
        Draw.Rect(x,                     y + thickness, thickness, height - thickness * 2, color);
        Draw.Rect(x + width - thickness, y + thickness, thickness, height - thickness * 2, color);
    }
}
