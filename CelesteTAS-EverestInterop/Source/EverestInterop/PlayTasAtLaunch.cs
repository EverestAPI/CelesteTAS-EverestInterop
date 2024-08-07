using System;
using System.Collections.Generic;
using System.IO;
using Celeste;
using Celeste.Mod;
using Monocle;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class PlayTasAtLaunch {
    public static bool WaitToPlayTas { get; private set; }

    [Initialize]
    private static void Initialize() {
        On.Celeste.Celeste.OnSceneTransition += CelesteOnOnSceneTransition;
        ParseArgs();
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.OnSceneTransition -= CelesteOnOnSceneTransition;
    }

    private static void CelesteOnOnSceneTransition(On.Celeste.Celeste.orig_OnSceneTransition orig, Celeste.Celeste self, Scene last, Scene next) {
        orig(self, last, next);
        if (WaitToPlayTas && next is Overworld) {
            WaitToPlayTas = false;
            Manager.EnableRun();
        }
    }

    private static void ParseArgs() {
        Queue<string> queue = new(Everest.Args);
        while (queue.Count > 0) {
            string arg = queue.Dequeue();

            if (arg == "--tas" && queue.Count >= 1) {
                string tasPath = queue.Dequeue();

                // fix: https://github.com/EverestAPI/CelesteTAS-EverestInterop/issues/62
                while (Environment.OSVersion.Platform != PlatformID.Win32NT && queue.Count >= 1 && !tasPath.EndsWith(".tas", StringComparison.OrdinalIgnoreCase)) {
                    tasPath += " " + queue.Dequeue();
                }

                if (File.Exists(tasPath)) {
                    Manager.Controller.FilePath = tasPath;
                    WaitToPlayTas = true;
                } else {
                    $"TAS file '{tasPath}' does not exist.".Log(LogLevel.Warn);
                }
            }
        }
    }
}