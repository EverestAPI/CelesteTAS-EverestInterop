using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Celeste.Mod;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using EnumExtensions = TAS.Utils.EnumExtensions;

namespace TAS;

public static class GameInfo {
    private static readonly GetDelegate<Level, float>? LevelUnpauseTimer = FastReflection.CreateGetDelegate<Level, float>("unpauseTimer");

    public static string Status = string.Empty;
    public static string StatusWithoutTime = string.Empty;
    public static string ExactStatus = string.Empty;
    public static string ExactStatusWithoutTime = string.Empty;
    public static string LevelName = string.Empty;
    public static string ChapterTime = string.Empty;
    public static string HudWatchingInfo = string.Empty;
    public static string StudioWatchingInfo = string.Empty;
    public static string CustomInfo = string.Empty;
    public static Vector2Double LastDiff;
    public static Vector2Double LastPos;
    public static Vector2Double LastPlayerSeekerDiff;
    public static Vector2Double LastPlayerSeekerPos;
    public static float DashTime;
    public static bool Frozen;
    public static int TransitionFrames;

    public static string HudInfo {
        get {
            List<string> infos = new();
            if (TasSettings.InfoGame && Status.IsNotNullOrWhiteSpace()) {
                infos.Add(Status);
            }

            if (InfoMouse.MouseInfo.IsNotEmpty()) {
                infos.Add(InfoMouse.MouseInfo);
            }

            if ((TasSettings.InfoCustom & HudOptions.HudOnly) != 0 && CustomInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(CustomInfo);
            }

            if (TasSettings.HudWatchEntity && HudWatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(HudWatchingInfo);
            }

            return string.Join("\n\n", infos);
        }
    }

    public static string StudioInfo {
        get {
            List<string> infos = new() {Status};

            if (InfoMouse.MouseInfo.IsNotEmpty()) {
                infos.Add(InfoMouse.MouseInfo);
            }

            if ((TasSettings.InfoCustom & HudOptions.StudioOnly) != 0 && CustomInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(CustomInfo);
            }

            if (TasSettings.StudioWatchEntity && StudioWatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(StudioWatchingInfo);
            }

            return string.Join("\n\n", infos);
        }
    }

    public static string ExactStudioInfo {
        get {
            List<string> infos = new() {ExactStatus};

            if (InfoMouse.MouseInfo.IsNotEmpty()) {
                infos.Add(InfoMouse.MouseInfo);
            }

            StudioWatchingInfo = InfoWatchEntity.GetInfo(TasSettings.InfoWatchEntityStudioType, alwaysUpdate: true, decimals: GameSettings.MaxDecimals);
            CustomInfo = InfoCustom.GetInfo(GameSettings.MaxDecimals);

            if (CustomInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(CustomInfo);
            }

            if (StudioWatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(StudioWatchingInfo);
            }

            return string.Join("\n\n", infos);
        }
    }

    public static string ExactStudioInfoAllowCodeExecution {
        get {
            List<string> infos = new() {ExactStatus};

            if (InfoMouse.MouseInfo.IsNotEmpty()) {
                infos.Add(InfoMouse.MouseInfo);
            }

            StudioWatchingInfo = InfoWatchEntity.GetInfo(TasSettings.InfoWatchEntityStudioType, alwaysUpdate: true, decimals: GameSettings.MaxDecimals);
            CustomInfo = InfoCustom.GetInfo(GameSettings.MaxDecimals, forceAllowCodeExecution: true);

            if (CustomInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(CustomInfo);
            }

            if (StudioWatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(StudioWatchingInfo);
            }

            return string.Join("\n\n", infos);
        }
    }

    private static int FramesPerGameSecond => (int) Math.Round(1 / Engine.RawDeltaTime / Engine.TimeRateB);
    private static int FramesPerRealSecond => (int) Math.Round(1 / Engine.RawDeltaTime);

