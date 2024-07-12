using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste; 
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;
using Type = System.Type;

namespace TAS.Communication;

public static class GameData {
    private static Dictionary<string, ModUpdateInfo> modUpdateInfos;
    
    [Load]
    private static void Load() {
        typeof(ModUpdaterHelper).GetMethod("DownloadModUpdateList")?.OnHook(ModUpdaterHelperOnDownloadModUpdateList);
        modUpdateInfos = Engine.Instance.GetDynamicDataInstance().Get<Dictionary<string, ModUpdateInfo>>(nameof(modUpdateInfos));
    }
    
    [Unload]
    private static void Unload() {
        Engine.Instance.GetDynamicDataInstance().Set(nameof(modUpdateInfos), modUpdateInfos);
    }
    
    private delegate Dictionary<string, ModUpdateInfo> orig_ModUpdaterHelper_DownloadModUpdateList();
    private static Dictionary<string, ModUpdateInfo> ModUpdaterHelperOnDownloadModUpdateList(orig_ModUpdaterHelper_DownloadModUpdateList orig) {
        return modUpdateInfos = orig();
    }
    
    public static string GetConsoleCommand(bool simple) {
        return ConsoleCommand.CreateConsoleCommand(simple);
    }
    
    public static string GetModInfo() {
        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        string MetaToString(EverestModuleMetadata metadata, int indentation = 0, bool comment = true) {
            return (comment ? "# " : string.Empty) + string.Empty.PadLeft(indentation) + $"{metadata.Name} {metadata.VersionString}\n";
        }

        HashSet<string> ignoreMetaNames = [
            "DialogCutscene",
            "UpdateChecker",
            "InfiniteSaves",
            "DebugRebind",
            "RebindPeriod"
        ];

        List<EverestModuleMetadata> metas = Everest.Modules
            .Where(module => !ignoreMetaNames.Contains(module.Metadata.Name) && module.Metadata.VersionString != "0.0.0-dummy")
            .Select(module => module.Metadata).ToList();
        metas.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        AreaData areaData = AreaData.Get(level);
        string moduleName = string.Empty;
        EverestModuleMetadata mapMeta = null;
        if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
            moduleName = mapModAsset.Source.Name;
            mapMeta = metas.FirstOrDefault(meta => meta.Name == moduleName);
        }

        string modInfo = "";

        EverestModuleMetadata celesteMeta = metas.First(metadata => metadata.Name == "Celeste");
        EverestModuleMetadata everestMeta = metas.First(metadata => metadata.Name == "Everest");
        EverestModuleMetadata tasMeta = metas.First(metadata => metadata.Name == "CelesteTAS");
        modInfo += MetaToString(celesteMeta);
        modInfo += MetaToString(everestMeta);
        modInfo += MetaToString(tasMeta);
        metas.Remove(celesteMeta);
        metas.Remove(everestMeta);
        metas.Remove(tasMeta);

        EverestModuleMetadata speedrunToolMeta = metas.FirstOrDefault(metadata => metadata.Name == "SpeedrunTool");
        if (speedrunToolMeta != null) {
            modInfo += MetaToString(speedrunToolMeta);
            metas.Remove(speedrunToolMeta);
        }

        ignoreMetaNames.UnionWith(new HashSet<string> {
            "Celeste",
            "Everest",
            "CelesteTAS",
            "SpeedrunTool"
        });

        modInfo += "\n# Map:\n";
        if (mapMeta != null) {
            modInfo += MetaToString(mapMeta, 2);
            if (modUpdateInfos?.TryGetValue(mapMeta.Name, out var modUpdateInfo) == true && modUpdateInfo.GameBananaId > 0) {
                modInfo += $"#   https://gamebanana.com/mods/{modUpdateInfo.GameBananaId}\n";
            }
        }

        string mode = level.Session.Area.Mode == AreaMode.Normal ? "ASide" : level.Session.Area.Mode.ToString();
        modInfo += $"#   {areaData.SID} {mode}\n";

