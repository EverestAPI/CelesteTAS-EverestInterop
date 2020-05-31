using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using TAS.EverestInterop;
using System.Reflection;
using TAS.StudioCommunication;

namespace TAS
{
	public partial class Manager
	{
		private static StreamWriter sw;
		public static bool ExportSyncData { get; set; }

		private static void UpdatePlayerInfo() {
			Player player = null;
			long chapterTime = 0;
			if (Engine.Scene is Level level) {
				player = level.Tracker.GetEntity<Player>();
				if (player != null) {
					chapterTime = level.Session.Time;
					if (chapterTime != lastTimer || lastPos != player.ExactPosition) {

						string pos = GetAdjustedPos(player.Position, player.PositionRemainder);
						string speed = $"Speed: {player.Speed.X.ToString("0.00")},{player.Speed.Y.ToString("0.00")}";
						Vector2 diff = (player.ExactPosition - lastPos) * 60;
						string vel = $"Vel: {diff.X.ToString("0.00")},{diff.Y.ToString("0.00")}";
						string polarvel = $"     {diff.Length().ToString("0.00")},{GetAngle(diff).ToString("0.00")}°";
						string miscstats = $"Stamina: {player.Stamina.ToString("0")} Timer: {(chapterTime / 10000000D).ToString("0.000")}";

						int dashCooldown = (int)((float)player.GetPrivateField("dashCooldownTimer") * 60f);
						string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
							+ (player.LoseShards ? "Ground " : string.Empty)
							+ ((bool)player.InvokePrivateMethod("WallJumpCheck", 1) ? "Wall-R " : string.Empty)
							+ ((bool)player.InvokePrivateMethod("WallJumpCheck", -1) ? "Wall-L " : string.Empty)
							+ (!player.LoseShards && (float)player.GetPrivateField("jumpGraceTimer") > 0 ? "Coyote " : string.Empty);
						statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
							+ (player.TimePaused ? "Paused " : string.Empty)
							+ (level.InCutscene ? "Cutscene " : string.Empty);

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
								|| (collectTimer = firstRedBerry.GetPrivateField("collectTimer")) == null) {

								// if this is a vanilla berry or a mod berry having no collectTimer, use the cached FieldInfo for Strawberry.collectTimer.
								collectTimer = strawberryCollectTimer.GetValue(firstRedBerry);
							}

							berryTimer = (int)Math.Round(60f * (float)collectTimer);
						}
						string timers = (berryTimer != -10 ? $"BerryTimer: {berryTimer.ToString()} " : string.Empty)
							+ (dashCooldown != 0 ? $"DashTimer: {(dashCooldown).ToString()} " : string.Empty);
						string map = $"[{level.Session.Level}]";

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
						sb.Append(timers);
						sb.Append(map);
						lastPos = player.ExactPosition;
						lastTimer = chapterTime;
						PlayerStatus = sb.ToString().TrimEnd();
					}
				}
				else
					PlayerStatus = level.InCutscene ? "Cutscene" : string.Empty;
			}

			else if (Engine.Scene is SummitVignette summit)
				PlayerStatus = "SummitVignette " + summit.GetPrivateField("ready");

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
			string pos = $"Pos: {x.ToString("0.00")},{y.ToString("0.00")}";
			return pos;
		}

		public static void BeginExport(string path) {
			sw = new StreamWriter(path);
		}

		public static void EndExport() {
			sw.Dispose();
		}

		public static void ExportPlayerInfo(string[] tracked = null) {
			Player player = null;
			if (Engine.Scene is Level level) {
				player = level.Tracker.GetEntity<Player>();
				if (player != null) {
					string inputs = controller.Current.ActionsToString().Substring(1);
					string time = (level.Session.Time / 10000000D).ToString("0.000");
					double x = (double)player.X + player.PositionRemainder.X;
					double y = (double)player.Y + player.PositionRemainder.Y;
					string pos = x.ToString() + "," + y.ToString();
					string speed = player.Speed.X.ToString() + "," + player.Speed.Y.ToString();

					int dashCooldown = (int)((float)player.GetPrivateField("dashCooldownTimer") * 60f);
					string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
						+ (player.LoseShards ? "Ground " : string.Empty)
						+ ((bool)player.InvokePrivateMethod("WallJumpCheck", 1) ? "Wall-R " : string.Empty)
						+ ((bool)player.InvokePrivateMethod("WallJumpCheck", -1) ? "Wall-L " : string.Empty)
						+ (!player.LoseShards && (float)player.GetPrivateField("jumpGraceTimer") > 0 ? "Coyote " : string.Empty);
					statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
						+ (player.TimePaused ? "Paused " : string.Empty)
						+ (level.InCutscene ? "Cutscene " : string.Empty);

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
					/*
					foreach (string s in tracked) {
						Type t = Type.GetType("Celeste." + s);
						MethodInfo method = typeof(Tracker).GetMethod("GetEntities");
						method.MakeGenericMethod(t);
						Entity[] entities = (Entity[])method.Invoke(level.Tracker, null);
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
							output += $" {s}: {pos}";
						}

					}
					*/
					sw.WriteLine(output);
				}
			}
		}
	}
}