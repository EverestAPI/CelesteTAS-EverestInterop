using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    [Command("get2", "get type.fieldOrProperty value. eg get Player,Position; get Level.Wind (CelesteTAS)"), UsedImplicitly]
    private static void GetCommand(string template) {
        var templateArgs = template.Split('.');

        var baseTypes = ResolveBaseTypes(templateArgs, out var memberArgs, out var entityId);
        if (baseTypes.IsEmpty()) {
            $"Failed to find base type for template '{template}'".ConsoleLog(LogLevel.Error);
            return;
        }
        if (memberArgs.IsEmpty()) {
            $"No members specified".ConsoleLog(LogLevel.Error);
            return;
        }

        entityId.ConsoleLog();
        baseTypes.ForEach(t => {
            t.ConsoleLog();

            var instances = ResolveTypeInstances(t, entityId);
            if (instances.IsEmpty()) {
                $" -> {GetMemberValue(t, null, memberArgs)}".ConsoleLog();
            } else {
                instances.ForEach(o => $" * {GetMemberValue(t, o, memberArgs)}".ConsoleLog());
            }
        });
    }

    /// Parses the first part of a template into types and an optional EntityID
    public static List<Type> ResolveBaseTypes(string[] templateArgs, out string[] memberArgs, out EntityID? entityId) {
        entityId = null;

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
        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
                return [..entities];
            } else {
                return Engine.Scene.Entities
                    .Where(e => e.GetType() == type)
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

    /// Recursively resolves the value of the specified members
    public static (object? Result, bool Success) GetMemberValue(Type baseType, object? baseObject, string[] memberArgs) {
        var currentType = baseType;
        var currentObject = baseObject;
        foreach (string member in memberArgs) {
            if (currentType.GetFieldInfo(member) is { } field) {
                currentType = field.FieldType;
                if (field.IsStatic) {
                    currentObject = field.GetValue(null);
                } else {
                    if (currentObject == null) {
                        // Cannot access non-static fields with static "instance"
                        return (null, Success: false);
                    }
                    currentObject = field.GetValue(currentObject);
                }
                continue;
            }
            if (currentType.GetPropertyInfo(member) is { } property && property.GetGetMethod() != null) {
                currentType = property.PropertyType;
                if (property.IsStatic()) {
                    currentObject = property.GetValue(null);
                } else {
                    if (currentObject == null) {
                        // Cannot access non-static properties with static "instance"
                        return (null, Success: false);
                    }
                    currentObject = property.GetValue(currentObject);
                }
                continue;
            }

            // Unable to recurse further
            return (currentObject, Success: false);
        }

        return (currentObject, Success: true);
    }
}
