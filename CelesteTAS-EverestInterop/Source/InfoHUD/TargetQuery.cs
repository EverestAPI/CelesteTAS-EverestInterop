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

namespace TAS.InfoHUD;

/// Contains all the logic for getting data from a target-query
public static class TargetQuery {
    /// Prevents invocations of methods / execution of Lua code in the Custom Info
    public static bool PreventCodeExecution => EnforceLegalCommand.EnabledWhenRunning;

    private static readonly Dictionary<string, List<Type>> allTypes = new();
    private static readonly Dictionary<string, (List<Type> Types, List<Type> ComponentTypes, EntityID? EntityID)> baseTypeCache = [];

    /// Searches for the target type, optional target assembly, optional component type, optional component assembly, and optional EntityID
    private static readonly Regex BaseTypeRegex = new(@"^([\w.]+)(@(?:[^.:\[\]\n]*))?(?::(\w+))?(@(?:[^.:\[\]\n]*))?(?:\[(.+):(\d+)\])?$", RegexOptions.Compiled);

    [Initialize]
    private static void CollectAllTypes() {
        allTypes.Clear();
        baseTypeCache.Clear();

        foreach (var type in ModUtils.GetTypes()) {
            if (type.FullName is { } fullName) {
                string assemblyName = type.Assembly.GetName().Name!;
                string modName = ConsoleEnhancements.GetModName(type);

                // Strip namespace
                int namespaceLen = type.Namespace != null
                    ? type.Namespace.Length + 1
                    : 0;
                string shortName = type.FullName[namespaceLen..];

                // Use '.' instead of '+' for nested types
                fullName = fullName.Replace('+', '.');
                shortName = shortName.Replace('+', '.');

                allTypes.AddToKey(fullName, type);
                allTypes.AddToKey($"{fullName}@{assemblyName}", type);
                allTypes.AddToKey($"{fullName}@{modName}", type);

                allTypes.AddToKey(shortName, type);
                allTypes.AddToKey($"{shortName}@{assemblyName}", type);
                allTypes.AddToKey($"{shortName}@{modName}", type);
            }
        }
    }

    [MonocleCommand("get", "'get Type.fieldOrProperty' -> value | Example: 'get Player.Position', 'get Level.Wind' (CelesteTAS)"), UsedImplicitly]
    private static void GetCommand(string? query) {
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
            foreach ((object? value, object? baseInstance) in result.Value) {
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
    public static Result<List<(object? Value, object? BaseInstance)>, string> GetMemberValues(string query, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs, out var componentTypes, out var entityId);
        if (baseTypes.IsEmpty()) {
            return Result<List<(object? Value, object? BaseInstance)>, string>.Fail($"Failed to find base type for target-query '{query}'");
        }
        if (memberArgs.IsEmpty()) {
            return Result<List<(object? Value, object? BaseInstance)>, string>.Fail("No members specified");
        }

        List< (object? Value, object? BaseInstance)> allResults = [];
        foreach (var baseType in baseTypes) {
            var instances = ResolveTypeInstances(baseType, componentTypes, entityId);

            if (componentTypes.IsEmpty()) {
                if (ProcessType(baseType).CheckFailure(out string? error)) {
                    return Result<List<(object? Value, object? BaseInstance)>, string>.Fail(error);
                }
            } else {
                foreach (var componentType in componentTypes) {
                    if (ProcessType(componentType).CheckFailure(out string? error)) {
                        return Result<List<(object? Value, object? BaseInstance)>, string>.Fail(error);
                    }
                }
            }
            continue;

            VoidResult<string> ProcessType(Type type) {
                var result = ResolveMemberValues(type, instances, memberArgs, forceAllowCodeExecution);
                if (result.Failure) {
                    return VoidResult<string>.Fail(result.Error);
                }

                if (instances == null) {
                    allResults.Add((result.Value[0], null));
                } else {
                    allResults.AddRange(result.Value.Select((value, i) => (value, (object?)instances[i])));
                }

                return VoidResult<string>.Ok;
            }
        }

        return Result<List<(object? Value, object? BaseInstance)>, string>.Ok(allResults);
    }

    /// Parses the first part of a query into types and an optional EntityID
    public static List<Type> ResolveBaseTypes(string[] queryArgs, out string[] memberArgs, out List<Type> componentTypes, out EntityID? entityId) {
        componentTypes = [];
        entityId = null;

        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            memberArgs = queryArgs;
            return [typeof(Settings)];
        }
        if (typeof(SaveData).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            memberArgs = queryArgs;
            return [typeof(SaveData)];
        }
        if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            memberArgs = queryArgs;
            return [typeof(Assists)];
        }

