using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace TAS.EverestInterop
{
    public class UnixPipeRTC
    {

        public static bool Running, Recording;
        private static readonly InputController controller = new InputController("Celeste.tas");
        public static State state, nextState;
        public static string CurrentStatus, PlayerStatus;
        public static int FrameStepCooldown, FrameLoops = 1;
        private static readonly bool frameStepWasDpadUp, frameStepWasDpadDown;
        private static Vector2 lastPos;
        private static long lastTimer;
        private static readonly CultureInfo enUS = CultureInfo.CreateSpecificCulture("en-US");
        private static KeyboardState kbState;
        private static readonly List<VirtualButton.Node>[] playerBindings;

        public static float GetAngle(Vector2 vector)
        {
            float angle = 360f / 6.283186f * Calc.Angle(vector);
            if (angle < -90.01f)
            {
                return 450f + angle;
            }
            else
            {
                return 90f + angle;
            }
        }

        private static void UpdatePlayerInfo()
        {
            Player player = null;
            long chapterTime = 0;
            if (Engine.Scene is Level level)
            {
                player = level.Tracker.GetEntity<Player>();
                if (player != null)
                {
                    chapterTime = level.Session.Time;
                    if (chapterTime != lastTimer || lastPos != player.ExactPosition)
                    {
                        string pos = $"Pos: {player.ExactPosition.X.ToString("0.00", enUS)},{player.ExactPosition.Y.ToString("0.00", enUS)}";
                        string speed = $"Speed: {player.Speed.X.ToString("0.00", enUS)},{player.Speed.Y.ToString("0.00", enUS)}";
                        Vector2 diff = (player.ExactPosition - lastPos) * 60;
                        string vel = $"Vel: {diff.X.ToString("0.00", enUS)},{diff.Y.ToString("0.00", enUS)}";
                        string polarvel = $"     {diff.Length().ToString("0.00", enUS)},{GetAngle(diff).ToString("0.00", enUS)}°";
                        string miscstats = $"Stamina: {player.Stamina.ToString("0")} Timer: {(chapterTime / 10000000D).ToString("0.000", enUS)}";
                        string statuses = (player.CanDash ? "Dash " : string.Empty) + (player.LoseShards ? "Ground " : string.Empty) + (player.WallJumpCheck(1) ? "Wall-R " : string.Empty) + (player.WallJumpCheck(-1) ? "Wall-L " : string.Empty) + (!player.LoseShards && player.jumpGraceTimer > 0 ? "Coyote " : string.Empty);
                        statuses = ((player.InControl && !level.Transitioning ? statuses : "NoControl ") + (player.TimePaused ? "Paused " : string.Empty) + (level.InCutscene ? "Cutscene " : string.Empty));
                        if (player.Holding == null)
                        {
                            foreach (Component component in level.Tracker.GetComponents<Holdable>())
                            {
                                Holdable holdable = (Holdable)component;
                                if (holdable.Check(player))
                                {
                                    statuses += "Grab ";
                                    break;
                                }
                            }
                        }

                        int berryTimer = -10;
                        Follower firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry berry && !berry.Golden);
                        if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry)
                        {
                            berryTimer = (int)Math.Round(60f * firstRedBerry.collectTimer);
                        }
                        string timers = (berryTimer != -10 ? $"BerryTimer: {berryTimer.ToString()} " : string.Empty) + ((int)(player.dashCooldownTimer * 60f) != 0 ? $"DashTimer: {((int)Math.Round(player.dashCooldownTimer * 60f) - 1).ToString()} " : string.Empty);

                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine(pos);
                        sb.AppendLine(speed);
                        sb.AppendLine(vel);
                        if (player.StateMachine.State == 19 || SaveData.Instance.Assists.ThreeSixtyDashing || SaveData.Instance.Assists.SuperDashing)
                        {
                            sb.AppendLine(polarvel);
                        }
                        sb.AppendLine(miscstats);
                        if (!string.IsNullOrEmpty(statuses))
                        {
                            sb.AppendLine(statuses);
                        }
                        sb.Append(timers);
                        PlayerStatus = sb.ToString().TrimEnd();
                        lastPos = player.ExactPosition;
                        lastTimer = chapterTime;
                    }
                }
                else
                {
                    PlayerStatus = level.InCutscene ? "Cutscene" : null;
                }
            }
            else if (Engine.Scene is SummitVignette summit)
            {
                PlayerStatus = string.Concat("SummitVignette ", summit);
            }
            else if (Engine.Scene is Overworld overworld)
            {
                PlayerStatus = string.Concat("Overworld ", overworld.ShowInputUI);
            }
            else if (Engine.Scene != null)
            {
                PlayerStatus = Engine.Scene.GetType().Name;
            }
        }
    }
}
