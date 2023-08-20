using System;
using System.Runtime.CompilerServices;
using Celeste.Mod.TASRecorder.Interop;

namespace TAS.Utils;

internal static class TASRecorderUtils {
    public static bool Installed => installed.Value;
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
    public static bool IsFFmpegInstalled() {
        if (installed.Value) return ffmpegInstalled();
        return false;
    }

    public static bool IsRecording() {
        if (installed.Value) return recording();
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void startRecording(string fileName = null) => TASRecorderInterop.StartRecording(fileName);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void stopRecording() => TASRecorderInterop.StopRecording();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void recordFrames(int frames, string fileName = null) => TASRecorderInterop.RecordFrames(frames, fileName);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ffmpegInstalled() => TASRecorderInterop.IsFFmpegInstalled();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool recording() => TASRecorderInterop.IsRecording();
}