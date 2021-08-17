using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Utils;

namespace TAS {
    public static class ExportGameInfo {
        private static StreamWriter streamWriter;
        private static IDictionary<string, Func<Level, IList>> trackedEntities;
        private static readonly MethodInfo EntityListFindAll = typeof(EntityList).GetMethod("FindAll");
        private static bool exporting;
        private static bool firstInputFrame;

        // ReSharper disable once UnusedMember.Local
        // "StartExportGameInfo"
        // "StartExportGameInfo Path"
        // "StartExportGameInfo Path EntitiesToTrack"
        [TasCommand(Name = "StartExportGameInfo")]
        private static void StartExportCommand(string[] args) {
            string path = "dump.txt";
            if (args.Length > 0) {
                if (args[0].Contains(".")) {
                    path = args[0];
                    args = args.Skip(1).ToArray();
                }
            }

            BeginExport(path, args);
            exporting = true;
        }

        // ReSharper disable once UnusedMember.Local
        [TasCommand(Name = "FinishExportGameInfo")]
        private static void FinishExportCommand(string[] args) {
            EndExport();
        }

        private static void BeginExport(string path, string[] tracked) {
            firstInputFrame = true;
            streamWriter?.Dispose();
            streamWriter = new StreamWriter(path);
            streamWriter.WriteLine(string.Join("\t", "Line", "Inputs", "Frames", "Time", "Position", "Speed", "State", "Statuses", "Entities"));
            trackedEntities = new Dictionary<string, Func<Level, IList>>();
            foreach (string typeName in tracked) {
                string fullTypeName = typeName.Contains("@") ? typeName.Replace("@", ",") : $"Celeste.{typeName}, Celeste";
                Type t = Type.GetType(fullTypeName);
                if (t != null && t.IsSameOrSubclassOf(typeof(Entity))) {
                    trackedEntities[t.Name] = level => FindEntity(t, level);
                }
            }
        }

        private static IList FindEntity(Type type, Level level) {
            if (level.Tracker.Entities.ContainsKey(type)) {
                return level.Tracker.Entities[type];
            } else {
                return EntityListFindAll.MakeGenericMethod(type).Invoke(level.Entities, null) as IList;
            }
        }

        [DisableRun]
        private static void EndExport() {
            exporting = false;
            firstInputFrame = false;
            streamWriter?.Dispose();
        }

        public static void ExportInfo() {
            if (!exporting) {
                return;
            }

            if (firstInputFrame) {
                firstInputFrame = false;
                return;
            }

            InputController controller = Manager.Controller;
            InputFrame previousInput = controller.Previous;
            if (previousInput == null) {
                return;
            }

            ExportInfo(previousInput);

            if (controller.Current is { } currentInput && controller.CurrentFrame == controller.Inputs.Count - 1) {
                Engine.Scene.OnEndOfFrame += () => ExportInfo(currentInput);
            }
        }

        private static void ExportInfo(InputFrame inputFrame) {
            InputController controller = Manager.Controller;
            string output = string.Empty;
            if (Engine.Scene is Level level) {
                Player player = level.Tracker.GetEntity<Player>();
                if (player == null) {
                    return;
                }

                string time = GameInfo.GetChapterTime(level);
                string pos = player.ToSimplePositionString(CelesteTasModule.Settings.RoundPosition);
                string speed = player.Speed.X + ", " + player.Speed.Y;

                int dashCooldown = (int) GameInfo.GetDashCooldownTimer(player);
                string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "CanDash " : string.Empty)
                                  + (player.LoseShards ? "Ground " : string.Empty)
                                  + (GameInfo.GetWallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                  + (GameInfo.GetWallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
                                  + (!player.LoseShards && GameInfo.GetJumpGraceTimer(player) > 0 ? "Coyote " : string.Empty);
                statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
                           + (player.TimePaused ? "Paused " : string.Empty)
                           + (level.InCutscene ? "Cutscene " : string.Empty)
                           + (GameInfo.AdditionalStatusInfo ?? string.Empty);

                if (player.Holding == null) {
                    foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
                        Holdable holdable = (Holdable) component;
                        if (holdable.Check(player)) {
                            statuses += "Grab ";
                            break;
                        }
                    }
                }

                output = string.Join("\t",
                    inputFrame.Line + 1, $"{controller.InputCurrentFrame}/{inputFrame}", controller.CurrentFrame, time, pos, speed,
                    PlayerStates.GetStateName(player.StateMachine.State),
                    statuses);

                foreach (string typeName in trackedEntities.Keys) {
                    IList entities = trackedEntities[typeName].Invoke(level);
                    if (entities == null) {
                        continue;
                    }

                    foreach (Entity entity in entities) {
                        output += $"\t{typeName}: {entity.ToSimplePositionString(CelesteTasModule.Settings.RoundCustomInfo)}";
                    }
                }

                if (InfoCustom.Parse(true) is { } customInfo && customInfo.IsNotEmpty()) {
                    output += $"\t{customInfo.ReplaceLineBreak(" ")}";
                }

                if (InfoInspectEntity.GetInspectingEntitiesInfo("\t", true) is { } inspectInfo && inspectInfo.IsNotEmpty()) {
                    output += $"\t{inspectInfo}";
                }
            } else {
                output = string.Join("\t", inputFrame.Line + 1, $"{controller.InputCurrentFrame}/{inputFrame}", controller.CurrentFrame,
                    Engine.Scene.GetType().Name);
            }

            streamWriter.WriteLine(output);
        }
    }
}