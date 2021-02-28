using System;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.Input;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
// TODO Add a command to check if savestate will cause desync
    public static class Savestates {
        public static Coroutine Routine;
        private static InputController savedController;
        private static string savedPlayerStatus;
        private static Vector2 savedLastPos;
        private static Vector2 savedLastPlayerSeekerPos;
        private static bool savedByBreakpoint;

        private static readonly Lazy<bool> SpeedrunToolInstalledLazy = new Lazy<bool>(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static int SavedLine =>
            (savedByBreakpoint
                ? Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.Line
                : Controller.Inputs.GetValueOrDefault(SavedCurrentFrame)?.Line) ?? -1;

        private static int SavedCurrentFrame => savedController?.CurrentFrame ?? -1;

        public static int StudioHighlightLine => SpeedrunToolInstalledLazy.Value && IsSaved() ? SavedLine : -1;
        public static bool SpeedrunToolInstalled => SpeedrunToolInstalledLazy.Value;

        private static bool BreakpointHasBeenDeleted =>
            IsSaved() && savedByBreakpoint && Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.SaveState != true;

        private static bool IsSaved() {
            return StateManager.Instance.IsSaved && StateManager.Instance.SavedByTas && savedController != null;
        }

        public static void HandleSaveStates() {
            if (!SpeedrunToolInstalled) {
                return;
            }

            if (!Running && IsSaved() && Engine.Scene is Level && Hotkeys.HotkeyStart.WasPressed && !Hotkeys.HotkeyStart.Pressed) {
                Load();
                return;
            }

            if (Running && Hotkeys.HotkeySaveState.Pressed && !Hotkeys.HotkeySaveState.WasPressed) {
                Save(false);
                return;
            }

            if (Hotkeys.HotkeyRestart.Pressed && !Hotkeys.HotkeyRestart.WasPressed && !Hotkeys.HotkeySaveState.Pressed) {
                Load();
                return;
            }

            if (Hotkeys.HotkeyClearState.Pressed && !Hotkeys.HotkeyClearState.WasPressed && !Hotkeys.HotkeySaveState.Pressed) {
                Clear();
                DisableExternal();
                return;
            }

            if (Running && BreakpointHasBeenDeleted) {
                Clear();
            }

            // save state when tas run to the last savestate breakpoint
            if (Running
                && Controller.Inputs.Count > Controller.CurrentFrame
                && Controller.CurrentFastForward is FastForward currentFastForward && currentFastForward.SaveState
                && Controller.FastForwards.Last(pair => pair.Value.SaveState).Value == currentFastForward
                && SavedCurrentFrame != currentFastForward.Frame) {
                Save(true);
                return;
            }

            // auto load state after entering the level if tas is started from outside the level.
            if (Running && IsSaved() && Engine.Scene is Level && Controller.CurrentFrame < savedController.CurrentFrame) {
                Load();
            }
        }

        private static void Save(bool breakpoint) {
            if (IsSaved()) {
                if (Controller.CurrentFrame == savedController.CurrentFrame) {
                    if (savedController.SavedChecksum == Controller.Checksum(savedController)) {
                        Manager.State &= ~State.FrameStep;
                        NextState &= ~State.FrameStep;
                        return;
                    }
                }
            }

            if (!StateManager.Instance.SaveState()) {
                return;
            }

            savedByBreakpoint = breakpoint;
            savedPlayerStatus = PlayerStatus;
            savedLastPos = LastPos;
            savedLastPlayerSeekerPos = LastPlayerSeekerPos;

            savedController = Controller.Clone();
            LoadStateRoutine();
        }

        private static void Load() {
            if (Engine.Scene is LevelLoader) {
                return;
            }

            if (IsSaved()) {
                Controller.RefreshInputs(false);
                if (!BreakpointHasBeenDeleted && savedController.SavedChecksum == Controller.Checksum(savedController)) {
                    if (Running && Controller.CurrentFrame == savedController.CurrentFrame) {
                        // Don't repeat load state, just play
                        Manager.State &= ~State.FrameStep;
                        NextState &= ~State.FrameStep;
                        return;
                    }

                    if (StateManager.Instance.LoadState()) {
                        if (!Running) {
                            EnableExternal();
                        }

                        LoadStateRoutine();
                        return;
                    }
                } else {
                    Clear();
                }
            }

            // If load state failed just playback normally
            PlayTas();
        }

        private static void Clear() {
            StateManager.Instance.ClearState();
            Routine = null;
            savedController = null;
            savedPlayerStatus = null;
            savedLastPos = default;
            savedLastPlayerSeekerPos = default;
            savedByBreakpoint = false;

            UpdateStudio();
        }

        private static void PlayTas() {
            DisableExternal();
            EnableExternal();
        }

        private static void LoadStateRoutine() {
            Controller.CopyFrom(savedController);
            SetTasState();
            PlayerStatus = savedPlayerStatus;
            LastPos = savedLastPos;
            LastPlayerSeekerPos = savedLastPlayerSeekerPos;
            UpdateStudio();
        }

        private static void SetTasState() {
            if ((CelesteTasModule.Settings.PauseAfterLoadState || savedByBreakpoint) && !(Controller.HasFastForward)) {
                Manager.State |= State.FrameStep;
            } else {
                Manager.State &= ~State.FrameStep;
            }

            NextState &= ~State.FrameStep;
        }

        private static void UpdateStudio() {
            if (Controller.CurrentFrame > 0) {
                UpdateManagerStatus();
            }

            StudioCommunicationClient.Instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
        }

        public static void Unload() {
            if (SpeedrunToolInstalled && IsSaved()) {
                Clear();
            }
        }
    }
}