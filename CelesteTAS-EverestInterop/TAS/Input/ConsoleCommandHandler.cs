using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input {
    public static class ConsoleCommandHandler {
        private static readonly FieldInfo MovementCounter = typeof(Actor).GetFieldInfo("movementCounter");
        private static Vector2 resetRemainder;
        private static Vector2 initSpeed;

        // ReSharper disable once UnusedMember.Local
        [Load]
        private static void Load() {
            On.Celeste.Level.LoadNewPlayer += LevelOnLoadNewPlayer;
            On.Celeste.Player.IntroRespawnEnd += PlayerOnIntroRespawnEnd;
        }

        // ReSharper disable once UnusedMember.Local
        [Unload]
        private static void Unload() {
            On.Celeste.Level.LoadNewPlayer -= LevelOnLoadNewPlayer;
            On.Celeste.Player.IntroRespawnEnd -= PlayerOnIntroRespawnEnd;
        }

        private static Player LevelOnLoadNewPlayer(On.Celeste.Level.orig_LoadNewPlayer orig, Vector2 position, PlayerSpriteMode spriteMode) {
            Player player = orig(position, spriteMode);

            if (resetRemainder != Vector2.Zero) {
                MovementCounter.SetValue(player, resetRemainder);
                resetRemainder = Vector2.Zero;
            }

            return player;
        }

        private static void PlayerOnIntroRespawnEnd(On.Celeste.Player.orig_IntroRespawnEnd orig, Player self) {
            orig(self);

            if (initSpeed != Vector2.Zero && self.Scene != null) {
                self.Scene.OnEndOfFrame += () => {
                    self.Speed = initSpeed;
                    initSpeed = Vector2.Zero;
                };
            }
        }

        // "Console CommandType",
        // "Console CommandType CommandArgs",
        // "Console LoadCommand IDorSID",
        // "Console LoadCommand IDorSID Screen",
        // "Console LoadCommand IDorSID Screen Spawnpoint",
        // "Console LoadCommand IDorSID PositionX PositionY"
        // "Console LoadCommand IDorSID PositionX PositionY SpeedX SpeedY"
        [TasCommand("Console", LegalInMainGame = false)]
        private static void ConsoleCommand(string[] arguments) {
            string commandName = arguments[0].ToLower();
            string[] args = arguments.Skip(1).ToArray();
            if (commandName.Equals("load", StringComparison.InvariantCultureIgnoreCase) ||
                commandName.Equals("hard", StringComparison.InvariantCultureIgnoreCase) ||
                commandName.Equals("rmx2", StringComparison.InvariantCultureIgnoreCase)) {
                LoadCommand(commandName, args);
            } else {
                Engine.Commands.ExecuteCommand(commandName, args);
            }
        }

        private static void LoadCommand(string command, string[] args) {
            try {
                if (SaveData.Instance == null || !Manager.AllowUnsafeInput && SaveData.Instance.FileSlot != -1) {
                    SaveData data = SaveData.Instance ?? UserIO.Load<SaveData>(SaveData.GetFilename(-1)) ?? new SaveData();
                    if (SaveData.Instance?.FileSlot is { } slot && slot != -1) {
                        SaveData.TryDelete(-1);
                        SaveData.LoadedModSaveDataIndex = -1;
                        foreach (EverestModule module in Everest.Modules) {
                            if (module._Session != null) {
                                module._Session.Index = -1;
                            }

                            if (module._SaveData != null) {
                                module._SaveData.Index = -1;
                            }
                        }

                        SaveData.Instance = data;
                        SaveData.Instance.FileSlot = -1;
                        UserIO.SaveHandler(true, true);
                    } else {
                        SaveData.Start(data, -1);
                    }

                    // Complete Prologue if incomplete and make sure the return to map menu item will be shown
                    LevelSetStats stats = data.GetLevelSetStatsFor("Celeste");
                    if (!data.Areas[0].Modes[0].Completed) {
                        data.Areas[0].Modes[0].Completed = true;
                        stats.UnlockedAreas++;
                    }
                }

                AreaMode mode = AreaMode.Normal;
                if (command.Equals("hard", StringComparison.InvariantCultureIgnoreCase)) {
                    mode = AreaMode.BSide;
                } else if (command.Equals("rmx2", StringComparison.InvariantCultureIgnoreCase)) {
                    mode = AreaMode.CSide;
                }

                int levelId = GetLevelId(args[0]);

                if (args.Length > 1) {
                    if (!double.TryParse(args[1], out double x) || args.Length == 2) {
                        string screen = args[1];
                        if (screen.StartsWith("lvl_")) {
                            screen = screen.Substring(4);
                        }

                        if (args.Length > 2) {
                            int spawnpoint = int.Parse(args[2]);
                            Load(mode, levelId, screen, spawnpoint);
                        } else {
                            Load(mode, levelId, screen);
                        }
                    } else if (args.Length > 2 && double.TryParse(args[2], out double y)) {
                        Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                        Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));

                        Vector2 speed = Vector2.Zero;
                        if (args.Length > 3 && float.TryParse(args[3], out float speedX)) {
                            speed.X = speedX;
                        }

                        if (args.Length > 4 && float.TryParse(args[4], out float speedY)) {
                            speed.Y = speedY;
                        }

                        Load(mode, levelId, position, remainder, speed);
                    }
                } else {
                    Load(mode, levelId);
                }
            } catch {
                // ignored
            }
        }

        private static int GetLevelId(string id) {
            if (int.TryParse(id, out int num)) {
                return num;
            } else {
                return AreaDataExt.Get(id).ID;
            }
        }

        private static void Load(AreaMode mode, int levelId, string screen = null, int? spawnPoint = null) {
            AreaKey areaKey = new(levelId, mode);
            Session session = AreaData.GetCheckpoint(areaKey, screen) != null ? new Session(areaKey, screen) : new Session(areaKey);

            if (screen != null) {
                session.Level = screen;
                session.FirstLevel = session.LevelData == session.MapData.StartLevel();
            }

            Vector2? startPosition = null;
            if (spawnPoint != null) {
                LevelData levelData = session.MapData.Get(screen);
                startPosition = levelData.Spawns[spawnPoint.Value];
            }

            session.StartedFromBeginning = spawnPoint == null && session.FirstLevel;
            Engine.Scene = new LevelLoader(session, startPosition);
        }

        private static void Load(AreaMode mode, int levelId, Vector2 spawnPoint, Vector2 remainder, Vector2 speed) {
            AreaKey areaKey = new(levelId, mode);
            Session session = new(areaKey);
            session.Level = session.MapData.GetAt(spawnPoint)?.Name;
            if (AreaData.GetCheckpoint(areaKey, session.Level) != null) {
                session = new Session(areaKey, session.Level);
            }

            session.FirstLevel = false;
            session.StartedFromBeginning = false;
            session.RespawnPoint = spawnPoint;
            resetRemainder = remainder;
            initSpeed = speed;
            Engine.Scene = new LevelLoader(session);
        }

        public static string CreateConsoleCommand(bool simple) {
            if (Engine.Scene is not Level level) {
                return null;
            }

            AreaKey area = level.Session.Area;
            string mode = null;
            switch (area.Mode) {
                case AreaMode.Normal:
                    mode = "load";
                    break;
                case AreaMode.BSide:
                    mode = "hard";
                    break;
                case AreaMode.CSide:
                    mode = "rmx2";
                    break;
            }

            string id = area.ID <= 10 ? area.ID.ToString() : area.GetSID();
            string separator = id.Contains(" ") ? ", " : " ";
            List<string> values = new() {"console", mode, id};

            if (!simple) {
                Player player = level.Tracker.GetEntity<Player>();
                if (player == null) {
                    values.Add(level.Session.Level);
                } else {
                    double x = player.X;
                    double y = player.Y;
                    double subX = player.PositionRemainder.X;
                    double subY = player.PositionRemainder.Y;

                    string format = "0.".PadRight(CelesteTasModuleSettings.MaxDecimals + 2, '#');
                    values.Add((x + subX).ToString(format, CultureInfo.InvariantCulture));
                    values.Add((y + subY).ToString(format, CultureInfo.InvariantCulture));

                    if (player.Speed != Vector2.Zero) {
                        values.Add(player.Speed.X.ToString(CultureInfo.InvariantCulture));
                        values.Add(player.Speed.Y.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            return string.Join(separator, values);
        }

        [Monocle.Command("giveberry", "Gives player a red berry (CelesteTAS)")]
        private static void CmdGiveBerry() {
            if (Engine.Scene is Level level && level.Tracker.GetEntity<Player>() is { } player) {
                EntityData entityData = new() {
                    Position = player.Position + new Vector2(0f, -16f),
                    ID = new Random().Next(),
                    Name = "strawberry"
                };
                EntityID gid = new(level.Session.Level, entityData.ID);
                Strawberry entity2 = new(entityData, Vector2.Zero, gid);
                level.Add(entity2);
            }
        }

        [Monocle.Command("clrsav", "clears save data on debug file (CelesteTAS)")]
        private static void CmdClearSave() {
            SaveData.TryDelete(-1);
            SaveData.Start(new SaveData {Name = "debug"}, -1);
            // Pretend that we've beaten Prologue.
            LevelSetStats stats = SaveData.Instance.GetLevelSetStatsFor("Celeste");
            stats.UnlockedAreas = 1;
            stats.AreasIncludingCeleste[0].Modes[0].Completed = true;
        }
    }
}