using System.Diagnostics;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
public static class Benchmark {
    private static Stopwatch watch;
    private static bool lastRunning;
    private static ulong lastFrameCounter;

    public static void Load() {
        On.Monocle.Engine.Update += EngineOnUpdate;
    }

    public static void Unload() {
        On.Monocle.Engine.Update -= EngineOnUpdate;
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, GameTime gameTime) {
        orig(self, gameTime);
        if (lastRunning != Manager.Running) {
            if (Manager.Running) {
                Start();
            } else {
                Stop();
            }
        }

        lastRunning = Manager.Running;
    }

    public static void Start() {
        lastFrameCounter = Engine.FrameCounter;
        watch = new Stopwatch();
        watch.Start();
        $"Benchmark Start: {Manager.controller.tasFilePath}".Log();
    }

    public static void Stop() {
        ulong frames = Engine.FrameCounter - lastFrameCounter;
        $"Benchmark Stop: frames={frames} time={watch.ElapsedMilliseconds}ms avg_speed={frames / 60f / watch.ElapsedMilliseconds * 1000f}".Log();
        watch.Stop();
    }
}
}