    [Load]
    private static void Load() {
        On.Monocle.Engine.Update += EngineOnUpdate;
        On.Monocle.Scene.AfterUpdate += SceneOnAfterUpdate;
        Everest.Events.Level.OnTransitionTo += LevelOnOnTransitionTo;
        On.Celeste.Level.Update += LevelOnUpdate;
        typeof(Player)
            .GetMethodInfo(nameof(Player.DashCoroutine))!
            .GetStateMachineTarget()!
            .IlHook(PlayerOnDashCoroutine);
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= EngineOnUpdate;
        On.Monocle.Scene.AfterUpdate -= SceneOnAfterUpdate;
        Everest.Events.Level.OnTransitionTo -= LevelOnOnTransitionTo;
        On.Celeste.Level.Update -= LevelOnUpdate;
    }

    private static void PlayerOnDashCoroutine(ILContext il) {
        ILCursor ilCursor = new(il);
        while (ilCursor.TryGotoNext(
                   ins => ins.MatchBox<float>(),
                   ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString()!.EndsWith("::<>2__current")
               )) {
            ilCursor.EmitDelegate<Func<float, float>>(SetDashTime);
            ilCursor.Index++;
        }
    }

    private static float SetDashTime(float dashTime) {
        DashTime = dashTime;
        return dashTime;
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        Frozen = Engine.FreezeTimer > 0;
        orig(self, gameTime);
        if (Frozen) {
            Update();
        }
    }

    private static void SceneOnAfterUpdate(On.Monocle.Scene.orig_AfterUpdate orig, Scene self) {
        orig(self);

        // TODO: While fast forwarding, only store required data for frame and compute string later

        if (self is Level level) {
            Update(!level.wasPaused);
        } else {
            Update();
        }
    }

    private static void LevelOnOnTransitionTo(Level level, LevelData next, Vector2 direction) {
        TransitionFrames = GetTransitionFrames(level, next);
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        if (TransitionFrames > 0 && TransitionFrames != int.MaxValue) {
            TransitionFrames--;
        }
    }

    private static int GetTransitionFrames(Level level, LevelData nextLevelData) {
        int result = 0;
        Session session = level.Session;

        bool darkRoom = nextLevelData.Dark && !session.GetFlag("ignore_darkness_" + nextLevelData.Name);

        float lightingStart = level.Lighting.Alpha;
        float lightingCurrent = lightingStart;
        float lightingEnd = darkRoom ? session.DarkRoomAlpha : level.BaseLightingAlpha + session.LightingAlphaAdd;
        bool lightingWait = lightingStart >= session.DarkRoomAlpha || lightingEnd >= session.DarkRoomAlpha;
        if (lightingWait) {
            while (Math.Abs(lightingCurrent - lightingEnd) > 0.000001f) {
                result++;
                lightingCurrent = Calc.Approach(lightingCurrent, lightingEnd, 2f * Engine.DeltaTime);
            }
        }

        if (level.NextTransitionDuration.ToCeilingFrames() is var transitionDuration && transitionDuration == int.MaxValue) {
            return int.MaxValue;
        }
        result += transitionDuration + 1;

        return result;
    }

