using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoHud {
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

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
            JetBrainsMonoFont.LoadFont();
        }

        private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
            orig(self);

            DrawInfo(self);
            InfoMouse.DragAndDropHud();
        }

        private static void DrawInfo(Level self) {
            if (!TasSettings.Enabled || !TasSettings.InfoHud) {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();
            InputController controller = Manager.Controller;
            List<InputFrame> inputs = controller.Inputs;
            if (Manager.Running && controller.CurrentFrame >= 0 && controller.CurrentFrame < inputs.Count) {
                InputFrame previous = null;
                InputFrame next = null;

                InputFrame current = controller.Current;
                if (controller.CurrentFrame >= 1 && current != controller.Previous) {
                    current = controller.Previous;
                }

                int currentIndex = inputs.IndexOf(current);
                if (currentIndex >= 1) {
                    previous = inputs[currentIndex - 1];
                }

                currentIndex = inputs.LastIndexOf(current);
                if (currentIndex < inputs.Count - 1) {
                    next = inputs[currentIndex + 1];
                }

                int maxLine = Math.Max(current.Line, Math.Max(previous?.Line ?? 0, next?.Line ?? 0)) + 1;
                int linePadLeft = maxLine.ToString().Length;

                int maxFrames = Math.Max(current.Frames, Math.Max(previous?.Frames ?? 0, next?.Frames ?? 0));
                int framesPadLeft = maxFrames.ToString().Length;

                if (previous != null) {
                    stringBuilder.Append(
                        $"{(previous.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - previous.Frames.ToString().Length)}{previous}\n");
                }

                string currentStr =
                    $"{(current.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - current.Frames.ToString().Length)}{current}";
                int maxWidth = currentStr.Length + controller.StudioFrameCount.ToString().Length + 1;
                maxWidth = PlayerInfo.Status.Split('\n').Select(s => s.Length).Concat(new[] {maxWidth}).Max();
                stringBuilder.Append(
                    $"{currentStr.PadRight(maxWidth - controller.StudioFrameCount.ToString().Length - 1)}{controller.StudioFrameCount}\n");
                if (next != null) {
                    stringBuilder.Append(
                        $"{(next.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - next.Frames.ToString().Length)}{next}\n");
                }

                stringBuilder.AppendLine();
            }

            stringBuilder.Append(PlayerInfo.Status);

            string text = stringBuilder.ToString();
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            float pixelScale = viewWidth / 320f;
            float margin = 2 * pixelScale;
            float padding = 2 * pixelScale;
            float fontSize = 0.15f * pixelScale;
            float alpha = 1f;

            Vector2 size = JetBrainsMonoFont.Measure(text) * fontSize;

            TasSettings.InfoPosition =
                TasSettings.InfoPosition.Clamp(margin, margin, viewWidth - size.X - margin - padding * 2, viewHeight - size.Y - margin - padding * 2);

            float x = TasSettings.InfoPosition.X;
            float y = TasSettings.InfoPosition.Y;

            Rectangle bgRect = new Rectangle((int) x, (int) y, (int) (size.X + padding * 2), (int) (size.Y + padding * 2));

            if (self.GetPlayer() is Player player) {
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

            JetBrainsMonoFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White * alpha);

            Draw.SpriteBatch.End();
        }
    }
}