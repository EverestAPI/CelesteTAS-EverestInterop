using System;
using System.Collections.Generic;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Entities;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static class InfoHud {
    private static EaseInSubMenu subMenuItem;
    public static Vector2 Size { get; private set; }

    [Load]
    private static void Load() {
        On.Celeste.Level.Render += LevelOnRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Render -= LevelOnRender;
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        DrawInfo(self);
        InfoMouse.DragAndDropHud();
    }

    public static void Toggle() {
        if (Hotkeys.InfoHud.DoublePressed) {
            TasSettings.InfoHud = !TasSettings.InfoHud;

            if (TasSettings.InfoHud && TasSettings.EnableInfoHudFirstTime) {
                TasSettings.EnableInfoHudFirstTime = false;
                Toast.Show($"Info HUD is provided by TAS Mod\nDouble press {Hotkeys.InfoHud} to toggle it", 5);
            }

            CelesteTasModule.Instance.SaveSettings();
        }
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
        if (string.IsNullOrEmpty(text) && !TasSettings.InfoSubpixelIndicator) {
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

        if (!Hotkeys.InfoHud.Check && ((level.Paused && !Celeste.Input.MenuJournal.Check) || CollidePlayer(level, bgRect))) {
            alpha *= TasSettings.InfoMaskedOpacity / 10f;
            infoAlpha *= alpha;
        }

        Draw.SpriteBatch.Begin();

        Draw.Rect(bgRect, Color.Black * alpha);

        InfoSubPixelIndicator.DrawIndicator(bgRect.Bottom, padding, infoAlpha);

        Vector2 textPosition = new(x + padding, y + padding);
        Vector2 scale = new(fontSize);

        JetBrainsMonoFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White * infoAlpha);

        Draw.SpriteBatch.End();
    }

    private static bool CollidePlayer(Level level, Rectangle bgRect) {
        if (level.GetPlayer() is not { } player) {
            return false;
        }

        Vector2 playerTopLeft = level.WorldToScreen(player.TopLeft) / Engine.Width * Engine.ViewWidth;
        Vector2 playerBottomRight = level.WorldToScreen(player.BottomRight) / Engine.Width * Engine.ViewWidth;
        Rectangle playerRect = new(
            (int) Math.Min(playerTopLeft.X, playerBottomRight.X),
            (int) Math.Min(playerTopLeft.Y, playerBottomRight.Y),
            (int) Math.Abs(playerTopLeft.X - playerBottomRight.X),
            (int) Math.Abs(playerTopLeft.Y - playerBottomRight.Y)
        );

        return playerRect.Intersects(bgRect);
    }

    private static void WriteTasInput(StringBuilder stringBuilder) {
        InputController controller = Manager.Controller;
        List<InputFrame> inputs = controller.Inputs;
        if (Manager.Running && controller.CurrentFrameInTas >= 0 && controller.CurrentFrameInTas < inputs.Count) {
            InputFrame current = controller.Current;
            if (controller.CurrentFrameInTas >= 1 && current != controller.Previous) {
                current = controller.Previous;
            }

            InputFrame previous = current.Previous;
            InputFrame next = current.Next;

            int maxLine = Math.Max(current.Line, Math.Max(previous?.Line ?? 0, next?.Line ?? 0)) + 1;
            int linePadLeft = maxLine.ToString().Length;

            int maxFrames = Math.Max(current.Frames, Math.Max(previous?.Frames ?? 0, next?.Frames ?? 0));
            int framesPadLeft = maxFrames.ToString().Length;

            string FormatInputFrame(InputFrame inputFrame) {
                return
                    $"{(inputFrame.Line + 1).ToString().PadLeft(linePadLeft)}: {string.Empty.PadLeft(framesPadLeft - inputFrame.Frames.ToString().Length)}{inputFrame}";
            }

            if (previous != null) {
                stringBuilder.AppendLine(FormatInputFrame(previous));
            }

            string currentStr = FormatInputFrame(current);
            int currentFrameLength = controller.CurrentFrameInInput.ToString().Length;
            int inputWidth = currentStr.Length + currentFrameLength + 2;
            inputWidth = Math.Max(inputWidth, 20);
            stringBuilder.AppendLine(
                $"{currentStr.PadRight(inputWidth - currentFrameLength)}{controller.CurrentFrameInInputForHud}{current.RepeatString}");

            if (next != null) {
                stringBuilder.AppendLine(FormatInputFrame(next));
            }
        }
    }

    public static EaseInSubMenu CreateSubMenu() {
        subMenuItem = new EaseInSubMenu("Info HUD".ToDialogText(), false).Apply(subMenu => {
            subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), TasSettings.InfoHud).Change(value => TasSettings.InfoHud = value));
            subMenu.Add(new TextMenu.OnOff("Info Game".ToDialogText(), TasSettings.InfoGame).Change(value => TasSettings.InfoGame = value));
            subMenu.Add(new TextMenu.OnOff("Info TAS Input".ToDialogText(), TasSettings.InfoTasInput).Change(value =>
                TasSettings.InfoTasInput = value));
            subMenu.Add(new TextMenu.OnOff("Info Subpixel Indicator".ToDialogText(), TasSettings.InfoSubpixelIndicator).Change(value =>
                TasSettings.InfoSubpixelIndicator = value));
            subMenu.Add(new TextMenuExt.EnumerableSlider<HudOptions>("Info Custom".ToDialogText(), CreateHudOptions(), TasSettings.InfoCustom)
                .Change(value => TasSettings.InfoCustom = value));
            subMenu.Add(new TextMenu.Button("Info Copy Custom Template".ToDialogText()).Pressed(() =>
                TextInput.SetClipboardText(string.IsNullOrEmpty(TasSettings.InfoCustomTemplate) ? "\0" : TasSettings.InfoCustomTemplate)));
            subMenu.Add(new TextMenu.Button("Info Set Custom Template".ToDialogText()).Pressed(() => {
                TasSettings.InfoCustomTemplate = TextInput.GetClipboardText() ?? string.Empty;
                CelesteTasModule.Instance.SaveSettings();
            }));
            subMenu.Add(new TextMenuExt.EnumerableSlider<HudOptions>("Info Watch Entity".ToDialogText(), CreateHudOptions(),
                TasSettings.InfoWatchEntity).Change(value => TasSettings.InfoWatchEntity = value));
            subMenu.Add(new TextMenuExt.EnumerableSlider<WatchEntityType>("Info Watch Entity Type".ToDialogText(), new[] {
                new KeyValuePair<WatchEntityType, string>(WatchEntityType.Position, "Info Watch Entity Position".ToDialogText()),
                new KeyValuePair<WatchEntityType, string>(WatchEntityType.DeclaredOnly, "Info Watch Entity Declared Only".ToDialogText()),
                new KeyValuePair<WatchEntityType, string>(WatchEntityType.All, "Info Watch Entity All".ToDialogText()),
            }, TasSettings.InfoWatchEntityType).Change(value => TasSettings.InfoWatchEntityType = value));
            subMenu.Add(new TextMenuExt.IntSlider("Info Text Size".ToDialogText(), 5, 20, TasSettings.InfoTextSize).Change(value =>
                TasSettings.InfoTextSize = value));
            subMenu.Add(new TextMenuExt.IntSlider("Info Subpixel Indicator Size".ToDialogText(), 5, 20, TasSettings.InfoSubpixelIndicatorSize)
                .Change(value =>
                    TasSettings.InfoSubpixelIndicatorSize = value));
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
        subMenuItem.AddDescription(menu, "Info HUD Description 3".ToDialogText());
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