    public static void Update(bool updateVel = false) {
        if (TasHelperInterop.InPrediction) {
            return;
        }
        Scene scene = Engine.Scene;

        // Dynamically show real-time / game-time timer
        bool showRealTime = MetadataCommands.RealTimeInfo != null && Manager.Controller.Commands.Values
            .SelectMany(commands => commands)
            .Any(command => command.Is("RealTime") || command.Is("MidwayRealTime"));
        bool showGameTime = !showRealTime || Manager.Controller.Commands.Values
            .SelectMany(commands => commands)
            .Any(command => command.Is("FileTime") || command.Is("ChapterTime") || command.Is("MidwayFileTime") || command.Is("MidwayChapterTime"));

        if (scene is Level level) {
            Player player = level.Tracker.GetEntity<Player>();
            if (player != null) {
                string pos = GetAdjustedPos(player, out string exactPos);
                string speed = GetAdjustedSpeed(player.Speed, out string exactSpeed);
                Vector2Double diff = player.GetMoreExactPosition(false) - LastPos;
                if (!Frozen && updateVel) {
                    LastDiff = diff;
                } else {
                    diff = LastDiff;
                }

                if (TasSettings.VelocityUnit == SpeedUnit.PixelPerSecond) {
                    diff *= FramesPerRealSecond;
                }

                string velocity = GetAdjustedVelocity(diff, out string exactVelocity);

                string polarVel = GetAdjustedPolarVel(diff, out string exactPolarVel);

                string analog = string.Empty;
                string exactAnalog = string.Empty;
                if (Manager.Running && Manager.Controller.Previous is { } inputFrame && EnumExtensions.Has(inputFrame.Actions, Actions.Feather)) {
                    analog = GetAdjustedAnalog(inputFrame.StickPosition, out exactAnalog);
                }

                string retainedSpeed = GetAdjustedRetainedSpeed(player, out string exactRetainedSpeed);

                string liftBoost = GetAdjustedLiftBoost(player, out string exactLiftBoost);

                string miscStats = $"Stamina: {player.Stamina:F2} "
                                   + (player.WallJumpCheck(1) ? "Wall-R " : string.Empty)
                                   + (player.WallJumpCheck(-1) ? "Wall-L " : string.Empty)
                                   + PlayerStates.GetCurrentStateName(player);

                int dashCooldown = player.dashCooldownTimer.ToFloorFrames();

                PlayerSeeker? playerSeeker = level.Tracker.GetEntityTrackIfNeeded<PlayerSeeker>();
                if (playerSeeker != null) {
                    pos = GetAdjustedPos(playerSeeker, out exactPos);
                    speed = GetAdjustedSpeed(playerSeeker.speed, out exactSpeed);
                    diff = playerSeeker.GetMoreExactPosition(false) - LastPlayerSeekerPos;
                    if (!Frozen && updateVel) {
                        LastPlayerSeekerDiff = diff;
                    } else {
                        diff = LastPlayerSeekerDiff;
                    }

                    if (TasSettings.VelocityUnit == SpeedUnit.PixelPerSecond) {
                        diff *= FramesPerRealSecond;
                    }

                    velocity = GetAdjustedVelocity(diff, out exactVelocity);

                    polarVel = $"Chase: {diff.Length():F2}, {diff.Angle():F5}°";
                    dashCooldown = playerSeeker.dashTimer.ToCeilingFrames();
                }

                string statuses = GetStatuses(level, player);

                string timers = string.Empty;
                Follower? firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry {Golden: false});
                if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                    float collectTimer = firstRedBerry.collectTimer;
                    if (collectTimer <= 0.15f) {
                        int collectFrames = (0.15f - collectTimer).ToCeilingFrames();
                        if (collectTimer >= 0f) {
                            timers += $"Berry({collectFrames.FormatFrames()}) ";
                        } else {
                            int additionalFrames = Math.Abs(collectTimer).ToCeilingFrames();
                            timers += $"Berry({collectFrames - additionalFrames}+{additionalFrames.FormatFrames()}) ";
                        }
                    }
                }

                if (dashCooldown > 0) {
                    timers += $"DashCD({dashCooldown.FormatFrames()}) ";
                }

                if ((FramesPerGameSecond != 60 || SaveData.Instance.Assists.SuperDashing || ExtendedVariantsInterop.SuperDashing) &&
                    DashTime.ToCeilingFrames() >= 1 && player.StateMachine.State == Player.StDash) {
                    DashTime = player.StateMachine.currentCoroutine.waitTimer;
                    timers += $"Dash({DashTime.ToCeilingFrames().FormatFrames()}) ";
                }

                if (player.StateMachine.State != Player.StDash) {
                    DashTime = 0f;
                }

                StatusWithoutTime = GetStatusWithoutTime(
                    pos,
                    speed,
                    velocity,
                    player,
                    playerSeeker,
                    polarVel,
                    analog,
                    retainedSpeed,
                    liftBoost,
                    miscStats,
                    statuses,
                    timers
                );

                ExactStatusWithoutTime = GetStatusWithoutTime(
                    exactPos,
                    exactSpeed,
                    exactVelocity,
                    player,
                    playerSeeker,
                    exactPolarVel,
                    exactAnalog,
                    exactRetainedSpeed,
                    exactLiftBoost,
                    miscStats,
                    statuses,
                    timers
                );

                if (Engine.FreezeTimer <= 0f) {
                    LastPos = player.GetMoreExactPosition(false);
                    LastPlayerSeekerPos = playerSeeker?.GetMoreExactPosition(false) ?? default;
                }
            } else if (level.InCutscene) {
                StatusWithoutTime = "Cutscene";
            }

