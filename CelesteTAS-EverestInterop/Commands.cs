using Celeste;
using FMOD.Studio;
using System;
using System.IO;
using System.Reflection;

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
					string commandType = char.ToUpper(args[0][0]) + args[0].Substring(1).ToLower() + "Command";
					MethodInfo method = typeof(InputCommands).GetMethod(commandType);
					return (bool)method.Invoke(null, new object[] { state, args, studioLine });
				}
				return false;
			}
			catch { return false; }
		}

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

		private static bool ConsoleCommand(InputController state, string[] args, int studioLine) {
			state.inputs.Add(new InputRecord(args));
			return false;
		}

		private static bool PlayCommand(InputController state, string[] args, int studioLine) {
			GetLine(args[0], state.filePath, out int startLine);
			if (args.Length > 1 && int.TryParse(args[1], out _))
				state.inputs.Add(new InputRecord(studioLine, args[1]));
			state.ReadFile(state.filePath, startLine);
			return true;
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
