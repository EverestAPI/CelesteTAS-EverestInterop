using System;
using System.Collections.Generic;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static partial class InfoWatchEntity {
    public static string GetAutoWatchInfo(Entity entity, string separator, int decimals, string entityId) {
        StringBuilder data = new();
        Player player = entity.Scene.GetPlayer();

        void appendSeparator(string add) {
            data.Append(separator);
            data.Append(add);
        }

        data.Append(GetPositionInfo(entity, entityId, decimals));
        if (entity is Platform platform) {
            appendSeparator($"Liftspeed: {platform.LiftSpeed.ToSimpleString(decimals)} ({platform.LiftSpeed.ClampLiftSpeed().ToSimpleString(decimals)})");
        }

        // TODO: Platform-specific information

        if (entity is Cloud cloud) {
            if (cloud.respawnTimer > 0f) {
                appendSeparator($"Respawn  : {GameInfo.ConvertToFrames(cloud.respawnTimer)}");
            } else {
                appendSeparator($"Speed    : {GameInfo.ConvertSpeedUnit(cloud.speed, TasSettings.SpeedUnit).ToFormattedString(decimals)}");
            }
        }

        if (entity is Seeker seeker) {
            AddSeekerData(data, seeker, separator, decimals);
        } else if (entity is AngryOshiro oshiro) {
            appendSeparator($"{oshiro.GetStateName()}");
            // TODO: State-specific information
        } else if (entity is Actor actor) {
            appendSeparator($"Liftboost: {actor.LiftSpeed.ToSimpleString(decimals)}");
        }

        if (entity is Bumper bumper) {
            appendSeparator($"Anchor : {bumper.anchor.ToSimpleString(decimals)}");
            appendSeparator($"Offset : {(bumper.Position - bumper.anchor).ToSimpleString(decimals)}");
            float bumperCycleAngle = bumper.sine.counter % (float) (Math.PI * 2d);
            appendSeparator($"Cycle  : {(bumperCycleAngle / (float) (Math.PI * 2d)).ToFormattedString(decimals)} ({bumperCycleAngle.ToFormattedString(decimals)})");
            if (player is { }) {
                float bumperYDot = Vector2.Dot((player.Center - bumper.Position).SafeNormalize(), Vector2.UnitY);
                appendSeparator($"Launch : {CalculateBumperLaunchSpeed(bumper, player, 280f, false).ToSimpleString(decimals)}");
                appendSeparator($"LaunchX: {CalculateBumperLaunchSpeed(bumper, player, 280f, true).ToSimpleString(decimals)}");
                appendSeparator($"Y-Dot  : {bumperYDot.ToFormattedString(decimals)} ({Math.Atan(bumperYDot).ToFormattedString(decimals)})");
            }

            if (bumper.respawnTimer > 0f) {
                appendSeparator($"Respawn: {GameInfo.ConvertToFrames(bumper.respawnTimer)}");
            }
        }

        // TODO: Other Actor-specific inforemation

        if (entity.GetOffset() is float offset) {
            appendSeparator($"Next check: {offset.NextCheckDistance()}");
        }

        return data.ToString();
    }

    public static void AddSeekerData(StringBuilder data, Seeker seeker, string separator, int decimals) {
        Player player = seeker.Scene.GetPlayer();

        void appendSeparator(string add) {
            data.Append(separator);
            data.Append(add);
        }

        int stateStringStart = data.Length;
        string stateString = $"{separator}{seeker.GetStateName()}";

        List<string> tags = new List<string>();

        Vector2 seekerPlayerAim = (seeker.FollowTarget - seeker.Center).SafeNormalize();
        Vector2 seekerSpeedAim = seeker.Speed.SafeNormalize();
        float seekerPlayerDot = seekerPlayerAim.Angle();
        float seekerAttackDot = Vector2.Dot(seekerSpeedAim, seekerPlayerAim);

        int state = seeker.State.state;

        int coroutineTimer = 0;
        if (state != Seeker.StPatrol) {
            coroutineTimer = GameInfo.ConvertToFrames(seeker.State.currentCoroutine.waitTimer);
        }

        switch (state) {
            case Seeker.StIdle:
                if (seeker.spotted) {
                    tags.Add("aware");
                }
                if (coroutineTimer >= 0) {
                    appendSeparator($"Patrol delay   : {coroutineTimer}");
                }
                break;
            case Seeker.StPatrol:
                appendSeparator($"Next point     :");
                if (seeker.patrolWaitTimer < 0.4f) {
                    data.Append($": {GameInfo.ConvertToFrames(seeker.patrolWaitTimer)}");
                } else {
                    tags.Add("close");
                }
                break;
            case Seeker.StSpotted:
                appendSeparator($"Losing player  : {GameInfo.ConvertToFrames(seeker.spottedLosePlayerTimer)}");
                if (coroutineTimer >= 0) {
                    appendSeparator($"Attack delay   : {coroutineTimer}");
                }
                break;
            case Seeker.StAttack:
                if (seeker.attackWindUp) {
                    tags.Add($"windup {coroutineTimer}");
                } else {
                    tags.Add("dash");
                }
                appendSeparator($"Speed-player  : {seekerAttackDot.ToDeg().ToFormattedString(decimals)} ({seekerAttackDot.ToFormattedString(decimals)})");
                break;
            case Seeker.StStunned:
                if (coroutineTimer >= 0) {
                    tags.Add(coroutineTimer.ToString());
                }
                break;
            case Seeker.StSkidding:
                tags.Add(seeker.strongSkid ? "strong" : $"weak {coroutineTimer}");
                break;
            case Seeker.StRegenerate:
                string regenerateRoutineTag = String.Empty;
                if (!seeker.shaker.on) {
                    regenerateRoutineTag = "falling";
                } else if (seeker.sprite.CurrentAnimationID == "pulse") {
                    regenerateRoutineTag = "pulsing";
                } else if (seeker.sprite.CurrentAnimationID == "recover") {
                    regenerateRoutineTag = "recovering";
                } else {
                    regenerateRoutineTag = "shaking";
                }
                regenerateRoutineTag += $" {coroutineTimer}";
                tags.Add(regenerateRoutineTag);
                break;
            case Seeker.StReturned:
                tags.Add(coroutineTimer.ToString());
                break;
        }

        if (state <= Seeker.StSpotted) {
            if (player is { } && Vector2.DistanceSquared(player.Center, seeker.Center) > 12544f) {
                tags.Add("far");
            }
            float lastDistance = Vector2.Distance(seeker.Center, seeker.FollowTarget);
            appendSeparator($"Last player    : {seeker.FollowTarget.ToSimpleString(decimals)}");
            appendSeparator($"Last distance  : {lastDistance.ToFormattedString(decimals)} px ({(lastDistance / 8f).ToFormattedString(decimals)} tiles)");
        }

        if (state == Seeker.StSpotted || state == Seeker.StAttack) {
            appendSeparator($"Player angle   : {seekerPlayerDot.ToDeg().ToFormattedString(decimals)} ({seekerPlayerDot.ToFormattedString(decimals)})");
        }

        if (tags.Count > 0) {
            stateString += $" ({string.Join(", ", tags)})";
        }

        data = data.Insert(stateStringStart, stateString);

        if (player is { }) {
            float playerDistance = Vector2.Distance(seeker.Center, player.Center);
            appendSeparator($"Player distance: {playerDistance.ToFormattedString(decimals)} px ({(playerDistance / 8f).ToFormattedString(decimals)} tiles)");
        }
        appendSeparator($"Speed          : {seeker.Speed.ToSimpleString(decimals)}");
        appendSeparator($"Speed Magnitude: {seeker.Speed.Length().ToFormattedString(decimals)}");
    }
    public static Vector2 CalculateBumperLaunchSpeed(Bumper bumper, Player player, float length = 280f, bool explosive = false) {
        const float HIGH_HIT_DOT = -0.55f;
        const float LOW_HIT_DOT = 0.65f;

        const float PLAYER_HOR_SPEED = -15f / 28f;

        float playerLineDot = Vector2.Dot((player.Center - bumper.Position).SafeNormalize(), Vector2.UnitY);

        double playerLineSin = playerLineDot;
        double playerLineCos = Math.Sqrt(1 - (playerLineSin * playerLineSin)) * Math.Sign(player.Center.X - bumper.Position.X);
        Vector2 playerLaunchSpeed = new Vector2((float) playerLineCos, (float) playerLineSin);
        bool autoJump = true;

        if (playerLineDot > LOW_HIT_DOT) {
            autoJump = false;
        } else if (playerLineDot > HIGH_HIT_DOT ) {
            playerLaunchSpeed.Y = PLAYER_HOR_SPEED;
            playerLaunchSpeed.X = Math.Sign(playerLaunchSpeed.X);
        }

        playerLaunchSpeed *= length;
        if (explosive) {
            playerLaunchSpeed.X *= 1.2f;
        }

        return playerLaunchSpeed;
    }
}