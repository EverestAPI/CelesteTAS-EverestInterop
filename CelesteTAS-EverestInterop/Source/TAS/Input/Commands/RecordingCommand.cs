using Celeste.Mod;
using StudioCommunication;
using TAS.Communication;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RecordingCommand {
    // "StartRecording, [FramesToRecord]"
    [TasCommand("StartRecording")]
    private static void StartRecording(string[] args) {
        if (!TASRecorderUtils.Installed) {
            AbortTas("TAS Recorder isn't installed");
            return;
        }
        if (!TASRecorderUtils.IsFFmpegInstalled()) {
            AbortTas("FFmpeg libraries aren't properly installed");
            return;
        }

        int framesToRecord = -1;

        if (args.Length != 0)
            int.TryParse(args[0], out framesToRecord);

        if (framesToRecord > 0)
            TASRecorderUtils.RecordFrames(framesToRecord);
        else
            TASRecorderUtils.StartRecording();

        Manager.Recording = true;
        Manager.States &= ~States.FrameStep;
        Manager.NextStates &= ~States.FrameStep;
    }

    // "StopRecording"
    [TasCommand("StopRecording")]
    private static void StopRecording() {
        TASRecorderUtils.StopRecording();
        Manager.Recording = false;
    }
}