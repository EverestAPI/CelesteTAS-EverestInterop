using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.EverestInterop;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections;
using System.Runtime.CompilerServices;

namespace TAS.InfoHUD;

/// By default, target queries can only access static members.
/// By providing an `IInstanceResolver`, types like `Player` can resolve to all instances of the player entity in the level.
internal interface IInstanceResolver {
    public bool CanResolve(Type type);

    List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId);
}

/// Contains all the logic for getting/setting/invoking data with the target-query syntax
/// See wiki for documentation: https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Info-HUD#target-queries
public static class TargetQuery {
    internal enum Variant {
        Get, Set, Invoke
    }

    /// Handler to provide support for Celeste-specific special cases
    internal abstract class Handler {
        public abstract bool CanResolveInstances(Type type);
        public abstract bool CanResolveMembers(Type type);

        public virtual object[] ResolveInstances(Type type) => [];
        public virtual (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) => null;

        public virtual object[] ResolveInstancesWithUserData(Type type, object? userData) => ResolveInstances(type);
        public virtual (List<Type> Types, string[] MemberArgs, object? UserData)? ResolveBaseTypesWithUserData(string[] queryArgs) {
            if (ResolveBaseTypes(queryArgs) is { } result) {
                return (result.Types, result.MemberArgs, UserData: null);
            }

            return null;
        }

        public virtual Result<bool, QueryError> ResolveMemberValues(ref object?[] values, ref int memberIdx, string[] memberArgs) {
            return Result<bool, QueryError>.Ok(false);
        }
        public virtual Result<bool, MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
            value = null;
            return Result<bool, MemberAccessError>.Ok(false);
        }
        public virtual Result<bool, MemberAccessError> ResolveMemberType(object? instance, out Type memberType, Type type, int memberIdx, string[] memberArgs) {
            memberType = null!;
            return Result<bool, MemberAccessError>.Ok(false);
        }

