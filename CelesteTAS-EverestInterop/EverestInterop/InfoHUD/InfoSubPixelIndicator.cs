using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoSubPixelIndicator {
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;
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

            Player player = Engine.Scene.Tracker.GetEntity<Player>();
            if (player != null) {
                Vector2 positionRemainder = player.PositionRemainder;
                subPixelLeft = (float) Math.Round(positionRemainder.X + 0.5f, decimals);
                if (Math.Abs(player.PositionRemainder.X) < 0.5f) {
                    if (subPixelLeft == 0f) {
                        subPixelLeft += (float) Math.Pow(10, -decimals);
                    } else {
                        subPixelLeft -= (float) Math.Pow(10, -decimals);
                    }
                }

                subPixelRight = 1f - subPixelLeft;

                subPixelTop = (float) Math.Round(positionRemainder.Y + 0.5f, decimals);
                if (Math.Abs(player.PositionRemainder.Y) < 0.5f) {
                    if (subPixelTop == 0f) {
                        subPixelTop += (float) Math.Pow(10, -decimals);
                    } else {
                        subPixelTop -= (float) Math.Pow(10, -decimals);
                    }
                }

                subPixelBottom = 1f - subPixelTop;
            }

            Vector2 textSize = GetSubPixelTextSize();
            float textWidth = textSize.X;
            float textHeight = textSize.Y;
            float rectSide = GetSubPixelRectSize();
            float x = TasSettings.InfoPosition.X + textWidth + padding * 2;
            y = y - rectSide - padding * 1.5f - textHeight;
            float thickness = PixelScale * TasSettings.InfoSubpixelIndicatorSize / 20f;
            DrawHollowRect(x, y, rectSide, rectSide, Color.Green * alpha, thickness);

            float pointSize = thickness * 1.2f;
            Draw.Rect(x + (rectSide - pointSize) * subPixelLeft, y + (rectSide - pointSize) * subPixelTop, pointSize, pointSize,
                Color.Red * alpha);

            string format = $"F{decimals}";
            string left = subPixelLeft.ToString(Math.Abs(player?.PositionRemainder.X ?? 0) == 0.5f ? "F0" : format)
                .PadLeft(TasSettings.SubpixelIndicatorDecimals + 2, ' ');
            string right = subPixelRight.ToString(Math.Abs(player?.PositionRemainder.X ?? 0) == 0.5f ? "F0" : format);
            string top = subPixelTop.ToString(Math.Abs(player?.PositionRemainder.Y ?? 0) == 0.5f ? "F0" : format)
                .PadLeft(TasSettings.SubpixelIndicatorDecimals / 2 + 2, ' ');
            string bottom = subPixelBottom.ToString(Math.Abs(player?.PositionRemainder.Y ?? 0) == 0.5f ? "F0" : format)
                .PadLeft(TasSettings.SubpixelIndicatorDecimals / 2 + 2, ' ');

            JetBrainsMonoFont.Draw(left, new Vector2(x - textWidth - padding / 2f, y + (rectSide - textHeight) / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(right, new Vector2(x + rectSide + padding / 2f, y + (rectSide - textHeight) / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(top, new Vector2(x + (rectSide - textWidth) / 2f, y - textHeight - padding / 2f),
                Vector2.Zero, new Vector2(GetSubPixelFontSize()), Color.White * alpha);
            JetBrainsMonoFont.Draw(bottom, new Vector2(x + (rectSide - textWidth) / 2f, y + rectSide + padding / 2f),
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
            for (int i = 0; i < thickness; i++) {
                Draw.HollowRect(left + i, top + i, width - i * 2, height - i * 2, color);
            }
        }
    }
}