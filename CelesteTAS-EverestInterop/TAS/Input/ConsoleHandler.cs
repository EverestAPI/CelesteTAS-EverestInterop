using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;

namespace TAS.Input {
    public static class ConsoleHandler {
        private static Vector2? resetSpawn;

        // ReSharper disable once UnusedMember.Local
        [DisableRun]
        private static void ClearResetSpawn() {
            resetSpawn = null;
        }

        // ReSharper disable once UnusedMember.Local
        [Load]
        private static void Load() {
            On.Celeste.LevelLoader.LoadingThread += LevelLoader_LoadingThread;
        }

        // ReSharper disable once UnusedMember.Local
        [Unload]
        private static void Unload() {
            On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;
        }

        private static void LevelLoader_LoadingThread(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self) {
            orig(self);
            Session session = self.Level.Session;
            if (resetSpawn is Vector2 spawn) {
                session.RespawnPoint = spawn;
                session.Level = session.MapData.GetAt(spawn)?.Name;
                session.FirstLevel = false;
                resetSpawn = null;
            }
        }

        // "Console CommandType",
        // "Console CommandType CommandArgs",
        // "Console LoadCommand IDorSID",
        // "Console LoadCommand IDorSID Screen",
        // "Console LoadCommand IDorSID Screen Checkpoint",
        // "Console LoadCommand IDorSID X Y"
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
                if (SaveData.Instance == null || (!Manager.AllowUnsafeInput && SaveData.Instance.FileSlot != -1)) {
                    int slot = SaveData.Instance == null ? -1 : SaveData.Instance.FileSlot;
                    SaveData data = UserIO.Load<SaveData>(SaveData.GetFilename(slot));
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
                    if (!int.TryParse(args[1], out int x) || args.Length == 2) {
                        string screen = args[1];
                        if (screen.StartsWith("lvl_")) {
                            screen = screen.Substring(4);
                        }

                        if (args.Length > 2) {
                            int checkpoint = int.Parse(args[2]);
                            Load(mode, levelId, screen, checkpoint);
                        } else {
                            Load(mode, levelId, screen);
                        }
                    } else if (args.Length > 2) {
                        int y = int.Parse(args[2]);
                        Load(mode, levelId, new Vector2(x, y));
                    }
                } else {
                    Load(mode, levelId);
                }
            } catch { }
        }

        private static int GetLevelId(string id) {
            if (int.TryParse(id, out int num)) {
                return num;
            } else {
                return AreaDataExt.Get(id).ID;
            }
        }

        private static void Load(AreaMode mode, int levelId, string screen = null, int checkpoint = 0) {
            Session session = new Session(new AreaKey(levelId, mode));
            if (screen != null) {
                session.Level = screen;
                session.FirstLevel = session.LevelData == session.MapData.StartLevel();
            }

            if (checkpoint != 0) {
                LevelData levelData = session.MapData.Get(screen);
                resetSpawn = levelData.Spawns[checkpoint];
            }

            session.StartedFromBeginning = checkpoint == 0 && session.FirstLevel;
            Engine.Scene = new LevelLoader(session);
        }

        private static void Load(AreaMode mode, int levelId, Vector2 spawnPoint) {
            Session session = new Session(new AreaKey(levelId, mode));
            session.Level = session.MapData.GetAt(spawnPoint)?.Name;
            session.FirstLevel = false;
            session.StartedFromBeginning = false;
            resetSpawn = spawnPoint;
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
            string location = null;
            Player player = level.Entities.FindFirst<Player>();
            if (player == null) {
                location = level.Session.Level;
            } else {
                location = player.X.ToString() + " " + player.Y.ToString();
            }

            if (id.Contains(" ")) {
                return $"console, {mode}, {id}, {location.Replace(" ", ", ")}";
            } else {
                return $"console {mode} {id} {location}";
            }
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