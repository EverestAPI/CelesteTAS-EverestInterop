using System;
using System.IO;
using System.Reflection;
using TAS.EverestInterop;

namespace TAS {
	public class InputCommands {

		public static string[] Split(string line) {
			if (line.Contains(","))
				return line.Trim().Split(',');
			return line.Trim().Split();
		}
		
		public static bool TryExecuteCommand(InputController state, string line, int studioLine) {
			try {
				if (char.IsLetter(line[0])) {
					string[] args = Split(line);
					string commandType = args[0] + "Command";
					MethodInfo method = typeof(InputCommands).GetMethod(commandType, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
					if (method == null) return false;

					string[] commandArgs = new string[args.Length - 1];
					for (int i = 1; i < args.Length; i++) {
						commandArgs[i - 1] = args[i];
					}
					TASCommandAttribute attribute = (TASCommandAttribute)method.GetCustomAttribute(typeof(TASCommandAttribute));
					if (!(Manager.enforceLegal && attribute.illegalInMaingame)) {
						if (attribute.executeAtStart)
							return (bool)method.Invoke(null, new object[] { state, commandArgs, studioLine });
						else {

							Action commandCall = () => method.Invoke(null, new object[] { commandArgs });
							state.inputs.Add(new InputRecord(commandCall));
						}
					}
				}
				return false;
			}
			catch { return false; }
		}

		[TASCommand(executeAtStart = true)]
		private static bool ReadCommand(InputController state, string[] args, int studioLine) {
			string filePath = args[0];
			string origFilePath = Manager.settings.DefaultPath;
			// Check for full and shortened Read versions for absolute path
			if (origFilePath != null) {
				string altFilePath = origFilePath + Path.DirectorySeparatorChar + filePath;
				if (File.Exists(altFilePath))
					filePath = altFilePath;
				else {
					string[] files = Directory.GetFiles(origFilePath, $"{filePath}*.tas");
					if (files.Length != 0)
						filePath = files[0].ToString();
				}
			}
			// Check for full and shortened Read versions for relative path
			if (!File.Exists(filePath)) {
				string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(), $"{filePath}*.tas");
				filePath = files[0].ToString();
				if (!File.Exists(filePath)) { return false; }
			}
			// Find starting and ending lines
			int skipLines = 0;
			int lineLen = int.MaxValue;
			if (args.Length > 1) {
				string startLine = args[1];
				GetLine(startLine, filePath, out skipLines);
				if (args.Length > 2) {
					string endLine = args[2];
					GetLine(endLine, filePath, out lineLen);
				}
			}
			state.ReadFile(filePath, skipLines, lineLen, studioLine);
			return false;
		}

		[TASCommand(illegalInMaingame = true)]
		private static void ConsoleCommand(string[] args) {
			ConsoleHandler.ExecuteCommand(args);
		}

		[TASCommand(executeAtStart = true)]
		private static bool PlayCommand(InputController state, string[] args, int studioLine) {
			GetLine(args[0], state.filePath, out int startLine);
			if (args.Length > 1 && int.TryParse(args[1], out _))
				state.inputs.Add(new InputRecord(studioLine, args[1]));
			state.ReadFile(state.filePath, startLine);
			return true;
		}

		//The capitalization 
		[TASCommand]
		private static void StartExportCommand(string[] args) {
			string path = "dump.txt";
			if (args.Length > 0)
				path = args[0];
			Manager.BeginExport(path);
			Manager.ExportSyncData = true;
		}

		[TASCommand]
		private static void FinishExportCommand(string[] args) {
			Manager.EndExport();
			Manager.ExportSyncData = false;
		}

		[TASCommand(executeAtStart = true)]
		private static void EnforceLegalCommand(InputController state, string[] args, int studioLine) {
			Manager.enforceLegal = true;
		}

		private static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
			if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
				int curLine = 0;
				using (StreamReader sr = new StreamReader(path)) {
					while (!sr.EndOfStream) {
						curLine++;
						string line = sr.ReadLine();
						if (line == ("#" + labelOrLineNumber)) {
							lineNumber = curLine;
							return;
						}
					}
					lineNumber = int.MaxValue;
				}
			}
		}
	}
}
