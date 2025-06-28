using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
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

    /// Attempt to guess the amount of frames the intro animation takes for the current level
    public static int? GetIntroTime() {
        if (Engine.Scene is not Level level) {
            return null;
        }

        var areaData = AreaData.Get(level);
        var player = level.GetPlayer();

        int? wakeupTime;
        switch (areaData.IntroType) {
            // Player.IntroTypes.Transition => expr,
            case Player.IntroTypes.Respawn: {
                wakeupTime = (int) Math.Ceiling(0.6f / Engine.DeltaTime); // respawnTween = Tween.Create(Tween.TweenMode.Oneshot, null, 0.6f, start: true);
                break;
            }

            case Player.IntroTypes.WalkInRight:
            case Player.IntroTypes.WalkInLeft: {
                if (player == null) {
                    wakeupTime = null;
                    break;
                }

                bool isRight = areaData.IntroType == Player.IntroTypes.WalkInRight;
                var direction = isRight ? Facings.Right : Facings.Left;

                var spawnPoint = level.DefaultSpawnPoint;
                float startX = isRight
                    ? level.Bounds.Left - 16.0f
                    : level.Bounds.Right + 16.0f;
                float targetX = spawnPoint.X;

                // Simulate walk done by IntroWalkCoroutine
                int walkFrames = 0;

                var was = player.Collider;
                player.Collider = player.normalHitbox;

                float xPos = startX;
                while (Math.Abs(Math.Round(xPos) - targetX) > 2.0f && !player.CollideCheck<Solid>(new Vector2(xPos + (float) direction, spawnPoint.Y))) {
                    xPos = Calc.Approach(xPos, targetX, 64.0f * Engine.DeltaTime);
                    walkFrames++;
                }

                player.Collider = was;

                wakeupTime = 1 + // From state-machine coroutine
                     (int) (Math.Ceiling(0.3f / Engine.DeltaTime) + 1) + // yield return 0.3f;
                     (int) (Math.Ceiling(0.2f / Engine.DeltaTime) + 1) + // yield return 0.2f;
                     walkFrames;
                break;
            }

            case Player.IntroTypes.Jump: {
                if (player == null) {
                    wakeupTime = null;
                    break;
                }

                var spawnPoint = level.DefaultSpawnPoint;
                float startY = level.Bounds.Bottom + 16.0f;
                float targetY = spawnPoint.Y - 8.0f;

                // Simulate jump done by IntroJumpCoroutine
                int jumpFrames = 0;

                float yPos = startY;
                while (yPos > targetY) {
                    yPos += -120.0f * Engine.DeltaTime;
                    jumpFrames++;
                }
                yPos = MathF.Round(yPos);

                float ySpeed = -100.0f;
                while (ySpeed < 0.0f) {
                    ySpeed += 800.0f * Engine.DeltaTime;
                    yPos += ySpeed * Engine.DeltaTime;
                    jumpFrames++;
                }
                ySpeed = 0.0f;

                var was = player.Collider;
                player.Collider = player.normalHitbox;

                static bool CheckGround(Player player, Vector2 pos) => player.CollideCheck<Solid>(pos) || player.CollideCheck<JumpThru>(pos);
                while (!CheckGround(player, new Vector2(spawnPoint.X, yPos))) {
                    ySpeed += 800.0f * Engine.DeltaTime;
                    yPos += ySpeed * Engine.DeltaTime;
                    jumpFrames++;
                }

                player.Collider = was;

                wakeupTime = 1 + // From state-machine coroutine
                     (int) (Math.Ceiling(0.5f / Engine.DeltaTime) + 1) + // yield return 0.5f;
                     (int) (Math.Ceiling(0.1f / Engine.DeltaTime) + 1) + // yield return 0.1f;
                     jumpFrames;
                break;
            }

            case Player.IntroTypes.WakeUp: {
                float wakeUpTime = player != null && player.Sprite.Animations.TryGetValue("wakeUp", out var wakeUpAnim)
                    ? wakeUpAnim.Frames.Length * wakeUpAnim.Delay
                    : 24 * 0.1f; // Taken from vanilla Sprites.xml

                wakeupTime = 1 + // From state-machine coroutine
                     (int) (Math.Ceiling(0.5f / Engine.DeltaTime) + 1) +       // yield return 0.5f;
                     (int) (Math.Ceiling(wakeUpTime / Engine.DeltaTime) + 1) + // yield return Sprite.PlayRoutine("wakeUp");
                     (int) (Math.Ceiling(0.2f / Engine.DeltaTime) + 1);        // yield return 0.2f;
                break;
            }

            case Player.IntroTypes.ThinkForABit: {
                wakeupTime = 1 + // From state-machine coroutine
                     (int) (Math.Ceiling(0.1f / Engine.DeltaTime) + 1) +     // yield return 0.1f;
                     (int) Math.Ceiling((8.0f / 32.0f) / Engine.DeltaTime) + // MoveH(32f * Engine.DeltaTime);
                     (int) (Math.Ceiling(0.3f / Engine.DeltaTime) + 1) +     // yield return 0.3f;
                     (int) (Math.Ceiling(0.8f / Engine.DeltaTime) + 1) +     // yield return 0.8f;
                     (int) (Math.Ceiling(0.1f / Engine.DeltaTime) + 1);      // yield return 0.1f;
                break;
            }

            case Player.IntroTypes.None:
            default:
                wakeupTime = null;
                break;
        }

        if (wakeupTime == null) {
            $"Couldn't determine wakeup time for intro type '{areaData.IntroType}'".Log(LogLevel.Warn);
        }

        return wakeupTime;
    }

    /// Retrieves the starting room for the current level
    public static string GetStartingRoom() {
        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        var mapData = level.Session.MapData;
        return mapData.StartLevel()?.Name ?? string.Empty;
    }

    public static GameState? GetGameState() {
        if (Engine.Scene is not Level level) {
            return null;
        }

        var player = level.Tracker.GetEntity<Player>();

        return new GameState {
            DeltaTime = Engine.DeltaTime,

            Player = new GameState.PlayerState {
                Position = player.Position.ToGameStateVec2(),
                PositionRemainder = player.PositionRemainder.ToGameStateVec2(),
                Speed = player.Speed.ToGameStateVec2(),
                starFlySpeedLerp = player.starFlySpeedLerp,
                OnGround = player.onGround,
                IsHolding = player.Holding != null,
                JumpTimer = player.varJumpTimer,
                AutoJump = player.AutoJump,
                MaxFall = player.maxFall
            },
            Level = new GameState.LevelState {
                Bounds = level.Bounds.ToGameStateRectI(),
                WindDirection = level.Wind.ToGameStateVec2(),
            },

            ChapterTime = GameInfo.GetChapterTime(level),
            RoomName = level.Session.Level,
            PlayerStateName = PlayerStates.GetCurrentStateName(player),
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
