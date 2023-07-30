using System.Collections.Generic;
using System.Linq;
using StudioCommunication;
using TAS.Communication;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class RecordingCommand {
    private record RecordingTime {
        public int StartFrame = int.MaxValue;
        public int StopFrame = int.MaxValue;
        public int Duration => StopFrame - StartFrame;

        public RecordingTime(int startFrame, int stopFrame) {
            StartFrame = startFrame;
            StopFrame = stopFrame;
        }

        public RecordingTime(int startFrame) {
            StartFrame = startFrame;
        }
    }

    private static readonly Dictionary<int, RecordingTime> recordingTimes = new();

    // workaround the first few frames get skipped when there is a breakpoint after StartRecording command
    public static bool StopFastForward {
        get {
            if (Manager.Recording) {
                return true;
            }

            return recordingTimes.Values.Any(time => {
                int currentFrame = Manager.Controller.CurrentFrameInTas;
                return currentFrame > time.StartFrame - 60 && currentFrame <= time.StopFrame;
            });
        }
    }

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

            RecordingTime time = new(Manager.Controller.Inputs.Count);
            recordingTimes[time.StartFrame] = time;
            if (args.Length != 0 && int.TryParse(args[0], out int framesToRecord)) {
                time.StopFrame = time.StartFrame + framesToRecord;
            }
        } else if (!Manager.Recording) {
            int framesToRecord = -1;

            if (args.Length != 0) {
                int.TryParse(args[0], out framesToRecord);
            } else if (recordingTimes.TryGetValue(Manager.Controller.CurrentFrameInTas, out RecordingTime time) &&
                       time.StartFrame != int.MaxValue && time.StopFrame != int.MaxValue) {
                framesToRecord = time.Duration;
            }

            if (framesToRecord > 0) {
                TASRecorderUtils.RecordFrames(framesToRecord);
            } else {
                TASRecorderUtils.StartRecording();
            }

            Manager.States &= ~States.FrameStep;
            Manager.NextStates &= ~States.FrameStep;
        }
    }

    // "StopRecording"
    [TasCommand("StopRecording", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void StopRecording() {
        if (ParsingCommand && recordingTimes.Count > 0) {
            RecordingTime last = recordingTimes.Last().Value;
            last.StopFrame = Manager.Controller.Inputs.Count;
        } else {
            TASRecorderUtils.StopRecording();
        }
    }

    [ClearInputs]
    private static void Clear() {
        recordingTimes.Clear();
    }

    [DisableRun]
    private static void DisableRun() {
        if (Manager.Recording) {
            TASRecorderUtils.StopRecording();
        }
    }
}