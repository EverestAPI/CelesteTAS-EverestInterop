using StudioCommunication;
using TAS.Communication;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RecordingCommand {
    // workaround the first few frames get skipped when there is a breakpoint after startrecording
    public static bool StopFastForward => Manager.Recording || Manager.Controller.CurrentFrameInTas >= startRecordingFrame - 60 &&
        Manager.Controller.CurrentFrameInTas <= stopRecordingFrame;

    private static int startRecordingFrame = -1;
    private static int stopRecordingFrame = -1;

    // "StartRecording, [FramesToRecord]"
    [TasCommand("StartRecording", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void StartRecording(string[] args) {
        if (ParsingCommand) {
            if (StudioCommunicationBase.Initialized && Manager.Running) {
                if (!TASRecorderUtils.Installed) {
                    StudioCommunicationClient.Instance?.SendRecordingFailed(RecordingFailedReason.TASRecorderNotInstalled);
                } else if (!TASRecorderUtils.IsFFmpegInstalled()) {
                    StudioCommunicationClient.Instance?.SendRecordingFailed(RecordingFailedReason.FFmpegNotInstalled);
                }
            }

            if (!TASRecorderUtils.Installed) {
                AbortTas("TAS Recorder isn't installed");
                return;
            }

            if (!TASRecorderUtils.IsFFmpegInstalled()) {
                AbortTas("FFmpeg libraries aren't properly installed");
                return;
            }

            startRecordingFrame = Manager.Controller.Inputs.Count;

            if (args.Length != 0 && int.TryParse(args[0], out int framesToRecord)) {
                stopRecordingFrame = startRecordingFrame + framesToRecord;
            }

        } else if (!Manager.Recording) {
            int framesToRecord = -1;

            if (args.Length != 0) {
                int.TryParse(args[0], out framesToRecord);
            }

            if (framesToRecord > 0) {
                TASRecorderUtils.RecordFrames(framesToRecord);
            } else {
                TASRecorderUtils.StartRecording();
            }

            Manager.Recording = true;
            Manager.States &= ~States.FrameStep;
            Manager.NextStates &= ~States.FrameStep;
        }
    }

    // "StopRecording"
    [TasCommand("StopRecording", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void StopRecording() {
        if (ParsingCommand) {
            stopRecordingFrame = Manager.Controller.Inputs.Count;
        } else {
            TASRecorderUtils.StopRecording();
            Manager.Recording = false;
        }
    }

    [ClearInputs]
    private static void Clear() {
        startRecordingFrame = -1;
        stopRecordingFrame = -1;
    }
}