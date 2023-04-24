using System;
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
using static TAS.Manager;
using TasStates = TAS.States;

namespace TAS;

public static class Savestates {
    private static InputController savedController;

    private static readonly Dictionary<FieldInfo, object> SavedGameInfo = new() {
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.Status)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ExactStatus)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.StatusWithoutTime)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ExactStatusWithoutTime)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LevelName)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.ChapterTime)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.WatchingInfo)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.CustomInfo)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPos)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastDiff)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerPos)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.LastPlayerSeekerDiff)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.DashTime)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.Frozen)), null},
        {typeof(GameInfo).GetFieldInfo(nameof(GameInfo.TransitionFrames)), null},
    };

    private static bool savedByBreakpoint;
    private static string savedTasFilePath;

    private static readonly Lazy<bool> SpeedrunToolInstalledLazy = new(() => ModUtils.IsInstalled("SpeedrunTool"));

    private static int SavedLine =>
        (savedByBreakpoint
            ? Controller.FastForwards.GetValueOrDefault(SavedCurrentFrame)?.Line
            : Controller.Inputs.GetValueOrDefault(SavedCurrentFrame)?.Line) ?? -1;

    private static int SavedCurrentFrame => IsSaved() ? savedController.CurrentFrameInTas : -1;

    public static int StudioHighlightLine => IsSaved_Safe() ? SavedLine : -1;
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

        if (!Running && IsSaved() && Engine.Scene is Level && Hotkeys.StartStop.Released) {
            Load();
            return;
        }

        if (Running && Hotkeys.SaveState.Pressed) {
            Save(false);
            return;
        }

        if (Hotkeys.Restart.Released) {
            Load();
            return;
        }

        if (Hotkeys.ClearState.Pressed) {
            Clear();
            DisableRun();
            return;
        }

        if (Running && BreakpointHasBeenDeleted) {
            Clear();
        }

        // save state when tas run to the last savestate breakpoint
        if (Running
            && Controller.Inputs.Count > Controller.CurrentFrameInTas
            && Controller.FastForwards.GetValueOrDefault(Controller.CurrentFrameInTas) is {SaveState: true} currentFastForward &&
            Controller.FastForwards.Last(pair => pair.Value.SaveState).Value == currentFastForward &&
            SavedCurrentFrame != currentFastForward.Frame) {
            Save(true);
            return;
        }

        // auto load state after entering the level if tas is started from outside the level.
        if (Running && IsSaved() && Engine.Scene is Level && Controller.CurrentFrameInTas < savedController.CurrentFrameInTas) {
            Load();
        }
    }

    private static void Save(bool breakpoint) {
        if (IsSaved()) {
            if (Controller.CurrentFrameInTas == savedController.CurrentFrameInTas) {
                if (savedController.SavestateChecksum == Controller.CalcChecksum(savedController)) {
                    Manager.States &= ~TasStates.FrameStep;
                    NextStates &= ~TasStates.FrameStep;
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
        SetTasState();
    }

    private static void Load() {
        if (IsSaved()) {
            Controller.RefreshInputs(false);
            if (!BreakpointHasBeenDeleted && savedController.SavestateChecksum == Controller.CalcChecksum(savedController)) {
                if (Running && Controller.CurrentFrameInTas == savedController.CurrentFrameInTas) {
                    // Don't repeat load state, just play
                    Manager.States &= ~TasStates.FrameStep;
                    NextStates &= ~TasStates.FrameStep;
                    return;
                }

                if (Engine.Scene is Level) {
                    if (!Running) {
                        EnableRun();
                    }

                    // make sure LoadState is after EnableRun, otherwise the input state will be reset in BindingHelper.SetTasBindings
                    StateManager.Instance.LoadState();

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
        DisableRun();
        EnableRun();
    }

    private static void LoadStateRoutine() {
        Controller.CopyProgressFrom(savedController);
        SetTasState();
        LoadGameInfo();
        UpdateStudio();
    }

    private static void SetTasState() {
        if (Controller.HasFastForward) {
            Manager.States &= ~TasStates.FrameStep;
        } else {
            Manager.States |= TasStates.FrameStep;
        }

        NextStates &= ~TasStates.FrameStep;
    }

    private static void UpdateStudio() {
        GameInfo.Update();
        SendStateToStudio();
    }

    [Load]
    private static void OnLoad() {
        if (SpeedrunToolInstalled) {
            SpeedrunToolUtils.AddSaveLoadAction();
        }
    }

    [Unload]
    private static void OnUnload() {
        if (IsSaved_Safe()) {
            Clear();
        }

        if (SpeedrunToolInstalled) {
            SpeedrunToolUtils.ClearSaveLoadAction();
        }
    }
}