using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;
using Commands = Monocle.Commands;
using StudioCommunication.Util;
using System;
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
            .GetMethodInfo(nameof(Engine.Update))!
            .IlHook((cur, _) => {
                if (cur.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Commands>(nameof(Commands.UpdateClosed)))) {
                    cur.MoveAfterLabels();
                    cur.EmitDelegate(Update);
                }
            });
        typeof(Engine)
            .GetMethodInfo(nameof(Engine.Draw))!
            .IlHook((cur, _) => {
                if (cur.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Commands>(nameof(Commands.Render)))) {
                    cur.MoveAfterLabels();
                    cur.EmitDelegate(Render);
                }
            });

        Task.Run(async () => {
            // Wait for font / dialog to be loaded
            // NOTE: 'loader.loaded' is checked instead of 'loader.dialogLoaded' since for the latter, there is a race condition with Fast-Texture-Loading not yet being done
            while ((Engine.Scene is GameLoader loader && !loader.loaded) || !GFX.Loaded || Dialog.Languages == null || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
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

    private const int BannerY = 150; // Slightly under the speedrun timer
    private const int TextY = BannerY + 23;

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
        if (!loaded || !TasSettings.ShowStudioUpdateBanner || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
            bannerWidth = Calc.Approach(bannerWidth, 0.0f, BannerSpeed * Engine.RawDeltaTime);
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
        if (!loaded || bannerWidth <= 0.001f || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || Font == null) {
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

                const float progressSize = 0.8f;

                left += PaddingLarge;

                (string downloadedAmount, string downloadedSuffix) = DownloadedBytes.HumanReadableBytes(decimals: 2);
                DrawDecimal(ref left, downloadedAmount, progressSize, draw);
                left += PaddingVerySmall * progressSize;
                DrawText(ref left, downloadedSuffix, downloadedSuffix, 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingSmall * progressSize;
                DrawText(ref left, "/", "/", progressSize, Calc.HexToColor("7a6f6d"), draw);
                left += PaddingSmall * progressSize;

                (string totalAmount, string totalSuffix) = TotalBytes.HumanReadableBytes(decimals: 2);
                DrawDecimal(ref left, totalAmount, progressSize, draw);
                left += PaddingVerySmall * progressSize;
                DrawText(ref left, totalSuffix, totalSuffix, 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingLarge * progressSize;

                (string speedAmount, string speedSuffix) = BytesPerSecond.HumanReadableBytes(decimals: 2);
                DrawText(ref left, "(", "(", 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);
                DrawDecimal(ref left, speedAmount, progressSize, draw);
                left += PaddingVerySmall * progressSize;
                DrawText(ref left, speedSuffix + "/s)", speedSuffix + "/s)", 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);

                left += PaddingLarge * progressSize;

                string progress = ((float)DownloadedBytes / (float)TotalBytes * 100.0f).ToString("0.00");
                DrawText(ref left, "[", "[", 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);
                DrawDecimal(ref left, progress, progressSize, draw);
                left += PaddingVerySmall * progressSize;
                DrawText(ref left, "%]", "%]", 0.75f * progressSize, Calc.HexToColor("7a6f6d"), draw);

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
            ActiveFont.Font.DrawCommonBaseline(text, ActiveFont.BaseSize,
                position: new Vector2(left, TextY/* + 15.0f * scale*/),
                justify: Vector2.Zero,
                scale: new Vector2(scale),
                color,
                stroke: 2.0f * scale,
                strokeColor: Color.Black,
                edgeDepth: 0.0f,
                edgeColor: Color.Transparent);
        }

        float fullWidth = ActiveFont.Measure(measureText).X * scale;
        left += fullWidth;
    }

    private static void DrawDecimal(ref float left, string text, float scale, bool draw = true) {
        float s = scale;
        var baseColor = Color.White;
        var smallColor = Color.LightGray;

        foreach (char c in text) {
            if (c == '.') {
                s = scale * 0.7f;
                // y -= 5.0f * scale;
            }

            var color = (c == ':' || c == '.' || s < scale) ? smallColor : baseColor;
            float advance = (c is ':' or '.' ? SpeedrunTimerDisplay.spacerWidth : SpeedrunTimerDisplay.numberWidth) * s;

            if (draw) {
                Font.DrawCommonBaseline(c.ToString(), FontFaceSize,
                    position: new Vector2(left, TextY),
                    justify: new Vector2(0.0f, 0.0f),
                    scale: new Vector2(s),
                    color,
                    stroke: 2.0f,
                    strokeColor: Color.Black,
                    edgeDepth: 0.0f,
                    edgeColor: Color.Transparent);
            }

            left += advance;
        }
    }
}
