using System;
using System.Collections.Concurrent;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using Celeste.Pico8;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using System.Threading.Tasks;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Module;
using TAS.Playback;
using TAS.Tools;
using TAS.Utils;

namespace TAS;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class EnableRunAttribute(int priority = 0) : EventAttribute(priority);

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class DisableRunAttribute(int priority = 0) : EventAttribute(priority);

/// Causes the method to be called every real-time frame, even if a TAS is currently running / paused
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class UpdateMetaAttribute(int priority = 0) : EventAttribute(priority);

/// Main controller, which manages how the TAS is played back
public static class Manager {
    public enum State {
        /// No TAS is currently active
        Disabled,
        /// Plays the current TAS back at the specified PlaybackSpeed
        Running,
        /// Pauses the current TAS
        Paused,
        /// Advances the current TAS by 1 frame and resets back to Paused
        FrameAdvance,
        /// Forwards the TAS while paused
        SlowForward,
    }

    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectAllMethods<EnableRunAttribute>();
        AttributeUtils.CollectAllMethods<DisableRunAttribute>();
        AttributeUtils.CollectAllMethods<UpdateMetaAttribute>();
    }

    public static bool Running => CurrState != State.Disabled;
    public static bool FastForwarding => Running && PlaybackSpeed >= 5.0f;
    public static float PlaybackSpeed { get; private set; } = 1.0f;

    public static State CurrState, NextState;
    public static readonly InputController Controller = new();

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    private static PopupToast.Entry? frameStepEofToast = null;
    private static PopupToast.Entry? autoPauseDraft = null;
    private static bool seenAutoPauseToast = false;

    // Allow accumulation of frames to step back, since the operation is time intensive
    internal static int FrameStepBackTargetFrame = -1;
    private const float FrameStepBackTime = 1.0f;
    private static float frameStepBackAmount = 0.0f;
    private static float frameStepBackTimeout = 0.0f;
    private static PopupToast.Entry? frameStepBackToast = null;

#if DEBUG
    // Hot-reloading support
    [Load]
    private static void RestoreStudioTasFilePath() {
        if (Engine.Instance.GetDynamicDataInstance().Get<string>("CelesteTAS_FilePath") is { } filePath) {
            Controller.FilePath = filePath;
        }

        // Stop TAS to avoid blocking reload
        typeof(AssetReloadHelper)
            .GetMethodInfo(nameof(AssetReloadHelper.Do), [typeof(string), typeof(Func<bool, Task>), typeof(bool), typeof(bool)])!
            .IlHook((cursor, _) => {
                var start = cursor.MarkLabel();
                cursor.MoveBeforeLabels();

                cursor.EmitLdarg2(); // bool silent

                // Only disable TAS for non-silent reload actions
                cursor.EmitBrtrue(start);
                cursor.EmitDelegate(DisableRun);
                cursor.EmitDelegate(SavestateManager.ClearAllSavestates); // Clean-up savestates
            });
    }

    [Unload]
    private static void SaveStudioTasFilePath() {
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_FilePath", Controller.FilePath);

        Controller.Stop();
        Controller.Clear();
    }
