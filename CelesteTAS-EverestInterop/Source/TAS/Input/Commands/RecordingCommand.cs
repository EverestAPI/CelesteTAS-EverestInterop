using Celeste.Mod;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RecordingCommand {
    // "StartRecording, [FramesToRecord]"
    [TasCommand("StartRecording")]
    private static void StartRecording(string[] args) {
        int framesToRecord = -1;

        if (args.Length != 0)
            int.TryParse(args[0], out framesToRecord);

        if (framesToRecord > 0)
            TASRecorderUtils.RecordFrames(framesToRecord);
        else
            TASRecorderUtils.StartRecording();
        Manager.Recording = true;
    }

    // "StopRecording"
    [TasCommand("StopRecording")]
    private static void StopRecording() {
        TASRecorderUtils.StopRecording();
        Manager.Recording = false;
    }
}