        if (!string.IsNullOrEmpty(moduleName) && mapMeta != null) {
            List<EverestModuleMetadata> dependencies = mapMeta.Dependencies
                .Where(metadata => !ignoreMetaNames.Contains(metadata.Name) && metadata.VersionString != "0.0.0-dummy")
                .ToList();
            dependencies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (dependencies.Count > 0) {
                modInfo += "\n# Dependencies:\n";
                modInfo += string.Join(string.Empty,
                    dependencies.Select(meta => metas.First(metadata => metadata.Name == meta.Name)).Select(meta => MetaToString(meta, 2)));
            }

            modInfo += "\n# Other Installed Mods:\n";
            modInfo += string.Join(string.Empty,
                metas.Where(meta => meta.Name != moduleName && dependencies.All(metadata => metadata.Name != meta.Name))
                    .Select(meta => MetaToString(meta, 2)));
        } else if (metas.IsNotEmpty()) {
            modInfo += "\n# Other Installed Mods:\n";
            modInfo += string.Join(string.Empty, metas.Select(meta => MetaToString(meta, 2)));
        }

        return modInfo;
    }
    
    public static string GetSettingValue(string settingName) {
        if (typeof(CelesteTasSettings).GetProperty(settingName) is { } property) {
            return property.GetValue(TasSettings).ToString();
        } else {
            return string.Empty;
        }
    }
    
    public static string GetModUrl() {
        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        AreaData areaData = AreaData.Get(level);
        string moduleName = string.Empty;
        EverestModule mapModule = null;
        if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
            moduleName = mapModAsset.Source.Name;
            mapModule = Everest.Modules.FirstOrDefault(module => module.Metadata?.Name == moduleName);
        }

        if (mapModule == null) {
            return string.Empty;
        }

        if (modUpdateInfos?.TryGetValue(moduleName, out var modUpdateInfo) == true && modUpdateInfo.GameBananaId > 0) {
            return $"# {moduleName}\n# https://gamebanana.com/mods/{modUpdateInfo.GameBananaId}\n\n";
        }

        return string.Empty;
    }
    
    // Sorts types by namespace into Celeste -> Monocle -> other (alphabetically)
    // Inside the namespace it's sorted alphabetically
    private class NamespaceComparer : IComparer<(string Name, Type Type)> {
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
    
    // Each entry is separated by a ';' and has the format: <'!' = final, '.' = non-final><displayed member name>#<displayed member extra#<type of member>
    // Special type names are inside angle-brackets
    private struct AutoCompleteEntry {
        public bool Final;
        public string MemberName;
        public string MemberExtra;
    }
    
    private static readonly string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod", "Snowberry"];
    public static string GetSetCommandAutoCompleteEntries(string argsText) {
        var entries = new List<AutoCompleteEntry>();
        
        var args = argsText.Split('.');
        if (args.Length == 1) {
            // Vanilla settings. Manually selected to filter out useless entries
            var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetField(e)!);
            var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetField(e)!);
            entries.AddRange(vanillaSettings.Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberExtra = $"{CSharpTypeName(f.FieldType)} (Settings)" }));
            entries.AddRange(vanillaSaveData.Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberExtra = $"{CSharpTypeName(f.FieldType)} (Save Data)" }));
            entries.AddRange(typeof(Assists).GetFields().Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberExtra = $"{CSharpTypeName(f.FieldType)} (Assists)" }));
            
            // Mod settings
            entries.AddRange(Everest.Modules
                .Where(mod => mod.SettingsType != null)
                // Require at least 1 settable field / property
                .Where(mod => mod.SettingsType.GetAllFieldInfos().Any() || mod.SettingsType.GetAllProperties().Any(p => p.GetSetMethod() != null))
                .Select(mod => new AutoCompleteEntry { Final = false, MemberName = mod.Metadata.Name, MemberExtra = "Mod Setting" }));
            
            var allTypes = ModUtils.GetTypes();
            var filteredTypes = allTypes
                // Filter-out types which probably aren't useful
                .Where(t => t.IsClass && t.IsPublic && t.FullName != null && t.Namespace != null && ignoredNamespaces.All(ns => !t.Namespace.StartsWith(ns)))
                // Filter-out compiler generated types
                .Where(t => t.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !t.FullName.Contains('<') && !t.FullName.Contains('>'))
                // Require either an entity, level, session or type with static variables
                .Where(t => t.IsSameOrSubclassOf(typeof(Entity)) || t.IsSameOrSubclassOf(typeof(Level)) || t.IsSameOrSubclassOf(typeof(Session)) ||
                                 t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Any(f => !f.IsInitOnly && IsSettableType(f.FieldType)) ||
                                 t.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Any(p => IsSettableType(p.PropertyType) && p.GetSetMethod() != null))
                // Strip the namespace and add the @modname suffix if the typename isn't unique
                .Select(currType => {
                    var currName = CSharpTypeName(currType);
                    foreach (var otherType in allTypes) {
                        if (otherType.FullName == null || otherType.Namespace == null) {
                            continue;
                        }
                        
                        var otherName = CSharpTypeName(otherType);
                        if (currType != otherType && currName == otherName) {
                            return ($"{currName}@{ConsoleEnhancements.GetModName(currType)} ", currType);
                        }
                    }
                    return (currName, currType);
                })
                .Order(new NamespaceComparer())
                .Select(pair => new AutoCompleteEntry { Final = false, MemberName = pair.Item1, MemberExtra = pair.Item2.Namespace ?? string.Empty })
                .ToArray();
            
            entries.AddRange(filteredTypes);
        } else if (Everest.Modules.FirstOrDefault(m => m.Metadata.Name == args[0] && m.SettingsType != null) is { } mod) {
            entries.AddRange(GetTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, args), AutoCompleteType.Set));
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            entries.AddRange(GetTypeAutoCompleteEntries(RecurseSetType(types[0], args), AutoCompleteType.Set));
        }
        
        return string.Join(';', entries.Select(entry => $"{(entry.Final ? "!" : ".")}{entry.MemberName}#{entry.MemberExtra}"));
    }
    
    public static string GetInvokeCommandAutoCompleteEntries(string argsText) {
        var entries = new List<AutoCompleteEntry>();
        
        var args = argsText.Split('.');
        if (args.Length == 1) {
            var allTypes = ModUtils.GetTypes();
            var filteredTypes = allTypes
                // Filter-out types which probably aren't useful
                .Where(t => t.IsClass && t.IsPublic && t.FullName != null && t.Namespace != null && ignoredNamespaces.All(ns => !t.Namespace.StartsWith(ns)))
                // Filter-out compiler generated types
                .Where(t => t.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !t.FullName.Contains('<') && !t.FullName.Contains('>'))
                // Require either an entity, level, session or type with static methods
                .Where(t => t.IsSameOrSubclassOf(typeof(Entity)) || t.IsSameOrSubclassOf(typeof(Level)) || t.IsSameOrSubclassOf(typeof(Session)) ||
                                 t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Any(IsInvokableMethod))
                // Strip the namespace and add the @modname suffix if the typename isn't unique
                .Select(currType => {
                    var currName = CSharpTypeName(currType);
                    foreach (var otherType in allTypes) {
                        if (otherType.FullName == null || otherType.Namespace == null) {
                            continue;
                        }
                        
                        var otherName = CSharpTypeName(otherType);
                        if (currType != otherType && currName == otherName) {
                            return ($"{currName}@{ConsoleEnhancements.GetModName(currType)} ", currType);
                        }
                    }
                    return (currName, currType);
                })
                .Order(new NamespaceComparer())
                .Select(pair => new AutoCompleteEntry { Final = false, MemberName = pair.Item1, MemberExtra = pair.Item2.Namespace ?? string.Empty })
                .ToArray();
            
            entries.AddRange(filteredTypes);
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            entries.AddRange(GetTypeAutoCompleteEntries(types[0], AutoCompleteType.Invoke));
        }
        
        return string.Join(';', entries.Select(entry => $"{(entry.Final ? "!" : ".")}{entry.MemberName}#{entry.MemberExtra}"));
    }
    
    public static string GetParameterAutoCompleteEntries(string argsText) {
        var entries = new List<AutoCompleteEntry>();
        
        var parts = argsText.Split(';');
        bool isSet = parts[0] == "Set";
        var args = parts[1].Split('.');
        int index = int.Parse(parts[2]);
        
        if (isSet && args.Length == 1) {
            // Vanilla setting / session / assist
            if (typeof(Settings).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fSettings) {
                entries.AddRange(GetTypeAutoCompleteEntries(fSettings.FieldType, AutoCompleteType.Parameter));
            } else if (typeof(SaveData).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fSaveData) {
                entries.AddRange(GetTypeAutoCompleteEntries(fSaveData.FieldType, AutoCompleteType.Parameter));
            } else if (typeof(Assists).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fAssists) {
                entries.AddRange(GetTypeAutoCompleteEntries(fAssists.FieldType, AutoCompleteType.Parameter));    
            }
        } if (isSet && Everest.Modules.FirstOrDefault(m => m.Metadata.Name == args[0] && m.SettingsType != null) is { } mod) {
            entries.AddRange(GetTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, args, includeLast: true), AutoCompleteType.Parameter));
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            if (isSet) {
                entries.AddRange(GetTypeAutoCompleteEntries(RecurseSetType(types[0], args, includeLast: true), AutoCompleteType.Parameter));
            } else {
                var parameters = types[0].GetMethodInfo(args[1]).GetParameters();
                if (index >= 0 && index < parameters.Length) {
                    bool final = index == parameters.Length - 1 || 
                                 index < parameters.Length - 1 && !IsSettableType(parameters[index].ParameterType); 
                    
                    entries.AddRange(GetTypeAutoCompleteEntries(parameters[index].ParameterType, AutoCompleteType.Parameter)
                        .Select(entry => entry with { Final = final }));
                }
            }
        }
        
        return string.Join(';', entries.Select(entry => $"{(entry.Final ? "!" : ".")}{entry.MemberName}#{entry.MemberExtra}"));
    }
    
    private enum AutoCompleteType { Set, Invoke, Parameter }
    private static Type RecurseSetType(Type baseType, string[] args, bool includeLast = false) {
        var type = baseType;
        for (int i = 1; i < args.Length - (includeLast ? 0 : 1); i++) {
            if (type.GetFieldInfo(args[i]) is { } field) {
                type = field.FieldType;
                continue;
            }
            if (type.GetPropertyInfo(args[i]) is { } property && property.GetSetMethod() != null) {
                type = property.PropertyType;
                continue;
            }
            break; // Invalid type
        }
        return type;
    }
    
    private static IEnumerable<AutoCompleteEntry> GetTypeAutoCompleteEntries(Type type, AutoCompleteType autoCompleteType) {
        bool staticMembers = !(type.IsSameOrSubclassOf(typeof(Entity)) || type.IsSameOrSubclassOf(typeof(Level)) || type.IsSameOrSubclassOf(typeof(Session)) || type.IsSameOrSubclassOf(typeof(EverestModuleSettings)));
        var bindingFlags = staticMembers
            ? BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            : BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        
        if (autoCompleteType == AutoCompleteType.Set) {
            foreach (var entry in type.GetProperties(bindingFlags)
                         // Filter-out compiler generated properties
                         .Where(p => p.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !p.Name.Contains('<') && !p.Name.Contains('>'))
                         .Where(p => IsSettableType(p.PropertyType) && p.GetSetMethod() != null)
                         .OrderBy(p => p.Name)
                         .Select(p => new AutoCompleteEntry {
                             Final = IsFinal(p.PropertyType), 
                             MemberName = p.Name, 
                             MemberExtra = CSharpTypeName(p.PropertyType)
                         }))
            {
                yield return entry;
            }
            foreach (var entry in type.GetFields(bindingFlags)
                         // Filter-out compiler generated fields
                         .Where(f => f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>'))
                         .Where(f => !f.IsInitOnly && IsSettableType(f.FieldType))
                         .OrderBy(f => f.Name)
                         .Select(f => new AutoCompleteEntry {
                             Final = IsFinal(f.FieldType), 
                             MemberName = f.Name, 
                             MemberExtra = CSharpTypeName(f.FieldType)
                         }))
            {
                yield return entry;
            }
        } else if (autoCompleteType == AutoCompleteType.Invoke) {
            foreach (var entry in type.GetMethods(bindingFlags)
                         // Filter-out compiler generated methods
                         .Where(m => m.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !m.Name.Contains('<') && !m.Name.Contains('>') && !m.Name.StartsWith("set_") && !m.Name.StartsWith("get_"))
                         .Where(IsInvokableMethod)
                         .OrderBy(m => m.Name)
                         .Select(m => new AutoCompleteEntry {
                             Final = !m.GetParameters().Any(p => IsSettableType(p.ParameterType) || p.HasDefaultValue), 
                             MemberName = m.Name, 
                             MemberExtra = $"({string.Join(", ", m.GetParameters().Select(p => CSharpTypeName(p.ParameterType)))})"
                         }))
            {
                yield return entry;
            }
        } else if (autoCompleteType == AutoCompleteType.Parameter) {
            if (type == typeof(bool)) {
                yield return new AutoCompleteEntry { Final = true, MemberName = "true", MemberExtra = CSharpTypeName(type) };
                yield return new AutoCompleteEntry { Final = true, MemberName = "false", MemberExtra = CSharpTypeName(type) };
            } else if (type == typeof(ButtonBinding)) {
                foreach (var button in Enum.GetValues<MButtons>()) {
                    yield return new AutoCompleteEntry { Final = true, MemberName = button.ToString(), MemberExtra = "Mouse" };
                }
                foreach (var key in Enum.GetValues<Keys>()) {
                    if (key is Keys.Left or Keys.Right) {
                        // These keys can't be used, since the mouse buttons already use that name
                        continue;
                    }
                    yield return new AutoCompleteEntry { Final = true, MemberName = key.ToString(), MemberExtra = "Key" };
                }
            } else if (type.IsEnum) {
                foreach (var value in Enum.GetValues(type)) {
                    yield return new AutoCompleteEntry { Final = true, MemberName = value.ToString(), MemberExtra = CSharpTypeName(type) };
                }
            }
        }
    }
    
    private static bool IsFinal(Type type) => type == typeof(string) || type == typeof(Vector2) || type == typeof(Random) || type == typeof(ButtonBinding) || type.IsEnum || type.IsPrimitive;
    private static bool IsSettableType(Type type) => !type.IsSameOrSubclassOf(typeof(Delegate));
    private static bool IsInvokableMethod(MethodInfo info) => !info.IsGenericMethod && info.GetParameters().All(p => IsSettableType(p.ParameterType) || p.HasDefaultValue);
    
    private static readonly Dictionary<Type, string> shorthandMap = new() {
        { typeof(bool), "bool" },
        { typeof(byte), "byte" },
        { typeof(char), "char" },
        { typeof(decimal), "decimal" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(int), "int" },
        { typeof(long), "long" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(string), "string" },
        { typeof(uint), "uint" },
        { typeof(ulong), "ulong" },
        { typeof(ushort), "ushort" },
    };
    private static string CSharpTypeName(Type type, bool isOut = false) {
        if (type.IsByRef) {
            return $"{(isOut ? "out" : "ref")} {CSharpTypeName(type.GetElementType())}";
        }
        if (type.IsGenericType) {
            if (type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                return $"{CSharpTypeName(Nullable.GetUnderlyingType(type))}?";
            }
            return $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GenericTypeArguments.Select(a => CSharpTypeName(a)).ToArray())}>";
        }
        if (type.IsArray) {
            return $"{CSharpTypeName(type.GetElementType())}[]";
        }
        
        if (shorthandMap.TryGetValue(type, out string shorthand)) {
            return shorthand;
        }
        if (type.FullName == null) {
            return type.Name;    
        }
        
        int namespaceLen = type.Namespace != null
            ? type.Namespace.Length + 1
            : 0;
        return type.FullName[namespaceLen..];
    }  
}