#endif

    public static void EnableRun() {
        if (Running) {
            return;
        }

        CurrState = NextState = State.Running;
        PlaybackSpeed = 1.0f;

        FrameStepBackTargetFrame = -1;

        Controller.Stop();
        Controller.RefreshInputs();

        if (Controller.Inputs.Count == 0) {
            // Empty / Invalid file
            CurrState = NextState = State.Disabled;
            SyncChecker.ReportRunFinished();
            return;
        }

        AttributeUtils.Invoke<EnableRunAttribute>();

        $"Starting TAS: {Controller.FilePath}".Log();
    }

    public static void DisableRun() {
        if (!Running) {
            return;
        }

        "Stopping TAS".Log();

        AttributeUtils.Invoke<DisableRunAttribute>();

        SyncChecker.ReportRunFinished();
        CurrState = NextState = State.Disabled;
        Controller.Stop();
    }

    /// Will start the TAS on the next update cycle
    public static void EnableRunLater() => NextState = State.Running;
    /// Will stop the TAS on the next update cycle
    public static void DisableRunLater() => NextState = State.Disabled;

    /// Updates the TAS itself
    public static void Update() {
        if (!Running && NextState == State.Running) {
            EnableRun();
        }
        if (Running && NextState == State.Disabled) {
            DisableRun();
        }

        SavestateManager.Update();

        CurrState = NextState;

        while (mainThreadActions.TryDequeue(out var action)) {
            action.Invoke();
        }

        if (!Running || CurrState == State.Paused || IsLoading()) {
            return;
        }

        if (CriticalErrorHandler.CurrentHandler != null) {
            // Always prevent execution inside crash handler, even with Unsafe
            // TODO: Move this after executing the first frame, once Everest fixes scene changes from the crash handler
            DisableRun();
            return;
        }

        if (FrameStepBackTargetFrame > 0) {
            NextState = State.Running;
            PlaybackSpeed = FastForward.DefaultSpeed;
        } else if (Controller.CurrentFastForward is { } forward && forward.Frame > Controller.CurrentFrameInTas) {
            NextState = State.Running;
            PlaybackSpeed = forward.Speed;
        }

        Controller.AdvanceFrame(out bool couldPlayback);

        if (!couldPlayback) {
            DisableRun();
            return;
        }

        // Catch frame step-back
        if (FrameStepBackTargetFrame > 0 && Controller.CurrentFrameInTas >= FrameStepBackTargetFrame) {
            FrameStepBackTargetFrame = -1;
            NextState = State.Paused;
        }
        // Auto-pause at end of drafts
        else if (!Controller.CanPlayback && TasSettings.AutoPauseDraft && IsDraft()) {
            NextState = State.Paused;

            if (CurrState == State.Running && !FastForwarding) {
                float duration = seenAutoPauseToast ? 2.0f : 8.0f;
                if (autoPauseDraft is not { Active: true }) {
                    autoPauseDraft = PopupToast.Show(Dialog.Clean("TAS_AutoPauseToast"), duration);
                } else {
                    autoPauseDraft.Text = Dialog.Clean("TAS_AutoPauseToast");
                    autoPauseDraft.Timeout = duration;
                }

                seenAutoPauseToast = true;
            }
        }
        // Pause the TAS if breakpoint is hit
        // Special-case for end of regular files, to update *Time-commands
        else if (FrameStepBackTargetFrame == -1 && Controller.Break && (Controller.CanPlayback || IsDraft())) {
            Controller.NextLabelFastForward = null;
            NextState = State.Paused;
        }

        // Prevent executing unsafe actions unless explicitly allowed
        if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
            // Only allow specific scenes
            if (Engine.Scene is not (Level or LevelLoader or LevelExit or Emulator or LevelEnter)) {
                SyncChecker.ReportUnsafeAction();
                DisableRun();
            }
            // Disallow modifying options
            else if (Engine.Scene is Level level && level.Tracker.GetEntityTrackIfNeeded<TextMenu>() is { } menu) {
                var item = menu.Items.FirstOrDefault();

                if (item is TextMenu.Header { Title: { } title }
                    && (title == Dialog.Clean("OPTIONS_TITLE") || title == Dialog.Clean("MENU_VARIANT_TITLE")
                        || Dialog.Has("MODOPTIONS_EXTENDEDVARIANTS_PAUSEMENU_BUTTON") && title == Dialog.Clean("MODOPTIONS_EXTENDEDVARIANTS_PAUSEMENU_BUTTON").ToUpperInvariant())
                    || item is TextMenuExt.HeaderImage { Image: "menu/everest" }
                ) {
                    SyncChecker.ReportUnsafeAction();
                    DisableRun();
                }
            }
        }
    }

    /// Updates everything around the TAS itself, like hotkeys, studio-communication, etc.
    public static void UpdateMeta() {
        if (!Hotkeys.Initialized) {
            return; // Still loading
        }

        Hotkeys.UpdateMeta();
        SavestateManager.UpdateMeta();
        AttributeUtils.Invoke<UpdateMetaAttribute>();

        SendStudioState();

        // Pending EnableRun/DisableRun. Prevent overwriting
        if (Running && NextState == State.Disabled || !Running && NextState != State.Disabled) {
            return;
        }

        // Check if the TAS should be enabled / disabled
        if (Hotkeys.StartStop.Pressed) {
            if (Running) {
                DisableRun();
            } else {
                EnableRun();
            }
            return;
        }

        if (Hotkeys.Restart.Pressed) {
            DisableRun();
            EnableRun();
            return;
        }

        if (TASRecorderInterop.IsRecording) {
            // Force recording at 1x playback
            NextState = State.Running;
            PlaybackSpeed = 1.0f;
            return;
        }

        if (frameStepBackAmount > 0.0f) {
            int frames = (int) Math.Round(frameStepBackAmount / Core.PlaybackDeltaTime);
            frameStepBackTimeout -= Core.PlaybackDeltaTime;

            // Advance a frame extra, since otherwise 0s would only be rendered AFTER the lag from the TAS restart
            string text = $"Frame Step Back: -{frames}f   (in {Math.Max(0.0f, frameStepBackTimeout - Core.PlaybackDeltaTime):F2}s)";
            if (frameStepBackToast is not { Active: true }) {
                frameStepBackToast = PopupToast.Show(text);
            } else {
                frameStepBackToast.Text = text;
            }
            frameStepBackToast.Timeout = frameStepBackTimeout;

            if (frameStepBackTimeout <= 0.0f) {
                FrameStepBackTargetFrame = Math.Max(1, Controller.CurrentFrameInTas - frames);

                Controller.Stop();
                CurrState = NextState = State.Running;
                AttributeUtils.Invoke<EnableRunAttribute>();

                frameStepBackTimeout = 0.0f;
                frameStepBackAmount = 0.0f;
            }
        }

        if (Running && Hotkeys.FastForwardComment.Pressed) {
            Controller.FastForwardToNextLabel();
            return;
        }

        switch (CurrState) {
            case State.Running:
                if (Hotkeys.PauseResume.Pressed || Hotkeys.FrameAdvance.Pressed) {
                    NextState = State.Paused;
                } else if (Hotkeys.FrameStepBack.Pressed) {
                    NextState = State.Paused;
                    frameStepBackTimeout = FrameStepBackTime;
                    frameStepBackAmount = Core.PlaybackDeltaTime;
                }
                break;

            case State.FrameAdvance:
                NextState = State.Paused;
                break;

            case State.Paused:
                if (frameStepBackAmount > 0.0f) {
                    if (Hotkeys.FrameStepBack.Repeated) {
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += Core.PlaybackDeltaTime;
                    } else if (Hotkeys.FastForward.Check) {
                        // Fast-forward during pause plays at 0.5x speed (due to alternating advancing / pausing)
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += Core.PlaybackDeltaTime * 2.0f;
                    } else if (Hotkeys.SlowForward.Check) {
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += TasSettings.SlowForwardSpeed;
                    }
                } else if (Hotkeys.FrameStepBack.Repeated) {
                    frameStepBackTimeout = FrameStepBackTime;
                    frameStepBackAmount = Core.PlaybackDeltaTime;
                } else if (Hotkeys.PauseResume.Pressed) {
                    NextState = State.Running;
                } else if (Hotkeys.FrameAdvance.Repeated || Hotkeys.FastForward.Check) {
                    // Prevent frame-advancing into the end of the TAS
                    if (!Controller.CanPlayback) {
                        Controller.RefreshInputs(); // Ensure there aren't any new inputs
                    }
                    if (Controller.CanPlayback) {
                        NextState = State.FrameAdvance;
                    } else {
                        const string text = "Cannot advance further: Reached end-of-file";
                        const float duration = 1.0f;
                        if (frameStepEofToast is not { Active: true }) {
                            frameStepEofToast = PopupToast.Show(text, duration);
                        } else {
                            frameStepEofToast.Text = text;
                            frameStepEofToast.Timeout = duration;
                        }
                    }
                }
                break;

            case State.Disabled:
            default:
                break;
        }

        // Allow altering the playback speed with the right thumb-stick
        float normalSpeed = Hotkeys.RightThumbSticksX switch {
            >=  0.001f => Hotkeys.RightThumbSticksX * TasSettings.FastForwardSpeed,
            <= -0.001f => (1 + Hotkeys.RightThumbSticksX) * TasSettings.SlowForwardSpeed,
            _          => 1.0f,
        };

        // Apply fast / slow forwarding
        switch (NextState) {
            case State.Running when FrameStepBackTargetFrame != -1:
                PlaybackSpeed = FastForward.DefaultSpeed;
                break;
            case State.Running when Hotkeys.FastForward.Check:
                PlaybackSpeed = TasSettings.FastForwardSpeed;
                break;
            case State.Running when Hotkeys.SlowForward.Check:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                break;

            case State.Paused or State.SlowForward when Hotkeys.SlowForward.Check && frameStepBackAmount <= 0.0f:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                NextState = State.SlowForward;
                break;
            case State.Paused or State.SlowForward:
                PlaybackSpeed = normalSpeed;
                NextState = State.Paused;
                break;

            case State.FrameAdvance:
                PlaybackSpeed = normalSpeed;
                break;

            default:
                PlaybackSpeed = Controller.HasFastForward ? Controller.CurrentFastForward!.Speed : normalSpeed;
                break;
        }
    }

    /// Queues an action to be performed on the main thread
    public static void AddMainThreadAction(Action action) {
        mainThreadActions.Enqueue(action);
    }

    /// TAS-execution is paused during loading screens
    public static bool IsLoading() {
        return Engine.Scene switch {
            Level level => level.IsAutoSaving() && level.Session.Level == "end-cinematic",
            SummitVignette summit => !summit.ready,
            Overworld overworld => overworld.Current is OuiFileSelect { SlotIndex: >= 0 } slot && slot.Slots[slot.SlotIndex].StartingGame ||
                                   overworld.Next is OuiChapterSelect && UserIO.Saving ||
                                   overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings),
            Emulator emulator => emulator.game == null,
            _ => Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby",
        };
    }

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() {
        if (Controller.Inputs.GetValueOrDefault(Controller.CurrentFrameInTas) is { } current && current.ParentCommand is { } command && command.Is("SaveAndQuitReenter")) {
            // SaveAndQuitReenter manually adds the optimal S&Q real-time
            return true;
        }

        return Engine.Scene switch {
            Level level => level.IsAutoSaving(),
            SummitVignette summit => !summit.ready,
            Overworld overworld => overworld.Next is OuiChapterSelect && UserIO.Saving ||
                                   overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings),
            LevelExit exit => exit.mode == LevelExit.Mode.Completed && !exit.completeLoaded || UserIO.Saving,
            _ => Engine.Scene is LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby" && UserIO.Saving,
        };
    }

    /// Determine if current TAS file is a draft
    private static bool IsDraft() {
        if (TASRecorderInterop.IsRecording) {
            return false;
        }

        // Require any *Time, alternatively Midway*Time at the end for the TAS to be counted as finished
        return Controller.Commands.Values
            .SelectMany(commands => commands)
            .All(command => !command.Is("FileTime") && !command.Is("ChapterTime") && !command.Is("RealTime"))
        && Controller.Commands.GetValueOrDefault(Controller.Inputs.Count, [])
            .All(command => !command.Is("MidwayFileTime") && !command.Is("MidwayChapterTime") && !command.Is("MidwayRealTime"));
    }

    public static bool PreventSendStudioState = false; // a cursed demand of tas helper's predictor

    internal static void SendStudioState() {
        if (PreventSendStudioState) {
            return;
        }
        var previous = Controller.Previous;
        var state = new StudioState {
            CurrentLine = previous?.StudioLine ?? -1,
            CurrentLineSuffix = $"{Controller.CurrentFrameInInput + (previous?.FrameOffset ?? 0)}{previous?.RepeatString ?? ""}",
            CurrentFrameInTas = Controller.CurrentFrameInTas,
            SaveStateLines = SavestateManager.AllSavestates.Select(state => state.StudioLine).ToArray(),
            PlaybackRunning = CurrState == State.Running,

            FileNeedsReload = Controller.NeedsReload,
            TotalFrames = Controller.Inputs.Count,

            GameInfo = GameInfo.StudioInfo,
            LevelName = GameInfo.LevelName,
            ChapterTime = GameInfo.ChapterTime,

            ShowSubpixelIndicator = TasSettings.InfoSubpixelIndicator && Engine.Scene is Level or Emulator,
        };

        if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
            state.PlayerPosition = (player.Position.X, player.Position.Y);
            state.PlayerPositionRemainder = (player.PositionRemainder.X, player.PositionRemainder.Y);
            state.PlayerSpeed = (player.Speed.X, player.Speed.Y);
        } else if (Engine.Scene is Emulator emulator && emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            state.PlayerPosition = (classicPlayer.x, classicPlayer.y);
            state.PlayerPositionRemainder = (classicPlayer.rem.X, classicPlayer.rem.Y);
            state.PlayerSpeed = (classicPlayer.spd.X, classicPlayer.spd.Y);
        }

        CommunicationWrapper.SendState(state);
    }

    [Monocle.Command("dump_tas", "Dumps the parsed TAS file into the console (CelesteTAS)"), UsedImplicitly]
    private static void CmdDumpTas() {
        if (Controller.NeedsReload) {
            if (Running) {
                "Cannot dump TAS file while running it with unparsed changes".Log(LogLevel.Error);
                return;
            }

            // Pretend to start a TAS. so that AbortTas detection works
            NextState = State.Running;
            Controller.RefreshInputs(forceRefresh: true);
            if (NextState == State.Disabled) {
                "TAS contains errors. Cannot dump to console".ConsoleLog(LogLevel.Error);
                return;
            }
            NextState = State.Disabled;
        }

        $"TAS file dump for '{Controller.FilePath}':".Log();

        var writer = Console.Out;
        for (int i = 0; i < Controller.Inputs.Count;) {
            foreach (var comment in Controller.Comments.GetValueOrDefault(i, [])) {
                writer.WriteLine($"#{comment.Text}");
            }
            foreach (var command in Controller.Commands.GetValueOrDefault(i, [])) {
                if (command.Attribute.ExecuteTiming == ExecuteTiming.Parse) {
                    // Comment-out parse-only commands
                    writer.WriteLine($"# {command.CommandLine.ToString()}");
                } else {
                    writer.WriteLine(command.CommandLine.ToString());
                }
            }
            if (Controller.FastForwards.TryGetValue(i, out var fastForward)) {
                writer.WriteLine(fastForward.ToString());
            }

            writer.WriteLine(Controller.Inputs[i]);
            i += Controller.Inputs[i].Frames;
        }
        writer.Flush();

        "Successfully dumped TAS file to console".ConsoleLog();
    }
}
