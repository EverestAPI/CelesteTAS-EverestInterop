using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste; 
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
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
    
    // Each entry is separated by a ';' and has the format: <'!' = final, '.' = non-final><displayed member name>#<type of member>
    // Special type names are inside angle-brackets
    private struct AutoCompleteEntry {
        public bool Final;
        public string MemberName;
        public string MemberType;
    }
    
    public static string GetSetCommandAutoCompleteEntries(string currentInput) {
        var entries = new List<AutoCompleteEntry>();
        
        var args = currentInput.Split('.');
        if (args.Length == 1) {
            // Vanilla game settings or mod settings type or a base type
            entries.AddRange(typeof(Settings).GetFields().Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberType = "<Settings>" }));
            entries.AddRange(typeof(SaveData).GetFields().Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberType = "<SaveData>" }));
            entries.AddRange(typeof(Assists).GetFields().Select(f => new AutoCompleteEntry { Final = true, MemberName = f.Name, MemberType = "<Assists>" }));
            
            // Mod settings
            entries.AddRange(Everest.Modules
                .Where(mod => mod.SettingsType != null)
                // Require at least 1 settable field / property
                .Where(mod => mod.SettingsType.GetAllFieldInfos().Any() || mod.SettingsType.GetAllProperties().Any(p => p.GetSetMethod() != null))
                .Select(mod => new AutoCompleteEntry { Final = true, MemberName = mod.Metadata.Name, MemberType = "<ModSetting>" }));
            
            string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod", "Snowberry"];
            var allTypes = ModUtils.GetTypes();
            var filteredTypes = allTypes
                // Filter-out types which probably aren't useful
                .Where(t => t.IsClass && t.IsPublic && t.FullName != null && t.Namespace != null && ignoredNamespaces.All(ns => !t.Namespace.StartsWith(ns)))
                // Filter-out compiler generated types
                .Where(t => t.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !t.FullName.Contains('<') && !t.FullName.Contains('>'))
                // Require either an entity, level, session or type with static variables
                .Where(t => t.IsSameOrSubclassOf(typeof(Entity)) || t.IsSameOrSubclassOf(typeof(Level)) || t.IsSameOrSubclassOf(typeof(Session)) ||
                            t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Any(f => !f.IsInitOnly && !IsUnSettableType(f.FieldType)) ||
                            t.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Any(p => !IsUnSettableType(p.PropertyType) && p.GetSetMethod() != null))
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
                .Select(pair => new AutoCompleteEntry { Final = false, MemberName = pair.Item1, MemberType = $"<NS:{pair.Item2.Namespace ?? string.Empty}>" })
                .ToArray();
            
            entries.AddRange(filteredTypes);
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            var type = types[0];
            // Recurse down to current type
            for (int i = 1; i < args.Length - 1; i++) {
                if (type.GetFieldInfo(args[i]) is { } field) {
                    type = field.FieldType;
                    continue;
                }
                if (type.GetPropertyInfo(args[i]) is { } property && property.GetSetMethod() != null) {
                    type = property.PropertyType;
                    continue;
                }
                return "#"; // Invalid type
            }
            
            bool staticMembers = !(type.IsSameOrSubclassOf(typeof(Entity)) || type.IsSameOrSubclassOf(typeof(Level)) || type.IsSameOrSubclassOf(typeof(Session)));
            var bindingFlags = staticMembers
                ? BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                : BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                
            entries.AddRange(type.GetProperties(bindingFlags)
                // Filter-out compiler generated properties
                .Where(p => p.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !p.Name.Contains('<') && !p.Name.Contains('>'))
                .Where(p => !IsUnSettableType(p.PropertyType) && p.GetSetMethod() != null) 
                .OrderBy(p => p.Name)
                .Select(p => new AutoCompleteEntry { Final = IsFinal(p.PropertyType), MemberName = p.Name, MemberType = CSharpTypeName(p.PropertyType) }));
            
            entries.AddRange(type.GetFields(bindingFlags)
                // Filter-out compiler generated fields
                .Where(f => f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>'))
                .Where(f => !f.IsInitOnly && !IsUnSettableType(f.FieldType))
                .OrderBy(f => f.Name)
                .Select(f => new AutoCompleteEntry { Final = IsFinal(f.FieldType), MemberName = f.Name, MemberType = CSharpTypeName(f.FieldType) }));
        }
        
        return string.Join(';', entries.Select(entry => $"{(entry.Final ? "!" : ".")}{entry.MemberName}#{entry.MemberType}"));
        
        static bool IsFinal(Type type) => type == typeof(string) || type == typeof(Vector2) || type == typeof(Random) || type.IsEnum || type.IsPrimitive;
        static bool IsUnSettableType(Type type) => type.IsSameOrSubclassOf(typeof(Delegate));
    }
    
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