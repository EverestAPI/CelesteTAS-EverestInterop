using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS;

public static class Manager {
    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    public static bool Running;
    public static readonly InputController Controller = new();
    public static States LastStates, States, NextStates;
    public static float FrameLoops { get; private set; } = 1f;
    public static bool UltraFastForwarding => FrameLoops >= 100 && Running;
    public static bool SlowForwarding => FrameLoops < 1f;
    public static bool AdvanceThroughHiddenFrame;

    private static bool SkipSlowForwardingFrame =>
        FrameLoops < 1f && (int) ((Engine.FrameCounter + 1) * FrameLoops) == (int) (Engine.FrameCounter * FrameLoops);

    public static bool SkipFrame => (States.HasFlag(States.FrameStep) || SkipSlowForwardingFrame) && !AdvanceThroughHiddenFrame;

    static Manager() {
        AttributeUtils.CollectMethods<EnableRunAttribute>();
        AttributeUtils.CollectMethods<DisableRunAttribute>();
    }

    private static bool ShouldForceState =>
        NextStates.HasFlag(States.FrameStep) && !Hotkeys.FastForward.OverrideCheck && !Hotkeys.SlowForward.OverrideCheck;

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

        if (States.HasFlag(States.Enable)) {
            Running = true;

            if (!SkipFrame) {
                Controller.AdvanceFrame(out bool canPlayback);

                // stop TAS if breakpoint is not placed at the end
                if (Controller.Break && Controller.CanPlayback) {
                    Controller.NextCommentFastForward = null;
                    NextStates |= States.FrameStep;
                    FrameLoops = 1;
                }

                if (!canPlayback) {
                    DisableRun();
                } else if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
                    if (Engine.Scene is not (Level or LevelLoader or LevelExit)) {
                        DisableRun();
                    } else if (Engine.Scene is Level level && level.Tracker.GetEntity<TextMenu>() is { } menu) {
                        if (menu.Items.FirstOrDefault() is TextMenu.Header header && header.Title == Dialog.Clean("options_title") ||
                            menu.Items.FirstOrDefault() is TextMenuExt.HeaderImage {Image: "menu/everest"}) {
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

        if (States.HasFlag(States.Enable) && !States.HasFlag(States.FrameStep) && !NextStates.HasFlag(States.FrameStep)) {
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

        if (States.HasFlag(States.Enable)) {
            if (NextStates.HasFlag(States.FrameStep)) {
                States |= States.FrameStep;
                NextStates &= ~States.FrameStep;
            }

            if (frameAdvance && !Hotkeys.FrameAdvance.LastCheck) {
                if (!States.HasFlag(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates |= States.FrameStep;
                }
            } else if (pause && !Hotkeys.PauseResume.LastCheck) {
                if (!States.HasFlag(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates &= ~States.FrameStep;
                }
            } else if (LastStates.HasFlag(States.FrameStep) && States.HasFlag(States.FrameStep) &&
                       (Hotkeys.FastForward.Check || Hotkeys.SlowForward.Check && Engine.FrameCounter % 10 == 0) &&
                       !Hotkeys.FastForwardComment.Check) {
                States &= ~States.FrameStep;
                NextStates |= States.FrameStep;
            }
        }
    }

    private static void CheckToEnable() {
        if (!Savestates.SpeedrunToolInstalled && Hotkeys.Restart.Released) {
            DisableRun();
            EnableRun();
            return;
        }

        if (Hotkeys.StartStop.Check) {
            if (States.HasFlag(States.Enable)) {
                NextStates |= States.Disable;
            } else {
                NextStates |= States.Enable;
            }
        } else if (NextStates.HasFlag(States.Enable)) {
            EnableRun();
        } else if (NextStates.HasFlag(States.Disable)) {
            DisableRun();
        }
    }

    public static void EnableRun() {
        if (Engine.Scene is GameLoader) {
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

        InputFrame previous = Controller.Previous;
        StudioInfo studioInfo = new(
            previous?.Line ?? -1,
            $"{Controller.CurrentFrameInInput + (previous?.FrameOffset ?? 0)}{previous?.RepeatString ?? ""}",
            Controller.CurrentFrameInTas,
            Controller.Inputs.Count,
            Savestates.StudioHighlightLine,
            (int) States,
            GameInfo.StudioInfo,
            GameInfo.LevelName,
            GameInfo.ChapterTime
        );
        StudioCommunicationClient.Instance?.SendState(studioInfo, !ShouldForceState);
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
            default:
                bool isLoading = Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby";
                return isLoading;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal class EnableRunAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class DisableRunAttribute : Attribute { }