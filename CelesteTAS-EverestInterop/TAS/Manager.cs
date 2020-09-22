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
using Celeste.Mod.SpeedrunTool.SaveLoad;
using System.Collections;

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

		static Manager() {
			FieldInfo strawberryCollectTimer = typeof(Strawberry).GetField("collectTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo dashCooldownTimer = typeof(Player).GetField("dashCooldownTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo jumpGraceTimer = typeof(Player).GetField("jumpGraceTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo WallJumpCheck = typeof(Player).GetMethod("WallJumpCheck", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.NonPublic);

			Manager.UpdateVirtualInputs = (d_UpdateVirtualInputs)UpdateVirtualInputs.CreateDelegate(typeof(d_UpdateVirtualInputs));
			Manager.WallJumpCheck = (d_WallJumpCheck)WallJumpCheck.CreateDelegate(typeof(d_WallJumpCheck));
			StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
			DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
			JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();

		}
		
		private static FieldInfo strawberryCollectTimer = typeof(Strawberry).GetField("collectTimer", BindingFlags.Instance | BindingFlags.NonPublic);

		//The things we do for faster replay times
		private delegate void d_UpdateVirtualInputs();
		private static d_UpdateVirtualInputs UpdateVirtualInputs;
		private delegate bool d_WallJumpCheck(Player player, int dir);
		private static d_WallJumpCheck WallJumpCheck;
		private delegate float GetBerryFloat(Strawberry berry);
		private static GetBerryFloat StrawberryCollectTimer;
		private delegate float GetFloat(Player player);
		private static GetFloat DashCooldownTimer;
		private static GetFloat JumpGraceTimer;
		
		public static bool Running, Recording;
		public static InputController controller = new InputController("Celeste.tas");
		public static State lastState, state, nextState;
		public static string CurrentStatus, PlayerStatus = "";
		public static int FrameStepCooldown, FrameLoops = 1;
		public static bool enforceLegal, allowUnsafeInput;
		public static int forceDelayTimer = 0;
		public static bool forceDelay;
		private static Vector2 lastPos;
		private static long lastTimer;
		private static List<VirtualButton.Node>[] playerBindings;
		private static Coroutine routine;
		public static Buttons grabButton = Buttons.Back;
		public static CelesteTASModuleSettings settings => CelesteTASModule.Settings;
		public static bool kbTextInput;
		private static bool ShouldForceState => HasFlag(nextState, State.FrameStep) && !Hotkeys.hotkeyFastForward.overridePressed;

		public static void UpdateInputs() {
			
			lastState = state;
			UpdatePlayerInfo();
			Hotkeys.instance?.Update();
			Savestates.HandleSaveStates();
			Savestates.routine?.Update();
			HandleFrameRates();
			CheckToEnable();
			FrameStepping();

			if (HasFlag(state, State.Enable)) {
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
					bool fastForward = controller.HasFastForward;
					controller.AdvanceFrame(false);
					if (fastForward
						&& (!controller.HasFastForward
						|| controller.Current.ForceBreak
						&& controller.CurrentInputFrame == controller.Current.Frames)) {
						nextState |= State.FrameStep;
						FrameLoops = 1;
					}
					if (!controller.CanPlayback || (!allowUnsafeInput && !(Engine.Scene is Level || Engine.Scene is LevelLoader || Engine.Scene is LevelExit || controller.CurrentFrame <= 1)))
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


		public static bool IsLoading() {
			if (Engine.Scene is Level level) {
				if (!level.IsAutoSaving())
					return false;
				return level.Session.Level == "end-cinematic";
			}
			if (Engine.Scene is SummitVignette summit)
				return !(bool)summit.GetPrivateField("ready");
			else if (Engine.Scene is Overworld overworld)
				return overworld.Current is OuiFileSelect slot && slot.SlotIndex >= 0 && slot.Slots[slot.SlotIndex].StartingGame;
			bool isLoading = (Engine.Scene is LevelExit) || (Engine.Scene is LevelLoader) || (Engine.Scene is GameLoader);
			bool flag = true;
			if (!flag)
				return isLoading;
			else
				return isLoading || Engine.Scene.GetType().Name == "LevelExitToLobby";
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
					}
					else {
						state &= ~State.FrameStep;
						nextState |= State.FrameStep;
						controller.AdvanceFrame(true);
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
			Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = kbTextInput;
			controller.resetSpawn = null;
			if (ExportSyncData) {
				EndExport();
				ExportSyncData = false;
			}
			enforceLegal = false;
			allowUnsafeInput = false;
		}
		private static void EnableRun() {
			nextState &= ~State.Enable;
			UpdateVariables(false);
			BackupPlayerBindings();
			kbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
			Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;
		}
		public static void EnableExternal() => EnableRun();
		public static void DisableExternal() => DisableRun();
		private static void BackupPlayerBindings() {
			playerBindings = new List<VirtualButton.Node>[5] { Input.Jump.Nodes, Input.Dash.Nodes, Input.Grab.Nodes, Input.Talk.Nodes, Input.QuickRestart.Nodes};
			Input.Jump.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.A), new VirtualButton.PadButton(Input.Gamepad, Buttons.Y) };
			Input.Dash.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B), new VirtualButton.PadButton(Input.Gamepad, Buttons.X) };
			Input.Grab.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, grabButton) };
			Input.Talk.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B) };
			Input.QuickRestart.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.LeftShoulder) };
		}
		private static void RestorePlayerBindings() {
			//This can happen if DisableExternal is called before any TAS has been run
			if (playerBindings == null)
				return;
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
					| (input.HasActions(Actions.Grab) ? grabButton : 0)
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

			UpdateVirtualInputs();
		}
	}
}
