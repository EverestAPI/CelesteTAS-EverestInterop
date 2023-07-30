using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using StudioCommunication;
using TAS.EverestInterop.Hitboxes;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;
using TAS.Utils;

namespace TAS;

public static class GameInfo {
    private static readonly GetDelegate<Level, float> LevelUnpauseTimer = FastReflection.CreateGetDelegate<Level, float>("unpauseTimer");

    public static string Status = string.Empty;
    public static string StatusWithoutTime = string.Empty;
    public static string ExactStatus = string.Empty;
    public static string ExactStatusWithoutTime = string.Empty;
    public static string LevelName = string.Empty;
    public static string ChapterTime = string.Empty;
    public static string WatchingInfo = string.Empty;
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

            if ((TasSettings.InfoWatchEntity & HudOptions.HudOnly) != 0 && WatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(WatchingInfo);
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

            if ((TasSettings.InfoWatchEntity & HudOptions.StudioOnly) != 0 && WatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(WatchingInfo);
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

            WatchingInfo = InfoWatchEntity.GetInfo(alwaysUpdate: true, decimals: CelesteTasSettings.MaxDecimals);
            CustomInfo = InfoCustom.GetInfo(CelesteTasSettings.MaxDecimals);

            if (CustomInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(CustomInfo);
            }

            if (WatchingInfo.IsNotNullOrWhiteSpace()) {
                infos.Add(WatchingInfo);
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
        typeof(Player).GetMethodInfo("DashCoroutine").GetStateMachineTarget().IlHook(PlayerOnDashCoroutine);
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
                   ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString().EndsWith("::<>2__current")
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

        if (Manager.UltraFastForwarding) {
            return;
        }

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

        if (TransitionFrames > 0) {
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

        result += level.NextTransitionDuration.ToCeilingFrames() + 1;

        return result;
    }

    public static void Update(bool updateVel = false) {
        if (Engine.Scene is Level level) {
            Player player = level.GetPlayer();
            if (player != null) {
                string pos = GetAdjustedPos(player, out string exactPos);
                string speed = GetAdjustedSpeed(player.Speed, out string exactSpeed);
                Vector2Double diff = player.GetMoreExactPosition(false) - LastPos;
                if (!Frozen && updateVel) {
                    LastDiff = diff;
                } else {
                    diff = LastDiff;
                }

                if (TasSettings.SpeedUnit == SpeedUnit.PixelPerSecond) {
                    diff *= FramesPerRealSecond;
                }

                string velocity = GetAdjustedVelocity(diff, out string exactVelocity);

                string polarVel = GetAdjustedPolarVel(diff, out string exactPolarVel);

                string analog = string.Empty;
                string exactAnalog = string.Empty;
                if (Manager.Running && Manager.Controller.Previous is { } inputFrame && inputFrame.HasActions(Actions.Feather)) {
                    analog = GetAdjustedAnalog(inputFrame.AngleVector2, out exactAnalog);
                }

                string retainedSpeed = GetAdjustedRetainedSpeed(player, out string exactRetainedSpeed);

                string liftBoost = GetAdjustedLiftBoost(player, out string exactLiftBoost);

                string miscStats = $"Stamina: {player.Stamina:F2} "
                                   + (player.WallJumpCheck(1) ? "Wall-R " : string.Empty)
                                   + (player.WallJumpCheck(-1) ? "Wall-L " : string.Empty)
                                   + PlayerStates.GetStateName(player.StateMachine.State);

                int dashCooldown = player.dashCooldownTimer.ToFloorFrames();

                PlayerSeeker playerSeeker = level.Tracker.GetEntity<PlayerSeeker>();
                if (playerSeeker != null) {
                    pos = GetAdjustedPos(playerSeeker, out exactPos);
                    speed = GetAdjustedSpeed(playerSeeker.speed, out exactSpeed);
                    diff = playerSeeker.GetMoreExactPosition(false) - LastPlayerSeekerPos;
                    if (!Frozen && updateVel) {
                        LastPlayerSeekerDiff = diff;
                    } else {
                        diff = LastPlayerSeekerDiff;
                    }

                    if (TasSettings.SpeedUnit == SpeedUnit.PixelPerSecond) {
                        diff *= FramesPerRealSecond;
                    }

                    velocity = GetAdjustedVelocity(diff, out exactVelocity);

                    polarVel = $"Chase: {diff.Length():F2}, {diff.Angle():F5}°";
                    dashCooldown = playerSeeker.dashTimer.ToCeilingFrames();
                }

                string statuses = GetStatuses(level, player);

                string timers = string.Empty;
                Follower firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry {Golden: false});
                if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                    float collectTimer = firstRedBerry.collectTimer;
                    if (collectTimer <= 0.15f) {
                        int collectFrames = (0.15f - collectTimer).ToCeilingFrames();
                        if (collectTimer >= 0f) {
                            timers += $"Berry({collectFrames}) ";
                        } else {
                            int additionalFrames = Math.Abs(collectTimer).ToCeilingFrames();
                            timers += $"Berry({collectFrames - additionalFrames}+{additionalFrames}) ";
                        }
                    }
                }

                if (dashCooldown > 0) {
                    timers += $"DashCD({dashCooldown}) ";
                }

                if ((FramesPerGameSecond != 60 || SaveData.Instance.Assists.SuperDashing || ExtendedVariantsUtils.SuperDashing) &&
                    DashTime.ToCeilingFrames() >= 1 && player.StateMachine.State == Player.StDash) {
                    DashTime = player.StateMachine.currentCoroutine.waitTimer;
                    timers += $"Dash({DashTime.ToCeilingFrames()}) ";
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

            Status = StatusWithoutTime + $"[{LevelName}] Timer: {ChapterTime}";
            ExactStatus = ExactStatusWithoutTime + $"[{LevelName}] Timer: {ChapterTime}";

            if (TasSettings.InfoHud && (TasSettings.InfoWatchEntity & HudOptions.HudOnly) != 0 ||
                (TasSettings.InfoWatchEntity & HudOptions.StudioOnly) != 0 && StudioCommunicationBase.Initialized) {
                WatchingInfo = InfoWatchEntity.GetInfo();
            } else {
                WatchingInfo = string.Empty;
            }

            if (TasSettings.InfoHud && (TasSettings.InfoCustom & HudOptions.HudOnly) != 0 ||
                (TasSettings.InfoCustom & HudOptions.StudioOnly) != 0 && StudioCommunicationBase.Initialized) {
                CustomInfo = InfoCustom.GetInfo();
            } else {
                CustomInfo = string.Empty;
            }
        } else {
            LevelName = string.Empty;
            ChapterTime = string.Empty;
            WatchingInfo = string.Empty;
            CustomInfo = string.Empty;
            if (Engine.Scene is SummitVignette summit) {
                Status = ExactStatus = $"SummitVignette {summit.ready}";
            } else if (Engine.Scene is Overworld overworld) {
                string ouiName = "";
                if ((overworld.Current ?? overworld.Next) is { } oui) {
                    ouiName = $"{oui.GetType().Name} ";
                }

                Status = ExactStatus = $"Overworld {ouiName}{overworld.ShowInputUI}";
            } else if (Engine.Scene != null) {
                Status = ExactStatus = Engine.Scene.GetType().Name;
            }
        }
    }

    public static string GetStatuses(Level level, Player player) {
        List<string> statuses = new();

        string noControlFrames = TransitionFrames > 0 ? $"({TransitionFrames})" : string.Empty;
        float unpauseTimer = LevelUnpauseTimer?.Invoke(level) ?? 0f;
        if (unpauseTimer > 0f) {
            noControlFrames = $"({(int) Math.Ceiling(unpauseTimer / Engine.RawDeltaTime)})";
        }

        if (Engine.FreezeTimer > 0f) {
            statuses.Add($"Frozen({Engine.FreezeTimer.ToCeilingFrames()})");
        }

        if (player.InControl && !level.Transitioning && unpauseTimer <= 0f) {
            if (player.dashCooldownTimer <= 0 && player.Dashes > 0) {
                statuses.Add("CanDash");
            }

            if (player.jumpGraceTimer.ToFloorFrames() is var coyote and > 0) {
                statuses.Add($"Coyote({coyote})");
            }

            if (player.varJumpTimer.ToFloorFrames() is var jumpTimer and > 0) {
                statuses.Add($"Jump({jumpTimer})");
            }

            if (player.StateMachine.State == Player.StNormal && (player.Speed.Y > 0f || player.Holding is {SlowFall: true})) {
                statuses.Add($"MaxFall({ConvertSpeedUnit(player.maxFall, TasSettings.SpeedUnit):0.##})");
            }

            if (player.forceMoveXTimer.ToCeilingFrames() is var forceMoveXTimer and > 0) {
                string direction = player.forceMoveX switch {
                    > 0 => "R",
                    < 0 => "L",
                    0 => "N"
                };
                statuses.Add($"ForceMove{direction}({forceMoveXTimer})");
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

    private static string GetStatusWithoutTime(string pos, string speed, string velocity, Player player, PlayerSeeker playerSeeker,
        string polarVel, string analog, string retainedSpeed, string liftBoost, string miscStats, string statuses, string timers) {
        StringBuilder builder = new();
        builder.AppendLine(pos);
        builder.AppendLine(speed);
        builder.AppendLine(velocity);

        if (player.StateMachine.State == Player.StStarFly
            || PlayerStates.GetStateName(player.StateMachine.State) == "Custom Feather"
            || playerSeeker != null
            || SaveData.Instance.Assists.ThreeSixtyDashing
            || SaveData.Instance.Assists.SuperDashing) {
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

        if (TasSettings.ShowHitboxes && TasSettings.ShowCycleHitboxColors) {
            builder.AppendLine($"TimeActive: {player.Scene.TimeActive} ({CycleHitboxColor.GroupCounter})");
        }

        return builder.ToString();
    }

    private static int ToCeilingFrames(this float seconds) {
        return (int) Math.Ceiling(seconds / Engine.RawDeltaTime / Engine.TimeRateB);
    }

    private static int ToFloorFrames(this float seconds) {
        return (int) Math.Floor(seconds / Engine.RawDeltaTime / Engine.TimeRateB);
    }

    public static int ConvertToFrames(float seconds) {
        return seconds.ToCeilingFrames();
    }

    private static string GetAdjustedPos(Actor actor, out string exactPos) {
        const string prefix = "Pos:   ";
        exactPos = $"{prefix}{actor.ToSimplePositionString(CelesteTasSettings.MaxDecimals)}";
        return $"{prefix}{actor.ToSimplePositionString(TasSettings.PositionDecimals)}";
    }

    private static string GetAdjustedSpeed(Vector2 speed, out string exactSpeed) {
        speed = ConvertSpeedUnit(speed, TasSettings.SpeedUnit);
        exactSpeed = $"Speed: {speed.ToSimpleString(CelesteTasSettings.MaxDecimals)}";
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
        exactVelocity = $"Vel:   {diff.ToSimpleString(CelesteTasSettings.MaxDecimals)}";
        return $"Vel:   {diff.ToSimpleString(TasSettings.VelocityDecimals)}";
    }

    private static string GetAdjustedPolarVel(Vector2Double diff, out string exactPolarVel) {
        exactPolarVel =
            $"Fly:   {diff.Length().ToFormattedString(CelesteTasSettings.MaxDecimals)}, {diff.Angle().ToFormattedString(CelesteTasSettings.MaxDecimals)}°";
        return
            $"Fly:   {diff.Length().ToFormattedString(TasSettings.VelocityDecimals)}, {diff.Angle().ToFormattedString(TasSettings.AngleDecimals)}°";
    }

    private static string GetAdjustedAnalog(Vector2 angleVector2, out string exactAnalog) {
        exactAnalog =
            $"Analog: {angleVector2.ToSimpleString(CelesteTasSettings.MaxDecimals)}, {GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)).ToFormattedString(CelesteTasSettings.MaxDecimals)}°";
        return
            $"Analog: {angleVector2.ToSimpleString(TasSettings.AngleDecimals)}, {GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)).ToFormattedString(TasSettings.AngleDecimals)}°";
        ;
    }

    private static string GetAdjustedRetainedSpeed(Player player, out string exactRetainedSpeed) {
        if (player.wallSpeedRetentionTimer > 0f) {
            int timer = player.wallSpeedRetentionTimer.ToCeilingFrames();
            float retainedSpeed = ConvertSpeedUnit(player.wallSpeedRetained, TasSettings.SpeedUnit);
            exactRetainedSpeed = $"Retained({timer}): {retainedSpeed.ToString($"F{CelesteTasSettings.MaxDecimals}")}";
            return $"Retained({timer}): {retainedSpeed.ToString($"F{TasSettings.SpeedDecimals}")}";
        } else {
            return exactRetainedSpeed = string.Empty;
        }
    }

    public static string GetAdjustedLiftBoost(Player player, out string exactLiftBoost) {
        if (player.LiftBoost is var liftBoost && liftBoost != Vector2.Zero) {
            liftBoost = ConvertSpeedUnit(liftBoost, TasSettings.SpeedUnit);
            int timer = player.liftSpeedTimer.ToCeilingFrames();
            exactLiftBoost = $"LiftBoost({timer}): {liftBoost.ToSimpleString(CelesteTasSettings.MaxDecimals)}";
            return $"LiftBoost({timer}): {liftBoost.ToSimpleString(TasSettings.SpeedDecimals)}";
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
        return time / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
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

    public static string GetStateName(int state) {
        return States.TryGetValue(state, out string name) ? name : state.ToString();
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

    public override bool Equals(object obj) =>
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