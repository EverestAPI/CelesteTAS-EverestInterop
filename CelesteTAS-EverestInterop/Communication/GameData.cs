using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste; 
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
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
    
    private static uint getGamebananaId(string url) {
        uint gbid = 0;
        if (url.StartsWith("http://gamebanana.com/dl/") && uint.TryParse(url.Substring("http://gamebanana.com/dl/".Length), out gbid)) 
            return gbid;
        if (url.StartsWith("https://gamebanana.com/dl/") && uint.TryParse(url.Substring("https://gamebanana.com/dl/".Length), out gbid)) 
            return gbid;
        if (url.StartsWith("http://gamebanana.com/mmdl/") && uint.TryParse(url.Substring("http://gamebanana.com/mmdl/".Length), out gbid)) 
            return gbid;
        if (url.StartsWith("https://gamebanana.com/mmdl/") && uint.TryParse(url.Substring("https://gamebanana.com/mmdl/".Length), out gbid)) 
            return gbid;
        return gbid;
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
            if (modUpdateInfos?.TryGetValue(mapMeta.Name, out var modUpdateInfo) == true && getGamebananaId(modUpdateInfo.URL) is var gamebananaId and > 0) {
                modInfo += $"#   https://gamebanana.com/mods/{gamebananaId}\n";
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

        if (modUpdateInfos?.TryGetValue(moduleName, out var modUpdateInfo) == true && getGamebananaId(modUpdateInfo.URL) is var gamebananaId and > 0) {
            return $"# {moduleName}\n# https://gamebanana.com/mods/{gamebananaId}\n\n";
        }

        return string.Empty;
    }
    
    public static GameState? GetGameState() {
        if (Engine.Scene is not Level level) {
            return null;
        }
        
        var player = level.Tracker.GetEntity<Player>();
        
        return new GameState {
            Player = new GameState.PlayerState {
                Position = player.Position.ToTuple(),
                PositionRemainder = player.PositionRemainder.ToTuple(),
                Speed = player.Speed.ToTuple(),
                starFlySpeedLerp = player.starFlySpeedLerp,
            },
            Level = new GameState.LevelState {
                Bounds = (level.Bounds.X, level.Bounds.Y, level.Bounds.Width, level.Bounds.Height),
                WindDirection = level.Wind.ToTuple(),
            },
            
            SolidsData = level.Session.LevelData.Solids,
            StaticSolids = level.Entities
                .Where(e => e is Solid and not StarJumpBlock { sinks: true } && e.Collider is Hitbox && e.Collidable)
                .Select(e => (e.X, e.Y, e.Width, e.Height))
                .ToArray(),
            
            Spinners = level.Entities
                .Where(e => e is CrystalStaticSpinner or DustStaticSpinner || e.GetType().Name == "CustomSpinner")
                .Select(e => e.Position.ToTuple())
                .ToArray(),
            Lightning = level.Entities
                .FindAll<Lightning>()
                .Select(e => (e.X, e.Y, e.Width, e.Height))
                .ToArray(),
            Spikes = level.Entities
                .FindAll<Spikes>()
                .Select(e => (e.X, e.Y, e.Width, e.Height, ToGameStateDirection(e.Direction)))
                .ToArray(),
            
            WindTriggers = level.Tracker
                .GetEntities<WindTrigger>().Cast<WindTrigger>()
                .Select(e => (e.X, e.Y, e.Width, e.Height, ToGameStatePattern(e.Pattern)))
                .ToArray(),
            
            JumpThrus = level.Entities
                .Where(e => e is JumpthruPlatform || e.GetType().Name is "SidewaysJumpThru" or "UpsideDownJumpThru")
                .Select(e => {
                    if (e is JumpthruPlatform) {
                        return (e.X, e.Y, e.Width, e.Height, GameState.Direction.Up, true);
                    }
                    if (e.GetType().Name == "SidewaysJumpThru") {
                        return (e.X, e.Y, e.Width, e.Height, e.GetFieldValue<bool>("AllowLeftToRight") ? GameState.Direction.Right : GameState.Direction.Left, e.GetFieldValue<bool>("pushPlayer"));
                    }
                    if (e.GetType().Name == "UpsideDownJumpThru") {
                        return (e.X, e.Y, e.Width, e.Height, GameState.Direction.Down, e.GetFieldValue<bool>("pushPlayer"));
                    }
                    throw new UnreachableException();
                })
                .ToArray(),
        };
            
        static GameState.Direction ToGameStateDirection(Spikes.Directions dir) => dir switch {
            Spikes.Directions.Up => GameState.Direction.Up,
            Spikes.Directions.Down => GameState.Direction.Down,
            Spikes.Directions.Left => GameState.Direction.Left,
            Spikes.Directions.Right => GameState.Direction.Right,
            _ => throw new UnreachableException()
        };
        static GameState.WindPattern ToGameStatePattern(WindController.Patterns pattern) => pattern switch {
            WindController.Patterns.None => GameState.WindPattern.None,
            WindController.Patterns.Left => GameState.WindPattern.Left,
            WindController.Patterns.Right => GameState.WindPattern.Right,
            WindController.Patterns.LeftStrong => GameState.WindPattern.LeftStrong,
            WindController.Patterns.RightStrong => GameState.WindPattern.RightStrong,
            WindController.Patterns.LeftOnOff => GameState.WindPattern.LeftOnOff,
            WindController.Patterns.RightOnOff => GameState.WindPattern.RightOnOff,
            WindController.Patterns.LeftOnOffFast => GameState.WindPattern.LeftOnOffFast,
            WindController.Patterns.RightOnOffFast => GameState.WindPattern.RightOnOffFast,
            WindController.Patterns.Alternating => GameState.WindPattern.Alternating,
            WindController.Patterns.LeftGemsOnly => GameState.WindPattern.LeftGemsOnly,
            WindController.Patterns.RightCrazy => GameState.WindPattern.RightCrazy,
            WindController.Patterns.Down => GameState.WindPattern.Down,
            WindController.Patterns.Up => GameState.WindPattern.Up,
            WindController.Patterns.Space => GameState.WindPattern.Space,
            _ => throw new UnreachableException()
        };
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
    
    private static readonly string[] ignoredNamespaces = ["System", "StudioCommunication", "TAS", "SimplexNoise", "FMOD", "MonoMod", "Snowberry"];
    public static IEnumerable<CommandAutoCompleteEntry> GetSetCommandAutoCompleteEntries(string argsText, int index) {
        if (index != 0) {
            return GetParameterAutoCompleteEntries(argsText, index, AutoCompleteType.Set);
        }
        
        var args = argsText.Split('.');
        if (args.Length == 1) {
            var entries = new List<CommandAutoCompleteEntry>();
            
            // Vanilla settings. Manually selected to filter out useless entries
            var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetField(e)!);
            var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetField(e)!);
            entries.AddRange(vanillaSettings.Select(f => new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{CSharpTypeName(f.FieldType)} (Settings)", IsDone = true }));
            entries.AddRange(vanillaSaveData.Select(f => new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{CSharpTypeName(f.FieldType)} (Save Data)", IsDone = true }));
            entries.AddRange(typeof(Assists).GetFields().Select(f => new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{CSharpTypeName(f.FieldType)} (Assists)", IsDone = true }));
            
            // Mod settings
            entries.AddRange(Everest.Modules
                .Where(mod => mod.SettingsType != null)
                // Require at least 1 settable field / property
                .Where(mod => mod.SettingsType.GetAllFieldInfos().Any() || mod.SettingsType.GetAllProperties().Any(p => p.GetSetMethod() != null))
                .Select(mod => new CommandAutoCompleteEntry { Name = mod.Metadata.Name, Extra = "Mod Setting", IsDone = false }));
            
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
                .Select(pair => new CommandAutoCompleteEntry { Name = pair.Item1, Extra = pair.Item2.Namespace ?? string.Empty, IsDone = false })
                .ToArray();
            
            entries.AddRange(filteredTypes);
            return entries.Select(e => e with { Name = e.IsDone ? e.Name : e.Name + "." }); // Append '.' for next segment if not done
        } else if (args[0] == "ExtendedVariantMode") {
            // Special case for setting extended variants
            if (ExtendedVariantsUtils.GetVariantsEnum() is { } variantsEnum) {
                return Enum.GetValues(variantsEnum).Cast<object>()
                    .Select(variant => {
                        string typeName = string.Empty;
                        try {
                            var variantType = ExtendedVariantsUtils.GetVariantType(new(variant));
                            if (variantType != null) {
                                typeName = CSharpTypeName(variantType);
                            }
                        } catch {
                            // ignore
                        }
                        return new CommandAutoCompleteEntry { Name = variant.ToString(), Prefix = string.Join('.', args[..^1]) + ".", Extra = typeName, IsDone = true, HasNext = true };
                    });
            }
        } else if (Everest.Modules.FirstOrDefault(m => m.Metadata.Name == args[0] && m.SettingsType != null) is { } mod) {
            return GetTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, args), AutoCompleteType.Set)
                .Select(e => e with { Name = e.Name + (e.IsDone ? "" : "."), Prefix = string.Join('.', args[..^1]) + ".", HasNext = true });
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            return GetTypeAutoCompleteEntries(RecurseSetType(types[0], args), AutoCompleteType.Set)
                .Select(e => e with { Name = e.Name + (e.IsDone ? "" : "."), Prefix = string.Join('.', args[..^1]) + ".", HasNext = true });
        }
        
        return [];
    }
    
    public static IEnumerable<CommandAutoCompleteEntry> GetInvokeCommandAutoCompleteEntries(string argsText, int index) {
        if (index != 0) {
            return GetParameterAutoCompleteEntries(argsText, index, AutoCompleteType.Invoke);
        }
        
        var args = argsText.Split('.');
        if (args.Length == 1) {
            var entries = new List<CommandAutoCompleteEntry>();

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
                .Select(pair => new CommandAutoCompleteEntry { Name = pair.Item1, Extra = pair.Item2.Namespace ?? string.Empty, IsDone = false })
                .ToArray();
            
            entries.AddRange(filteredTypes);
            return entries.Select(e => e with { Name = e.IsDone ? e.Name : e.Name + "." }); // Append '.' for next segment if not done;
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            return GetTypeAutoCompleteEntries(types[0], AutoCompleteType.Invoke)
                .Select(e => e with { Name = e.Name + (e.IsDone ? "" : "."), Prefix = string.Join('.', args[..^1]) + ".", HasNext = true });
        }
        
        return [];
    }
    
    private enum AutoCompleteType { Set, Invoke, Parameter }

    private static IEnumerable<CommandAutoCompleteEntry> GetParameterAutoCompleteEntries(string argsText, int index, AutoCompleteType autoCompleteType) {
        var args = argsText.Split('.');
        
        if (autoCompleteType == AutoCompleteType.Set && args.Length == 1) {
            // Vanilla setting / session / assist
            if (typeof(Settings).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fSettings) {
                return GetTypeAutoCompleteEntries(fSettings.FieldType, AutoCompleteType.Parameter);
            } else if (typeof(SaveData).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fSaveData) {
                return GetTypeAutoCompleteEntries(fSaveData.FieldType, AutoCompleteType.Parameter);
            } else if (typeof(Assists).GetFieldInfo(args[0], BindingFlags.Instance | BindingFlags.Public) is { } fAssists) {
                return GetTypeAutoCompleteEntries(fAssists.FieldType, AutoCompleteType.Parameter);
            }
        } else if (args[0] == "ExtendedVariantMode") {
            // Special case for setting extended variants
            var variant = ExtendedVariantsUtils.ParseVariant(args[1]);
            var variantType = ExtendedVariantsUtils.GetVariantType(new(variant));
            
            if (variantType != null) {
                return GetTypeAutoCompleteEntries(RecurseSetType(variantType, args, includeLast: true), AutoCompleteType.Parameter);
            }
        } else if (autoCompleteType == AutoCompleteType.Set && Everest.Modules.FirstOrDefault(m => m.Metadata.Name == args[0] && m.SettingsType != null) is { } mod) {
            return GetTypeAutoCompleteEntries(RecurseSetType(mod.SettingsType, args, includeLast: true), AutoCompleteType.Parameter);
        } else if (InfoCustom.TryParseTypes(args[0], out var types, out _, out _)) {
            // Let's just assume the first type
            if (autoCompleteType == AutoCompleteType.Set) {
                return GetTypeAutoCompleteEntries(RecurseSetType(types[0], args, includeLast: true), AutoCompleteType.Parameter);
            } else if (autoCompleteType == AutoCompleteType.Invoke) {
                var parameters = types[0].GetMethodInfo(args[1]).GetParameters();
                if (index >= 0 && index < parameters.Length) {
                    bool final = index == parameters.Length - 1 || 
                                 index < parameters.Length - 1 && !IsSettableType(parameters[index].ParameterType);
                    
                    return GetTypeAutoCompleteEntries(parameters[index].ParameterType, AutoCompleteType.Parameter)
                        .Select(e => e with { HasNext = !final });
                }
            }
        }
        
        return [];
    }
    
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
    
    private static IEnumerable<CommandAutoCompleteEntry> GetTypeAutoCompleteEntries(Type type, AutoCompleteType autoCompleteType) {
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
                         .Select(p => new CommandAutoCompleteEntry {
                             Name = p.Name,
                             Extra = CSharpTypeName(p.PropertyType),
                             IsDone = IsFinal(p.PropertyType), 
                         }))
            {
                yield return entry;
            }
            foreach (var entry in type.GetFields(bindingFlags)
                         // Filter-out compiler generated fields
                         .Where(f => f.GetCustomAttributes<CompilerGeneratedAttribute>().IsEmpty() && !f.Name.Contains('<') && !f.Name.Contains('>'))
                         .Where(f => !f.IsInitOnly && IsSettableType(f.FieldType))
                         .OrderBy(f => f.Name)
                         .Select(f => new CommandAutoCompleteEntry {
                             Name = f.Name, 
                             Extra = CSharpTypeName(f.FieldType),
                             IsDone = IsFinal(f.FieldType), 
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
                         .Select(m => new CommandAutoCompleteEntry {
                             Name = m.Name, 
                             Extra = $"({string.Join(", ", m.GetParameters().Select(p => CSharpTypeName(p.ParameterType)))})",
                             IsDone = !m.GetParameters().Any(p => IsSettableType(p.ParameterType) || p.HasDefaultValue),
                         }))
            {
                yield return entry;
            }
        } else if (autoCompleteType == AutoCompleteType.Parameter) {
            if (type == typeof(bool)) {
                yield return new CommandAutoCompleteEntry { Name = "true", Extra = CSharpTypeName(type), IsDone = true };
                yield return new CommandAutoCompleteEntry { Name = "false", Extra = CSharpTypeName(type), IsDone = true };
            } else if (type == typeof(ButtonBinding)) {
                foreach (var button in Enum.GetValues<MButtons>()) {
                    yield return new CommandAutoCompleteEntry { Name = button.ToString(), Extra = "Mouse", IsDone = true };
                }
                foreach (var key in Enum.GetValues<Keys>()) {
                    if (key is Keys.Left or Keys.Right) {
                        // These keys can't be used, since the mouse buttons already use that name
                        continue;
                    }
                    yield return new CommandAutoCompleteEntry { Name = key.ToString(), Extra = "Key", IsDone = true };
                }
            } else if (type.IsEnum) {
                foreach (var value in Enum.GetValues(type)) {
                    yield return new CommandAutoCompleteEntry { Name = value.ToString(), Extra = CSharpTypeName(type), IsDone = true };
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