            LevelName = level.Session.Level;
            ChapterTime = GetChapterTime(level);

            string timer = "";
            if (showGameTime && !showRealTime) {
                timer = $"[{LevelName}] Timer: {ChapterTime}";
            } else if (!showGameTime && showRealTime) {
                int realTimeFrames = MetadataCommands.RealTimeInfo!.Value.FrameCount;
                timer = $"[{LevelName}] Real Timer: {TimeSpan.FromSeconds(realTimeFrames / 60.0f).ShortGameplayFormat()}({realTimeFrames})";
            } else if (showGameTime && showRealTime) {
                int realTimeFrames = MetadataCommands.RealTimeInfo!.Value.FrameCount;
                timer = $"[{LevelName}] Game Timer: {ChapterTime}\n{new string(' ', LevelName.Length + 3)}Real Timer: {TimeSpan.FromSeconds(realTimeFrames / 60.0f).ShortGameplayFormat()}({realTimeFrames})";
            }

            Status = StatusWithoutTime + timer;
            ExactStatus = ExactStatusWithoutTime + timer;
            UpdateAdditionInfo();
        } else if (scene is Emulator {game: { } game} emulator) {
            StringBuilder stringBuilder = new();
            Classic.player? player = emulator.game.objects.FirstOrDefault(o => o is Classic.player) as Classic.player;
            if (player != null) {
                stringBuilder.AppendLine($"Pos:   {player.x}, {player.y}");
                stringBuilder.AppendLine($"Rem:   {player.rem.ToSimpleString(TasSettings.PositionDecimals)}");
                stringBuilder.AppendLine($"Speed: {player.spd.ToSimpleString(TasSettings.SpeedDecimals)}");
            }

            stringBuilder.AppendLine($"Seed:  {Pico8Fixer.Seed}");
            if (player?.grace > 1) {
                stringBuilder.AppendLine($"Coyote({player.grace - 1})");
            }

            LevelName = $"[Level{game.level_index() + 1} X:{game.room.X} Y:{game.room.Y}]";
            ChapterTime = $"{game.minutes}:{game.seconds.ToString().PadLeft(2, '0')}{(Pico8Fixer.Frames / 30.0).ToString($"F3").TrimStart('0')}";
            Status = ExactStatus = $"{stringBuilder}{LevelName} Timer: {ChapterTime}";
            UpdateAdditionInfo();
        } else {
            LevelName = string.Empty;
            ChapterTime = string.Empty;
            HudWatchingInfo = string.Empty;
            StudioWatchingInfo = string.Empty;
            CustomInfo = string.Empty;
            if (scene is SummitVignette summit) {
                Status = ExactStatus = $"SummitVignette {summit.ready}";
            } else if (scene is Overworld overworld) {
                string ouiName = "";
                if ((overworld.Current ?? overworld.Next) is { } oui) {
                    ouiName = $"{oui.GetType().Name} ";
                }

                Status = ExactStatus = ouiName;
            } else if (scene != null) {
                Status = ExactStatus = scene.GetType().Name;
            }

            if (showRealTime) {
                int realTimeFrames = MetadataCommands.RealTimeInfo!.Value.FrameCount;
                Status += $"\n\nReal Timer: {TimeSpan.FromSeconds(realTimeFrames / 60.0f).ShortGameplayFormat()}({realTimeFrames})";
            }
        }
    }

    private static void UpdateAdditionInfo() {
        InfoWatchEntity.UpdateInfo();

        if (TasSettings.InfoHud && (TasSettings.InfoCustom & HudOptions.HudOnly) != 0 ||
            (TasSettings.InfoCustom & HudOptions.StudioOnly) != 0 && CommunicationWrapper.Connected) {
            CustomInfo = InfoCustom.GetInfo();
        } else {
            CustomInfo = string.Empty;
        }
    }

    public static string GetStatuses(Level level, Player player) {
        List<string> statuses = new();

        string noControlFrames = TransitionFrames > 0 ? $"({TransitionFrames.FormatFrames()})" : string.Empty;
        float unpauseTimer = LevelUnpauseTimer?.Invoke(level) ?? 0f;
        if (unpauseTimer > 0f) {
            noControlFrames = $"({(int) Math.Ceiling(unpauseTimer / Engine.RawDeltaTime)})";
        }

        if (Engine.FreezeTimer > 0f) {
            statuses.Add($"Frozen({(int) Math.Ceiling(Engine.FreezeTimer / Engine.RawDeltaTime)})");
        }

        if (player.InControl && !level.Transitioning && unpauseTimer <= 0f) {
            if (player.dashCooldownTimer <= 0 && player.Dashes > 0) {
                statuses.Add("CanDash");
            }

            if (player.jumpGraceTimer.ToFloorFrames() is var coyote and > 0) {
                statuses.Add($"Coyote({coyote.FormatFrames()})");
            }

            if (player.varJumpTimer.ToFloorFrames() is var jumpTimer and > 0) {
                statuses.Add($"Jump({jumpTimer.FormatFrames()})");
            }

            if (player.StateMachine.State == Player.StNormal && (player.Speed.Y > 0f || player.Holding is {SlowFall: true})) {
                statuses.Add($"MaxFall({ConvertSpeedUnit(player.maxFall, TasSettings.SpeedUnit):0.##})");
            }

            if (player.forceMoveXTimer.ToCeilingFrames() is var forceMoveXTimer and > 0) {
                string direction = player.forceMoveX switch {
                    > 0 => "R",
                    < 0 => "L",
                    0 => "N",
                };
                statuses.Add($"ForceMove{direction}({forceMoveXTimer.FormatFrames()})");
            }
        } else {
            statuses.Add($"NoControl{noControlFrames}");
        }

        if (level.Wipe != null && !level.InCutscene && player.InControl) {
            statuses.Add("CantPause");
        }

        if (player.Dead) {
            statuses.Add("Dead");
        }

        if (level.InCutscene) {
            statuses.Add("Cutscene");
        }

        if (player.Holding == null && level.Tracker.GetCastComponents<Holdable>().Any(holdable => holdable.Check(player))) {
            statuses.Add("Grab");
        }

        return string.Join(" ", statuses);
    }

    private static string GetStatusWithoutTime(string pos, string speed, string velocity, Player player, PlayerSeeker? playerSeeker,
        string polarVel, string analog, string retainedSpeed, string liftBoost, string miscStats, string statuses, string timers) {
        StringBuilder builder = new();
        builder.AppendLine(pos);
        builder.AppendLine(speed);
        builder.AppendLine(velocity);

        if (player.StateMachine.State == Player.StStarFly
            || PlayerStates.GetCurrentStateName(player) == "Custom Feather"
            || playerSeeker != null
            || SaveData.Instance.Assists.ThreeSixtyDashing
            || SaveData.Instance.Assists.SuperDashing
            || ExtendedVariantsInterop.SuperDashing) {
            builder.AppendLine(polarVel);
        }

        if (!string.IsNullOrEmpty(analog)) {
            builder.AppendLine(analog);
        }

        if (!string.IsNullOrEmpty(retainedSpeed)) {
            builder.AppendLine(retainedSpeed);
        }

        if (!string.IsNullOrEmpty(liftBoost)) {
            builder.AppendLine(liftBoost);
        }

        builder.AppendLine(miscStats);
        if (!string.IsNullOrEmpty(statuses)) {
            builder.AppendLine(statuses);
        }

        if (!string.IsNullOrEmpty(timers)) {
            builder.AppendLine(timers);
        }

        return builder.ToString();
    }

    internal static int ToCeilingFrames(this float timer) {
        float frames = MathF.Ceiling(timer / Engine.DeltaTime);
        return float.IsInfinity(frames) || float.IsNaN(frames) ? int.MaxValue : (int) frames;
    }
    public static int ToFloorFrames(this float timer) {
        float frames = MathF.Floor(timer / Engine.DeltaTime);
        return float.IsInfinity(frames) || float.IsNaN(frames) ? int.MaxValue : (int) frames;
    }
    internal static string FormatFrames(this int frames) {
        return frames == int.MaxValue ? "\u221e" : frames.ToString();
    }

    private static string GetAdjustedPos(Actor actor, out string exactPos) {
        const string prefix = "Pos:   ";
        exactPos = $"{prefix}{actor.ToSimplePositionString(GameSettings.MaxDecimals)}";
        return $"{prefix}{actor.ToSimplePositionString(TasSettings.PositionDecimals)}";
    }

    private static string GetAdjustedSpeed(Vector2 speed, out string exactSpeed) {
        speed = ConvertSpeedUnit(speed, TasSettings.SpeedUnit);
        exactSpeed = $"Speed: {speed.ToSimpleString(GameSettings.MaxDecimals)}";
        return $"Speed: {speed.ToSimpleString(TasSettings.SpeedDecimals)}";
    }

    public static Vector2 ConvertSpeedUnit(Vector2 speed, SpeedUnit speedUnit) {
        if (speedUnit == SpeedUnit.PixelPerFrame) {
            speed /= FramesPerGameSecond;
        } else {
            speed *= Engine.TimeRateB;
        }

        return speed;
    }

    public static float ConvertSpeedUnit(float speed, SpeedUnit speedUnit) {
        if (speedUnit == SpeedUnit.PixelPerFrame) {
            speed /= FramesPerGameSecond;
        } else {
            speed *= Engine.TimeRateB;
        }

        return speed;
    }

    private static string GetAdjustedVelocity(Vector2Double diff, out string exactVelocity) {
        exactVelocity = $"Vel:   {diff.ToSimpleString(GameSettings.MaxDecimals)}";
        return $"Vel:   {diff.ToSimpleString(TasSettings.VelocityDecimals)}";
    }

    private static string GetAdjustedPolarVel(Vector2Double diff, out string exactPolarVel) {
        exactPolarVel =
            $"Fly:   {diff.Length().ToFormattedString(GameSettings.MaxDecimals)}, {diff.Angle().ToFormattedString(GameSettings.MaxDecimals)}°";
        return
            $"Fly:   {diff.Length().ToFormattedString(TasSettings.VelocityDecimals)}, {diff.Angle().ToFormattedString(TasSettings.AngleDecimals)}°";
    }

    private static string GetAdjustedAnalog(Vector2 angleVector2, out string exactAnalog) {
        exactAnalog =
            $"Analog: {angleVector2.ToSimpleString(GameSettings.MaxDecimals)}, {GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)).ToFormattedString(GameSettings.MaxDecimals)}°";
        return
            $"Analog: {angleVector2.ToSimpleString(TasSettings.AngleDecimals)}, {GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)).ToFormattedString(TasSettings.AngleDecimals)}°";
    }

    private static string GetAdjustedRetainedSpeed(Player player, out string exactRetainedSpeed) {
        if (player.wallSpeedRetentionTimer > 0f) {
            int timer = player.wallSpeedRetentionTimer.ToCeilingFrames();
            float retainedSpeed = ConvertSpeedUnit(player.wallSpeedRetained, TasSettings.SpeedUnit);
            exactRetainedSpeed = $"Retained({timer.FormatFrames()}): {retainedSpeed.ToString($"F{GameSettings.MaxDecimals}")}";
            return $"Retained({timer.FormatFrames()}): {retainedSpeed.ToString($"F{TasSettings.SpeedDecimals}")}";
        } else {
            return exactRetainedSpeed = string.Empty;
        }
    }

    public static string GetAdjustedLiftBoost(Player player, out string exactLiftBoost) {
        if (player.LiftBoost is var liftBoost && liftBoost != Vector2.Zero) {
            liftBoost = ConvertSpeedUnit(liftBoost, TasSettings.SpeedUnit);
            int timer = player.liftSpeedTimer.ToCeilingFrames();
            exactLiftBoost = $"LiftBoost({timer.FormatFrames()}): {liftBoost.ToSimpleString(GameSettings.MaxDecimals)}";
            return $"LiftBoost({timer.FormatFrames()}): {liftBoost.ToSimpleString(TasSettings.SpeedDecimals)}";
        } else {
            return exactLiftBoost = string.Empty;
        }
    }

    public static string GetChapterTime(Level level) {
        return FormatTime(level.Session.Time);
    }

    public static string FormatTime(long time) {
        TimeSpan timeSpan = TimeSpan.FromTicks(time);
        return $"{timeSpan.ShortGameplayFormat()}({ConvertMicroSecondToFrames(time)})";
    }

    private static long ConvertMicroSecondToFrames(long time) {
        return time / Engine.RawDeltaTime.SecondsToTicks();
    }

    private static double GetAngle(Vector2 vector) {
        double angle = 180 / Math.PI * Math.Atan2(vector.Y, vector.X);
        if (angle < -90.01f) {
            return 450 + angle;
        } else {
            return 90 + angle;
        }
    }
}

