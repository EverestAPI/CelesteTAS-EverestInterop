// ReSharper disable RedundantUsingDirective

using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

// ReSharper disable HeuristicUnreachableCode
namespace TAS.Utils;

internal static class LogUtil {
    private const string Tag = "CelesteTAS";

#if DEBUG
    // ReSharper disable once UnusedMember.Global
    public static void DebugLog(this object text, LogLevel logLevel = LogLevel.Info) {
        text.DebugLog(true, logLevel);
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static void DebugLog(this object text, bool outputToCommands, LogLevel logLevel = LogLevel.Info) {
        text.Log(outputToCommands, logLevel);
    }
#endif

    public static void LogException(this Exception e, string header, LogLevel logLevel = LogLevel.Warn) {
        header.Log(logLevel);
        e.LogDetailed();
    }

    public static void Log(this object text, LogLevel logLevel = LogLevel.Info) {
        text.Log(false, logLevel);
    }

    // ReSharper disable once RedundantAssignment
    public static void Log(this object text, bool outputToCommands, LogLevel logLevel = LogLevel.Info) {
        text = text == null ? "null" : text.ToString();
        Logger.Log(logLevel, Tag, text.ToString());

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (outputToCommands) {
            text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Tag}] {logLevel}: {text}";
            ConsoleLog(text, logLevel);
        }
    }

    public static void ConsoleLog(this object text, LogLevel logLevel = LogLevel.Verbose) {
        text = text == null ? "null" : text.ToString();
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
            Engine.Commands?.Log(text, color);
        } catch (Exception) {
            // ignored
        }
    }
}