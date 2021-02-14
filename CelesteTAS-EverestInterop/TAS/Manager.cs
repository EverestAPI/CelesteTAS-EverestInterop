using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using GameInput = Celeste.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using TAS.Input;
using FMOD;
using System.Net.NetworkInformation;

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
        public static AnalogueMode analogueMode = AnalogueMode.Ignore; //Needs to be tested with the libTAS converter
        public static bool kbTextInput;

        private static long lastTimer;
        private static List<VirtualButton.Node>[] playerBindings;

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
                if (!HasFlag(state, State.Enable)) {
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
            analogueMode = AnalogueMode.Ignore;
            Hotkeys.ReleaseAllKeys();
            InputCommands.TryRestoreSettings();
        }

        private static void EnableRun() {
            nextState &= ~State.Enable;
            InitializeRun(false);
            BackupPlayerBindings();
            kbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;
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

        public static void SetInputs(InputFrame input) {
            GamePadDPad pad = default;
            GamePadThumbSticks sticks = default;
            GamePadState state = default;

            if (input.HasActions(Actions.Feather))
                SetFeather(input, ref pad, ref sticks);
            else
                SetDPad(input, ref pad, ref sticks);

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

        private static Vector2 ComputeFeather(float x,float y) {
            if (x < 0) {
                Vector2 feather = ComputeFeather(-x, y);
                return new Vector2(-feather.X,feather.Y);
            }
            if (y < 0) {
                Vector2 feather = ComputeFeather(x, -y);
                return new Vector2(feather.X, -feather.Y);
            }
            if (x < y) {
                Vector2 feather = ComputeFeather(y,x);
                return new Vector2(feather.Y, feather.X);
            }
            /// assure positive and x>y
            const short deadzone = 7849;
            const short validArea = 32767 - deadzone;
            short X, Y;
            switch (analogueMode) {
                case AnalogueMode.Ignore:
                    return new Vector2(x, y);
                case AnalogueMode.Circle:
                    X = (short)(x*validArea + 0.5);
                    Y = (short)(y*validArea + 0.5);
                    break;
                case AnalogueMode.Square:
                    float divisor = Math.Max(Math.Abs(x), Math.Abs(y));
                    x /= divisor;
                    y /= divisor;
                    X = (short)(x * validArea + 0.5);
                    Y = (short)(y * validArea + 0.5);
                    break;
                case AnalogueMode.Precise:
                    GetPreciseFeatherPos(x,y,out X,out Y);
                    break;
                default:
                    throw new Exception("what the fuck");
            }
            return new Vector2((float)X/validArea,(float)Y/validArea);
        }
        private static Vector2 ValidateFeatherInput(InputFrame input) {
            return ComputeFeather(input.GetX(),input.GetY());
        }

        //https://www.ics.uci.edu/~eppstein/numth/frap.c
        private static void GetPreciseFeatherPos(float xPos, float yPos, out short outX, out short outY) {

            const short maxden = short.MaxValue - 7849;
            //special cases where this is imprecise
            if (Math.Abs(xPos) == Math.Abs(yPos) || Math.Abs(xPos) < 1E-10 || Math.Abs(yPos) < 1E-10) {
                if (Math.Abs(xPos) < 1E-10)
                    xPos = 0;
                if (Math.Abs(yPos) < 1E-10)
                    yPos = 0;
                outX = (short)(maxden * (short)Math.Sign(xPos));
                outY = (short)(maxden * (short)Math.Sign(yPos));
                return;
            }

            if (Math.Abs(xPos) > Math.Abs(yPos)) {
                GetPreciseFeatherPos(yPos, xPos, out outY, out outX);
                return;
            }


            long[][] m = new long[2][];
            m[0] = new long[2];
            m[1] = new long[2];
            double x = (double)xPos / (double)yPos;
            double startx = x;
            long ai;

            /* initialize matrix */
            m[0][0] = m[1][1] = 1;
            m[0][1] = m[1][0] = 0;

            /* loop finding terms until denom gets too big */
            while (m[1][0] * (ai = (long)x) + m[1][1] <= maxden) {
                long t;
                t = m[0][0] * ai + m[0][1];
                m[0][1] = m[0][0];
                m[0][0] = t;
                t = m[1][0] * ai + m[1][1];
                m[1][1] = m[1][0];
                m[1][0] = t;
                if (x == (double)ai)
                    break; // AF: division by zero
                x = 1 / (x - (double)ai);
                if (x > (double)0x7FFFFFFF)
                    break; // AF: representation failure
            }

            /* now remaining x is between 0 and 1/ai */
            /* approx as either 0 or 1/m where m is max that will fit in maxden */
            /* first try zero */
            outX = (short)m[0][0];
            outY = (short)m[1][0];

            double err1 = startx - ((double)m[0][0] / (double)m[1][0]);

            /* now try other possibility */
            ai = (maxden - m[1][1]) / m[1][0];
            m[0][0] = m[0][0] * ai + m[0][1];
            m[1][0] = m[1][0] * ai + m[1][1];

            double err2 = startx - ((double)m[0][0] / (double)m[1][0]);


            //magic
            if (err1 > err2) {
                outX = (short)m[0][0];
                outY = (short)m[1][0];
            }

            //why is there no short negation operator lmfao
            if (yPos < 0) {
                outX = (short)-outX;
                outY = (short)-outY;
            }

            //make sure it doesn't end up in the deadzone
            short mult = (short)Math.Floor(maxden / (float)Math.Max(Math.Abs(outX), Math.Abs(outY)));
            outX *= mult;
            outY *= mult;

            return;
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
