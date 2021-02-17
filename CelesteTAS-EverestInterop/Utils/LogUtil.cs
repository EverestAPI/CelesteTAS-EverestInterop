// ReSharper disable RedundantUsingDirective

using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS {
    internal static class LogUtil {
        private const string Tag = "CelesteTAS";

        public static void Log(this object text, LogLevel logLevel = LogLevel.Verbose) {
            text = text == null ? "null" : text.ToString();
            Logger.Log(logLevel, Tag, text.ToString());
#if DEBUG
            Color color;
            switch (logLevel) {
                case LogLevel.Warn:
                    color = Color.Yellow;
                    break;
                case LogLevel.Error:
                    color = Color.Red;
                    break;
                default:
                    color = Color.Cyan;
                    break;
            }

            try {
                Engine.Commands?.Log($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Tag}] {logLevel}: {text}", color);
            } catch (Exception err) {
                // ignored
            }
#endif
        }
    }
}