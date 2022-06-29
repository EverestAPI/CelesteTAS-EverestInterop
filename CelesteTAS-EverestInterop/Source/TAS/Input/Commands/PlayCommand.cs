using Celeste.Mod;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PlayCommand {
    // "Play, StartLine",
    // "Play, StartLine, FramesToWait"
    [TasCommand("Play", ExecuteTiming = ExecuteTiming.Parse)]
    private static void Play(string[] args, int studioLine) {
        ReadCommand.GetLine(args[0], InputController.TasFilePath, out int startLine);
        if (args.Length > 1 && int.TryParse(args[1], out _)) {
            Manager.Controller.AddFrames(args[1], studioLine);
        }

        if (startLine <= studioLine + 1) {
            "Play command does not allow playback from before the current line".Log(LogLevel.Warn);
            return;
        }

        Manager.Controller.ReadFile(InputController.TasFilePath, startLine, int.MaxValue, startLine - 1);
    }
}