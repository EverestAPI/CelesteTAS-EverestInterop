using CelesteStudio.Communication;
using CelesteStudio.Editing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Data;

public record struct BindableAction {
    public required MenuEntryCategory Category;
    public required string EntryName;
    public required Hotkey DefaultKeyBinding;

    public static readonly Dictionary<MenuEntry, BindableAction> All = new() {
        {
            MenuEntry.File_New, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "&New File",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.N),
                Action = () => Studio.Instance.OnNewFile(),
            }
        }, {
            MenuEntry.File_Open, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "&Open File...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.O),
                Action = () => Studio.Instance.OnOpenFile(),
            }
        }, {
            MenuEntry.File_OpenPrevious, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "Open &Previous File",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.AlternateModifier | Keys.Left),
                Action = () => Studio.Instance.OpenPrevious(),
            }
        }, {
            MenuEntry.File_Save, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "Save",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.S),
                Action = () => Studio.Instance.OnSaveFile(),
            }
        }, {
            MenuEntry.File_SaveAs, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "&Save As...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.S),
                Action = () => Studio.Instance.OnSaveFileAs(),
            }
        }, {
            MenuEntry.File_Show, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "Show in &File Explorer...",
                DefaultKeyBinding = Hotkey.None,
                Action = () => Studio.Instance.ShowFile(),
            }
        }, {
            MenuEntry.File_RecordTAS, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "&Record TAS...",
                DefaultKeyBinding = Hotkey.None,
                Action = () => Studio.Instance.RecordTAS(),
            }
        }, {
            MenuEntry.File_Quit, new BindableAction {
                Category = MenuEntryCategory.File,
                EntryName = "Quit",
                DefaultKeyBinding = Hotkey.None,
                Action = () => Application.Instance.Quit(),
            }
        }, {
            MenuEntry.Settings_SendInputs, new BindableAction {
                Category = MenuEntryCategory.Settings,
                EntryName = "&Send Inputs to Celeste",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.D),
                Action = () => { }, // TODO settings toggle supports for char shortcuts
            }
        }, {
            MenuEntry.View_ShowGameInfo, new BindableAction {
                Category = MenuEntryCategory.View,
                EntryName = "Show Game Info",
                DefaultKeyBinding = Hotkey.None,
                Action = () => { }, // TODO remove?
            }
        }, {
            MenuEntry.View_ShowSubpixelIndicator, new BindableAction {
                Category = MenuEntryCategory.View,
                EntryName = "Show Subpixel Indicator",
                DefaultKeyBinding = Hotkey.None,
                Action = () => { }, // TODO settings toggle supports for char shortcuts
            }
        }, {
            MenuEntry.View_AlwaysOnTop, new BindableAction {
                Category = MenuEntryCategory.View,
                EntryName = "Always on Top",
                DefaultKeyBinding = Hotkey.None,
                Action = () => { }, // TODO settings toggle supports for char shortcuts
            }
        }, {
            MenuEntry.View_WrapComments, new BindableAction {
                Category = MenuEntryCategory.View,
                EntryName = "Word Wrap Comments",
                DefaultKeyBinding = Hotkey.None,
                Action = () => { }, // TODO settings toggle supports for char shortcuts
            }
        }, {
            MenuEntry.View_ShowFoldingIndicator, new BindableAction {
                Category = MenuEntryCategory.View,
                EntryName = "Show Fold Indicators",
                DefaultKeyBinding = Hotkey.None,
                Action = () => { }, // TODO settings toggle supports for char shortcuts
            }
        }, {
            MenuEntry.Editor_Cut, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Cut",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.X),
                Action = () => Studio.Instance.Editor.OnCut(),
            }
        }, {
            MenuEntry.Editor_Copy, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Copy",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.C),
                Action = () => Studio.Instance.Editor.OnCopy(),
            }
        }, {
            MenuEntry.Editor_Paste, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Paste",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.V),
                Action = () => Studio.Instance.Editor.OnPaste(),
            }
        }, {
            MenuEntry.Editor_Undo, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Undo",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Z),
                Action = () => Studio.Instance.Editor.OnUndo(),
            }
        }, {
            MenuEntry.Editor_Redo, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Redo",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.Z),
                Action = () => Studio.Instance.Editor.OnRedo(),
            }
        }, {
            MenuEntry.Editor_SelectAll, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Select All",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.A),
                Action = () => Studio.Instance.Editor.OnSelectAll(),
            }
        }, {
            MenuEntry.Editor_SelectBlock, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Select Block",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.W),
                Action = () => Studio.Instance.Editor.OnSelectBlock(),
            }
        }, {
            MenuEntry.Editor_Find, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Find...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.F),
                Action = () => Studio.Instance.Editor.OnFind(),
            }
        }, {
            MenuEntry.Editor_GoTo, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Go To...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.G),
                Action = () => Studio.Instance.Editor.OnGoTo(),
            }
        }, {
            MenuEntry.Editor_ToggleFolding, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Toggle Folding",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Minus),
                Action = () => Studio.Instance.Editor.OnToggleFolding(),
            }
        }, {
            MenuEntry.Editor_DeleteSelectedLines, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Delete Selected Lines",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Y),
                Action = () => Studio.Instance.Editor.OnDeleteSelectedLines(),
            }
        }, {
            MenuEntry.Editor_SetFrameCountToStepAmount, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Set Frame Count to current Step Amount",
                DefaultKeyBinding = Hotkey.None,
                Action = () => Studio.Instance.Editor.OnSetFrameCountToStepAmount(),
            }
        }, {
            MenuEntry.Editor_InsertRemoveBreakpoint, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert / Remove Breakpoint",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Period),
                Action = () => Studio.Instance.Editor.InsertOrRemoveText(Editor.UncommentedBreakpointRegex, "***"),
            }
        }, {
            MenuEntry.Editor_InsertRemoveSavestateBreakpoint, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert / Remove Savestate Breakpoint",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.Period),
                Action = () => Studio.Instance.Editor.InsertOrRemoveText(Editor.UncommentedBreakpointRegex, "***S"),
            }
        }, {
            MenuEntry.Editor_RemoveAllUncommentedBreakpoints, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Remove All Uncommented Breakpoints",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.P),
                Action = () => Studio.Instance.Editor.RemoveLinesMatching(Editor.UncommentedBreakpointRegex),
            }
        }, {
            MenuEntry.Editor_RemoveAllBreakpoints, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Remove All Breakpoints",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.P),
                Action = () => Studio.Instance.Editor.RemoveLinesMatching(Editor.AllBreakpointRegex),
            }
        }, {
            MenuEntry.Editor_CommentUncommentAllBreakpoints, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Comment / Uncomment All Breakpoints",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier |
                                               Application.Instance.AlternateModifier | Keys.P),
                Action = () => Studio.Instance.Editor.OnToggleCommentBreakpoints(),
            }
        }, {
            MenuEntry.Editor_CommentUncommentInputs, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Comment / Uncomment Inputs",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.K),
                Action = () => Studio.Instance.Editor.OnToggleCommentInputs(),
            }
        }, {
            MenuEntry.Editor_CommentUncommentText, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Comment / Uncomment Text",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.K),
                Action = () => Studio.Instance.Editor.OnToggleCommentInputs(),
            }
        }, {
            MenuEntry.Editor_InsertRoomName, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Room Name",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.R),
                Action = () => Studio.Instance.Editor.OnInsertRoomName(),
            }
        }, {
            MenuEntry.Editor_InsertCurrentTime, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Current In-Game Time",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.T),
                Action = () => Studio.Instance.Editor.OnInsertTime(),
            }
        }, {
            MenuEntry.Editor_RemoveAllTimestamps, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Remove All Timestamps",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.T),
                Action = () => Studio.Instance.Editor.RemoveLinesMatching(Editor.TimestampRegex),
            }
        }, {
            MenuEntry.Editor_InsertCurrentPosition, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Current Player Position",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => Studio.Instance.Editor.OnInsertPosition(),
            }
        }, {
            MenuEntry.Editor_InsertCurrentSpeed, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Current Player Speed",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => Studio.Instance.Editor.OnInsertSpeed(),
            }
        }, {
            MenuEntry.Editor_InsertModInfo, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Mod Info",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => Studio.Instance.Editor.OnInsertModInfo(),
            }
        }, {
            MenuEntry.Editor_InsertConsoleLoadCommand, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Console Load Command",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.R),
                Action = () => Studio.Instance.Editor.OnInsertConsoleLoadCommand(),
            }
        }, {
            MenuEntry.Editor_InsertSimpleConsoleLoadCommand, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Insert Simple Console Load Command",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier |
                                               Application.Instance.AlternateModifier | Keys.R),
                Action = () => Studio.Instance.Editor.OnInsertSimpleConsoleLoadCommand(),
            }
        }, {
            MenuEntry.Editor_SwapSelectedLR, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Swap Selected L and R",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_SwapSelectedJK, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Swap Selected J and K",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_SwapSelectedXC, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Swap Selected X and C",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_CombineConsecutiveSameInputs, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Combine Consecutive Same Inputs",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.L),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_ForceCombineInputFrames, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Force Combine Input Frames",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.L),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_SplitFrames, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Split Input Frames",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Editor_OpenReadFileGoToPlayLine, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Open Read File / Go To Play Line",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // TODO: remove?
            }
        }, {
            MenuEntry.Editor_OpenAutoCompleteMenu, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Open Auto Complete menu...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Space),
                Action = () => Studio.Instance.Editor.OpenAutoComplete(),
            }
        }, {
            MenuEntry.Editor_OpenContextActionsMenu, new BindableAction {
                Category = MenuEntryCategory.Editor,
                EntryName = "Open Context Actions menu...",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.AlternateModifier | Keys.Enter),
                Action = () => Studio.Instance.Editor.OpenContextActions(),
            }
        }, {
            MenuEntry.ContextActions_InlineReadCommand, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Inline Read-command",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_InlineRepeatCommand, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Inline Repeat-command",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_CreateRepeatCommand, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Create Repeat-command",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_SwapActionsLR, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Swap L and R",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_SwapActionsJK, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Swap J and K",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_SwapActionsXC, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Swap X and C",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_CombineConsecutiveSameInputs, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Combine Consecutive Same Inputs",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.L),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_ForceCombineInputFrames, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Force Combine Input Frames",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.L),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_SplitFrames, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Split Input Frames",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_OpenReadFile, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Open Read File",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.ContextActions_GoToPlayLine, new BindableAction {
                Category = MenuEntryCategory.ContextActions,
                EntryName = "Go To Play Line",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // is a context action
            }
        }, {
            MenuEntry.Status_CopyGameInfoToClipboard, new BindableAction {
                Category = MenuEntryCategory.Status,
                EntryName = "&Copy Game Info to Clipboard",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.C),
                Action = () => Studio.Instance.GameInfo.CopyGameInfoToClipboard(),
            }
        }, {
            MenuEntry.Status_ReconnectStudioCeleste, new BindableAction {
                Category = MenuEntryCategory.Status,
                EntryName = "&Reconnect Studio and Celeste",
                DefaultKeyBinding = Hotkey.Key(Application.Instance.CommonModifier | Keys.Shift | Keys.D),
                Action = CommunicationWrapper.ForceReconnect,
            }
        }, {
            MenuEntry.Status_EditCustomInfoTemplate, new BindableAction {
                Category = MenuEntryCategory.Status,
                EntryName = "&Edit Custom Info Template",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => Studio.Instance.GameInfo.EditCustomInfoTemplate(),
            }
        }, {
            MenuEntry.Status_ClearWatchEntityInfo, new BindableAction {
                Category = MenuEntryCategory.Status,
                EntryName = "Clear Watch Entity Info",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = CommunicationWrapper.ClearWatchEntityInfo,
            }
        }, {
            MenuEntry.StatusPopout_AlwaysOnTop, new BindableAction {
                Category = MenuEntryCategory.StatusPopout,
                EntryName = "Always on Top",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => { }, // TODO: make this bindable?
            }
        }, {
            MenuEntry.Game_Start, new BindableAction {
                Category = MenuEntryCategory.GameHotkeys,
                EntryName = "Start",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => CommunicationWrapper.SendHotkey(HotkeyID.Start),
            }
        }, {
            MenuEntry.Game_Pause, new BindableAction {
                Category = MenuEntryCategory.GameHotkeys,
                EntryName = "Pause",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => CommunicationWrapper.SendHotkey(HotkeyID.Pause),
            }
        }, {
            MenuEntry.Game_Restart, new BindableAction {
                Category = MenuEntryCategory.GameHotkeys,
                EntryName = "Restart",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => CommunicationWrapper.SendHotkey(HotkeyID.Restart),
            }
        }, {
            MenuEntry.Game_FrameAdvance, new BindableAction {
                Category = MenuEntryCategory.GameHotkeys,
                EntryName = "Advance Frame",
                DefaultKeyBinding = Hotkey.Key(Keys.None),
                Action = () => CommunicationWrapper.SendHotkey(HotkeyID.FrameAdvance),
            }
        },
    };

    public required Action Action;
}

