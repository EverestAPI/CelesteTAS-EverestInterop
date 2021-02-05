using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
public static class InfoHUD {
    private static CelesteTASModuleSettings ModSettings => CelesteTASModule.Settings;

    public static void Load() {
        On.Celeste.Level.Render += LevelOnRender;
        On.Celeste.Fonts.Prepare += FontsOnPrepare;
    }

    public static void Unload() {
        On.Celeste.Level.Render -= LevelOnRender;
        On.Celeste.Fonts.Prepare -= FontsOnPrepare;
    }

    private static void FontsOnPrepare(On.Celeste.Fonts.orig_Prepare orig) {
        orig();
        JetBrainsMono.LoadFont();
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        if (!ModSettings.Enabled || ModSettings.InfoHUD == InfoPositions.OFF) {
            return;
        }

        int viewWidth = Engine.ViewWidth;
        int viewHeight = Engine.ViewHeight;

        float pixelScale = viewWidth / 320f;
        float margin = 2 * pixelScale;
        float padding = 2 * pixelScale;
        float fontSize = 0.15f * pixelScale;
        float alpha = 1f;

        string text = Manager.PlayerStatus;

        if (string.IsNullOrEmpty(text)) {
            return;
        }

        Vector2 size = JetBrainsMono.Measure(text) * fontSize;

        float x;
        float y;
        switch (ModSettings.InfoHUD) {
            case InfoPositions.TopLeft:
                x = margin;
                y = margin;
                if (Settings.Instance.SpeedrunClock == SpeedrunType.Chapter) {
                    y += 16 * pixelScale;
                } else if (Settings.Instance.SpeedrunClock == SpeedrunType.File) {
                    y += 20 * pixelScale;
                }

                break;
            case InfoPositions.TopRight:
                x = viewWidth - size.X - margin - padding * 2;
                y = margin;
                break;
            case InfoPositions.BottomLeft:
                x = margin;
                y = viewHeight - size.Y - margin - padding * 2;
                break;
            case InfoPositions.BottomRight:
                x = viewWidth - size.X - margin - padding * 2;
                y = viewHeight - size.Y - margin - padding * 2;
                break;
            case InfoPositions.OFF:
                throw new ArgumentException();
            default:
                throw new ArgumentOutOfRangeException();
        }

        Rectangle bgRect = new Rectangle((int) x, (int) y, (int) (size.X + padding * 2), (int) (size.Y + padding * 2));

        if (self.Entities.FindFirst<Player>() is Player player) {
            Vector2 playerPosition = self.Camera.CameraToScreen(player.TopLeft) * pixelScale;
            Rectangle playerRect = new Rectangle((int) playerPosition.X, (int) playerPosition.Y, (int) (8 * pixelScale), (int) (11 * pixelScale));
            Rectangle mirrorBgRect = bgRect;
            if (SaveData.Instance?.Assists.MirrorMode == true) {
                mirrorBgRect.X = (int) Math.Abs(x - viewWidth + size.X + padding * 2);
            }

            if (self.Paused || playerRect.Intersects(mirrorBgRect)) {
                alpha = 0.5f;
            }
        }

        Draw.SpriteBatch.Begin();

        Draw.Rect(bgRect, Color.Black * 0.8f * alpha);

        Vector2 textPosition = new Vector2(x + padding, y + padding);
        Vector2 scale = new Vector2(fontSize);

        JetBrainsMono.Draw(text, textPosition, Vector2.Zero, scale, Color.White * alpha);

        Draw.SpriteBatch.End();
    }
}

public enum InfoPositions {
    OFF,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

// Copy of ActiveFont that always uses the JetBrains Mono font.
public static class JetBrainsMono {
    private const string FontFace = "JetBrains Mono";
    public static PixelFont Font => Fonts.Get(FontFace) ?? LoadFont();

    public static PixelFontSize FontSize => Font.Get(BaseSize);

    public static float BaseSize => 32;

    public static float LineHeight => FontSize.LineHeight;

    public static PixelFont LoadFont() {
        return Fonts.Load(FontFace);
    }

    public static Vector2 Measure(char text)
        => FontSize.Measure(text);

    public static Vector2 Measure(string text)
        => FontSize.Measure(text);

    public static float WidthToNextLine(string text, int start)
        => FontSize.WidthToNextLine(text, start);

    public static float HeightOf(string text)
        => FontSize.HeightOf(text);

    public static void Draw(char character, Vector2 position, Vector2 justify, Vector2 scale, Color color)
        => Font.Draw(BaseSize, character, position, justify, scale, color);

    private static void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float edgeDepth, Color edgeColor,
        float stroke, Color strokeColor)
        => Font.Draw(BaseSize, text, position, justify, scale, color, edgeDepth, edgeColor, stroke, strokeColor);

    public static void Draw(string text, Vector2 position, Color color)
        => Draw(text, position, Vector2.Zero, Vector2.One, color, 0f, Color.Transparent, 0f, Color.Transparent);

    public static void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color)
        => Draw(text, position, justify, scale, color, 0f, Color.Transparent, 0f, Color.Transparent);

    public static void DrawOutline(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float stroke, Color strokeColor)
        => Draw(text, position, justify, scale, color, 0f, Color.Transparent, stroke, strokeColor);

    public static void DrawEdgeOutline(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float edgeDepth,
        Color edgeColor, float stroke = 0f, Color strokeColor = default)
        => Draw(text, position, justify, scale, color, edgeDepth, edgeColor, stroke, strokeColor);
}
}