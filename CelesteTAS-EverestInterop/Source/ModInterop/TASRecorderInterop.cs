using System;
using System.Runtime.CompilerServices;
using Celeste.Mod.TASRecorder;

namespace TAS.ModInterop;

internal static class TASRecorderInterop {
    public static bool Installed => installed.Value;
    private static readonly Lazy<bool> installed = new(() => ModUtils.IsInstalled("TASRecorder"));

    /// Starts a recording.
    /// If <see cref="IsRecording"/> is true or <see cref="IsFFmpegInstalled"/> is false, this shouldn't be called.
    /// <param name="fileName">The file name of the recording. If <c>null</c>, it's generated from "dd-MM-yyyy_HH-mm-ss"</param>
    public static void StartRecording(string? fileName = null) {
        if (Installed) {
            startRecording(fileName);
        }
    }

    /// Stops a recording which was previously started.
    /// If <see cref="IsRecording"/> or <see cref="IsFFmpegInstalled"/> is false, this shouldn't be called.
    public static void StopRecording() {
        if (Installed) {
            stopRecording();
        }
    }

    /// Sets the estimated amount of total frames.
    /// This is used for the progress bar, but doesn't actually interact with the recording.
    /// If <see cref="IsRecording"/> or <see cref="IsFFmpegInstalled"/> is false, this shouldn't be called.
    /// <param name="frames">The total amount of frames, excluding loading times. If set to <c>null</c>, there isn't a progress bar.</param>
    public static void SetDurationEstimate(int? frames) {
        if (Installed) {
            setDurationEstimate(frames ?? TASRecorderAPI.NoEstimate);
        }
    }

    /// Whether TAS Recorder is currently recording.
    public static bool IsRecording => Installed && isRecording();

    /// Whether TAS Recorder could properly load FFmpeg.
    public static bool IsFFmpegInstalled => Installed && isFFmpegInstalled();

    // These methods **must not** be called if 'Installed == false'

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void startRecording(string? fileName = null) => TASRecorderAPI.StartRecording(fileName);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void stopRecording() => TASRecorderAPI.StopRecording();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void setDurationEstimate(int frames) => TASRecorderAPI.SetDurationEstimate(frames);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool isRecording() => TASRecorderAPI.IsRecording();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool isFFmpegInstalled() => TASRecorderAPI.IsFFmpegInstalled();
}
