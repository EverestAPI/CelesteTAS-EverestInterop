using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using System.Threading;

namespace TAS {
	public class ConsoleHandler {
		public static void ExecuteCommand(string[] command) {
			string[] args = InputCommands.TrimArray(command, 1);
			string commandID = command[0].ToLower();
			if (commandID == "load" || commandID == "hard" || commandID == "rmx2")
				LoadCommand(commandID, args);
			else
				Engine.Commands.ExecuteCommand(commandID, args);
		}

		private static void LoadCommand(string command, string[] args) {
			try {
				if (SaveData.Instance == null || (!Manager.allowUnsafeInput && SaveData.Instance.FileSlot != -1)) {
					int slot = SaveData.Instance == null ? -1 : SaveData.Instance.FileSlot;
					SaveData data = UserIO.Load<SaveData>(SaveData.GetFilename(slot));
					SaveData.Start(data, -1);
				}

				AreaMode mode = AreaMode.Normal;
				if (command == "hard")
					mode = AreaMode.BSide;
				else if (command == "rmx2")
					mode = AreaMode.CSide;
				int levelID = GetLevelID(args[0]);

				if (args.Length > 1) {
					if (!int.TryParse(args[1], out int x)) {
						string screen = args[1];
						if (screen.StartsWith("lvl_"))
							screen = screen.Substring(4);
						if (args.Length > 2) {
							int checkpoint = int.Parse(args[2]);
							Load(mode, levelID, screen, checkpoint);
						}
						else {
							Load(mode, levelID, screen);
						}
					}

					else if (args.Length > 2) {
						int y = int.Parse(args[2]);
						Load(mode, levelID, new Vector2(x, y));
					}
				} 
				else {
					Load(mode, levelID);
				}
			}
			catch { }
		}

		private static int GetLevelID(string ID) {
			if (int.TryParse(ID, out int num))
				return num;
			else
				return AreaDataExt.Get(ID).ID;
		}

		private static void Load(AreaMode mode, int levelID, string screen = null, int checkpoint = 0) {
			Session session = new Session(new AreaKey(levelID, mode));
			if (screen != null) {
				session.Level = screen;
				session.FirstLevel = false;
			}
			if (checkpoint != 0) {
				LevelData levelData = session.MapData.Get(screen);
				Manager.controller.resetSpawn = levelData.Spawns[checkpoint];
			}
			Engine.Scene = new LevelLoader(session);
		}

		private static void Load(AreaMode mode, int levelID, Vector2 spawnPoint) {
			Session session = new Session(new AreaKey(levelID, mode));
			session.Level = session.MapData.GetAt(spawnPoint)?.Name;
			session.FirstLevel = false;
			Manager.controller.resetSpawn = spawnPoint;
			Engine.Scene = new LevelLoader(session);
		}
	}
}
