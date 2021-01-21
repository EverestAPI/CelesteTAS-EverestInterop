using System;
using System.Collections;
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
        public static bool AllowExecuteSaveStateCommand;
        private static int? savedLine;
        public static int SavedLine => SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;
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
                SaveState();
            } else if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                LoadStateOrPlayTAS();
            } else if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                ClearState();
            }
        }

        public static void SaveState(int? commandLine = null) {
            if (StateManager.Instance.SaveState()) {
                savedController = controller.Clone();
                LoadStateRoutine();

                if (AllowExecuteSaveStateCommand && commandLine.HasValue) {
                    AllowExecuteSaveStateCommand = false;
                }

                savedLine = commandLine ?? controller.Current.Line;
                savedLine--;

                state |= State.FrameStep;
                nextState &= ~State.FrameStep;

                savedLastPos = LastPos;

                routine = new Coroutine(DelayRoutine(() => {
                    if (!CelesteTASModule.Settings.PauseAfterLoadState || controller.HasFastForward) {
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
                    }
                }));
            }
        }

        private static IEnumerator DelayRoutine(Action onComplete) {
            yield return null;
            onComplete();
        }

        private static void LoadStateOrPlayTAS() {
            if (StateManager.Instance.IsSaved && savedController != null) {
                // Don't repeat load state
                if (Running && savedController.CurrentFrame == controller.CurrentFrame) {
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

        private static void Load() {
            state &= ~State.FrameStep;
            nextState &= ~State.FrameStep;

            controller.AdvanceFrame(true);
            if (savedController != null
                && savedController.SavedChecksum == controller.Checksum(savedController.CurrentFrame)) {
                if (!StateManager.Instance.LoadState()) return;
                if (!Running) EnableExternal();
                savedController.inputs = controller.inputs;
                savedController.AdvanceFrame(true, true);
                LoadStateRoutine();
                return;
            }

            //If savestate load failed just playback normally
            PlayTAS();
        }

        private static void PlayTAS() {
            DisableExternal();
            EnableExternal();
            AllowExecuteSaveStateCommand = true;
        }

        private static void LoadStateRoutine() {
            controller = savedController.Clone();
            if (controller.Current.CommandType == "savestatecommand") {
                controller.Current.Command = null;
            }

            if (CelesteTASModule.Settings.PauseAfterLoadState && !controller.HasFastForward) {
                state |= State.FrameStep;
                nextState &= ~State.FrameStep;

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