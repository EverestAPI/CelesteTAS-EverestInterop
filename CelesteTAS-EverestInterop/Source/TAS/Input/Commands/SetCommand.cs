using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using System.Runtime.CompilerServices;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

#nullable enable

public static class SetCommand {
    internal class SetMeta : ITasCommandMeta {
        // Sorts types by namespace into Celeste -> Monocle -> other (alphabetically)
        // Inside the namespace it's sorted alphabetically
        internal class NamespaceComparer : IComparer<(string Name, Type Type)> {
            public int Compare((string Name, Type Type) x, (string Name, Type Type) y) {
                if (x.Type == null || y.Type == null || x.Type.Namespace == null || y.Type.Namespace == null) {
                    // Should never happen to use anyway
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
                var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetField(e)!);
                var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetField(e)!);

                foreach (var f in vanillaSettings) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Settings)", IsDone = true };;
                }
                foreach (var f in vanillaSaveData) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Save Data)", IsDone = true };;
                }
                foreach (var f in typeof(Assists).GetFields()) {
                    yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };;
                }

                // Mod settings
                foreach (var mod in Everest.Modules) {
                    if (mod.SettingsType != null && (mod.SettingsType.GetAllFieldInfos().Any() ||
                                                     mod.SettingsType.GetAllProperties().Any(p => p.GetSetMethod() != null)))
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
                            .All(p => !IsSettableType(p.PropertyType) || p.GetSetMethod() == null))
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
                if (ExtendedVariantsUtils.GetVariantsEnum() is { } variantsEnum) {
                    foreach (object variant in Enum.GetValues(variantsEnum)) {
                        string typeName = string.Empty;
                        try {
                            var variantType = ExtendedVariantsUtils.GetVariantType(new Lazy<object>(variant));
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
            } else if (targetArgs.Length >= 1 && InfoCustom.TryParseTypes(targetArgs[0], out var types, out _, out _)) {
                // Let's just assume the first type
                foreach (var entry in GetSetTypeAutoCompleteEntries(RecurseSetType(types[0], targetArgs), isRootType: targetArgs.Length == 1)) {
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
                    IsSettableType(property.PropertyType) && property.GetSetMethod() != null)
                {
                    yield return new CommandAutoCompleteEntry { Name = property.Name, Extra = property.PropertyType.CSharpName(), IsDone = IsFinalTarget(property.PropertyType), };;
                }
            }
            foreach (var property in type.GetFields(bindingFlags).OrderBy(p => p.Name)) {
                // Filter-out compiler generated properties
                if (property.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !property.Name.Contains('<') && !property.Name.Contains('>') &&
                    IsSettableType(property.FieldType))
                {
                    bool done = IsFinalTarget(property.FieldType);
                    yield return new CommandAutoCompleteEntry { Name = done ? property.Name : $"{property.Name}.", Extra = property.FieldType.CSharpName(), IsDone = done };;
                }
            }
        }

        private static IEnumerator<CommandAutoCompleteEntry> GetParameterAutoCompleteEntries(string[] targetArgs) {
            if (targetArgs.Length == 1) {
                // Vanilla setting / session / assist
                if (typeof(Settings).GetFieldInfo(targetArgs[0], BindingFlags.Instance | BindingFlags.Public) is { } fSettings) {
                    return GetParameterTypeAutoCompleteEntries(fSettings.FieldType);
                }
                if (typeof(SaveData).GetFieldInfo(targetArgs[0], BindingFlags.Instance | BindingFlags.Public) is { } fSaveData) {
                    return GetParameterTypeAutoCompleteEntries(fSaveData.FieldType);
                }
                if (typeof(Assists).GetFieldInfo(targetArgs[0], BindingFlags.Instance | BindingFlags.Public) is { } fAssists) {
                    return GetParameterTypeAutoCompleteEntries(fAssists.FieldType);
                }
            }
            if (targetArgs.Length == 1 && targetArgs[0] == "ExtendedVariantMode") {
                // Special case for setting extended variants
                var variant = ExtendedVariantsUtils.ParseVariant(targetArgs[1]);
                var variantType = ExtendedVariantsUtils.GetVariantType(new(variant));

                if (variantType != null) {
                    return GetParameterTypeAutoCompleteEntries(variantType);
                }
            }
            if (targetArgs.Length >= 1 && Everest.Modules.FirstOrDefault(m => m.Metadata.Name == targetArgs[0] && m.SettingsType != null) is { } mod) {
                return GetParameterTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, targetArgs));
            }
            if (targetArgs.Length >= 1 && InfoCustom.TryParseTypes(targetArgs[0], out var types, out _, out _)) {
                // Let's just assume the first type
                return GetParameterTypeAutoCompleteEntries(RecurseSetType(types[0], targetArgs));
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

        private static Type RecurseSetType(Type baseType, string[] targetArgs) {
            var type = baseType;
            for (int i = 1; i < targetArgs.Length; i++) {
                if (type.GetFieldInfo(targetArgs[i]) is { } field) {
                    type = field.FieldType;
                    continue;
                }
                if (type.GetPropertyInfo(targetArgs[i]) is { } property && property.GetSetMethod() != null) {
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

    private static bool consolePrintLog;
    private const string logPrefix = "Set Command Failed: ";

    private static void ReportError(string message) {
        if (consolePrintLog) {
            $"Set Command Failed: {message}".ConsoleLog(LogLevel.Error);
        } else {
            AbortTas($"Set Command Failed: {message}");
        }
    }

    [Monocle.Command("set", "'set Settings/Level/Session/Entity value' | Example: 'set DashMode Infinite', 'set Player.Speed 325 -52.5' (CelesteTAS)"), UsedImplicitly]
    private static void ConsoleSet(string? arg1, string? arg2, string? arg3, string? arg4, string? arg5, string? arg6, string? arg7, string? arg8, string? arg9) {
        // TODO: Support arbitrary amounts of arguments
        string?[] args = [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9];
        consolePrintLog = true;
        Set(args.TakeWhile(arg => arg != null).ToArray()!);
        consolePrintLog = false;
    }

    // Set, Setting, Value
    // Set, Mod.Setting, Value
    // Set, Entity.Field, Value
    // Set, Type.StaticMember, Value
    [TasCommand("Set", LegalInFullGame = false, MetaDataProvider = typeof(SetMeta))]
    private static void Set(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Set(commandLine.Arguments);
    }

    private static void Set(string[] args) {
        if (args.Length < 2) {
            ReportError("Target-template and value required");
            return;
        }

        string template = args[0];
        string[] templateArgs = template.Split('.');

        var baseTypes = InfoTemplate.ResolveBaseTypes(templateArgs, out var memberArgs, out var entityId);
        if (baseTypes.IsEmpty()) {
            ReportError($"Failed to find base type for template '{template}'");
            return;
        }
        if (memberArgs.IsEmpty()) {
            ReportError("No members specified");
            return;
        }

        foreach (var type in baseTypes) {
            (var targetType, bool success) = InfoTemplate.ResolveMemberType(type, memberArgs);
            if (!success) {
                ReportError($"Failed to find members '{string.Join('.', memberArgs)}' on type '{type}'");
                return;
            }

            (object?[] values, success, string errorMessage) = InfoTemplate.ResolveValues(args[1..], [targetType]);
            if (!success) {
                ReportError(errorMessage);
                return;
            }

            var instances = InfoTemplate.ResolveTypeInstances(type, entityId);
            success = InfoTemplate.SetMemberValues(type, instances, values[0], memberArgs);
            if (!success) {
                ReportError($"Failed to set members '{string.Join('.', memberArgs)}' on type '{type}' to '{values[0]}'");
                return;
            }
        }
    }

    private static void SetGameSetting(string[] args) {
        object settings = null;
        string settingName = args[0];
        string[] parameters = args.Skip(1).ToArray();

        FieldInfo field;
        if ((field = typeof(Settings).GetField(settingName)) != null) {
            settings = Settings.Instance;
        } else if ((field = typeof(SaveData).GetField(settingName)) != null) {
            settings = SaveData.Instance;
        } else if ((field = typeof(Assists).GetField(settingName)) != null) {
            settings = SaveData.Instance.Assists;
        }

        if (settings == null) {
            return;
        }

        object value = ConvertType(parameters, field.FieldType);

        if (!SettingsSpecialCases(settingName, value)) {
            field.SetValue(settings, value);

            if (settings is Assists assists) {
                SaveData.Instance.Assists = assists;
            }
        }

        if (settings is Assists variantAssists && !Equals(variantAssists, Assists.Default)) {
            SaveData.Instance.VariantMode = true;
            SaveData.Instance.AssistMode = false;
        }
    }

    private static bool TrySetModSetting(string moduleSetting, string[] values) {
        int index = moduleSetting.IndexOf(".", StringComparison.Ordinal);
        string moduleName = moduleSetting.Substring(0, index);
        string settingName = moduleSetting.Substring(index + 1);
        foreach (EverestModule module in Everest.Modules) {
            if (module.Metadata.Name == moduleName && module.SettingsType is { } settingsType) {
                bool success = TrySetMember(settingsType, module._Settings, settingName, values);

                // Allow setting extended variants
                if (!success && moduleName == "ExtendedVariantMode") {
                    if (!TrySetExtendedVariant(settingName, values)) {
                        Log($"Setting or extended variant {moduleName}.{settingName} not found");
                    }
                } else if (!success) {
                    Log($"{settingsType.FullName}.{settingName} member not found");
                }

                return true;
            }
        }

        return false;
    }

    private static bool FindObjectAndSetMember(Type type, string entityId, List<string> memberNames, string[] values, object structObj = null) {
        if (memberNames.IsEmpty() || values.IsEmpty() && structObj == null) {
            return false;
        }

        string lastMemberName = memberNames.Last();
        memberNames = memberNames.SkipLast(1).ToList();

        Type objType;
        object obj = null;
        if (memberNames.IsEmpty() &&
            (type.GetGetMethod(lastMemberName) is {IsStatic: true} || type.GetFieldInfo(lastMemberName) is {IsStatic: true})) {
            objType = type;
        } else if (memberNames.IsNotEmpty() &&
                   (type.GetGetMethod(memberNames.First()) is {IsStatic: true} ||
                    type.GetFieldInfo(memberNames.First()) is {IsStatic: true})) {
            obj = InfoCustom.GetMemberValue(type, null, memberNames, true);
            if (TryPrintErrorLog()) {
                return false;
            }

            objType = obj.GetType();
        } else {
            obj = FindSpecialObject(type, entityId);
            if (obj == null) {
                Log($"{type.FullName}{entityId.LogId()} object is not found");
                return false;
            } else {
                if (type.IsSameOrSubclassOf(typeof(Entity)) && obj is List<Entity> entities) {
                    if (entities.IsEmpty()) {
                        Log($"{type.FullName}{entityId.LogId()} entity is not found");
                        return false;
                    } else {
                        List<object> memberValues = new();
                        foreach (Entity entity in entities) {
                            object memberValue = InfoCustom.GetMemberValue(type, entity, memberNames, true);
                            if (TryPrintErrorLog()) {
                                return false;
                            }

                            if (memberValue != null) {
                                memberValues.Add(memberValue);
                            }
                        }

                        if (memberValues.IsEmpty()) {
                            return false;
                        }

                        obj = memberValues;
                        objType = memberValues.First().GetType();
                    }
                } else {
                    obj = InfoCustom.GetMemberValue(type, obj, memberNames, true);
                    if (TryPrintErrorLog()) {
                        return false;
                    }

                    objType = obj.GetType();
                }
            }
        }

        if (type.IsSameOrSubclassOf(typeof(Entity)) && obj is List<object> objects) {
            bool success = false;
            objects.ForEach(_ => success |= SetMember(_));
            return success;
        } else {
            return SetMember(obj);
        }

        bool SetMember(object @object) {
            if (!TrySetMember(objType, @object, lastMemberName, values, structObj)) {
                Log($"{objType.FullName}.{lastMemberName} member not found");
                return false;
            }

            // after modifying the struct
            // we also need to update the object own the struct
            if (memberNames.IsNotEmpty() && objType.IsStructType()) {
                string[] position = @object switch {
                    Vector2 vector2 => new[] {vector2.X.ToString(CultureInfo.InvariantCulture), vector2.Y.ToString(CultureInfo.InvariantCulture)},
                    Vector2Double vector2Double => new[] {
                        vector2Double.X.ToString(CultureInfo.InvariantCulture), vector2Double.Y.ToString(CultureInfo.InvariantCulture)
                    },
                    _ => new string[] { }
                };

                return FindObjectAndSetMember(type, entityId, memberNames, position, position.IsEmpty() ? @object : null);
            }

            return true;
        }

        bool TryPrintErrorLog() {
            if (obj == null) {
                Log($"{type.FullName}{entityId.LogId()} member value is null");
                return true;
            } else if (obj is string errorMsg && errorMsg.EndsWith(" not found")) {
                Log(errorMsg);
                return true;
            }

            return false;
        }
    }

    private static bool TrySetMember(Type objType, object obj, string lastMemberName, string[] values, object structObj = null) {
        if (objType.GetPropertyInfo(lastMemberName) is { } property && property.GetSetMethod(true) is { } setMethod) {
            if (obj is Actor actor && lastMemberName is "X" or "Y") {
                double.TryParse(values[0], out double value);
                Vector2 remainder = actor.movementCounter;
                if (lastMemberName == "X") {
                    actor.Position.X = (int) Math.Round(value);
                    remainder.X = (float) (value - actor.Position.X);
                } else {
                    actor.Position.Y = (int) Math.Round(value);
                    remainder.Y = (float) (value - actor.Position.Y);
                }

                actor.movementCounter = remainder;
            } else if (obj is Platform platform && lastMemberName is "X" or "Y") {
                double.TryParse(values[0], out double value);
                Vector2 remainder = platform.movementCounter;
                if (lastMemberName == "X") {
                    platform.Position.X = (int) Math.Round(value);
                    remainder.X = (float) (value - platform.Position.X);
                } else {
                    platform.Position.Y = (int) Math.Round(value);
                    remainder.Y = (float) (value - platform.Position.Y);
                }

                platform.movementCounter = remainder;
            } else if (property.PropertyType == typeof(ButtonBinding) && property.GetValue(obj) is ButtonBinding buttonBinding) {
                HashSet<Keys> keys = new();
                HashSet<MButtons> mButtons = new();
                IList mouseButtons = buttonBinding.Button.GetFieldValue<object>("Binding")?.GetFieldValue<IList>("Mouse");
                foreach (string str in values) {
                    // parse mouse first, so Mouse.Left is not parsed as Keys.Left
                    if (Enum.TryParse(str, true, out MButtons mButton)) {
                        if (mouseButtons == null && mButton is MButtons.X1 or MButtons.X2) {
                            AbortTas("X1 and X2 are not supported before Everest adding mouse support");
                            return false;
                        }

                        mButtons.Add(mButton);
                    } else if (Enum.TryParse(str, true, out Keys key)) {
                        keys.Add(key);
                    } else {
                        AbortTas($"{str} is not a valid key");
                        return false;
                    }
                }

                List<VirtualButton.Node> nodes = buttonBinding.Button.Nodes;

                if (keys.IsNotEmpty()) {
                    foreach (VirtualButton.Node node in nodes.ToList()) {
                        if (node is VirtualButton.KeyboardKey) {
                            nodes.Remove(node);
                        }
                    }

                    nodes.AddRange(keys.Select(key => new VirtualButton.KeyboardKey(key)));
                }

                if (mButtons.IsNotEmpty()) {
                    foreach (VirtualButton.Node node in nodes.ToList()) {
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
                        foreach (MButtons mButton in mButtons) {
                            mouseButtons.Add(mButton);
                        }
                    } else {
                        foreach (MButtons mButton in mButtons) {
                            if (mButton == MButtons.Left) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseLeftButton()));
                            } else if (mButton == MButtons.Right) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseRightButton()));
                            } else if (mButton == MButtons.Middle) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseMiddleButton()));
                            }
                        }
                    }
                }
            } else {
                object value = structObj ?? ConvertType(values, property.PropertyType);
                setMethod.Invoke(obj, new[] {value});
            }
        } else if (objType.GetFieldInfo(lastMemberName) is { } field) {
            if (obj is Actor actor && lastMemberName == "Position" && values.Length == 2) {
                double.TryParse(values[0], out double x);
                double.TryParse(values[1], out double y);
                Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                actor.Position = position;
                actor.movementCounter = remainder;
            } else if (obj is Platform platform && lastMemberName == "Position" && values.Length == 2) {
                double.TryParse(values[0], out double x);
                double.TryParse(values[1], out double y);
                Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                platform.Position = position;
                platform.movementCounter = remainder;
            } else {
                object value = structObj ?? ConvertType(values, field.FieldType);
                if (lastMemberName.Equals("Speed", StringComparison.OrdinalIgnoreCase) && value is Vector2 speed &&
                    Math.Abs(Engine.TimeRateB - 1f) > 1e-10) {
                    field.SetValue(obj, speed / Engine.TimeRateB);
                } else {
                    field.SetValue(obj, value);
                }
            }
        } else {
            return false;
        }

        return true;
    }

    private static bool TrySetExtendedVariant(string variantName, string[] values) {
        Lazy<object> variant = new(ExtendedVariantsUtils.ParseVariant(variantName));
        Type type = ExtendedVariantsUtils.GetVariantType(variant);
        if (type is null) return false;

        object value = ConvertType(values, type);
        ExtendedVariantsUtils.SetVariantValue(variant, value);

        return true;
    }

    public static object FindSpecialObject(Type type, string entityId) {
        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            return InfoCustom.FindEntities(type, entityId);
        } else if (type == typeof(Level)) {
            return Engine.Scene.GetLevel();
        } else if (type == typeof(Session)) {
            return Engine.Scene.GetSession();
        } else {
            return null;
        }
    }

    private static string LogId(this string entityId) {
        return entityId.IsNullOrEmpty() ? "" : $"[{entityId}]";
    }

    private static void Log(string text) {
        // if (suspendLog) {
        //     errorLogs.Add(text);
        //     return;
        // }

        if (!consolePrintLog) {
            text = $"{logPrefix}{text}";
        }

        text.Log(consolePrintLog, LogLevel.Warn);
    }

    public static object Convert(object value, Type type) {
        try {
            if (value is null or string and ("" or "null")) {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            } else if (type == typeof(string) && value is "\"\"") {
                return string.Empty;
            } else {
                return type.IsEnum ? Enum.Parse(type, (string) value, true) : System.Convert.ChangeType(value, type);
            }
        } catch {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private static object ConvertType(string[] values, Type type) {
        Type nullableType = type;
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string)) {
            return string.Join(" ", values);
        } if (values.Length == 2 && type == typeof(Vector2)) {
            float.TryParse(values[0], out float x);
            float.TryParse(values[1], out float y);
            return new Vector2(x, y);
        } else if (values.Length == 1) {
            if (type == typeof(Random)) {
                if (int.TryParse(values[0], out int seed)) {
                    return new Random(seed);
                } else {
                    return new Random(values[0].GetHashCode());
                }
            } else {
                return Convert(values[0], nullableType);
            }
        } else if (values.Length >= 2) {
            object instance = Activator.CreateInstance(type);
            MemberInfo[] members = type.GetMembers().Where(info => (info.MemberType & (MemberTypes.Field | MemberTypes.Property)) != 0).ToArray();
            for (int i = 0; i < members.Length && i < values.Length; i++) {
                string memberName = members[i].Name;
                if (type.GetField(memberName) is { } fieldInfo) {
                    fieldInfo.SetValue(instance, Convert(values[i], fieldInfo.FieldType));
                } else if (type.GetProperty(memberName) is { } propertyInfo) {
                    propertyInfo.SetValue(instance, Convert(values[i], propertyInfo.PropertyType));
                }
            }

            return instance;
        }

        return default;
    }

    private static bool SettingsSpecialCases(string settingName, object value) {
        Player player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
        SaveData saveData = SaveData.Instance;
        Settings settings = Settings.Instance;
        switch (settingName) {
            // Assists
            case "GameSpeed":
                saveData.Assists.GameSpeed = (int) value;
                Engine.TimeRateB = saveData.Assists.GameSpeed / 10f;
                break;
            case "MirrorMode":
                saveData.Assists.MirrorMode = (bool) value;
                Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = (bool) value;
                if (typeof(Celeste.Input).GetFieldValue<VirtualJoystick>("Feather") is { } featherJoystick) {
                    featherJoystick.InvertedX = (bool) value;
                }

                break;
            case "PlayAsBadeline":
                saveData.Assists.PlayAsBadeline = (bool) value;
                if (player != null) {
                    PlayerSpriteMode mode = saveData.Assists.PlayAsBadeline
                        ? PlayerSpriteMode.MadelineAsBadeline
                        : player.DefaultSpriteMode;
                    if (player.Active) {
                        player.ResetSpriteNextFrame(mode);
                    } else {
                        player.ResetSprite(mode);
                    }
                }

                break;
            case "DashMode":
                saveData.Assists.DashMode = (Assists.DashModes) value;
                if (player != null) {
                    player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                }

                break;

            // SaveData
            case "VariantMode":
                saveData.VariantMode = (bool) value;
                saveData.AssistMode = false;
                if (!saveData.VariantMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }

                break;
            case "AssistMode":
                saveData.AssistMode = (bool) value;
                saveData.VariantMode = false;
                if (!saveData.AssistMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }

                break;

            // Settings
            case "Rumble":
                settings.Rumble = (RumbleAmount) value;
                Celeste.Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                break;
            case "GrabMode":
                settings.SetFieldValue("GrabMode", value);
                typeof(Celeste.Celeste).InvokeMethod("ResetGrab");
                break;
            // case "Fullscreen":
            // game get stuck when toggle fullscreen
            // typeof(MenuOptions).InvokeMethod("SetFullscreen", value);
            // break;
            case "WindowScale":
                typeof(MenuOptions).InvokeMethod("SetWindow", value);
                break;
            case "VSync":
                typeof(MenuOptions).InvokeMethod("SetVSync", value);
                break;
            case "MusicVolume":
                typeof(MenuOptions).InvokeMethod("SetMusic", value);
                break;
            case "SFXVolume":
                typeof(MenuOptions).InvokeMethod("SetSfx", value);
                break;
            case "Language":
                string language = value.ToString();
                if (settings.Language != language && Dialog.Languages.ContainsKey(language)) {
                    if (settings.Language != "english") {
                        Fonts.Unload(Dialog.Languages[Settings.Instance.Language].FontFace);
                    }
                    settings.Language = language;
                    settings.ApplyLanguage();
                }
                break;
            default:
                return false;
        }

        return true;
    }

    public static void ResetVariants(Assists assists) {
        SaveData.Instance.Assists = assists;
        SettingsSpecialCases("DashMode", assists.DashMode);
        SettingsSpecialCases("GameSpeed", assists.GameSpeed);
        SettingsSpecialCases("MirrorMode", assists.MirrorMode);
        SettingsSpecialCases("PlayAsBadeline", assists.PlayAsBadeline);
    }
}
