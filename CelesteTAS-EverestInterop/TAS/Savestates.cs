using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;

namespace TAS {
	static class Savestates {
		public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private static InputController savedController;
		private static List<float> savedBuffers = new List<float>();
		public static Coroutine routine;

		public const int LOAD_TIME = 36;

		public static void HandleSaveStates() {
			if (Engine.FreezeTimer > 0)
				return;
			if (Hotkeys.hotkeyLoadState == null || Hotkeys.hotkeySaveState == null)
				return;
			if (Manager.Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
				Save();
			}
			else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				Load();
			}
			else
				return;
		}

		private static void Save() {
			InputController temp = Manager.controller.Clone();
			//+1 speedrun tool, -5 buffered inputs
			temp.ReverseFrames(4);
			Engine.Scene.OnEndOfFrame += () => {
				if (StateManager.Instance.ExternalSave()) {
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
			};
		}

		private static void Load() {
			Manager.controller.ReadFile();
			if (StateManager.Instance.SavedPlayer != null
				&& savedController?.SavedChecksum == Manager.controller.Checksum(savedController.CurrentFrame)) {

				//Fastforward to breakpoint if one exists
				var fastForwards = Manager.controller.fastForwards;
				if (fastForwards.Count > 0 && fastForwards[fastForwards.Count - 1].Line > savedController.Current.Line) {
					Manager.state &= ~State.FrameStep;
					Manager.nextState &= ~State.FrameStep;
				}
				else {
					//InputRecord ff = new InputRecord(0, "***");
					//savedController.fastForwards.Insert(0, ff);
					//savedController.inputs.Insert(savedController.inputs.IndexOf(savedController.Current) + 1, ff);
				}

				Engine.Scene.OnEndOfFrame += () => {
					if (!StateManager.Instance.ExternalLoad())
						return;
					if (!Manager.Running)
						Manager.EnableExternal();
					savedController.inputs = Manager.controller.inputs;
					Manager.controller = savedController.Clone();
					routine = new Coroutine(LoadStateRoutine());
				};

			}
			//If savestate load failed just playback normally
			Manager.DisableExternal();
			Manager.EnableExternal();
		}

		private static IEnumerator LoadStateRoutine() {
			Manager.forceDelayTimer = 100;
			while (Engine.Scene.Entities.FindFirst<Player>() != null)
				yield return null;
			while (Engine.Scene.Entities.FindFirst<Player>() == null)
				yield return null;
			//5 for buffered inputs, 3 for speedrun tool
			Manager.forceDelayTimer = LOAD_TIME - 8;
			yield return Engine.DeltaTime;
			Manager.controller.AdvanceFrame(true);
			while (Manager.forceDelayTimer > 1)
				yield return null;

			yield break;
		}
	}
}
