using System;
using System.Collections;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
    static class Savestates {
        private static InputController savedController;
        public static Coroutine routine;
        public static bool AllowExecuteSaveStateCommand;
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
            if (!SpeedrunToolInstalled.Value)
                return;

            if (Hotkeys.hotkeyLoadState == null || Hotkeys.hotkeySaveState == null) return;

            if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
                SaveState();
            } else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                LoadState();
            } else if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                ClearState();
            }
        }

        public static void SaveState(int? commandLine = null) {
            if (IsSaved() && savedController.CurrentFrame == controller.CurrentFrame && savedController.SavedChecksum == controller.Checksum(savedController.CurrentFrame)) {
                // Don't repeat save state, just play
                state &= ~State.FrameStep;
                nextState &= ~State.FrameStep;
                return;
            }
            if (StateManager.Instance.SaveState()) {
                Logger.Log("RepeatSaveState", "RepeatSaveState");
                AllowExecuteSaveStateCommand = false;

                state |= State.FrameStep;
                nextState &= ~State.FrameStep;

                savedController = controller.Clone();
                LoadStateRoutine();

                savedLine = commandLine ?? controller.Current.Line;
                savedLine--;

                savedPlayerStatus = PlayerStatus;

                routine = new Coroutine(DelayRoutine(() => {
                    if (CelesteTASModule.Settings.PauseAfterLoadState && !controller.HasFastForward) return;
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                }));
            }
        }

        private static IEnumerator DelayRoutine(Action onComplete) {
            yield return null;
            onComplete();
        }

        private static void LoadState() {
            state &= ~State.FrameStep;
            nextState &= ~State.FrameStep;

            if (IsSaved()) {
                controller.AdvanceFrame(true);
                if (savedController.SavedChecksum == controller.Checksum(savedController.CurrentFrame)) {
                    if (Running && savedController.CurrentFrame == controller.CurrentFrame) {
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

        private static void ClearState() {
            StateManager.Instance.ClearState();
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
            AllowExecuteSaveStateCommand = true;
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