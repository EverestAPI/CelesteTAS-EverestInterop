using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.Input {
    public static class ConsoleCommandHandler {
        private static readonly FieldInfo MovementCounter = typeof(Actor).GetFieldInfo("movementCounter");
        private static Vector2? resetSpawn;
        private static Vector2 resetRemainder;
        private static Vector2 initSpeed;

        // ReSharper disable once UnusedMember.Local
        [DisableRun]
        private static void ClearResetSpawn() {
            resetSpawn = null;
        }

        // ReSharper disable once UnusedMember.Local
        [Load]
        private static void Load() {
            On.Celeste.LevelLoader.LoadingThread += LevelLoader_LoadingThread;
            On.Celeste.Level.LoadNewPlayer += LevelOnLoadNewPlayer;
            On.Celeste.Player.IntroRespawnEnd += PlayerOnIntroRespawnEnd;
        }

        // ReSharper disable once UnusedMember.Local
        [Unload]
        private static void Unload() {
            On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;
            On.Celeste.Level.LoadNewPlayer -= LevelOnLoadNewPlayer;
            On.Celeste.Player.IntroRespawnEnd -= PlayerOnIntroRespawnEnd;
        }

        private static void LevelLoader_LoadingThread(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self) {
            orig(self);
            if (resetSpawn is Vector2 respawnPoint) {
                Session session = self.Level.Session;
                session.RespawnPoint = respawnPoint;
                session.Level = session.MapData.GetAt(respawnPoint)?.Name;
                resetSpawn = null;
            }
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
        [TasCommand(LegalInMainGame = false, Name = "Console")]
        private static void ConsoleCommand(string[] arguments) {
            string commandName = arguments[0].ToLower();
            string[] args = arguments.Skip(1).ToArray();
            if (commandName == "load" || commandName == "hard" || commandName == "rmx2") {
                LoadCommand(commandName, args);
            } else {
                Engine.Commands.ExecuteCommand(commandName, args);
            }
        }

        private static void LoadCommand(string command, string[] args) {
            try {
                if (SaveData.Instance == null || !Manager.AllowUnsafeInput && SaveData.Instance.FileSlot != -1) {
                    int slot = SaveData.Instance == null ? -1 : SaveData.Instance.FileSlot;
                    SaveData data = UserIO.Load<SaveData>(SaveData.GetFilename(slot)) ?? new SaveData();
                    SaveData.Start(data, -1);

                    // Complete Prologue if incomplete
                    LevelSetStats stats = SaveData.Instance.GetLevelSetStatsFor("Celeste");
                    if (stats.UnlockedAreas == 0) {
                        stats.UnlockedAreas = 1;
                        stats.AreasIncludingCeleste[0].Modes[0].Completed = true;
                    }
                }

                AreaMode mode = AreaMode.Normal;
                if (command == "hard") {
                    mode = AreaMode.BSide;
                } else if (command == "rmx2") {
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
                        Vector2 position = new Vector2((int) Math.Round(x), (int) Math.Round(y));
                        Vector2 remainder = new Vector2((float) (x - Math.Truncate(x) + (int) x - (int) Math.Round(x)),
                            (float) (y - Math.Truncate(y) + (int) y - (int) Math.Round(y)));

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

        private static void Load(AreaMode mode, int levelId, string screen = null, int? spawnpoint = null) {
            Session session = new Session(new AreaKey(levelId, mode), screen);
            if (screen != null) {
                session.Level = screen;
                session.FirstLevel = session.LevelData == session.MapData.StartLevel();
            }

            if (spawnpoint != null) {
                LevelData levelData = session.MapData.Get(screen);
                resetSpawn = levelData.Spawns[spawnpoint.Value];
            }

            session.StartedFromBeginning = spawnpoint == null && session.FirstLevel;
            Engine.Scene = new LevelLoader(session);
        }

        private static void Load(AreaMode mode, int levelId, Vector2 spawnPoint, Vector2 remainder, Vector2 speed) {
            Session session = new Session(new AreaKey(levelId, mode));
            session.Level = session.MapData.GetAt(spawnPoint)?.Name;
            session.FirstLevel = false;
            session.StartedFromBeginning = false;
            resetSpawn = spawnPoint;
            resetRemainder = remainder;
            initSpeed = speed;
            Engine.Scene = new LevelLoader(session);
        }

        public static string CreateConsoleCommand() {
            if (!(Engine.Scene is Level level)) {
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
            List<string> values = new List<string> {"console", mode, id};
            Player player = level.Tracker.GetEntity<Player>();
            if (player == null) {
                values.Add(level.Session.Level);
            } else {
                double x = player.X;
                double y = player.Y;
                double subX = player.PositionRemainder.X;
                double subY = player.PositionRemainder.Y;
                values.Add((x + subX).ToString("0.############", CultureInfo.InvariantCulture));
                values.Add((y + subY).ToString("0.############", CultureInfo.InvariantCulture));

                if (player.Speed != Vector2.Zero) {
                    values.Add(player.Speed.X.ToString(CultureInfo.InvariantCulture));
                    values.Add(player.Speed.Y.ToString(CultureInfo.InvariantCulture));
                }
            }

            return string.Join(separator, values);
        }

        [Monocle.Command("giveberry", "Gives player a red berry")]
        private static void CmdGiveBerry() {
            Level level = Engine.Scene as Level;
            if (level != null) {
                Player entity = level.Tracker.GetEntity<Player>();
                if (entity != null) {
                    EntityData entityData = new EntityData();
                    entityData.Position = entity.Position + new Vector2(0f, -16f);
                    entityData.ID = Calc.Random.Next();
                    entityData.Name = "strawberry";
                    EntityID gid = new EntityID(level.Session.Level, entityData.ID);
                    Strawberry entity2 = new Strawberry(entityData, Vector2.Zero, gid);
                    level.Add(entity2);
                }
            }
        }

        [Monocle.Command("clrsav", "clears save data on debug file")]
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