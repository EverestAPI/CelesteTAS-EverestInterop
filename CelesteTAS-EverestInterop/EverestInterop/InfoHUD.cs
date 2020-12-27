using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
public static class InfoHUD {
        private static CelesteTASModuleSettings ModSettings => CelesteTASModule.Settings;

        public static void Load() {
            On.Celeste.Level.Render += LevelOnRender;
        }

        public static void Unload() {
            On.Celeste.Level.Render -= LevelOnRender;
        }

        private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
            orig(self);

            if (!ModSettings.Enabled || !ModSettings.InfoHUD) {
                return;
            }

            Draw.SpriteBatch.Begin();

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            int margin = 20;
            int padding = 20;

            string text = Manager.PlayerStatus;
            float fontSize = 0.5f;
            Vector2 size = ActiveFont.Measure(text) * fontSize;

            float x;
            float y;
            switch (ModSettings.InfoPosition) {
            case InfoPositions.TopLeft:
                    x = margin;
                    y = margin;
                    if (Settings.Instance.SpeedrunClock == SpeedrunType.Chapter) {
                        y += 130;
                    } else if (Settings.Instance.SpeedrunClock == SpeedrunType.File) {
                        y += 160;
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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Vector2 rectPosition = new Vector2(x, y);
            Draw.Rect(rectPosition, size.X + padding * 2, size.Y + padding * 2 , Color.Black * 0.7f);

            Vector2 textPosition = new Vector2(x + padding, y + padding);
            Vector2 scale = new Vector2(fontSize);
            ActiveFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White);

            Draw.SpriteBatch.End();
        }
    }

    public enum InfoPositions {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}