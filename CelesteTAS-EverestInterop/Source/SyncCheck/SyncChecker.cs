using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using TAS.Input;
using TAS.Module;

#nullable enable

namespace TAS.SyncCheck;

/// Automatically runs the specified TAS files and reports if they were successful
internal static class SyncChecker {
    /// Whether the game is in sync-check mode and disallows user intervention
    public static bool Active { get; private set; } = false;

    private static bool waitingForLoad = true;

    private static readonly Queue<string> fileQueue = [];
    private static string resultFile = string.Empty;

    private static SyncCheckResult.Status currentStatus = SyncCheckResult.Status.Success;
    private static object? currentAdditionalInformation = null;

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
        if (currentStatus == SyncCheckResult.Status.Success && Engine.Scene is not (Level { Completed: true } or LevelExit or AreaComplete)) {
            // TAS did not finish
            currentStatus = SyncCheckResult.Status.NotFinished;
            currentAdditionalInformation = null;
        }

        GameInfo.Update(updateVel: false);
        var entry = new SyncCheckResult.Entry(InputController.StudioTasFilePath, currentStatus, GameInfo.ExactStatus, currentAdditionalInformation);

        result.Entries.Add(entry);
        result.WriteToFile(resultFile);

        if (fileQueue.TryDequeue(out string? file)) {
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

        if (currentStatus != SyncCheckResult.Status.WrongTime) {
            currentStatus = SyncCheckResult.Status.WrongTime;
            currentAdditionalInformation = new List<SyncCheckResult.WrongTimeInfo>();
        }
        ((List<SyncCheckResult.WrongTimeInfo>)currentAdditionalInformation!).Add(new SyncCheckResult.WrongTimeInfo(filePath, fileLine, oldTime, newTime));
    }

    /// Indicates that an unsafe action was performed in safe-mode
    public static void ReportUnsafeAction() {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected unsafe action");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation = null;
    }

    /// Indicates that an Assert-command failed
    public static void ReportAssertFailed(string lineText, string filePath, int fileLine, string expected, string actual) {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected failed assertion '{lineText}' in file '{filePath}' line {fileLine}: Expected '{expected}', got '{actual}'");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation = new SyncCheckResult.AssertFailedInfo(filePath, fileLine, actual, expected);
    }

    /// Indicates that a crash happened while sync-checking
    public static void ReportCrash(Exception ex) {
        Logger.Error("CelesteTAS/SyncCheck", $"Detected a crash: {ex}");

        currentStatus = SyncCheckResult.Status.Crash;
        currentAdditionalInformation = ex.ToString();
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
        if (waitingForLoad && Active && next is Overworld) {
            waitingForLoad = false;

            if (string.IsNullOrEmpty(resultFile)) {
                Logger.Error("CelesteTAS/SyncCheck", "No result file specified. Aborting sync-check!");
                Engine.Instance.Exit();
            }

            if (fileQueue.TryDequeue(out string? file)) {
                CheckFile(file);
            }
        }
    }

    /// Starts executing a TAS for sync-checking
    private static void CheckFile(string file) {
        // Reset state
        currentStatus = SyncCheckResult.Status.Success;
        currentAdditionalInformation = null;

        Logger.Info("CelesteTAS/SyncCheck", $"Starting check for file: '{file}'");

        InputController.StudioTasFilePath = file;
        // Insert breakpoint at the end
        Manager.Controller.FastForwards.Add(Manager.Controller.Inputs.Count, new FastForward(Manager.Controller.Inputs.Count, "", Manager.Controller.Inputs[^1].Line));
        Manager.EnableRun();
    }
}
