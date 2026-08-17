using System;
using System.Collections.Generic;
using System.IO;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using System.Linq;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop.Lua;

public static class EvalLuaCommand {
    public const string CommandName = "EvalLua";

    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Code]";
        public bool HasArguments => true;
    }

    internal static bool ConsoleCommandRunning;

    public static void Log(object message) {
        if (ConsoleCommandRunning) {
            Engine.Commands.Log(message);
        }

        $"{CommandName} Command Failed: {message}".Log();
    }

    [Monocle.Command(CommandName, "Evaluate Lua code (CelesteTAS)"), UsedImplicitly]
    private static void EvalLua() {
        if (!CommandLine.TryParse(Engine.Commands.commandHistory[0], out var commandLine)) {
            $"{CommandName} Command Failed: Couldn't parse arguments of command".ConsoleLog(LogLevel.Error);
            return;
        }

        try {
            ConsoleCommandRunning = true;
            object?[]? result = ExecuteLua(string.Join(commandLine.ArgumentSeparator, commandLine.Arguments));
            LogResult(result);
        } finally {
            ConsoleCommandRunning = false;
        }
    }

    [TasCommand(CommandName, LegalInFullGame = false, MetaDataProvider = typeof(Meta))]
    private static void EvalLua(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.IsEmpty()) {
            return;
        }

        ExecuteLua(string.Join(commandLine.ArgumentSeparator, commandLine.Arguments));
    }

    private static string? envCode;
    internal static object?[]? ExecuteLua(string code) {
        // Prepend useful helper functions as environment
        if (envCode == null) {
            var asset = Everest.Content.Get("bin/env");
            using var reader = new StreamReader(asset.Stream);
            envCode = reader.ReadToEnd() + "\n";
        }
        code = envCode + code;

        object?[]? objects;
        try {
            objects = Everest.LuaLoader.Run(code, null);
        } catch (Exception e) {
            e.Log();
            return [e];
        }

        return objects;
    }

    internal static void LogResult(object?[]? objects) {
        if (objects == null || objects.Length == 0) {
            return;
        }

        var result = new List<string>();
        if (objects.Length == 1) {
            result.Add(objects[0]?.ToString() ?? "null");
        } else {
            result.AddRange(objects.Select((obj, idx) => $"{idx + 1}: {obj?.ToString() ?? "null"}"));
        }

        Engine.Commands.Log(string.Join("\n", result));
    }
}
