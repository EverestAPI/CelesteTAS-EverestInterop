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
            float fontSize = 0.25f * pixelScale;
            float alpha = 1f;

            if (!Manager.Running || (Manager.state | State.FrameStep) != State.FrameStep) {
                Manager.UpdatePlayerInfo();
            }

            if (string.IsNullOrEmpty(Manager.PlayerStatus)) {
                return;
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
                        y += 15 * pixelScale;
                    } else if (Settings.Instance.SpeedrunClock == SpeedrunType.File) {
                        y += 19 * pixelScale;
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
                    Draw.Rect(mirrorBgRect, Color.Black * 0.8f * alpha);
                }
                if (self.Paused || playerRect.Intersects(mirrorBgRect)) {
                    alpha = 0.5f;
                }
            }

            Draw.SpriteBatch.Begin();

            Draw.Rect(bgRect, Color.Black * 0.8f * alpha);

            Vector2 textPosition = new Vector2(x + padding, y + padding);
            Vector2 scale = new Vector2(fontSize);

            Draw.SpriteBatch.DrawString(Draw.DefaultFont, text, textPosition, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

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