public static class PlayerStates {
    private static readonly IDictionary<int, string> States = new Dictionary<int, string> {
        {Player.StNormal, nameof(Player.StNormal)},
        {Player.StClimb, nameof(Player.StClimb)},
        {Player.StDash, nameof(Player.StDash)},
        {Player.StSwim, nameof(Player.StSwim)},
        {Player.StBoost, nameof(Player.StBoost)},
        {Player.StRedDash, nameof(Player.StRedDash)},
        {Player.StHitSquash, nameof(Player.StHitSquash)},
        {Player.StLaunch, nameof(Player.StLaunch)},
        {Player.StPickup, nameof(Player.StPickup)},
        {Player.StDreamDash, nameof(Player.StDreamDash)},
        {Player.StSummitLaunch, nameof(Player.StSummitLaunch)},
        {Player.StDummy, nameof(Player.StDummy)},
        {Player.StIntroWalk, nameof(Player.StIntroWalk)},
        {Player.StIntroJump, nameof(Player.StIntroJump)},
        {Player.StIntroRespawn, nameof(Player.StIntroRespawn)},
        {Player.StIntroWakeUp, nameof(Player.StIntroWakeUp)},
        {Player.StBirdDashTutorial, nameof(Player.StBirdDashTutorial)},
        {Player.StFrozen, nameof(Player.StFrozen)},
        {Player.StReflectionFall, nameof(Player.StReflectionFall)},
        {Player.StStarFly, nameof(Player.StStarFly)},
        {Player.StTempleFall, nameof(Player.StTempleFall)},
        {Player.StCassetteFly, nameof(Player.StCassetteFly)},
        {Player.StAttract, nameof(Player.StAttract)},
        {Player.StIntroMoonJump, nameof(Player.StIntroMoonJump)},
        {Player.StFlingBird, nameof(Player.StFlingBird)},
        {Player.StIntroThinkForABit, nameof(Player.StIntroThinkForABit)},
    };

