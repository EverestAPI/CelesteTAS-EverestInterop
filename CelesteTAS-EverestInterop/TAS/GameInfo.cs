using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Utils;

namespace TAS {
    public static class GameInfo {
        private static readonly FieldInfo SummitVignetteReadyFieldInfo = typeof(SummitVignette).GetFieldInfo("ready");

        private static readonly DWallJumpCheck WallJumpCheck;
        private static readonly GetBerryFloat StrawberryCollectTimer;
        private static readonly GetFloat DashCooldownTimer;
        private static readonly GetFloat JumpGraceTimer;
        private static readonly GetPlayerSeekerSpeed PlayerSeekerSpeed;
        private static readonly GetPlayerSeekerDashTimer PlayerSeekerDashTimer;
        private static readonly Func<Player, Vector2> PlayerLiftBoost;
        private static readonly GetFloat ActorLiftSpeedTimer;
        private static readonly GetFloat PlayerRetainedSpeed;
        private static readonly GetFloat PlayerRetainedSpeedTimer;
        private static readonly Func<Level, float> LevelUnpauseTimer;

        public static string Status = string.Empty;
        public static string StatusWithoutTime = string.Empty;
        public static string LastVel = string.Empty;
        public static string CustomInfo = string.Empty;
        public static long LastChapterTime;
        public static Vector2 LastPos;
        public static Vector2 LastPlayerSeekerPos;

        private static StreamWriter sw;
        private static List<MethodInfo> trackedEntities;

        private static int transitionFrames;

        //for debugging
        public static string AdditionalStatusInfo;

        static GameInfo() {
            MethodInfo wallJumpCheck = typeof(Player).GetMethodInfo("WallJumpCheck");
            FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");
            FieldInfo dashCooldownTimer = typeof(Player).GetFieldInfo("dashCooldownTimer");
            FieldInfo jumpGraceTimer = typeof(Player).GetFieldInfo("jumpGraceTimer");
            FieldInfo playerSeekerSpeed = typeof(PlayerSeeker).GetFieldInfo("speed");
            FieldInfo playerSeekerDashTimer = typeof(PlayerSeeker).GetFieldInfo("dashTimer");
            MethodInfo playerLiftSpeed = typeof(Player).GetPropertyInfo("LiftBoost").GetGetMethod(true);
            FieldInfo actorLiftSpeedTimer = typeof(Actor).GetFieldInfo("liftSpeedTimer");
            FieldInfo playerRetainedSpeed = typeof(Player).GetFieldInfo("wallSpeedRetained");
            FieldInfo playerRetainedSpeedTimer = typeof(Player).GetFieldInfo("wallSpeedRetentionTimer");
            FieldInfo levelUnpauseTimer = typeof(Level).GetFieldInfo("unpauseTimer");

            WallJumpCheck = (DWallJumpCheck) wallJumpCheck.CreateDelegate(typeof(DWallJumpCheck));
            StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
            DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
            JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
            PlayerSeekerSpeed = playerSeekerSpeed.CreateDelegate_Get<GetPlayerSeekerSpeed>();
            PlayerSeekerDashTimer = playerSeekerDashTimer.CreateDelegate_Get<GetPlayerSeekerDashTimer>();
            PlayerLiftBoost = (Func<Player, Vector2>) playerLiftSpeed.CreateDelegate(typeof(Func<Player, Vector2>));
            ActorLiftSpeedTimer = actorLiftSpeedTimer.CreateDelegate_Get<GetFloat>();
            PlayerRetainedSpeed = playerRetainedSpeed.CreateDelegate_Get<GetFloat>();
            PlayerRetainedSpeedTimer = playerRetainedSpeedTimer.CreateDelegate_Get<GetFloat>();
            LevelUnpauseTimer = levelUnpauseTimer?.CreateDelegate_Get<Func<Level, float>>();
        }

        private static float FramesPerSecond => 60f / Engine.TimeRateB;

        public static bool ExportSyncData { get; private set; }

