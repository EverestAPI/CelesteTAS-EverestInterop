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
using TAS.Module;
using TAS.Utils;

#nullable enable

namespace TAS.EverestInterop;

/// Contains all the logic for getting data from an info template
public static class InfoTemplate {
    private static readonly Dictionary<string, List<Type>> allTypes = new();
    private static readonly Dictionary<string, (List<Type> types, EntityID? entityId)> baseTypeCache = [];

    private static readonly Regex BaseTypeRegex = new(@"^([\w.]+)(?:\[(.+):(\d+)\])?(@(?:[^.]*))?$", RegexOptions.Compiled);

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

    [Monocle.Command("get2", "'get Type.fieldOrProperty' -> value | Example: 'get Player.Position', 'get Level.Wind' (CelesteTAS)"), UsedImplicitly]
    private static void GetCommand(string template) {
        var (results, success, errorMessage) = GetMemberValues(template);
        if (!success) {
            errorMessage.ConsoleLog(LogLevel.Error);
            return;
        }

        if (results.Count == 1) {
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

    /// Parses a target-template and returns the results for that
    public static (List<(object? Value, object? BaseInstance)> Results, bool Success, string ErrorMessage) GetMemberValues(string template) {
        var templateArgs = template.Split('.');

        var baseTypes = ResolveBaseTypes(templateArgs, out var memberArgs, out var entityId);
        if (baseTypes.IsEmpty()) {
            return ([(null, null)], Success: false, ErrorMessage: $"Failed to find base type for template '{template}'");
        }
        if (memberArgs.IsEmpty()) {
            return ([(null, null)], Success: false, ErrorMessage: "No members specified");
        }

        List< (object? Value, object? BaseInstance)> allResults = [];
        foreach (var type in baseTypes) {
            var instances = ResolveTypeInstances(type, entityId);
            var (values, success) = ResolveMemberValues(type, instances, memberArgs);

            if (!success) {
                return ([(null, null)], Success: false, ErrorMessage: $"Failed resolving members '{string.Join('.', memberArgs)}' for type '{type}'");
            }

            allResults.AddRange(values.Select((value, i) => (value, (object?)instances[i])));
        }

        return (allResults, Success: true, ErrorMessage: string.Empty);
    }

    /// Parses the first part of a template into types and an optional EntityID
    public static List<Type> ResolveBaseTypes(string[] templateArgs, out string[] memberArgs, out EntityID? entityId) {
        entityId = null;

        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetFields().FirstOrDefault(f => f.Name == templateArgs[0]) != null) {
            memberArgs = templateArgs;
            return [typeof(Settings)];
        }
        if (typeof(SaveData).GetFields().FirstOrDefault(f => f.Name == templateArgs[0]) != null) {
            memberArgs = templateArgs;
            return [typeof(SaveData)];
        }
        if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == templateArgs[0]) != null) {
            memberArgs = templateArgs;
            return [typeof(Assists)];
        }

