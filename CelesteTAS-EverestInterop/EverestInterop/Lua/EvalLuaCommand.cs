using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste.Mod;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Lua;

public static class EvalLuaCommand {
    private static bool consoleCommandRunning;
    private const string commandName = "evallua";
    private static readonly Regex commandAndSeparatorRegex = new(@$"^{commandName}[ |,]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly FieldInfo DebugRClogFieldInfo = typeof(Commands).GetFieldInfo("debugRClog");

    [Load]
    private static void Load() {
        HookEverestDebugRc();
    }

    private static void HookEverestDebugRc() {
        var methods = typeof(Everest.DebugRC).GetNestedType("<>c", BindingFlags.NonPublic)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var method in methods) {
            var methodBody = method.GetMethodBody();
            if (methodBody == null) {
                continue;
            }

            foreach (var localVariable in methodBody.LocalVariables) {
                if (localVariable.LocalType?.FullName != "Monocle.Commands+CommandData") {
                    continue;
                }

                method.IlHook((cursor, _) => {
                    // insert codes after "rawCommand.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries);"
                    if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<string>("Split"))) {
                        cursor.Emit(OpCodes.Ldloc_0).EmitDelegate<Func<string[], string, string[]>>(
                            (commandAndArgs, rawCommand) => {
                                if (commandAndArgs[0].ToLower() == commandName && commandAndArgs.Length >= 2) {
                                    return new[] {commandName, commandAndSeparatorRegex.Replace(rawCommand, "")};
                                }

                                return commandAndArgs;
                            });
                    }
                });

                return;
            }
        }
    }

    private static string ReadContent(string assetPath) {
        ModAsset modAsset = Everest.Content.Get(assetPath, true);
        if (modAsset != null) {
            using StreamReader streamReader = new(modAsset.Stream);
            return streamReader.ReadToEnd();
        } else {
            return null;
        }
    }

    public static void Log(object message) {
        if (consoleCommandRunning) {
            Engine.Commands.Log(message);
        }

        $"EvalLua Command Failed: {message}".Log();
    }

    [Monocle.Command(commandName, "Evaluate lua code (CelesteTAS)")]
    private static void EvalLua(string code) {
        string firstHistory = Engine.Commands.commandHistory.FirstOrDefault();
        if (DebugRClogFieldInfo.GetValue(Engine.Commands) == null &&
            firstHistory?.StartsWith(commandName, StringComparison.InvariantCultureIgnoreCase) == true) {
            code = commandAndSeparatorRegex.Replace(firstHistory, "");
        }

        consoleCommandRunning = true;
        object[] result = EvalLuaImpl(code);
        consoleCommandRunning = false;
        LogResult(result);
    }

    [TasCommand(commandName, LegalInFullGame = false)]
    private static void EvalLua(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.IsEmpty()) {
            return;
        }

        EvalLuaImpl(commandAndSeparatorRegex.Replace(commandLine.OriginalText, ""));
    }

    public static object[] EvalLuaImpl(string code) {
        string localCode = ReadContent("bin/env");
        code = $"{localCode}\n{code}";

        object[] objects;
        try {
            objects = Everest.LuaLoader.Run(code, null);
        } catch (Exception e) {
            e.Log();
            return new object[] {e};
        }

        return objects;
    }

    private static void LogResult(object[] objects) {
        var result = new List<string>();

        if (objects == null || objects.Length == 0) {
            return;
        } else if (objects.Length == 1) {
            result.Add(objects[0]?.ToString() ?? "null");
        } else {
            for (var i = 0; i < objects.Length; i++) {
                result.Add($"{i + 1}: {objects[i]?.ToString() ?? "null"}");
            }
        }

        Engine.Commands.Log(string.Join("\n", result));
    }
}
