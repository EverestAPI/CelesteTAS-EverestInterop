using System;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        // TODO Move the HUD if the player is obscured by the HUD
        private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
            orig(self);

            if (!ModSettings.Enabled || ModSettings.InfoHUD == InfoPositions.OFF) {
                return;
            }

            Draw.SpriteBatch.Begin();

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            float windowScale = viewHeight / 1920f;
            float margin = 30 * windowScale;
            float padding = 30 * windowScale;
            float fontSize = 3f * windowScale;

            if (!Manager.Running || (Manager.state | State.FrameStep) != State.FrameStep) {
                Manager.UpdatePlayerInfo();
            }
            string text = Manager.PlayerStatus;
            Vector2 size = Draw.DefaultFont.MeasureString(text) * fontSize;

            float x;
            float y;
            switch (ModSettings.InfoHUD) {
                case InfoPositions.TopLeft:
                        x = margin;
                        y = margin;
                        if (Settings.Instance.SpeedrunClock == SpeedrunType.Chapter) {
                            y += 170 * windowScale;
                        } else if (Settings.Instance.SpeedrunClock == SpeedrunType.File) {
                            y += 210 * windowScale;
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

            Vector2 rectPosition = new Vector2(x, y);
            Draw.Rect(rectPosition, size.X + padding * 2, size.Y + padding * 2 , Color.Black * 0.8f);

            Vector2 textPosition = new Vector2(x + padding, y + padding);
            Vector2 scale = new Vector2(fontSize);

            Draw.SpriteBatch.DrawString(Draw.DefaultFont, text, textPosition, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

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
}