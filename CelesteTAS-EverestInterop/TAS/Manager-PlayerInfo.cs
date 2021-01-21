using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using TAS.EverestInterop;
using System.Reflection;

namespace TAS {
	public static partial class Manager {
		private static StreamWriter sw;
		private static MethodInfo[] trackedEntities;

		private static float framesPerSecond;

		//for debugging
		public static string additionalStatusInfo;
		public static bool ExportSyncData { get; set; }

		public static void UpdatePlayerInfo() {
			Player player = null;
			long chapterTime = 0;
			if (Engine.Scene is Level level) {
				player = level.Tracker.GetEntity<Player>();
				if (player != null) {
					chapterTime = level.Session.Time;
					if (chapterTime != lastTimer || LastPos != player.ExactPosition) {
						framesPerSecond = 60f / Engine.TimeRateB;
						string pos = GetAdjustedPos(player.Position, player.PositionRemainder);
						string speed = $"Speed: {player.Speed.X.ToString("0.00")}, {player.Speed.Y.ToString("0.00")}";
						Vector2 diff = (player.ExactPosition - LastPos) * 60f;
						string vel = $"Vel:   {diff.X.ToString("0.00")}, {diff.Y.ToString("0.00")}";
						string polarvel = $"Fly:   {diff.Length().ToString("0.00")}, {GetAngle(diff).ToString("0.00")}°";
						string miscstats = $"Stamina: {player.Stamina.ToString("0")}  "
						                   + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
						                   + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty);
						int dashCooldown = (int)(DashCooldownTimer(player) * framesPerSecond);
						string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
							+ (player.LoseShards ? "Ground " : string.Empty)
							+ (!player.LoseShards && JumpGraceTimer(player) > 0 ? $"Coyote({(int)(JumpGraceTimer(player) * framesPerSecond)})" : string.Empty);
						string transitionFrames = PlayerInfo.TransitionFrames > 0 ? $"({PlayerInfo.TransitionFrames})" : string.Empty;
						statuses = (player.InControl && !level.Transitioning ? statuses : $"NoControl{transitionFrames} ")
							+ (player.TimePaused ? "Paused " : string.Empty)
							+ (level.InCutscene ? "Cutscene " : string.Empty)
							+ (additionalStatusInfo ?? string.Empty);


						if (player.Holding == null) {
							foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
								Holdable holdable = (Holdable)component;
								if (holdable.Check(player)) {
									statuses += "Grab ";
									break;
								}
							}
						}

						int berryTimer = -10;
						Follower firstRedBerryFollower = player.Leader.Followers.Find(follower => follower.Entity is Strawberry berry && !berry.Golden);
						if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
							object collectTimer;
							if (firstRedBerry.GetType() == typeof(Strawberry)
								|| (collectTimer = StrawberryCollectTimer(firstRedBerry)) == null) {

								// if this is a vanilla berry or a mod berry having no collectTimer, use the cached FieldInfo for Strawberry.collectTimer.
								collectTimer = strawberryCollectTimer.GetValue(firstRedBerry);
							}

							berryTimer = 9 - (int)Math.Round((float)collectTimer * framesPerSecond);
						}
						string timers = (berryTimer != -10 ? berryTimer <= 9 ? $"BerryTimer: {berryTimer} " : $"BerryTimer: 9+{berryTimer-9} " : string.Empty)
							+ (dashCooldown != 0 ? $"DashTimer: {(dashCooldown).ToString()} " : string.Empty);
						string roomNameAndTime = $"[{level.Session.Level}] Timer: {(chapterTime / 10000000D).ToString("0.000")}({chapterTime / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks})";

						StringBuilder sb = new StringBuilder();
						sb.AppendLine(pos);
						sb.AppendLine(speed);
						sb.AppendLine(vel);

						if (player.StateMachine.State == Player.StStarFly
							|| SaveData.Instance.Assists.ThreeSixtyDashing
							|| SaveData.Instance.Assists.SuperDashing)
							sb.AppendLine(polarvel);

						sb.AppendLine(miscstats);
						if (!string.IsNullOrEmpty(statuses))
							sb.AppendLine(statuses);
						if(!string.IsNullOrEmpty(timers))
							sb.AppendLine(timers);
						sb.Append(roomNameAndTime);
						LastPos = player.ExactPosition;
						lastTimer = chapterTime;
						PlayerStatus = sb.ToString().TrimEnd();
					}
				}
				else
					PlayerStatus = level.InCutscene ? "Cutscene" : string.Empty;
			}

			else if (Engine.Scene is SummitVignette summit)
				PlayerStatus = "SummitVignette " + summitVignetteReady.GetValue(summit);

			else if (Engine.Scene is Overworld overworld)
				PlayerStatus = "Overworld " + overworld.ShowInputUI;

			else if (Engine.Scene != null)
				PlayerStatus = Engine.Scene.GetType().Name;
		}

		private static string GetAdjustedPos(Vector2 intPos, Vector2 subpixelPos) {
			double x = intPos.X;
			double y = intPos.Y;
			double subX = subpixelPos.X;
			double subY = subpixelPos.Y;

			if (!settings.RoundPosition) {
				return $"Pos:   {(x + subX).ToString("0.000000000000")}, {(y + subY).ToString("0.000000000000")}";
			}

			if (Math.Abs(subX) % 0.25 < 0.01 || Math.Abs(subX) % 0.25 > 0.24) {
				if (x > 0 || (x == 0 && subX > 0))
					x += Math.Floor(subX * 100) / 100;
				else
					x += Math.Ceiling(subX * 100) / 100;
			}
			else
				x += subX;
			if (Math.Abs(subY) % 0.25 < 0.01 || Math.Abs(subY) % 0.25 > 0.24) {
				if (y > 0 || (y == 0 && subY > 0))
					y += Math.Floor(subY * 100) / 100;
				else
					y += Math.Ceiling(subY * 100) / 100;
			}
			else
				y += subY;
			string pos = $"Pos:   {x.ToString("0.00")}, {y.ToString("0.00")}";
			return pos;
		}

		public static void BeginExport(string path, string[] tracked) {
			if (sw == null || sw.BaseStream == null) {
				sw = new StreamWriter(path);

				trackedEntities = new MethodInfo[tracked.Length];
				Assembly asm = typeof(Celeste.Celeste).Assembly;
				for (int i = 0; i < tracked.Length; i++) {
					Type t = asm.GetType("Celeste." + tracked[i]);
					if (t != null) {
						MethodInfo method = typeof(Tracker).GetMethod("GetEntities");
						trackedEntities[i] = method.MakeGenericMethod(t);
					}
				}
			}
		}

		public static void EndExport() {
			sw?.Dispose();
		}

		public static void ExportPlayerInfo() {
			Player player = null;
			if (Engine.Scene is Level level) {
				player = level.Tracker.GetEntity<Player>();
				if (player != null) {
					string inputs = controller.Current.ActionsToString();
					if (inputs.Length > 1)
						inputs = inputs.Substring(1);
					string time = (level.Session.Time / 10000000D).ToString("0.000");
					double x = (double)player.X + player.PositionRemainder.X;
					double y = (double)player.Y + player.PositionRemainder.Y;
					string pos = x.ToString() + "," + y.ToString();
					string speed = player.Speed.X.ToString() + "," + player.Speed.Y.ToString();

					int dashCooldown = (int)(DashCooldownTimer(player) * framesPerSecond);
					string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
						+ (player.LoseShards ? "Ground " : string.Empty)
						+ (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
						+ (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
						+ (!player.LoseShards && JumpGraceTimer(player) > 0 ? "Coyote " : string.Empty);
					statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
						+ (player.TimePaused ? "Paused " : string.Empty)
						+ (level.InCutscene ? "Cutscene " : string.Empty)
						+ (additionalStatusInfo != null ? additionalStatusInfo : string.Empty);

					if (player.Holding == null) {
						foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
							Holdable holdable = (Holdable)component;
							if (holdable.Check(player)) {
								statuses += "Grab ";
								break;
							}
						}
					}
					string output = string.Join(" ", inputs, controller.CurrentFrame, time, pos, speed, player.StateMachine.State, statuses);
					
					foreach (MethodInfo method in trackedEntities) {
						if (method == null)
							continue;
						List<Entity> entities = (List<Entity>)method.Invoke(level.Tracker, null);
						foreach (Entity entity in entities) {
							Actor actor = entity as Actor;
							if (actor != null) {
								x = (double)actor.X + actor.PositionRemainder.X;
								y = (double)actor.Y + actor.PositionRemainder.Y;
								pos = x.ToString() + "," + y.ToString();
							}
							else {
								pos = entity.X.ToString() + "," + entity.Y.ToString();
							}
							output += $" {method.GetGenericArguments()[0].Name}: {pos}";
						}

					}
					
					sw.WriteLine(output);
				}
			}
			else {
				string inputs = controller.Current.ActionsToString();
				if (inputs.Length > 1)
					inputs = inputs.Substring(1);
				string output = string.Join(" ", inputs, controller.CurrentFrame, Engine.Scene.GetType().Name);
				sw.WriteLine(output);
			}
		}
	}
}
