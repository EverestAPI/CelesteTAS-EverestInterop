using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Celeste.Pico8;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class EnableRunAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class DisableRunAttribute : Attribute;

public static class Manager {
    public enum State {
        /// No TAS is currently active
        Disabled,
        /// Plays the current TAS back at normal speed
        Running,
        /// Pauses the current TAS
        Paused,
        /// Advances the current TAS by 1 frame and resets back to Paused
        FrameAdvance,
        /// Forwards the TAS while paused
        SlowForward,
        /// Fast-forwards the current TAS to the next breakpoint
        FastForward,
    }

#if true

    public static bool Running => CurrState != State.Disabled;
    public static float PlaybackSpeed { get; private set; } = 1.0f;
    public static bool FastForwarding => CurrState == State.FastForward;

    public static State CurrState, NextState;

    public static readonly InputController Controller = new();

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    static Manager() {
        AttributeUtils.CollectMethods<EnableRunAttribute>();
        AttributeUtils.CollectMethods<DisableRunAttribute>();
    }

    public static void EnableRun()
    {
        if (Running) {
            return;
        }

        $"Starting TAS: {Controller.FilePath}".Log();

        CurrState = NextState = State.Running;
        AttributeUtils.Invoke<EnableRunAttribute>();
        Controller.Stop();
        Controller.RefreshInputs();
    }

    public static void DisableRun()
    {
        if (!Running) {
            return;
        }

        Environment.StackTrace.Log(LogLevel.Verbose);
        "Stopping TAS".Log();

        CurrState = NextState = State.Disabled;
        AttributeUtils.Invoke<DisableRunAttribute>();
        Controller.Stop();
    }

    /// Will start the TAS on the next update cycle
    public static void EnableRunLater() => NextState = State.Running;
    /// Will stop the TAS on the next update cycle
    public static void DisableRunLater() => NextState = State.Disabled;

    public static void Update() {
        if (!Running && NextState == State.Running) {
            EnableRun();
        }
        if (Running && NextState == State.Disabled) {
            DisableRun();
        }

        CurrState = NextState;

        while (mainThreadActions.TryDequeue(out Action action)) {
            action.Invoke();
        }

        if (Running && !IsPaused()) {
            if (Controller.HasFastForward) {
                NextState = State.FastForward;
                PlaybackSpeed = Controller.CurrentFastForward!.Speed;
            }

            Controller.AdvanceFrame();

            // Pause the TAS if breakpoint is not placed at the end
            if (Controller.Break && Controller.CanPlayback) {
                Controller.NextLabelFastForward = null;
                NextState = State.Paused;
                PlaybackSpeed = 1.0f;
                return;
            }

            if (!Controller.CanPlayback) {
                DisableRun();
            }
        }
    }

    public static void UpdateHotkeys() {
        Hotkeys.Update();

        // Check if the TAS should be enabled / disabled
        if (Hotkeys.StartStop.Pressed) {
            if (Running) {
                DisableRun();
            } else {
                EnableRun();
            }
            return;
        }

        // Do not use Hotkeys.Restart.Pressed unless the fast forwarding optimization in Hotkeys.Update() is removed
        if (Hotkeys.Restart.Released) {
            DisableRun();
            EnableRun();
            return;
        }

        if (Running && Hotkeys.FastForwardComment.Pressed) {
            Controller.FastForwardToNextLabel();
            return;
        }

        switch (CurrState) {
            case State.Running:
                if (Hotkeys.PauseResume.Pressed) {
                    NextState = State.Paused;
                }
                break;

            case State.FrameAdvance:
                NextState = State.Paused;
                break;

            case State.Paused:
                if (Hotkeys.PauseResume.Pressed) {
                    NextState = State.Running;
                } else if (Hotkeys.FrameAdvance.Pressed || Hotkeys.FastForward.Check) {
                    NextState = State.FrameAdvance;
                }
                break;

            case State.Disabled:
            default:
                break;
        }

        // Apply fast / slow forwarding
        switch (NextState)
        {
            case State.Paused or State.SlowForward when Hotkeys.SlowForward.Check:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                NextState = State.SlowForward;
                break;
            case State.Paused or State.SlowForward:
                PlaybackSpeed = 1.0f;
                NextState = State.Paused;
                break;

            case State.Running when Hotkeys.FastForward.Check:
                PlaybackSpeed = TasSettings.FastForwardSpeed;
                break;
            case State.Running when Hotkeys.SlowForward.Check:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                break;

            default:
                if (CurrState != State.FastForward) {
                    PlaybackSpeed = 1.0f;
                }
                break;
        }
    }

