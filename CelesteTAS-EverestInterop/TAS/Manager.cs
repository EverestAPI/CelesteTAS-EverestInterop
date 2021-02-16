using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Celeste;
using GameInput = Celeste.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using TAS.Input;
using System.Xml.Serialization;

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
        public enum AnalogueMode {
            Ignore,
            Circle,
            Square,
            Precise,
        }

        private static readonly FieldInfo summitVignetteReady = typeof(SummitVignette).GetFieldInfo("ready");
        private static readonly FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");

        private static readonly d_UpdateVirtualInputs UpdateVirtualInputs;
        private static readonly d_WallJumpCheck WallJumpCheck;
        private static readonly GetBerryFloat StrawberryCollectTimer;
        private static readonly GetFloat DashCooldownTimer;
        private static readonly GetFloat JumpGraceTimer;
        private static readonly GetPlayerSeekerSpeed PlayerSeekerSpeed;
        private static readonly GetPlayerSeekerDashTimer PlayerSeekerDashTimer;

        public static bool Running, Recording;
        public static InputController controller = new InputController();
        public static State lastState, state, nextState;
        public static string CurrentStatus, PlayerStatus = "";
        public static int FrameLoops = 1;
        public static bool enforceLegal, allowUnsafeInput;
        public static Vector2 LastPos;
        public static Vector2 LastPlayerSeekerPos;
        public static Buttons grabButton = Buttons.Back;
        public static bool kbTextInput;

        private static long lastTimer;
        private static List<VirtualButton.Node>[] playerBindings;
        private static Task checkHotkeyStarTask;

        static Manager() {
            MethodInfo WallJumpCheck = typeof(Player).GetMethodInfo("WallJumpCheck");
            MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethodInfo("UpdateVirtualInputs");

            FieldInfo strawberryCollectTimer = typeof(Strawberry).GetFieldInfo("collectTimer");
            FieldInfo dashCooldownTimer = typeof(Player).GetFieldInfo("dashCooldownTimer");
            FieldInfo jumpGraceTimer = typeof(Player).GetFieldInfo("jumpGraceTimer");
            FieldInfo playerSeekerSpeed = typeof(PlayerSeeker).GetFieldInfo("speed");
            FieldInfo playerSeekerDashTimer = typeof(PlayerSeeker).GetFieldInfo("dashTimer");


            Manager.UpdateVirtualInputs = (d_UpdateVirtualInputs)UpdateVirtualInputs.CreateDelegate(typeof(d_UpdateVirtualInputs));
            Manager.WallJumpCheck = (d_WallJumpCheck)WallJumpCheck.CreateDelegate(typeof(d_WallJumpCheck));

            StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
            DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
            JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
            PlayerSeekerSpeed = playerSeekerSpeed.CreateDelegate_Get<GetPlayerSeekerSpeed>();
            PlayerSeekerDashTimer = playerSeekerDashTimer.CreateDelegate_Get<GetPlayerSeekerDashTimer>();
            Ana = new AnalogHelper();
        }

        public static CelesteTASModuleSettings settings => CelesteTASModule.Settings;
        private static bool ShouldForceState => HasFlag(nextState, State.FrameStep) && !Hotkeys.hotkeyFastForward.overridePressed;

        public static void Update() {
            lastState = state;
            Hotkeys.Update();
            Savestates.HandleSaveStates();
            Savestates.routine?.Update();
            HandleFrameRates();
            CheckToEnable();
            FrameStepping();

            if (HasFlag(state, State.Enable)) {
                bool canPlayback = controller.CanPlayback;
                Running = true;

                if (HasFlag(state, State.FrameStep)) {
                    StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
                    return;
                }
                /*
                if (HasFlag(state, State.Record)) {
                    controller.RecordPlayer();
                }
                */
                else {
                    controller.AdvanceFrame();
                    if (controller.Break && controller.CurrentFrame < controller.inputs.Count) {
                        nextState |= State.FrameStep;
                        FrameLoops = 1;
                    }

                    if (!canPlayback || (!allowUnsafeInput &&
                                                    !(Engine.Scene is Level || Engine.Scene is LevelLoader || Engine.Scene is LevelExit ||
                                                      controller.CurrentFrame <= 1)))
                        DisableRun();
                }
                if (canPlayback && controller.CurrentFrame > 0) {
                    UpdateManagerStatus();
                }
            } else {
                Running = false;
                CurrentStatus = null;
                if (!Engine.Instance.IsActive) {
                    UpdateVirtualInputs();
                    for (int i = 0; i < 4; i++) {
                        if (MInput.GamePads[i].Attached) {
                            MInput.GamePads[i].CurrentState = GamePad.GetState((PlayerIndex)i);
                        }
                    }
                }
            }

            StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
        }

        public static void UpdateManagerStatus() {
            string status = string.Join(",", new object[] {
                controller.Previous.Line,
                controller.StudioFrameCount,
                controller.CurrentFrame,
                controller.inputs.Count,
                Savestates.StudioHighlightLine
            });
            CurrentStatus = status;
        }

        public static bool IsLoading() {
            if (Engine.Scene is Level level) {
                if (!level.IsAutoSaving())
                    return false;
                return level.Session.Level == "end-cinematic";
            }

            if (Engine.Scene is SummitVignette summit)
                return !(bool)summitVignetteReady.GetValue(summit);
            else if (Engine.Scene is Overworld overworld)
                return overworld.Current is OuiFileSelect slot && slot.SlotIndex >= 0 && slot.Slots[slot.SlotIndex].StartingGame;
            bool isLoading = (Engine.Scene is LevelExit) || (Engine.Scene is LevelLoader) || (Engine.Scene is GameLoader) ||
                             Engine.Scene.GetType().Name == "LevelExitToLobby";
            return isLoading;
        }

        public static float GetAngle(Vector2 vector) {
            float angle = 360f / 6.283186f * Calc.Angle(vector);
            if (angle < -90.01f)
                return 450f + angle;
            else
                return 90f + angle;
        }

        private static void HandleFrameRates() {
            if (HasFlag(state, State.Enable) && !HasFlag(state, State.FrameStep) && !HasFlag(nextState, State.FrameStep) && !HasFlag(state, State.Record)) {
                if (controller.HasFastForward) {
                    FrameLoops = controller.FastForwardSpeed;
                    return;
                }

                //q: but euni, why not just use the hotkey system you implemented?
                //a: i have no fucking idea
                if (Hotkeys.IsKeyDown(settings.KeyFastForward.Keys) || Hotkeys.hotkeyFastForward.overridePressed) {
                    FrameLoops = 10;
                    return;
                }
            }

            FrameLoops = 1;
        }

        private static void FrameStepping() {
            bool frameAdvance = Hotkeys.hotkeyFrameAdvance.pressed && !Hotkeys.hotkeyStart.pressed;
            bool pause = Hotkeys.hotkeyPause.pressed && !Hotkeys.hotkeyStart.pressed;

            if (HasFlag(state, State.Enable) && !HasFlag(state, State.Record)) {
                if (HasFlag(nextState, State.FrameStep)) {
                    state |= State.FrameStep;
                    nextState &= ~State.FrameStep;
                }

                if (frameAdvance && !Hotkeys.hotkeyFrameAdvance.wasPressed) {
                    if (!HasFlag(state, State.FrameStep)) {
                        state |= State.FrameStep;
                        nextState &= ~State.FrameStep;
                    } else {
                        state &= ~State.FrameStep;
                        nextState |= State.FrameStep;
                    }
                } else if (pause && !Hotkeys.hotkeyPause.wasPressed) {
                    if (!HasFlag(state, State.FrameStep)) {
                        state |= State.FrameStep;
                        nextState &= ~State.FrameStep;
                    } else {
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
                    }
                } else if (HasFlag(lastState, State.FrameStep) && HasFlag(state, State.FrameStep) && Hotkeys.hotkeyFastForward.pressed) {
                    state &= ~State.FrameStep;
                    nextState |= State.FrameStep;
                }
            }
        }

        private static void CheckToEnable() {
            if (!Savestates.SpeedrunToolInstalled && Hotkeys.hotkeyRestart.pressed && !Hotkeys.hotkeyRestart.wasPressed) {
                DisableRun();
                EnableRun();
                return;
            }

            if (Hotkeys.hotkeyStart.pressed) {
                if (!HasFlag(state, State.Enable) && checkHotkeyStarTask == null) {
                    nextState |= State.Enable;
                } else {
                    nextState |= State.Disable;
                }
            } else if (HasFlag(nextState, State.Enable)) {
                if (Engine.Scene is Level level && (!level.CanPause || Engine.FreezeTimer > 0)) {
                    controller.RefreshInputs(true);
                    if (controller.Current.HasActions(Actions.Restart) || controller.Current.HasActions(Actions.Start)) {
                        nextState |= State.Delay;
                        FrameLoops = FastForward.DefaultFastForwardSpeed;
                        return;
                    }
                }

                EnableRun();
            } else if (HasFlag(nextState, State.Disable))
                DisableRun();
        }

        private static void EnableRun() {
            nextState &= ~State.Enable;
            InitializeRun(false);
            BackupPlayerBindings();
            kbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;

            checkHotkeyStarTask = Task.Run(() => {
                while (Running || Hotkeys.hotkeyStart.pressed) {
                    if (FrameLoops > 100) {
                        if (Running && Hotkeys.hotkeyStart.pressed) {
                            DisableRun();
                        }
                    }
                }
                checkHotkeyStarTask = null;
            });
        }

        private static void DisableRun() {
            Running = false;
            /*
            if (Recording) {
                controller.WriteInputs();
            }
            */
            Recording = false;
            state = State.None;
            nextState = State.None;
            RestorePlayerBindings();
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = kbTextInput;
            controller.resetSpawn = null;
            if (ExportSyncData) {
                EndExport();
                ExportSyncData = false;
            }

            enforceLegal = false;
            allowUnsafeInput = false;
            Ana.AnalogModeChange(AnalogueMode.Ignore);
            Hotkeys.ReleaseAllKeys();
            InputCommands.TryRestoreSettings();
        }

        public static void EnableExternal() => EnableRun();

        public static void DisableExternal() => DisableRun();

        private static void BackupPlayerBindings() {
            playerBindings = new List<VirtualButton.Node>[5]
                {GameInput.Jump.Nodes, GameInput.Dash.Nodes, GameInput.Grab.Nodes, GameInput.Talk.Nodes, GameInput.QuickRestart.Nodes};
            GameInput.Jump.Nodes = new List<VirtualButton.Node>
                {new VirtualButton.PadButton(GameInput.Gamepad, Buttons.A), new VirtualButton.PadButton(GameInput.Gamepad, Buttons.Y)};
            GameInput.Dash.Nodes = new List<VirtualButton.Node>
                {new VirtualButton.PadButton(GameInput.Gamepad, Buttons.B), new VirtualButton.PadButton(GameInput.Gamepad, Buttons.X)};
            GameInput.Grab.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(GameInput.Gamepad, grabButton) };
            GameInput.Talk.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(GameInput.Gamepad, Buttons.B) };
            GameInput.QuickRestart.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(GameInput.Gamepad, Buttons.LeftShoulder) };
        }

        private static void RestorePlayerBindings() {
            //This can happen if DisableExternal is called before any TAS has been run
            if (playerBindings == null)
                return;
            GameInput.Jump.Nodes = playerBindings[0];
            GameInput.Dash.Nodes = playerBindings[1];
            GameInput.Grab.Nodes = playerBindings[2];
            GameInput.Talk.Nodes = playerBindings[3];
            GameInput.QuickRestart.Nodes = playerBindings[4];
        }

        private static void InitializeRun(bool recording) {
            state |= State.Enable;
            state &= ~State.FrameStep;
            if (recording) {
                Recording = recording;
                state |= State.Record;
                controller.InitializeRecording();
            } 
            else {
                state &= ~State.Record;
                controller.RefreshInputs(true);
                Running = true;
            }
        }

        public static bool HasFlag(State state, State flag) => (state & flag) == flag;
        private static void WriteLibTASFrame(string outputKeys, string outputAxes, string outputButtons) {
            LibTAS.WriteLine($"|{outputKeys}|{outputAxes}:0:0:0:0:{outputButtons}|.........|");
        }
        private static void WriteEmptyFrame() {
            WriteLibTASFrame("", "0:0", "...............");
        }
        public static void AddFrames(int number) {
            if (ExportLibTAS) {
                for (int i = 0; i < number; ++i)
                    WriteEmptyFrame();
            }
        }

        public static void SetInputs(InputFrame input) {
            GamePadDPad pad = default;
            GamePadThumbSticks sticks = default;
            GamePadState state = default;
            FeatherInput = false;
            if (input.HasActions(Actions.Feather))
                SetFeather(input, ref pad, ref sticks);
            else
                SetDPad(input, ref pad, ref sticks);

            SetState(input, ref state, ref pad, ref sticks);

            if(ExportLibTAS)
               WriteLibTASFrame(input.LibTASKeys(),FeatherInput?($"{Ana.LastDS.x}:{-Ana.LastDS.y}"):"0:0",input.LibTASButtons());

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
                    (input.HasActions(Actions.Jump) ? Buttons.A : 0)
                    | (input.HasActions(Actions.Jump2) ? Buttons.Y : 0)
                    | (input.HasActions(Actions.Dash) ? Buttons.B : 0)
                    | (input.HasActions(Actions.Dash2) ? Buttons.X : 0)
                    | (input.HasActions(Actions.Grab) ? grabButton : 0)
                    | (input.HasActions(Actions.Start) ? Buttons.Start : 0)
                    | (input.HasActions(Actions.Restart) ? Buttons.LeftShoulder : 0)
                ),
                pad
            );
        }
        private static bool FeatherInput;
        public static AnalogHelper Ana;
        private static Vector2 ValidateFeatherInput(InputFrame input) {
            FeatherInput = true;
            return Ana.ComputeFeather(input.GetX(), input.GetY());
        }

        //The things we do for faster replay times
        private delegate void d_UpdateVirtualInputs();
        private delegate bool d_WallJumpCheck(Player player, int dir);
        private delegate float GetBerryFloat(Strawberry berry);
        private delegate float GetFloat(Player player);
        private delegate Vector2 GetPlayerSeekerSpeed(PlayerSeeker playerSeeker);
        private delegate float GetPlayerSeekerDashTimer(PlayerSeeker playerSeeker);
    }
}