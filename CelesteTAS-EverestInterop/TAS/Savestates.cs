using System;
using System.Collections;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
    static class Savestates {
        public static int SavedLine => SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;
        public static Coroutine routine;
        private static InputController savedController;
        private static int? savedLine;
        private static string savedPlayerStatus;
        private static bool savedByBreakpoint;

        private static bool BreakpointHasBeenDeleted => IsSaved() && savedByBreakpoint && savedController.InputIndex < controller.inputs.Count &&
                   controller.inputs[savedController.InputIndex].SaveState == false;

        private static readonly Lazy<bool> SpeedrunToolInstalled = new Lazy<bool>(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static bool IsSaved() {
            return StateManager.Instance.IsSaved && savedController != null;
        }

        public static void HandleSaveStates() {
            if (!SpeedrunToolInstalled.Value) return;

            if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
                Save(false);
                return;
            }

            if (IsSaved() && !Running && !Hotkeys.hotkeyStart.pressed && Hotkeys.hotkeyStart.wasPressed) {
                // check the start key just released
                Load();
                return;
            }

            if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Load();
                return;
            }

            if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Clear();
                return;
            }

            if (BreakpointHasBeenDeleted) {
                Clear();
            }

            // save state when tas run to ***s breakpoint
            if (Running && controller.Current.SaveState && controller.inputs.Where(record => record.SaveState).All(record => controller.Current.Line >= record.Line)) {
                Save(true);
            }
        }

        private static IEnumerator WaitForSavingState(Action onComplete) {
            yield return null;
            onComplete();
        }

        private static void Save(bool breakpoint) {
            if (IsSaved()) {
                // TODO don't response save state hotkey at ***s
                if (controller.CurrentFrame == savedController.CurrentFrame && savedController.SavedChecksum == controller.Checksum(savedController)) {
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                    return;
                }
            }
            if (StateManager.Instance.SaveState()) {
                savedLine =  controller.Current.Line;
                if (!breakpoint) {
                    savedLine--;
                }

                savedByBreakpoint = breakpoint;
                savedPlayerStatus = PlayerStatus;

                state |= State.FrameStep;
                nextState &= ~State.FrameStep;

                savedController = controller.Clone();
                LoadStateRoutine();

                routine = new Coroutine(WaitForSavingState(() => {
                    if (!CelesteTASModule.Settings.PauseAfterLoadState || controller.HasFastForward) {
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
                    }
                }));
            }
        }

        private static void Load() {
            state &= ~State.FrameStep;
            nextState &= ~State.FrameStep;

            if (IsSaved()) {
                controller.AdvanceFrame(true);
                if (!BreakpointHasBeenDeleted && savedController.SavedChecksum == controller.Checksum(savedController)) {
                    if (Running &&  controller.CurrentFrame - savedController.CurrentFrame <= 1) {
                        // Don't repeat load state, just play
                        return;
                    }
                    if (StateManager.Instance.LoadState()) {
                        if (!Running) EnableExternal();
                        LoadStateRoutine();
                        return;
                    }
                } else {
                    Clear();
                }
            }

            // If load state failed just playback normally
            PlayTAS();
        }

        private static void Clear() {
            StateManager.Instance.ClearState();
            routine = null;
            savedController = null;
            savedLine = null;
            savedPlayerStatus = null;
            savedByBreakpoint = false;
            if (Running) {
                UpdateStudio();
            }
        }

        private static void PlayTAS() {
            DisableExternal();
            EnableExternal();
        }

        private static void LoadStateRoutine() {
            controller = savedController.Clone();
            controller.AdvanceFrame(true, true);

            if (CelesteTASModule.Settings.PauseAfterLoadState && !controller.HasFastForward) {
                state |= State.FrameStep;
                nextState &= ~State.FrameStep;
            }

            if (!string.IsNullOrEmpty(savedPlayerStatus)) {
                PlayerStatus = savedPlayerStatus;
            }
            UpdateStudio();
        }

        private static void UpdateStudio() {
            CurrentStatus = controller.Current.Line + "[" + controller + "]" + SavedLine;
            StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
        }
    }
}