        [Load]
        private static void Load() {
            On.Monocle.Engine.Update += EngineOnUpdate;
            On.Monocle.Scene.AfterUpdate += SceneOnAfterUpdate;
            Everest.Events.Level.OnTransitionTo += LevelOnOnTransitionTo;
            On.Celeste.Level.Update += LevelOnUpdate;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Engine.Update -= EngineOnUpdate;
            On.Monocle.Scene.AfterUpdate -= SceneOnAfterUpdate;
            Everest.Events.Level.OnTransitionTo -= LevelOnOnTransitionTo;
            On.Celeste.Level.Update -= LevelOnUpdate;
        }

        private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
            bool frozen = Engine.FreezeTimer > 0;
            orig(self, gameTime);
            if (frozen) {
                Update();
            }
        }

        private static void SceneOnAfterUpdate(On.Monocle.Scene.orig_AfterUpdate orig, Scene self) {
            orig(self);
            Update();
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

            result += (int) (level.NextTransitionDuration * FramesPerSecond) + 2;

            return result;
        }

        public static void Update() {
            if (Engine.Scene is Level level) {
                Player player = level.Tracker.GetEntity<Player>();
                long chapterTime = level.Session.Time;
                if (player != null) {
                    StringBuilder stringBuilder = new StringBuilder();
                    string pos = GetAdjustedPos(player.Position, player.PositionRemainder);
                    string speed = GetAdjustedSpeed(player.Speed);
                    Vector2 diff = (player.ExactPosition - LastPos) * 60f;
                    string vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                    if (chapterTime == LastChapterTime) {
                        vel = LastVel;
                    } else {
                        LastVel = vel;
                    }

                    string polarVel = $"Fly:   {diff.Length():F2}, {Manager.GetAngle(diff):F5}°";

                    string joystick = string.Empty;
                    if (Manager.Running && Manager.Controller.Previous is InputFrame inputFrame && inputFrame.HasActions(Actions.Feather)) {
                        Vector2 angleVector2 = inputFrame.AngleVector2;
                        joystick =
                            $"Analog: {angleVector2.X:F5}, {angleVector2.Y:F5}, {Manager.GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)):F5}°";
                    }

                    string retainedSpeed = string.Empty;
                    if (PlayerRetainedSpeedTimer(player) is float retainedSpeedTimer && retainedSpeedTimer > 0f) {
                        retainedSpeed = $"Retained: {PlayerRetainedSpeed(player):F2} ({PlayerRetainedSpeedTimer(player) * FramesPerSecond:F0})";
                    }

                    string liftBoost = string.Empty;
                    if (PlayerLiftBoost(player) is Vector2 liftBoostVector2 && liftBoostVector2 != Vector2.Zero) {
                        liftBoost =
                            $"LiftBoost: {liftBoostVector2.X:F2}, {liftBoostVector2.Y:F2} ({ActorLiftSpeedTimer(player) * FramesPerSecond:F0})";
                    }

                    string miscStats = $"Stamina: {player.Stamina:0} "
                                       + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                       + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
                                       + (PlayerState) player.StateMachine.State;

                    int dashCooldown = (int) (DashCooldownTimer(player) * FramesPerSecond);

                    PlayerSeeker playerSeeker = level.Tracker.GetEntity<PlayerSeeker>();
                    if (playerSeeker != null) {
                        pos = GetAdjustedPos(playerSeeker.Position, playerSeeker.PositionRemainder);
                        speed =
                            $"Speed: {PlayerSeekerSpeed(playerSeeker).X:F2}, {PlayerSeekerSpeed(playerSeeker).Y:F2}";
                        diff = (playerSeeker.ExactPosition - LastPlayerSeekerPos) * 60f;
                        vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                        polarVel = $"Chase: {diff.Length():F2}, {Manager.GetAngle(diff):F2}°";
                        dashCooldown = (int) (PlayerSeekerDashTimer(playerSeeker) * FramesPerSecond);
                    }

                    string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
                                      + (player.LoseShards ? "Ground " : string.Empty)
                                      + (!player.LoseShards && JumpGraceTimer(player) > 0
                                          ? $"Coyote({(int) (JumpGraceTimer(player) * FramesPerSecond)})"
                                          : string.Empty);

                    string noControlFrames = transitionFrames > 0 ? $"({transitionFrames})" : string.Empty;
                    float unpauseTimer = LevelUnpauseTimer?.Invoke(level) ?? 0f;
                    if (unpauseTimer > 0f) {
                        noControlFrames = $"({unpauseTimer * FramesPerSecond:F0})";
                    }

                    statuses = (Engine.FreezeTimer > 0f ? $"Frozen({Engine.FreezeTimer * FramesPerSecond:F0}) " : string.Empty)
                               + (player.InControl && !level.Transitioning && unpauseTimer <= 0f ? statuses : $"NoControl{noControlFrames} ")
                               + (player.Dead ? "Dead " : string.Empty)
                               + (level.InCutscene ? "Cutscene " : string.Empty)
                               + (AdditionalStatusInfo ?? string.Empty);

                    if (player.Holding == null
                        && level.Tracker.GetComponents<Holdable>().Any(holdable => ((Holdable) holdable).Check(player))) {
                        statuses += "Grab ";
                    }

                    int berryTimer = -10;
                    Follower firstRedBerryFollower =
                        player.Leader.Followers.Find(follower => follower.Entity is Strawberry berry && !berry.Golden);
                    if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                        berryTimer = 9 - (int) Math.Round(StrawberryCollectTimer(firstRedBerry) * FramesPerSecond);
                    }

                    string timers = (berryTimer != -10
                                        ? berryTimer <= 9 ? $"BerryTimer: {berryTimer} " : $"BerryTimer: 9+{berryTimer - 9} "
                                        : string.Empty)
                                    + (dashCooldown != 0 ? $"DashTimer: {(dashCooldown).ToString()} " : string.Empty);

                    stringBuilder.AppendLine(pos);
                    stringBuilder.AppendLine(speed);
                    stringBuilder.AppendLine(vel);

                    if (player.StateMachine.State == Player.StStarFly
                        || playerSeeker != null
                        || SaveData.Instance.Assists.ThreeSixtyDashing
                        || SaveData.Instance.Assists.SuperDashing) {
                        stringBuilder.AppendLine(polarVel);
                    }

                    if (!string.IsNullOrEmpty(joystick)) {
                        stringBuilder.AppendLine(joystick);
                    }

                    if (!string.IsNullOrEmpty(retainedSpeed)) {
                        stringBuilder.AppendLine(retainedSpeed);
                    }

                    if (!string.IsNullOrEmpty(liftBoost)) {
                        stringBuilder.AppendLine(liftBoost);
                    }

                    stringBuilder.AppendLine(miscStats);
                    if (!string.IsNullOrEmpty(statuses)) {
                        stringBuilder.AppendLine(statuses);
                    }

                    if (!string.IsNullOrEmpty(timers)) {
                        stringBuilder.AppendLine(timers);
                    }

                    if (Manager.FrameLoops == 1) {
                        string inspectingInfo = InfoInspectEntity.GetInspectingEntitiesInfo();
                        if (inspectingInfo.IsNotNullOrEmpty()) {
                            stringBuilder.AppendLine(inspectingInfo);
                        }

                        CustomInfo = InfoCustom.Parse();
                        if (CustomInfo.IsNotEmpty()) {
                            stringBuilder.AppendLine(CustomInfo);
                        }
                    }

                    StatusWithoutTime = stringBuilder.ToString();
                    if (Engine.FreezeTimer <= 0f) {
                        LastPos = player.ExactPosition;
                        LastPlayerSeekerPos = playerSeeker?.ExactPosition ?? default;
                    }
                } else if (level.InCutscene) {
                    StatusWithoutTime = "Cutscene";
                }

                string roomNameAndTime =
                    $"[{level.Session.Level}] Timer: {(chapterTime / 10000000D):F3}({chapterTime / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks})";
                Status = StatusWithoutTime + roomNameAndTime;
                LastChapterTime = chapterTime;
            } else if (Engine.Scene is SummitVignette summit) {
                Status = "SummitVignette " + SummitVignetteReadyFieldInfo.GetValue(summit);
            } else if (Engine.Scene is Overworld overworld) {
                Status = "Overworld " + overworld.ShowInputUI;
            } else if (Engine.Scene != null) {
                Status = Engine.Scene.GetType().Name;
            }
        }

        private static string GetAdjustedPos(Vector2 intPos, Vector2 subpixelPos) {
            double x = intPos.X;
            double y = intPos.Y;
            double subX = subpixelPos.X;
            double subY = subpixelPos.Y;

            if (!CelesteTasModule.Settings.RoundPosition) {
                return $"Pos:   {(x + subX):F12}, {(y + subY):F12}";
            }

            if (Math.Abs(subX) % 0.25 < 0.01 || Math.Abs(subX) % 0.25 > 0.24) {
                if (x > 0 || (x == 0 && subX > 0)) {
                    x += Math.Floor(subX * 100) / 100;
                } else {
                    x += Math.Ceiling(subX * 100) / 100;
                }
            } else {
                x += subX;
            }

            if (Math.Abs(subY) % 0.25 < 0.01 || Math.Abs(subY) % 0.25 > 0.24) {
                if (y > 0 || (y == 0 && subY > 0)) {
                    y += Math.Floor(subY * 100) / 100;
                } else {
                    y += Math.Ceiling(subY * 100) / 100;
                }
            } else {
                y += subY;
            }

            string pos = $"Pos:   {x:F2}, {y:F2}";
            return pos;
        }

        private static string GetAdjustedSpeed(Vector2 speed) {
            return "Speed: " + (CelesteTasModule.Settings.RoundSpeed ? $"{speed.X:F2}, {speed.Y:F2}" : $"{speed.X:F10}, {speed.Y:F10}");
        }

        private static void BeginExport(string path, string[] tracked) {
            sw?.Dispose();
            sw = new StreamWriter(path);
            sw.WriteLine(string.Join("\t", "Line", "Inputs", "Frames", "Time", "Position", "Speed", "State", "Statuses", "Entities"));
            trackedEntities = new List<MethodInfo>();
            foreach (string typeName in tracked) {
                string fullTypeName = typeName.Contains("@") ? typeName.Replace("@", ",") : $"Celeste.{typeName}, Celeste";
                Type t = Type.GetType(fullTypeName);
                if (t != null) {
                    trackedEntities.Add(typeof(EntityList).GetMethod("FindAll")?.MakeGenericMethod(t));
                }
            }
        }

        public static void EndExport() {
            ExportSyncData = false;
            Engine.Scene.OnEndOfFrame += () => { sw?.Dispose(); };
        }

        public static void ExportPlayerInfo() {
            Engine.Scene.OnEndOfFrame += () => {
                InputController controller = Manager.Controller;
                if (Engine.Scene is Level level) {
                    Player player = level.Tracker.GetEntity<Player>();
                    if (player == null) {
                        return;
                    }

                    string time = (level.Session.Time / 10000000D).ToString("0.000");
                    double x = (double) player.X + player.PositionRemainder.X;
                    double y = (double) player.Y + player.PositionRemainder.Y;
                    string pos = x + ", " + y;
                    string speed = player.Speed.X + ", " + player.Speed.Y;

                    int dashCooldown = (int) (DashCooldownTimer(player) * FramesPerSecond);
                    string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
                                      + (player.LoseShards ? "Ground " : string.Empty)
                                      + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                      + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
                                      + (!player.LoseShards && JumpGraceTimer(player) > 0 ? "Coyote " : string.Empty);
                    statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
                               + (player.TimePaused ? "Paused " : string.Empty)
                               + (level.InCutscene ? "Cutscene " : string.Empty)
                               + (AdditionalStatusInfo ?? string.Empty);

                    if (player.Holding == null) {
                        foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
                            Holdable holdable = (Holdable) component;
                            if (holdable.Check(player)) {
                                statuses += "Grab ";
                                break;
                            }
                        }
                    }

                    string output = string.Empty;
                    if (controller.CurrentFrame > 0 && controller.Inputs.Count > 0) {
                        output = string.Join("\t",
                            controller.Previous.Line, controller.Previous, controller.CurrentFrame - 1, time, pos, speed,
                            (PlayerState) player.StateMachine.State,
                            statuses);
                    }

                    foreach (MethodInfo method in trackedEntities) {
                        if (method == null) {
                            continue;
                        }

                        IList entities = (IList) method.Invoke(level.Entities, null);
                        foreach (Entity entity in entities) {
                            if (entity is Actor actor) {
                                x = (double) actor.X + actor.PositionRemainder.X;
                                y = (double) actor.Y + actor.PositionRemainder.Y;
                                pos = x + ", " + y;
                            } else {
                                pos = entity.X + ", " + entity.Y;
                            }

                            output += $"\t{method.GetGenericArguments()[0].Name}: {pos}";
                        }
                    }

                    output += InfoInspectEntity.GetInspectingEntitiesInfo("\t");

                    sw.WriteLine(output);
                } else {
                    string inputs = controller.Current.ToString();
                    if (inputs.Length > 1) {
                        inputs = inputs.Substring(1);
                    }

                    string output = string.Join(" ", inputs, controller.CurrentFrame, Engine.Scene.GetType().Name);
                    sw.WriteLine(output);
                }
            };
        }

        // ReSharper disable once UnusedMember.Local
        // "StartExport",
        // "StartExport Path",
        // "StartExport EntitiesToTrack",
        // "StartExport Path EntitiesToTrack"
        [TasCommand(Name = "StartExport")]
        private static void StartExportCommand(string[] args) {
            string path = "dump.txt";
            if (args.Length > 0) {
                if (args[0].Contains(".")) {
                    path = args[0];
                    args = args.Skip(1).ToArray();
                }
            }

            BeginExport(path, args);
            ExportSyncData = true;
        }

        // ReSharper disable once UnusedMember.Local
        [TasCommand(Name = "FinishExport")]
        private static void FinishExportCommand(string[] args) {
            EndExport();
            ExportSyncData = false;
        }

        //The things we do for faster replay times
        private delegate bool DWallJumpCheck(Player player, int dir);

        private delegate float GetBerryFloat(Strawberry berry);

        private delegate float GetFloat(Player player);

        private delegate Vector2 GetPlayerSeekerSpeed(PlayerSeeker playerSeeker);

        private delegate float GetPlayerSeekerDashTimer(PlayerSeeker playerSeeker);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum PlayerState {
        StNormal = Player.StNormal,
        StClimb = Player.StClimb,
        StDash = Player.StDash,
        StSwim = Player.StSwim,
        StBoost = Player.StBoost,
        StRedDash = Player.StRedDash,
        StHitSquash = Player.StHitSquash,
        StLaunch = Player.StLaunch,
        StPickup = Player.StPickup,
        StDreamDash = Player.StDreamDash,
        StSummitLaunch = Player.StSummitLaunch,
        StDummy = Player.StDummy,
        StIntroWalk = Player.StIntroWalk,
        StIntroJump = Player.StIntroJump,
        StIntroRespawn = Player.StIntroRespawn,
        StIntroWakeUp = Player.StIntroWakeUp,
        StBirdDashTutorial = Player.StBirdDashTutorial,
        StFrozen = Player.StFrozen,
        StReflectionFall = Player.StReflectionFall,
        StStarFly = Player.StStarFly,
        StTempleFall = Player.StTempleFall,
        StCassetteFly = Player.StCassetteFly,
        StAttract = Player.StAttract,
        StIntroMoonJump = Player.StIntroMoonJump,
        StFlingBird = Player.StFlingBird,
        StIntroThinkForABit = Player.StIntroThinkForABit
    }
}