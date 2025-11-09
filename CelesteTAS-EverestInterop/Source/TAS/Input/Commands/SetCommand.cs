using System;
using System.Linq;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;
using TAS.EverestInterop;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Playback;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class SetCommand {
    internal class SetMeta : ITasCommandMeta {
        public string Insert => $"Set{CommandInfo.Separator}[0;Query]{CommandInfo.Separator}[1;Value]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            var hash = new StableHashCode();
            hash.Add(TargetQuery.GetQueryArgs(args, 0).Aggregate(new StableHashCode(), (argHash, arg) => argHash.Append(arg)).ToHashCode());
            hash.Add(TargetQuery.GetQueryArgs(args, 1).Aggregate(new StableHashCode(), (argHash, arg) => argHash.Append(arg)).ToHashCode());
            hash.Add(args.Length);
            return hash.ToHashCode();
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            // Target
            string[] targetQueryArgs = args.Length > 0 ? args[0].Split('.') : [];
            if (args.Length <= 1) {
                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(targetQueryArgs, TargetQuery.Variant.Set);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current with { HasNext = true };
                }
                yield break;
            }

            // Parameter
            {
                string[] paramQueryArgs = args.Length > 1 ? args[1].Split('.') : [];
                var baseTypes = TargetQuery.ResolveBaseTypes(targetQueryArgs, out string[] memberArgs);
                var targetTypes = baseTypes
                    .Select(type => TargetQuery.RecurseMemberType(type, memberArgs, TargetQuery.Variant.Set))
                    .Where(type => type != null)
                    .ToArray();

                using var enumerator = TargetQuery.ResolveAutoCompleteEntries(paramQueryArgs, TargetQuery.Variant.Get, targetTypes!);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current;
                }
            }
        }
    }

    private static (string Name, int Line)? activeFile;

    private static void ReportError(string message) {
        if (activeFile == null) {
            $"Set Command Failed: {message}".ConsoleLog(LogLevel.Error);
        } else {
            PopupToast.ShowAndLog($"""
                                   Set '{activeFile.Value.Name}' line {activeFile.Value.Line} failed:
                                   {message}
                                   """);
        }
    }

    [Monocle.Command("set", "'set Settings/Level/Session/Entity value' | Example: 'set DashMode Infinite', 'set Player.Speed 325 -52.5' (CelesteTAS)"), UsedImplicitly]
    private static void SetCmd() {
        if (!CommandLine.TryParse(Engine.Commands.commandHistory[0], out var commandLine)) {
            "Set Command Failed: Couldn't parse arguments of command".ConsoleLog(LogLevel.Error);
            return;
        }

        Set(commandLine.Arguments);
    }

    // Set, Setting, Value
    // Set, Mod.Setting, Value
    // Set, Entity.Field, Value
    // Set, Type.StaticMember, Value
    [TasCommand("Set", LegalInFullGame = false, MetaDataProvider = typeof(SetMeta))]
    private static void Set(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        activeFile = (filePath, fileLine);
        Set(commandLine.Arguments);
        activeFile = null;
    }

    private static void Set(string[] args) {
        switch (args.Length) {
            case < 1:
                ReportError("Expected target-query and value");
                return;
            case < 2:
                ReportError($"Expected value (only get target-query '{args[0]}')");
                return;
        }

        var result = TargetQuery.SetMemberValues(args[0], args[1..]);
        if (result.Failure) {
            ReportError(result.Error.ToString());
        }
    }
}
