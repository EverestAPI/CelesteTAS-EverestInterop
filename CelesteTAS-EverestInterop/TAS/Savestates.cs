using System;
using System.Collections;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
    static class Savestates {
        private static InputController savedController;
        public static Coroutine routine;
        private static int? savedLine;
        public static int SavedLine => SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;
        private static string savedPlayerStatus;

        private static readonly Lazy<bool> SpeedrunToolInstalled = new Lazy<bool>(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static bool IsSaved() {
            return StateManager.Instance.IsSaved && savedController != null;
        }

        public static void HandleSaveStates() {
            if (!SpeedrunToolInstalled.Value) return;

            if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
                Save(null);
            } else if (IsSaved() && !Running && !Hotkeys.hotkeyStart.pressed && Hotkeys.hotkeyStart.wasPressed) {
                // check the start key just released
                Load();
            } else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Load();
            } else if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Clear();
            }
        }

        private static IEnumerator WaitForSavingState(Action onComplete) {
            yield return null;
            onComplete();
        }

        public static void SaveSafe(InputRecord breakpoint) {
            if (SpeedrunToolInstalled.Value) {
                Save(breakpoint);
            }
        }
        private static void Save(InputRecord breakpoint) {
            if (IsSaved()) {
                if (savedController.CurrentFrame == controller.CurrentFrame && savedController.SavedChecksum == controller.Checksum(savedController)) {
                    // Don't repeat save state, just play
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                    return;
                }
            }
            if (StateManager.Instance.SaveState()) {
                state |= State.FrameStep;
                nextState &= ~State.FrameStep;

                savedController = controller.Clone();
                LoadStateRoutine();

                savedLine =  controller.Current.Line;
                if (breakpoint == null) {
                    savedLine--;
                }

                savedPlayerStatus = PlayerStatus;

                routine = new Coroutine(WaitForSavingState(() => {
                    if (CelesteTASModule.Settings.PauseAfterLoadState && !controller.HasFastForward) return;
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                }));
            }
        }

        private static void Load() {
            state &= ~State.FrameStep;
            nextState &= ~State.FrameStep;

            if (IsSaved()) {
                controller.AdvanceFrame(true, true);
                if (savedController.SavedChecksum == controller.Checksum(savedController)) {
                    if (Running && savedController.CurrentFrame + 1 == controller.CurrentFrame) {
                        // Don't repeat load state, just play
                        return;
                    }
                    if (StateManager.Instance.LoadState()) {
                        if (!Running) EnableExternal();
                        LoadStateRoutine();
                        return;
                    }
                }
            }

            //If load state failed just playback normally
            PlayTAS();
        }

        private static void Clear() {
            StateManager.Instance.ClearState();
            routine = null;
            savedController = null;
            savedLine = null;
            savedPlayerStatus = null;
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