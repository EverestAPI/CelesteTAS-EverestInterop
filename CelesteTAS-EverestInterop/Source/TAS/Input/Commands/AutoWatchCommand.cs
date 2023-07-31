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

    [Monocle.Command("autowatch", "Adds an entity type(s) to auto watch list e.g. autowatch Kevin, autowatch Bumper")]
    private static void InvokeWatch(string arg1, string arg2, string arg3, string arg4, string arg5, string arg6, string arg7, string arg8,
        string arg9) {
        string[] args = {arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9};
        consolePrintLog = true;
        InvokeWatch(args.TakeWhile(arg => arg != null).ToArray());
        consolePrintLog = false;
    }

    // AutoWatch, Type1, Type2, ...
    [TasCommand("AutoWatch")]
    private static void InvokeWatch(string[] args) {
        List<Type> watchTypes = new();

        foreach (string arg in args) {
            try {
                if (!InfoCustom.TryParseType(arg, out Type watchType, out _, out string errorMessage)) {
                    errorMessage.Log(consolePrintLog, LogLevel.Warn);
                } else {
                    watchTypes.Add(watchType);
                }
            } catch (Exception e) {
                e.Log(consolePrintLog, LogLevel.Warn);
            }
        }

        foreach (var watchType in watchTypes) {
            InfoWatchEntity.AutoWatch.Add(watchType);
        }
    }

    [Monocle.Command("endautowatch", "Removes an entity type(s) from auto watch list e.g. endautowatch Kevin. Don't specify entity types to stop all autowatching")]
    private static void InvokeEndWatch(string arg1, string arg2, string arg3, string arg4, string arg5, string arg6, string arg7, string arg8,
        string arg9) {
        string[] args = { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 };
        consolePrintLog = true;
        InvokeEndWatch(args.TakeWhile(arg => arg != null).ToArray());
        consolePrintLog = false;
    }

    // EndAutoWatch, Type1, Type2, ...
    // EndAutoWatch
    [TasCommand("EndAutoWatch")]
    private static void InvokeEndWatch(string[] args) {
        List<Type> watchTypes = new();

        foreach (string arg in args) {
            try {
                if (!InfoCustom.TryParseType(arg, out Type watchType, out _, out string errorMessage)) {
                    errorMessage.Log(consolePrintLog, LogLevel.Warn);
                } else {
                    watchTypes.Add(watchType);
                }
            } catch (Exception e) {
                e.Log(consolePrintLog, LogLevel.Warn);
            }
        }

        if (watchTypes.Count == 0) {
            InfoWatchEntity.AutoWatch.Clear();
        }

        foreach (var watchType in watchTypes) {
            InfoWatchEntity.AutoWatch.Remove(watchType);
        }
    }
}