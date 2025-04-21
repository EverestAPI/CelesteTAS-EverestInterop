using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.InfoHUD;

internal class SettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Settings);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(Settings)], queryArgs);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return [Settings.Instance];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetFieldInfo(e)!);
        foreach (var f in vanillaSettings) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Settings)", IsDone = true };
        }
    }
}

internal class SaveDataQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(SaveData);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(SaveData).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(SaveData)], queryArgs);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return SaveData.Instance != null ? [SaveData.Instance] : [];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetFieldInfo(e)!);
        foreach (var f in vanillaSaveData) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Save Data)", IsDone = true };
        }
    }
}

internal class AssistsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Assists);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(Assists)], queryArgs);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return SaveData.Instance != null ? [SaveData.Instance.Assists] : [];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var f in typeof(Assists).GetAllFieldInfos()) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };
        }
    }
}

internal class EverestModuleSettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSettings));
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
            return ([module.SettingsType], queryArgs[1..]);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var mod in Everest.Modules) {
            if (mod.SettingsType != null &&
                (mod.SettingsType.GetAllFieldInfos().Any() ||
                 mod.SettingsType.GetAllPropertyInfos().Any(p => variant == TargetQuery.Variant.Get ? p.CanRead : p.CanWrite)))
            {
                yield return new CommandAutoCompleteEntry { Name = $"{mod.Metadata.Name}.", Extra = "Mod Setting", IsDone = false };
            }
        }
    }
}

internal class EntityQueryHandler : TargetQuery.Handler {
    internal record Data(List<Type> ComponentTypes, EntityID? EntityID);

    /// Matches an EntityID specification on the base type
    /// e.g. `BaseType[Room:ID]`
    private static readonly Regex EntityIDRegex = new(@"^(.+)(?:\[(.+):(\d+)\])(.*)$", RegexOptions.Compiled);

    /// Matches a component specification on the base type / members
    /// e.g. `BaseTypeOrMember:Component@Assembly`
    private static readonly Regex ComponentRegex = new(@"^([\w@]*):([\w@]+)$", RegexOptions.Compiled);