    /// Queues an action to be performed on the main thread
    public static void AddMainThreadAction(Action action) {
        mainThreadActions.Enqueue(action);
    }

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

    public static bool IsPaused() => CurrState == State.Paused && !IsLoading();

#else

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    public static bool Running;
    public static bool Recording => TASRecorderUtils.Recording;
    public static readonly InputController Controller = new();
    public static States LastStates, States, NextStates;
    public static float FrameLoops { get; private set; } = 1f;
    public static bool UltraFastForwarding => FrameLoops >= 100 && Running;
    public static bool SlowForwarding => FrameLoops < 1f;
    public static bool AdvanceThroughHiddenFrame;

    private static bool SkipSlowForwardingFrame =>
        FrameLoops < 1f && (int) ((Engine.FrameCounter + 1) * FrameLoops) == (int) (Engine.FrameCounter * FrameLoops);

    public static bool SkipFrame => (States.Has(States.FrameStep) || SkipSlowForwardingFrame) && !AdvanceThroughHiddenFrame;

    static Manager() {
        AttributeUtils.CollectMethods<EnableRunAttribute>();
        AttributeUtils.CollectMethods<DisableRunAttribute>();
    }

    private static bool ShouldForceState =>
        NextStates.Has(States.FrameStep) && !Hotkeys.FastForward.OverrideCheck && !Hotkeys.SlowForward.OverrideCheck;

    public static void AddMainThreadAction(Action action) {
        if (Thread.CurrentThread == MainThreadHelper.MainThread) {
            action();
        } else {
            mainThreadActions.Enqueue(action);
        }
    }

    private static void ExecuteMainThreadActions() {
        while (mainThreadActions.TryDequeue(out Action action)) {
            action.Invoke();
        }
    }

    public static void Update() {
        LastStates = States;
        ExecuteMainThreadActions();
        Hotkeys.Update();
        Savestates.HandleSaveStates();
        HandleFrameRates();
        CheckToEnable();
        FrameStepping();

        if (States.Has(States.Enable)) {
            Running = true;

            if (!SkipFrame) {
                Controller.AdvanceFrame(out bool canPlayback);

                // stop TAS if breakpoint is not placed at the end
                if (Controller.Break && Controller.CanPlayback && !Recording) {
                    Controller.NextCommentFastForward = null;
                    NextStates |= States.FrameStep;
                    FrameLoops = 1;
                }

                if (!canPlayback) {
                    DisableRun();
                } else if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
                    if (Engine.Scene is not (Level or LevelLoader or LevelExit or Emulator or LevelEnter)) {
                        DisableRun();
                    } else if (Engine.Scene is Level level && level.Tracker.GetEntity<TextMenu>() is { } menu) {
                        TextMenu.Item item = menu.Items.FirstOrDefault();
                        if (item is TextMenu.Header {Title: { } title} &&
                            (title == Dialog.Clean("OPTIONS_TITLE") || title == Dialog.Clean("MENU_VARIANT_TITLE") ||
                             title == Dialog.Clean("MODOPTIONS_EXTENDEDVARIANTS_PAUSEMENU_BUTTON").ToUpperInvariant()) ||
                            item is TextMenuExt.HeaderImage {Image: "menu/everest"}) {
                            DisableRun();
                        }
                    }
                }
            }
        } else {
            Running = false;
        }

