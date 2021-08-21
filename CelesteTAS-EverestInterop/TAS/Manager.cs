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
using TAS.Utils;

namespace TAS {
    public static class Manager {
        private static readonly FieldInfo SummitVignetteReadyFieldInfo = typeof(SummitVignette).GetFieldInfo("ready");

        private static readonly DUpdateVirtualInputs UpdateVirtualInputs;

        public static bool Running, Recording;
        public static readonly InputController Controller = new();
        public static State LastState, State, NextState;
        public static int FrameLoops = 1;
        public static bool EnforceLegal, AllowUnsafeInput;
        private static bool kbTextInput;

        static Manager() {
            MethodInfo updateVirtualInputs = typeof(MInput).GetMethodInfo("UpdateVirtualInputs");
            UpdateVirtualInputs = (DUpdateVirtualInputs) updateVirtualInputs.CreateDelegate(typeof(DUpdateVirtualInputs));

            AttributeUtils.CollectMethods<EnableRunAttribute>();
            AttributeUtils.CollectMethods<DisableRunAttribute>();
        }

        public static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;
        private static bool ShouldForceState => NextState.HasFlag(State.FrameStep) && !Hotkeys.HotkeyFastForward.OverridePressed;

        public static void Update() {
            LastState = State;
            Hotkeys.Update();
            Savestates.HandleSaveStates();
            HandleFrameRates();
            CheckToEnable();
            FrameStepping();

            if (State.HasFlag(State.Enable)) {
                bool canPlayback = Controller.CanPlayback;
                Running = true;

                /*
                if (State.HasFlag(State.Record)) {
                    controller.RecordPlayer();
                }
                */
                if (!State.HasFlag(State.FrameStep)) {
                    Controller.AdvanceFrame();
                    canPlayback = canPlayback || Controller.CanPlayback;
                    if (Controller.Break && Controller.CurrentFrame < Controller.Inputs.Count) {
                        NextState |= State.FrameStep;
                        FrameLoops = 1;
                    }

                    if (!canPlayback || !AllowUnsafeInput &&
                        !(Engine.Scene is Level or LevelLoader or LevelExit || Controller.CurrentFrame <= 1)) {
                        DisableRun();
                    }
                }
            } else {
                Running = false;
                if (!Engine.Instance.IsActive) {
                    UpdateVirtualInputs();
                    for (int i = 0; i < 4; i++) {
                        if (MInput.GamePads[i].Attached) {
                            MInput.GamePads[i].CurrentState = GamePad.GetState((PlayerIndex) i);
                        }
                    }
                }
            }

            SendStateToStudio();
        }

        public static void SendStateToStudio() {
            StudioInfo studioInfo = new(
                (Controller.Previous?.Line ?? -1),
                Controller.InputCurrentFrame.ToString(),
                Controller.CurrentFrame,
                Controller.Inputs.Count,
                Savestates.StudioHighlightLine,
                State,
                GameInfo.GetStudioInfo(false),
                GameInfo.GetStudioInfo(true),
                GameInfo.LevelName,
                GameInfo.ChapterTime,
                CelesteTasModule.Instance.Metadata.VersionString
            );
            StudioCommunicationClient.Instance?.SendState(studioInfo, !ShouldForceState);
        }

        public static bool IsLoading() {
            if (Engine.Scene is Level level) {
                if (!level.IsAutoSaving()) {
                    return false;
                }

                return level.Session.Level == "end-cinematic";
            }

            if (Engine.Scene is SummitVignette summit) {
                return !(bool) SummitVignetteReadyFieldInfo.GetValue(summit);
            } else if (Engine.Scene is Overworld overworld) {
                return overworld.Current is OuiFileSelect {SlotIndex: >= 0} slot && slot.Slots[slot.SlotIndex].StartingGame;
            }

            bool isLoading = Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby";
            return isLoading;
        }

        public static double GetAngle(Vector2 vector) {
            double angle = 180 / Math.PI * Math.Atan2(vector.Y, vector.X);
            if (angle < -90.01f) {
                return 450 + angle;
            } else {
                return 90 + angle;
            }
        }

        private static void HandleFrameRates() {
            if (State.HasFlag(State.Enable) && !State.HasFlag(State.FrameStep) && !NextState.HasFlag(State.FrameStep) &&
                !State.HasFlag(State.Record)) {
                if (Controller.HasFastForward) {
                    FrameLoops = Controller.FastForwardSpeed;
                    return;
                }

                //q: but euni, why not just use the hotkey system you implemented?
                //a: i have no fucking idea
                if (Hotkeys.IsKeyDown(Settings.KeyFastForward.Keys) || Hotkeys.HotkeyFastForward.OverridePressed) {
                    FrameLoops = 10;
                    return;
                }
            }

            FrameLoops = 1;
        }