// ReSharper disable InconsistentNaming
public enum MenuEntry {
    File_New, File_Open, File_OpenPrevious, File_Save, File_SaveAs, File_Show, File_RecordTAS, File_Quit,
    Settings_SendInputs,
    View_ShowGameInfo, View_ShowSubpixelIndicator, View_AlwaysOnTop, View_WrapComments, View_ShowFoldingIndicator,

    Editor_Cut, Editor_Copy, Editor_Paste,
    Editor_Undo, Editor_Redo,
    Editor_SelectAll, Editor_SelectBlock,
    Editor_Find, Editor_GoTo, Editor_ToggleFolding,
    Editor_DeleteSelectedLines, Editor_SetFrameCountToStepAmount,
    Editor_InsertRemoveBreakpoint, Editor_InsertRemoveSavestateBreakpoint, Editor_RemoveAllUncommentedBreakpoints, Editor_RemoveAllBreakpoints, Editor_CommentUncommentAllBreakpoints, Editor_CommentUncommentInputs, Editor_CommentUncommentText,
    Editor_InsertRoomName, Editor_InsertCurrentTime, Editor_RemoveAllTimestamps, Editor_InsertCurrentPosition, Editor_InsertCurrentSpeed, Editor_InsertModInfo, Editor_InsertConsoleLoadCommand, Editor_InsertSimpleConsoleLoadCommand,
    Editor_SwapSelectedLR, Editor_SwapSelectedJK, Editor_SwapSelectedXC, Editor_CombineConsecutiveSameInputs, Editor_ForceCombineInputFrames, Editor_SplitFrames,
    Editor_OpenReadFileGoToPlayLine,
    Editor_OpenAutoCompleteMenu, Editor_OpenContextActionsMenu,

