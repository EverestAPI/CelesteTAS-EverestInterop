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
        public static Vector2 Size { get; private set; }

        public static void Load() {
            On.Celeste.Level.Render += LevelOnRender;
            // avoid issues if center camera is enabled
            CenterCamera.Unload();
            CenterCamera.Load();

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

        private static void DrawInfo(Level level) {
            if (!TasSettings.Enabled || !TasSettings.InfoHud) {
                return;
            }

            StringBuilder stringBuilder = new();

            if (TasSettings.InfoTasInput) {
                WriteTasInput(stringBuilder);
            }

            string hudInfo = GameInfo.HudInfo;
            if (hudInfo.IsNotEmpty()) {
                if (stringBuilder.Length > 0) {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(hudInfo);
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

            Size = JetBrainsMonoFont.Measure(text) * fontSize;
            Size = InfoSubPixelIndicator.TryExpandSize(Size, padding);

            float maxX = viewWidth - Size.X - margin - padding * 2;
            float maxY = viewHeight - Size.Y - margin - padding * 2;
            if (maxY > 0f) {
                TasSettings.InfoPosition = TasSettings.InfoPosition.Clamp(margin, margin, maxX, maxY);
            }

            float x = TasSettings.InfoPosition.X;
            float y = TasSettings.InfoPosition.Y;

            Rectangle bgRect = new((int) x, (int) y, (int) (Size.X + padding * 2), (int) (Size.Y + padding * 2));

            if (level.GetPlayer() is { } player) {
                Vector2 playerPosition = level.Camera.CameraToScreen(player.TopLeft) * pixelScale;
                Rectangle playerRect = new((int) playerPosition.X, (int) playerPosition.Y, (int) (8 * pixelScale), (int) (11 * pixelScale));
                Rectangle mirrorBgRect = bgRect;
                if (SaveData.Instance?.Assists.MirrorMode == true) {
                    mirrorBgRect.X = (int) Math.Abs(x - viewWidth + Size.X + padding * 2);
                }

                if (level.Paused || playerRect.Intersects(mirrorBgRect)) {
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
                int maxWidth = currentStr.Length + controller.InputCurrentFrame.ToString().Length + 1;
                maxWidth = GameInfo.HudInfo.Split('\n').Select(s => s.Length).Concat(new[] {maxWidth}).Max();
                maxWidth = Math.Max(20, maxWidth);
                stringBuilder.AppendLine(
                    $"{currentStr.PadRight(maxWidth - controller.InputCurrentFrame.ToString().Length - 1)}{controller.InputCurrentFrame}");
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
                subMenu.Add(new TextMenuExt.EnumerableSlider<HudOptions>("Info Custom".ToDialogText(), CreateHudOptions(), TasSettings.InfoCustom)
                    .Change(value => TasSettings.InfoCustom = value));
                subMenu.Add(new TextMenu.Button("Info Copy Custom Template".ToDialogText()).Pressed(() =>
                    TextInput.SetClipboardText(TasSettings.InfoCustomTemplate ?? string.Empty)));
                subMenu.Add(new TextMenu.Button("Info Set Custom Template".ToDialogText()).Pressed(() => {
                    TasSettings.InfoCustomTemplate = TextInput.GetClipboardText() ?? string.Empty;
                    CelesteTasModule.Instance.SaveSettings();
                }));
                subMenu.Add(new TextMenuExt.EnumerableSlider<HudOptions>("Info Inspect Entity".ToDialogText(), CreateHudOptions(),
                    TasSettings.InfoInspectEntity).Change(value => TasSettings.InfoInspectEntity = value));
                subMenu.Add(new TextMenuExt.EnumerableSlider<InspectEntityTypes>("Info Inspect Entity Type".ToDialogText(), new[] {
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

        private static KeyValuePair<HudOptions, string>[] CreateHudOptions() {
            return new[] {
                new KeyValuePair<HudOptions, string>(HudOptions.Off, "Info HUD Options Off".ToDialogText()),
                new KeyValuePair<HudOptions, string>(HudOptions.HudOnly, "Info HUD Options Hud Only".ToDialogText()),
                new KeyValuePair<HudOptions, string>(HudOptions.StudioOnly, "Info HUD Options Studio Only".ToDialogText()),
                new KeyValuePair<HudOptions, string>(HudOptions.Both, "Info HUD Options Both".ToDialogText()),
            };
        }

        public static void AddSubMenuDescription(TextMenu menu) {
            subMenuItem.AddDescription(menu, "Info HUD Description 2".ToDialogText());
            subMenuItem.AddDescription(menu, "Info HUD Description 1".ToDialogText());
        }
    }

    [Flags]
    public enum HudOptions {
        Off = 0,
        HudOnly = 1,
        StudioOnly = 2,
        Both = HudOnly | StudioOnly
    }
}