using System;
using System.Collections;
using System.Linq;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
	static class Savestates {
		private static InputController savedController;
		public static Coroutine routine;
		public static bool Saving;
		private static bool InFrameStepWhenSaved;
		public static bool StartedByLoadState;
		private static int? savedLine;
		public static int SavedLine =>  SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;
		private static Vector2? savedLastPos;

		private static readonly Lazy<bool> SpeedrunToolInstalled = new Lazy<bool>(() =>
				Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
		);

		private static bool IsSaved() {
			return StateManager.Instance.IsSaved;
		}

		public static void HandleSaveStates() {
			if (!SpeedrunToolInstalled.Value)
				return;

			if (Hotkeys.hotkeyLoadState == null || Hotkeys.hotkeySaveState == null) return;

			if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
				SaveAfterFreeze();
			} else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				LoadOrPlayTAS();
			} else if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				ClearState();
			}
		}

		public static void SaveAfterFreeze(int? studioLine = null) {
			Saving = true;
			InFrameStepWhenSaved = studioLine.HasValue && controller.HasFastForward && controller.fastForwards.Last().Line == studioLine.Value + 1;
			savedLine = studioLine.HasValue ? studioLine - 1 : controller.Current.Line;

			state &= ~State.FrameStep;
			nextState &= ~State.FrameStep;

			if (Engine.FreezeTimer > 0) {
				routine = new Coroutine(DelaySaveStatesRoutine(Save));
			} else {
				Save();
			}
		}

		private static void LoadOrPlayTAS() {
			if (StateManager.Instance.IsSaved && savedController != null) {
				state &= ~State.FrameStep;
				nextState &= ~State.FrameStep;

				// Don't repeat load state
				if (Running && savedController.CurrentFrame + 5 == controller.CurrentFrame &&
				    savedController.CurrentInputFrame + 5 == controller.CurrentInputFrame) {
					return;
				}
				Load();
			} else {
				PlayTAS();
			}
		}

		private static void ClearState() {
			StateManager.Instance.ClearState();
			savedController = null;
			savedLine = null;
			savedLastPos = null;
			if (Running) {
				SendDataToStudio();
			}
		}

		private static IEnumerator DelaySaveStatesRoutine(Action onComplete) {
			while (Engine.FreezeTimer > 0)
				yield return null;
			onComplete();
		}

		private static void Save() {
			savedLastPos = LastPos;
			InputController temp = controller.Clone();
			//+1 speedrun tool, -5 buffered inputs
			temp.ReverseFrames(4);
			Engine.Scene.OnEndOfFrame += () => {
				if (StateManager.Instance.SaveState()) {
					savedController = temp;
					controller = savedController.Clone();

					/*
					List<VirtualInput> inputs = (List<VirtualInput>)
						typeof(MInput).GetField("VirtualInputs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
					foreach (VirtualInput input in inputs) {
						if (input is VirtualButton)
							savedBuffers.Add((float)input.GetPrivateField("bufferCounter"));
					}
					*/

					LoadStateRoutine();
				}
				Saving = false;
			};
		}

		private static void Load() {
			controller.AdvanceFrame(true, true);
			if (savedController != null
				&& savedController.SavedChecksum == controller.Checksum(savedController.CurrentFrame)) {

				//Fastforward to breakpoint if one exists
				// var fastForwards = Manager.controller.fastForwards;
				// if (fastForwards.Count > 0 && fastForwards.Last().Line > savedController.Current.Line) {
				// 	Manager.state &= ~State.FrameStep;
				// 	Manager.nextState &= ~State.FrameStep;
				// }
				// else {
					//InputRecord ff = new InputRecord(0, "***");
					//savedController.fastForwards.Insert(0, ff);
					//savedController.inputs.Insert(savedController.inputs.IndexOf(savedController.Current) + 1, ff);
				// }

				Engine.Scene.OnEndOfFrame += () => {
					if (!StateManager.Instance.LoadState()) return;
					if (!Running) EnableExternal();
					savedController.inputs = controller.inputs;
					controller = savedController.Clone();
					LoadStateRoutine();
				};
				return;
			}
			//If savestate load failed just playback normally
			PlayTAS();
		}

		private static void PlayTAS() {
			DisableExternal();
			EnableExternal();
			StartedByLoadState = true;
		}

		private static void LoadStateRoutine() {
			controller.AdvanceFrame(true, true);
			controller.DryAdvanceFrames(5);
			if (InFrameStepWhenSaved) {
				state |= State.FrameStep;
				nextState |= State.FrameStep;

				// PlayerStatus will auto update, we just need restore lastPos
				if (savedLastPos.HasValue) {
					LastPos = savedLastPos.Value;
				}
				SendDataToStudio();
			}
		}

		private static void SendDataToStudio() {
			CurrentStatus = controller.Current.Line + "[" + controller + "]" + SavedLine;
			StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
		}
	}
}
