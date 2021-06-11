using System;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Utils;
using static TAS.Manager;

namespace TAS {
    public static class Savestates {
        private static InputController savedController;
        private static string savedGameStatus;
        private static string savedStatusWithoutTime;
        private static string savedLastVel;
        private static string savedLastPlayerSeekerVel;
        private static string savedCustomInfo;
        private static Vector2Double savedLastPos;
        private static Vector2Double savedLastPlayerSeekerPos;
        private static float savedDashTime;
        private static bool savedFrozen;
        private static bool savedByBreakpoint;

        private static readonly Lazy<bool> SpeedrunToolInstalledLazy = new(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static int SavedLine =>
            (savedByBreakpoint
                ? Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.Line
                : Controller.Inputs.GetValueOrDefault(SavedCurrentFrame)?.Line) ?? -1;

        private static int SavedCurrentFrame => IsSaved() ? savedController.CurrentFrame : -1;

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
                && Controller.CurrentFastForward is {SaveState: true} currentFastForward &&
                Controller.FastForwards.Last(pair => pair.Value.SaveState).Value == currentFastForward &&
                SavedCurrentFrame != currentFastForward.Frame) {
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
            savedGameStatus = GameInfo.Status;
            savedLastVel = GameInfo.LastVel;
            savedLastPlayerSeekerVel = GameInfo.LastPlayerSeekerVel;
            savedCustomInfo = GameInfo.CustomInfo;
            savedStatusWithoutTime = GameInfo.StatusWithoutTime;
            savedLastPos = GameInfo.LastPos;
            savedLastPlayerSeekerPos = GameInfo.LastPlayerSeekerPos;
            savedDashTime = GameInfo.DashTime;
            savedFrozen = GameInfo.Frozen;

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
            savedController = null;
            savedGameStatus = null;
            savedLastVel = null;
            savedLastPlayerSeekerVel = null;
            savedCustomInfo = null;
            savedStatusWithoutTime = null;
            savedLastPos = default;
            savedLastPlayerSeekerPos = default;
            savedDashTime = 0f;
            savedFrozen = false;
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
            GameInfo.Status = savedGameStatus;
            GameInfo.LastVel = savedLastVel;
            GameInfo.LastPlayerSeekerVel = savedLastPlayerSeekerVel;
            GameInfo.CustomInfo = savedCustomInfo;
            GameInfo.StatusWithoutTime = savedStatusWithoutTime;
            GameInfo.LastPos = savedLastPos;
            GameInfo.LastPlayerSeekerPos = savedLastPlayerSeekerPos;
            GameInfo.DashTime = savedDashTime;
            GameInfo.Frozen = savedFrozen;
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

            StudioCommunicationClient.Instance?.SendStateAndGameData(CurrentStatus, GameInfo.Status, false);
        }

        // ReSharper disable once UnusedMember.Local
        [Unload]
        private static void ClearStateWhenHotReload() {
            if (SpeedrunToolInstalled && IsSaved()) {
                Clear();
            }
        }
    }
}