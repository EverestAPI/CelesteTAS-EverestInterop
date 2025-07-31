using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TAS.EverestInterop;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using StudioCommunication;
using StudioCommunication.Util;
using System.Runtime.CompilerServices;
using System.Text;

namespace TAS.InfoHUD;

/// Contains all the logic for getting/setting/invoking data with the target-query syntax
/// See wiki for documentation: https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Info-HUD#target-queries
public static class TargetQuery {
    internal enum Variant {
        Get, Set, Invoke
    }

    /// Handler to provide support for Celeste-specific special cases
    internal abstract class Handler {
        public virtual bool CanResolveInstances(Type type) => false;
        public virtual bool CanResolveValue(Type type) => false;
        public virtual bool CanEnumerateMemberEntries(Type type, Variant variant) => false;
        public virtual bool CanEnumerateTypeEntries(Type type, Variant variant) => false;

        /// Provide currently active instances for the specified type. <br/>
        /// Only invoked if <see cref="CanResolveInstances"/> returned <c>true</c> for the type.
        public virtual object[] ResolveInstances(Type type) => [];

        /// Can process the query arguments into invalid members, to allow for custom syntax
        public virtual IEnumerable<string> ProcessQueryArguments(IEnumerable<string> queryArgs) => queryArgs;

        /// Resolve a set of base target-types for a query. <br/>
        /// <c>null</c> should be returned when no base-types could be resolved.
        public virtual (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) => null;

        /// Attempts to resolve the next member value for all current objects in parallel. <br/>
        /// <c>true</c> should be returned when the handler could resolve the next member, otherwise <c>false</c>.
        public virtual Result<bool, QueryError> ResolveMemberValues(ref object?[] values, ref int memberIdx, string[] memberArgs) {
            return Result<bool, QueryError>.Ok(false);
        }

        /// Attempts to resolve the next member for a single value. <br/>
        /// <c>true</c> should be returned when the handler could resolve the next member, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
            value = null;
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Attempts to process the new value for the current instance slot. <br/>
        /// <c>true</c> should be returned when the handler could process the value, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> ProcessValue(ref object?[] values, int valueIdx, object? value, Type currentType, ref int memberIdx, string[] memberArgs, bool needsFlush) {
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Attempts to resolve an instance into an actual target object / type. <br/>
        /// <c>true</c> should be returned when the handler could resolve the target, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> ResolveTarget(object instance, out object targetObject, out Type targetType) {
            targetObject = null!;
            targetType = null!;
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Attempts to resolve the target types of the parameters for the next member. <br/>
        /// <c>true</c> should be returned when the handler could resolve the target types, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
            targetTypes = [];
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Attempts to set the value of the next member to the target value. <br/>
        /// <c>true</c> should be returned when the handler could set the next member, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Attempts to invoke the next member with the target parameter values. <br/>
        /// <c>true</c> should be returned when the handler could invoke the next member, otherwise <c>false</c>.
        public virtual Result<bool, MemberAccessError> InvokeMember(object? instance, object?[] parameterValue, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
            return Result<bool, MemberAccessError>.Ok(false);
        }

        /// Allows for post-processing after the value has been set
        public virtual VoidResult<MemberAccessError> PostProcessSetMember(object targetObject, object? value, string[] memberArgs, bool forceAllowCodeExecution) {
            return VoidResult<MemberAccessError>.Ok;
        }

        /// Allows for post-processing after the method has been invoked
        public virtual VoidResult<MemberAccessError> PostProcessInvokeMember(object targetObject, object?[] parameterValues, string[] memberArgs, bool forceAllowCodeExecution) {
            return VoidResult<MemberAccessError>.Ok;
        }

        /// Attempts to resolve a value from a string for the target type. <br/>
        /// Only invoked if <see cref="CanResolveValue"/> returned <c>true</c> for the type. <br/>
        /// <c>true</c> should be returned when the handler could a value, otherwise <c>false</c>.
        public virtual Result<bool, QueryError> ResolveValue(Type targetType, ref int argIdx, string[] valueArgs, out object? value) {
            value = null;
            return Result<bool, QueryError>.Ok(false);
        }

        /// Provide a list of auto-complete entries which should be listed along base-types.
        [MustDisposeResource]
        public virtual IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(string[] queryArgs, string queryPrefix, Variant variant, Type[]? targetTypeFilter) {
            yield break;
        }

        /// Overwrite the list of auto-complete entries which are provided for the members of the type
        /// Only invoked if <see cref="CanEnumerateMemberEntries"/> returned <c>true</c> for the type. <br/>
        [MustDisposeResource]
        public virtual IEnumerator<CommandAutoCompleteEntry> EnumerateMemberEntries(Type type, Variant variant) {
            yield break;
        }
        /// Overwrite the list of auto-complete entries which are provided for the values of the type
        /// Only invoked if <see cref="CanEnumerateTypeEntries"/> returned <c>true</c> for the type. <br/>
        [MustDisposeResource]
        public virtual IEnumerator<CommandAutoCompleteEntry> EnumerateTypeEntries(Type type, Variant variant) {
            yield break;
        }
    }

    /// Prevents invocations of methods / execution of Lua code in the Custom Info
    public static bool PreventCodeExecution => EnforceLegalCommand.EnabledWhenRunning;

    internal static readonly Dictionary<string, HashSet<Type>> AllTypes = new();
    internal static readonly Dictionary<string, (HashSet<Type> Types, string[] MemberArgs)> BaseTypeCache = [];

    internal static readonly Handler[] Handlers = [
        new SettingsQueryHandler(),
        new SaveDataQueryHandler(),
        new AssistsQueryHandler(),
        new ExtendedVariantsQueryHandler(),
        new EverestModuleSettingsQueryHandler(),
        new EverestModuleSessionQueryHandler(),
        new EverestModuleSaveDataQueryHandler(),
        new SceneQueryHandler(),
        new SessionQueryHandler(),
        new EntityQueryHandler(),
        new ComponentQueryHandler(),
        new CollectionQueryHandler(),
        new SpecialValueQueryHandler(),
        new DeterministicVariablesQueryHandler(),
        new ModInteropQueryHandler(),
    ];

    [Initialize(ConsoleEnhancements.InitializePriority + 1)]
    private static void CollectAllTypes() {
        AllTypes.Clear();
        BaseTypeCache.Clear();

        foreach (var type in ModUtils.GetTypes()) {
            if (type.FullName is { } fullName) {
                string assemblyName = type.Assembly.GetName().Name!;
                string modName = ConsoleEnhancements.GetModName(type);

                // Use '.' instead of '+' for nested types
                fullName = fullName.Replace('+', '.');

                AllTypes.AddToKey(fullName, type);
                AllTypes.AddToKey($"{fullName}@{assemblyName}", type);
                AllTypes.AddToKey($"{fullName}@{modName}", type);

                // Strip namespace
                if (type.Namespace != null) {
                    int namespaceLen = type.Namespace != null
                        ? type.Namespace.Length + 1
                        : 0;
                    string shortName = fullName[namespaceLen..];
                    AllTypes.AddToKey(shortName, type);
                    AllTypes.AddToKey($"{shortName}@{assemblyName}", type);
                    AllTypes.AddToKey($"{shortName}@{modName}", type);
                }
            }
        }
    }