        // Check for mod settings
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
            memberArgs = queryArgs[1..];
            return [module.SettingsType];
        }

        // Greedily increase amount of tested arguments
        string currentType = string.Empty;
        int currentIndex = 0;

        for (int i = 1; i <= queryArgs.Length; i++) {
            string typeName = string.Join('.', queryArgs[..i]);

            if (baseTypeCache.ContainsKey(typeName)) {
                currentType = typeName;
                currentIndex = i;
                continue;
            }

            var match = BaseTypeRegex.Match(typeName);
            if (!match.Success) {
                break; // No further matches
            }

            // Remove the entity ID from the type check
            string checkTypeName = $"{match.Groups[1].Value}{match.Groups[2].Value}";
            string componentTypeName = $"{match.Groups[3].Value}{match.Groups[4].Value}";

            if (int.TryParse(match.Groups[6].Value, out int id)) {
                entityId = new EntityID(match.Groups[5].Value, id);
            }

            if (!allTypes.TryGetValue(checkTypeName, out var types)) {
                break; // No further existing types
            }

            if (!allTypes.TryGetValue(componentTypeName, out componentTypes!)) {
                componentTypes = [];
            }

            baseTypeCache[typeName] = (Types: types, ComponentTypes: componentTypes, EntityID: entityId);
            currentType = typeName;
            currentIndex = i;
        }

        if (baseTypeCache.TryGetValue(currentType, out var pair)) {
            componentTypes = pair.ComponentTypes;
            entityId = pair.EntityID;
            memberArgs = queryArgs[currentIndex..];
            return pair.Types;
        }

        // No matching type found
        memberArgs = queryArgs;
        return [];
    }

    /// Resolves a type into all applicable instances of it
    /// Returns null for types which are always in a static context
    public static List<object>? ResolveTypeInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (type == typeof(Settings)) {
            return [Settings.Instance];
        }
        if (type == typeof(SaveData)) {
            return [Settings.Instance];
        }
        if (type == typeof(Assists)) {
            return [Settings.Instance];
        }

        if (type.IsSameOrSubclassOf(typeof(EverestModuleSettings))) {
            return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
        }

        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            IEnumerable<Entity> entityInstances;
            if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
                entityInstances = entities
                    .Where(e => entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key);
            } else {
                entityInstances = Engine.Scene.Entities
                    .Where(e => e.GetType().IsSameOrSubclassOf(type) && (entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key));
            }

            if (componentTypes.IsEmpty()) {
                return entityInstances
                    .Select(e => (object) e)
                    .ToList();
            } else {
                return entityInstances
                    .SelectMany(e => e.Components.Where(c => componentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType))))
                    .Select(c => (object) c)
                    .ToList();
            }
        }

        if (type.IsSameOrSubclassOf(typeof(Component))) {
            IEnumerable<Component> componentInstances;
            if (Engine.Scene.Tracker.Components.TryGetValue(type, out var components)) {
                componentInstances = components;
            } else {
                componentInstances = Engine.Scene.Entities
                    .SelectMany(e => e.Components)
                    .Where(c => c.GetType().IsSameOrSubclassOf(type));
            }

            return componentInstances
                .Select(c => (object) c)
                .ToList();
        }

        if (Engine.Scene is Level level) {
            if (type == typeof(Session)) {
                return [level.Session];
            }
        }
        if (Engine.Scene.GetType() == type) {
            return [Engine.Scene];
        }

        // Nothing found
        return null;
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

    /// Recursively resolves the value of the specified members
    public static Result<object?, string> ResolveMemberValue(Type baseType, object? baseObject, string[] memberArgs, bool forceAllowCodeExecution = false) {
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
                return Result<object?, string>.Fail($"Unknown exception: {ex}");
            }

            return Result<object?, string>.Fail($"Cannot find field / property '{member}' on type {currentType}");
        }

        return Result<object?, string>.Ok(currentObject);
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static Result<List<object?>, string> ResolveMemberValues(Type baseType, List<object>? baseObjects, string[] memberArgs, bool forceAllowCodeExecution = false) {
        if (baseObjects == null) {
            // Static target context
            var result = ResolveMemberValue(baseType, null, memberArgs, forceAllowCodeExecution);
            if (result.Failure) {
                return Result<List<object?>, string>.Fail(result.Error);
            }

            return Result<List<object?>, string>.Ok([result.Value]);
        }

        List<object?> values = new(capacity: baseObjects.Count);

        foreach (object obj in baseObjects) {
            var result = ResolveMemberValue(baseType, obj, memberArgs, forceAllowCodeExecution);
            if (result.Failure) {
                return Result<List<object?>, string>.Fail(result.Error);
            }

            values.Add(result.Value);
        }

        return Result<List<object?>, string>.Ok(values);
    }

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
            var arg = valueArgs[i];
            var targetType = targetTypes[index];
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try {
                if (arg.Contains('.') && !float.TryParse(arg, out _)) {
                    // The value is a target-query, which needs to be resolved
                    var result = GetMemberValues(arg);
                    if (result.Failure) {
                        return Result<object?[], string>.Fail(result.Error);
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
                    // Parse mouse first, so Mouse.Left is not parsed as Keys.Left
                    if (Enum.TryParse<MInput.MouseData.MouseButtons>(arg, ignoreCase: true, out var button)) {
                        data.MouseButtons.Add(button);
                    } else if (Enum.TryParse<Keys>(arg, ignoreCase: true, out var key)) {
                        data.KeyboardKeys.Add(key);
                    } else {
                        return Result<object?[], string>.Fail($"'{arg}' is not a valid keyboard key or mouse button");
                    }

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
                return Result<object?[], string>.Fail($"Failed to resolve value for type '{targetType}': {ex}");
            }
        }

        return Result<object?[], string>.Ok(values);
    }
}
