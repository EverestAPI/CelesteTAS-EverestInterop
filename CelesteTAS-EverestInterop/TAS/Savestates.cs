using System;
using System.Collections;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;

namespace TAS {
	static class Savestates {
		private static InputController savedController;
		public static Coroutine routine;
		public static bool Saving;

		private static readonly Lazy<bool> SpeedrunToolInstalled = new Lazy<bool>(() =>
				Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
		);

		public static void HandleSaveStates() {
			if (!SpeedrunToolInstalled.Value)
				return;

			if (Hotkeys.hotkeyLoadState == null || Hotkeys.hotkeySaveState == null)
				return;
			if (Manager.Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
				SaveAfterFreeze();
			}
			else if (savedController != null && Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				LoadAfterFreeze();
			}
		}

		public static void SaveAfterFreeze() {
			Manager.state &= ~State.FrameStep;
			Manager.nextState &= ~State.FrameStep;
			Saving = true;
			if (Engine.FreezeTimer > 0) {
				routine = new Coroutine(DelaySaveStatesRoutine(Save));
				return;
			}
			Save();
		}

		private static void LoadAfterFreeze() {
			Manager.state &= ~State.FrameStep;
			Manager.nextState &= ~State.FrameStep;
			if (Engine.FreezeTimer > 0) {
				routine = new Coroutine(DelaySaveStatesRoutine(Load));
				return;
			}
			Load();
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
					routine = new Coroutine(LoadStateRoutine());
				}
				Saving = false;
			};
		}

		private static void Load() {
			Manager.controller.AdvanceFrame(true);
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
					routine = new Coroutine(LoadStateRoutine());
				};
				return;
			}
			//If savestate load failed just playback normally
			Manager.DisableExternal();
			Manager.EnableExternal();
		}

		private static IEnumerator LoadStateRoutine() {
			Manager.forceDelay = true;
			yield return Engine.DeltaTime;
			yield return Engine.DeltaTime;
			while (!(Engine.Scene is Level))
				yield return null;
			while ((Engine.Scene as Level).Frozen)
				yield return null;
			Manager.forceDelay = false;
			Manager.controller.AdvanceFrame(true, true);
			Manager.controller.DryAdvanceFrames(5);
		}
	}
}
