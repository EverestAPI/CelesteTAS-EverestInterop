using Celeste.Mod;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PlayCommand {
    private class PlayMeta : ITasCommandMeta {
        public string Insert => $"Play{CommandInfo.Separator}[0;Starting Label]";
        public bool HasArguments => true;

        public int GetHash(string[] args, string filePath, int fileLine) {
            // Only file contents and line matters
            return 31 * File.ReadAllText(filePath).GetStableHashCode() + 17 * fileLine;
        }

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            // Don't include labels before the current line
            foreach (string line in File.ReadAllText(filePath).ReplaceLineEndings("\n").Split('\n').Skip(fileLine)) {
                if (!StudioCommunication.CommentLine.IsLabel(line)) {
                    continue;
                }

                string label = line[1..]; // Remove the #
                yield return new CommandAutoCompleteEntry { Name = label, IsDone = true, HasNext = false };
            }
        }
    }

    // "Play, StartLabel",
    // "Play, StartLabel, FramesToWait"
    [TasCommand("Play", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(PlayMeta))]
    private static void Play(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (!ReadCommand.TryGetLine(args[0], Manager.Controller.FilePath, out int startLine)) {
            AbortTas($"\"Play, {string.Join(", ", args)}\" failed\n{args[0]} is invalid", true);
            return;
        }

        if (args.Length > 1 && int.TryParse(args[1], out _)) {
            Manager.Controller.AddFrames(args[1], studioLine);
        }

        if (startLine <= studioLine + 1) {
            "Play command does not allow playback from before the current line".Log(LogLevel.Warn);
            return;
        }

        Manager.Controller.ReadFile(Manager.Controller.FilePath, startLine, int.MaxValue, startLine - 1);
    }
}