    [MonocleCommand("get", "'get Type.fieldOrProperty' -> value | Example: 'get Player.Position', 'get Level.Wind' (CelesteTAS)"), UsedImplicitly]
    private static void GetCmd() {
        if (!CommandLine.TryParse(Engine.Commands.commandHistory[0], out var commandLine)) {
            "Get Command Failed: Couldn't parse arguments of command".ConsoleLog(LogLevel.Error);
            return;
        }

        if (commandLine.Arguments.Length == 0) {
            "Get Command Failed: No target-query specified".ConsoleLog(LogLevel.Error);
            return;
        }

        string query = string.Join(commandLine.ArgumentSeparator, commandLine.Arguments);

        var result = GetMemberValues(query);
        if (result.Failure) {
            $"Get Command Failed: {result.Error}".ConsoleLog(LogLevel.Error);
            return;
        }

        if (result.Value.Count == 0) {
            "Get Command Failed: No instances found".ConsoleLog(LogLevel.Error);
        } else if (result.Value.Count == 1) {
            result.Value[0].Value.ConsoleLog();
        } else {
            foreach ((object? baseInstance, object? value) in result.Value) {
                if (baseInstance is Entity entity && !string.IsNullOrEmpty(entity.SourceId.Level)) {
                    $"[{entity.SourceId}] {value}".ConsoleLog();
                } else {
                    value.ConsoleLog();
                }
            }
        }
    }

    /// Parses a target-query and returns the results for that
    internal static Result<List<(object BaseInstance, object? Value)>, QueryError> GetMemberValues(string query, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs);
        if (baseTypes.IsEmpty()) {
            return Result<List<(object BaseInstance, object? Value)>, QueryError>.Fail(new QueryError.NoBaseTypes(query));
        }

        List<(object BaseInstance, object? Value)> allResults = [];
        MemberAccessError? error = null;

        foreach (var baseType in baseTypes) {
            object[] instances = ResolveTypeInstances(baseType);
            foreach (object instance in instances) {
                if (instance is Type && memberArgs.Length == 0) {
                    error = MemberAccessError.Aggregate(error, new MemberAccessError.NoMembers());
                    continue;
                }

                var result = GetMemberValue(instance, memberArgs, forceAllowCodeExecution);
                if (result.Failure) {
                    if (result.Error is MemberAccessError accessError) {
                        error = MemberAccessError.Aggregate(error, accessError);
                    } else {
                        return Result<List<(object BaseInstance, object? Value)>, QueryError>.Fail(result.Error);
                    }

                    continue;
                }

                allResults.AddRange(result.Value.Where(value => value != InvalidValue && value is not QueryError).Select(value => (instance, value)));
            }
        }

        if (error == null || allResults.Count != 0) {
            return Result<List<(object BaseInstance, object? Value)>, QueryError>.Ok(allResults);
        }

