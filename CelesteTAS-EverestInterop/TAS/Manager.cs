using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using TAS.EverestInterop;
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
	public partial class Manager {
		private static FieldInfo strawberryCollectTimer = typeof(Strawberry).GetField("collectTimer", BindingFlags.Instance | BindingFlags.NonPublic);
		private static MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.NonPublic);
		public static bool Running, Recording;
		public static InputController controller = new InputController("Celeste.tas");
		public static State lastState, state, nextState;
		public static string CurrentStatus, PlayerStatus = "";
		public static int FrameStepCooldown, FrameLoops = 1;
		public static bool enforceLegal, allowUnsafeInput;
		private static Vector2 lastPos;
		private static long lastTimer;
		private static List<VirtualButton.Node>[] playerBindings;
		public static CelesteTASModuleSettings settings => CelesteTASModule.Settings;

		public static void UpdateInputs() {
			
			lastState = state;
			UpdatePlayerInfo();
			Hotkeys.instance?.Update();
			HandleFrameRates();
			CheckToEnable();
			FrameStepping();
			
			if (HasFlag(state, State.Enable)) {
				Running = true;

				if (HasFlag(state, State.FrameStep)) {
					StudioCommunicationClient.instance.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !HasFlag(nextState, State.FrameStep));
					return;
				}
				/*
				if (HasFlag(state, State.Record)) {
					controller.RecordPlayer();
				}
				*/
				else {
					bool fastForward = controller.HasFastForward;
					controller.AdvanceFrame(false);
					if (fastForward
						&& (!controller.HasFastForward
						|| controller.Current.ForceBreak
						&& controller.CurrentInputFrame == controller.Current.Frames)) {
						nextState |= State.FrameStep;
						FrameLoops = 1;
					}
					if (!controller.CanPlayback || (!allowUnsafeInput && !(Engine.Scene is Level || Engine.Scene is LevelLoader || controller.CurrentFrame <= 1)))
						DisableRun();
				}
				string status = controller.Current.Line + "[" + controller.ToString() + "]";
				CurrentStatus = status;
			}/*
			else if (HasFlag(state, State.Delay)) {
				Level level = Engine.Scene as Level;
				if (level.CanPause && Engine.FreezeTimer == 0f)
					EnableRun();
				
			}*/
			else {
				Running = false;
				CurrentStatus = null;
				if (!Engine.Instance.IsActive)
					UpdateVirtualInputs.Invoke(null, null);
			}
			StudioCommunicationClient.instance.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !HasFlag(nextState, State.FrameStep));
		}


		public static bool IsLoading() {
			if (Engine.Scene is Level level) {
				if (!level.IsAutoSaving())
					return false;
				return (level.Session.Level == "end-cinematic");
			}
			if (Engine.Scene is SummitVignette summit)
				return !(bool)summit.GetPrivateField("ready");
			else if (Engine.Scene is Overworld overworld)
				return overworld.Current is OuiFileSelect slot && slot.SlotIndex >= 0 && slot.Slots[slot.SlotIndex].StartingGame;
			return (Engine.Scene is LevelExit) || (Engine.Scene is LevelLoader) || (Engine.Scene is GameLoader);
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
				if (Hotkeys.IsKeyDown(settings.KeyFastForward)) {
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
					}
					else {
						state &= ~State.FrameStep;
						nextState |= State.FrameStep;
						controller.AdvanceFrame(true);
						if (ExportSyncData)
							ExportPlayerInfo();
					}
					FrameStepCooldown = 60;
				}
				else if (pause && !Hotkeys.hotkeyPause.wasPressed) {
					state &= ~State.FrameStep;
					nextState &= ~State.FrameStep;

				}
				else if (HasFlag(lastState, State.FrameStep) && HasFlag(state, State.FrameStep) && Hotkeys.hotkeyFastForward.pressed) {
					state &= ~State.FrameStep;
					nextState |= State.FrameStep;
					controller.AdvanceFrame(true);
				}
			}
		}
		
		private static void CheckToEnable() {
			if (Hotkeys.hotkeyStart.pressed) {
				if (!HasFlag(state, State.Enable))
					nextState |= State.Enable;
				else
					nextState |= State.Disable;
			}
			else if (HasFlag(nextState, State.Enable)) {
				if (Engine.Scene is Level level && (!level.CanPause || Engine.FreezeTimer > 0)) {
					
					//this code tries to prevent desyncs when not using console load - however the initialize playback interferes w/ input buffering
					controller.InitializePlayback();
					if (controller.Current.HasActions(Actions.Restart) || controller.Current.HasActions(Actions.Start)) {
						
						nextState |= State.Delay;
						FrameLoops = 400;
						return;
					}
					
				}
				EnableRun();
			}
			else if (HasFlag(nextState, State.Disable))
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
			controller.resetSpawn = null;
			if (ExportSyncData)
				EndExport();
			enforceLegal = false;
			allowUnsafeInput = false;
		}
		private static void EnableRun() {
			nextState &= ~State.Enable;
			UpdateVariables(false);
			BackupPlayerBindings();
		}
		private static void BackupPlayerBindings() {
			playerBindings = new List<VirtualButton.Node>[5] { Input.Jump.Nodes, Input.Dash.Nodes, Input.Grab.Nodes, Input.Talk.Nodes, Input.QuickRestart.Nodes};
			Input.Jump.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.A), new VirtualButton.PadButton(Input.Gamepad, Buttons.Y) };
			Input.Dash.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B), new VirtualButton.PadButton(Input.Gamepad, Buttons.X) };
			Input.Grab.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.Back) };
			Input.Talk.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B) };
			Input.QuickRestart.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.LeftShoulder) };
		}
		private static void RestorePlayerBindings() {
			Input.Jump.Nodes = playerBindings[0];
			Input.Dash.Nodes = playerBindings[1];
			Input.Grab.Nodes = playerBindings[2];
			Input.Talk.Nodes = playerBindings[3];
			Input.QuickRestart.Nodes = playerBindings[4];
		}
		private static void UpdateVariables(bool recording) {
			state |= State.Enable;
			state &= ~State.FrameStep;
			if (recording) {
				Recording = recording;
				state |= State.Record;
				controller.InitializeRecording();
			} else {
				state &= ~State.Record;
				controller.InitializePlayback();
			}
			Running = true;
		}
		private static bool HasFlag(State state, State flag) {
			return (state & flag) == flag;
		}
		public static void SetInputs(InputRecord input) {
			GamePadDPad pad;
			GamePadThumbSticks sticks;
			if (input.HasActions(Actions.Feather)) {
				pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
				sticks = new GamePadThumbSticks(new Vector2(input.GetX(), input.GetY()), new Vector2(0, 0));
			} else {
				pad = new GamePadDPad(
					input.HasActions(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
					input.HasActions(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
					input.HasActions(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
					input.HasActions(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
				);
				sticks = new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0));
			}
			GamePadState state = new GamePadState(
				sticks,
				new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
				new GamePadButtons(
					(input.HasActions(Actions.Jump) ? Buttons.A : 0)
					| (input.HasActions(Actions.Jump2) ? Buttons.Y : 0)
					| (input.HasActions(Actions.Dash) ? Buttons.B : 0)
					| (input.HasActions(Actions.Dash2) ? Buttons.X : 0)
					| (input.HasActions(Actions.Grab) ? Buttons.Back : 0)
					| (input.HasActions(Actions.Start) ? Buttons.Start : 0)
					| (input.HasActions(Actions.Restart) ? Buttons.LeftShoulder : 0)
				),
				pad
			);

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

			UpdateVirtualInputs.Invoke(null, null);
		}
	}
}