        private static void FrameStepping() {
            bool frameAdvance = Hotkeys.HotkeyFrameAdvance.Pressed && !Hotkeys.HotkeyStart.Pressed;
            bool pause = Hotkeys.HotkeyPause.Pressed && !Hotkeys.HotkeyStart.Pressed;

            if (State.HasFlag(State.Enable) && !State.HasFlag(State.Record)) {
                if (NextState.HasFlag(State.FrameStep)) {
                    State |= State.FrameStep;
                    NextState &= ~State.FrameStep;
                }

                if (frameAdvance && !Hotkeys.HotkeyFrameAdvance.WasPressed) {
                    if (!State.HasFlag(State.FrameStep)) {
                        State |= State.FrameStep;
                        NextState &= ~State.FrameStep;
                    } else {
                        State &= ~State.FrameStep;
                        NextState |= State.FrameStep;
                    }
                } else if (pause && !Hotkeys.HotkeyPause.WasPressed) {
                    if (!State.HasFlag(State.FrameStep)) {
                        State |= State.FrameStep;
                        NextState &= ~State.FrameStep;
                    } else {
                        State &= ~State.FrameStep;
                        NextState &= ~State.FrameStep;
                    }
                } else if (LastState.HasFlag(State.FrameStep) && State.HasFlag(State.FrameStep) && Hotkeys.HotkeyFastForward.Pressed) {
                    State &= ~State.FrameStep;
                    NextState |= State.FrameStep;
                }
            }
        }

        private static void CheckToEnable() {
            if (!Savestates.SpeedrunToolInstalled && Hotkeys.HotkeyRestart.Pressed && !Hotkeys.HotkeyRestart.WasPressed) {
                DisableRun();
                EnableRun();
                return;
            }

            if (Hotkeys.HotkeyStart.Pressed) {
                if (!State.HasFlag(State.Enable)) {
                    NextState |= State.Enable;
                } else {
                    NextState |= State.Disable;
                }
            } else if (NextState.HasFlag(State.Enable)) {
                if (Engine.Scene is Level level && (!level.CanPause || Engine.FreezeTimer > 0)) {
                    Controller.RefreshInputs(true);
                    if (Controller.Current != null &&
                        (Controller.Current.HasActions(Actions.Restart) || Controller.Current.HasActions(Actions.Start))) {
                        NextState |= State.Delay;
                        FrameLoops = FastForward.DefaultSpeed;
                        return;
                    }
                }

                EnableRun();
            } else if (NextState.HasFlag(State.Disable)) {
                DisableRun();
            }
        }

        private static void EnableRun() {
            NextState &= ~State.Enable;
            InitializeRun(false);
            kbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;
            AttributeUtils.Invoke<EnableRunAttribute>();
        }

        private static void DisableRun() {
            Running = false;
            /*
            if (Recording) {
                controller.WriteInputs();
            }
            */
            Recording = false;
            State = State.None;
            NextState = State.None;

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

        public static void EnableExternal() => EnableRun();

        public static void DisableExternal() => DisableRun();

        private static void InitializeRun(bool recording) {
            State |= State.Enable;
            State &= ~State.FrameStep;
            if (recording) {
                Recording = recording;
                State |= State.Record;
                Controller.InitializeRecording();
            } else {
                State &= ~State.Record;
                Controller.RefreshInputs(true);
                Running = true;
            }
        }

        public static void SetInputs(InputFrame input) {
            GamePadDPad pad = default;
            GamePadThumbSticks sticks = default;
            GamePadState state = default;
            if (input.HasActions(Actions.Feather)) {
                SetFeather(input, ref pad, ref sticks);
            } else {
                SetDPad(input, ref pad, ref sticks);
            }

            SetState(input, ref state, ref pad, ref sticks);

            bool found = false;
            for (int i = 0; i < 4; i++) {
                MInput.GamePads[i].Update();
                if (MInput.GamePads[i].Attached) {
                    found = true;
                    MInput.GamePads[i].CurrentState = state;
                }
            }

            if (!found) {
                MInput.GamePads[0].CurrentState = state;
                MInput.GamePads[0].Attached = true;
            }

            if (input.HasActions(Actions.Confirm)) {
                MInput.Keyboard.CurrentState = new KeyboardState(Keys.Enter);
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

        private static void SetState(InputFrame input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
            state = new GamePadState(
                sticks,
                new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
                new GamePadButtons(
                    (input.HasActions(Actions.Jump) ? BindingHelper.JumpAndConfirm : 0)
                    | (input.HasActions(Actions.Jump2) ? BindingHelper.Jump2 : 0)
                    | (input.HasActions(Actions.DemoDash) ? BindingHelper.DemoDash : 0)
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