    private const string SpecialSeparator = "___";
    private const string EntityIDKey = "EntityIDFilter";
    private const string ComponentKey = "ComponentAccess";

    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(Entity));
    public override bool CanResolveMembers(Type type) => type.IsSameOrSubclassOf(typeof(Entity));

    public override (List<Type> Types, string[] MemberArgs, object? UserData)? ResolveBaseTypesWithUserData(string[] queryArgs) {
        // Both special cases use colons, so check for them to early-exit
        if (queryArgs.All(arg => !arg.Contains(':'))) {
            return null;
        }

        var newQueryArgs = new List<string>(capacity: queryArgs.Length);
        foreach (string arg in queryArgs) {
            if (EntityIDRegex.Match(arg) is { Success: true } entityMatch && int.TryParse(entityMatch.Groups[3].Value, out int id)) {
                newQueryArgs.Add(entityMatch.Groups[1].Value);
                newQueryArgs.Add($"{EntityIDKey}{SpecialSeparator}{entityMatch.Groups[2].Value}{SpecialSeparator}{id}");

                if (ComponentRegex.Match(entityMatch.Groups[4].Value) is { Success: true} componentMatch) {
                    newQueryArgs.Add($"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}");
                }
            } else if (ComponentRegex.Match(arg) is { Success: true} componentMatch) {
                newQueryArgs.Add(componentMatch.Groups[1].Value);
                newQueryArgs.Add($"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}");
            } else {
                newQueryArgs.Add(arg);
            }
        }

        var baseTypes = TargetQuery.ParseGenericBaseTypes(newQueryArgs.ToArray(), out string[] memberArgs);
        return (baseTypes, memberArgs, UserData: null);

        // // Split component access into own member
        // string[] splitQueryArgs = queryArgs
        //     .SelectMany<string, string>(arg => EntityIDRegex.Match(arg) is { Success: true } match && int.TryParse(match.Groups[3].Value, out int id)
        //         ? [match.Groups[1].Value, $"{EntityIDKey}{SpecialSeparator}{match.Groups[2].Value}{SpecialSeparator}{id}"]
        //         : [arg])
        //     .SelectMany<string, string>(arg => ComponentRegex.Match(arg) is { Success: true } match
        //         ? [match.Groups[1].Value, $"{ComponentKey}{SpecialSeparator}{match.Groups[2].Value}"]
        //         : [arg])
        //     .ToArray();
        //
        // // // Search for entity ID
        // // for (int i = 0; i < splitQueryArgs.Length; i++) {
        // //     if (EntityIDRegex.Match(splitQueryArgs[i]) is not { Success: true } match) {
        // //         continue;
        // //     }
        // //
        // //     var baseTypes = TargetQuery.ParseGenericBaseTypes([..splitQueryArgs[..i], match.Groups[1].Value], out string[] memberArgs);
        // //     var entityId = int.TryParse(match.Groups[3].Value, out int id) ? new EntityID(match.Groups[2].Value, id) : EntityID.None;
        // //     return (baseTypes, memberArgs, entityId);
        // // }
        //
        // if (splitQueryArgs.Length != queryArgs.Length) {
        //     var baseTypes = TargetQuery.ParseGenericBaseTypes(splitQueryArgs, out string[] memberArgs);
        //     return (baseTypes, memberArgs, UserData: null);
        // }
        //
        // // No component access
        // return null;
    }

    public override object[] ResolveInstancesWithUserData(Type type, object? userData) {
        var entityId = userData as EntityID?;

        IEnumerable<Entity> entityInstances;
        if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
            entityInstances = entities
                .Where(e => entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key);
        } else {
            entityInstances = Engine.Scene.Entities
                .Where(e => e.GetType().IsSameOrSubclassOf(type) && (entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key));
        }

        return entityInstances
            .Select(object (e) => e)
            .ToArray();

        // if (data == null || data.ComponentTypes.IsEmpty()) {
        //     return entityInstances
        //         .Select(object (e) => e)
        //         .ToArray();
        // } else {
        //     return entityInstances
        //         .SelectMany(e => e.Components.Where(c => data.ComponentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType))))
        //         .Select(object (c) => c)
        //         .ToArray();
        // }
    }

    public override Result<bool, TargetQuery.QueryError> ResolveMember(ref object?[] values, ref int memberIdx, string[] memberArgs) {
        string[] parts = memberArgs[memberIdx].Split(SpecialSeparator);
        if (parts.Length == 0) {
            return Result<bool, TargetQuery.QueryError>.Ok(false);
        }

        switch (parts[0]) {
            case EntityIDKey when parts.Length == 3:
                string key = $"{parts[1]}:{parts[2]}";
                for (int valueIdx = 0; valueIdx < values.Length; valueIdx++) {
                    if (values[valueIdx] is not Entity entity || entity.GetEntityData()?.ToEntityId().Key != key) {
                        values[valueIdx] = TargetQuery.InvalidValue;
                    }
                }
                return Result<bool, TargetQuery.QueryError>.Ok(true);

            case ComponentKey when parts.Length == 2:
                string[] componentQuery = [parts[1], ..memberArgs[(memberIdx + 1)..]];
                var componentTypes = TargetQuery.ParseGenericBaseTypes(componentQuery, out string[] componentMemberArgs);
                if (componentTypes.IsEmpty()) {
                    return Result<bool, TargetQuery.QueryError>.Fail(new TargetQuery.QueryError.NoBaseTypes(string.Join('.', componentQuery)));
                }

                memberIdx = memberArgs.Length - componentMemberArgs.Length - 1;

                List<object?>? additionalValues = null;
                for (int valueIdx = 0; valueIdx < values.Length; valueIdx++) {
                    if (values[valueIdx] is not Entity entity) {
                        values[valueIdx] = TargetQuery.InvalidValue;
                        continue;
                    }

                    int componentIdx = 0;
                    foreach (var component in entity.Components.Where(c => componentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType)))) {
                        if (componentIdx == 0) {
                            values[valueIdx] = component;
                        } else {
                            if (additionalValues == null) {
                                additionalValues = [component];
                            } else {
                                additionalValues.Add(component);
                            }
                        }

                        componentIdx++;
                    }
                    if (componentIdx == 0) {
                        // No component instances
                        values[valueIdx] = TargetQuery.InvalidValue;
                    }
                }
                if (additionalValues != null) {
                    int startIdx = values.Length;
                    Array.Resize(ref values, values.Length + additionalValues.Count);
                    additionalValues.CopyTo(values, startIdx);
                }

                return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        return Result<bool, TargetQuery.QueryError>.Ok(false);
    }
}
