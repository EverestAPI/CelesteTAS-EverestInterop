using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;
public static class AutoWatchCommand {
    private static bool consolePrintLog;
    private const string logPrefix = "{AutoWatch Command Failed: }";
    private static readonly object nonReturnObject = new();

    [Monocle.Command("autowatch", "Add Type to auto watch list, with optional onscreen limiter - e.g. AutoWatch Kevin, AutoWatch Bumper OnScreen")]
    private static void Invoke(string arg1, string arg2, string arg3, string arg4, string arg5, string arg6, string arg7, string arg8,
        string arg9) {
        string[] args = {arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9};
        consolePrintLog = true;
        Invoke(args.TakeWhile(arg => arg != null).ToArray());
        consolePrintLog = false;
    }

    // AutoWatch, Type, [OnScreen]
    [TasCommand("AutoWatch")]
    private static void Invoke(string[] args) {
        if (args.Length == 0) {
            return;
        }

        Type watchType = null;
        bool unwatch = false;

        try {
            if (!InfoCustom.TryParseType(args[0], out watchType, out _, out string errorMessage)) {
                errorMessage.Log(consolePrintLog, LogLevel.Warn);
            }
        } catch (Exception e) {
            e.Log(consolePrintLog, LogLevel.Warn);
        }

        if (watchType is null) return;

        for (int i = 1; i < args.Length; i++) {
            if (args[i].ToLowerInvariant() == "unwatch") {
                unwatch = true;
            }
        }

        if (unwatch) {
            InfoWatchEntity.AutoWatch.Remove(watchType);
        } else {
            InfoWatchEntity.AutoWatch.Add(watchType);
        }
    }
}