using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TAS.Entities;
using TAS.EverestInterop;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.Input.Commands;

// Sorts types by namespace into Celeste -> Monocle -> other (alphabetically)
// Inside the namespace it's sorted alphabetically
internal class NamespaceComparer : IComparer<(string Name, Type Type)> {
    public int Compare((string Name, Type Type) x, (string Name, Type Type) y) {
        if (x.Type.Namespace == null || y.Type.Namespace == null) {
            return 0;
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

public static class SetCommand {
    internal class SetMeta : ITasCommandMeta {
        internal static readonly string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod", "Snowberry"];

        public string Insert => $"Set{CommandInfo.Separator}[0;(Mod).Setting]{CommandInfo.Separator}[1;Value]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            int hash = GetTargetArgs(args)
                .Aggregate(17, (current, arg) => 31 * current + arg.GetStableHashCode());
            // The other argument don't influence each other, so just the length matters
            return 31 * hash + 17 * args.Length;
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            var targetArgs = GetTargetArgs(args).ToArray();

            // Parameter
            if (args.Length > 1) {
                using var enumerator = GetParameterAutoCompleteEntries(targetArgs);
                while (enumerator.MoveNext()) {
                    yield return enumerator.Current;
                }
                yield break;
            }

            if (targetArgs.Length == 0) {
                // Vanilla settings. Manually selected to filter out useless entries
                var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetFieldInfo(e)!);
                var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetFieldInfo(e)!);

                foreach (var f in vanillaSettings) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Settings)", IsDone = true };
                }
                foreach (var f in vanillaSaveData) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Save Data)", IsDone = true };
                }
                foreach (var f in typeof(Assists).GetFields()) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };
                }

                // Mod settings
                foreach (var mod in Everest.Modules) {
                    if (mod.SettingsType != null && (mod.SettingsType.GetAllFieldInfos().Any() ||
                                                     mod.SettingsType.GetAllPropertyInfos().Any(p => p.SetMethod != null)))
                    {
                        yield return new CommandAutoCompleteEntry { Name = $"{mod.Metadata.Name}.", Extra = "Mod Setting", IsDone = false };
                    }
                }

                var allTypes = ModUtils.GetTypes();
                foreach ((string typeName, var type) in allTypes
                             .Select(type => (type.CSharpName(), type))
                             .Order(new NamespaceComparer()))
                {
                    if (
                        // Filter-out types which probably aren't useful
                        !type.IsClass || !type.IsPublic || type.FullName == null || type.Namespace == null || ignoredNamespaces.Any(ns => type.Namespace.StartsWith(ns)) ||

                        // Filter-out compiler generated types
                        !type.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() || type.FullName.Contains('<') || type.FullName.Contains('>') ||

                        // Require either an entity, level, session
                        !type.IsSameOrSubclassOf(typeof(Entity)) && !type.IsSameOrSubclassOf(typeof(Level)) && !type.IsSameOrSubclassOf(typeof(Session)) &&
                        // Or type with static (settable) variables
                        type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                            .All(f => f.IsInitOnly || !IsSettableType(f.FieldType)) &&
                        type.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                            .All(p => !IsSettableType(p.PropertyType) || p.SetMethod == null))
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
            } else if (targetArgs.Length == 1 && targetArgs[0] == "ExtendedVariantMode") {
                // Special case for setting extended variants
                if (ExtendedVariantsInterop.GetVariantsEnum() is { } variantsEnum) {
                    foreach (object variant in Enum.GetValues(variantsEnum)) {
                        string typeName = string.Empty;
                        try {
                            var variantType = ExtendedVariantsInterop.GetVariantType(new Lazy<object?>(variant));
                            if (variantType != null) {
                                typeName = variantType.CSharpName();
                            }
                        } catch {
                            // ignore
                        }

                        yield return new CommandAutoCompleteEntry { Name = variant.ToString()!, Prefix = "ExtendedVariantMode.", Extra = typeName, IsDone = true, HasNext = true };
                    }
                }
            } else if (targetArgs.Length >= 1 && Everest.Modules.FirstOrDefault(m => m.Metadata.Name == targetArgs[0] && m.SettingsType != null) is { } mod) {
                foreach (var entry in GetSetTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, args), isRootType: targetArgs.Length == 1)) {
                    yield return entry with { Name = entry.Name + (entry.IsDone ? "" : "."), Prefix = string.Join('.', targetArgs) + ".", HasNext = true };
                }
            } else if (targetArgs.Length >= 1 && TargetQuery.ResolveBaseTypes(targetArgs, out string[] memberArgs, out _, out _) is { } types && types.IsNotEmpty()) {
                // Assume the first type
                foreach (var entry in GetSetTypeAutoCompleteEntries(RecurseSetType(types[0], memberArgs), isRootType: targetArgs.Length == 1)) {
                    yield return entry with { Name = entry.Name + (entry.IsDone ? "" : "."), Prefix = string.Join('.', targetArgs) + ".", HasNext = true };
                }
            }
        }

        private static IEnumerable<CommandAutoCompleteEntry> GetSetTypeAutoCompleteEntries(Type type, bool isRootType) {
            bool staticMembers = isRootType && !(type.IsSameOrSubclassOf(typeof(Entity)) || type.IsSameOrSubclassOf(typeof(Level)) || type.IsSameOrSubclassOf(typeof(Session)) || type.IsSameOrSubclassOf(typeof(EverestModuleSettings)));
            var bindingFlags = staticMembers
                ? BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                : BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            foreach (var property in type.GetProperties(bindingFlags).OrderBy(p => p.Name)) {
                // Filter-out compiler generated properties
                if (property.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !property.Name.Contains('<') && !property.Name.Contains('>') &&
                    IsSettableType(property.PropertyType) && property.SetMethod != null)
                {
                    yield return new CommandAutoCompleteEntry { Name = property.Name, Extra = property.PropertyType.CSharpName(), IsDone = IsFinalTarget(property.PropertyType), };
                }
            }
            foreach (var property in type.GetFields(bindingFlags).OrderBy(p => p.Name)) {
                // Filter-out compiler generated properties
                if (property.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !property.Name.Contains('<') && !property.Name.Contains('>') &&
                    IsSettableType(property.FieldType))
                {
                    bool done = IsFinalTarget(property.FieldType);
                    yield return new CommandAutoCompleteEntry { Name = done ? property.Name : $"{property.Name}.", Extra = property.FieldType.CSharpName(), IsDone = done };
                }
            }
        }

        [MustDisposeResource]
        private static IEnumerator<CommandAutoCompleteEntry> GetParameterAutoCompleteEntries(string[] targetArgs) {
            if (targetArgs.Length == 1) {
                // Vanilla setting / session / assist
                if (typeof(Settings).GetFieldInfo(targetArgs[0], logFailure: false) is { } fSettings) {
                    return GetParameterTypeAutoCompleteEntries(fSettings.FieldType);
                }
                if (typeof(SaveData).GetFieldInfo(targetArgs[0], logFailure: false) is { } fSaveData) {
                    return GetParameterTypeAutoCompleteEntries(fSaveData.FieldType);
                }
                if (typeof(Assists).GetFieldInfo(targetArgs[0], logFailure: false) is { } fAssists) {
                    return GetParameterTypeAutoCompleteEntries(fAssists.FieldType);
                }
            }
            if (targetArgs.Length == 2 && targetArgs[0] == "ExtendedVariantMode") {
                // Special case for setting extended variants
                var variant = ExtendedVariantsInterop.ParseVariant(targetArgs[1]);
                var variantType = ExtendedVariantsInterop.GetVariantType(new(variant));

                if (variantType != null) {
                    return GetParameterTypeAutoCompleteEntries(variantType);
                }
            }
            if (targetArgs.Length >= 1 && Everest.Modules.FirstOrDefault(m => m.Metadata.Name == targetArgs[0] && m.SettingsType != null) is { } mod) {
                return GetParameterTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, targetArgs[1..]));
            }
            if (targetArgs.Length >= 1 && TargetQuery.ResolveBaseTypes(targetArgs, out string[] memberArgs, out _, out _) is { } types && types.IsNotEmpty()) {
                // Assume the first type
                return GetParameterTypeAutoCompleteEntries(RecurseSetType(types[0], memberArgs));
            }

            return Enumerable.Empty<CommandAutoCompleteEntry>().GetEnumerator();
        }

        internal static IEnumerator<CommandAutoCompleteEntry> GetParameterTypeAutoCompleteEntries(Type type, bool hasNextArgument = false) {
            if (type == typeof(bool)) {
                yield return new CommandAutoCompleteEntry { Name = "true", Extra = type.CSharpName(), IsDone = true, HasNext = hasNextArgument };
                yield return new CommandAutoCompleteEntry { Name = "false", Extra = type.CSharpName(), IsDone = true, HasNext = hasNextArgument };
            } else if (type == typeof(ButtonBinding)) {
                foreach (var button in Enum.GetValues<MButtons>()) {
                    yield return new CommandAutoCompleteEntry { Name = button.ToString(), Extra = "Mouse", IsDone = true, HasNext = hasNextArgument };
                }
                foreach (var key in Enum.GetValues<Keys>()) {
                    if (key is Keys.Left or Keys.Right) {
                        // These keys can't be used, since the mouse buttons already use that name
                        continue;
                    }
                    yield return new CommandAutoCompleteEntry { Name = key.ToString(), Extra = "Key", IsDone = true, HasNext = hasNextArgument };
                }
            } else if (type.IsEnum) {
                foreach (object value in Enum.GetValues(type)) {
                    yield return new CommandAutoCompleteEntry { Name = value.ToString()!, Extra = type.CSharpName(), IsDone = true, HasNext = hasNextArgument };
                }
            }
        }

        private static Type RecurseSetType(Type baseType, string[] memberArgs) {
            var type = baseType;
            foreach (string member in memberArgs) {
                if (type.GetFieldInfo(member, logFailure: false) is { } field) {
                    type = field.FieldType;
                    continue;
                }
                if (type.GetPropertyInfo(member, logFailure: false) is { } property && property.SetMethod != null) {
                    type = property.PropertyType;
                    continue;
                }
                break; // Invalid type
            }
            return type;
        }

        internal static bool IsSettableType(Type type) => !type.IsSameOrSubclassOf(typeof(Delegate));
        private static bool IsFinalTarget(Type type) => type == typeof(string) || type == typeof(Vector2) || type == typeof(Random) || type == typeof(ButtonBinding) || type.IsEnum || type.IsPrimitive;

        internal static IEnumerable<string> GetTargetArgs(string[] args) {
            if (args.Length == 0) {
                return [];
            }

            return args[0]
                .Split('.')
                // Only skip last part if we're currently editing that
                .SkipLast(args.Length == 1 ? 1 : 0);
        }
    }

    private static (string Name, int Line)? activeFile;

    private static void ReportError(string message) {
        if (activeFile == null) {
            $"Set Command Failed: {message}".ConsoleLog(LogLevel.Error);
        } else {
            Toast.ShowAndLog($"""
                              Set '{activeFile.Value.Name}' line {activeFile.Value.Line} failed:
                              {message}
                              """);
        }
    }

    [Monocle.Command("set", "'set Settings/Level/Session/Entity value' | Example: 'set DashMode Infinite', 'set Player.Speed 325 -52.5' (CelesteTAS)"), UsedImplicitly]
    private static void ConsoleSet(string? arg1, string? arg2, string? arg3, string? arg4, string? arg5, string? arg6, string? arg7, string? arg8, string? arg9) {
        // TODO: Support arbitrary amounts of arguments
        string?[] args = [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9];
        Set(args.TakeWhile(arg => arg != null).ToArray()!);
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
        if (args.Length < 2) {
            ReportError("Target-query and value required");
            return;
        }

        string query = args[0];
        string[] queryArgs = query.Split('.');

        var baseTypes = TargetQuery.ResolveBaseTypes(queryArgs, out string[]? memberArgs, out var componentTypes, out var entityId);
        if (baseTypes.IsEmpty()) {
            ReportError($"Failed to find base type for query '{query}'");
            return;
        }
        if (memberArgs.IsEmpty()) {
            ReportError("No members specified");
            return;
        }

        // Handle special cases
        if (baseTypes.Count == 1 && (baseTypes[0] == typeof(Settings) || baseTypes[0] == typeof(SaveData) || baseTypes[0] == typeof(Assists))) {
            SetGameSetting(memberArgs[0], args[1..]);
            return;
        }
        if (baseTypes.Count == 1 &&
            baseTypes[0].IsSameOrSubclassOf(typeof(EverestModuleSettings)) &&
            Everest.Modules.FirstOrDefault(mod => mod.SettingsType == baseTypes[0]) is { } module &&
            module.Metadata.Name == "ExtendedVariantMode")
        {
            SetExtendedVariant(memberArgs[0], args[1..]);
            return;
        }

        foreach (var type in baseTypes) {
            if (componentTypes.IsNotEmpty()) {
                foreach (var componentType in componentTypes) {
                    var typeResult = TargetQuery.ResolveMemberType(componentType, memberArgs);
                    if (typeResult.Failure) {
                        ReportError(typeResult);
                        return;
                    }

                    var valuesResult = TargetQuery.ResolveValues(args[1..], [typeResult]);
                    if (valuesResult.Failure) {
                        ReportError(valuesResult);
                        return;
                    }

                    var instances = TargetQuery.ResolveTypeInstances(type, [componentType], entityId);
                    var setResult = TargetQuery.SetMemberValues(componentType, instances, valuesResult.Value[0], memberArgs);
                    if (setResult.Failure) {
                        ReportError($"Failed to set members '{string.Join('.', memberArgs)}' of type '{typeResult.Value}' on type '{componentType}' to '{valuesResult.Value[0]}':\n{setResult.Error}");
                        return;
                    }
                }
            } else {
                var targetResult = TargetQuery.ResolveMemberType(type, memberArgs);
                if (targetResult.Failure) {
                    ReportError(targetResult);
                    return;
                }

                var valuesResult = TargetQuery.ResolveValues(args[1..], [targetResult]);
                if (valuesResult.Failure) {
                    ReportError(valuesResult);
                    return;
                }

                var instances = TargetQuery.ResolveTypeInstances(type, componentTypes, entityId);
                var setResult = TargetQuery.SetMemberValues(type, instances, valuesResult.Value[0], memberArgs);
                if (setResult.Failure) {
                    ReportError($"Failed to set members '{string.Join('.', memberArgs)}' of type '{targetResult.Value}' on type '{type}' to '{valuesResult.Value[0]}':\n{setResult.Error}");
                    return;
                }
            }
        }
    }

    private static void SetGameSetting(string settingName, string[] valueArgs) {
        object? settings = null;

        FieldInfo? field;
        if ((field = typeof(Settings).GetFieldInfo(settingName, logFailure: false)) != null) {
            settings = Settings.Instance;
        } else if ((field = typeof(SaveData).GetFieldInfo(settingName, logFailure: false)) != null) {
            settings = SaveData.Instance;
        } else if ((field = typeof(Assists).GetFieldInfo(settingName, logFailure: false)) != null) {
            settings = SaveData.Instance.Assists;
        }

        if (settings == null || field == null) {
            return;
        }

        var valuesResult = TargetQuery.ResolveValues(valueArgs, [field.FieldType]);
        if (valuesResult.Failure) {
            ReportError(valuesResult);
            return;
        }

        if (!HandleSpecialCases(settingName, valuesResult.Value[0])) {
            field.SetValue(settings, valuesResult.Value[0]);

            // Assists is a struct, so it needs to be re-assign
            if (settings is Assists assists) {
                SaveData.Instance.Assists = assists;
            }
        }

        if (settings is Assists variantAssists && !Equals(variantAssists, Assists.Default)) {
            SaveData.Instance.VariantMode = true;
            SaveData.Instance.AssistMode = false;
        }
    }
    private static void SetExtendedVariant(string variantName, string[] valueArgs) {
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));
        var variantType = ExtendedVariantsInterop.GetVariantType(variant);
        if (variantType is null) {
            ReportError($"Failed to resolve type for extended variant '{variantName}'");
            return;
        }

        var valuesResult = TargetQuery.ResolveValues(valueArgs, [variantType]);
        if (valuesResult.Failure) {
            ReportError(valuesResult);
            return;
        }

        ExtendedVariantsInterop.SetVariantValue(variant, valuesResult.Value[0]);
    }

    /// Applies the setting, while handing special cases
    private static bool HandleSpecialCases(string settingName, object? value) {
        var player = Engine.Scene.Tracker.GetEntity<Player>();
        var saveData = SaveData.Instance;
        var settings = Settings.Instance;

        switch (settingName) {
            // Assists
            case nameof(Assists.Invincible) when Manager.Running && TasSettings.BetterInvincible:
                BetterInvincible.Invincible = (bool) value!;
                break;
            case nameof(Assists.GameSpeed):
                saveData.Assists.GameSpeed = (int) value!;
                Engine.TimeRateB = saveData.Assists.GameSpeed / 10f;
                break;
            case nameof(Assists.MirrorMode):
                saveData.Assists.MirrorMode = (bool) value!;
                Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = saveData.Assists.MirrorMode;
                Celeste.Input.Feather.InvertedX = saveData.Assists.MirrorMode;
                break;
            case nameof(Assists.PlayAsBadeline):
                saveData.Assists.PlayAsBadeline = (bool) value!;
                if (player != null) {
                    var mode = saveData.Assists.PlayAsBadeline
                        ? PlayerSpriteMode.MadelineAsBadeline
                        : player.DefaultSpriteMode;

                    if (player.StateMachine.State == Player.StIntroWakeUp) {
                        // player.Sprite is captured in IntroWakeUpCoroutine(),
                        // so resetting the sprite would cause the player to be stuck in StIntroWakeUp
                        break;
                    }

                    if (player.Active) {
                        player.ResetSpriteNextFrame(mode);
                    } else {
                        player.ResetSprite(mode);
                    }
                }
                break;
            case nameof(Assists.DashMode):
                saveData.Assists.DashMode = (Assists.DashModes) value!;
                if (player != null) {
                    player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                }
                break;

            // SaveData
            case nameof(SaveData.VariantMode):
                saveData.VariantMode = (bool) value!;
                saveData.AssistMode = false;
                if (!saveData.VariantMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }
                break;
            case nameof(SaveData.AssistMode):
                saveData.AssistMode = (bool) value!;
                saveData.VariantMode = false;
                if (!saveData.AssistMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }
                break;

            // Settings
            case nameof(Settings.Rumble):
                settings.Rumble = (RumbleAmount) value!;
                Celeste.Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                break;
            case nameof(Settings.GrabMode):
                settings.GrabMode = (GrabModes) value!;
                Celeste.Input.ResetGrab();
                break;
            case nameof(Settings.Fullscreen):
            case nameof(Settings.WindowScale):
            case nameof(Settings.VSync):
            case nameof(Settings.MusicVolume):
            case nameof(Settings.SFXVolume):
            case nameof(Settings.Language):
                // Intentional no-op. A TAS should not modify these user preferences
                break;

            default:
                return false;
        }

        return true;
    }

    public static void ResetVariants(Assists assists) {
        SaveData.Instance.Assists = assists;
        HandleSpecialCases(nameof(Assists.DashMode), assists.DashMode);
        HandleSpecialCases(nameof(Assists.GameSpeed), assists.GameSpeed);
        HandleSpecialCases(nameof(Assists.MirrorMode), assists.MirrorMode);
        HandleSpecialCases(nameof(Assists.PlayAsBadeline), assists.PlayAsBadeline);
    }
}
