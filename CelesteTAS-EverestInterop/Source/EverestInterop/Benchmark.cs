#if DEBUG
using JetBrains.Profiler.Api;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Diagnostics;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class Benchmark {
    [Load]
    private static void Load() {
        On.Monocle.Engine.Update += EngineOnUpdate;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= EngineOnUpdate;
    }

    private static bool lastRunning;
    private static readonly Stopwatch runtimeWatch = new();

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        orig(self, gameTime);

        if (lastRunning != Manager.Running && Manager.Controller.HasFastForward) {
            if (Manager.Running) {
                $"Starting performance profiling at {DateTime.Now}".DebugLog();

                MeasureProfiler.StartCollectingData();
                runtimeWatch.Restart();
            } else {
                runtimeWatch.Stop();
                MeasureProfiler.SaveData();

                $"Stopping performance profiling at {DateTime.Now}".DebugLog();
                $" => {runtimeWatch.Elapsed} total".DebugLog();
                $" => {(Manager.Controller.Inputs.Count > 0 ? runtimeWatch.Elapsed.TotalNanoseconds / Manager.Controller.Inputs.Count : "N/A")} ns/Update".DebugLog();
                $" => {Manager.Controller.Inputs.Count / runtimeWatch.Elapsed.TotalSeconds} Updates/s ({Manager.Controller.Inputs.Count / runtimeWatch.Elapsed.TotalSeconds / 60.0f}x)".DebugLog();
            }
        }

        lastRunning = Manager.Running;
    }
}
#endif
