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
using TAS.Input;
using TAS.Utils;

namespace TAS {
    public static class PlayerInfo {
        private static readonly FieldInfo SummitVignetteReadyFieldInfo = typeof(SummitVignette).GetFieldInfo("ready");
        private static readonly FieldInfo StrawberryCollectTimerFieldInfo = typeof(Strawberry).GetFieldInfo("collectTimer");

        private static readonly DWallJumpCheck WallJumpCheck;
        private static readonly GetBerryFloat StrawberryCollectTimer;
        private static readonly GetFloat DashCooldownTimer;
        private static readonly GetFloat JumpGraceTimer;
        private static readonly GetPlayerSeekerSpeed PlayerSeekerSpeed;
        private static readonly GetPlayerSeekerDashTimer PlayerSeekerDashTimer;

        public static string Status = string.Empty;
        public static Vector2 LastPos;
        public static Vector2 LastPlayerSeekerPos;
        private static string statusWithoutTime = string.Empty;

        private static StreamWriter sw;
        private static List<MethodInfo> trackedEntities;

        private static int transitionFrames;

        //for debugging
        public static string AdditionalStatusInfo;

        static PlayerInfo() {
            MethodInfo wallJumpCheck = typeof(Player).GetMethodInfo("WallJumpCheck");
            FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");
            FieldInfo dashCooldownTimer = typeof(Player).GetFieldInfo("dashCooldownTimer");
            FieldInfo jumpGraceTimer = typeof(Player).GetFieldInfo("jumpGraceTimer");
            FieldInfo playerSeekerSpeed = typeof(PlayerSeeker).GetFieldInfo("speed");
            FieldInfo playerSeekerDashTimer = typeof(PlayerSeeker).GetFieldInfo("dashTimer");

            WallJumpCheck = (DWallJumpCheck) wallJumpCheck.CreateDelegate(typeof(DWallJumpCheck));
            StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
            DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
            JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
            PlayerSeekerSpeed = playerSeekerSpeed.CreateDelegate_Get<GetPlayerSeekerSpeed>();
            PlayerSeekerDashTimer = playerSeekerDashTimer.CreateDelegate_Get<GetPlayerSeekerDashTimer>();
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

        private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gametime) {
            bool frozen = Engine.FreezeTimer > 0;
            orig(self, gametime);
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
                    string speed = $"Speed: {player.Speed.X:F2}, {player.Speed.Y:F2}";
                    Vector2 diff = (player.ExactPosition - LastPos) * 60f;
                    string vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                    string polarvel = $"Fly:   {diff.Length():F2}, {Manager.GetAngle(diff):F5}°";

                    string joystick;
                    if (Manager.Running && Manager.Controller.Previous is InputFrame inputFrame && inputFrame.HasActions(Actions.Feather)) {
                        Vector2 angleVector2 = inputFrame.AngleVector2;
                        joystick =
                            $"Analog: {angleVector2.X:F5}, {angleVector2.Y:F5}, {Manager.GetAngle(new Vector2(angleVector2.X, -angleVector2.Y)):F5}°";
                    } else {
                        joystick = string.Empty;
                    }

                    string miscstats = $"Stamina: {player.Stamina:0}  "
                                       + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                       + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty);
                    int dashCooldown = (int) (DashCooldownTimer(player) * FramesPerSecond);

                    PlayerSeeker playerSeeker = level.Entities.FindFirst<PlayerSeeker>();
                    if (playerSeeker != null) {
                        pos = GetAdjustedPos(playerSeeker.Position, playerSeeker.PositionRemainder);
                        speed =
                            $"Speed: {PlayerSeekerSpeed(playerSeeker).X:F2}, {PlayerSeekerSpeed(playerSeeker).Y:F2}";
                        diff = (playerSeeker.ExactPosition - LastPlayerSeekerPos) * 60f;
                        vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                        polarvel = $"Chase: {diff.Length():F2}, {Manager.GetAngle(diff):F2}°";
                        dashCooldown = (int) (PlayerSeekerDashTimer(playerSeeker) * FramesPerSecond);
                    }

                    string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
                                      + (player.LoseShards ? "Ground " : string.Empty)
                                      + (!player.LoseShards && JumpGraceTimer(player) > 0
                                          ? $"Coyote({JumpGraceTimer(player) * FramesPerSecond:F0})"
                                          : string.Empty);
                    string transitionFramesStr = transitionFrames > 0 ? $"({transitionFrames})" : string.Empty;
                    statuses = (Engine.FreezeTimer > 0f ? $"Frozen({Engine.FreezeTimer * FramesPerSecond:F0}) " : string.Empty)
                               + (player.InControl && !level.Transitioning ? statuses : $"NoControl{transitionFramesStr} ")
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
                        stringBuilder.AppendLine(polarvel);
                    }

                    if (!string.IsNullOrEmpty(joystick)) {
                        stringBuilder.AppendLine(joystick);
                    }

                    stringBuilder.AppendLine(miscstats);
                    if (!string.IsNullOrEmpty(statuses)) {
                        stringBuilder.AppendLine(statuses);
                    }

                    if (!string.IsNullOrEmpty(timers)) {
                        stringBuilder.AppendLine(timers);
                    }

                    if (Manager.FrameLoops == 1) {
                        string inspectingInfo = ConsoleEnhancements.GetInspectingEntitiesInfo();
                        if (inspectingInfo.IsNotNullOrEmpty()) {
                            stringBuilder.AppendLine(inspectingInfo);
                        }
                    }

                    statusWithoutTime = stringBuilder.ToString();
                    if (Engine.FreezeTimer <= 0f) {
                        LastPos = player.ExactPosition;
                        LastPlayerSeekerPos = playerSeeker?.ExactPosition ?? default;
                    }
                } else if (level.InCutscene) {
                    statusWithoutTime = "Cutscene";
                }

                string roomNameAndTime =
                    $"[{level.Session.Level}] Timer: {(chapterTime / 10000000D):F3}({chapterTime / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks})";
                Status = statusWithoutTime + roomNameAndTime;
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

                    output += ConsoleEnhancements.GetInspectingEntitiesInfo("\t");

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
        Normal = Player.StNormal,
        Climb = Player.StClimb,
        Dash = Player.StDash,
        Swim = Player.StSwim,
        Boost = Player.StBoost,
        RedDash = Player.StRedDash,
        HitSquash = Player.StHitSquash,
        Launch = Player.StLaunch,
        Pickup = Player.StPickup,
        DreamDash = Player.StDreamDash,
        SummitLaunch = Player.StSummitLaunch,
        Dummy = Player.StDummy,
        IntroWalk = Player.StIntroWalk,
        IntroJump = Player.StIntroJump,
        IntroRespawn = Player.StIntroRespawn,
        IntroWakeUp = Player.StIntroWakeUp,
        BirdDashTutorial = Player.StBirdDashTutorial,
        Frozen = Player.StFrozen,
        ReflectionFall = Player.StReflectionFall,
        StarFly = Player.StStarFly,
        TempleFall = Player.StTempleFall,
        CassetteFly = Player.StCassetteFly,
        Attract = Player.StAttract,
        IntroMoonJump = Player.StIntroMoonJump,
        FlingBird = Player.StFlingBird,
        IntroThinkForABit = Player.StIntroThinkForABit
    }
}