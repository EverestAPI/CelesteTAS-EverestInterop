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
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

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

        (var results, bool success, string errorMessage) = GetMemberValues(query);
        if (!success) {
            errorMessage.ConsoleLog(LogLevel.Error);
            return;
        }

        if (results.Count == 0) {
            "No instances found".ConsoleLog(LogLevel.Error);
        } else if (results.Count == 1) {
            results[0].Value.ConsoleLog();
        } else {
            foreach ((object? value, object? baseInstance) in results) {
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
    public static (List<(object? Value, object? BaseInstance)> Results, bool Success, string ErrorMessage) GetMemberValues(string query, bool forceAllowCodeExecution = false) {
        string[] queryArgs = query.Split('.');

        var baseTypes = ResolveBaseTypes(queryArgs, out string[] memberArgs, out var componentTypes, out var entityId);
        if (baseTypes.IsEmpty()) {
            return ([(null, null)], Success: false, ErrorMessage: $"Failed to find base type for target-query '{query}'");
        }
        if (memberArgs.IsEmpty()) {
            return ([(null, null)], Success: false, ErrorMessage: "No members specified");
        }

        List< (object? Value, object? BaseInstance)> allResults = [];
        foreach (var baseType in baseTypes) {
            var instances = ResolveTypeInstances(baseType, componentTypes, entityId);

            if (componentTypes.IsEmpty()) {
                if (!ProcessType(baseType, out string errorMessage)) {
                    return (Results: [], Success: false, ErrorMessage: errorMessage);
                }
            } else {
                foreach (var componentType in componentTypes) {
                    if (!ProcessType(componentType, out string errorMessage)) {
                        return (Results: [], Success: false, ErrorMessage: errorMessage);
                    }
                }
            }

            bool ProcessType(Type type, out string errorMessage) {
                (var values, bool success, errorMessage) = ResolveMemberValues(type, instances, memberArgs, forceAllowCodeExecution);
                if (!success) {
                    return false;
                }

                if (instances.IsEmpty()) {
                    allResults.Add((values[0], null));
                } else {
                    allResults.AddRange(values.Select((value, i) => (value, (object?)instances[i])));
                }

                return true;
            }
        }

        return (allResults, Success: true, ErrorMessage: string.Empty);
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
    public static List<object> ResolveTypeInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
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
        return [];
    }

    /// Recursively resolves the type of the specified members
    public static (Type Type, bool Success) ResolveMemberType(Type baseType, string[] memberArgs) {
        var typeStack = new Stack<Type>();

        var currentType = baseType;
        foreach (string member in memberArgs) {
            typeStack.Push(currentType);

            if (currentType.GetFieldInfo(member) is { } field) {
                currentType = field.FieldType;
                continue;
            }
            if (currentType.GetPropertyInfo(member) is { } property && property.GetMethod != null) {
                currentType = property.PropertyType;
                continue;
            }

            // Unable to recurse further
            return (currentType, Success: false);
        }

        // Special case for Actor / Platform positions, since they use subpixels
        if (memberArgs[^1] is nameof(Entity.X) or nameof(Entity.Y)) {
            var entityType = typeof(Entity);
            if (typeStack.Count >= 1) {
                // "Entity.X"
                entityType = typeStack.Pop();
            } else if (typeStack.Count >= 2 && memberArgs[^2] is nameof(Entity.Position)) {
                // "Entity.Position.X"
                _ = typeStack.Pop();
                entityType = typeStack.Pop();
            }

            if (entityType.IsSameOrSubclassOf(typeof(Actor)) || entityType.IsSameOrSubclassOf(typeof(Platform))) {
                return (typeof(SubpixelComponent), Success: true);
            }
        } else if (memberArgs[^1] is nameof(Entity.Position)) {
            // "Entity.Position"
            var entityType = typeStack.Pop();

            if (entityType.IsSameOrSubclassOf(typeof(Actor)) || entityType.IsSameOrSubclassOf(typeof(Platform))) {
                return (typeof(SubpixelPosition), Success: true);
            }
        }

        return (currentType, Success: true);
    }

    /// Recursively resolves a method for the specified members
    public static (MethodInfo? Method, bool Success) ResolveMemberMethod(Type baseType, string[] memberArgs) {
        var currentType = baseType;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            string member = memberArgs[i];

            if (currentType.GetFieldInfo(member) is { } field) {
                currentType = field.FieldType;
                continue;
            }

            if (currentType.GetPropertyInfo(member) is { } property && property.GetMethod != null) {
                currentType = property.PropertyType;
                continue;
            }

            // Unable to recurse further
            return (null, Success: false);
        }

        // Find method
        if (currentType.GetMethodInfo(memberArgs[^1]) is { } method) {
            return (method, Success: true);
        }

        // Couldn't find the method
        return (null, Success: true);
    }

    /// Recursively resolves the value of the specified members
    public static (object? Value, bool Success, string ErrorMessage) ResolveMemberValue(Type baseType, object? baseObject, string[] memberArgs, bool forceAllowCodeExecution = false) {
        var currentType = baseType;
        var currentObject = baseObject;
        foreach (string member in memberArgs) {
            try {
                if (currentType.GetFieldInfo(member) is { } field) {
                    currentType = field.FieldType;
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            // Propagate null
                            return (Value: null, Success: true, ErrorMessage: "");
                        }

                        currentObject = field.GetValue(currentObject);
                    }
                    continue;
                }
                if (currentType.GetPropertyInfo(member) is { } property && property.GetMethod != null) {
                    if (PreventCodeExecution && !forceAllowCodeExecution) {
                        return (Value: null, Success: false, ErrorMessage: $"Cannot safely get property '{member}' during EnforceLegal");
                    }

                    currentType = property.PropertyType;
                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        if (currentObject == null) {
                            // Propagate null
                            return (Value: null, Success: true, ErrorMessage: "");
                        }

                        currentObject = property.GetValue(currentObject);
                    }
                    continue;
                }
            } catch (Exception ex) {
                // Something went wrong
                return (currentObject, Success: false, ErrorMessage: ex.Message);
            }

            // Unable to recurse further
            return (currentObject, Success: false, ErrorMessage: $"Cannot find field / property '{member}' on type {currentType}");
        }

        return (currentObject, Success: true, ErrorMessage: "");
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static (List<object?> Values, bool Success, string ErrorMessage) ResolveMemberValues(Type baseType, List<object> baseObjects, string[] memberArgs, bool forceAllowCodeExecution = false) {
        if (baseObjects.IsEmpty()) {
            (object? result, bool success, string errorMessage) = ResolveMemberValue(baseType, null, memberArgs, forceAllowCodeExecution);
            return ([result], success, errorMessage);
        } else {
            List<object?> values = new(capacity: baseObjects.Count);

            foreach (object obj in baseObjects) {
                (object? result, bool success, string errorMessage) = ResolveMemberValue(baseType, obj, memberArgs, forceAllowCodeExecution);

                if (!success) {
                    return (Values: [], Success: false, errorMessage);
                }
                values.Add(result);
            }

            return (values, Success: true, ErrorMessage: "");
        }
    }

    /// Recursively resolves the value of the specified members
    public static bool SetMemberValue(Type baseType, object? baseObject, object? value, string[] memberArgs) {
        var typeStack = new Stack<Type>();
        var objectStack = new Stack<object?>();

        var currentType = baseType;
        object? currentObject = baseObject;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            typeStack.Push(currentType);
            objectStack.Push(currentObject);

            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member) is { } field) {
                    currentType = field.FieldType;
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        currentObject = field.GetValue(currentObject);
                    }

                    continue;
                }

                if (currentType.GetPropertyInfo(member) is { } property && property.SetMethod != null) {
                    if (PreventCodeExecution) {
                        return false; // Cannot safely invoke methods during EnforceLegal
                    }

                    currentType = property.PropertyType;
                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        currentObject = property.GetValue(currentObject);
                    }

                    continue;
                }
            } catch (Exception) {
                // Something went wrong
                return false;
            }

            // Unable to recurse further
            return false;
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
                    return true;
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
                    return true;
                }
            } else if (memberArgs[^1] is nameof(Entity.Position)) {
                if (currentObject is Actor actor) {
                    var subpixelValue = (SubpixelPosition) value!;

                    actor.Position = new(subpixelValue.X.Position, subpixelValue.Y.Position);
                    actor.movementCounter = new(subpixelValue.X.Remainder, subpixelValue.Y.Remainder);
                    return true;
                } else if (currentObject is Platform platform) {
                    var subpixelValue = (SubpixelPosition) value!;

                    platform.Position = new(subpixelValue.X.Position, subpixelValue.Y.Position);
                    platform.movementCounter = new(subpixelValue.X.Remainder, subpixelValue.Y.Remainder);
                    return true;
                }
            }

            if (currentType.GetFieldInfo(memberArgs[^1]) is { } field) {
                if (field.IsStatic) {
                    field.SetValue(null, value);
                } else {
                    field.SetValue(currentObject, value);
                }
            } else if (currentType.GetPropertyInfo(memberArgs[^1]) is { } property && property.SetMethod != null) {
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
                                        // TODO: Error message
                                        // AbortTas("X1 and X2 are not supported before Everest adding mouse support");
                                        return false;
                                }
                            }
                        }
                    }
                    return true;
                }

                if (PreventCodeExecution) {
                    return false; // Cannot safely invoke methods during EnforceLegal
                }

                if (property.IsStatic()) {
                    property.SetValue(null, value);
                } else {
                    property.SetValue(currentObject, value);
                }
            } else {
                // Couldn't find the last member
                return false;
            }
        } catch (Exception) {
            // Something went wrong
            return false;
        }

        // Recurse back up to properly set value-types
        for (int i = memberArgs.Length - 2; i >= 0 && currentType.IsValueType; i--) {
            value = currentObject;
            currentType = typeStack.Pop();
            currentObject = objectStack.Pop();

            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member) is { } field) {
                    if (field.IsStatic) {
                        field.SetValue(null, value);
                    } else {
                        field.SetValue(currentObject, value);
                    }
                } else if (currentType.GetPropertyInfo(member) is { } property && property.SetMethod != null) {
                    if (PreventCodeExecution) {
                        return false; // Cannot safely invoke methods during EnforceLegal
                    }

                    if (property.IsStatic()) {
                        property.SetValue(null, value);
                    } else {
                        property.SetValue(currentObject, value);
                    }
                }
            } catch (Exception) {
                // Something went wrong
                return false;
            }
        }

        return true;
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static bool SetMemberValues(Type baseType, List<object> baseObjects, object? value, string[] memberArgs) {
        if (baseObjects.IsEmpty()) {
            return SetMemberValue(baseType, null, value, memberArgs);
        } else {
            return baseObjects
                .Select(obj => SetMemberValue(baseType, obj, value, memberArgs))
                .All(success => success);
        }
    }

    /// Recursively resolves the value of the specified members
    public static bool InvokeMemberMethod(Type baseType, object? baseObject, object?[] parameters, string[] memberArgs) {
        if (PreventCodeExecution) {
            return false; // Cannot safely invoke methods during EnforceLegal
        }

        var currentType = baseType;
        object? currentObject = baseObject;
        for (int i = 0; i < memberArgs.Length - 1; i++) {
            string member = memberArgs[i];

            try {
                if (currentType.GetFieldInfo(member) is { } field) {
                    currentType = field.FieldType;
                    if (field.IsStatic) {
                        currentObject = field.GetValue(null);
                    } else {
                        currentObject = field.GetValue(currentObject);
                    }

                    continue;
                }

                if (currentType.GetPropertyInfo(member) is { } property && property.SetMethod != null) {
                    if (PreventCodeExecution) {
                        return false; // Cannot safely invoke methods during EnforceLegal
                    }

                    currentType = property.PropertyType;
                    if (property.IsStatic()) {
                        currentObject = property.GetValue(null);
                    } else {
                        currentObject = property.GetValue(currentObject);
                    }

                    continue;
                }
            } catch (Exception) {
                // Something went wrong
                return false;
            }

            // Unable to recurse further
            return false;
        }

        // Invoke the method
        try {
            if (currentType.GetMethodInfo(memberArgs[^1]) is { } method) {
                if (method.IsStatic) {
                    method.Invoke(null, parameters);
                } else {
                    method.Invoke(currentObject, parameters);
                }
                return true;
            }
        } catch (Exception) {
            // Something went wrong
            return false;
        }

        // Couldn't find the method
        return false;
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static bool InvokeMemberMethods(Type baseType, List<object> baseObjects, object?[] parameters, string[] memberArgs) {
        if (baseObjects.IsEmpty()) {
            return InvokeMemberMethod(baseType, null, parameters, memberArgs);
        } else {
            return baseObjects
                .Select(obj => InvokeMemberMethod(baseType, obj, parameters, memberArgs))
                .All(success => success);
        }
    }

    /// Data-class to hold parsed ButtonBinding data, before it being set
    private class ButtonBindingData {
        public readonly HashSet<Keys> KeyboardKeys = [];
        public readonly HashSet<MInput.MouseData.MouseButtons> MouseButtons = [];
    }

    /// Resolves the value arguments into the specified types if possible
    public static (object?[] Values, bool Success, string ErrorMessage) ResolveValues(string[] valueArgs, Type[] targetTypes) {
        var values = new object?[targetTypes.Length];
        int index = 0;

        for (int i = 0; i < valueArgs.Length; i++) {
            var arg = valueArgs[i];
            var targetType = targetTypes[index];
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try {
                if (arg.Contains('.') && !float.TryParse(arg, out _)) {
                    // The value is a target-query, which needs to be resolved
                    (var results, bool success, string errorMessage) = GetMemberValues(arg);
                    if (!success) {
                        return ([], Success: false, ErrorMessage: errorMessage);
                    }
                    if (results.Count != 1) {
                        return ([], Success: false, ErrorMessage: $"Target-query '{arg}' for type '{targetType}' needs to resolve to exactly 1 value! Got {results.Count}");
                    }
                    if (results[0].Value != null && !results[0].Value!.GetType().IsSameOrSubclassOf(targetType)) {
                        return ([], Success: false, ErrorMessage: $"Expected type '{targetType}' for target-query '{arg}'! Got {results[0].GetType()}");
                    }

                    values[index++] = results[0].Value;
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
                        return ([], Success: false, ErrorMessage: $"'{arg}' is not a valid keyboard key or mouse button");
                    }

                    values[index++] = data;
                    continue;
                }

                if (targetType.IsEnum) {
                    if (Enum.TryParse(targetType, arg, ignoreCase: true, out var value) && (int) value < Enum.GetNames(targetType).Length) {
                        values[index++] = value;
                        continue;
                    } else {
                        return ([], Success: false, ErrorMessage: $"'{arg}' is not a valid enum state for '{targetType.FullName}'");
                    }
                }

                if (string.IsNullOrWhiteSpace(arg) || arg == "null") {
                    values[index++] = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                    continue;
                }

                values[index++] = Convert.ChangeType(arg, targetType);
            } catch (Exception ex) {
                return ([], Success: false, ErrorMessage: $"Failed to resolve value for type '{targetType}': {ex}");
            }
        }

        return (values, Success: true, ErrorMessage: string.Empty);
    }
}
