using System;
using System.Diagnostics;
using JetBrains.Profiler.Api;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

#if DEBUG
namespace TAS.EverestInterop {
    public static class Benchmark {
        private static readonly Stopwatch watch = new Stopwatch();
        private static bool lastRunning;
        private static ulong lastFrameCounter;

        [Load]
        private static void Load() {
            On.Monocle.Engine.Update += EngineOnUpdate;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Engine.Update -= EngineOnUpdate;
        }

        private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
            orig(self, gameTime);
            if (lastRunning != Manager.Running && Manager.Controller.HasFastForward) {
                if (Manager.Running) {
                    Start();
                } else {
                    Stop();
                }
            }

            if (Manager.Running) {
                if (watch.IsRunning && Manager.IsLoading()) {
                    watch.Stop();
                } else if (!watch.IsRunning && !Manager.IsLoading()) {
                    watch.Start();
                }
            }

            lastRunning = Manager.Running;
        }

        private static void Start() {
            lastFrameCounter = Engine.FrameCounter;
            watch.Restart();
            $"Benchmark Start: {InputController.TasFilePath}".Log();
            MeasureProfiler.StartCollectingData();
        }

        private static void Stop() {
            MeasureProfiler.SaveData();
            ulong frames = Engine.FrameCounter - lastFrameCounter;
            float framesPerSecond = (int) Math.Round(1 / Engine.RawDeltaTime);
            $"Benchmark Stop: frames={frames} time={watch.ElapsedMilliseconds}ms avg_speed={frames / framesPerSecond / watch.ElapsedMilliseconds * 1000f}"
                .Log();
            watch.Stop();
        }
    }
}
#endif