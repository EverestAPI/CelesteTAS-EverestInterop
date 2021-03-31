using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoHud {
        private static TextMenu.Item subMenuItem;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;
        private static float PixelScale => Engine.ViewWidth / 320f;

        public static void Load() {
            On.Celeste.Level.Render += LevelOnRender;
            On.Celeste.Fonts.Prepare += FontsOnPrepare;
            InfoInspectEntity.Load();
        }

        public static void Unload() {
            On.Celeste.Level.Render -= LevelOnRender;
            On.Celeste.Fonts.Prepare -= FontsOnPrepare;
            InfoInspectEntity.Unload();
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

            if (TasSettings.InfoTasInput) {
                WriteTasInput(stringBuilder);
            }

            if (TasSettings.InfoGame) {
                if (stringBuilder.Length > 0) {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(GameInfo.Status);
            }

            string text = stringBuilder.ToString().Trim();
            if (string.IsNullOrEmpty(text) && !TasSettings.InfoSubPixelIndicator) {
                return;
            }

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            float pixelScale = PixelScale;
            float margin = 2 * pixelScale;
            float padding = 2 * pixelScale;
            float fontSize = 0.15f * pixelScale * TasSettings.InfoTextSize / 10f;
            float alpha = TasSettings.InfoOpacity / 10f;
            float infoAlpha = 1f;

            Vector2 size = JetBrainsMonoFont.Measure(text) * fontSize;

            TasSettings.InfoPosition =
                TasSettings.InfoPosition.Clamp(margin, margin, viewWidth - size.X - margin - padding * 2, viewHeight - size.Y - margin - padding * 2);

            float x = TasSettings.InfoPosition.X;
            float y = TasSettings.InfoPosition.Y;

            Rectangle bgRect = new Rectangle((int) x, (int) y, (int) (size.X + padding * 2), (int) (size.Y + padding * 2));
            if (TasSettings.InfoSubPixelIndicator) {
                bgRect.Height += (int) (GetSubPixelRectSize() + GetSubPixelTextSize().Y * 2 + padding * 2);
                if (!TasSettings.InfoGame && !TasSettings.InfoTasInput) {
                    bgRect.Height -= (int) padding;
                    bgRect.Width = (int) (GetSubPixelRectSize() + GetSubPixelTextSize().X * 2 + padding * 4);
                }
            }

            if (self.GetPlayer() is Player player) {
                Vector2 playerPosition = self.Camera.CameraToScreen(player.TopLeft) * pixelScale;
                Rectangle playerRect = new Rectangle((int) playerPosition.X, (int) playerPosition.Y, (int) (8 * pixelScale), (int) (11 * pixelScale));
                Rectangle mirrorBgRect = bgRect;
                if (SaveData.Instance?.Assists.MirrorMode == true) {
                    mirrorBgRect.X = (int) Math.Abs(x - viewWidth + size.X + padding * 2);
                }

                if (self.Paused || playerRect.Intersects(mirrorBgRect)) {
                    alpha *= TasSettings.InfoMaskedOpacity / 10f;
                    infoAlpha *= alpha;
                }
            }

            Draw.SpriteBatch.Begin();

            Draw.Rect(bgRect, Color.Black * alpha);

            DrawSubPixel(bgRect.Bottom, padding, infoAlpha);

            Vector2 textPosition = new Vector2(x + padding, y + padding);
            Vector2 scale = new Vector2(fontSize);

            JetBrainsMonoFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White * infoAlpha);

            Draw.SpriteBatch.End();
        }

        private static void WriteTasInput(StringBuilder stringBuilder) {
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
                    stringBuilder.AppendLine(
                        $"{(previous.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - previous.Frames.ToString().Length)}{previous}");
                }

                string currentStr =
                    $"{(current.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - current.Frames.ToString().Length)}{current}";
                int maxWidth = currentStr.Length + controller.StudioFrameCount.ToString().Length + 1;
                maxWidth = GameInfo.Status.Split('\n').Select(s => s.Length).Concat(new[] {maxWidth}).Max();
                stringBuilder.AppendLine(
                    $"{currentStr.PadRight(maxWidth - controller.StudioFrameCount.ToString().Length - 1)}{controller.StudioFrameCount}");
                if (next != null) {
                    stringBuilder.AppendLine(
                        $"{(next.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - next.Frames.ToString().Length)}{next}");
                }
            }
        }

        private static void DrawSubPixel(float y, float padding, float alpha) {
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
            Draw.Rect(x + rectSide * subPixelLeft - thickness, y + rectSide * subPixelTop - thickness, thickness * 2, thickness * 2,
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

        public static TextMenu.Item CreateSubMenu() {
            subMenuItem = new TextMenuExt.SubMenu("Info HUD".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), TasSettings.InfoHud).Change(value => TasSettings.InfoHud = value));
                subMenu.Add(new TextMenu.OnOff("Info Game".ToDialogText(), TasSettings.InfoGame).Change(value => TasSettings.InfoGame = value));
                subMenu.Add(new TextMenu.OnOff("Info TAS Input".ToDialogText(), TasSettings.InfoTasInput).Change(value =>
                    TasSettings.InfoTasInput = value));
                subMenu.Add(new TextMenu.OnOff("Info Sub Pixel Indicator".ToDialogText(), TasSettings.InfoSubPixelIndicator).Change(value =>
                    TasSettings.InfoSubPixelIndicator = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Text Size".ToDialogText(), 5, 20, TasSettings.InfoTextSize).Change(value =>
                    TasSettings.InfoTextSize = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Sub Pixel Indicator Size".ToDialogText(), 5, 20, TasSettings.InfoSubPixelIndicatorSize)
                    .Change(value =>
                        TasSettings.InfoSubPixelIndicatorSize = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Opacity".ToDialogText(), 1, 10, TasSettings.InfoOpacity).Change(value =>
                    TasSettings.InfoOpacity = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Masked Opacity".ToDialogText(), 0, 10, TasSettings.InfoMaskedOpacity).Change(value =>
                    TasSettings.InfoMaskedOpacity = value));
            });
            return subMenuItem;
        }

        public static void AddSubMenuDescription(TextMenu menu) {
            subMenuItem.AddDescription(menu, "Info HUD Description".ToDialogText());
        }
    }
}