        return Result<List<(object BaseInstance, object? Value)>, QueryError>.Fail(error);
    }
    /// Parses a target-query and sets the value to the arguments
    internal static VoidResult<QueryError> SetMemberValues(string query, string[] arguments, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs);
        if (baseTypes.IsEmpty()) {
            return VoidResult<QueryError>.Fail(new QueryError.NoBaseTypes(query));
        }
        if (memberArgs.IsEmpty()) {
            return VoidResult<QueryError>.Fail(new MemberAccessError.NoMembers());
        }

        bool anySuccessful = false;
        MemberAccessError? error = null;

        var targetValueCache = new Dictionary<Type, object?>();

        foreach (var baseType in baseTypes) {
            object[] instances = ResolveTypeInstances(baseType);
            foreach (object instance in instances) {
                var memberResult = PrepareMemberValue(instance, memberArgs[..^1]);
                if (memberResult.Failure) {
                    if (memberResult.Error is MemberAccessError accessError) {
                        error = MemberAccessError.Aggregate(error, accessError);
                    } else {
                        return VoidResult<QueryError>.Fail(memberResult.Error);
                    }

                    continue;
                }

                foreach (object? target in memberResult.Value.Where(value => value != null && value != InvalidValue && value is not QueryError)) {
                    var targetTypeResult = ResolveMemberTargetTypes(target!, memberArgs.Length - 1, memberArgs, Variant.Set, arguments.Length);
                    if (targetTypeResult.Failure) {
                        error = MemberAccessError.Aggregate(error, targetTypeResult.Error);
                        continue;
                    }

                    var targetTypes = targetTypeResult.Value;
                    if (!targetValueCache.TryGetValue(targetTypes[0], out object? value)) {
                        var valueResult = ResolveValue(arguments, targetTypes);
                        if (valueResult.Failure) {
                            return VoidResult<QueryError>.Fail(valueResult.Error);
                        }

                        targetValueCache[targetTypes[0]] = value = valueResult.Value[0];
                    }

                    var setResult = SetMember(target!, value, memberArgs);
                    if (setResult.Failure) {
                        error = MemberAccessError.Aggregate(error, setResult.Error);
                    } else {
                        anySuccessful = true;
                    }
                }
            }
        }

        if (!anySuccessful && error != null) {
            return VoidResult<QueryError>.Fail(error);
        }

        return VoidResult<QueryError>.Ok;
    }
    /// Parses a target-query and invokes the method with the arguments on them
    internal static VoidResult<QueryError> InvokeMemberMethods(string query, string[] arguments, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs);
        if (baseTypes.IsEmpty()) {
            return VoidResult<QueryError>.Fail(new QueryError.NoBaseTypes(query));
        }
        if (memberArgs.IsEmpty()) {
            return VoidResult<QueryError>.Fail(new MemberAccessError.NoMembers());
        }

        bool anySuccessful = false;
        MemberAccessError? error = null;

        var targetValueCache = new Dictionary<Type[], object?[]>();

        foreach (var baseType in baseTypes) {
            object[] instances = ResolveTypeInstances(baseType);
            foreach (object instance in instances) {
                var memberResult = PrepareMemberValue(instance, memberArgs[..^1]);
                if (memberResult.Failure) {
                    if (memberResult.Error is MemberAccessError accessError) {
                        error = MemberAccessError.Aggregate(error, accessError);
                    } else {
                        return VoidResult<QueryError>.Fail(memberResult.Error);
                    }

                    continue;
                }

                foreach (object? target in memberResult.Value.Where(value => value != null && value != InvalidValue && value is not QueryError)) {
                    var targetTypeResult = ResolveMemberTargetTypes(target!, memberArgs.Length - 1, memberArgs, Variant.Invoke, arguments.Length);
                    if (targetTypeResult.Failure) {
                        error = MemberAccessError.Aggregate(error, targetTypeResult.Error);
                        continue;
                    }

                    var targetTypes = targetTypeResult.Value;
                    if (!targetValueCache.TryGetValue(targetTypes, out object?[]? values)) {
                        var valueResult = ResolveValue(arguments, targetTypes);
                        if (valueResult.Failure) {
                            return VoidResult<QueryError>.Fail(valueResult.Error);
                        }

                        targetValueCache[targetTypes] = values = valueResult.Value;
                    }

                    var invokeResult = InvokeMember(target!, values, memberArgs);
                    if (invokeResult.Failure) {
                        error = MemberAccessError.Aggregate(error, invokeResult.Error);
                    } else {
                        anySuccessful = true;
                    }
                }
            }
        }

        if (!anySuccessful && error != null) {
            return VoidResult<QueryError>.Fail(error);
        }

        return VoidResult<QueryError>.Ok;
    }

    #region Auto-Complete

    /// Sorts types by namespace into Celeste -> Monocle -> other (alphabetically)
    /// Inside the namespace it's sorted alphabetically
    private class NamespaceComparer : IComparer<(string Name, Type Type)> {
        public int Compare((string Name, Type Type) x, (string Name, Type Type) y) {
            if (x.Type.Namespace == null && y.Type.Namespace != null) {
                return -1;
            }
            if (x.Type.Namespace != null && y.Type.Namespace == null) {
                return 1;
            }

            if (x.Type.Namespace == null || y.Type.Namespace == null) {
                return StringComparer.Ordinal.Compare(x.Name, y.Name);
            }

            int namespaceCompare = CompareNamespace(x.Type.Namespace, y.Type.Namespace);
            if (namespaceCompare != 0) {
                return namespaceCompare;
            }

            return StringComparer.Ordinal.Compare(x.Name, y.Name);
        }

        private int CompareNamespace(string x, string y) {
            if (x.StartsWith("Celeste") && y.StartsWith("Celeste")) return 0;
            if (x.StartsWith("Celeste")) return -1;
            if (y.StartsWith("Celeste")) return  1;
            if (x.StartsWith("Monocle") && y.StartsWith("Monocle")) return 0;
            if (x.StartsWith("Monocle")) return -1;
            if (y.StartsWith("Monocle")) return  1;
            return StringComparer.Ordinal.Compare(x, y);
        }
    }

    internal static readonly string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod", "Snowberry"];

    private const int MaxTypeViabilityRecursion = 3;

    /// Checks if a value for the target type could be resolved and uses that to guess if this is the final type
    private static bool GuessIsFinal(Type type) {
        return type.IsAssignableFrom(typeof(string))
               || type.IsPrimitive || type == typeof(decimal)
               || type.IsEnum
               || Handlers.Any(handler => handler.CanResolveValue(type));
    }

    internal static bool IsTypeViable(Type type, Variant variant, bool isRoot, Type[]? targetTypeFilter, int maxDepth) {
        // Filter-out types which probably aren't useful / possible
        if (!(type.IsClass || type.IsStructType()) || type.IsGenericType || type.FullName == null || (type.Namespace != null && ignoredNamespaces.Any(ns => type.Namespace.StartsWith(ns)))) {
            return false;
        }
        // Filter-out compiler generated types
        if (type.GetCustomAttributes<CompilerGeneratedAttribute>().IsNotEmpty() || type.FullName!.Contains('<') || type.FullName.Contains('>')) {
            return false;
        }

        if (maxDepth <= 0) {
            return true; // Dont recurse further
        }

        // Require some viable members
        var bindingFlags = isRoot
            ? Handlers.Any(handler => handler.CanResolveInstances(type))
                ? ReflectionExtensions.StaticInstanceAnyVisibility
                : ReflectionExtensions.StaticAnyVisibility
            : ReflectionExtensions.InstanceAnyVisibility;

        return EnumerateViableFields(type, variant, bindingFlags, targetTypeFilter, maxDepth).Any() ||
               EnumerateViableProperties(type, variant, bindingFlags, targetTypeFilter, maxDepth).Any() ||
               EnumerateViableMethods(type, variant, bindingFlags, targetTypeFilter, maxDepth).Any();
    }
    internal static bool IsFieldViable(FieldInfo field, Variant variant, bool isFinal, Type[]? targetTypeFilter, int maxDepth) {
        return IsFieldUsable(field, variant, isFinal) && variant switch {
            Variant.Get or Variant.Set =>
                targetTypeFilter == null
                || targetTypeFilter.Any(type => field.FieldType.CanCoerceTo(type))
                || IsTypeViable(field.FieldType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            // We don't know the return type of the delegate, so assume viable
            Variant.Invoke =>
                isFinal
                || targetTypeFilter == null
                || targetTypeFilter.Any(type => field.FieldType.CanCoerceTo(type))
                || IsTypeViable(field.FieldType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsPropertyViable(PropertyInfo property, Variant variant, bool isFinal, Type[]? targetTypeFilter, int maxDepth) {
        return IsPropertyUsable(property, variant, isFinal) && variant switch {
            Variant.Get or Variant.Set =>
                targetTypeFilter == null
                || targetTypeFilter.Any(type => property.PropertyType.CanCoerceTo(type))
                || IsTypeViable(property.PropertyType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            // We don't know the return type of the delegate, so assume viable
            Variant.Invoke =>
                isFinal
                || targetTypeFilter == null
                || targetTypeFilter.Any(type => property.PropertyType.CanCoerceTo(type))
                || IsTypeViable(property.PropertyType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsMethodViable(MethodInfo method, Variant variant, bool isFinal, Type[]? targetTypeFilter, int maxDepth) {
        return IsMethodUsable(method, variant, isFinal) && variant switch {
            Variant.Get or Variant.Set =>
                targetTypeFilter == null
                || targetTypeFilter.Any(type => method.ReturnType.CanCoerceTo(type))
                || IsTypeViable(method.ReturnType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            Variant.Invoke =>
                isFinal
                || targetTypeFilter == null
                || targetTypeFilter.Any(type => method.ReturnType.CanCoerceTo(type))
                || IsTypeViable(method.ReturnType, variant, isRoot: false, targetTypeFilter, maxDepth - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }

    internal static IEnumerable<FieldInfo> EnumerateViableFields(Type type, Variant variant, BindingFlags bindingFlags, Type[]? targetTypeFilter, int maxDepth) {
        return type
            .GetAllFieldInfos(bindingFlags)
            .Where(f =>
                // Filter-out compiler generated fields
                f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>') &&
                // Require to be viable
                IsFieldViable(f, variant, GuessIsFinal(f.FieldType), targetTypeFilter, maxDepth));
    }
    internal static IEnumerable<PropertyInfo> EnumerateViableProperties(Type type, Variant variant, BindingFlags bindingFlags, Type[]? targetTypeFilter, int maxDepth) {
        return type
            .GetAllPropertyInfos(bindingFlags)
            .Where(p =>
                // Filter-out compiler generated properties
                p.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !p.Name.Contains('<') && !p.Name.Contains('>') &&
                // Require to be viable
                IsPropertyViable(p, variant, GuessIsFinal(p.PropertyType), targetTypeFilter, maxDepth));
    }
    internal static IEnumerable<MethodInfo> EnumerateViableMethods(Type type, Variant variant, BindingFlags bindingFlags, Type[]? targetTypeFilter, int maxDepth) {
        return type
            .GetAllMethodInfos(bindingFlags)
            .Where(m =>
                // Filter-out compiler generated fields
                m.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !m.Name.Contains('<') && !m.Name.Contains('>') &&
                // Require to be viable
                IsMethodViable(m, variant, isFinal: true, targetTypeFilter, maxDepth));
    }

    internal static IEnumerator<CommandAutoCompleteEntry> ResolveAutoCompleteEntries(string[] queryArgs, Variant variant, Type[]? targetTypeFilter = null) {
        string queryPrefix = queryArgs.Length != 0 ? $"{string.Join('.', queryArgs)}." : "";

        if (variant == Variant.Get && targetTypeFilter != null) {
            foreach (var targetType in targetTypeFilter) {
                foreach (var handler in Handlers.Where(handler => handler.CanEnumerateTypeEntries(targetType, variant))) {
                    using var enumerator = handler.EnumerateTypeEntries(targetType, variant);
                    while (enumerator.MoveNext()) {
                        yield return enumerator.Current;
                    }
                    goto NextType;
                }

                if (targetType == typeof(bool)) {
                    yield return new CommandAutoCompleteEntry { Name = "true", Extra = targetType.CSharpName(), IsDone = true };
                    yield return new CommandAutoCompleteEntry { Name = "false", Extra = targetType.CSharpName(), IsDone = true };
                } else if (targetType.IsEnum) {
                    foreach (object value in Enum.GetValues(targetType)) {
                        yield return new CommandAutoCompleteEntry { Name = value.ToString()!, Extra = targetType.CSharpName(), IsDone = true };
                    }
                }

                NextType:;
            }
        }

        {
            using var enumerator = ResolveBaseTypeAutoCompleteEntries(queryArgs, queryPrefix, variant, targetTypeFilter);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }
        }

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs);
        foreach (var baseType in baseTypes) {
            // Recurse type
            var currentType = RecurseMemberType(baseType, memberArgs, variant);
            if (currentType == null) {
                yield break;
            }

            foreach (var handler in Handlers.Where(handler => handler.CanEnumerateMemberEntries(currentType, variant))) {
                using var enumerator = handler.EnumerateMemberEntries(currentType, variant);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current with {
                        Name = enumerator.Current.IsDone ? enumerator.Current.Name : enumerator.Current.Name + ".",
                        Prefix = queryPrefix,
                    };
                }
                goto NextType;
            }

            // Generic handler
            {
                var bindingFlags = memberArgs.Length == 0
                    ? Handlers.Any(handler => handler.CanResolveInstances(currentType))
                        ? ReflectionExtensions.StaticInstanceAnyVisibility
                        : ReflectionExtensions.StaticAnyVisibility
                    : ReflectionExtensions.InstanceAnyVisibility;

                foreach (var field in EnumerateViableFields(currentType, variant, bindingFlags, targetTypeFilter, maxDepth: MaxTypeViabilityRecursion).OrderBy(f => f.Name)) {
                    bool isFinal = GuessIsFinal(field.FieldType) && (targetTypeFilter == null || targetTypeFilter.Any(type => field.FieldType.CanCoerceTo(type)));
                    yield return new CommandAutoCompleteEntry {
                        Name = isFinal ? field.Name : field.Name + ".",
                        Extra = field.FieldType.CSharpName(),
                        Prefix = queryPrefix,
                        IsDone = isFinal,
                    };
                }
                foreach (var property in EnumerateViableProperties(currentType, variant, bindingFlags, targetTypeFilter, maxDepth: MaxTypeViabilityRecursion).OrderBy(p => p.Name)) {
                    bool isFinal = GuessIsFinal(property.PropertyType) && (targetTypeFilter == null || targetTypeFilter.Any(type => property.PropertyType.CanCoerceTo(type)));
                    yield return new CommandAutoCompleteEntry {
                        Name = isFinal ? property.Name : property.Name + ".",
                        Extra = property.PropertyType.CSharpName(),
                        Prefix = queryPrefix,
                        IsDone = isFinal,
                    };
                }
                foreach (var method in EnumerateViableMethods(currentType, variant, bindingFlags, targetTypeFilter, maxDepth: MaxTypeViabilityRecursion).OrderBy(m => m.Name)) {
                    yield return new CommandAutoCompleteEntry {
                        Name = method.Name,
                        Extra = $"({string.Join(", ", method.GetParameters().Select(p => p.HasDefaultValue ? $"[{p.ParameterType.CSharpName()}]" : p.ParameterType.CSharpName()))})",
                        Prefix = queryPrefix,
                        IsDone = true,
                    };
                }
            }

            NextType:;
        }
    }
    private static IEnumerator<CommandAutoCompleteEntry> ResolveBaseTypeAutoCompleteEntries(string[] queryArgs, string queryPrefix, Variant variant, Type[]? targetTypeFilter) {
        foreach (var handler in Handlers) {
            using var enumerator = handler.ProvideGlobalEntries(queryArgs, queryPrefix, variant, targetTypeFilter);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }
        }

        var types = ModUtils.GetTypes()
            .Where(type =>
                IsTypeViable(type, variant, isRoot: true, targetTypeFilter, maxDepth: MaxTypeViabilityRecursion) &&
                // Require query-arguments to match namespace
                type.FullName!.StartsWith(queryPrefix))
            .OrderBy(t => (t.CSharpName(), t), new NamespaceComparer())
            .ToArray();

        string[][] namespaces = types
            .Select<Type, string?>(type => type.Namespace)
            .OfType<string>()
            .Distinct()
            .Select(ns => ns.Split('.'))
            .Where(ns => ns.Length > queryArgs.Length)
            .ToArray();

        // Merge the lowest common namespaces (we love triple nested loops!)
        for (int nsIdxA = 0; nsIdxA < namespaces.Length; nsIdxA++) {
            for (int compLen = namespaces[nsIdxA].Length; compLen > queryArgs.Length; compLen--) {
                string[] subSeq = namespaces[nsIdxA][..compLen];
                bool foundAny = false;

                for (int nsIdxB = 0; nsIdxB < namespaces.Length; nsIdxB++) {
                    if (nsIdxA == nsIdxB || namespaces[nsIdxB].Length < compLen) {
                        continue;
                    }

                    if (namespaces[nsIdxB].SequenceStartsWith(subSeq)) {
                        foundAny = true;
                        namespaces[nsIdxB] = [];
                    }
                }

                if (foundAny) {
                    namespaces[nsIdxA] = subSeq;
                }
            }
        }

        foreach (string[] ns in namespaces) {
            if (ns.Length <= queryArgs.Length) {
                continue;
            }

            yield return new CommandAutoCompleteEntry { Name = $"{string.Join('.', ns[queryArgs.Length..])}.", Extra = "Namespace", Prefix = queryPrefix, IsDone = false };
        }

        foreach (var type in types) {
            if (queryPrefix.Length != 0 && type.Namespace!.Length + 1 != queryPrefix.Length) {
                // Require exact prefix if specified
                continue;
            }

            string assemblyName = type.Assembly.GetName().Name!;
            string modName = ConsoleEnhancements.GetModName(type);

            // Use '.' instead of '+' for nested types
            string fullName = type.FullName!.Replace('+', '.');

            // Strip namespace
            int namespaceLen = type.Namespace != null
                ? type.Namespace.Length + 1
                : 0;
            string shortName = fullName[namespaceLen..];

            // Use short name if possible, otherwise specify mod name / assembly name
            if (AllTypes[shortName].Count == 1) {
                yield return new CommandAutoCompleteEntry { Name = $"{shortName}.", Extra = type.Namespace ?? string.Empty, Prefix = queryPrefix, IsDone = false };
            } else if (AllTypes[$"{shortName}@{modName}"].Count == 1) {
                yield return new CommandAutoCompleteEntry { Name = $"{shortName}@{modName}.", Extra = type.Namespace ?? string.Empty, Prefix = queryPrefix, IsDone = false };
            } else if (AllTypes[$"{shortName}@{assemblyName}"].Count == 1) {
                yield return new CommandAutoCompleteEntry { Name = $"{shortName}@{assemblyName}.", Extra = type.Namespace ?? string.Empty, Prefix = queryPrefix, IsDone = false };
            }
        }
    }

    internal static Type? RecurseMemberType(Type baseType, string[] memberArgs, Variant variant) {
        var currentType = baseType;
        for (int memberIdx = 0; memberIdx < memberArgs.Length; memberIdx++) {
            foreach (var handler in Handlers) {
                var result = handler.ResolveTargetTypes(out var targetTypes, currentType, memberIdx, memberArgs);
                if (result.Success && result.Value) {
                    currentType = targetTypes[0]; // Ignore others
                    goto NextMember;
                }
                if (result.Failure) {
                    return null;
                }
            }

            string member = memberArgs[memberIdx];
            var bindingFlags = memberIdx == 0
                ? Handlers.Any(handler => handler.CanResolveInstances(currentType))
                    ? ReflectionExtensions.StaticInstanceAnyVisibility
                    : ReflectionExtensions.StaticAnyVisibility
                : ReflectionExtensions.InstanceAnyVisibility;

            if (currentType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, variant, isFinal: false)) {
                currentType = field.FieldType;
                continue;
            }
            if (currentType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, variant, isFinal: false)) {
                currentType = property.PropertyType;
                continue;
            }

            NextMember:;
        }
        return currentType;
    }

    #endregion
    #region Helpers

    private static bool IsSettableType(Type type) => !type.IsSameOrSubclassOf(typeof(Delegate));
    private static bool IsInvokableMethod(MethodInfo info) {
        // Generic methods could probably be supported somehow, but that's probably not worth
        if (info.IsGenericMethod) {
            return false;
        }
        // To be invokable, all parameters need to be settable or have a default value from a non-settable onwards
        bool requireDefaults = false;
        foreach (var param in info.GetParameters()) {
            if (!requireDefaults && !IsSettableType(param.ParameterType)) {
                requireDefaults = true;
            }

            if (requireDefaults && !param.HasDefaultValue) {
                return false;
            }
        }

        return true;
    }

    internal static bool IsFieldUsable(FieldInfo field, Variant variant, bool isFinal) {
        return variant switch {
            Variant.Get => true,
            Variant.Set => isFinal
                ? (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0 && IsSettableType(field.FieldType)
                : (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0 || !field.FieldType.IsValueType,
            Variant.Invoke => !isFinal || field.FieldType.IsSameOrSubclassOf(typeof(Delegate)),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsPropertyUsable(PropertyInfo property, Variant variant, bool isFinal) {
        return variant switch {
            Variant.Get => property.CanRead,
            Variant.Set => isFinal
                ? property.CanWrite && IsSettableType(property.PropertyType)
                : property.CanRead && (property.CanWrite || !property.PropertyType.IsValueType),
            Variant.Invoke => !isFinal || property.PropertyType.IsSameOrSubclassOf(typeof(Delegate)),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsMethodUsable(MethodInfo method, Variant variant, bool isFinal) {
        return variant switch {
            Variant.Get => false,
            Variant.Set => false,
            Variant.Invoke => isFinal && IsInvokableMethod(method),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }

    #endregion

    /// Parses the first part of a query into types and an optional EntityID
    public static HashSet<Type> ResolveBaseTypes(string[] queryArgs, out string[] memberArgs) {
        if (queryArgs.Length == 0) {
            memberArgs = queryArgs;
            return [];
        }

        queryArgs = Handlers.Aggregate((IEnumerable<string>) queryArgs, (current, handler) => handler.ProcessQueryArguments(current)).ToArray();
        if (queryArgs.Any(arg => arg == InvalidQueryArgument)) {
            memberArgs = queryArgs;
            return [];
        }

        string fullQueryArgs = string.Join('.', queryArgs);
        if (BaseTypeCache.TryGetValue(fullQueryArgs, out var cache)) {
            memberArgs = cache.MemberArgs;
            return cache.Types;
        }

        foreach (var handler in Handlers) {
            if (handler.ResolveBaseTypes(queryArgs) is { } result) {
                BaseTypeCache[fullQueryArgs] = result;

                memberArgs = result.MemberArgs;
                return result.Types;
            }
        }

        return ParseGenericBaseTypes(queryArgs, out memberArgs);
    }

    /// Parses query-arguments into a list of types, while only searching for generic .NET types
    /// Does not reference any defined special-case handlers
    internal static HashSet<Type> ParseGenericBaseTypes(string[] queryArgs, out string[] memberArgs) {
        string fullQueryArgs = string.Join('.', queryArgs);

        if (BaseTypeCache.TryGetValue(fullQueryArgs, out var cache)) {
            memberArgs = cache.MemberArgs;
            return cache.Types;
        }

        // Decrease query arguments until a match is found
        for (int i = queryArgs.Length; i > 0; i--) {
            bool isFirst = i == queryArgs.Length;
            string typeName = isFirst
                ? fullQueryArgs
                : string.Join('.', queryArgs, startIndex: 0, count: i);

            if (!isFirst && BaseTypeCache.TryGetValue(typeName, out cache) && cache.MemberArgs.Length == 0) {
                memberArgs = queryArgs[i..];
                return cache.Types;
            }

            if (AllTypes.TryGetValue(typeName, out var types)) {
                memberArgs = queryArgs[i..];

                BaseTypeCache[typeName] = (types, []);
                BaseTypeCache[fullQueryArgs] = (types, memberArgs);
                return types;
            }
        }

        // No matching type found
        memberArgs = queryArgs;
        return [];
    }

    /// Resolves a type into all applicable instances of it
    /// Returns null for types which are always in a static context
    public static object[] ResolveTypeInstances(Type type) {
        if (Handlers.FirstOrDefault(h => h.CanResolveInstances(type)) is { } handler) {
            return handler.ResolveInstances(type);
        }

        // No instances available
        return [type];
    }

    /// Value in the result array which is not valid, but was kept in the array to avoid allocations
    internal static readonly object InvalidValue = new();
    /// Indicator used to abort query-argument post-processing from an IEnumerable
    internal const string InvalidQueryArgument = "___INVALID___";

    internal record QueryError {
        public record NoBaseTypes(string Query) : QueryError {
            public override string ToString() => $"No base types found for query '{Query}'";
        }
        public record TooManyArguments : QueryError {
            public override string ToString() => "Too many arguments specified";
        }
        public record UnknownException(Exception Exception) : QueryError {
            public override string ToString() => $"Unknown exception: {Exception}";
        }
        public record Custom(string ErrorMessage) : QueryError {
            public override string ToString() => ErrorMessage;
        }
        public record InvalidEnumState(Type EnumType, string Member) : QueryError {
            public override string ToString() => $"'{Member}' is not a valid enum state for '{EnumType}'";
        }
        public record InstanceCountMismatch(string Query, int ExpectedCount, int ActualCount) : QueryError {
            public override string ToString() => ActualCount == 0
                ? ExpectedCount == 1
                    ? $"'Query {Query}' returned no values, instead of 1"
                    : $"'Query {Query}' returned no values, instead of a maximum of {ExpectedCount}"
                : ExpectedCount == 1
                    ? $"'Query {Query}' returned {ActualCount} values, instead of 1"
                    : $"'Query {Query}' returned {ActualCount} values, instead of a maximum of {ExpectedCount}";
        }
        public record ConversionError(string Argument, Type TargetType) : QueryError {
            public override string ToString() => $"Failed to convert argument '{Argument}' into target-type '{TargetType}'";
        }
    }

    internal record MemberAccessError(Type type, int memberIndex) : QueryError {
        public record NoMembers() : MemberAccessError(typeof(object), -1) {
            public override string ToString() => "No members specified";
        }
        public record CodeExecutionNotAllowed : MemberAccessError {
            private readonly string[] MemberArgs;

            public CodeExecutionNotAllowed(Type type, int memberIndex, string[] memberArgs) : base(type, memberIndex) {
                MemberArgs = memberArgs;
            }

            public override string ToString() => $"Cannot safely access member '{MemberArgs[MemberIndex]}' on type '{Type}' during EnforceLegal";
        }
        public new record UnknownException : MemberAccessError {
            private readonly string[] MemberArgs;
            private readonly Exception Exception;

            public UnknownException(Type? type, int memberIndex, string[] memberArgs, Exception ex) : base(type ?? typeof(object), memberIndex) {
                MemberArgs = memberArgs;
                Exception = ex;
            }

            public override string ToString() => $"Unknown exception while accessing member '{MemberArgs[MemberIndex]}' on type '{Type}': {Exception}";
        }
        public new record Custom : MemberAccessError {
            private readonly string ErrorMessage;

            public Custom(Type type, int memberIndex, string errorMessage) : base(type, memberIndex) {
                ErrorMessage = errorMessage;
            }

            public override string ToString() => ErrorMessage;
        }
        public record ReadOnlyCollection : MemberAccessError {
            private readonly string[] MemberArgs;

            public ReadOnlyCollection(Type type, int memberIndex, string[] memberArgs) : base(type, memberIndex) {
                MemberArgs = memberArgs;
            }

            public override string ToString() => $"Cannot modify readonly collection '{MemberArgs[MemberIndex]}', containing value-types, on type '{Type}'";
        }
        public record MemberNotFound : MemberAccessError {
            private readonly string[] MemberArgs;
            private readonly BindingFlags BindingFlags;

            public MemberNotFound(Type type, int memberIndex, string[] memberArgs, BindingFlags bindingFlags) : base(type, memberIndex) {
                MemberArgs = memberArgs;
                BindingFlags = bindingFlags;
            }

            public override string ToString() => BindingFlags switch {
                ReflectionExtensions.InstanceAnyVisibility => $"Cannot find instance member '{MemberArgs[MemberIndex]}' on type '{Type}'",
                ReflectionExtensions.StaticAnyVisibility => $"Cannot find static member '{MemberArgs[MemberIndex]}' on type '{Type}'",
                ReflectionExtensions.StaticInstanceAnyVisibility => $"Cannot find instance / static member '{MemberArgs[MemberIndex]}' on type '{Type}'",
                _ => $"Cannot find member '{MemberArgs[MemberIndex]}' on type '{Type}'",
            };
        }

        public Type Type = type;
        public int MemberIndex = memberIndex;

        public static MemberAccessError Aggregate(MemberAccessError? accum, MemberAccessError error) {
            if (accum == null) {
                return error;
            }

            // 1. NoMembers
            // 2. CodeExecutionNotAllowed
            // 3. UnknownException
            // 4. Custom
            // 5. ReadOnlyCollection
            // 6. MemberNotFound
            if (accum is NoMembers && error is not NoMembers) {
                return accum;
            } else if (error is NoMembers && accum is not NoMembers) {
                return error;
            }
            if (accum is CodeExecutionNotAllowed && error is not CodeExecutionNotAllowed) {
                return accum;
            } else if (error is CodeExecutionNotAllowed && accum is not CodeExecutionNotAllowed) {
                return error;
            }
            if (accum is UnknownException && error is not UnknownException) {
                return accum;
            } else if (error is UnknownException && accum is not UnknownException) {
                return error;
            }
            if (accum is Custom && error is not Custom) {
                return accum;
            } else if (error is Custom && accum is not Custom) {
                return error;
            }
            if (accum is ReadOnlyCollection && error is not ReadOnlyCollection) {
                return accum;
            } else if (error is ReadOnlyCollection && accum is not ReadOnlyCollection) {
                return error;
            }

            // Prefer lower member index
            if (accum.MemberIndex < error.MemberIndex) {
                return accum;
            } else if (error.MemberIndex < accum.MemberIndex) {
                return error;
            }

            // Resolve common base type
            if (accum.Type.IsAssignableTo(error.Type)) {
                return error;
            }

            // This will always terminate when ret == typeof(object)
            while (!error.Type.IsAssignableTo(accum.Type)) {
                accum.Type = accum.Type.BaseType ?? typeof(object);
            }
            return accum;
        }
    }

    private static VoidResult<MemberAccessError> ResolveTargetObject(object instance, out object targetObject, out Type targetType) {
        foreach (var handler in Handlers) {
            var result = handler.ResolveTarget(instance, out targetObject, out targetType);
            if (result.Success && result.Value) {
                return VoidResult<MemberAccessError>.Ok;
            }
            if (result.Failure) {
                return VoidResult<MemberAccessError>.Fail(result.Error);
            }
        }

        targetObject = instance;
        targetType = instance as Type ?? instance.GetType();

        return VoidResult<MemberAccessError>.Ok;
    }

    /// Evaluates the member arguments on the base-instances and gets the specified value(s)
    internal static Result<object?[], QueryError> GetMemberValue(object instance, string[] memberArgs, bool forceAllowCodeExecution = false)
        => ResolveMemberValue(instance, memberArgs, forceAllowCodeExecution, needsFlush: false);

    /// Evaluates the member arguments on the base-instances and prepares the specified value(s)
    /// The values can then be modified with SetMemberValue
    internal static Result<object?[], QueryError> PrepareMemberValue(object instance, string[] memberArgs, bool forceAllowCodeExecution = false)
        => ResolveMemberValue(instance, memberArgs, forceAllowCodeExecution, needsFlush: true);

    private static Result<object?[], QueryError> ResolveMemberValue(object instance, string[] memberArgs, bool forceAllowCodeExecution, bool needsFlush) {
        object?[] values = [instance];

        for (int memberIdx = 0; memberIdx < memberArgs.Length; memberIdx++) {
            string member = memberArgs[memberIdx];
            bool isFinal = memberIdx == memberArgs.Length - 1 && !needsFlush;

            var bindingFlags = memberIdx == 0
                ? instance is Type
                    ? ReflectionExtensions.StaticAnyVisibility
                    : ReflectionExtensions.StaticInstanceAnyVisibility
                : ReflectionExtensions.InstanceAnyVisibility;

            // Forward to handlers
            try {
                foreach (var handler in Handlers) {
                    var result = handler.ResolveMemberValues(ref values, ref memberIdx, memberArgs);
                    if (result.Success && result.Value) {
                        goto NextMember;
                    }
                    if (result.Failure) {
                        return Result<object?[], QueryError>.Fail(result.Error);
                    }
                }
            } catch (Exception ex) {
                return Result<object?[], QueryError>.Fail(new MemberAccessError.UnknownException(null, memberIdx, memberArgs, ex));
            }

            // Resolve members individually
            for (int valueIdx = values.Length - 1; valueIdx >= 0; valueIdx--) {
                if (values[valueIdx] == null || values[valueIdx] == InvalidValue || values[valueIdx] is QueryError) {
                    continue;
                }

                var currentType = values[valueIdx] as Type ?? values[valueIdx]!.GetType();

                try {
                    foreach (var handler in Handlers) {
                        var result = handler.ResolveMember(values[valueIdx], out object? value, currentType, memberIdx, memberArgs);
                        if (result.Success && result.Value) {
                            ProcessValue(ref values, valueIdx, value, currentType, ref memberIdx, memberArgs, needsFlush);
                            goto NextValue;
                        }
                        if (result.Failure) {
                            values[valueIdx] = result.Error;
                            goto NextValue;
                        }
                    }

                    if (currentType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, needsFlush ? Variant.Set : Variant.Get, isFinal)) {
                        if (field.IsStatic) {
                            ProcessValue(ref values, valueIdx, field.GetValue(null), currentType, ref memberIdx, memberArgs, needsFlush);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                        } else {
                            ProcessValue(ref values, valueIdx, field.GetValue(values[valueIdx]), currentType, ref memberIdx, memberArgs, needsFlush);
                        }
                        continue;
                    }

                    if (currentType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, needsFlush ? Variant.Set : Variant.Get, isFinal)) {
                        if (PreventCodeExecution && !forceAllowCodeExecution) {
                            values[valueIdx] = new MemberAccessError.CodeExecutionNotAllowed(currentType, memberIdx, memberArgs);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                            continue;
                        }

                        if (property.IsStatic()) {
                            ProcessValue(ref values, valueIdx, property.GetValue(null), currentType, ref memberIdx, memberArgs, needsFlush);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                        } else {
                            ProcessValue(ref values, valueIdx, property.GetValue(values[valueIdx]), currentType, ref memberIdx, memberArgs, needsFlush);
                        }
                        continue;
                    }

                    values[valueIdx] = new MemberAccessError.MemberNotFound(currentType, memberIdx, memberArgs, bindingFlags);

                    // Invalidate all other instances of this type to avoid duplicates
                    InvalidateValues(ref values, valueIdx, currentType);

                    NextValue:;
                } catch (Exception ex) {
                    values[valueIdx] = new MemberAccessError.UnknownException(currentType, memberIdx, memberArgs, ex);

                    // Invalidate all other instances of this type to avoid duplicates
                    InvalidateValues(ref values, valueIdx, currentType);
                }
            }

            NextMember:;
        }

        if (values.Any(value => value is not MemberAccessError)) {
            return Result<object?[], QueryError>.Ok(values);
        }

        // No valid results. Aggregate errors
        return Result<object?[], QueryError>.Fail((MemberAccessError) values.Aggregate((accum, error) => {
            return MemberAccessError.Aggregate((MemberAccessError) accum!, (MemberAccessError) error!);
        })!);

        static void InvalidateValues(ref object?[] values, int valueIdx, Type currentType) {
            for (int otherValueIdx = valueIdx - 1; otherValueIdx >= 0; otherValueIdx--) {
                if (values[otherValueIdx]?.GetType() == currentType) {
                    values[otherValueIdx] = InvalidValue;
                }
            }
        }

        static void ProcessValue(ref object?[] values, int valueIdx, object? value, Type currentType, ref int memberIdx, string[] memberArgs, bool needsFlush) {
            foreach (var handler in Handlers) {
                var result = handler.ProcessValue(ref values, valueIdx, value, currentType, ref memberIdx, memberArgs, needsFlush);
                if (result.Success && result.Value) {
                    return;
                }
                if (result.Failure) {
                    values[valueIdx] = result.Error;
                    return;
                }
            }

            values[valueIdx] = value;
        }
    }

    internal static VoidResult<MemberAccessError> SetMember(object targetObject, object? value, string[] memberArgs, bool forceAllowCodeExecution = false) {
        if (ResolveTargetObject(targetObject, out object target, out var targetType).Error is { } err) {
            return VoidResult<MemberAccessError>.Fail(err);
        }

        try {
            string member = memberArgs[^1];

            var bindingFlags = memberArgs.Length == 1
                ? target is Type
                    ? ReflectionExtensions.StaticAnyVisibility
                    : ReflectionExtensions.StaticInstanceAnyVisibility
                : ReflectionExtensions.InstanceAnyVisibility;

            foreach (var handler in Handlers) {
                var result = handler.SetMember(target, value, targetType, memberArgs.Length - 1, memberArgs, forceAllowCodeExecution);
                if (result.Success && result.Value) {
                    goto PostProcess;
                }
                if (result.Failure) {
                    return VoidResult<MemberAccessError>.Fail(result.Error);
                }
            }

            if (targetType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, Variant.Set, isFinal: true)) {
                if (field.IsStatic) {
                    field.SetValue(null, value);
                } else {
                    field.SetValue(target, value);
                }

                goto PostProcess;
            } else if (targetType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, Variant.Set, isFinal: true)) {
                if (PreventCodeExecution && !forceAllowCodeExecution) {
                    return VoidResult<MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(targetType, memberArgs.Length - 1, memberArgs));
                }

                if (property.IsStatic()) {
                    property.SetValue(null, value);
                } else {
                    property.SetValue(target, value);
                }

                goto PostProcess;
            }

            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.MemberNotFound(targetType, memberArgs.Length - 1, memberArgs, bindingFlags));
        } catch (Exception ex) {
            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.UnknownException(targetType, memberArgs.Length - 1, memberArgs, ex));
        }

        PostProcess:
        foreach (var handler in Handlers) {
            var result = handler.PostProcessSetMember(targetObject, value, memberArgs, forceAllowCodeExecution);
            if (result.Failure) {
                return result;
            }
        }

        return VoidResult<MemberAccessError>.Ok;
    }

    internal static VoidResult<MemberAccessError> InvokeMember(object targetObject, object?[] parameterValues, string[] memberArgs, bool forceAllowCodeExecution = false) {
        if (ResolveTargetObject(targetObject, out object target, out var targetType).Error is { } err) {
            return VoidResult<MemberAccessError>.Fail(err);
        }

        if (PreventCodeExecution && !forceAllowCodeExecution) {
            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(targetType, memberArgs.Length - 1, memberArgs));
        }

        try {
            string member = memberArgs[^1];

            var bindingFlags = memberArgs.Length == 1
                ? target is Type
                    ? ReflectionExtensions.StaticAnyVisibility
                    : ReflectionExtensions.StaticInstanceAnyVisibility
                : ReflectionExtensions.InstanceAnyVisibility;

            foreach (var handler in Handlers) {
                var result = handler.InvokeMember(target, parameterValues, targetType, memberArgs.Length - 1, memberArgs, forceAllowCodeExecution);
                if (result.Success && result.Value) {
                    goto PostProcess;
                }
                if (result.Failure) {
                    return VoidResult<MemberAccessError>.Fail(result.Error);
                }
            }

            if (targetType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && field.FieldType.IsSameOrSubclassOf(typeof(Delegate))) {
                var del = (Delegate?) (field.IsStatic ? field.GetValue(null) : field.GetValue(target));
                del?.DynamicInvoke(parameterValues);

                goto PostProcess;
            } else if (targetType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property &&
                       property.PropertyType.IsSameOrSubclassOf(typeof(Delegate))) {
                var del = (Delegate?)(property.IsStatic() ? property.GetValue(null) : property.GetValue(target));
                del?.DynamicInvoke(parameterValues);

                goto PostProcess;
            } else if (ResolveMethod(targetType, member, bindingFlags, parameterValues.Length) is { Success: true, Value: {} method }) {
                if (method.IsStatic) {
                    method.Invoke(null, parameterValues);
                } else {
                    method.Invoke(target, parameterValues);
                }

                goto PostProcess;
            }

            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.MemberNotFound(targetType, memberArgs.Length - 1, memberArgs, bindingFlags));
        } catch (Exception ex) {
            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.UnknownException(targetType, memberArgs.Length - 1, memberArgs, ex));
        }

        PostProcess:
        foreach (var handler in Handlers) {
            var result = handler.PostProcessInvokeMember(targetObject, parameterValues, memberArgs, forceAllowCodeExecution);
            if (result.Failure) {
                return result;
            }
        }

        return VoidResult<MemberAccessError>.Ok;
    }

    private static Result<MethodInfo?, string> ResolveMethod(Type type, string member, BindingFlags bindingFlags, int argumentCount) {
        try {
            if (type.GetMethodInfo(member, parameterTypes: null, bindingFlags, logFailure: false) is { } method) {
                return Result<MethodInfo?, string>.Ok(method);
            }

            return Result<MethodInfo?, string>.Ok(null);
        } catch (AmbiguousMatchException) {
            var methods = type.GetAllMethodInfos()
                .Where(method => method.Name == member && method.GetParameters().Length == argumentCount)
                .ToArray();

            switch (methods.Length) {
                case 1: {
                    return Result<MethodInfo?, string>.Ok(methods[0]);
                }

                case 0: {
                    var builder = new StringBuilder($"No overload of method '{member}' on type '{type}' has {argumentCount} argument(s). Found");
                    foreach (var candidate in type.GetMethods().Where(method => method.Name == member)) {
                        builder.AppendLine($"- {candidate}");
                    }

                    return Result<MethodInfo?, string>.Fail(builder.ToString());
                }
                default: {
                    var builder = new StringBuilder($"Ambiguous overload of method '{member}' on type '{type}' with {argumentCount} argument(s). Found");
                    foreach (var candidate in type.GetMethods().Where(method => method.Name == member)) {
                        builder.AppendLine($"- {candidate}");
                    }

                    return Result<MethodInfo?, string>.Fail(builder.ToString());
                }
            }
        }
    }

    internal static Result<Type[], MemberAccessError> ResolveMemberTargetTypes(object instanceObject, int memberIdx, string[] memberArgs, Variant variant, int argumentCount, bool forceAllowCodeExecution = false) {
        if (ResolveTargetObject(instanceObject, out object instance, out var type).Error is { } err) {
            return Result<Type[], MemberAccessError>.Fail(err);
        }

        string member = memberArgs[memberIdx];
        bool isFinal = memberIdx == memberArgs.Length - 1;

        var bindingFlags = memberIdx == 0
            ? instance is Type
                ? ReflectionExtensions.StaticAnyVisibility
                : ReflectionExtensions.StaticInstanceAnyVisibility
            : ReflectionExtensions.InstanceAnyVisibility;

        try {
            foreach (var handler in Handlers) {
                var result = handler.ResolveTargetTypes(out var targetTypes, type, memberIdx, memberArgs);
                if (result.Success && result.Value) {
                    return Result<Type[], MemberAccessError>.Ok(targetTypes);
                }
                if (result.Failure) {
                    return Result<Type[], MemberAccessError>.Fail(result.Error);
                }
            }

            if (type.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, variant, isFinal)) {
                if (field.FieldType.IsSameOrSubclassOf(typeof(Delegate))) {
                    if (PreventCodeExecution && !forceAllowCodeExecution) {
                        return Result<Type[], MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(type, memberArgs.Length - 1, memberArgs));
                    }

                    if ((field.IsStatic ? field.GetValue(null) : field.GetValue(instance)) is Delegate del) {
                        return Result<Type[], MemberAccessError>.Ok(del.Method.GetParameters().Select(p => p.ParameterType).ToArray());
                    }
                } else {
                    return Result<Type[], MemberAccessError>.Ok([field.FieldType]);
                }
            }
            if (type.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, variant, isFinal)) {
                if (property.PropertyType.IsSameOrSubclassOf(typeof(Delegate))) {
                    if (PreventCodeExecution && !forceAllowCodeExecution) {
                        return Result<Type[], MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(type, memberArgs.Length - 1, memberArgs));
                    }

                    if ((property.IsStatic() ? property.GetValue(null) : property.GetValue(instance)) is Delegate del) {
                        return Result<Type[], MemberAccessError>.Ok(del.Method.GetParameters().Select(p => p.ParameterType).ToArray());
                    }
                } else {
                    return Result<Type[], MemberAccessError>.Ok([property.PropertyType]);
                }
            }


            var methodResult = ResolveMethod(type, member, bindingFlags, argumentCount);
            if (!methodResult.Success) {
                return Result<Type[], MemberAccessError>.Fail(new MemberAccessError.Custom(type, memberIdx, methodResult.Error));
            }

            if (methodResult.Value is { } method && IsMethodUsable(method, variant, isFinal)) {
                return Result<Type[], MemberAccessError>.Ok(method.GetParameters().Select(p => p.ParameterType) .ToArray());
            }

            return Result<Type[], MemberAccessError>.Fail(new MemberAccessError.MemberNotFound(type, memberIdx, memberArgs, bindingFlags));
        } catch (Exception ex) {
            return Result<Type[], MemberAccessError>.Fail(new MemberAccessError.UnknownException(type, memberIdx, memberArgs, ex));
        }
    }

    internal static Result<object?[], QueryError> ResolveValue(string[] valueArgs, Type[] targetTypes) {
        object?[] values = new object?[targetTypes.Length];
        int valueIdx = 0;

        for (int argIdx = 0; argIdx < valueArgs.Length; argIdx++) {
            if (valueIdx >= values.Length) {
                return Result<object?[], QueryError>.Fail(new QueryError.TooManyArguments());
            }
            string arg = valueArgs[argIdx];

            var targetType = targetTypes[valueIdx];
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try {
                foreach (var handler in Handlers.Where(handler => handler.CanResolveValue(targetType))) {
                    var result = handler.ResolveValue(targetType, ref argIdx, valueArgs, out object? value);
                    if (result.Success && result.Value) {
                        values[valueIdx++] = value;
                        goto NextArg;
                    }
                    if (result.Failure) {
                        return Result<object?[], QueryError>.Fail(result.Error);
                    }
                }

                // Attempt to evaluate as a target-query
                var queryResult = GetMemberValues(arg);
                if (queryResult.Success) {
                    if (queryResult.Value.Count == 0 || queryResult.Value.Count > values.Length - valueIdx) {
                        return Result<object?[], QueryError>.Fail(new QueryError.InstanceCountMismatch(arg, values.Length - valueIdx, queryResult.Value.Count));
                    }

                    foreach ((object? _, object? value) in queryResult.Value) {
                        var coerceResult = value.CoerceTo(targetTypes[valueIdx]);
                        if (coerceResult.Failure) {
                            return Result<object?[], QueryError>.Fail(new QueryError.Custom(coerceResult.Error));
                        }

                        values[valueIdx++] = coerceResult.Value;
                    }
                    continue;
                }

                if (targetType.IsAssignableFrom(typeof(string))) {
                    values[valueIdx++] = arg;
                    continue;
                }
                if (targetType.IsPrimitive || targetType == typeof(decimal)) {
                    values[valueIdx++] = Convert.ChangeType(arg, targetType);
                    continue;
                }
                if (targetType.IsEnum) {
                    if (!Enum.TryParse(targetType, arg, ignoreCase: true, out object? value)) {
                        return Result<object?[], QueryError>.Fail(new QueryError.InvalidEnumState(targetType, arg));
                    }

                    values[valueIdx++] = value;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(arg) || arg == "null") {
                    values[valueIdx++] = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                    continue;
                }

                return Result<object?[], QueryError>.Fail(new QueryError.ConversionError(arg, targetType));

                NextArg:;
            } catch (Exception ex) {
                ex.LogException($"Failed to resolve value for type '{targetType}'");
                return Result<object?[], QueryError>.Fail(new QueryError.UnknownException(ex));
            }
        }

        return Result<object?[], QueryError>.Ok(values);
    }
}
