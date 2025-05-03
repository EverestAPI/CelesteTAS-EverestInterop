using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Celeste.Mod.UI;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.Tools;

/// Automatically runs the specified TAS files and reports if they were successful
internal static class SyncChecker {
    /// Whether the game is in sync-check mode and disallows user intervention
    public static bool Active { get; private set; } = false;

    private static bool waitingForLoad = true;

    private static readonly Queue<string> fileQueue = [];
    private static string resultFile = string.Empty;

    private static SyncCheckResult.Status currentStatus = SyncCheckResult.Status.Success;
    private static SyncCheckResult.AdditionalInfo currentAdditionalInformation = new();

    private static SyncCheckResult result = new();

    private static string? CurrentFilePath => Manager.Controller.Current?.FilePath;
    private static int? CurrentFileLine => Manager.Controller.Current?.FileLine;

    public static void AddFile(string file) {
        Active = true;

        if (!File.Exists(file)) {
            Logger.Error("CelesteTAS/SyncCheck", $"TAS file to sync check was not found: '{file}'");
            return;
        }

        Logger.Info("CelesteTAS/SyncCheck", $"Registered file for sync checking: '{file}'");

        fileQueue.Enqueue(file);
    }
    public static void SetResultFile(string file) {
        Active = true;

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

        Logger.Info("CelesteTAS/SyncCheck", $"Finished check for file: '{Manager.Controller.FilePath}'");

        // Check for desyncs
        if (currentStatus == SyncCheckResult.Status.Success && Engine.Scene is not (
                Level { Completed: true } or
                Level { Session: { Area.SID: "Celeste/8-Epilogue", Level: "inside" } } or
                LevelExit or
                AreaComplete or
                Overworld { Current: OuiJournal }
        )) {
            // TAS did not finish
            currentStatus = SyncCheckResult.Status.NotFinished;
            currentAdditionalInformation.Clear();
            currentAdditionalInformation.Abort = new SyncCheckResult.AbortInfo(CurrentFilePath, CurrentFileLine, Manager.Controller.Current?.ToString());
        }

        GameInfo.Update(updateVel: false);

        string infoWithSid = Engine.Scene?.GetSession() is { } session
            ? $"{GameInfo.ExactStatus}\n\nSID: {session.Area} ({session.MapData.Filename})"
            : GameInfo.ExactStatus;
        var entry = new SyncCheckResult.Entry(Manager.Controller.FilePath, currentStatus, infoWithSid, currentAdditionalInformation);

        result.Entries.Add(entry);
        result.WriteToFile(resultFile);

        if (fileQueue.TryDequeue(out string? file)) {
            // Add to action queue, since we're still in DisableRun and can't start the next one immediately
            Manager.AddMainThreadAction(() => CheckFile(file));
        } else {
            // Done with all checks
            result.Finished = true;
            result.WriteToFile(resultFile);

            Environment.Exit(0);
        }
    }

    /// Indicates that a time command was updated with another time
    public static void ReportWrongTime(string filePath, int fileLine, string oldTime, string newTime) {
        if (!Active) {
            return;
        }

        Logger.Error("CelesteTAS/SyncCheck", $"Detected wrong time in file '{filePath}' line {fileLine}: '{oldTime}' vs '{newTime}'");

        if (currentStatus != SyncCheckResult.Status.WrongTime) {
            currentStatus = SyncCheckResult.Status.WrongTime;
            currentAdditionalInformation.Clear();
            currentAdditionalInformation.WrongTime = [];
        }
        currentAdditionalInformation.WrongTime!.Add(new SyncCheckResult.WrongTimeInfo(filePath, fileLine, oldTime, newTime));
    }

    /// Indicates that an unsafe action was performed in safe-mode
    public static void ReportUnsafeAction() {
        if (!Active) {
            return;
        }

        Logger.Error("CelesteTAS/SyncCheck", "Detected unsafe action");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.Abort = new SyncCheckResult.AbortInfo(CurrentFilePath, CurrentFileLine, Manager.Controller.Current?.ToString());
    }

