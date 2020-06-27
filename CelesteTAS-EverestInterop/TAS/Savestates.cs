using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;

namespace TAS {
	class Savestates {
		public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private static InputController savedController;
		public static Coroutine routine;

		public static void HandleSaveStates() {
			if (Hotkeys.hotkeyLoadState == null || Hotkeys.hotkeySaveState == null)
				return;
			if (Manager.Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
				Engine.Scene.OnEndOfFrame += () => {
					if (StateManager.Instance.ExternalSave()) {
						savedController = Manager.controller.Clone();
						routine = new Coroutine(LoadStateRoutine());
					}
				};
			}
			else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
				if (StateManager.Instance.SavedPlayer != null) {
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
			}
			else
				return;
		}

		private static IEnumerator LoadStateRoutine() {
			Manager.forceDelayTimer = 100;
			while (Engine.Scene.Entities.FindFirst<Player>() != null)
				yield return null;
			while (Engine.Scene.Entities.FindFirst<Player>() == null)
				yield return null;
			Manager.forceDelayTimer = 35;
			yield return Engine.DeltaTime;
			Manager.controller.AdvanceFrame(true);
			while (Manager.forceDelayTimer > 1)
				yield return null;
			yield break;
		}
	}
}
