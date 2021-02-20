using System;
using System.Reflection;
using System.Threading.Tasks;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.Input;
using TAS.StudioCommunication;

namespace TAS {
    [Flags]
    public enum State {
        None = 0,
        Enable = 1,
        Record = 2,
        FrameStep = 4,
        Disable = 8,
        Delay = 16
    }

    public static partial class Manager {
        private static readonly FieldInfo SummitVignetteReadyFieldInfo = typeof(SummitVignette).GetFieldInfo("ready");
        private static readonly FieldInfo StrawberryCollectTimerFieldInfo = typeof(Strawberry).GetFieldInfo("collectTimer");

        private static readonly DUpdateVirtualInputs UpdateVirtualInputs;
        private static readonly DWallJumpCheck WallJumpCheck;
        private static readonly GetBerryFloat StrawberryCollectTimer;
        private static readonly GetFloat DashCooldownTimer;
        private static readonly GetFloat JumpGraceTimer;
        private static readonly GetPlayerSeekerSpeed PlayerSeekerSpeed;
        private static readonly GetPlayerSeekerDashTimer PlayerSeekerDashTimer;

        public static bool Running, Recording;
        public static InputController Controller = new InputController();
        public static State LastState, State, NextState;
        public static string CurrentStatus, PlayerStatus = "";
        public static int FrameLoops = 1;
        public static bool EnforceLegal, AllowUnsafeInput;
        public static Vector2 LastPos;
        public static Vector2 LastPlayerSeekerPos;
        public static bool KbTextInput;

        private static long lastTimer;
        private static Task checkHotkeyStarTask;

        private static bool featherInput;

        static Manager() {
            MethodInfo wallJumpCheck = typeof(Player).GetMethodInfo("WallJumpCheck");
            MethodInfo updateVirtualInputs = typeof(MInput).GetMethodInfo("UpdateVirtualInputs");

            FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");
            FieldInfo dashCooldownTimer = typeof(Player).GetFieldInfo("dashCooldownTimer");
            FieldInfo jumpGraceTimer = typeof(Player).GetFieldInfo("jumpGraceTimer");
            FieldInfo playerSeekerSpeed = typeof(PlayerSeeker).GetFieldInfo("speed");
            FieldInfo playerSeekerDashTimer = typeof(PlayerSeeker).GetFieldInfo("dashTimer");


            Manager.UpdateVirtualInputs = (DUpdateVirtualInputs) updateVirtualInputs.CreateDelegate(typeof(DUpdateVirtualInputs));
            Manager.WallJumpCheck = (DWallJumpCheck) wallJumpCheck.CreateDelegate(typeof(DWallJumpCheck));

            StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
            DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
            JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
            PlayerSeekerSpeed = playerSeekerSpeed.CreateDelegate_Get<GetPlayerSeekerSpeed>();
            PlayerSeekerDashTimer = playerSeekerDashTimer.CreateDelegate_Get<GetPlayerSeekerDashTimer>();
        }

        public static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;
        private static bool ShouldForceState => HasFlag(NextState, State.FrameStep) && !Hotkeys.HotkeyFastForward.OverridePressed;

        public static void Update() {
            LastState = State;
            Hotkeys.Update();
            Savestates.HandleSaveStates();
            Savestates.Routine?.Update();
            HandleFrameRates();
            CheckToEnable();
            FrameStepping();

            if (HasFlag(State, State.Enable)) {
                bool canPlayback = Controller.CanPlayback;
                Running = true;

                if (HasFlag(State, State.FrameStep)) {
                    StudioCommunicationClient.Instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
                    return;
                }
                /*
                if (HasFlag(state, State.Record)) {
                    controller.RecordPlayer();
                }
                */
                else {
                    Controller.AdvanceFrame();
                    if (Controller.Break && Controller.CurrentFrame < Controller.Inputs.Count) {
                        NextState |= State.FrameStep;
                        FrameLoops = 1;
                    }

                    if (!canPlayback || (!AllowUnsafeInput &&
                                         !(Engine.Scene is Level || Engine.Scene is LevelLoader || Engine.Scene is LevelExit ||
                                           Controller.CurrentFrame <= 1))) {
                        DisableRun();
                    }
                }

                if (canPlayback && Controller.CurrentFrame > 0) {
                    UpdateManagerStatus();
                }
            } else {
                Running = false;
                CurrentStatus = null;
                if (!Engine.Instance.IsActive) {
                    UpdateVirtualInputs();
                    for (int i = 0; i < 4; i++) {
                        if (MInput.GamePads[i].Attached) {
                            MInput.GamePads[i].CurrentState = GamePad.GetState((PlayerIndex) i);
                        }
                    }
                }
            }

            StudioCommunicationClient.Instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
        }