        // Check for mod settings
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == templateArgs[0]) is { } module) {
            memberArgs = templateArgs[1..];
            return [module.SettingsType];
        }

        // Simply increase used arguments until something is found
        for (int i = 1; i <= templateArgs.Length; i++) {
            string typeName = string.Join('.', templateArgs[..i]);

            if (baseTypeCache.TryGetValue(typeName, out var pair)) {
                entityId = pair.entityId;
                memberArgs = templateArgs[i..];
                return pair.types;
            }

            var match = BaseTypeRegex.Match(typeName);
            if (!match.Success) {
                continue;
            }

            // Remove the entity ID from the type check
            string checkTypeName = $"{match.Groups[1].Value}{match.Groups[4].Value}";

            if (int.TryParse(match.Groups[3].Value, out int id)) {
                entityId = new EntityID(match.Groups[2].Value, id);
            }

            if (allTypes.TryGetValue(checkTypeName, out var types)) {
                baseTypeCache[typeName] = (types, entityId);
                memberArgs = templateArgs[i..];
                return types;
            }
        }

        memberArgs = templateArgs;
        return [];
    }

    /// Resolves a type into all applicable instances of it
    public static List<object> ResolveTypeInstances(Type type, EntityID? entityId) {
        if (type.IsSameOrSubclassOf(typeof(EverestModuleSettings))) {
            return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
        }

        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
                return entities
                    .Where(e => entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key)
                    .Select(e => (object)e)
                    .ToList();
            } else {
                return Engine.Scene.Entities
                    .Where(e => e.GetType().IsSameOrSubclassOf(type) && (entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key))
                    .Select(e => (object)e)
                    .ToList();
            }
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
        var currentType = baseType;
        foreach (string member in memberArgs) {
            if (currentType.GetFieldInfo(member) is { } field) {
                currentType = field.FieldType;
                continue;
            }
            if (currentType.GetPropertyInfo(member) is { } property && property.GetGetMethod() != null) {
                currentType = property.PropertyType;
                continue;
            }

            // Unable to recurse further
            return (currentType, Success: false);
        }

        return (currentType, Success: true);
    }

    /// Recursively resolves the value of the specified members
    public static (object? Value, bool Success) ResolveMemberValue(Type baseType, object? baseObject, string[] memberArgs) {
        var currentType = baseType;
        var currentObject = baseObject;
        foreach (string member in memberArgs) {
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
                if (currentType.GetPropertyInfo(member) is { } property && property.GetGetMethod() != null) {
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
                return (currentObject, Success: false);
            }

            // Unable to recurse further
            return (currentObject, Success: false);
        }

        return (currentObject, Success: true);
    }

    /// Recursively resolves the value of the specified members for multiple instances at once
    public static (List<object?> Values, bool Success) ResolveMemberValues(Type baseType, List<object> baseObjects, string[] memberArgs) {
        if (baseObjects.IsEmpty()) {
            (object? result, bool success) = ResolveMemberValue(baseType, null, memberArgs);
            return ([result], success);
        } else {
            var pairs = baseObjects.Select(obj => ResolveMemberValue(baseType, obj, memberArgs)).ToArray();
            return (pairs.Select(pair => pair.Value).ToList(), pairs.All(pair => pair.Success));
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

                if (currentType.GetPropertyInfo(member) is { } property && property.GetSetMethod() != null) {
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
            if (currentType.GetFieldInfo(memberArgs[^1]) is { } field) {
                if (field.IsStatic) {
                    field.SetValue(null, value);
                } else {
                    field.SetValue(currentObject, value);
                }
            } else if (currentType.GetPropertyInfo(memberArgs[^1]) is { } property && property.GetSetMethod() != null) {
                // Special case to support binding custom keys
                if (property.PropertyType == typeof(ButtonBinding) && property.GetValue(currentObject) is ButtonBinding binding) {
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
                } else if (currentType.GetPropertyInfo(member) is { } property && property.GetSetMethod() != null) {
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
                if (arg.Contains('.')) {
                    // The value is a template-target, which needs to be resolved
                    (var results, bool success, string errorMessage) = GetMemberValues(arg);
                    if (!success) {
                        return ([], Success: false, ErrorMessage: errorMessage);
                    }
                    if (results.Count != 1) {
                        return ([], Success: false, ErrorMessage: $"Target-template '{arg}' for type '{targetType}' needs to resolve to exactly 1 value! Got {results.Count}");
                    }
                    if (results[0].Value != null && !results[0].Value!.GetType().IsSameOrSubclassOf(targetType)) {
                        return ([], Success: false, ErrorMessage: $"Expected type '{targetType}' for target-template '{arg}'! Got {results[0].GetType()}");
                    }

                    values[index++] = results[0].Value;
                    continue;
                }

                if (targetType == typeof(Vector2)) {
                    values[index++] = new Vector2(
                        float.Parse(valueArgs[i + 0]),
                        float.Parse(valueArgs[i + 1]));
                    i++;
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
