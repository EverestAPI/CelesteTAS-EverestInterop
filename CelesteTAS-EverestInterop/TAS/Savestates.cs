using System;
using System.Collections;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using TAS.Input;
using static TAS.Manager;

namespace TAS {
// TODO Add a command to check if savestate will cause desync
static class Savestates {
    public static Coroutine routine;
    private static InputController savedController;
    private static int? savedLine;
    private static string savedPlayerStatus;
    private static Vector2 savedLastPos;
    private static Vector2 savedLastPlayerSeekerPos;
    private static bool savedByBreakpoint;
    private static AnalogueMode savedAnalogueMode;

    private static readonly Lazy<bool> speedrunToolInstalledLazy = new Lazy<bool>(() =>
        Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
    );

    public static int StudioHighlightLine => (speedrunToolInstalledLazy.Value && IsSaved() && savedLine.HasValue ? savedLine.Value : -1);
    public static bool SpeedrunToolInstalled => speedrunToolInstalledLazy.Value;

    private static bool BreakpointHasBeenDeleted =>
            IsSaved() && savedByBreakpoint
            && savedController.FfIndex < savedController.fastForwards.Count
            && !controller.fastForwards.Any(ff => ff.SaveState && ff.frame == savedController.CurrentFF.frame);

    private static bool IsSaved() {
        return StateManager.Instance.IsSaved && StateManager.Instance.SavedByTas && savedController != null;
    }

    public static void HandleSaveStates() {
        if (!SpeedrunToolInstalled) {
            return;
        }

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
            && controller.inputs.Count > controller.CurrentFrame
            && controller.fastForwards.Count > controller.FfIndex
            && controller.CurrentFF.SaveState && !controller.CurrentFF.HasSavedState
            && controller.CurrentFF.frame == controller.CurrentFrame
            && controller.fastForwards.LastOrDefault(record => record.SaveState) == controller.CurrentFF) {
            Save(true);
            return;
        }

        // auto load state after entering the level if tas is started from outside the level.
        if (Running && IsSaved() && Engine.Scene is Level && controller.CurrentFrame < savedController.CurrentFrame) {
            Load();
        }
    }

    private static void Save(bool breakpoint) {
        if (IsSaved()) {
            if (controller.CurrentFrame == savedController.CurrentFrame) {
                if (savedController.SavedChecksum == controller.Checksum(savedController)) {
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                    return;
                }
            }
        }

        if (!StateManager.Instance.SaveState()) {
            return;
        }

        if (breakpoint && controller.CurrentFF.SaveState) {
            controller.CurrentFF.HasSavedState = true;
        }

        if (breakpoint) {
            savedLine = controller.Current.Line - 1;
        } else {
            savedLine = controller.Current.Line;
        }

        savedByBreakpoint = breakpoint;
        savedPlayerStatus = PlayerStatus;
        savedLastPos = LastPos;
        savedLastPlayerSeekerPos = LastPlayerSeekerPos;
        savedAnalogueMode = analogueMode;

        savedController = controller.Clone();
        LoadStateRoutine();
    }

    private static void Load() {
        if (Engine.Scene is LevelLoader) {
            return;
        }

        if (IsSaved()) {
            controller.RefreshInputs(false);
            if (!BreakpointHasBeenDeleted && savedController.SavedChecksum == controller.Checksum(savedController)) {
                if (Running && controller.CurrentFrame == savedController.CurrentFrame) {
                    // Don't repeat load state, just play
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
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
        PlayTAS();
    }

    private static void Clear() {
        StateManager.Instance.ClearState();
        routine = null;
        savedController = null;
        savedLine = null;
        savedPlayerStatus = null;
        savedLastPos = default;
        savedLastPlayerSeekerPos = default;
        savedByBreakpoint = false;
        foreach (FastForward fastForward in controller.fastForwards) {
            fastForward.HasSavedState = false;
        }

        UpdateStudio();
    }

    private static void PlayTAS() {
        DisableExternal();
        EnableExternal();
    }

    private static void LoadStateRoutine() {
        controller = savedController.Clone();
        controller.RefreshInputs(false);
       // Some fields were reset by RefreshInputs(false), so we need restore it.
       controller.FfIndex = savedController.FfIndex;
       for (int i = 0; i < savedController.fastForwards.Count && i < controller.fastForwards.Count; i++) {
           if (savedController.fastForwards[i].HasSavedState) {
               controller.fastForwards[i].HasSavedState = true;
               break;
           }
       }

        SetTasState();
        analogueMode = savedAnalogueMode;
        PlayerStatus = savedPlayerStatus;
        LastPos = savedLastPos;
        LastPlayerSeekerPos = savedLastPlayerSeekerPos;
        UpdateStudio();
    }

    private static void SetTasState() {
        if ((CelesteTASModule.Settings.PauseAfterLoadState || savedByBreakpoint) && !(controller.HasFastForward)) {
            state |= State.FrameStep;
        } else {
            state &= ~State.FrameStep;
        }

        nextState &= ~State.FrameStep;
    }

    private static void UpdateStudio() {
        if (controller.CurrentFrame > 0) {
            UpdateManagerStatus();
        }
        StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
    }
}
}