        public static void UpdateManagerStatus() {
            string status = string.Join(",", new object[] {
                Controller.Previous.Line,
                Controller.StudioFrameCount,
                Controller.CurrentFrame,
                Controller.Inputs.Count,
                Savestates.StudioHighlightLine
            });
            CurrentStatus = status;
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
                return overworld.Current is OuiFileSelect slot && slot.SlotIndex >= 0 && slot.Slots[slot.SlotIndex].StartingGame;
            }

            bool isLoading = (Engine.Scene is LevelExit) || (Engine.Scene is LevelLoader) || (Engine.Scene is GameLoader) ||
                             Engine.Scene.GetType().Name == "LevelExitToLobby";
            return isLoading;
        }

        public static float GetAngle(Vector2 vector) {
            float angle = 360f / 6.283186f * Calc.Angle(vector);
            if (angle < -90.01f) {
                return 450f + angle;
            } else {
                return 90f + angle;
            }
        }

        private static void HandleFrameRates() {
            if (HasFlag(State, State.Enable) && !HasFlag(State, State.FrameStep) && !HasFlag(NextState, State.FrameStep) &&
                !HasFlag(State, State.Record)) {
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

            if (HasFlag(State, State.Enable) && !HasFlag(State, State.Record)) {
                if (HasFlag(NextState, State.FrameStep)) {
                    State |= State.FrameStep;
                    NextState &= ~State.FrameStep;
                }

                if (frameAdvance && !Hotkeys.HotkeyFrameAdvance.WasPressed) {
                    if (!HasFlag(State, State.FrameStep)) {
                        State |= State.FrameStep;
                        NextState &= ~State.FrameStep;
                    } else {
                        State &= ~State.FrameStep;
                        NextState |= State.FrameStep;
                    }
                } else if (pause && !Hotkeys.HotkeyPause.WasPressed) {
                    if (!HasFlag(State, State.FrameStep)) {
                        State |= State.FrameStep;
                        NextState &= ~State.FrameStep;
                    } else {
                        State &= ~State.FrameStep;
                        NextState &= ~State.FrameStep;
                    }
                } else if (HasFlag(LastState, State.FrameStep) && HasFlag(State, State.FrameStep) && Hotkeys.HotkeyFastForward.Pressed) {
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
                if (!HasFlag(State, State.Enable) && checkHotkeyStarTask == null) {
                    NextState |= State.Enable;
                } else {
                    NextState |= State.Disable;
                }
            } else if (HasFlag(NextState, State.Enable)) {
                if (Engine.Scene is Level level && (!level.CanPause || Engine.FreezeTimer > 0)) {
                    Controller.RefreshInputs(true);
                    if (Controller.Current.HasActions(Actions.Restart) || Controller.Current.HasActions(Actions.Start)) {
                        NextState |= State.Delay;
                        FrameLoops = FastForward.DefaultFastForwardSpeed;
                        return;
                    }
                }

                EnableRun();
            } else if (HasFlag(NextState, State.Disable)) {
                DisableRun();
            }
        }

        private static void EnableRun() {
            NextState &= ~State.Enable;
            InitializeRun(false);
            BindingHelper.SetTasBindings();
            KbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;

            checkHotkeyStarTask = Task.Run(() => {
                while (Running || Hotkeys.HotkeyStart.Pressed) {
                    if (FrameLoops > 100) {
                        if (Running && Hotkeys.HotkeyStart.Pressed) {
                            DisableRun();
                        }
                    }
                }

                checkHotkeyStarTask = null;
            });

            RestoreSettings.TryBackup();
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
            BindingHelper.RestorePlayerBindings();
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = KbTextInput;
            Controller.ResetSpawn = null;
            if (ExportSyncData) {
                EndExport();
                ExportSyncData = false;
            }

            LibTasHelper.EndExport();

            EnforceLegal = false;
            AllowUnsafeInput = false;
            AnalogHelper.AnalogModeChange(AnalogueMode.Ignore);
            Hotkeys.ReleaseAllKeys();
            RestoreSettings.TryRestore();
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

        public static bool HasFlag(State state, State flag) => (state & flag) == flag;

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
            Vector2 aim = ValidateFeatherInput(input);
            sticks = new GamePadThumbSticks(aim, new Vector2(0, 0));
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

        private static Vector2 ValidateFeatherInput(InputFrame input) {
            return AnalogHelper.ComputeFeather(input.GetX(), input.GetY());
        }

        //The things we do for faster replay times
        private delegate void DUpdateVirtualInputs();

        private delegate bool DWallJumpCheck(Player player, int dir);

        private delegate float GetBerryFloat(Strawberry berry);

        private delegate float GetFloat(Player player);

        private delegate Vector2 GetPlayerSeekerSpeed(PlayerSeeker playerSeeker);

        private delegate float GetPlayerSeekerDashTimer(PlayerSeeker playerSeeker);
    }
}