using System;
using System.Collections;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
    // TODO Add a command to check if savestate will cause desync
    static class Savestates {
        // studio highlight line number start from 0
        // input record line number start from 1
        public static int StudioHighlightLine => (speedrunToolInstalledLazy.Value && IsSaved() && savedLine.HasValue ? savedLine.Value  : 0) - 1;
        public static bool SpeedrunToolInstalled => speedrunToolInstalledLazy.Value;
        public static Coroutine routine;
        private static InputController savedController;
        private static int? savedLine;
        private static string savedPlayerStatus;
        private static Vector2 savedLastPos;
        private static bool savedByBreakpoint;
        private static AnalogueMode savedAnalogueMode;

        private static bool BreakpointHasBeenDeleted => IsSaved() && savedByBreakpoint && savedController.InputIndex < controller.inputs.Count &&
                   controller.inputs[savedController.InputIndex].SaveState == false;

        private static readonly Lazy<bool> speedrunToolInstalledLazy = new Lazy<bool>(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static bool IsSaved() {
            return StateManager.Instance.IsSaved && StateManager.Instance.SavedByTas && savedController != null;
        }

        public static void HandleSaveStates() {
            if (!SpeedrunToolInstalled) return;

            if (!Running && IsSaved() && Engine.Scene is Level && Hotkeys.hotkeyStart.wasPressed && !Hotkeys.hotkeyStart.pressed) {
                Load();
                return;
            }

            if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
                Save(false);
                return;
            }

            if (Hotkeys.hotkeyRestart.pressed && !Hotkeys.hotkeyRestart.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Load();
                return;
            }

            if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Clear();
                DisableExternal();
                return;
            }

            if (BreakpointHasBeenDeleted) {
                Clear();
            }

            // save state when tas run to the last savestate breakpoint
            if (Running
                && controller.Current.SaveState && !controller.Current.HasSavedState
                && controller.CurrentInputFrame == controller.Current.Frames
                && controller.inputs.LastOrDefault(record => record.SaveState) == controller.Current)  {
                Save(true);
                return;
            }

            // auto load state after entering the level if tas is started from outside the level.
            if (Running && IsSaved() && Engine.Scene is Level && controller.InputIndex < savedController.InputIndex) {
                Load();
            }
        }

        private static IEnumerator WaitForSavingState() {
            yield return null;
            SetTasState();
        }

        private static void Save(bool breakpoint) {
            if (IsSaved()) {
                if (controller.CurrentFrame  == savedController.CurrentFrame) {
                    if (savedController.SavedChecksum == controller.Checksum(savedController)) {
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
                        return;
                    }
                }
            }

            if (!StateManager.Instance.SaveState()) return;

            if (breakpoint && controller.Current.SaveState) {
                controller.Current.HasSavedState = true;
            }

            if (breakpoint) {
                savedLine = controller.Current.Line + 1;
            } else {
                savedLine = controller.Current.Line;
            }

            savedByBreakpoint = breakpoint;
            savedPlayerStatus = PlayerStatus;
            savedLastPos = LastPos;
            savedAnalogueMode = analogueMode;

            savedController = controller.Clone();
            LoadStateRoutine();

            state |= State.FrameStep;
            nextState &= ~State.FrameStep;

            routine = new Coroutine(WaitForSavingState());
        }

        private static void Load() {
            if (Engine.Scene is LevelLoader) return;

            if (IsSaved()) {
                controller.AdvanceFrame(true);
                if (!BreakpointHasBeenDeleted && savedController.SavedChecksum == controller.Checksum(savedController)) {
                    if (Running && controller.CurrentFrame == savedController.CurrentFrame) {
                        // Don't repeat load state, just play
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
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
            savedLastPos = default;
            savedByBreakpoint = false;
            UpdateStudio();
        }

        private static void PlayTAS() {
            DisableExternal();
            EnableExternal();
        }

        private static void LoadStateRoutine() {
            controller = savedController.Clone();
            controller.AdvanceFrame(true);
            if (savedByBreakpoint && controller.Current.SaveState) {
                // HasSavedState is set to false by AdvanceFrame(true), so we need restore it.
                controller.Current.HasSavedState = true;
            }
            SetTasState();
            analogueMode = savedAnalogueMode;
            PlayerStatus = savedPlayerStatus;
            LastPos = savedLastPos;
            UpdateStudio();
        }

        private static void SetTasState() {
            if ((CelesteTASModule.Settings.PauseAfterLoadState || savedByBreakpoint) && !controller.HasFastForward) {
                state |= State.FrameStep;
            } else {
                state &= ~State.FrameStep;
            }

            nextState &= ~State.FrameStep;
        }

        private static void UpdateStudio() {
            if (controller.Current != null) {
                CurrentStatus = controller.Current.Line + "[" + controller + "]" + StudioHighlightLine;
            }
            StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
        }
    }
}