using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Monocle;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS;

/// Handles saving / loading game state with SpeedrunTool
public static class Savestates {
    // These fields can't just be pulled from the current frame and therefore need to be saved too
    private static readonly Dictionary<FieldInfo, object> SavedGameInfo = new() {
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPos)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastDiff)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerPos)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerDiff)), null},
    };

    private static bool savedByBreakpoint;
    private static int savedChecksum;
    private static InputController savedController;

    private static int SavedLine =>
        (savedByBreakpoint
            ? Manager.Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.Line
            : Manager.Controller.Inputs.GetValueOrDefault(SavedCurrentFrame)?.Line) ?? -1;

    public static int StudioHighlightLine => IsSaved_Safe ? SavedLine : -1;
    private static int SavedCurrentFrame => IsSaved ? savedController.CurrentFrameInTAS : -1;

    private static bool BreakpointHasBeenDeleted => IsSaved &&
                                                    savedByBreakpoint &&
                                                    Manager.Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.SaveState != true;

    public static bool IsSaved_Safe => SpeedrunToolUtils.Installed && IsSaved;
    private static bool IsSaved => StateManager.Instance.IsSaved &&
                                   StateManager.Instance.SavedByTas &&
                                   savedController != null &&
                                   savedController.FilePath == Manager.Controller.FilePath;

    [Load]
    private static void Load() {
        if (SpeedrunToolUtils.Installed) {
            SpeedrunToolUtils.AddSaveLoadAction();
        }
    }

    [Unload]
    private static void Unload() {
        if (IsSaved_Safe) {
            ClearState();
        }

        if (SpeedrunToolUtils.Installed) {
            SpeedrunToolUtils.ClearSaveLoadAction();
        }
    }

    /// Update for each TAS frame
    public static void Update() {
        if (!SpeedrunToolUtils.Installed) {
            return;
        }

        // Only save-state when the current breakpoint is the last save-state one
        if (Manager.Controller.Inputs.Count > Manager.Controller.CurrentFrameInTAS &&
            Manager.Controller.CurrentFastForward is { SaveState: true } currentFastForward &&
            Manager.Controller.FastForwards.Last(pair => pair.Value.SaveState).Value == currentFastForward &&
            SavedCurrentFrame != currentFastForward.Frame)
        {
            SaveState(byBreakpoint: true);
            return;
        }

        // Autoload state after entering the level, if the TAS was started outside the level
        if (IsSaved && Engine.Scene is Level && Manager.Controller.CurrentFrameInTAS < savedController.CurrentFrameInTAS) {
            LoadState();
        }
    }

    /// Update for checking hotkeys
    internal static void UpdateMeta() {
        if (!SpeedrunToolUtils.Installed) {
            return;
        }

        if (Manager.Running && Hotkeys.SaveState.Pressed) {
            SaveState(false);
            return;
        }
        if (Hotkeys.ClearState.Pressed) {
            ClearState();
            Manager.DisableRun();
            return;
        }

        if (Manager.Running && BreakpointHasBeenDeleted) {
            ClearState();
        }
    }

    internal static void EnableRun() {
        if (SpeedrunToolUtils.Installed && IsSaved && Engine.Scene is Level) {
            LoadState();
        }
    }

    private static void SaveState(bool byBreakpoint) {
        if (IsSaved &&
            Manager.Controller.CurrentFrameInTAS == savedController.CurrentFrameInTAS &&
            savedChecksum == Manager.Controller.CalcChecksum(savedController.CurrentFrameInTAS))
        {
            return; // Already saved
        }

        if (!StateManager.Instance.SaveState()) {
            return;
        }

        savedByBreakpoint = byBreakpoint;
        savedChecksum = Manager.Controller.CalcChecksum(Manager.Controller.CurrentFrameInTAS);
        savedController = Manager.Controller.Clone();
        SaveGameInfo();
        SetTasState();
    }

    private static void LoadState() {
        // Don't load save-states while recording
        if (TASRecorderUtils.Recording) {
            return;
        }

        if (IsSaved) {
            if (!BreakpointHasBeenDeleted && savedChecksum == Manager.Controller.CalcChecksum(savedController.CurrentFrameInTAS)) {
                if (Manager.Controller.CurrentFrameInTAS == savedController.CurrentFrameInTAS) {
                    // Don't repeat loading the state, just play
                    Manager.NextState = Manager.State.Running;
                    return;
                }

                if (Engine.Scene is Level) {
                    StateManager.Instance.LoadState();
                    Manager.Controller.CopyProgressFrom(savedController);

                    LoadGameInfo();
                    UpdateStudio();
                    SetTasState();
                }
            } else {
                ClearState();
            }
        }
    }

    private static void ClearState() {
        StateManager.Instance.ClearState();
        ClearGameInfo();
        savedByBreakpoint = false;
        savedChecksum = -1;
        savedController = null;

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

    private static void SetTasState() {
        if (Manager.Controller.HasFastForward) {
            Manager.CurrState = Manager.NextState = Manager.State.Running;
        } else {
            Manager.CurrState = Manager.NextState = Manager.State.Paused;
        }
    }

    private static void UpdateStudio() {
        GameInfo.Update();
        Manager.SendStudioState();
    }
}