    /// Indicates that an Assert-command failed
    public static void ReportAssertFailed(string lineText, string filePath, int fileLine, string expected, string actual) {
        if (!Active) {
            return;
        }

        Logger.Error("CelesteTAS/SyncCheck", $"Detected failed assertion '{lineText}' in file '{filePath}' line {fileLine}: Expected '{expected}', got '{actual}'");

        currentStatus = SyncCheckResult.Status.UnsafeAction;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.AssertFailed = new SyncCheckResult.AssertFailedInfo(filePath, fileLine, actual, expected);
    }

    /// Indicates that a crash happened while sync-checking
    public static void ReportCrash(string ex) {
        if (!Active) {
            return;
        }

        Logger.Error("CelesteTAS/SyncCheck", $"Detected a crash: {ex}");

        currentStatus = SyncCheckResult.Status.Crash;
        currentAdditionalInformation.Clear();
        currentAdditionalInformation.Crash = new SyncCheckResult.CrashInfo(CurrentFilePath, CurrentFileLine, ex);
    }

    [Initialize]
    private static void Initialize() {
        On.Celeste.Celeste.OnSceneTransition += On_Celeste_OnSceneTransition;

        var handleCriticalError = typeof(CriticalErrorHandler)
            .GetMethodInfo(nameof(CriticalErrorHandler.HandleCriticalError))!;

        // Prevent critical error handler from interrupting TASes while sync checking
        handleCriticalError.OverrideReturn(_ => Active, (ExceptionDispatchInfo error) => error);
        handleCriticalError.HookBefore(Manager.DisableRun);

        // Apply certain patches already done in headless mode, which are required to properly perform a sync check
        if (!Active || Everest.Flags.IsHeadless) {
            return;
        }

        // Skip intro animation
        typeof(GameLoader)
            .GetMethodInfo(nameof(GameLoader.Begin))!
            .HookAfter((GameLoader loader) => loader.skipped = true);

        // Skip auto updates
        typeof(GameLoader)
            .GetMethodInfo(nameof(GameLoader._GetNextScene))!
            .IlHook((cursor, _) => {
                cursor.EmitLdarg0();
                cursor.EmitLdarg1();
                cursor.EmitNewobj(typeof(OverworldLoader).GetConstructor([typeof(Overworld.StartMode), typeof(HiresSnow)])!);
                cursor.EmitRet();
            });

        // Skip OOBE
        typeof(OuiOOBE)
            .GetMethodInfo(nameof(OuiOOBE.IsStart))!
            .IlHook((cursor, _) => {
                cursor.EmitLdcI4(/* false */ 0);
                cursor.EmitRet();
            });
        typeof(OuiTitleScreen)
            .GetMethodInfo(nameof(OuiTitleScreen.IsStart))!
            .IlHook((cursor, _) => {
                cursor.EmitLdarg0();
                cursor.EmitLdarg1();
                cursor.EmitLdarg2();
                cursor.EmitCallvirt(typeof(OuiTitleScreen).GetMethodInfo($"orig_{nameof(OuiTitleScreen.IsStart)}")!);
                cursor.EmitRet();
            });
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

            // Allow scene to initialize itself
            CoreModule.Settings.DebugMode = CoreModuleSettings.VanillaTristate.Everest;
            next.OnEndOfFrame += () => {
                if (fileQueue.TryDequeue(out string? file)) {
                    CheckFile(file);
                } else {
                    // No files provided
                    result.Finished = true;
                    result.WriteToFile(resultFile);

                    Environment.Exit(0);
                }
            };
        }
    }

    /// Starts executing a TAS for sync-checking
    private static void CheckFile(string file) {
        // Reset state
        currentStatus = SyncCheckResult.Status.Success;
        currentAdditionalInformation.Clear();

        Logger.Info("CelesteTAS/SyncCheck", $"Starting check for file: '{file}'");

        Manager.Controller.FilePath = file;
        Manager.EnableRun();
    }

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (Active) {
            if (Manager.Controller.Inputs.Count == 0) {
                ReportRunFinished();
                return;
            }

            // Insert breakpoint at the end
            Manager.Controller.FastForwards[Manager.Controller.Inputs.Count] = new FastForward(Manager.Controller.Inputs.Count, "", Manager.Controller.Inputs[^1].StudioLine);
        }
    }
}
