using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;
using Commands = Monocle.Commands;
using StudioCommunication.Util;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TAS.EverestInterop;

/// Displays a banner in the top-right corner, indicating the Studio update / installation
internal static class StudioUpdateBanner {
    public enum State {
        Inactive, Download, Install, Success, Failure, Launch
    }

    [Load]
    private static void Load() {
        typeof(Engine)
            .GetMethodInfo(nameof(Engine.Update))
            .IlHook((cur, _) => {
                if (cur.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Commands>(nameof(Commands.UpdateClosed)))) {
                    cur.MoveAfterLabels();
                    cur.EmitDelegate(Update);
                }
            });
        typeof(Engine)
            .GetMethodInfo(nameof(Engine.Draw))
            .IlHook((cur, _) => {
                if (cur.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Commands>(nameof(Commands.Render)))) {
                    cur.MoveAfterLabels();
                    cur.EmitDelegate(Render);
                }
            });

        var t = Task.Run(async () => {
            // Wait for font / dialog to be loaded
            while ((Engine.Scene is GameLoader loader && !loader.dialogLoaded) || !GFX.Loaded || Dialog.Languages == null || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
                await Task.Delay(10).ConfigureAwait(false);
            }

            // This only gets called when the speedrun timer is constructed
            // However the banner could be displayed before that
            SpeedrunTimerDisplay.CalculateBaseSizes();

            banner = GFX.Gui["CelesteTAS/extendedStrawberryCountBG"];
            loaded = true;
        });
    }

    public static State CurrentState = State.Inactive;

    public static long DownloadedBytes;
    public static long TotalBytes;
    public static long BytesPerSecond;
    public static float FadeoutTimer = -1.0f;

    private static MTexture banner = null!;
    private static PixelFont Font => Dialog.Languages[Settings.EnglishLanguage].Font;
    private static float FontFaceSize => Dialog.Languages[Settings.EnglishLanguage].FontFaceSize;

    private const int BannerY = 60; // Same as speedrun timer
    private const int TextY = BannerY + 44;

    private const float PaddingVerySmall = 16.0f;
    private const float PaddingSmall = 32.0f;
    private const float PaddingLarge = 40.0f;

    private const float DotSpeed = 0.5f;
    private const float BannerSpeed = 2000.0f;

    private static bool loaded = false;

    private static float bannerWidth;

    private static float dotTimer;
    private static int dotCount;

    private static void Update() {
        if (!loaded || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
            return;
        }

        dotTimer -= Engine.RawDeltaTime;
        if (dotTimer <= 0.0f) {
            dotCount = (dotCount + 1).Mod(4);
            dotTimer = DotSpeed;
        }

        float targetWidth = 0.0f;
        DrawAndMeasureBannerText(ref targetWidth, draw: false);

        // Keep last state during fadeout, so don't switch directly to Inactive
        if (FadeoutTimer > 0.0f) {
            FadeoutTimer -= Engine.RawDeltaTime;
            if (FadeoutTimer <= 0.0f) {
                FadeoutTimer = 0.0f;
            }
        }
        if (FadeoutTimer == 0.0f) {
            targetWidth = 0.0f;
        }

        bannerWidth = Calc.Approach(bannerWidth, targetWidth, BannerSpeed * Engine.RawDeltaTime);

        if (FadeoutTimer == 0.0f && bannerWidth == 0.0f) {
            CurrentState = State.Inactive;
            FadeoutTimer = -1.0f;
        }
    }

    private static void Render() {
        if (!loaded || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
            return;
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, RasterizerState.CullNone, null, Engine.ScreenMatrix);

        float left = Celeste.Celeste.TargetWidth - bannerWidth;

        banner.DrawJustified(position: new Vector2(left, BannerY),
            justify: new Vector2(1.0f, 0.0f),
            color: Color.White,
            scale: new Vector2(-1.0f, 1.0f));

        DrawAndMeasureBannerText(ref left, draw: true);

        Draw.SpriteBatch.End();
    }

    private static void DrawAndMeasureBannerText(ref float left, bool draw) {
        switch (CurrentState) {
            case State.Inactive:
                break;

            case State.Download:
                left += PaddingSmall;

                DrawText(ref left, $"Downloading Celeste TAS Studio v{StudioHelper.CurrentStudioVersion}{new string('.', dotCount)}", $"Downloading Celeste TAS Studio v{StudioHelper.CurrentStudioVersion}...", 1.0f, Color.White, draw);

                left += PaddingLarge;

                (string downloadedAmount, string downloadedSuffix) = DownloadedBytes.HumanReadableBytes(decimals: 2);
                DrawDecimal(ref left, downloadedAmount, 1.0f, draw);
                left += PaddingVerySmall;
                DrawText(ref left, downloadedSuffix, downloadedSuffix, 0.8f, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingSmall;
                DrawText(ref left, "/", "/", 1.0f, Calc.HexToColor("7a6f6d"), draw);
                left += PaddingSmall;

                (string totalAmount, string totalSuffix) = TotalBytes.HumanReadableBytes(decimals: 2);
                DrawDecimal(ref left, totalAmount, 1.0f, draw);
                left += PaddingVerySmall;
                DrawText(ref left, totalSuffix, totalSuffix, 0.8f, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingLarge;

                (string speedAmount, string speedSuffix) = BytesPerSecond.HumanReadableBytes(decimals: 2);
                DrawText(ref left, "(", "(", 0.8f, Calc.HexToColor("7a6f6d"), draw);
                DrawDecimal(ref left, speedAmount, 1.0f, draw);
                left += PaddingVerySmall;
                DrawText(ref left, speedSuffix + "/s)", speedSuffix + "/s)", 0.8f, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingLarge;

                string progress = ((float)DownloadedBytes / (float)TotalBytes * 100.0f).ToString("0.00");
                DrawText(ref left, "[", "[", 0.8f, Calc.HexToColor("7a6f6d"), draw);
                DrawDecimal(ref left, progress, 1.0f, draw);
                left += PaddingVerySmall;
                DrawText(ref left, "%]", "%]", 0.8f, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingSmall;
                break;

            case State.Install:
                left += PaddingSmall;
                DrawText(ref left, $"Installing Celeste TAS Studio v{StudioHelper.CurrentStudioVersion}{new string('.', dotCount)}", $"Installing Celeste TAS Studio v{StudioHelper.CurrentStudioVersion}...", 1.0f, Color.White, draw);
                left += PaddingSmall;
                break;

            case State.Success:
                left += PaddingSmall;
                DrawText(ref left, $"Celeste TAS Studio v{StudioHelper.CurrentStudioVersion} successfully installed", $"Celeste TAS Studio v{StudioHelper.CurrentStudioVersion} successfully installed", 1.0f, Color.LightGreen, draw);
                left += PaddingSmall;
                break;

            case State.Failure:
                left += PaddingSmall;
                DrawText(ref left, $"Celeste TAS Studio v{StudioHelper.CurrentStudioVersion} install failed!", $"Celeste TAS Studio v{StudioHelper.CurrentStudioVersion} install failed!", 1.0f, Color.IndianRed, draw);
                left += PaddingSmall;
                break;

            case State.Launch:
                left += PaddingSmall;
                DrawText(ref left, $"Launching Celeste TAS Studio{new string('.', dotCount)}", "Launching Celeste TAS Studio...", 1.0f, Color.LightSkyBlue, draw);
                left += PaddingSmall;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void DrawText(ref float left, string text, string measureText, float scale, Color color, bool draw = true) {
        if (draw) {
            float currWidth = ActiveFont.Measure(text).X * scale;
            ActiveFont.DrawOutline(text,
                position: new Vector2(left + currWidth, TextY - 5.0f * scale),
                justify: Vector2.One,
                scale: new Vector2(scale),
                color,
                stroke: 2.0f * scale,
                strokeColor: Color.Black);
        }

        float fullWidth = ActiveFont.Measure(measureText).X * scale;
        left += fullWidth;
    }

    private static void DrawDecimal(ref float left, string text, float scale, bool draw = true) {
        float s = scale;
        float y = TextY;
        var baseColor = Color.White;
        var smallColor = Color.LightGray;

        foreach (char c in text) {
            if (c == '.') {
                s = scale * 0.7f;
                y -= 5.0f * scale;
            }

            var color = (c == ':' || c == '.' || s < scale) ? smallColor : baseColor;
            float advance = (c is ':' or '.' ? SpeedrunTimerDisplay.spacerWidth : SpeedrunTimerDisplay.numberWidth) * s;

            if (draw) {
                Font.DrawOutline(FontFaceSize, c.ToString(),
                    position: new Vector2(left + advance / 2.0f, y),
                    justify: new Vector2(0.5f, 1f),
                    scale: new Vector2(s),
                    color,
                    stroke: 2.0f,
                    strokeColor: Color.Black);
            }

            left += advance;
        }
    }
}
