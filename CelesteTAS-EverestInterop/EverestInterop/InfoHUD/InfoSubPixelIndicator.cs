using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoSubPixelIndicator {
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;
        private static float PixelScale => Engine.ViewWidth / 320f;

        public static void DrawIndicator(float y, float padding, float alpha) {
            if (!TasSettings.InfoSubPixelIndicator) {
                return;
            }

            float subPixelLeft = 0.5f;
            float subPixelRight = 0.5f;
            float subPixelTop = 0.5f;
            float subPixelBottom = 0.5f;

            Player player = Engine.Scene.Tracker.GetEntity<Player>();
            if (player != null) {
                subPixelLeft = player.PositionRemainder.X + 0.5f;
                subPixelRight = 1f - subPixelLeft;
                subPixelTop = player.PositionRemainder.Y + 0.5f;
                subPixelBottom = 1f - subPixelTop;
            }

            Vector2 textSize = GetSubPixelTextSize();
            float textWidth = textSize.X;
            float textHeight = textSize.Y;
            float rectSide = GetSubPixelRectSize();
            float x = TasSettings.InfoPosition.X + textWidth + padding * 2;
            y = y - rectSide - padding * 1.5f - textHeight;
            float thickness = PixelScale * TasSettings.InfoSubPixelIndicatorSize / 20f;
            DrawHollowRect(x, y, rectSide, rectSide, Color.Green * alpha, thickness);

            float pointSize = thickness * 1.5f;
            Draw.Rect(x + rectSide * subPixelLeft - pointSize / 2 + 1, y + rectSide * subPixelTop - pointSize / 2 + 1, pointSize, pointSize,
                Color.Red * alpha);

            JetBrainsMonoFont.Draw(subPixelLeft.ToString("F2"), new Vector2(x - textWidth - padding / 2f, y + (rectSide - textHeight) / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(subPixelRight.ToString("F2"), new Vector2(x + rectSide + padding / 2f, y + (rectSide - textHeight) / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(subPixelTop.ToString("F2"), new Vector2(x + (rectSide - textWidth) / 2f, y - textHeight - padding / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(subPixelBottom.ToString("F2"), new Vector2(x + (rectSide - textWidth) / 2f, y + rectSide + padding / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
        }

        public static Vector2 TryExpandSize(Vector2 size, float padding) {
            if (TasSettings.InfoSubPixelIndicator) {
                size.Y += GetSubPixelRectSize() + GetSubPixelTextSize().Y * 2 + padding * 2;
                if (!TasSettings.InfoGame && !TasSettings.InfoCustom && (!TasSettings.InfoTasInput || !Manager.Running)) {
                    size.Y -= padding;
                    size.X = GetSubPixelRectSize() + GetSubPixelTextSize().X * 2 + padding * 2;
                }
            }

            return size;
        }

        public static float GetHeight(float padding) {
            if (TasSettings.InfoSubPixelIndicator) {
                return GetSubPixelRectSize() + GetSubPixelTextSize().Y * 2 + padding * 2;
            } else {
                return 0f;
            }
        }

        private static float GetSubPixelRectSize() {
            if (TasSettings.InfoSubPixelIndicator) {
                return PixelScale * TasSettings.InfoSubPixelIndicatorSize;
            } else {
                return 0f;
            }
        }

        private static Vector2 GetSubPixelTextSize() {
            if (TasSettings.InfoSubPixelIndicator) {
                return JetBrainsMonoFont.Measure("0.00") * GetSubPixelFontSize();
            } else {
                return default;
            }
        }

        private static float GetSubPixelFontSize() {
            return 0.15f * PixelScale * TasSettings.InfoTextSize / 10f;
        }

        private static void DrawHollowRect(float left, float top, float width, float height, Color color, float thickness) {
            for (int i = 0; i < thickness; i++) {
                Draw.HollowRect(left + i, top + i, width - i * 2, height - i * 2, color);
            }
        }
    }
}