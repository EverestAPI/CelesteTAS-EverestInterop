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
    private static string resultFile = string.Empty;

    private static List<string> wrongTimes = [];
    private static bool wasUnsafe = false;
    private static bool wasAssert = false;

    private static SyncCheckResult result = new();

    public static void AddFile(string file) {
        if (!File.Exists(file)) {
            Logger.Error("CelesteTAS/SyncCheck", $"TAS file to sync check was not found: '{file}'");
            return;
        }

        Logger.Info("CelesteTAS/SyncCheck", $"Registered file for sync checking: '{file}'");

        Active = true;
        fileQueue.Enqueue(file);
    }
    public static void SetResultFile(string file) {
        if (!string.IsNullOrEmpty(resultFile)) {
            Logger.Warn("CelesteTAS/SyncCheck", $"Overwriting previously defined result file '{resultFile}' with '{file}");
        } else {
            Logger.Info("CelesteTAS/SyncCheck", $"Writing sync-check result to file: '{file}'");
        }

        resultFile = file;
    }

    /// Indicates that the current TAS has finished executing
    public static void ReportRunFinished() {
        if (!Active) {
            return;
        }

        Logger.Info("CelesteTAS/SyncCheck", $"Finished check for file: '{InputController.StudioTasFilePath}'");

        // Check for desyncs
        SyncCheckResult.Entry entry;
        if (wasUnsafe) {
            // Performed unsafe action in safe-mode
            entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, SyncCheckResult.Status.UnsafeAction, GameInfo.ExactStatus);
        } else if (wasAssert) {
            // Assertion failure
            entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, SyncCheckResult.Status.AssertFailed, GameInfo.ExactStatus);
        } else if (Engine.Scene is not (Level { Completed: true } or LevelExit or AreaComplete)) {
            // TAS did not finish
            GameInfo.Update(updateVel: false);
            entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, SyncCheckResult.Status.NotFinished, GameInfo.ExactStatus);
        } else if (wrongTimes.Count != 0) {
            // TAS finished with wrong time(s)
            entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, SyncCheckResult.Status.WrongTime, string.Join("\n", wrongTimes));
        } else {
            // No desync
            entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, SyncCheckResult.Status.Success, string.Empty);
        }

        result.Entries.Add(entry);
        result.WriteToFile(resultFile);

        if (fileQueue.TryDequeue(out string file)) {
            CheckFile(file);
        } else {
            // Done with all checks
            result.Finished = true;
            result.WriteToFile(resultFile);

            Engine.Instance.Exit();
        }
    }

    /// Indicates that a time command was updated with another time
    public static void ReportWrongTime(string filePath, int fileLine, string oldTime, string newTime) {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected wrong time in file '{filePath}' line {fileLine}: '{oldTime}' vs '{newTime}'");
        wrongTimes.Add($"{filePath}\t{fileLine}\t{oldTime}\t{newTime}");
    }

    /// Indicates that an unsafe action was performed in safe-mode
    public static void ReportUnsafeAction() {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected unsafe action");
        wasUnsafe = true;
    }

    /// Indicates that an Assert-command failed
    public static void ReportAssertFailed(string lineText, string filePath, int fileLine, string expected, string actual) {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected failed assertion '{lineText}' in file '{filePath}' line {fileLine}: Expected '{expected}', got '{actual}'");
        wasAssert = true;
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

            if (string.IsNullOrEmpty(resultFile)) {
                Logger.Error("CelesteTAS/SyncCheck", "No result file specified. Aborting sync-check!");
                Engine.Instance.Exit();
            }

            if (fileQueue.TryDequeue(out string file)) {
                CheckFile(file);
            }
        }
    }

    /// Starts executing a TAS for sync-checking
    private static void CheckFile(string file) {
        // Reset state
        wrongTimes.Clear();
        wasUnsafe = false;
        wasAssert = false;

        Logger.Info("CelesteTAS/SyncCheck", $"Starting check for file: '{file}'");

        InputController.StudioTasFilePath = file;
        // Insert breakpoint at the end
        Manager.Controller.FastForwards.Add(Manager.Controller.Inputs.Count, new FastForward(Manager.Controller.Inputs.Count, "", Manager.Controller.Inputs[^1].Line));
        Manager.EnableRun();
    }
}