        SendStateToStudio();
    }

    private static void HandleFrameRates() {
        FrameLoops = 1;

        // Keep frame rate consistant while recording
        if (Recording) {
            return;
        }

        if (States.Has(States.Enable) && !States.Has(States.FrameStep) && !NextStates.Has(States.FrameStep)) {
            if (Controller.HasFastForward) {
                FrameLoops = Controller.FastForwardSpeed;
            }

            if (Hotkeys.FastForward.Check) {
                FrameLoops = TasSettings.FastForwardSpeed;
            } else if (Hotkeys.SlowForward.Check) {
                FrameLoops = TasSettings.SlowForwardSpeed;
            } else if (Math.Round(Hotkeys.RightThumbSticksX * TasSettings.FastForwardSpeed) is var fastForwardSpeed and >= 2) {
                FrameLoops = (int) fastForwardSpeed;
            } else if (Hotkeys.RightThumbSticksX < 0f &&
                       (1 + Hotkeys.RightThumbSticksX) * TasSettings.SlowForwardSpeed is var slowForwardSpeed and <= 0.9f) {
                FrameLoops = Math.Max(slowForwardSpeed, FastForward.MinSpeed);
            }
        }
    }

    private static void FrameStepping() {
        bool frameAdvance = Hotkeys.FrameAdvance.Check && !Hotkeys.StartStop.Check;
        bool pause = Hotkeys.PauseResume.Check && !Hotkeys.StartStop.Check;

        if (States.Has(States.Enable)) {
            if (NextStates.Has(States.FrameStep)) {
                States |= States.FrameStep;
                NextStates &= ~States.FrameStep;
            }

            if (frameAdvance && !Hotkeys.FrameAdvance.LastCheck && !Recording) {
                if (!States.Has(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates |= States.FrameStep;
                }
            } else if (pause && !Hotkeys.PauseResume.LastCheck && !Recording) {
                if (!States.Has(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates &= ~States.FrameStep;
                }
            } else if (LastStates.Has(States.FrameStep) && States.Has(States.FrameStep) &&
                       (Hotkeys.FastForward.Check || Hotkeys.SlowForward.Check && Engine.FrameCounter % Math.Round(4 / TasSettings.SlowForwardSpeed) == 0) &&
                       !Hotkeys.FastForwardComment.Check) {
                States &= ~States.FrameStep;
                NextStates |= States.FrameStep;
            }
        }
    }

    private static void CheckToEnable() {
        // Do not use Hotkeys.Restart.Pressed unless the fast forwarding optimization in Hotkeys.Update() is removed
        if (!Savestates.SpeedrunToolInstalled && Hotkeys.Restart.Released) {
            DisableRun();
            EnableRun();
            return;
        }

        if (Hotkeys.StartStop.Check) {
            if (States.Has(States.Enable)) {
                NextStates |= States.Disable;
            } else {
                NextStates |= States.Enable;
            }
        } else if (NextStates.Has(States.Enable)) {
            EnableRun();
        } else if (NextStates.Has(States.Disable)) {
            DisableRun();
        }
    }

    public static void EnableRun() {
        if (Engine.Scene is GameLoader || CriticalErrorHandlerFixer.Handling) {
            Running = false;
            LastStates = States.None;
            States = States.None;
            NextStates = States.None;
            return;
        }

        Running = true;
        States |= States.Enable;
        States &= ~States.FrameStep;
        NextStates &= ~States.Enable;
        AttributeUtils.Invoke<EnableRunAttribute>();
        Controller.RefreshInputs(true);
    }

    public static void DisableRun() {
        Running = false;

        LastStates = States.None;
        States = States.None;
        NextStates = States.None;

        // fix the input that was last held stays for a frame when it ends
        if (MInput.GamePads != null && MInput.GamePads.FirstOrDefault(data => data.Attached) is { } gamePadData) {
            gamePadData.CurrentState = new GamePadState();
        }

        AttributeUtils.Invoke<DisableRunAttribute>();
        Controller.Stop();
    }

    public static void DisableRunLater() {
        NextStates |= States.Disable;
    }

    public static void SendStateToStudio() {
        if (UltraFastForwarding && Engine.FrameCounter % 23 > 0) {
            return;
        }

        var previous = Controller.Previous;
        var state = new StudioState {
            CurrentLine = previous?.Line ?? -1,
            CurrentLineSuffix = $"{Controller.CurrentFrameInInput + (previous?.FrameOffset ?? 0)}{previous?.RepeatString ?? ""}",
            CurrentFrameInTas = Controller.CurrentFrameInTas,
            CurrentFrameInInput = Controller.CurrentFrameInInput,
            TotalFrames = Controller.Inputs.Count,
            SaveStateLine = Savestates.StudioHighlightLine,

            tasStates = States,
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

    public static bool IsLoading() {
        switch (Engine.Scene) {
            case Level level:
                return level.IsAutoSaving() && level.Session.Level == "end-cinematic";
            case SummitVignette summit:
                return !summit.ready;
            case Overworld overworld:
                return overworld.Current is OuiFileSelect {SlotIndex: >= 0} slot && slot.Slots[slot.SlotIndex].StartingGame ||
                       overworld.Next is OuiChapterSelect && UserIO.Saving ||
                       overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings);
            case Emulator emulator:
                return emulator.game == null;
            default:
                bool isLoading = Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby";
                return isLoading;
        }
    }

#endif
}