using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System.Runtime.CompilerServices;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

#nullable enable

public static class InvokeCommand {
    private class InvokeMeta : ITasCommandMeta {
        public string Insert => $"Invoke{CommandInfo.Separator}[0;Entity.Method]{CommandInfo.Separator}[1;Parameter]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            int hash = SetCommand.SetMeta.GetTargetArgs(args)
                .Aggregate(17, (current, arg) => 31 * current + arg.GetStableHashCode());
            // The other argument don't influence each other, so just the length matters
            return 31 * hash + 17 * args.Length;
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            var targetArgs = SetCommand.SetMeta.GetTargetArgs(args).ToArray();

            // Parameters
            if (args.Length > 1) {
                using var enumerator = GetParameterAutoCompleteEntries(targetArgs, args.Length - 2);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current;
                }
                yield break;
            }

            if (targetArgs.Length == 0) {
                var allTypes = ModUtils.GetTypes();
                foreach ((string typeName, var type) in allTypes
                             .Select(type => (type.CSharpName(), type))
                             .Order(new NamespaceComparer()))
                {
                    if (
                        // Filter-out types which probably aren't useful
                        !type.IsClass || !type.IsPublic || type.FullName == null || type.Namespace == null || SetCommand.SetMeta.ignoredNamespaces.Any(ns => type.Namespace.StartsWith(ns)) ||

                        // Filter-out compiler generated types
                        !type.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() || type.FullName.Contains('<') || type.FullName.Contains('>') ||

                        // Require either an entity, level, session
                        !type.IsSameOrSubclassOf(typeof(Entity)) && !type.IsSameOrSubclassOf(typeof(Level)) && !type.IsSameOrSubclassOf(typeof(Session)) &&
                        // Or type with static (invokable) methods
                        !type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                            .Any(IsInvokableMethod))
                    {
                        continue;
                    }

                    // Strip the namespace and add the @modname suffix if the typename isn't unique
                    string uniqueTypeName = typeName;
                    foreach (var otherType in allTypes) {
                        if (otherType.FullName == null || otherType.Namespace == null) {
                            continue;
                        }

                        string otherName = otherType.CSharpName();
                        if (type != otherType && typeName == otherName) {
                            uniqueTypeName = $"{typeName}@{ConsoleEnhancements.GetModName(type)}";
                            break;
                        }
                    }

                    yield return new CommandAutoCompleteEntry { Name = $"{uniqueTypeName}.", Extra = type.Namespace ?? string.Empty, IsDone = false };
                }
            } else if (targetArgs.Length >= 1 && InfoCustom.TryParseTypes(targetArgs[0], out var types, out _, out _)) {
                // Let's just assume the first type
                foreach (var entry in GetInvokeTypeAutoCompleteEntries(types[0], targetArgs.Length == 1)) {
                    yield return entry with { Name = entry.Name + (entry.IsDone ? "" : "."), Prefix = string.Join('.', targetArgs) + ".", HasNext = true };
                }
            }
        }

        private static IEnumerable<CommandAutoCompleteEntry> GetInvokeTypeAutoCompleteEntries(Type type, bool isRootType) {
            bool staticMembers = isRootType && !(type.IsSameOrSubclassOf(typeof(Entity)) || type.IsSameOrSubclassOf(typeof(Level)) || type.IsSameOrSubclassOf(typeof(Session)) || type.IsSameOrSubclassOf(typeof(EverestModuleSettings)));
            var bindingFlags = staticMembers
                ? BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                : BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            foreach (var m in type.GetMethods(bindingFlags).OrderBy(m => m.Name)) {
                // Filter-out compiler generated methods
                if (m.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !m.Name.Contains('<') && !m.Name.Contains('>') && !m.Name.StartsWith("set_") && !m.Name.StartsWith("get_") &&
                    IsInvokableMethod(m))
                {
                    yield return new CommandAutoCompleteEntry { Name = m.Name, Extra = $"({string.Join(", ", m.GetParameters().Select(p => p.HasDefaultValue ? $"[{p.ParameterType.CSharpName()}]" : p.ParameterType.CSharpName()))})", IsDone = true, };
                }
            }
        }

        [MustDisposeResource]
        private static IEnumerator<CommandAutoCompleteEntry> GetParameterAutoCompleteEntries(string[] targetArgs, int parameterIndex) {
            if (targetArgs.Length == 2 && InfoCustom.TryParseTypes(targetArgs[0], out var types, out _, out _)) {
                // Let's just assume the first type
                var parameters = types[0].GetMethodInfo(targetArgs[1]).GetParameters();
                if (parameterIndex >= 0 && parameterIndex < parameters.Length) {
                    // End arguments if further parameters aren't settable anymore
                    bool final = parameterIndex == parameters.Length - 1 ||
                                 parameterIndex < parameters.Length - 1 && !SetCommand.SetMeta.IsSettableType(parameters[parameterIndex].ParameterType);

                    return SetCommand.SetMeta.GetParameterTypeAutoCompleteEntries(parameters[parameterIndex].ParameterType, hasNextArgument: !final);
                }
            }

            return Enumerable.Empty<CommandAutoCompleteEntry>().GetEnumerator();
        }

        private static bool IsInvokableMethod(MethodInfo info) {
            // Generic methods could probably be supported somehow, but that's probably not worth
            if (info.IsGenericMethod) {
                return false;
            }
            // To be invokable, all parameters need to be settable or have a default value from a non-settable onwards
            bool requireDefaults = false;
            foreach (var param in info.GetParameters()) {
                if (!requireDefaults && !SetCommand.SetMeta.IsSettableType(param.ParameterType)) {
                    requireDefaults = true;
                }

                if (requireDefaults && !param.HasDefaultValue) {
                    return false;
                }
            }

            return true;
        }
    }

    private static bool logToConsole;

    private static void ReportError(string message) {
        if (logToConsole) {
            $"Invoke Command Failed: {message}".ConsoleLog(LogLevel.Error);
        } else {
            AbortTas($"Invoke Command Failed: {message}");
        }
    }

    [Monocle.Command("invoke", "Invoke level/session/entity method. eg invoke Level.Pause; invoke Player.Jump (CelesteTAS)"), UsedImplicitly]
    private static void ConsoleInvoke(string? arg1, string? arg2, string? arg3, string? arg4, string? arg5, string? arg6, string? arg7, string? arg8, string? arg9) {
        // TODO: Support arbitrary amounts of arguments
        string?[] args = [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9];
        logToConsole = true;
        Invoke(args.TakeWhile(arg => arg != null).ToArray()!);
        logToConsole = false;
    }

    // Invoke, Level.Method, Parameters...
    // Invoke, Session.Method, Parameters...
    // Invoke, Entity.Method, Parameters...
    // Invoke, Type.StaticMethod, Parameters...
    [TasCommand("Invoke", LegalInFullGame = false, MetaDataProvider = typeof(InvokeMeta))]
    private static void Invoke(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Invoke(commandLine.Arguments);
    }

    private static void Invoke(string[] args) {
        if (args.Length < 1) {
            ReportError("Target-query required");
            return;
        }

        string query = args[0];
        string[] queryArgs = query.Split('.');

        var baseTypes = TargetQuery.ResolveBaseTypes(queryArgs, out string[] memberArgs, out var entityId);
        if (baseTypes.IsEmpty()) {
            ReportError($"Failed to find base type for query '{query}'");
            return;
        }
        if (memberArgs.IsEmpty()) {
            ReportError("No members specified");
            return;
        }

        foreach (var type in baseTypes) {
            (var method, bool success) = TargetQuery.ResolveMemberMethod(type, memberArgs);
            if (!success) {
                ReportError($"Failed to find method '{string.Join('.', memberArgs)}' on type '{type}'");
                return;
            }

            (object?[] values, success, string errorMessage) = TargetQuery.ResolveValues(args[1..], method!.GetParameters().Select(param => param.ParameterType).ToArray());
            if (!success) {
                ReportError(errorMessage);
                return;
            }

            var instances = TargetQuery.ResolveTypeInstances(type, entityId);
            success = TargetQuery.InvokeMemberMethods(type, instances, values, memberArgs);
            if (!success) {
                ReportError($"Failed to invoke method '{string.Join('.', memberArgs)}' on type '{type}' to with parameters '{string.Join(';', values)}'");
                return;
            }
        }
    }
}
