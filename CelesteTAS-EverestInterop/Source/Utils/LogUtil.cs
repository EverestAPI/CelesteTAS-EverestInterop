using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.Utils;

#nullable enable

internal static class LogUtil {
    private const string Tag = "CelesteTAS";

#if DEBUG
    public static void DebugLog(this object text, LogLevel logLevel = LogLevel.Debug) => text.DebugLog(true, logLevel);
    public static void DebugLog(this object text, bool outputToCommands, LogLevel logLevel = LogLevel.Debug) => text.Log(string.Empty, outputToCommands, logLevel);
#endif

    public static void LogException(this Exception e) => e.LogException(false);
    public static void LogException(this Exception e, bool outputToCommands) {
        Logger.LogDetailed(e, Tag);

        if (outputToCommands) {
            Engine.Commands.Log(e.Message, Color.Yellow);
            Engine.Commands.LogStackTrace(e.StackTrace);
        }
    }

    public static void LogException(this Exception e, string header, LogLevel logLevel = LogLevel.Error) => e.LogException(header, string.Empty, false, logLevel);
    public static void LogException(this Exception e, string header, bool outputToCommands, LogLevel logLevel = LogLevel.Error) => e.LogException(header, string.Empty, outputToCommands, logLevel);
    public static void LogException(this Exception e, string header, string category, LogLevel logLevel = LogLevel.Error) => e.LogException(header, category, false, logLevel);
    public static void LogException(this Exception e, string header, string category, bool outputToCommands, LogLevel logLevel = LogLevel.Error) {
        header.Log(category, outputToCommands, logLevel);
        Logger.LogDetailed(e, Tag);

        if (outputToCommands) {
            Engine.Commands.Log(e.Message, Color.Yellow);
            Engine.Commands.LogStackTrace(e.StackTrace);
        }
    }

    public static void Log(this object? text, LogLevel logLevel = LogLevel.Info) => text.Log(string.Empty, false, logLevel);
    public static void Log(this object? text, string category, LogLevel logLevel = LogLevel.Info) => text.Log(category, false, logLevel);
    public static void Log(this object? text, string category, bool outputToCommands, LogLevel logLevel = LogLevel.Info) {
        string tag = category == string.Empty
            ? Tag
            : $"{Tag}/{category}";

        string textStr = text?.ToString() ?? "null";
        Logger.Log(logLevel, tag, textStr);

        if (outputToCommands) {
            ConsoleLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {logLevel}: {textStr}", logLevel);
        }
    }

    public static void ConsoleLog(this object? text, LogLevel logLevel = LogLevel.Verbose) {
        var color = logLevel switch {
            LogLevel.Warn => Color.Yellow,
            LogLevel.Error => Color.Red,
            _ => Color.Cyan
        };

        try {
            Engine.Commands?.Log(text?.ToString() ?? "null", color);
        } catch (Exception) {
            // ignored
        }
    }
}
