using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Utils;
using static TAS.Manager;
using TasState = StudioCommunication.State;

namespace TAS {
    public static class Savestates {
        private static InputController savedController;

        private static readonly Dictionary<FieldInfo, object> SavedGameInfo = new() {
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.Status)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ExactStatus)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.StatusWithoutTime)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ExactStatusWithoutTime)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LevelName)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ChapterTime)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastVel)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastExactVel)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerVel)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerExactVel)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.WatchingInfo)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.CustomInfo)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPos)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerPos)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.DashTime)), null},
            {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.Frozen)), null}
        };

        private static bool savedByBreakpoint;
        private static string savedTasFilePath;

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
            return StateManager.Instance.IsSaved && StateManager.Instance.SavedByTas && savedController != null &&
                   savedTasFilePath == InputController.TasFilePath;
        }

        public static bool IsSaved_Safe() {
            return SpeedrunToolInstalled && IsSaved();
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
                        State &= ~TasState.FrameStep;
                        NextState &= ~TasState.FrameStep;
                        return;
                    }
                }
            }

            if (!StateManager.Instance.SaveState()) {
                return;
            }

            savedByBreakpoint = breakpoint;
            savedTasFilePath = InputController.TasFilePath;
            SaveGameInfo();
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
                        State &= ~TasState.FrameStep;
                        NextState &= ~TasState.FrameStep;
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
            ClearGameInfo();
            savedByBreakpoint = false;
            savedTasFilePath = null;

            UpdateStudio();
        }

        private static void SaveGameInfo() {
            foreach (FieldInfo fieldInfo in SavedGameInfo.Keys.ToList()) {
                SavedGameInfo[fieldInfo] = fieldInfo.GetValue(null);
            }
        }

        private static void LoadGameInfo() {
            foreach (FieldInfo fieldInfo in SavedGameInfo.Keys.ToList()) {
                fieldInfo.SetValue(null, SavedGameInfo[fieldInfo]);
            }
        }

        private static void ClearGameInfo() {
            foreach (FieldInfo fieldInfo in SavedGameInfo.Keys.ToList()) {
                SavedGameInfo[fieldInfo] = null;
            }
        }

        private static void PlayTas() {
            DisableExternal();
            EnableExternal();
        }

        private static void LoadStateRoutine() {
            Controller.CopyFrom(savedController);
            SetTasState();
            LoadGameInfo();
            UpdateStudio();
        }

        private static void SetTasState() {
            if ((CelesteTasModule.Settings.PauseAfterLoadState || savedByBreakpoint) && !(Controller.HasFastForward)) {
                State |= TasState.FrameStep;
            } else {
                State &= ~TasState.FrameStep;
            }

            NextState &= ~TasState.FrameStep;
        }

        private static void UpdateStudio() {
            SendStateToStudio();
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