using System;
using System.Collections;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;

namespace TAS {
	static class Savestates {
		private static InputController savedController;
		public static Coroutine routine;
		public static bool Saving;
		private static bool InFrameStepWhenSaved;
		public static bool StartedByLoadState;
		private static int? savedLine;
		public static int SavedLine =>  SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;

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

			if (Manager.Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
				SaveAfterFreeze();
			} else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				LoadOrPlayTAS();
			}
		}

		public static void SaveAfterFreeze(int? studioLine = null) {
			Saving = true;
			InFrameStepWhenSaved = studioLine.HasValue && Manager.controller.fastForwards.Any(record => record.Line == studioLine.Value + 1);
			savedLine = studioLine.HasValue ? studioLine - 1 : Manager.controller.Current.Line;

			Manager.state &= ~State.FrameStep;
			Manager.nextState &= ~State.FrameStep;

			if (Engine.FreezeTimer > 0) {
				routine = new Coroutine(DelaySaveStatesRoutine(Save));
				return;
			}
			Save();
		}

		private static void LoadOrPlayTAS() {
			if (StateManager.Instance.IsSaved && savedController != null) {
				Manager.state &= ~State.FrameStep;
				Manager.nextState &= ~State.FrameStep;

				// Don't repeat load state
				if (savedController.CurrentFrame + 5 == Manager.controller.CurrentFrame &&
				    savedController.CurrentInputFrame + 5 == Manager.controller.CurrentInputFrame) {
					return;
				}
				Load();
			} else {
				PlayTAS();
			}
		}

		private static IEnumerator DelaySaveStatesRoutine(Action onComplete) {
			while (Engine.FreezeTimer > 0)
				yield return null;
			onComplete();
		}

		private static void Save() {
			InputController temp = Manager.controller.Clone();
			//+1 speedrun tool, -5 buffered inputs
			temp.ReverseFrames(4);
			Engine.Scene.OnEndOfFrame += () => {
				if (StateManager.Instance.SaveState()) {
					savedController = temp;
					Manager.controller = savedController.Clone();

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
			Manager.controller.AdvanceFrame(true, true);
			if (savedController != null
				&& savedController.SavedChecksum == Manager.controller.Checksum(savedController.CurrentFrame)) {

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
					if (!StateManager.Instance.LoadState())
						return;
					if (!Manager.Running)
						Manager.EnableExternal();
					savedController.inputs = Manager.controller.inputs;
					Manager.controller = savedController.Clone();
					LoadStateRoutine();
				};
				return;
			}
			//If savestate load failed just playback normally
			PlayTAS();
		}

		private static void PlayTAS() {
			Manager.DisableExternal();
			Manager.EnableExternal();
			StartedByLoadState = true;
		}

		private static void LoadStateRoutine() {
			Manager.controller.AdvanceFrame(true, true);
			Manager.controller.DryAdvanceFrames(5);
			if (InFrameStepWhenSaved) {
				Manager.state |= State.FrameStep;
				Manager.nextState &= ~State.FrameStep;
			}
		}
	}
}
