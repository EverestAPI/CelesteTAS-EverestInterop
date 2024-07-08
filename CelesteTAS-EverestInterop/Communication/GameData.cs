using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste; 
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

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
    
    public static string GetSetCommandAutoCompleteOptions(string currentInput) {
        var final = new List<string>();
        var nonFinal = new List<string>();
        
        var args = currentInput.Split('.');
        
        if (args.Length == 1) {
            // Vanilla game settings or mod settings type or a base type
            final.AddRange(typeof(Settings).GetFields().Select(f => f.Name));
            final.AddRange(typeof(SaveData).GetFields().Select(f => f.Name));
            final.AddRange(typeof(Assists).GetFields().Select(f => f.Name));
            
            nonFinal.AddRange(Everest.Modules.Where(mod => mod.SettingsType != null).Select(mod => mod.Metadata.Name));
            
            string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod"];
            var allTypes = ModUtils.GetTypes();
            var filteredTypes = allTypes 
                .Where(t => t.FullName != null && t.Namespace != null && ignoredNamespaces.All(ns => !t.Namespace.StartsWith(ns))) // Filter-out types which probably aren't useful
                .Where(t => t.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !t.FullName.Contains('<') && !t.FullName.Contains('>')) // Filter-out compiler generated types
                .Select(t => {
                    // Strip the namespace and add the @modname suffix if the typename isn't unique
                    var currName = t.FullName![(t.Namespace!.Length + 1)..];
                    foreach (var type in allTypes) {
                        if (type.FullName == null || type.Namespace == null) {
                            continue;
                        }
                        
                        var otherName = type.FullName![(type.Namespace.Length + 1)..];
                        if (t != type && currName == otherName) {
                            return ($"{currName}@{ConsoleEnhancements.GetModName(t)} ", t);
                        }
                    }
                    return (currName, t);
                })
                .Order(new NamespaceComparer())
                .Select(pair => pair.Item1)
                .ToArray();
                
            nonFinal.AddRange(filteredTypes);
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
            
            final.AddRange(type.GetAllProperties()
                .Where(p => p.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !p.Name.Contains('<') && !p.Name.Contains('>')) // Filter-out compiler generated properties
                .Where(p => p.GetSetMethod() != null) // Require settable property
                .Select(p => p.Name)
                .Order());
            
            final.AddRange(type.GetAllFieldInfos()
                .Where(f => f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>')) // Filter-out compiler generated fields
                .Select(f => f.Name)
                .Order());
        }
        
        return string.Join(';', final) + '#' + string.Join(';', nonFinal);
    }
}