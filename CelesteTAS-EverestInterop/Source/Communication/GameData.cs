using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Monocle;
using StudioCommunication;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.Communication;

public static class GameData {
    private static Dictionary<string, ModUpdateInfo>? modUpdateInfos;

    [Load]
    private static void Load() {
        typeof(ModUpdaterHelper).GetMethodInfo("DownloadModUpdateList")?.OnHook(ModUpdaterHelperOnDownloadModUpdateList);
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
        EverestModuleMetadata? mapMeta = null;
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

        EverestModuleMetadata? speedrunToolMeta = metas.FirstOrDefault(metadata => metadata.Name == "SpeedrunTool");
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
        if (typeof(CelesteTasSettings).GetPropertyInfo(settingName) is { } property) {
            return property.GetValue(TasSettings)!.ToString()!;
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
        EverestModule? mapModule = null;
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

    public static int? GetWakeupTime() {
        if (Engine.Scene is not Level level) {
            return null;
        }

        AreaData areaData = AreaData.Get(level);

        int? wakeupTime = areaData.IntroType switch {
            // Player.IntroTypes.Transition => expr,
            Player.IntroTypes.Respawn => 36,
            // Player.IntroTypes.WalkInRight => expr,
            // Player.IntroTypes.WalkInLeft => expr,
            // Player.IntroTypes.Jump => expr,
            Player.IntroTypes.WakeUp => 190,
            // Player.IntroTypes.Fall => expr,
            // Player.IntroTypes.TempleMirrorVoid => expr,
            Player.IntroTypes.None => null,
            // Player.IntroTypes.ThinkForABit => expr,
            _ => null,
        };

        if (wakeupTime == null) {
            $"Couldn't determine wakeup time for intro type '{areaData.IntroType}'".Log(LogLevel.Warn);
        }

        return wakeupTime;
    }

    public static GameState? GetGameState() {
        if (Engine.Scene is not Level level) {
            return null;
        }

        var player = level.Tracker.GetEntity<Player>();

        return new GameState {
            Player = new GameState.PlayerState {
                Position = player.Position.ToGameStateVec2(),
                PositionRemainder = player.PositionRemainder.ToGameStateVec2(),
                Speed = player.Speed.ToGameStateVec2(),
                starFlySpeedLerp = player.starFlySpeedLerp,
            },
            Level = new GameState.LevelState {
                Bounds = level.Bounds.ToGameStateRectI(),
                WindDirection = level.Wind.ToGameStateVec2(),
            },

            SolidsData = level.Session.LevelData.Solids,
            StaticSolids = level.Entities
                .Where(e => e is Solid and not StarJumpBlock { sinks: true } && e.Collider is Hitbox && e.Collidable)
                .Select(e => e.ToGameStateRectF())
                .ToArray(),

            Spinners = level.Entities
                .Where(e => e is CrystalStaticSpinner or DustStaticSpinner || e.GetType().Name == "CustomSpinner")
                .Select(e => e.Position.ToGameStateVec2())
                .ToArray(),
            Lightning = level.Entities
                .FindAll<Lightning>()
                .Select(e => e.ToGameStateRectF())
                .ToArray(),
            Spikes = level.Entities
                .FindAll<Spikes>()
                .Select(e => new GameState.Spike(e.ToGameStateRectF(), e.Direction.ToGameStateDirection()))
                .ToArray(),

            WindTriggers = level.Tracker
                .GetEntities<WindTrigger>().Cast<WindTrigger>()
                .Select(e => new GameState.WindTrigger(e.ToGameStateRectF(), e.Pattern.ToGameStatePattern()))
                .ToArray(),

            JumpThrus = level.Entities
                .Where(e => e is JumpthruPlatform || e.GetType().Name is "SidewaysJumpThru" or "UpsideDownJumpThru")
                .Select(e => {
                    if (e is JumpthruPlatform) {
                        return new GameState.JumpThru(e.ToGameStateRectF(), GameState.Direction.Up, true);
                    }
                    if (e.GetType().Name == "SidewaysJumpThru") {
                        return new GameState.JumpThru(e.ToGameStateRectF(), e.GetFieldValue<bool>("AllowLeftToRight") ? GameState.Direction.Right : GameState.Direction.Left, e.GetFieldValue<bool>("pushPlayer"));
                    }
                    if (e.GetType().Name == "UpsideDownJumpThru") {
                        return new GameState.JumpThru(e.ToGameStateRectF(), GameState.Direction.Down, e.GetFieldValue<bool>("pushPlayer"));
                    }
                    throw new UnreachableException();
                })
                .ToArray(),
        };
    }
}
