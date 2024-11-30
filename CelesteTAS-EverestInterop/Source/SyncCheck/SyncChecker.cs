using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.SyncCheck;

/// Automatically runs the specified TAS files and reports if they were successful
internal static class SyncChecker {
    /// Whether the game is in sync-check mode and disallows user intervention
    public static bool Active { get; private set; } = false;

    private static bool waitingForLoad = true;
    private static readonly Queue<string> fileQueue = [];

    public static void AddFile(string file) {
        if (!File.Exists(file)) {
            Logger.Error("CelesteTAS/SyncCheck", $"TAS file to sync check was not found: '{file}'");
            return;
        }

        Logger.Info("CelesteTAS/SyncCheck", $"Registered file for sync checking: '{file}'");

        Active = true;
        fileQueue.Enqueue(file);
    }

    [Initialize]
    private static void Initialize() {
        On.Celeste.Celeste.OnSceneTransition += On_Celeste_OnSceneTransition;
    }
    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.OnSceneTransition -= On_Celeste_OnSceneTransition;
    }

    private static void On_Celeste_OnSceneTransition(On.Celeste.Celeste.orig_OnSceneTransition orig, Celeste.Celeste self, Scene last, Scene next) {
        orig(self, last, next);

        // Wait until game is done loading
        if (waitingForLoad && next is Overworld) {
            waitingForLoad = false;

            if (fileQueue.TryDequeue(out string file)) {
                CheckFile(file);
            }
        }
    }

    [DisableRun]
    private static void OnDisableRun() {
        if (!Active) {
            return;
        }

        Logger.Info("CelesteTAS/SyncCheck", $"Finished check for file: '{InputController.StudioTasFilePath}'");

        if (fileQueue.TryDequeue(out string file)) {
            CheckFile(file);
        } else {
            // Done with all checks
            // TODO: Write result
            // Engine.Instance.Exit();
            Console.WriteLine("EXIT EXIT EXIT");
        }
    }

    private static void CheckFile(string file) {
        Logger.Info("CelesteTAS/SyncCheck", $"Starting check for file: '{file}'");

        InputController.StudioTasFilePath = file;
        // Insert breakpoint at the end
        Manager.Controller.FastForwards.Add(Manager.Controller.Inputs.Count, new FastForward(Manager.Controller.Inputs.Count, "", Manager.Controller.Inputs[^1].Line));
        Manager.EnableRun();
    }
}
