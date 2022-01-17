using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using StudioCommunication;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS {
    public static class GameInfo {
        private static readonly Func<SummitVignette, bool> SummitVignetteReady = "ready".CreateDelegate_Get<SummitVignette, bool>();
        private static readonly DWallJumpCheck WallJumpCheck;
        private static readonly GetBerryFloat StrawberryCollectTimer;
        private static readonly GetFloat DashCooldownTimer;
        private static readonly GetFloat JumpGraceTimer;
        private static readonly GetFloat VarJumpTimer;
        private static readonly GetFloat MaxFall;
        private static readonly GetPlayerSeekerSpeed PlayerSeekerSpeed;
        private static readonly GetPlayerSeekerDashTimer PlayerSeekerDashTimer;
        private static readonly Func<Player, Vector2> PlayerLiftBoost;
        private static readonly GetFloat ActorLiftSpeedTimer;
        private static readonly GetFloat PlayerRetainedSpeed;
        private static readonly GetFloat PlayerRetainedSpeedTimer;
        private static readonly Func<Level, float> LevelUnpauseTimer;
        private static readonly Func<StateMachine, Coroutine> StateMachineCurrentCoroutine;
        private static readonly Func<Coroutine, float> CoroutineWaitTimer;

        private static ILHook dashCoroutineIlHook;

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

        private static int transitionFrames;

        static GameInfo() {
            MethodInfo wallJumpCheck = typeof(Player).GetMethodInfo("WallJumpCheck");
            FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");
            FieldInfo dashCooldownTimer = typeof(Player).GetFieldInfo("dashCooldownTimer");
            FieldInfo jumpGraceTimer = typeof(Player).GetFieldInfo("jumpGraceTimer");
            FieldInfo varJumpTimer = typeof(Player).GetFieldInfo("varJumpTimer");
            FieldInfo maxFall = typeof(Player).GetFieldInfo("maxFall");
            FieldInfo playerSeekerSpeed = typeof(PlayerSeeker).GetFieldInfo("speed");
            FieldInfo playerSeekerDashTimer = typeof(PlayerSeeker).GetFieldInfo("dashTimer");
            MethodInfo playerLiftSpeed = typeof(Player).GetPropertyInfo("LiftBoost").GetGetMethod(true);
            FieldInfo actorLiftSpeedTimer = typeof(Actor).GetFieldInfo("liftSpeedTimer");
            FieldInfo playerRetainedSpeed = typeof(Player).GetFieldInfo("wallSpeedRetained");
            FieldInfo playerRetainedSpeedTimer = typeof(Player).GetFieldInfo("wallSpeedRetentionTimer");
            FieldInfo levelUnpauseTimer = typeof(Level).GetFieldInfo("unpauseTimer");
            FieldInfo currentCoroutine = typeof(StateMachine).GetFieldInfo("currentCoroutine");
            FieldInfo waitTimer = typeof(Coroutine).GetFieldInfo("waitTimer");

            WallJumpCheck = (DWallJumpCheck) wallJumpCheck.CreateDelegate(typeof(DWallJumpCheck));
            StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
            DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
            JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
            VarJumpTimer = varJumpTimer.CreateDelegate_Get<GetFloat>();
            MaxFall = maxFall.CreateDelegate_Get<GetFloat>();
            PlayerSeekerSpeed = playerSeekerSpeed.CreateDelegate_Get<GetPlayerSeekerSpeed>();
            PlayerSeekerDashTimer = playerSeekerDashTimer.CreateDelegate_Get<GetPlayerSeekerDashTimer>();
            PlayerLiftBoost = (Func<Player, Vector2>) playerLiftSpeed.CreateDelegate(typeof(Func<Player, Vector2>));
            ActorLiftSpeedTimer = actorLiftSpeedTimer.CreateDelegate_Get<GetFloat>();
            PlayerRetainedSpeed = playerRetainedSpeed.CreateDelegate_Get<GetFloat>();
            PlayerRetainedSpeedTimer = playerRetainedSpeedTimer.CreateDelegate_Get<GetFloat>();
            LevelUnpauseTimer = levelUnpauseTimer?.CreateDelegate_Get<Func<Level, float>>();
            StateMachineCurrentCoroutine = currentCoroutine.CreateDelegate_Get<Func<StateMachine, Coroutine>>();
            CoroutineWaitTimer = waitTimer.CreateDelegate_Get<Func<Coroutine, float>>();
        }

        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

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

                WatchingInfo = InfoWatchEntity.GetWatchingEntitiesInfo(alwaysUpdate: true, decimals: CelesteTasModuleSettings.MaxDecimals);
                CustomInfo = InfoCustom.Parse(CelesteTasModuleSettings.MaxDecimals);

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
            dashCoroutineIlHook = new ILHook(typeof(Player).GetMethodInfo("DashCoroutine").GetStateMachineTarget(), PlayerOnDashCoroutine);
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Engine.Update -= EngineOnUpdate;
            On.Monocle.Scene.AfterUpdate -= SceneOnAfterUpdate;
            Everest.Events.Level.OnTransitionTo -= LevelOnOnTransitionTo;
            On.Celeste.Level.Update -= LevelOnUpdate;
            dashCoroutineIlHook?.Dispose();
            dashCoroutineIlHook = null;
        }

        private static void PlayerOnDashCoroutine(ILContext il) {
            ILCursor ilCursor = new(il);
            while (ilCursor.TryGotoNext(
                       ins => ins.MatchBox<float>(),
                       ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString().EndsWith("::<>2__current")
                   )) {
                ilCursor.EmitDelegate<Func<float, float>>(dashTime => {
                    DashTime = dashTime;
                    return dashTime;
                });
                ilCursor.Index++;
            }
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

            Update(true);
        }

        private static void LevelOnOnTransitionTo(Level level, LevelData next, Vector2 direction) {
            transitionFrames = GetTransitionFrames(level, next);
        }

        private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            if (transitionFrames > 0) {
                transitionFrames--;
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

                    if (TasSettings.SpeedUnit == SpeedUnit.PixelPerSecond) {
                        diff *= FramesPerRealSecond;
                    }

                    string velocity = GetAdjustedVelocity(diff, out string exactVelocity);

                    string polarVel = $"Fly:   {diff.Length():F2}, {diff.Angle():F5}°";

                    string analog = string.Empty;
                    if (Manager.Running && Manager.Controller.Previous is { } inputFrame && inputFrame.HasActions(Actions.Feather)) {
                        Vector2 angleVector2 = inputFrame.AngleVector2;
                        analog =
                            $"Analog: {angleVector2.X:F5}, {angleVector2.Y:F5}, {GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)):F5}°";
                    }

                    string retainedSpeed = GetAdjustedRetainedSpeed(player, out string exactRetainedSpeed);

                    string liftBoost = GetAdjustedLiftBoost(player, out string exactLiftBoost);

                    string miscStats = $"Stamina: {player.Stamina:0} "
                                       + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                       + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
                                       + PlayerStates.GetStateName(player.StateMachine.State);

                    int dashCooldown = DashCooldownTimer(player).ToFloorFrames();

                    PlayerSeeker playerSeeker = level.Tracker.GetEntity<PlayerSeeker>();
                    if (playerSeeker != null) {
                        pos = GetAdjustedPos(playerSeeker, out exactPos);
                        speed = GetAdjustedSpeed(PlayerSeekerSpeed(playerSeeker), out exactSpeed);
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
                        dashCooldown = PlayerSeekerDashTimer(playerSeeker).ToCeilingFrames();
                    }

                    string statuses = GetStatuses(level, player, dashCooldown);

                    string timers = string.Empty;
                    Follower firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry {Golden: false});
                    if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                        float collectTimer = StrawberryCollectTimer(firstRedBerry);
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

                    if ((FramesPerGameSecond != 60 || SaveData.Instance.Assists.SuperDashing) &&
                        DashTime.ToCeilingFrames() >= 1 && player.StateMachine.State == Player.StDash) {
                        DashTime = CoroutineWaitTimer(StateMachineCurrentCoroutine(player.StateMachine));
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
                        polarVel,
                        analog,
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
                    WatchingInfo = InfoWatchEntity.GetWatchingEntitiesInfo();
                } else {
                    WatchingInfo = string.Empty;
                }

                if (TasSettings.InfoHud && (TasSettings.InfoCustom & HudOptions.HudOnly) != 0 ||
                    (TasSettings.InfoCustom & HudOptions.StudioOnly) != 0 && StudioCommunicationBase.Initialized) {
                    CustomInfo = InfoCustom.Parse();
                } else {
                    CustomInfo = string.Empty;
                }
            } else {
                LevelName = string.Empty;
                ChapterTime = string.Empty;
                WatchingInfo = string.Empty;
                CustomInfo = string.Empty;
                if (Engine.Scene is SummitVignette summit) {
                    Status = ExactStatus = $"SummitVignette {SummitVignetteReady(summit)}";
                } else if (Engine.Scene is Overworld overworld) {
                    Status = ExactStatus = $"Overworld {(overworld.Current ?? overworld.Next).GetType().Name} {overworld.ShowInputUI}";
                } else if (Engine.Scene != null) {
                    Status = ExactStatus = Engine.Scene.GetType().Name;
                }
            }
        }

        public static string GetStatuses(Level level, Player player, int dashCooldown) {
            List<string> statuses = new();

            string noControlFrames = transitionFrames > 0 ? $"({transitionFrames})" : string.Empty;
            float unpauseTimer = LevelUnpauseTimer?.Invoke(level) ?? 0f;
            if (unpauseTimer > 0f) {
                noControlFrames = $"({unpauseTimer.ToCeilingFrames()})";
            }

            if (Engine.FreezeTimer > 0f) {
                statuses.Add($"Frozen({Engine.FreezeTimer.ToCeilingFrames()})");
            }

            if (player.InControl && !level.Transitioning && unpauseTimer <= 0f) {
                if (dashCooldown <= 0 && player.Dashes > 0) {
                    statuses.Add("CanDash");
                }

                if (player.LoseShards) {
                    statuses.Add("Ground");
                } else {
                    if (JumpGraceTimer(player).ToFloorFrames() is var coyote and > 0) {
                        statuses.Add($"Coyote({coyote})");
                    }

                    if (VarJumpTimer(player).ToFloorFrames() is var jumpTimer and > 0) {
                        statuses.Add($"Jump({jumpTimer})");
                    }

                    if (player.StateMachine.State == Player.StNormal && MaxFall(player) is var maxFall &&
                        (player.Speed.Y > 0f || player.Holding is {SlowFall: true})) {
                        statuses.Add($"MaxFall({maxFall:0})");
                    }
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

            return builder.ToString();
        }

        public static float GetDashCooldownTimer(Player player) {
            return DashCooldownTimer(player);
        }

        public static bool GetWallJumpCheck(Player player, int dir) {
            return WallJumpCheck(player, dir);
        }

        public static float GetJumpGraceTimer(Player player) {
            return JumpGraceTimer(player);
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
            string result;

            if (TasSettings.PositionDecimals > 2) {
                result = actor.ToSimplePositionString(TasSettings.PositionDecimals);
            } else {
                Vector2 intPos = actor.Position;
                Vector2 subpixelPos = actor.PositionRemainder;
                double x = intPos.X;
                double y = intPos.Y;
                double subX = subpixelPos.X;
                double subY = subpixelPos.Y;

                // euni: ensure .999/.249 round away from .0/.25
                // .00/.25/.75 let you distinguish which 8th of a pixel you're on, quite handy when doing subpixel manip
                if (Math.Abs(subX) % 0.25 < 0.01 || Math.Abs(subX) % 0.25 > 0.24) {
                    if (x > 0 || x == 0 && subX > 0) {
                        x += Math.Floor(subX * 100) / 100;
                    } else {
                        x += Math.Ceiling(subX * 100) / 100;
                    }
                } else {
                    x += subX;
                }

                if (Math.Abs(subY) % 0.25 < 0.01 || Math.Abs(subY) % 0.25 > 0.24) {
                    if (y > 0 || y == 0 && subY > 0) {
                        y += Math.Floor(subY * 100) / 100;
                    } else {
                        y += Math.Ceiling(subY * 100) / 100;
                    }
                } else {
                    y += subY;
                }

                result = $"{x:F2}, {y:F2}";
            }

            const string prefix = "Pos:   ";
            exactPos = $"{prefix}{actor.ToSimplePositionString(CelesteTasModuleSettings.MaxDecimals)}";
            return $"{prefix}{result}";
        }

        private static string GetAdjustedSpeed(Vector2 speed, out string exactSpeed) {
            speed = ConvertSpeedUnit(speed, TasSettings.SpeedUnit);
            exactSpeed = $"Speed: {speed.ToSimpleString(CelesteTasModuleSettings.MaxDecimals)}";
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
            exactVelocity = $"Vel:   {diff.ToSimpleString(CelesteTasModuleSettings.MaxDecimals)}";
            return $"Vel:   {diff.ToSimpleString(TasSettings.VelocityDecimals)}";
        }

        private static string GetAdjustedRetainedSpeed(Player player, out string exactRetainedSpeed) {
            if (PlayerRetainedSpeedTimer(player) is float retainedSpeedTimer and > 0f) {
                int timer = retainedSpeedTimer.ToCeilingFrames();
                float retainedSpeed = ConvertSpeedUnit(PlayerRetainedSpeed(player), TasSettings.SpeedUnit);
                exactRetainedSpeed = $"Retained({timer}): {retainedSpeed.ToString($"F{CelesteTasModuleSettings.MaxDecimals}")}";
                return $"Retained({timer}): {retainedSpeed.ToString($"F{TasSettings.SpeedDecimals}")}";
            } else {
                return exactRetainedSpeed = string.Empty;
            }
        }

        private static string GetAdjustedLiftBoost(Player player, out string exactLiftBoost) {
            if (PlayerLiftBoost(player) is var liftBoost && liftBoost != Vector2.Zero) {
                liftBoost = ConvertSpeedUnit(liftBoost, TasSettings.SpeedUnit);
                int timer = ActorLiftSpeedTimer(player).ToCeilingFrames();
                exactLiftBoost = $"LiftBoost({timer}): {liftBoost.ToSimpleString(CelesteTasModuleSettings.MaxDecimals)}";
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

        //The things we do for faster replay times
        private delegate bool DWallJumpCheck(Player player, int dir);

        private delegate float GetFloat(Player player);

        private delegate float GetBerryFloat(Strawberry berry);

        private delegate Vector2 GetPlayerSeekerSpeed(PlayerSeeker playerSeeker);

        private delegate float GetPlayerSeekerDashTimer(PlayerSeeker playerSeeker);
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
            return States.ContainsKey(state) ? States[state] : state.ToString();
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
                double round = Math.Round(exactPosition, decimals);

                switch (Math.Abs(remainder)) {
                    case 0.5f:
                        // don't show subsequent zeros when subpixel is exactly equal to 0.5
                        return round.ToString("F1");
                    case < 0.5f: {
                        // make 0.495 round away from 0.50
                        int diffX = (int) position - (int) Math.Round(round, MidpointRounding.AwayFromZero);
                        if (diffX != 0) {
                            round += diffX * Math.Pow(10, -decimals);
                        }

                        break;
                    }
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
}