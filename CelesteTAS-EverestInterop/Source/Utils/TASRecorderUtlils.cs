using System;
using Celeste.Mod.TASRecorder.Interop;

namespace TAS.Utils;

internal static class TASRecorderUtils {
    private static readonly Lazy<bool> installed = new(() => ModUtils.IsInstalled("TASRecorder"));

    public static void StartRecording(string fileName = null) {
        if (installed.Value) startRecording(fileName);
    }
    public static void StopRecording() {
        if (installed.Value) stopRecording();
    }
    public static void RecordFrames(int frames, string fileName = null) {
        if (installed.Value) recordFrames(frames, fileName);
    }

    private static void startRecording(string fileName = null) => TASRecorderInterop.StartRecording(fileName);
    private static void stopRecording() => TASRecorderInterop.StopRecording();
    private static void recordFrames(int frames, string fileName = null) => TASRecorderInterop.RecordFrames(frames, fileName);
}