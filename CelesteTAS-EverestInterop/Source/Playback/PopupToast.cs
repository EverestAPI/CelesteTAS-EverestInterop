using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TAS.Gameplay;
using TAS.Module;
using TAS.Utils;

namespace TAS.Playback;

/// Popup toast message to be displayed in the bottom-left of the screen
internal static class PopupToast {
    private const int Padding = 25;
    public const float DefaultDuration = 3.0f;

    public class Entry(string text, float timeout, Color color) {
        public string Text = text;
        public Color Color = color;

        public float Timeout = timeout;

        public float Fade = 0.0f;
        public bool Active => entries.Contains(this);
    }

    private static readonly List<Entry> entries = [];

    public static Entry Show(string message, float timeout = DefaultDuration) {
        var entry = new Entry(message, timeout, Color.White);
        Show(entry);
        return entry;
    }
    public static Entry ShowWithColor(string message, Color color, float timeout = DefaultDuration) {
        var entry = new Entry(message, timeout, color);
        Show(entry);
        return entry;
    }
    public static Entry ShowAndLog(string message, float timeout = DefaultDuration, LogLevel level = LogLevel.Warn) {
        message.Log(level);
        return ShowWithColor(message, level switch {
            LogLevel.Verbose => Color.Purple,
            LogLevel.Debug => Color.Blue,
            LogLevel.Info => Color.White,
            LogLevel.Warn => Color.Yellow,
            LogLevel.Error => Color.Red,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        }, timeout);
    }

    public static void Show(Entry entry) {
        entries.Add(entry);
    }

    [Load]
    private static void Load() {
        Events.PostRender += () => {
            // Wait for font / dialog to be loaded
            // NOTE: 'loader.loaded' is checked instead of 'loader.dialogLoaded' since for the latter, there is a race condition with Fast-Texture-Loading not yet being done
            if ((Engine.Scene is GameLoader loader && !loader.loaded) || !GFX.Loaded || Dialog.Languages == null || !Dialog.Languages.ContainsKey(Settings.EnglishLanguage) || ActiveFont.Font == null) {
                return;
            }

            var span = CollectionsMarshal.AsSpan(entries);

            // Fade in/out
            foreach (ref var entry in span) {
                float target = entry.Timeout > 0.0f ? 1.0f : 0.0f;
                entry.Fade = Calc.Approach(entry.Fade, target, Core.PlaybackDeltaTime * 5.0f);
            }

            // Render
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Engine.ScreenMatrix);

            float yOffset = Engine.Height;
            foreach (ref var entry in span) {
                var size = ActiveFont.Measure(entry.Text);
                yOffset -= size.Y * Math.Min(1.0f, entry.Fade * 2.0f) + Padding / 2.0f;

                float alpha = entry.Timeout > 0.0
                    ? Ease.SineIn(entry.Fade)
                    : Ease.SineOut(entry.Fade);

                ActiveFont.DrawOutline(entry.Text,
                    position: new Vector2(Padding, yOffset),
                    justify: Vector2.Zero,
                    scale: Vector2.One,
                    color: entry.Color * alpha,
                    stroke: 2,
                    strokeColor: Color.Black * (alpha * alpha * alpha));

                // Advance timer
                if (entry.Fade >= 1.0f) {
                    entry.Timeout -= Core.PlaybackDeltaTime;
                }
            }

            Draw.SpriteBatch.End();

            // Cleanup
            entries.RemoveAll(entry => entry.Timeout <= 0.0f && entry.Fade <= 0.0f);
        };
    }
}