        public virtual Result<bool, MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
            return Result<bool, MemberAccessError>.Ok(false);
        }

        public virtual Result<bool, QueryError> ResolveValue(Type targetType, ref int argIdx, string[] valueArgs, out object? value) {
            value = null;
            return Result<bool, QueryError>.Ok(false);
        }

        [MustDisposeResource]
        public virtual IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(Variant variant) {
            yield break;
        }
        [MustDisposeResource]
        public virtual IEnumerator<CommandAutoCompleteEntry> EnumerateMemberEntries(Type type, Variant variant) {
            foreach (var field in EnumerateUsableFields(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(f => f.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = field.Name,
                    Extra = field.FieldType.CSharpName(),
                    IsDone = IsFinalTarget(field.FieldType)
                };
            }
            foreach (var property in EnumerateUsableProperties(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(p => p.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = property.Name,
                    Extra = property.PropertyType.CSharpName(),
                    IsDone = IsFinalTarget(property.PropertyType)
                };
            }
            foreach (var method in EnumerateUsableMethods(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(m => m.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = method.Name,
                    Extra = $"({string.Join(", ", method.GetParameters().Select(p => p.HasDefaultValue ? $"[{p.ParameterType.CSharpName()}]" : p.ParameterType.CSharpName()))})",
                    IsDone = true,
                };
            }
        }
    }

    private static bool IsSettableType(Type type) => !type.IsSameOrSubclassOf(typeof(Delegate));
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

    internal static bool IsFieldUsable(FieldInfo field, Variant variant, bool isFinal) {
        return variant switch {
            Variant.Get => true,
            Variant.Set => isFinal
                ? (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0 && IsSettableType(field.FieldType)
                : (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0 || !field.FieldType.IsValueType,
            Variant.Invoke => !isFinal,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsPropertyUsable(PropertyInfo property, Variant variant, bool isFinal) {
        return variant switch {
            Variant.Get => property.CanRead,
            Variant.Set => isFinal
                ? property.CanWrite && IsSettableType(property.PropertyType)
                : property.CanRead && (property.CanWrite || !property.PropertyType.IsValueType),
            Variant.Invoke => !isFinal,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }
    internal static bool IsMethodUsable(MethodInfo method, Variant variant) {
        return variant switch {
            Variant.Get => false,
            Variant.Set => false,
            Variant.Invoke => IsInvokableMethod(method),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };
    }

    internal static IEnumerable<FieldInfo> EnumerateUsableFields(Type type, Variant variant, BindingFlags bindingFlags) {
        return type
            .GetAllFieldInfos(bindingFlags)
            .Where(f =>
                // Filter-out compiler generated fields
                f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>') &&
                // Require to be usable
                IsFieldUsable(f, variant, IsFinalTarget(f.FieldType)));
    }
    internal static IEnumerable<PropertyInfo> EnumerateUsableProperties(Type type, Variant variant, BindingFlags bindingFlags) {
        return type
            .GetAllPropertyInfos(bindingFlags)
            .Where(p =>
                // Filter-out compiler generated properties
                p.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !p.Name.Contains('<') && !p.Name.Contains('>') &&
                // Require to be usable
                IsPropertyUsable(p, variant, IsFinalTarget(p.PropertyType)));
    }
    internal static IEnumerable<MethodInfo> EnumerateUsableMethods(Type type, Variant variant, BindingFlags bindingFlags) {
        return type
            .GetAllMethodInfos(bindingFlags)
            .Where(m =>
                // Filter-out compiler generated fields
                m.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !m.Name.Contains('<') && !m.Name.Contains('>') &&
                // Require to be usable
                IsMethodUsable(m, variant));
    }

    /// Guesses whether the target type is reasonably the final part of a target-query
    /// This is only relevant for auto-complete and therefore not important to be 100% correct each time
    private static bool IsFinalTarget(Type type) => type == typeof(string) || type == typeof(Vector2) || type == typeof(Random) || type == typeof(ButtonBinding) || type.IsEnum || type.IsPrimitive;

    /// Prevents invocations of methods / execution of Lua code in the Custom Info
    public static bool PreventCodeExecution => EnforceLegalCommand.EnabledWhenRunning;

    internal static readonly Dictionary<string, List<Type>> AllTypes = new();
    internal static readonly Dictionary<string, (List<Type> Types, string[] MemberArgs, object? UserData)> BaseTypeCache = [];

    /// Searches for the target type, optional target assembly, optional component type, optional component assembly, and optional EntityID
    /// e.g. `Type@Assembly:Component@Assembly[RoomName:EntityId]`
    private static readonly Regex BaseTypeRegex = new(@"^([\w.]+)(@(?:[^.:\[\]\n]*))?(?::(\w+))?(@(?:[^.:\[\]\n]*))?(?:\[(.+):(\d+)\])?$", RegexOptions.Compiled);

    private static readonly IInstanceResolver[] TypeInstanceResolvers = [
        // new GlobalInstanceResolver<Settings>(() => Settings.Instance),
        // new GlobalInstanceResolver<SaveData>(() => SaveData.Instance),
        // new GlobalInstanceResolver<Assists>(() => SaveData.Instance.Assists),
        // new EverestSettingsInstanceResolver(),
        new EntityInstanceResolver(),
        new ComponentInstanceResolver(),
        new SceneInstanceResolver(),
        new SessionInstanceResolver(),
    ];
    private static readonly Handler[] Handlers = [
        new SettingsQueryHandler(),
        new SaveDataQueryHandler(),
        new AssistsQueryHandler(),
        new ExtendedVariantsQueryHandler(),
        new EverestModuleSettingsQueryHandler(),
        new EntityQueryHandler(),
        new ComponentQueryHandler(),
        new SpecialValueQueryHandler(),
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

                // Strip namespace
                int namespaceLen = type.Namespace != null
                    ? type.Namespace.Length + 1
                    : 0;
                string shortName = fullName[namespaceLen..];

                AllTypes.AddToKey(fullName, type);
                AllTypes.AddToKey($"{fullName}@{assemblyName}", type);
                AllTypes.AddToKey($"{fullName}@{modName}", type);

                AllTypes.AddToKey(shortName, type);
                AllTypes.AddToKey($"{shortName}@{assemblyName}", type);
                AllTypes.AddToKey($"{shortName}@{modName}", type);
            }
        }
    }

    [MonocleCommand("get", "'get Type.fieldOrProperty' -> value | Example: 'get Player.Position', 'get Level.Wind' (CelesteTAS)"), UsedImplicitly]
    private static void GetCmd(string? query) {
        if (query == null) {
            "No target-query specified".ConsoleLog(LogLevel.Error);
            return;
        }

        var result = GetMemberValues(query);
        if (result.Failure) {
            result.Error.ConsoleLog(LogLevel.Error);
            return;
        }

        if (result.Value.Count == 0) {
            "No instances found".ConsoleLog(LogLevel.Error);
        } else if (result.Value.Count == 1) {
            result.Value[0].Value.ConsoleLog();
        } else {
            foreach ((object? baseInstance, object? value) in result.Value) {
                if (baseInstance is Entity entity &&
                    entity.GetEntityData()?.ToEntityId().ToString() is { } id)
                {
                    $"[{id}] {value}".ConsoleLog();
                } else {
                    value.ConsoleLog();
                }
            }
        }
    }

    /// Parses a target-query and returns the results for that
    /// A single BaseInstance == null entry is returned for static contexts
    internal static Result<List<(object BaseInstance, object? Value)>, QueryError> GetMemberValues(string query, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs, out object? userData);
        if (baseTypes.IsEmpty()) {
            return Result<List<(object BaseInstance, object? Value)>, QueryError>.Fail(new QueryError.NoBaseTypes(query));
        }

        List<(object BaseInstance, object? Value)> allResults = [];
        MemberAccessError? error = null;

        foreach (var baseType in baseTypes) {
            object[] instances = ResolveTypeInstances(baseType, userData);
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

    #region Auto-Complete

    /// Sorts types by namespace into Celeste -> Monocle -> other (alphabetically)
    /// Inside the namespace it's sorted alphabetically
    internal class NamespaceComparer : IComparer<(string Name, Type Type)> {
        public int Compare((string Name, Type Type) x, (string Name, Type Type) y) {
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

    internal static IEnumerator<CommandAutoCompleteEntry> ResolveAutoCompleteEntries(string[] queryArgs, Variant variant) {
        {
            using var enumerator = ResolveBaseTypeAutoCompleteEntries(queryArgs, variant);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }
        }

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs, out var userData);
        foreach (var type in baseTypes) {
            foreach (var handler in Handlers.Where(handler => handler.CanResolveInstances(type))) {
                using var enumerator = handler.EnumerateMemberEntries(type, variant);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current;
                }
                goto NextType;
            }

            // Generic handler
            foreach (var field in EnumerateUsableFields(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(f => f.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = field.Name,
                    Extra = field.FieldType.CSharpName(),
                    IsDone = IsFinalTarget(field.FieldType)
                };
            }
            foreach (var property in EnumerateUsableProperties(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(p => p.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = property.Name,
                    Extra = property.PropertyType.CSharpName(),
                    IsDone = IsFinalTarget(property.PropertyType)
                };
            }
            foreach (var method in EnumerateUsableMethods(type, variant, ReflectionExtensions.StaticInstanceAnyVisibility).OrderBy(m => m.Name)) {
                yield return new CommandAutoCompleteEntry {
                    Name = method.Name,
                    Extra = $"({string.Join(", ", method.GetParameters().Select(p => p.HasDefaultValue ? $"[{p.ParameterType.CSharpName()}]" : p.ParameterType.CSharpName()))})",
                    IsDone = true,
                };
            }

            NextType:;
        }
    }
    private static IEnumerator<CommandAutoCompleteEntry> ResolveBaseTypeAutoCompleteEntries(string[] queryArgs, Variant variant) {
        foreach (var handler in Handlers) {
            using var enumerator = handler.ProvideGlobalEntries(variant);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }
        }

        string queryPrefix = queryArgs.Length != 0 ? $"{string.Join('.', queryArgs)}." : "";

        var types = ModUtils.GetTypes()
            .Where(type =>
                // Filter-out types which probably aren't useful
                (type.IsClass || type.IsStructType()) && type.FullName != null && type.Namespace != null && !ignoredNamespaces.Any(ns => type.Namespace.StartsWith(ns)) &&
                // Filter-out compiler generated types
                type.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() & !type.FullName.Contains('<') && !type.FullName.Contains('>') &&
                // Require query-arguments to match namespace
                type.FullName.StartsWith(queryPrefix))
            .Where(type => {
                var bindingFlags = Handlers.Any(handler => handler.CanResolveInstances(type))
                    ? ReflectionExtensions.StaticInstanceAnyVisibility
                    : ReflectionExtensions.StaticAnyVisibility;

                // Require some viable members
                return EnumerateUsableFields(type, variant, bindingFlags).Any() ||
                       EnumerateUsableProperties(type, variant, bindingFlags).Any() ||
                       EnumerateUsableMethods(type, variant, bindingFlags).Any();
            })
            .OrderBy(t => (t.CSharpName(), t), new NamespaceComparer())
            .ToArray();

        string[][] namespaces = types
            .Select(type => type.Namespace!)
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

    #endregion
    #region Resolving

    /// Parses the first part of a query into types and an optional EntityID
    public static List<Type> ResolveBaseTypes(string[] queryArgs, out string[] memberArgs, out object? userData) {
        if (queryArgs.Length == 0) {
            memberArgs = queryArgs;
            userData = null;
            return [];
        }

        string fullQueryArgs = string.Join('.', queryArgs);
        if (BaseTypeCache.TryGetValue(fullQueryArgs, out var cache)) {
            memberArgs = cache.MemberArgs;
            userData = cache.UserData;
            return cache.Types;
        }

        foreach (var handler in Handlers) {
            if (handler.ResolveBaseTypesWithUserData(queryArgs) is { } result) {
                BaseTypeCache[fullQueryArgs] = result;

                memberArgs = result.MemberArgs;
                userData = result.UserData;
                return result.Types;
            }
        }

        userData = null;
        return ParseGenericBaseTypes(queryArgs, out memberArgs);

        // Vanilla settings don't need a prefix
        // if (typeof(Settings).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
        //     memberArgs = queryArgs;
        //     return [typeof(Settings)];
        // }
        // if (typeof(SaveData).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null && queryArgs[0] != nameof(SaveData.Assists)) {
        //     memberArgs = queryArgs;
        //     return [typeof(SaveData)];
        // }
        // if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
        //     memberArgs = queryArgs;
        //     return [typeof(Assists)];
        // }

        // Check for mod settings
        // if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
        //     memberArgs = queryArgs[1..];
        //     return [module.SettingsType];
        // }

        // Decrease query arguments until a match is found
        for (int i = queryArgs.Length; i > 0; i--) {
            string typeName = string.Join('.', queryArgs[..i]);

            if (BaseTypeCache.TryGetValue(typeName, out cache)) {
                memberArgs = cache.MemberArgs;
                userData = cache.UserData;
                return cache.Types;
            }

            if (AllTypes.TryGetValue(typeName, out var types)) {
                memberArgs = queryArgs[i..];
                userData = null;

                BaseTypeCache[fullQueryArgs] = BaseTypeCache[typeName] = (types, memberArgs, UserData: null);
                return types;
            }
        }

        // // Greedily increase amount of tested arguments
        // string currentType = string.Empty;
        // int currentIndex = 0;
        //
        // for (int i = 1; i <= queryArgs.Length; i++) {
        //     string typeName = string.Join('.', queryArgs[..i]);
        //
        //     if (baseTypeCache.ContainsKey(typeName)) {
        //         currentType = typeName;
        //         currentIndex = i;
        //         continue;
        //     }
        //
        //     var match = BaseTypeRegex.Match(typeName);
        //     if (!match.Success) {
        //         break; // No further matches
        //     }
        //
        //     // Remove the entity ID from the type check
        //     string checkTypeName = $"{match.Groups[1].Value}{match.Groups[2].Value}";
        //     string componentTypeName = $"{match.Groups[3].Value}{match.Groups[4].Value}";
        //
        //     if (int.TryParse(match.Groups[6].Value, out int id)) {
        //         entityId = new EntityID(match.Groups[5].Value, id);
        //     }
        //
        //     if (!allTypes.TryGetValue(checkTypeName, out var types)) {
        //         break; // No further existing types
        //     }
        //
        //     if (!allTypes.TryGetValue(componentTypeName, out componentTypes!)) {
        //         componentTypes = [];
        //     }
        //
        //     baseTypeCache[typeName] = (Types: types, ComponentTypes: componentTypes, EntityID: entityId);
        //     currentType = typeName;
        //     currentIndex = i;
        // }
        //
        // if (baseTypeCache.TryGetValue(currentType, out var pair)) {
        //     componentTypes = pair.ComponentTypes;
        //     entityId = pair.EntityID;
        //     memberArgs = queryArgs[currentIndex..];
        //     return pair.Types;
        // }

        // No matching type found
        memberArgs = queryArgs;
        userData = null;
        return [];
    }

    /// Parses query-arguments into a list of types, while only searching for generic .NET types
    /// Does not reference any defined special-case handlers
    internal static List<Type> ParseGenericBaseTypes(string[] queryArgs, out string[] memberArgs) {
        string fullQueryArgs = string.Join('.', queryArgs);

        if (BaseTypeCache.TryGetValue(fullQueryArgs, out var cache) && cache.UserData == null) {
            memberArgs = cache.MemberArgs;
            return cache.Types;
        }

        // Decrease query arguments until a match is found
        for (int i = queryArgs.Length; i > 0; i--) {
            bool isFirst = i == queryArgs.Length;
            string typeName = isFirst
                ? fullQueryArgs
                : string.Join('.', queryArgs, startIndex: 0, count: i);

            if (!isFirst && BaseTypeCache.TryGetValue(typeName, out cache) && cache.UserData == null && cache.MemberArgs.Length == 0) {
                memberArgs = queryArgs[i..];
                return cache.Types;
            }

            if (AllTypes.TryGetValue(typeName, out var types)) {
                memberArgs = queryArgs[i..];

                BaseTypeCache[typeName] = (types, [], UserData: null);
                BaseTypeCache[fullQueryArgs] = (types, memberArgs, UserData: null);
                return types;
            }
        }

        // No matching type found
        memberArgs = queryArgs;
        return [];
    }

    /// Resolves a type into all applicable instances of it
    /// Returns null for types which are always in a static context
    public static object[] ResolveTypeInstances(Type type, object? userData) {
        foreach (var handler in Handlers) {
            if (handler.CanResolveInstances(type)) {
                return handler.ResolveInstancesWithUserData(type, userData);
            }
        }
        // foreach (var resolver in TypeInstanceResolvers) {
        //     if (resolver.CanResolve(type)) {
        //         return resolver.Resolve(type, componentTypes, entityId);
        //     }
        // }

        // No instances available
        return [type];
    }

    #endregion
    #region Evaluation

    /// Value in the result array which is not valid, but was kept in the array to avoid allocations
    internal static readonly object InvalidValue = new();

    internal record QueryError {
        public record NoBaseTypes(string query) : QueryError;
        public record TooManyArguments : QueryError;
        public record UnknownException(Exception ex) : QueryError;
        public record Custom(string ErrorMessage) : QueryError;
        public record InvalidEnumState(Type enumType, string member) : QueryError; // return Result<object?[], string>.Fail($"'{arg}' is not a valid enum state for '{targetType.FullName}'");
    }

    internal record MemberAccessError : QueryError {
        public record NoMembers : MemberAccessError {
            public NoMembers() : base(typeof(object), -1) { }
        }
        public record CodeExecutionNotAllowed : MemberAccessError {
            public CodeExecutionNotAllowed(Type type, int member, string[] memberArgs) : base(type, member) { }
            //  VoidResult<string>.Fail($"Cannot safely get property '{member}' during EnforceLegal");
        }
        public record UnknownException : MemberAccessError {
            public UnknownException(Type? type, int member, string[] memberArgs, Exception ex) : base(type ?? typeof(object), member) { }
            // VoidResult<string>.Fail($"Unknown exception: {ex}");
        }
        public record Custom : MemberAccessError {
            public Custom(Type type, int member, string[] memberArgs, string errorMessage) : base(type, member) { }
        }
        public record ReadOnlyCollection : MemberAccessError {
            public ReadOnlyCollection(Type type, int member, string[] memberArgs) : base(type, member) { }
            // VoidResult<string>.Fail($"Cannot find member '{member}' on type {currentType}");
        }
        public record MemberNotFound : MemberAccessError {
            public MemberNotFound(Type type, int member, string[] memberArgs, BindingFlags bindingFlags) : base(type, member) { }
            // VoidResult<string>.Fail($"Cannot find member '{member}' on type {currentType}");
        }

        public Type Type;
        public int Member;

        public MemberAccessError(Type type, int member) {
            Type = type;
            Member = member;
        }

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
            if (accum.Member < error.Member) {
                return accum;
            } else if (error.Member < accum.Member) {
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

    /// Evaluates the member arguments on the base-instances and gets the specified value(s)
    internal static Result<object?[], QueryError> GetMemberValue(object instance, string[] memberArgs, bool forceAllowCodeExecution = false)
        => ResolveMemberValue(instance, memberArgs, forceAllowCodeExecution, needsFlush: false);

    /// Evaluates the member arguments on the base-instances and prepares the specified value(s)
    /// The values can then be modifed with SetMemberValue
    internal static Result<object?[], QueryError> PrepareMemberValue(object instance, string[] memberArgs, bool forceAllowCodeExecution = false)
        => ResolveMemberValue(instance, memberArgs, forceAllowCodeExecution, needsFlush: true);

    private record BoxedValueHolder(object BaseInstance, int Index, Stack<object> ValueStack);

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
                            ProcessValue(ref values, valueIdx, value, currentType, memberIdx, memberArgs, needsFlush);
                            goto NextValue;
                        }
                        if (result.Failure) {
                            values[valueIdx] = result.Error;
                            goto NextValue;
                        }
                    }

                    if (currentType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, needsFlush ? Variant.Set : Variant.Get, isFinal)) {
                        if (field.IsStatic) {
                            ProcessValue(ref values, valueIdx, field.GetValue(null), currentType, memberIdx, memberArgs, needsFlush);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                        } else {
                            ProcessValue(ref values, valueIdx, field.GetValue(values[valueIdx]), currentType, memberIdx, memberArgs, needsFlush);
                        }
                        continue;
                    }

                    if (currentType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, needsFlush ? Variant.Set : Variant.Get, isFinal)) {
                        if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
                            values[valueIdx] = new MemberAccessError.CodeExecutionNotAllowed(currentType, memberIdx, memberArgs);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                            continue;
                        }

                        if (property.IsStatic()) {
                            ProcessValue(ref values, valueIdx, property.GetValue(null), currentType, memberIdx, memberArgs, needsFlush);

                            // Invalidate all other instances of this type to avoid duplicates
                            InvalidateValues(ref values, valueIdx, currentType);
                        } else {
                            ProcessValue(ref values, valueIdx, property.GetValue(values[valueIdx]), currentType, memberIdx, memberArgs, needsFlush);
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

        static void ProcessValue(ref object?[] values, int valueIdx, object? value, Type currentType, int memberIdx, string[] memberArgs, bool needsFlush) {
            if (needsFlush) {
                ProcessFlushableValue(ref values, valueIdx, value, currentType, memberIdx, memberArgs);
            } else {
                ProcessGetValue(ref values, valueIdx, value);
            }
        }
        static void ProcessGetValue(ref object?[] values, int valueIdx, object? value) {
            if (value is ICollection collection) {
                switch (collection.Count) {
                    case 0:
                        values[valueIdx] = InvalidValue;
                        break;

                    case 1:
                        collection.CopyTo(values, valueIdx);
                        break;

                    default:
                        // Can only copy entire collection, so need to invalidate previous instance
                        values[valueIdx] = InvalidValue;
                        int startIdx = values.Length;
                        Array.Resize(ref values, values.Length + collection.Count);
                        collection.CopyTo(values, startIdx);
                        break;
                }
            } else {
                values[valueIdx] = value;
            }
        }
        static void ProcessFlushableValue(ref object?[] values, int valueIdx, object? value, Type currentType, int memberIdx, string[] memberArgs) {
            switch (value) {
                case IList list:
                    switch (list.Count) {
                        case 0:
                            values[valueIdx] = InvalidValue;
                            break;

                        case 1:
                            // Value types need a writable collection
                            if (list[0] != null && list[0]!.GetType().IsValueType) {
                                if (list.IsReadOnly) {
                                    values[valueIdx] = new MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                } else {
                                    if (values[valueIdx] is BoxedValueHolder holder) {
                                        holder.ValueStack.Push(list[0]!);
                                    } else {
                                        holder = new BoxedValueHolder(list, 0, new(capacity: 1));
                                        holder.ValueStack.Push(list[0]!);
                                        values[valueIdx] = holder;
                                    }
                                }
                            } else {
                                values[valueIdx] = list[0];
                            }
                            break;

                        default:
                            // Can only copy entire collection, so need to invalidate previous instance
                            int startIdx = values.Length;
                            Array.Resize(ref values, values.Length + list.Count - 1);

                            // Value types need a writable collection
                            if (list[0] != null && list[0]!.GetType().IsValueType) {
                                if (list.IsReadOnly) {
                                    values[valueIdx] = new MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                } else {
                                    if (values[valueIdx] is BoxedValueHolder holder) {
                                        holder.ValueStack.Push(list[0]!);
                                    } else {
                                        holder = new BoxedValueHolder(list, 0, new(capacity: 1));
                                        holder.ValueStack.Push(list[0]!);
                                        values[valueIdx] = holder;
                                    }
                                }
                            } else {
                                values[valueIdx] = list[0];
                            }

                            for (int i = 1; i < list.Count; i++) {
                                if (list[i] != null && list[i]!.GetType().IsValueType) {
                                    if (list.IsReadOnly) {
                                        values[startIdx + i - 1] = new MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                    } else {
                                        if (values[startIdx + i - 1] is BoxedValueHolder holder) {
                                            holder.ValueStack.Push(list[i]!);
                                        } else {
                                            holder = new BoxedValueHolder(list, i, new(capacity: 1));
                                            holder.ValueStack.Push(list[i]!);
                                            values[startIdx + i - 1] = holder;
                                        }
                                    }
                                } else {
                                    values[startIdx + i - 1] = list[i];
                                }
                            }
                            break;
                    }
                    break;

                case ICollection collection:
                    switch (collection.Count) {
                        case 0:
                            values[valueIdx] = InvalidValue;
                            break;

                        case 1:
                            collection.CopyTo(values, valueIdx);

                            // Value types need a writable collection
                            if (values[valueIdx]?.GetType().IsValueType ?? false) {
                                values[valueIdx] = new MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            }
                            break;

                        default:
                            // Can only copy entire collection, so need to invalidate previous instance
                            values[valueIdx] = InvalidValue;
                            int startIdx = values.Length;
                            Array.Resize(ref values, values.Length + collection.Count);
                            collection.CopyTo(values, startIdx);

                            // Value types need a writable collection
                            for (int i = startIdx; i < values.Length; i++) {
                                if (values[i]?.GetType().IsValueType ?? false) {
                                    values[i] = new MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                }
                            }
                            break;
                    }
                    break;

                default:
                    if (value != null && value.GetType().IsValueType) {
                        if (values[valueIdx] is BoxedValueHolder holder) {
                            holder.ValueStack.Push(value);
                        } else {
                            holder = new BoxedValueHolder(values[valueIdx]!, -1, new(capacity: 1));
                            holder.ValueStack.Push(value);
                            values[valueIdx] = holder;
                        }
                    } else {
                        values[valueIdx] = value;
                    }
                    break;
            }
        }
    }

    internal static Result<Type, QueryError> ResolveLastMemberType(object targetObject, string[] memberArgs) {
        object target = (targetObject as BoxedValueHolder)?.ValueStack.Peek() ?? targetObject;
        var targetType = target as Type ?? target.GetType();

        string member = memberArgs[^1];

        var bindingFlags = memberArgs.Length == 1
            ? target is Type
                ? ReflectionExtensions.StaticAnyVisibility
                : ReflectionExtensions.StaticInstanceAnyVisibility
            : ReflectionExtensions.InstanceAnyVisibility;

        try {
            foreach (var handler in Handlers) {
                var result = handler.ResolveMemberType(target, out Type memberType, targetType, memberArgs.Length - 1, memberArgs);
                if (result.Success && result.Value) {
                    return Result<Type, QueryError>.Ok(memberType);
                }
                if (result.Failure) {
                    return Result<Type, QueryError>.Fail(result.Error);
                }
            }

            if (targetType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, Variant.Set, isFinal: true)) {
                return Result<Type, QueryError>.Ok(field.FieldType);
            }
            if (targetType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, Variant.Set, isFinal: true)) {
                return Result<Type, QueryError>.Ok(property.PropertyType);
            }

            return Result<Type, QueryError>.Fail(new MemberAccessError.MemberNotFound(targetType, memberArgs.Length - 1, memberArgs, bindingFlags));
        } catch (Exception ex) {
            return Result<Type, QueryError>.Fail(new MemberAccessError.UnknownException(targetType, memberArgs.Length - 1, memberArgs, ex));
        }
    }

    internal static VoidResult<MemberAccessError> SetMemberValue(object targetObject, object? value, string[] memberArgs, bool forceAllowCodeExecution = false) {
        object target = (targetObject as BoxedValueHolder)?.ValueStack.Peek() ?? targetObject;
        var targetType = target as Type ?? target.GetType();

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
                    goto PropagateValueTypeStack;
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
            } else if (targetType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property && IsPropertyUsable(property, Variant.Set, isFinal: true)) {
                if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
                    return VoidResult<MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(targetType, memberArgs.Length - 1, memberArgs));
                }

                if (property.IsStatic()) {
                    property.SetValue(null, value);
                } else {
                    property.SetValue(target, value);
                }
            }
        } catch (Exception ex) {
            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.UnknownException(targetType, memberArgs.Length - 1, memberArgs, ex));
        }

        PropagateValueTypeStack:

        // Propagate value-type stack
        if (targetObject is BoxedValueHolder holder) {
            object currentValue = holder.ValueStack.Pop();

            int memberIdx = memberArgs.Length - 2;
            while (holder.ValueStack.TryPop(out object? currentTarget)) {
                var currentTargetType = currentTarget as Type ?? currentTarget.GetType();
                string member = memberArgs[memberIdx];

                try {
                    foreach (var handler in Handlers) {
                        var result = handler.SetMember(target, value, currentTargetType, memberArgs.Length - 1, memberArgs, forceAllowCodeExecution);
                        if (result.Success && result.Value) {
                            goto NextValue;
                        }
                        if (result.Failure) {
                            return VoidResult<MemberAccessError>.Fail(result.Error);
                        }
                    }

                    if (currentTargetType.GetFieldInfo(member, ReflectionExtensions.InstanceAnyVisibility, logFailure: false) is { } field) {
                        if (field.IsStatic) {
                            field.SetValue(null, currentValue);
                        } else {
                            field.SetValue(currentTarget, currentValue);
                        }
                    } else if (currentTargetType.GetPropertyInfo(member, ReflectionExtensions.InstanceAnyVisibility, logFailure: false) is { } property) {
                        if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
                            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(currentTargetType, memberIdx, memberArgs));
                        }

                        if (property.IsStatic()) {
                            property.SetValue(null, currentValue);
                        } else {
                            property.SetValue(currentTarget, currentValue);
                        }
                    }

                    NextValue:;
                } catch (Exception ex) {
                    return VoidResult<MemberAccessError>.Fail(new MemberAccessError.UnknownException(currentTargetType, memberIdx, memberArgs, ex));
                }

                currentValue = currentTarget;
                memberIdx--;
            }

            if (holder.Index < 0) {
                // Regular base instance
                var baseTargetType = holder.BaseInstance as Type ?? holder.BaseInstance.GetType();
                string member = memberArgs[memberIdx];

                var bindingFlags = memberIdx == 0
                    ? holder.BaseInstance is Type
                        ? ReflectionExtensions.StaticAnyVisibility
                        : ReflectionExtensions.StaticInstanceAnyVisibility
                    : ReflectionExtensions.InstanceAnyVisibility;

                try {
                    foreach (var handler in Handlers) {
                        var result = handler.SetMember(holder.BaseInstance, currentValue, baseTargetType, memberIdx, memberArgs, forceAllowCodeExecution);
                        if (result.Success && result.Value) {
                            return VoidResult<MemberAccessError>.Ok;
                        }
                        if (result.Failure) {
                            return VoidResult<MemberAccessError>.Fail(result.Error);
                        }
                    }

                    if (baseTargetType.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field) {
                        if (field.IsStatic) {
                            field.SetValue(null, currentValue);
                        } else {
                            field.SetValue(holder.BaseInstance, currentValue);
                        }
                    } else if (baseTargetType.GetPropertyInfo(member, bindingFlags, logFailure: false) is { } property) {
                        if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
                            return VoidResult<MemberAccessError>.Fail(new MemberAccessError.CodeExecutionNotAllowed(baseTargetType, memberIdx, memberArgs));
                        }

                        if (property.IsStatic()) {
                            property.SetValue(null, currentValue);
                        } else {
                            property.SetValue(holder.BaseInstance, currentValue);
                        }
                    }
                } catch (Exception ex) {
                    return VoidResult<MemberAccessError>.Fail(new MemberAccessError.UnknownException(baseTargetType, memberIdx, memberArgs, ex));
                }
            } else {
                // List base instance
                var baseList = (IList) holder.BaseInstance;
                baseList[holder.Index] = currentValue;
            }
        }

        return VoidResult<MemberAccessError>.Ok;
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
                // if (arg.Contains('.') && !float.TryParse(arg, out _)) {
                //     // The value is a target-query, which needs to be resolved
                //     var result = GetMemberValues(arg);
                //     if (result.Failure) {
                //         return Result<object?[], string>.Fail(result.Error.ToString());
                //     }
                //     if (result.Value.Count != 1) {
                //         return Result<object?[], string>.Fail($"Target-query '{arg}' for type '{targetType}' needs to resolve to exactly 1 value! Got {result.Value.Count}");
                //     }
                //     if (result.Value[0].Value != null && !result.Value[0].Value!.GetType().IsSameOrSubclassOf(targetType)) {
                //         return Result<object?[], string>.Fail($"Expected type '{targetType}' for target-query '{arg}'! Got {result.Value[0].GetType()}");
                //     }
                //
                //     values[valueIdx++] = result.Value[0].Value;
                //     continue;
                // }

                foreach (var handler in Handlers) {
                    var result = handler.ResolveValue(targetType, ref argIdx, valueArgs, out object? value);
                    if (result.Success && result.Value) {
                        values[valueIdx++] = value;
                        goto NextArg;
                    }
                    if (result.Failure) {
                        return Result<object?[], QueryError>.Fail(result.Error);
                    }
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

                values[valueIdx++] = Convert.ChangeType(arg, targetType);

                NextArg:;
            } catch (Exception ex) {
                ex.LogException($"Failed to resolve value for type '{targetType}'");
                return Result<object?[], QueryError>.Fail(new QueryError.UnknownException(ex));
            }
        }

        return Result<object?[], QueryError>.Ok(values);
    }

    #endregion

    /// Recursively gets the value of the specified members
    public static Result<object?, string> GetMemberValue(Type baseType, object? baseObject, string[] memberArgs, bool forceAllowCodeExecution = false) {
        var currentType = baseType;
        var currentObject = baseObject;
        foreach (string member in memberArgs) {
            try {
                if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return Result<object?, string>.Fail($"Cannot access instance field '{member}' in a static context");
                            }

                            // Propagate null
                            return Result<object?, string>.Ok(null);
                        }

                        currentObject = field.GetValue(currentObject);
                    }
                    currentType = field.FieldType;

                    continue;
                }
                if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.GetMethod != null) {
                    if (PreventCodeExecution && !forceAllowCodeExecution) {
                        return Result<object?, string>.Fail($"Cannot safely get property '{member}' during EnforceLegal");
                    }

                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return Result<object?, string>.Fail($"Cannot access instance property '{member}' in a static context");
                            }

                            // Propagate null
                            return Result<object?, string>.Ok(null);
                        }

                        currentObject = property.GetValue(currentObject);
                    }
                    currentType = property.PropertyType;

                    continue;
                }
            } catch (Exception ex) {
                ex.LogException($"Failed to resolve member '{member}' on type '{currentType}'");
                return Result<object?, string>.Fail($"Unknown exception: {ex}");
            }

            return Result<object?, string>.Fail($"Cannot find field / property '{member}' on type {currentType}");
        }

        return Result<object?, string>.Ok(currentObject);
    }

    /// Recursively resolves the type of the specified members
    public static Result<Type, string> ResolveMemberType(Type baseType, string[] memberArgs) {
        var typeStack = new Stack<Type>();

        var currentType = baseType;
        foreach (string member in memberArgs) {
            typeStack.Push(currentType);

            if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                currentType = field.FieldType;
                continue;
            }
            if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.GetMethod != null) {
                currentType = property.PropertyType;
                continue;
            }

            // Unable to recurse further
            return Result<Type, string>.Fail($"Cannot find field / property '{member}' on type {currentType}");
        }

        // Special case for Actor / Platform positions, since they use subpixels
        if (memberArgs[^1] is nameof(Entity.X) or nameof(Entity.Y)) {
            var entityType = typeof(Entity);
            if (typeStack.Count >= 2 && memberArgs[^2] is nameof(Entity.Position)) {
                // "Entity.Position.X"
                _ = typeStack.Pop();
                entityType = typeStack.Pop();
            } else if (typeStack.Count >= 1) {
                // "Entity.X"
                entityType = typeStack.Pop();
            }

            if (entityType.IsSameOrSubclassOf(typeof(Actor)) || entityType.IsSameOrSubclassOf(typeof(Platform))) {
                return Result<Type, string>.Ok(typeof(SubpixelComponent));
            }
        } else if (memberArgs[^1] is nameof(Entity.Position)) {
            // "Entity.Position"
            var entityType = typeStack.Pop();

            if (entityType.IsSameOrSubclassOf(typeof(Actor)) || entityType.IsSameOrSubclassOf(typeof(Platform))) {
                return Result<Type, string>.Ok(typeof(SubpixelPosition));
            }
        }

        return Result<Type, string>.Ok(currentType);
    }

    /// Recursively resolves a method for the specified members
    public static Result<MethodInfo, string> ResolveMemberMethod(Type baseType, string[] memberArgs) {
        var currentType = baseType;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            string member = memberArgs[i];

            if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                currentType = field.FieldType;
                continue;
            }

            if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.GetMethod != null) {
                currentType = property.PropertyType;
                continue;
            }

            // Unable to recurse further
            return Result<MethodInfo, string>.Fail($"Failed to find field / property '{member}' on type '{currentType}'");
        }

        // Find method
        if (currentType.GetMethodInfo(memberArgs[^1], logFailure: false) is { } method) {
            return Result<MethodInfo, string>.Ok(method);
        }

        // Couldn't find the method
        return Result<MethodInfo, string>.Fail($"Failed to find method '{memberArgs[^1]}' on type '{currentType}'");
    }





    // /// Recursively resolves the value of the specified members for multiple instances at once
    // /// The resulting array contains the final values in the same order of the input instances
    // /// If the result value is null, it's a static context
    // /// If the result type is null, it's an invalid result value which should be ignored
    // public static Result<(object? Value, Type? Type)[], string> ResolveMemberValues(Type baseType, object[]? instances, string[] memberArgs, bool forceAllowCodeExecution = false) {
    //     var current = new (object? Instance, object[]? Values, Type? CommonType)[instances?.Length ?? 1];
    //     if (instances == null) {
    //         current[0] = (Instance: null, Values: null, CommonType: baseType);
    //     } else {
    //         for (int i = 0; i < instances.Length; i++) {
    //             //current[i] = ([instances[i]], instances[i].GetType());
    //             current[i] = (instances[i], [instances[i]], instances[i].GetType());
    //         }
    //     }
    //
    //     for (int memberIdx = 0; memberIdx < memberArgs.Length; memberIdx++) {
    //         string member = memberArgs[memberIdx];
    //
    //         for (int i = 0; i < current.Length; i++) {
    //             // var currentType = current[i].Values?
    //             //     .Select(obj => obj.GetType())
    //             //     .Aggregate((accum, type) => {
    //             //         if (accum.IsAssignableTo(type)) {
    //             //             return type;
    //             //         }
    //             //
    //             //         // This will always terminate when accum == typeof(object)
    //             //         while (!type.IsAssignableTo(accum)) {
    //             //             accum = accum.BaseType!;
    //             //         }
    //             //
    //             //         return accum;
    //             //     }) ?? baseType;
    //
    //             var bindingFlags = memberIdx == 0
    //                 ? instances == null
    //                     ? ReflectionExtensions.StaticAnyVisibility
    //                     : ReflectionExtensions.StaticInstanceAnyVisibility
    //                 : ReflectionExtensions.InstanceAnyVisibility;
    //
    //             bool isFinal = memberIdx == memberArgs.Length - 1;
    //
    //             try {
    //                 if (current[i].CommonType!.GetFieldInfo(member, bindingFlags, logFailure: false) is { } field && IsFieldUsable(field, Variant.Get, isFinal)) {
    //                     if (field.IsStatic) {
    //                         for (int j = 0; j < current[i].Values.Length; j++) {
    //                             current[i].Values[j] = field.GetValue(current[i].Values[j]);
    //                         }
    //                         current[i].Values = field.GetValue(null);
    //                         current[i].Type = current[i].Values?.GetType() ?? field.FieldType;
    //
    //                         // Clear all other instances of this type to avoid duplicates
    //                         var currentType = current[i].Type;
    //                         for (int j = i + 1; j < current.Length; j++) {
    //                             if (current[j].Type == currentType) {
    //                                 current[j].Type = null;
    //                             }
    //                         }
    //                     } else {
    //                         if (current[i].Values == null) {
    //                             if (memberIdx == 0) {
    //                                 return Result<(object? Value, Type? Type)[], string>.Fail($"Cannot access instance field '{member}' in a static context");
    //                             }
    //                         } else {
    //                             current[i].Values = field.GetValue(current[i].Values);
    //                             current[i].Type = current[i].Values?.GetType() ?? field.FieldType;
    //                         }
    //                     }
    //                     continue;
    //                 }
    //
    //                 if (current[i].Type!.GetPropertyInfo(member, logFailure: false) is { } property && IsPropertyUsable(property, Variant.Get, isFinal)) {
    //                     if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
    //                         return Result<(object? Value, Type? Type)[], string>.Fail($"Cannot safely get property '{member}' during EnforceLegal");
    //                     }
    //
    //                     if (property.IsStatic()) {
    //                         current[i].Values = property.GetValue(null);
    //                         current[i].Type = current[i].Values?.GetType() ?? property.PropertyType;
    //
    //                         // Clear all other instances of this type to avoid duplicates
    //                         var currentType = current[i].Type;
    //                         for (int j = i + 1; j < current.Length; j++) {
    //                             if (current[j].Type == currentType) {
    //                                 current[j].Type = null;
    //                             }
    //                         }
    //                     } else {
    //                         if (current[i].Values == null) {
    //                             if (memberIdx == 0) {
    //                                 return Result<(object? Value, Type? Type)[], string>.Fail($"Cannot access instance property '{member}' in a static context");
    //                             }
    //                         } else {
    //                             current[i].Values = property.GetValue(current[i].Values);
    //                             current[i].Type = current[i].Values?.GetType() ?? property.PropertyType;
    //                         }
    //                     }
    //                     continue;
    //                 }
    //
    //                 current[i].Values = Result<(object? Value, Type? Type)[], string>.Fail($"Cannot find member '{member}' on type {current[i].Type}");
    //
    //                 // Clear all instances of this type to avoid trying over and over
    //                 {
    //                     var currentType = current[i].Type;
    //                     for (int j = i; j < current.Length; j++) {
    //                         if (current[j].Type == currentType) {
    //                             current[j].Type = null;
    //                         }
    //                     }
    //                 }
    //             } catch (Exception ex) {
    //                 current[i].Values = Result<(object? Value, Type? Type)[], string>.Fail($"Unknown exception: {ex}");
    //                 current[i].Type = null;
    //             }
    //         }
    //     }
    //
    //     var result = Result<(object? Value, Type? Type)[], string>.Ok(current);
    //     for (int i = 0; i < current.Length; i++) {
    //         if (current[i].Values is Result<(object? Value, Type? Type)[], string> error) {
    //             result = error;
    //             continue;
    //         }
    //         if (current[i].Type == null) {
    //             continue;
    //         }
    //
    //         // At least 1 instance is valid
    //         result = Result<(object? Value, Type? Type)[], string>.Ok(current);
    //         break;
    //     }
    //
    //     return result;
    // }

    /// Recursively resolves the value of the specified members
    public static VoidResult<string> SetMemberValue(Type baseType, object? baseObject, object? value, string[] memberArgs) {
        var typeStack = new Stack<Type>();
        var objectStack = new Stack<object?>();

        var currentType = baseType;
        object? currentObject = baseObject;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            typeStack.Push(currentType);
            objectStack.Push(currentObject);

            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return VoidResult<string>.Fail($"Cannot access instance field '{member}' in a static context");
                            }

                            // Propagate null
                            return VoidResult<string>.Ok;
                        }

                        currentObject = field.GetValue(currentObject);
                    }
                    currentType = field.FieldType;

                    continue;
                }

                if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.SetMethod != null) {
                    if (PreventCodeExecution) {
                        return VoidResult<string>.Fail($"Cannot safely set property '{member}' during EnforceLegal");
                    }

                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return VoidResult<string>.Fail($"Cannot access instance property '{member}' in a static context");
                            }

                            // Propagate null
                            return VoidResult<string>.Ok;
                        }

                        currentObject = property.GetValue(currentObject);
                    }
                    currentType = property.PropertyType;

                    continue;
                }
            } catch (Exception ex) {
                ex.LogException($"Failed to get member '{member}' on type '{currentType}'");
                return VoidResult<string>.Fail($"Unknown exception: {ex}");
            }

            return VoidResult<string>.Fail($"Cannot find field / property '{member}' on type {currentType}");
        }

        // Set the value
        try {
            // Special case for Actor / Platform positions, since they use subpixels
            if (memberArgs[^1] is nameof(Entity.X) or nameof(Entity.Y)) {
                object? entityObject = null;
                if (objectStack.Count == 0) {
                    // "Entity.X"
                    entityObject = currentObject;
                } else if (objectStack.Count >= 1 && memberArgs[^2] is nameof(Entity.Position)) {
                    // "Entity.Position.X"
                    entityObject = objectStack.Peek();
                }

                if (entityObject is Actor actor) {
                    var subpixelValue = (SubpixelComponent) value!;

                    var remainder = actor.movementCounter;
                    if (memberArgs[^1] == nameof(Entity.X)) {
                        actor.Position.X = subpixelValue.Position;
                        remainder.X = subpixelValue.Remainder;
                    } else {
                        actor.Position.Y = subpixelValue.Position;
                        remainder.Y = subpixelValue.Remainder;
                    }
                    actor.movementCounter = remainder;
                    return VoidResult<string>.Ok;
                } else if (entityObject is Platform platform) {
                    var subpixelValue = (SubpixelComponent) value!;

                    var remainder = platform.movementCounter;
                    if (memberArgs[^1] == nameof(Entity.X)) {
                        platform.Position.X = subpixelValue.Position;
                        remainder.X = subpixelValue.Remainder;
                    } else {
                        platform.Position.Y = subpixelValue.Position;
                        remainder.Y = subpixelValue.Remainder;
                    }
                    platform.movementCounter = remainder;
                    return VoidResult<string>.Ok;
                }
            } else if (memberArgs[^1] is nameof(Entity.Position)) {
                if (currentObject is Actor actor) {
                    var subpixelValue = (SubpixelPosition) value!;

                    actor.Position = new(subpixelValue.X.Position, subpixelValue.Y.Position);
                    actor.movementCounter = new(subpixelValue.X.Remainder, subpixelValue.Y.Remainder);
                    return VoidResult<string>.Ok;
                } else if (currentObject is Platform platform) {
                    var subpixelValue = (SubpixelPosition) value!;

                    platform.Position = new(subpixelValue.X.Position, subpixelValue.Y.Position);
                    platform.movementCounter = new(subpixelValue.X.Remainder, subpixelValue.Y.Remainder);
                    return VoidResult<string>.Ok;
                }
            }

            if (currentType.GetFieldInfo(memberArgs[^1], logFailure: false) is { } field) {
                if (field.IsStatic) {
                    field.SetValue(null, value);
                } else {
                    if (currentObject == null) {
                        if (currentType == baseType) {
                            return VoidResult<string>.Fail($"Cannot access instance field '{memberArgs[^1]}' in a static context");
                        }

                        // Propagate null
                        return VoidResult<string>.Ok;
                    }

                    field.SetValue(currentObject, value);
                }
            } else if (currentType.GetPropertyInfo(memberArgs[^1], logFailure: false) is { } property && property.SetMethod != null) {
                // Special case to support binding custom keys
                if (property.PropertyType == typeof(ButtonBinding) && !PreventCodeExecution && property.GetValue(currentObject) is ButtonBinding binding) {
                    var nodes = binding.Button.Nodes;
                    var mouseButtons = binding.Button.Binding.Mouse;
                    var data = (ButtonBindingData)value!;

                    if (data.KeyboardKeys.IsNotEmpty()) {
                        foreach (var node in nodes.ToList()) {
                            if (node is VirtualButton.KeyboardKey) {
                                nodes.Remove(node);
                            }
                        }

                        nodes.AddRange(data.KeyboardKeys.Select(key => new VirtualButton.KeyboardKey(key)));
                    }

                    if (data.MouseButtons.IsNotEmpty()) {
                        foreach (var node in nodes.ToList()) {
                            switch (node) {
                                case VirtualButton.MouseLeftButton:
                                case VirtualButton.MouseRightButton:
                                case VirtualButton.MouseMiddleButton:
                                    nodes.Remove(node);
                                    break;
                            }
                        }

                        if (mouseButtons != null) {
                            mouseButtons.Clear();
                            foreach (var button in data.MouseButtons) {
                                mouseButtons.Add(button);
                            }
                        } else {
                            foreach (var button in data.MouseButtons) {
                                switch (button)
                                {
                                    case MInput.MouseData.MouseButtons.Left:
                                        nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseLeftButton()));
                                        break;
                                    case MInput.MouseData.MouseButtons.Right:
                                        nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseRightButton()));
                                        break;
                                    case MInput.MouseData.MouseButtons.Middle:
                                        nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseMiddleButton()));
                                        break;
                                    case MInput.MouseData.MouseButtons.XButton1 or MInput.MouseData.MouseButtons.XButton2:
                                        return VoidResult<string>.Fail("X1 and X2 are not supported before Everest adding mouse support");
                                }
                            }
                        }
                    }
                    return VoidResult<string>.Ok;
                }

                if (PreventCodeExecution) {
                    return VoidResult<string>.Fail($"Cannot safely set property '{memberArgs[^1]}' during EnforceLegal");
                }

                if (property.IsStatic()) {
                    property.SetValue(null, value);
                } else {
                    if (currentObject == null) {
                        if (currentType == baseType) {
                            return VoidResult<string>.Fail($"Cannot access instance field '{memberArgs[^1]}' in a static context");
                        }

                        // Propagate null
                        return VoidResult<string>.Ok;
                    }

                    property.SetValue(currentObject, value);
                }
            } else {
                return VoidResult<string>.Fail($"Cannot find field / property '{memberArgs[^1]}' on type {currentType}");
            }
        } catch (Exception ex) {
            ex.LogException($"Failed to set member '{memberArgs[^1]}' on type '{currentType}' to value '{value}'");
            return VoidResult<string>.Fail($"Unknown exception: {ex}");
        }

        // Recurse back up to properly set value-types
        for (int i = memberArgs.Length - 2; i >= 0 && currentType.IsValueType; i--) {
            value = currentObject;
            currentType = typeStack.Pop();
            currentObject = objectStack.Pop();

            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                    if (field.IsStatic) {
                        field.SetValue(null, value);
                    } else {
                        field.SetValue(currentObject, value);
                    }
                } else if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.SetMethod != null) {
                    if (PreventCodeExecution) {
                        return VoidResult<string>.Fail($"Cannot safely set property '{member}' during EnforceLegal");
                    }

                    if (property.IsStatic()) {
                        property.SetValue(null, value);
                    } else {
                        property.SetValue(currentObject, value);
                    }
                }
            } catch (Exception ex) {
                ex.LogException($"Failed to set member '{member}' on type '{currentType}' to value '{value}' (value-type back-recursion)");
                return VoidResult<string>.Fail($"Unknown exception: {ex}");
            }
        }

        return VoidResult<string>.Ok;
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static VoidResult<string> SetMemberValues(Type baseType, List<object>? baseObjects, object? value, string[] memberArgs) {
        if (baseObjects == null) {
            // Static target context
            return SetMemberValue(baseType, null, value, memberArgs);
        }
        if (baseObjects.IsEmpty()) {
            return VoidResult<string>.Ok; // Nothing to do
        }

        return baseObjects
            .Select(obj => SetMemberValue(baseType, obj, value, memberArgs))
            .Aggregate(VoidResult<string>.AggregateError);
    }

    /// Recursively resolves the value of the specified members
    public static VoidResult<string> InvokeMemberMethod(Type baseType, object? baseObject, object?[] parameters, string[] memberArgs) {
        if (PreventCodeExecution) {
            return VoidResult<string>.Fail("Cannot safely invoke methods during EnforceLegal");
        }

        var currentType = baseType;
        object? currentObject = baseObject;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member, logFailure: false) is { } field) {
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return VoidResult<string>.Fail($"Cannot access instance field '{member}' in a static context");
                            }

                            // Propagate null
                            return VoidResult<string>.Ok;
                        }

                        currentObject = field.GetValue(currentObject);
                    }
                    currentType = field.FieldType;

                    continue;
                }

                if (currentType.GetPropertyInfo(member, logFailure: false) is { } property && property.SetMethod != null) {
                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            if (currentType == baseType) {
                                return VoidResult<string>.Fail($"Cannot access property field '{member}' in a static context");
                            }

                            // Propagate null
                            return VoidResult<string>.Ok;
                        }

                        currentObject = property.GetValue(currentObject);
                    }
                    currentType = property.PropertyType;

                    continue;
                }
            } catch (Exception ex) {
                ex.LogException($"Failed to get member '{member}' on type '{currentType}'");
                return VoidResult<string>.Fail($"Unknown exception: {ex}");
            }

            // Unable to recurse further
            return VoidResult<string>.Fail($"Cannot find field / property '{member}' on type {currentType}");
        }

        // Invoke the method
        try {
            if (currentType.GetMethodInfo(memberArgs[^1], logFailure: false) is { } method) {
                if (method.IsStatic) {
                    method.Invoke(null, parameters);
                } else {
                    if (currentObject == null) {
                        if (currentType == baseType) {
                            return VoidResult<string>.Fail($"Cannot access property field '{memberArgs[^1]}' in a static context");
                        }

                        // Propagate null
                        return VoidResult<string>.Ok;
                    }

                    method.Invoke(currentObject, parameters);
                }

                return VoidResult<string>.Ok;
            }
        } catch (Exception ex) {
            ex.LogException($"Failed to invoke method '{memberArgs[^1]}' on type '{currentType}' with parameters ({string.Join(", ", parameters)})");
            return VoidResult<string>.Fail($"Unknown exception: {ex}");
        }

        // Unable to recurse further
        return VoidResult<string>.Fail($"Cannot find field / property '{memberArgs[^1]}' on type {currentType}");
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static VoidResult<string> InvokeMemberMethods(Type baseType, List<object>? baseObjects, object?[] parameters, string[] memberArgs) {
        if (baseObjects == null) {
            // Static target context
            return InvokeMemberMethod(baseType, null, parameters, memberArgs);
        }
        if (baseObjects.IsEmpty()) {
            return VoidResult<string>.Ok; // Nothing to do
        }

        return baseObjects
            .Select(obj => InvokeMemberMethod(baseType, obj, parameters, memberArgs))
            .Aggregate(VoidResult<string>.AggregateError);
    }

    /// Data-class to hold parsed ButtonBinding data, before it being set
    private class ButtonBindingData {
        public readonly HashSet<Keys> KeyboardKeys = [];
        public readonly HashSet<MInput.MouseData.MouseButtons> MouseButtons = [];
    }

    /// Resolves the value arguments into the specified types if possible
    public static Result<object?[], string> ResolveValues(string[] valueArgs, Type[] targetTypes) {
        var values = new object?[targetTypes.Length];
        int index = 0;

        for (int i = 0; i < valueArgs.Length; i++) {
            if (index >= targetTypes.Length) {
                return Result<object?[], string>.Fail($"Too many arguments");
            }

            string arg = valueArgs[i];
            var targetType = targetTypes[index];
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try {
                if (arg.Contains('.') && !float.TryParse(arg, out _)) {
                    // The value is a target-query, which needs to be resolved
                    var result = GetMemberValues(arg);
                    if (result.Failure) {
                        return Result<object?[], string>.Fail(result.Error.ToString());
                    }
                    if (result.Value.Count != 1) {
                        return Result<object?[], string>.Fail($"Target-query '{arg}' for type '{targetType}' needs to resolve to exactly 1 value! Got {result.Value.Count}");
                    }
                    if (result.Value[0].Value != null && !result.Value[0].Value!.GetType().IsSameOrSubclassOf(targetType)) {
                        return Result<object?[], string>.Fail($"Expected type '{targetType}' for target-query '{arg}'! Got {result.Value[0].GetType()}");
                    }

                    values[index++] = result.Value[0].Value;
                    continue;
                }

                if (targetType == typeof(Vector2)) {
                    values[index++] = new Vector2(
                        float.Parse(valueArgs[i + 0]),
                        float.Parse(valueArgs[i + 1]));
                    i++; // Account for second argument
                    continue;
                }

                if (targetType == typeof(SubpixelComponent)) {
                    double doubleValue = double.Parse(valueArgs[i]);

                    int position = (int) Math.Round(doubleValue);
                    float remainder = (float) (doubleValue - position);

                    values[index++] = new SubpixelComponent(position, remainder);
                    continue;
                }
                if (targetType == typeof(SubpixelPosition)) {
                    double doubleValueX = double.Parse(valueArgs[i + 0]);
                    double doubleValueY = double.Parse(valueArgs[i + 1]);

                    int positionX = (int) Math.Round(doubleValueX);
                    int positionY = (int) Math.Round(doubleValueY);
                    float remainderX = (float) (doubleValueX - positionX);
                    float remainderY = (float) (doubleValueY - positionY);

                    values[index++] = new SubpixelPosition(
                        new SubpixelComponent(positionX, remainderX),
                        new SubpixelComponent(positionY, remainderY));
                    i++; // Account for second argument
                    continue;
                }

                if (targetType == typeof(Random)) {
                    values[index++] = new Random(int.Parse(arg));
                    continue;
                }

                if (targetType == typeof(ButtonBinding)) {
                    var data = new ButtonBindingData();

                    // Parse all possible keys
                    int j = i;
                    for (; j < valueArgs.Length; j++) {
                        // Parse mouse first, so Mouse.Left is not parsed as Keys.Left
                        if (Enum.TryParse<MInput.MouseData.MouseButtons>(valueArgs[j], ignoreCase: true, out var button)) {
                            data.MouseButtons.Add(button);
                        } else if (Enum.TryParse<Keys>(valueArgs[j], ignoreCase: true, out var key)) {
                            data.KeyboardKeys.Add(key);
                        } else {
                            if (j == i) {
                                return Result<object?[], string>.Fail($"'{valueArgs[j]}' is not a valid keyboard key or mouse button");
                            }

                            break;
                        }
                    }
                    i = j - 1;

                    values[index++] = data;
                    continue;
                }

                if (targetType.IsEnum) {
                    if (Enum.TryParse(targetType, arg, ignoreCase: true, out var value) && (int) value < Enum.GetNames(targetType).Length) {
                        values[index++] = value;
                        continue;
                    } else {
                        return Result<object?[], string>.Fail($"'{arg}' is not a valid enum state for '{targetType.FullName}'");
                    }
                }

                if (string.IsNullOrWhiteSpace(arg) || arg == "null") {
                    values[index++] = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                    continue;
                }

                values[index++] = Convert.ChangeType(arg, targetType);
            } catch (Exception ex) {
                ex.LogException($"Failed to resolve value for type '{targetType}'");
                return Result<object?[], string>.Fail($"Failed to resolve value for type '{targetType}': {ex}");
            }
        }

        return Result<object?[], string>.Ok(values);
    }
}
