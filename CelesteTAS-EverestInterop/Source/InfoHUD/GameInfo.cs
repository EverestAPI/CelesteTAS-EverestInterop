using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Gameplay;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Provides detailed information about the current game state
public static class GameInfo {
    public enum Target { InGameHud, Studio, ExactInfo }

    /// Fetches the info for the current frame
    /// May **not** be called during scene.Update! Only before or after Update.
    [PublicAPI]
    public static IEnumerable<string> Query(Target target, bool forceAllowCodeExecution = false) {
#if DEBUG
        if (midUpdate) {
            "Attempted to call GameInfo.Query() during Update!".Log(LogLevel.Error);
            yield return "<ERROR: Attempted to call GameInfo.Query() during Update>";
            yield break;
        }
#endif
        switch (target) {
            case Target.InGameHud: {
                yield return TAS.GameInfo.HudInfo;
                yield return "===";

                // TODO:
                // if (TasSettings.InfoTasInput) {
                //     WriteTasInput(stringBuilder);
                // }

                if (TasSettings.InfoGame && levelStatus.Value is { } status && sessionData.Value is { } session) {
                    yield return $"{status}\n[{session.RoomName}] Timer: {session.ChapterTime}";
                }
                if (InfoMouse.Info.Value is { } infoMouse) {
                    yield return infoMouse;
                }
                if (TasSettings.InfoCustom.Has(HudOptions.HudOnly) && customInfo.Value is { } infoCustom && !string.IsNullOrEmpty(infoCustom)) {
                    yield return infoCustom;
                }

                break;
            }

            case Target.Studio: {
                if (TasSettings.InfoGame && levelStatus.Value is { } status && sessionData.Value is { } session) {
                    yield return $"{status}\n[{session.RoomName}] Timer: {session.ChapterTime}";
                }
                if (InfoMouse.Info.Value is { } infoMouse) {
                    yield return infoMouse;
                }
                if (TasSettings.InfoCustom.Has(HudOptions.StudioOnly) && customInfo.Value is { } infoCustom && !string.IsNullOrEmpty(infoCustom)) {
                    yield return infoCustom;
                }

                break;
            }

            case Target.ExactInfo: {
                if (TasSettings.InfoGame && levelStatusExact.Value is { } status && sessionData.Value is { } session) {
                    yield return $"{status}\n[{session.RoomName}] Timer: {session.ChapterTime}";
                }
                if (InfoMouse.Info.Value is { } infoMouse) {
                    yield return infoMouse;
                }

                string infoCustom = forceAllowCodeExecution
                    ? customInfoExactForceAllowCodeExecution.Value
                    : customInfoExact.Value;
                if (!string.IsNullOrEmpty(infoCustom)) {
                    yield return infoCustom;
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

#if DEBUG
    // Safety check that Query isn't used mid-update
    private static bool midUpdate = false;
#endif

    [Load]
    private static void Load() {
        Everest.Events.Level.OnTransitionTo += OnLevelTransition;
    }

    [Events.PreSceneUpdate]
    private static void PreSceneUpdate(Scene scene) {
#if DEBUG
        midUpdate = true;
#endif

        if (scene is not Level level || level.Paused || level.unpauseTimer > 0.0f) {
            return;
        }

        // Store data which is required for the info, but will change until it is used
        if (level.Tracker.GetEntity<PlayerSeeker>() is { } playerSeeker) {
            preUpdatePosition = SubpixelPosition.FromActor(playerSeeker);
        } else if (level.Tracker.GetEntity<Player>() is { } player) {
            preUpdatePosition = SubpixelPosition.FromActor(player);
        }
    }
    [Events.PostSceneUpdate]
    private static void PostSceneUpdate(Scene scene) {
#if DEBUG
        midUpdate = false;
#endif

        if (scene is Level level) {
            if (!level.wasPaused) {
                // Only update last position after Update, since the level might've been paused
                lastPosition = preUpdatePosition;
            }

            if (transitionFrames > 0) {
                transitionFrames--;
            }
        }

        ResetCache();
    }

    [Events.EngineFrozenUpdate]
    private static void ResetCache() {
        if (Engine.Scene is Level level) {
            // Maintain previous state if player disappeared (e.g. death)
            if (level.Tracker.GetEntity<Player>() is not null) {
                levelStatus.Reset();
                levelStatusExact.Reset();
            }
        } else {
            levelStatus.Reset();
            levelStatusExact.Reset();
        }

        sessionData.Reset();

        if (customInfoTemplateHash != TasSettings.InfoCustomTemplate.GetHashCode()) {
            customInfoComponents.Reset();
        }

        customInfo.Reset();
        customInfoExact.Reset();
        customInfoExactForceAllowCodeExecution.Reset();
    }

    private static void OnLevelTransition(Level level, LevelData next, Vector2 direction) {
        // Calculate frames spent transitioning, to be displayed in the game info
        transitionFrames = 0;
        var session = level.Session;

        bool darkRoom = next.Dark && !session.GetFlag($"ignore_darkness_{next.Name}");

        // Simulate lighting change from level.TransitionRoutine
        float lightingStart = level.Lighting.Alpha;
        float lightingEnd = darkRoom ? session.DarkRoomAlpha : level.BaseLightingAlpha + session.LightingAlphaAdd;
        bool lightingWait = lightingStart >= session.DarkRoomAlpha || lightingEnd >= session.DarkRoomAlpha;

        float lightingCurrent = lightingStart;
        if (lightingWait) {
            while (Math.Abs(lightingCurrent - lightingEnd) > 0.000001f) {
                transitionFrames++;
                lightingCurrent = Calc.Approach(lightingCurrent, lightingEnd, 2.0f * Engine.DeltaTime);
            }
        }

        transitionFrames += level.NextTransitionDuration.ToCeilingFrames() + 1;
    }

    // Caches of calculations for the current frame
    private static LazyValue<string?> levelStatus = new(QueryDisplayLevelStatus);
    private static LazyValue<string?> levelStatusExact = new(QueryExactLevelStatus);
    private static LazyValue<(string RoomName, string ChapterTime)?> sessionData = new(QuerySessionData);

    private static int customInfoTemplateHash = int.MaxValue;
    private static LazyValue<InfoCustom.TemplateComponent[]> customInfoComponents = new(QueryCustomInfoComponents);

    private static LazyValue<string> customInfo = new(QueryDisplayCustomInfo);
    private static LazyValue<string> customInfoExact = new(QueryExactCustomInfo);
    private static LazyValue<string> customInfoExactForceAllowCodeExecution = new(QueryExactCustomInfoForceAllowCodeExecution);

    // Data which needs to be tracked, because it'll be used in the next frame
    private static SubpixelPosition preUpdatePosition, lastPosition;
    private static int transitionFrames;

    // Kept to reduce allocations
    private static readonly StringBuilder builder = new();

    private static string? QueryDisplayLevelStatus() => QueryLevelStatus(exact: false);
    private static string? QueryExactLevelStatus() => QueryLevelStatus(exact: true);

    private static string? QueryLevelStatus(bool exact) {
        if (Engine.Scene is not Level level) {
            return null;
        }

        int speedDecimals = exact ? GameSettings.MaxDecimals : TasSettings.SpeedDecimals;

        builder.Clear();
        if (level.Tracker.GetEntity<PlayerSeeker>() is { } playerSeeker) {
            builder.AppendLine(FormatPosition(playerSeeker, exact));
            builder.AppendLine(FormatSpeed(playerSeeker.speed, exact));
            builder.AppendLine(FormatVelocity(SubpixelPosition.FromActor(playerSeeker) - lastPosition, exact));
        } else if (level.Tracker.GetEntity<Player>() is { } player) {
            var positionDelta = SubpixelPosition.FromActor(player) - lastPosition;

            builder.AppendLine(FormatPosition(player, exact));
            builder.AppendLine(FormatSpeed(player.Speed, exact));
            builder.AppendLine(FormatVelocity(positionDelta, exact));

            if (player.StateMachine.State == Player.StStarFly
                || SaveData.Instance.Assists.ThreeSixtyDashing
                || SaveData.Instance.Assists.SuperDashing
                || ExtendedVariantsInterop.SuperDashing
            ) {
                builder.AppendLine(FormatPolarVelocity(positionDelta, exact));
            }

            if (Manager.Running && Manager.Controller.Previous is { } previous && EnumExtensions.Has(previous.Actions, Actions.Feather)) {
                builder.AppendLine(FormatAnalog(previous.StickPosition, exact));
            }

            builder.Append($"Stamina: {player.Stamina:F2} ");
            if (player.WallJumpCheck(+1)) { builder.Append("Wall-R "); }
            if (player.WallJumpCheck(-1)) { builder.Append("Wall-L "); }
            builder.AppendLine(FormatState(player.StateMachine));

            if (player.wallSpeedRetentionTimer > 0f) {
                builder.AppendLine($"Retained({player.wallSpeedRetentionTimer.ToCeilingFrames()}): {ConvertSpeedUnit(player.wallSpeedRetained, TasSettings.SpeedUnit).FormatValue(speedDecimals)}");
            }
            if (player.LiftBoost is var liftBoost && liftBoost != Vector2.Zero) {
                builder.AppendLine($"LiftBoost({player.liftSpeedTimer.ToCeilingFrames()}): {ConvertSpeedUnit(liftBoost, TasSettings.SpeedUnit).FormatValue(speedDecimals)}");
            }

            // Statuses
            {
                if (Engine.FreezeTimer > 0.0f) {
                    builder.Append($"Frozen({Math.Ceiling(Engine.FreezeTimer / Engine.RawDeltaTime)}) ");
                }

                if (transitionFrames > 0) {
                    builder.Append($"NoControl({transitionFrames}) ");
                } else if (level.unpauseTimer > 0.0f) {
                    builder.Append($"NoControl({Math.Ceiling(level.unpauseTimer / Engine.RawDeltaTime)}) ");
                } else if (!player.InControl || level.Transitioning) {
                    builder.Append("NoControl ");
                } else {
                    if (player.dashCooldownTimer <= 0.0f && player.Dashes > 0) {
                        builder.Append("CanDash ");
                    }

                    if (player.jumpGraceTimer.ToFloorFrames() is var coyote and > 0) {
                        builder.Append($"Coyote({coyote}) ");
                    }

                    if (player.varJumpTimer.ToFloorFrames() is var jumpTimer and > 0) {
                        builder.Append($"Jump({jumpTimer}) ");
                    }

                    if (player.StateMachine.State == Player.StNormal && (player.Speed.Y > 0f || player.Holding is {SlowFall: true})) {
                        builder.Append($"MaxFall({ConvertSpeedUnit(player.maxFall, TasSettings.SpeedUnit):0.##}) ");
                    }

                    if (player.forceMoveXTimer.ToCeilingFrames() is var forceMoveXTimer and > 0) {
                        string direction = player.forceMoveX switch {
                            > 0 => "R",
                            < 0 => "L",
                              0 => "N"
                        };
                        builder.Append($"ForceMove{direction}({forceMoveXTimer}) ");
                    }
                }

                if (level.Wipe is { } wipe && !level.InCutscene && player.InControl) {
                    builder.Append($"CantPause({Math.Ceiling(((1.0f - wipe.Percent) * wipe.Duration + wipe.EndTimer) / Engine.RawDeltaTime) + (!wipe.Completed ? 1 : 0) + 1}) ");
                }

                if (player.Dead) {
                    builder.Append("Dead ");
                }

                if (level.InCutscene) {
                    builder.Append("Cutscene ");
                }

                if (player.Holding == null && level.Tracker.GetComponents<Holdable>().Any(holdable => ((Holdable)holdable).Check(player))) {
                    builder.Append("Grab ");
                }

                builder.AppendLine();
            }

            // Timers
            {
                var firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry { Golden: false });
                if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                    float collectTimer = firstRedBerry.collectTimer;
                    if (collectTimer <= 0.15f) {
                        int collectFrames = (0.15f - collectTimer).ToCeilingFrames();
                        if (collectTimer >= 0.0f) {
                            builder.Append($"Berry({collectFrames}) ");
                        } else {
                            int additionalFrames = Math.Abs(collectTimer).ToCeilingFrames();
                            builder.Append($"Berry({collectFrames - additionalFrames}+{additionalFrames}) ");
                        }
                    }
                }

                if (player.dashCooldownTimer.ToFloorFrames() is var dashCooldown and > 0) {
                    builder.Append($"DashCD({dashCooldown}) ");
                }

                // Only display when dash time isn't the default 15f
                if (((Engine.RawDeltaTime / Engine.TimeRateB).SecondsToTicks() != 166667L || SaveData.Instance.Assists.SuperDashing || ExtendedVariantsInterop.SuperDashing)
                    && player.StateMachine.State == Player.StDash && player.StateMachine.currentCoroutine.waitTimer.ToCeilingFrames() is var dashFrames and > 0
                ) {
                    builder.Append($"Dash({dashFrames}) ");
                }

                builder.AppendLine();
            }
        } else {
            return null;
        }

        return builder.TrimEnd().ToString();
    }

    private static (string RoomName, string ChapterTime)? QuerySessionData() {
        if (Engine.Scene is Level level) {
            return (level.Session.Level, FormatTime(level.Session.Time));
        }

        return null;
    }

    private static InfoCustom.TemplateComponent[] QueryCustomInfoComponents() {
        customInfoTemplateHash = TasSettings.InfoCustomTemplate.GetHashCode();
        return InfoCustom.ParseTemplate(TasSettings.InfoCustomTemplate);
    }

    private static string QueryDisplayCustomInfo() => InfoCustom.EvaluateTemplate(customInfoComponents.Value, TasSettings.CustomInfoDecimals);
    private static string QueryExactCustomInfo() => InfoCustom.EvaluateTemplate(customInfoComponents.Value, GameSettings.MaxDecimals);
    private static string QueryExactCustomInfoForceAllowCodeExecution() => InfoCustom.EvaluateTemplate(customInfoComponents.Value, GameSettings.MaxDecimals, forceAllowCodeExecution: true);

    private const string PositionPrefix      = "Pos:   ";
    private const string SpeedPrefix         = "Speed: ";
    private const string VelocityPrefix      = "Vel:   ";
    private const string PolarVelocityPrefix = "Fly:   ";

    private static string FormatPosition(Entity entity, bool exact) {
        return exact
            ? $"{PositionPrefix}{entity.ToSimplePositionString(GameSettings.MaxDecimals)}"
            : $"{PositionPrefix}{entity.ToSimplePositionString(TasSettings.PositionDecimals)}";
    }
    private static string FormatSpeed(Vector2 speed, bool exact) {
        speed = ConvertSpeedUnit(speed, TasSettings.SpeedUnit);

        return exact
            ? $"{SpeedPrefix}{speed.FormatValue(GameSettings.MaxDecimals)}"
            : $"{SpeedPrefix}{speed.FormatValue(TasSettings.SpeedDecimals)}";
    }
    private static string FormatVelocity(SubpixelPosition delta, bool exact) {
        if (TasSettings.VelocityUnit == SpeedUnit.PixelPerSecond) {
            delta /= Engine.RawDeltaTime;
        }

        return exact
            ? $"{VelocityPrefix}{delta.FormatValue(GameSettings.MaxDecimals, subpixelRounding: false)}"
            : $"{VelocityPrefix}{delta.FormatValue(TasSettings.VelocityDecimals, subpixelRounding: false)}";
    }
    private static string FormatPolarVelocity(SubpixelPosition delta, bool exact) {
        if (TasSettings.VelocityUnit == SpeedUnit.PixelPerSecond) {
            delta /= Engine.RawDeltaTime;
        }

        float  length = delta.Exact.Length();
        double angle  = Math.Atan2(delta.X.Exact, -delta.Y.Exact).Mod(Calc.Circle) * Calc.RadToDeg;

        return exact
            ? $"{PolarVelocityPrefix}{length.FormatValue(GameSettings.MaxDecimals)}, {(length == 0.0f ? "N/A" : angle.FormatValue(GameSettings.MaxDecimals))}°"
            : $"{PolarVelocityPrefix}{length.FormatValue(TasSettings.VelocityDecimals)}, {(length == 0.0f ? "N/A" : angle.FormatValue(TasSettings.AngleDecimals))}°";
    }
    private static string FormatAnalog(Vector2 analog, bool exact) {
        double angle = Math.Atan2(analog.X, analog.Y).Mod(Calc.Circle) * Calc.RadToDeg;

        return exact
            ? $"Analog: {analog.FormatValue(GameSettings.MaxDecimals)}, {(analog.LengthSquared() == 0.0f ? "N/A" : angle.FormatValue(GameSettings.MaxDecimals))}°"
            : $"Analog: {analog.FormatValue(TasSettings.AngleDecimals)}, {(analog.LengthSquared() == 0.0f ? "N/A" : angle.FormatValue(TasSettings.AngleDecimals))}°";
    }
    private static string FormatState(StateMachine stateMachine) {
        string name = stateMachine.GetCurrentStateName();

        // Ensure "St" prefix
        if (!name.StartsWith("St")) {
            name = $"St{name}";
        }

        return name;
    }
    public static string FormatTime(long ticks) {
        var timeSpan = TimeSpan.FromTicks(ticks);
        long frames = ticks / Engine.RawDeltaTime.SecondsToTicks();

        return $"{timeSpan.ShortGameplayFormat()}({frames})";
    }

    private static float ConvertSpeedUnit(float speed, SpeedUnit unit) {
        return unit == SpeedUnit.PixelPerSecond
            ? speed * Engine.TimeRateB
            : speed * Engine.RawDeltaTime * Engine.TimeRateB;
    }
    private static Vector2 ConvertSpeedUnit(Vector2 speed, SpeedUnit unit) {
        return unit == SpeedUnit.PixelPerSecond
            ? speed * Engine.TimeRateB
            : speed * Engine.RawDeltaTime * Engine.TimeRateB;
    }

    private static int ToCeilingFrames(this float seconds) => (int) Math.Ceiling(seconds / Engine.RawDeltaTime / Engine.TimeRateB);
    private static int ToFloorFrames(this float seconds) => (int) Math.Floor(seconds / Engine.RawDeltaTime / Engine.TimeRateB);
}
