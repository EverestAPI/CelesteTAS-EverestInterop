using System;
using System.Runtime.CompilerServices;
using Celeste.Mod.TASRecorder;

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
    public static void SetDurationEstimate(int frames) {
        if (installed.Value) setDurationEstimate(frames);
    }
    
    public static bool Recording => installed.Value && isRecording();
    public static bool FFmpegInstalled => installed.Value && isFFmpegInstalled();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void startRecording(string fileName = null) => TASRecorderAPI.StartRecording(fileName);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void stopRecording() => TASRecorderAPI.StopRecording();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void setDurationEstimate(int frames) => TASRecorderAPI.SetDurationEstimate(frames);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool isRecording() => TASRecorderAPI.IsRecording();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool isFFmpegInstalled() => TASRecorderAPI.IsFFmpegInstalled();
}