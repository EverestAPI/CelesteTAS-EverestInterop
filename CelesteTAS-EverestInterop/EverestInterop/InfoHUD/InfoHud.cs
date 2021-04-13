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
            InfoMouse.ToggleAndDrag();
        }

        private static void DrawInfo(Level self) {
            if (!TasSettings.Enabled || !TasSettings.InfoHud) {
                return;
            }

            StringBuilder stringBuilder = new();

            if (TasSettings.InfoTasInput) {
                WriteTasInput(stringBuilder);
            }

            if (TasSettings.InfoGame) {
                if (stringBuilder.Length > 0) {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(GameInfo.Status);
            } else if (TasSettings.InfoCustom) {
                if (stringBuilder.Length > 0) {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(GameInfo.CustomInfo);
            }


            string text = stringBuilder.ToString().Trim();
            if (string.IsNullOrEmpty(text) && !TasSettings.InfoSubPixelIndicator) {
                return;
            }

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            float pixelScale = Engine.ViewWidth / 320f;
            float margin = 2 * pixelScale;
            float padding = 2 * pixelScale;
            float fontSize = 0.15f * pixelScale * TasSettings.InfoTextSize / 10f;
            float alpha = TasSettings.InfoOpacity / 10f;
            float infoAlpha = 1f;

            Vector2 size = JetBrainsMonoFont.Measure(text) * fontSize;
            size = InfoSubPixelIndicator.TryExpandSize(size, padding);

            TasSettings.InfoPosition = TasSettings.InfoPosition.Clamp(margin, margin, viewWidth - size.X - margin - padding * 2,
                viewHeight - size.Y - margin - padding * 2);

            float x = TasSettings.InfoPosition.X;
            float y = TasSettings.InfoPosition.Y;

            Rectangle bgRect = new((int) x, (int) y, (int) (size.X + padding * 2), (int) (size.Y + padding * 2));

            if (self.GetPlayer() is { } player) {
                Vector2 playerPosition = self.Camera.CameraToScreen(player.TopLeft) * pixelScale;
                Rectangle playerRect = new((int) playerPosition.X, (int) playerPosition.Y, (int) (8 * pixelScale), (int) (11 * pixelScale));
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

            InfoSubPixelIndicator.DrawIndicator(bgRect.Bottom, padding, infoAlpha);

            Vector2 textPosition = new(x + padding, y + padding);
            Vector2 scale = new(fontSize);

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

        public static TextMenu.Item CreateSubMenu() {
            subMenuItem = new TextMenuExt.SubMenu("Info HUD".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), TasSettings.InfoHud).Change(value => TasSettings.InfoHud = value));
                subMenu.Add(new TextMenu.OnOff("Info Game".ToDialogText(), TasSettings.InfoGame).Change(value => TasSettings.InfoGame = value));
                subMenu.Add(new TextMenu.OnOff("Info TAS Input".ToDialogText(), TasSettings.InfoTasInput).Change(value =>
                    TasSettings.InfoTasInput = value));
                subMenu.Add(new TextMenu.OnOff("Info Subpixel Indicator".ToDialogText(), TasSettings.InfoSubPixelIndicator).Change(value =>
                    TasSettings.InfoSubPixelIndicator = value));
                subMenu.Add(new TextMenu.OnOff("Info Custom".ToDialogText(), TasSettings.InfoCustom).Change(value => TasSettings.InfoCustom = value));
                subMenu.Add(new TextMenu.Button("Info Copy Custom Template".ToDialogText()).Pressed(() =>
                    TextInput.SetClipboardText(TasSettings.InfoCustomTemplate)));
                subMenu.Add(new TextMenu.Button("Info Set Custom Template".ToDialogText()).Pressed(() => {
                    TasSettings.InfoCustomTemplate = TextInput.GetClipboardText();
                    CelesteTasModule.Instance.SaveSettings();
                }));
                subMenu.Add(new TextMenuExt.EnumerableSlider<InspectEntityTypes>("Info Inspect Entity".ToDialogText(), new[] {
                    new KeyValuePair<InspectEntityTypes, string>(InspectEntityTypes.Position, "Info Inspect Entity Position".ToDialogText()),
                    new KeyValuePair<InspectEntityTypes, string>(InspectEntityTypes.DeclaredOnly, "Info Inspect Entity Declared Only".ToDialogText()),
                    new KeyValuePair<InspectEntityTypes, string>(InspectEntityTypes.All, "Info Inspect Entity All".ToDialogText()),
                }, TasSettings.InfoInspectEntityType).Change(value => TasSettings.InfoInspectEntityType = value));
                subMenu.Add(new TextMenu.OnOff("Info Ignore Trigger When Click Entity".ToDialogText(), TasSettings.InfoIgnoreTriggerWhenClickEntity)
                    .Change(value => TasSettings.InfoIgnoreTriggerWhenClickEntity = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Text Size".ToDialogText(), 5, 20, TasSettings.InfoTextSize).Change(value =>
                    TasSettings.InfoTextSize = value));
                subMenu.Add(new TextMenuExt.IntSlider("Info Subpixel Indicator Size".ToDialogText(), 5, 20, TasSettings.InfoSubPixelIndicatorSize)
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
            subMenuItem.AddDescription(menu, "Info HUD Description 2".ToDialogText());
            subMenuItem.AddDescription(menu, "Info HUD Description 1".ToDialogText());
        }
    }
}