    ContextActions_InlineReadCommand, ContextActions_InlineRepeatCommand, ContextActions_CreateRepeatCommand, ContextActions_SwapActionsLR, ContextActions_SwapActionsJK, ContextActions_SwapActionsXC,
    ContextActions_CombineConsecutiveSameInputs, ContextActions_ForceCombineInputFrames, ContextActions_SplitFrames, ContextActions_OpenReadFile, ContextActions_GoToPlayLine,

    Status_CopyGameInfoToClipboard, Status_ReconnectStudioCeleste,
    Status_EditCustomInfoTemplate, Status_ClearWatchEntityInfo,

    StatusPopout_AlwaysOnTop,

    Game_Start, Game_Restart, Game_FrameAdvance, Game_Pause,
}
public enum MenuEntryCategory { File, Settings, View, Editor, ContextActions, Status, StatusPopout, GameHotkeys }

public static class MenuEntryExtensions {
    private static readonly Dictionary<MenuEntryCategory, MenuEntry[]> Categories = new() {
        { MenuEntryCategory.File, [
            MenuEntry.File_New, MenuEntry.File_Open, MenuEntry.File_OpenPrevious, MenuEntry.File_Save, MenuEntry.File_SaveAs, MenuEntry.File_Show, MenuEntry.File_RecordTAS, MenuEntry.File_Quit] },

        { MenuEntryCategory.Settings, [
            MenuEntry.Settings_SendInputs] },

        { MenuEntryCategory.View, [
            MenuEntry.View_ShowGameInfo, MenuEntry.View_ShowSubpixelIndicator, MenuEntry.View_AlwaysOnTop, MenuEntry.View_WrapComments, MenuEntry.View_ShowFoldingIndicator] },

        { MenuEntryCategory.Editor, [
            MenuEntry.Editor_Cut, MenuEntry.Editor_Copy, MenuEntry.Editor_Paste,
            MenuEntry.Editor_Undo, MenuEntry.Editor_Redo,
            MenuEntry.Editor_SelectAll, MenuEntry.Editor_SelectBlock,
            MenuEntry.Editor_Find, MenuEntry.Editor_GoTo, MenuEntry.Editor_ToggleFolding,
            MenuEntry.Editor_DeleteSelectedLines, MenuEntry.Editor_SetFrameCountToStepAmount,
            MenuEntry.Editor_InsertRemoveBreakpoint, MenuEntry.Editor_InsertRemoveSavestateBreakpoint, MenuEntry.Editor_RemoveAllUncommentedBreakpoints, MenuEntry.Editor_RemoveAllBreakpoints, MenuEntry.Editor_CommentUncommentAllBreakpoints, MenuEntry.Editor_CommentUncommentInputs, MenuEntry.Editor_CommentUncommentText,
            MenuEntry.Editor_InsertRoomName, MenuEntry.Editor_InsertCurrentTime, MenuEntry.Editor_RemoveAllTimestamps, MenuEntry.Editor_InsertCurrentPosition, MenuEntry.Editor_InsertCurrentSpeed, MenuEntry.Editor_InsertModInfo, MenuEntry.Editor_InsertConsoleLoadCommand, MenuEntry.Editor_InsertSimpleConsoleLoadCommand,
            MenuEntry.Editor_SwapSelectedLR, MenuEntry.Editor_SwapSelectedJK, MenuEntry.Editor_SwapSelectedXC, MenuEntry.Editor_CombineConsecutiveSameInputs, MenuEntry.Editor_ForceCombineInputFrames, MenuEntry.Editor_SplitFrames,
            MenuEntry.Editor_OpenReadFileGoToPlayLine,
            MenuEntry.Editor_OpenAutoCompleteMenu, MenuEntry.Editor_OpenContextActionsMenu] },

        { MenuEntryCategory.ContextActions, [
            MenuEntry.ContextActions_InlineReadCommand, MenuEntry.ContextActions_InlineRepeatCommand, MenuEntry.ContextActions_CreateRepeatCommand, MenuEntry.ContextActions_SwapActionsLR, MenuEntry.ContextActions_SwapActionsJK, MenuEntry.ContextActions_SwapActionsXC,
            MenuEntry.ContextActions_CombineConsecutiveSameInputs, MenuEntry.ContextActions_ForceCombineInputFrames, MenuEntry.ContextActions_SplitFrames, MenuEntry.ContextActions_OpenReadFile, MenuEntry.ContextActions_GoToPlayLine] },

        { MenuEntryCategory.Status, [
            MenuEntry.Status_CopyGameInfoToClipboard, MenuEntry.Status_ReconnectStudioCeleste,
            MenuEntry.Status_EditCustomInfoTemplate, MenuEntry.Status_ClearWatchEntityInfo] },

        { MenuEntryCategory.StatusPopout, [MenuEntry.StatusPopout_AlwaysOnTop] },

        { MenuEntryCategory.GameHotkeys, [MenuEntry.Game_Start, MenuEntry.Game_Pause, MenuEntry.Game_Restart, MenuEntry.Game_FrameAdvance] },
    };

#if DEBUG
    public static void VerifyData() {
        // Ensures that every entry has all the required data
        foreach (var entry in Enum.GetValues<MenuEntry>()) {
            foreach (var category in Enum.GetValues<MenuEntryCategory>()) {
                var entries = Categories[category];
                if (entries.Contains(entry)) {
                    goto NextIter;
                }
            }

            throw new Exception($"Entry '{entry}' is not assigned to a category");

            NextIter:;
        }
    }
#endif