    [Obsolete("GetStateName(int) is deprecated, please use GetCurrentStateName(Player) instead.")]
    public static string GetStateName(int state) {
        return States.TryGetValue(state, out string? name) ? name : state.ToString();
    }

    public static string GetCurrentStateName(Player player) {
        if (!States.TryGetValue(player.StateMachine.state, out string? name)) {
            name = player.StateMachine.GetCurrentStateName();
        }

        // Ensure "St" prefix
        if (!name.StartsWith("St")) {
            name = $"St{name}";
        }

        return name;
    }

    // ReSharper disable once UnusedMember.Global
    public static void Register(int state, string stateName) {
        States[state] = stateName;
    }

    // ReSharper disable once UnusedMember.Global
    public static bool Unregister(int state) {
        return States.Remove(state);
    }
}

// ReSharper disable once StructCanBeMadeReadOnly
public struct Vector2Double {
    public Vector2 Position;
    public Vector2 PositionRemainder;
    public bool SubpixelRounding;

    public double X {
        get => (double) Position.X + PositionRemainder.X;
        set {
            Position.X = (int) Math.Round(value);
            PositionRemainder.X = (float) (value - Position.X);
        }
    }

    public double Y {
        get => (double) Position.Y + PositionRemainder.Y;
        set {
            Position.Y = (int) Math.Round(value);
            PositionRemainder.Y = (float) (value - Position.Y);
        }
    }

