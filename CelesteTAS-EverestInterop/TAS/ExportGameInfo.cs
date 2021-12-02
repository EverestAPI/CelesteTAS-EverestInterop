using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS {
    public static class ExportGameInfo {
        private static StreamWriter streamWriter;
        private static IDictionary<string, Func<Level, IList>> trackedEntities;
        private static readonly MethodInfo EntityListFindAll = typeof(EntityList).GetMethod("FindAll");
        private static bool exporting;
        private static bool firstInputFrame;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        // ReSharper disable once UnusedMember.Local
        // "StartExportGameInfo"
        // "StartExportGameInfo Path"
        // "StartExportGameInfo Path EntitiesToTrack"
        [TasCommand("StartExportGameInfo", CalcChecksum = false)]
        private static void StartExportCommand(string[] args) {
            string path = "dump.txt";
            if (args.Length > 0) {
                if (args[0].Contains(".")) {
                    path = args[0];
                    args = args.Skip(1).ToArray();
                }
            }

            BeginExport(path, args);
        }

        // ReSharper disable once UnusedMember.Local
        [TasCommand("FinishExportGameInfo", CalcChecksum = false)]
        private static void FinishExportCommand() {
            EndExport();
        }

        private static void BeginExport(string path, string[] tracked) {
            exporting = true;
            firstInputFrame = true;
            streamWriter?.Dispose();
            if (Path.GetDirectoryName(path) is { } dir && dir.IsNotEmpty()) {
                Directory.CreateDirectory(dir);
            }

            streamWriter = new StreamWriter(path);
            streamWriter.WriteLine(string.Join("\t", "Line", "Inputs", "Frames", "Time", "Position", "Speed", "State", "Statuses", "Entities"));
            trackedEntities = new Dictionary<string, Func<Level, IList>>();
            foreach (string typeName in tracked) {
                if (InfoCustom.TryParseType(typeName, out Type t, out _, out _) && t.IsSameOrSubclassOf(typeof(Entity))) {
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

            if (controller.Current is { } currentInput && controller.CurrentFrameInTas == controller.Inputs.Count - 1) {
                Engine.Scene.OnEndOfFrame += () => ExportInfo(currentInput);
            }
        }

        private static void ExportInfo(InputFrame inputFrame) {
            InputController controller = Manager.Controller;
            string output;
            if (Engine.Scene is Level level) {
                Player player = level.Tracker.GetEntity<Player>();
                if (player == null) {
                    return;
                }

                string time = GameInfo.GetChapterTime(level);
                string pos = player.ToSimplePositionString(CelesteTasModuleSettings.MaxDecimals);
                string speed = player.Speed.ToSimpleString(Settings.SpeedDecimals);

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
                    foreach (Holdable holdable in level.Tracker.GetCastComponents<Holdable>()) {
                        if (holdable.Check(player)) {
                            statuses += "Grab ";
                            break;
                        }
                    }
                }

                output = string.Join("\t",
                    inputFrame.Line + 1, $"{controller.CurrentFrameInInput}/{inputFrame}", controller.CurrentFrameInTas, time, pos, speed,
                    PlayerStates.GetStateName(player.StateMachine.State),
                    statuses);

                foreach (string typeName in trackedEntities.Keys) {
                    IList entities = trackedEntities[typeName].Invoke(level);
                    if (entities == null) {
                        continue;
                    }

                    foreach (Entity entity in entities) {
                        output += $"\t{typeName}: {entity.ToSimplePositionString(Settings.CustomInfoDecimals)}";
                    }
                }

                if (InfoCustom.Parse() is { } customInfo && customInfo.IsNotEmpty()) {
                    output += $"\t{customInfo.ReplaceLineBreak(" ")}";
                }

                if (InfoWatchEntity.GetWatchingEntitiesInfo("\t", true) is { } watchInfo && watchInfo.IsNotEmpty()) {
                    output += $"\t{watchInfo}";
                }
            } else {
                string sceneName;
                if (Engine.Scene is Overworld overworld) {
                    sceneName = $"Overworld {(overworld.Current ?? overworld.Next).GetType().Name}";
                } else {
                    sceneName = Engine.Scene.GetType().Name;
                }

                output = string.Join("\t", inputFrame.Line + 1, $"{controller.CurrentFrameInInput}/{inputFrame}", controller.CurrentFrameInTas,
                    sceneName);
            }

            streamWriter.WriteLine(output);
        }
    }
}