    public static MenuEntry[] GetEntries(this MenuEntryCategory category) => Categories[category];
    public static string GetName(this MenuEntryCategory category) => category switch {
        MenuEntryCategory.File => "File",
        MenuEntryCategory.Settings => "Settings",
        MenuEntryCategory.View => "View",
        MenuEntryCategory.Editor => "Editor",
        MenuEntryCategory.ContextActions => "Context Actions",
        MenuEntryCategory.Status => "Game Info",
        MenuEntryCategory.StatusPopout => "Game Info Popout",
        MenuEntryCategory.GameHotkeys => "Additional Game Hotkeys",
        _ => throw new UnreachableException(),
    };

    public static BindableAction Get(this MenuEntry entry) => BindableAction.All[entry];
    
    public static string GetName(this MenuEntry entry) => BindableAction.All[entry].EntryName;
    public static Hotkey GetDefaultHotkey(this MenuEntry entry) => BindableAction.All[entry].DefaultKeyBinding;
    public static Hotkey GetHotkey(this MenuEntry entry) => Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut) ? shortcut : GetDefaultHotkey(entry);

    public static CheckMenuItem ToCheckbox(this MenuEntry entry) {
        var action = entry.Get();
        return new CheckMenuItem {
            Text = action.EntryName,
            Shortcut = Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut)
                ? shortcut.KeyOrNone
                : action.DefaultKeyBinding.KeyOrNone,
        };
    }

    public static MenuItem ToAction(this MenuEntry entry) {
        var action = entry.Get();
        return MenuUtils.CreateAction(
            action.EntryName,
            Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut)
                ? shortcut.KeyOrNone
                : action.DefaultKeyBinding.KeyOrNone,
            action.Action);
    }

    public static MenuItem ToSettingToggle(this MenuEntry entry, string settingName, Action<bool>? onChanged = null) {
        var action = entry.Get();
        return MenuUtils.CreateSettingToggle(
            action.EntryName,
            settingName,
            Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut)
                ? shortcut.KeyOrNone
                : action.DefaultKeyBinding.KeyOrNone,
            onChanged);
    }
}