    public Vector2Double(Vector2 position, Vector2 positionRemainder, bool subpixelRounding) {
        Position = position;
        PositionRemainder = positionRemainder;
        SubpixelRounding = subpixelRounding;
    }

    public override bool Equals(object? obj) =>
        obj is Vector2Double other && Position == other.Position && PositionRemainder == other.PositionRemainder;

    public override int GetHashCode() => ToString().GetHashCode();

    public override string ToString() => "{X:" + X + " Y:" + Y + "}";

    public string ToSimpleString(int decimals) {
        if (SubpixelRounding) {
            return ToExactPositionString(decimals);
        } else {
            return $"{X.ToFormattedString(decimals)}, {Y.ToFormattedString(decimals)}";
        }
    }

    private string ToExactPositionString(int decimals) {
        string RoundPosition(double exactPosition, float position, float remainder) {
            if (decimals == 0) {
                return exactPosition.ToFormattedString(decimals);
            }

            double round = Math.Round(exactPosition, decimals);

            switch (Math.Abs(remainder)) {
                case 0.5f:
                    // don't show subsequent zeros when subpixel is exactly equal to 0.5
                    return round.ToString("F1");
                case < 0.5f:
                    // make 0.495 round away from 0.50
                    int diffX = (int) position - (int) Math.Round(round, MidpointRounding.AwayFromZero);
                    if (diffX != 0) {
                        round += diffX * Math.Pow(10, -decimals);
                    }

                    break;
            }

            return round.ToFormattedString(decimals);
        }

        string resultX = RoundPosition(X, Position.X, PositionRemainder.X);
        string resultY = RoundPosition(Y, Position.Y, PositionRemainder.Y);
        return $"{resultX}, {resultY}";
    }

    public double Length() => Math.Sqrt(X * X + Y * Y);

    public double Angle() {
        double angle = 180 / Math.PI * Math.Atan2(Y, X);
        if (angle < -90.01) {
            return 450 + angle;
        } else {
            return 90 + angle;
        }
    }

    public static bool operator ==(Vector2Double value1, Vector2Double value2) => value1.X == value2.X && value1.Y == value2.Y;

    public static bool operator !=(Vector2Double value1, Vector2Double value2) => !(value1 == value2);

    public static Vector2Double operator +(Vector2Double value1, Vector2Double value2) {
        return new(value1.Position + value2.Position, value1.PositionRemainder + value2.PositionRemainder, value1.SubpixelRounding);
    }

    public static Vector2Double operator -(Vector2Double value1, Vector2Double value2) {
        return new(value1.Position - value2.Position, value1.PositionRemainder - value2.PositionRemainder, value1.SubpixelRounding);
    }

    public static Vector2Double operator *(Vector2Double value, float scaleFactor) {
        return new(value.Position * scaleFactor, value.PositionRemainder * scaleFactor, value.SubpixelRounding);
    }

    public static Vector2Double operator /(Vector2Double value, float scaleFactor) {
        return value * (1 / scaleFactor);
    }
}
