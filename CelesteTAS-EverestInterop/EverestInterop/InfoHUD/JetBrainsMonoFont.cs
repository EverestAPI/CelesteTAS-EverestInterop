using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.InfoHUD {
    // Copy of ActiveFont that always uses the JetBrains Mono font.
    public static class JetBrainsMonoFont {
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