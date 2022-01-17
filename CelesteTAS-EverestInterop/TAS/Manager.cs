using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS {
    public static class Manager {
        private static readonly Func<SummitVignette, bool> SummitVignetteReady = "ready".CreateDelegate_Get<SummitVignette, bool>();

        private static readonly DUpdateVirtualInputs UpdateVirtualInputs;

        public static bool Running, Recording;
        public static readonly InputController Controller = new();
        public static States LastStates, States, NextStates;
        public static int FrameLoops { get; private set; } = 1;
        public static bool UltraFastForwarding => FrameLoops >= 100 && Running;
        public static bool EnforceLegal, AllowUnsafeInput;
        private static bool kbTextInput;

        static Manager() {
            MethodInfo updateVirtualInputs = typeof(MInput).GetMethodInfo("UpdateVirtualInputs");
            UpdateVirtualInputs = (DUpdateVirtualInputs) updateVirtualInputs.CreateDelegate(typeof(DUpdateVirtualInputs));

            AttributeUtils.CollectMethods<EnableRunAttribute>();
            AttributeUtils.CollectMethods<DisableRunAttribute>();
        }

        private static bool ShouldForceState => NextStates.HasFlag(States.FrameStep) && !Hotkeys.FastForward.OverrideCheck;

        public static void Update() {
            LastStates = States;
            Hotkeys.Update();
            Savestates.HandleSaveStates();
            HandleFrameRates();
            CheckToEnable();
            FrameStepping();

            if (States.HasFlag(States.Enable)) {
                Running = true;

                /*
                if (State.HasFlag(State.Record)) {
                    controller.RecordPlayer();
                }
                */
                if (!States.HasFlag(States.FrameStep)) {
                    Controller.AdvanceFrame(out bool canPlayback);

                    // stop TAS if breakpoint is not placed at the end
                    if (Controller.Break && Controller.CanPlayback) {
                        Controller.NextCommentFastForward = null;
                        NextStates |= States.FrameStep;
                        FrameLoops = 1;
                    }

                    if (!canPlayback || !AllowUnsafeInput &&
                        !(Engine.Scene is Level or LevelLoader or LevelExit || Controller.CurrentFrameInTas <= 1)) {
                        DisableRun();
                    }
                }
            } else {
                Running = false;
                if (!Engine.Instance.IsActive) {
                    // MInput.Keyboard.UpdateNull();
                    MInput.Keyboard.PreviousState = MInput.Keyboard.CurrentState;
                    MInput.Keyboard.CurrentState = default;

                    // MInput.Mouse.UpdateNull();
                    MInput.Mouse.PreviousState = MInput.Mouse.CurrentState;
                    MInput.Mouse.CurrentState = default;

                    for (int i = 0; i < 4; i++) {
                        if (MInput.Active) {
                            MInput.GamePads[i].Update();
                        } else {
                            MInput.GamePads[i].UpdateNull();
                        }
                    }

                    UpdateVirtualInputs();
                }
            }

            SendStateToStudio();
        }

        private static void HandleFrameRates() {
            if (States.HasFlag(States.Enable) && !States.HasFlag(States.FrameStep) && !NextStates.HasFlag(States.FrameStep) &&
                !States.HasFlag(States.Record)) {
                if (Controller.HasFastForward) {
                    FrameLoops = Controller.FastForwardSpeed;
                    return;
                }

                //q: but euni, why not just use the hotkey system you implemented?
                //a: i have no fucking idea
                if (Hotkeys.FastForward.Check) {
                    FrameLoops = Math.Max(1, (int) Math.Round(Hotkeys.FastForward.Value * 10));
                    return;
                }
            }

            FrameLoops = 1;
        }

        private static void FrameStepping() {
            bool frameAdvance = Hotkeys.FrameAdvance.Check && !Hotkeys.StartStop.Check;
            bool pause = Hotkeys.PauseResume.Check && !Hotkeys.StartStop.Check;

            if (States.HasFlag(States.Enable) && !States.HasFlag(States.Record)) {
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
                } else if (LastStates.HasFlag(States.FrameStep) && States.HasFlag(States.FrameStep) && Hotkeys.FastForward.Check &&
                           !Hotkeys.FastForwardComment.Check) {
                    States &= ~States.FrameStep;
                    NextStates |= States.FrameStep;
                }
            }
        }

        private static void CheckToEnable() {
            if (!Savestates.SpeedrunToolInstalled && Hotkeys.Restart.Pressed) {
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

            NextStates &= ~States.Enable;
            InitializeRun(false);
            AttributeUtils.Invoke<EnableRunAttribute>();
        }

        public static void DisableRun() {
            Running = false;
            /*
            if (Recording) {
                controller.WriteInputs();
            }
            */
            Recording = false;
            States = States.None;
            NextStates = States.None;

            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = kbTextInput;

            EnforceLegal = false;
            AllowUnsafeInput = false;

            // fix the input that was last held stays for a frame when it ends
            if (MInput.GamePads != null && MInput.GamePads.FirstOrDefault(data => data.Attached) is { } gamePadData) {
                gamePadData.CurrentState = new GamePadState();
            }

            AttributeUtils.Invoke<DisableRunAttribute>();
            Controller.Stop();
        }

        private static void InitializeRun(bool recording) {
            States |= States.Enable;
            States &= ~States.FrameStep;
            if (recording) {
                Recording = recording;
                States |= States.Record;
                Controller.InitializeRecording();
            } else {
                States &= ~States.Record;
                Controller.RefreshInputs(true);
                Running = true;
            }
        }

        public static void SendStateToStudio() {
            StudioInfo studioInfo = new(
                Controller.Previous?.Line ?? -1,
                $"{Controller.CurrentFrameInInput}{Controller.Previous?.RepeatString ?? ""}",
                Controller.CurrentFrameInTas,
                Controller.Inputs.Count,
                Savestates.StudioHighlightLine,
                (int) States,
                GameInfo.StudioInfo,
                GameInfo.LevelName,
                GameInfo.ChapterTime,
                CelesteTasModule.Instance.Metadata.VersionString
            );
            StudioCommunicationClient.Instance?.SendState(studioInfo, !ShouldForceState);
        }

        public static bool IsLoading() {
            switch (Engine.Scene) {
                case Level level:
                    return level.IsAutoSaving() && level.Session.Level == "end-cinematic";
                case SummitVignette summit:
                    return !SummitVignetteReady(summit);
                case Overworld overworld:
                    return overworld.Current is OuiFileSelect {SlotIndex: >= 0} slot && slot.Slots[slot.SlotIndex].StartingGame;
                default:
                    bool isLoading = Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby";
                    return isLoading;
            }
        }

        public static void SetInputs(InputFrame input) {
            GamePadDPad pad = default;
            GamePadThumbSticks sticks = default;
            GamePadState gamePadState = default;
            if (input.HasActions(Actions.Feather)) {
                SetFeather(input, ref pad, ref sticks);
            } else {
                SetDPad(input, ref pad, ref sticks);
            }

            SetGamePadState(input, ref gamePadState, ref pad, ref sticks);

            bool found = false;
            for (int i = 0; i < 4; i++) {
                MInput.GamePads[i].Update();
                if (MInput.GamePads[i].Attached) {
                    found = true;
                    MInput.GamePads[i].CurrentState = gamePadState;
                }
            }

            if (!found) {
                MInput.GamePads[0].CurrentState = gamePadState;
                MInput.GamePads[0].Attached = true;
            }

            MInput.Keyboard.PreviousState = MInput.Keyboard.CurrentState;
            if (input.HasActions(Actions.Confirm)) {
                MInput.Keyboard.CurrentState = new KeyboardState(BindingHelper.Confirm2);
            } else {
                MInput.Keyboard.CurrentState = new KeyboardState();
            }

            UpdateVirtualInputs();
        }

        private static void SetFeather(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
            pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            sticks = new GamePadThumbSticks(input.AngleVector2, new Vector2(0, 0));
        }

        private static void SetDPad(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
            pad = new GamePadDPad(
                input.HasActions(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
                input.HasActions(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
                input.HasActions(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
                input.HasActions(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
            );
            sticks = new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0));
        }

        private static void SetGamePadState(InputFrame input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
            state = new GamePadState(
                sticks,
                new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
                new GamePadButtons(
                    (input.HasActions(Actions.Jump) ? BindingHelper.JumpAndConfirm : 0)
                    | (input.HasActions(Actions.Jump2) ? BindingHelper.Jump2 : 0)
                    | (input.HasActions(Actions.DemoDash) ? BindingHelper.DemoDash : 0)
                    | (input.HasActions(Actions.DemoDash2) ? BindingHelper.DemoDash2 : 0)
                    | (input.HasActions(Actions.Dash) ? BindingHelper.DashAndTalkAndCancel : 0)
                    | (input.HasActions(Actions.Dash2) ? BindingHelper.Dash2AndCancel : 0)
                    | (input.HasActions(Actions.Grab) ? BindingHelper.Grab : 0)
                    | (input.HasActions(Actions.Start) ? BindingHelper.Pause : 0)
                    | (input.HasActions(Actions.Restart) ? BindingHelper.QuickRestart : 0)
                    | (input.HasActions(Actions.Up) ? BindingHelper.Up : 0)
                    | (input.HasActions(Actions.Down) ? BindingHelper.Down : 0)
                    | (input.HasActions(Actions.Left) ? BindingHelper.Left : 0)
                    | (input.HasActions(Actions.Right) ? BindingHelper.Right : 0)
                    | (input.HasActions(Actions.Journal) ? BindingHelper.Journal : 0)
                ),
                pad
            );
        }


        //The things we do for faster replay times
        private delegate void DUpdateVirtualInputs();
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class EnableRunAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    internal class DisableRunAttribute : Attribute { }
}