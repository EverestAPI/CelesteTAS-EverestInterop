using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.Lua;
using TAS.Module;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Evaluates the provided Lua code
internal static class EvalLuaCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Code]";
        public bool HasArguments => true;
    }

    private const string CommandName = "EvalLua";
    private static readonly Regex commandSeparatorRegex = new($"^{CommandName}[ |,]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// Used by LuaHelpers error reporting
    internal static bool LogToConsole = false;

    [Load]
    private static void Load() {
        // Hook DebugRC '/console' endpoint callback to avoid splitting arguments
        var callbackMethods = typeof(Everest.DebugRC)
            .GetNestedType("<>c", BindingFlags.NonPublic)!
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);

        // Check all methods since the internal name might change
        foreach (var method in callbackMethods) {
            var methodBody = method.GetMethodBody();
            if (methodBody == null || methodBody.LocalVariables.All(localVariable => localVariable.LocalType.FullName != "Monocle.Commands+CommandData")) {
                continue;
            }

            method.IlHook((cursor, _) => {
                // Insert codes after "rawCommand.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries);"
                if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<string>(nameof(string.Split)))) {
                    cursor.EmitLdloc0();
                    cursor.EmitStaticDelegate("DontSplitArguments", string[] (string[] commandAndArgs, string rawCommand) => {
                        if (commandAndArgs[0].ToLower() == CommandName && commandAndArgs.Length >= 2) {
                            return [CommandName, commandSeparatorRegex.Replace(rawCommand, "")];
                        }

                        return commandAndArgs;
                    });
                }
            });
            return;
        }
    }

    [MonocleCommand(CommandName, "Evaluate Lua code (CelesteTAS)"), UsedImplicitly]
    private static void CmdEvalLua(string code) {
        // Avoid arguments getting split when using from Debug Console
        string? history = Engine.Commands.commandHistory.FirstOrDefault();
        if (Engine.Commands.debugRClog  == null && history?.StartsWith(CommandName, StringComparison.InvariantCultureIgnoreCase) == true) {
            code = commandSeparatorRegex.Replace(history, "");
        }

        var ctx = LuaContext.Compile(code, CommandName);
        if (ctx.Failure) {
            Engine.Commands.Log($"Invalid Lua code: {ctx.Error}", Color.Red);
            return;
        }

        try {
            LogToConsole = true;
            var result = ctx.Value.Execute();
            LogToConsole = false;

            if (result.Failure) {
                Engine.Commands.Log($"Lua error: {result.Error.Message}\n{result.Error.Stacktrace}", Color.Red);
                return;
            }

            object?[] objects = result.Value.ToArray();
            if (objects.Length == 1) {
                Engine.Commands.Log(objects[0]?.ToString() ?? "null");
            } else {
                for (int i = 0; i < objects.Length; i++) {
                    Engine.Commands.Log($"{i + 1}: {objects[i]?.ToString() ?? "null"}");
                }
            }
        } finally {
            ctx.Value.Dispose();
        }
    }

    private static readonly Dictionary<int, List<LuaContext>> luaContexts = new();

    [ClearInputs]
    private static void ClearInputs() {
        foreach (var context in luaContexts.SelectMany(entry => entry.Value)) {
            context.Dispose();
        }
        luaContexts.Clear();
    }

    /// EvalLua, Code
    [TasCommand(CommandName, LegalInFullGame = false, ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    private static void EvalLua(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (Command.Parsing) {
            // Support [[ Lua Code ]] syntax, like Custom Info
            string code = InfoCustom.LuaRegex.Replace(string.Join(commandLine.ArgumentSeparator, commandLine.Arguments), match => match.Groups[1].Value);
            
            if (string.IsNullOrWhiteSpace(code)) {
                AbortTas("Expected Lua code");
                return;
            }
            

            var ctx = LuaContext.Compile(code, CommandName);
            if (ctx.Failure) {
                AbortTas($"Invalid Lua code: {ctx.Error}");
                return;
            }

            luaContexts.AddToKey(Manager.Controller.CurrentParsingFrame, ctx);
        } else {
            if (!luaContexts.TryGetValue(Manager.Controller.CurrentFrameInTas, out var contexts)) {
                return;
            }

            foreach (var context in contexts) {
                var result = context.Execute();
                if (result.Failure) {
                    AbortTas($"Lua error: {result.Error.Message}\n{result.Error.Stacktrace}");
                    return;
                }
            }
        }
    }
}
