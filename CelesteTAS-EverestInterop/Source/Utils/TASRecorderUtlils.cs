using System;
using Celeste.Mod.TASRecorder.Interop;

namespace TAS.Utils;

internal static class TASRecorderUtils {
    private static readonly Lazy<bool> installed = new(() => ModUtils.IsInstalled("TASRecorder"));

    public static void StartRecording() {
        if (installed.Value) startRecording();
    }
    public static void StopRecording() {
        if (installed.Value) stopRecording();
    }
    public static void RecordFrames(int frames) {
        if (installed.Value) recordFrames(frames);
    }

    private static void startRecording() => TASRecorderInterop.StartRecording();
    private static void stopRecording() => TASRecorderInterop.StopRecording();
    private static void recordFrames(int frames) => TASRecorderInterop.RecordFrames(frames);
}