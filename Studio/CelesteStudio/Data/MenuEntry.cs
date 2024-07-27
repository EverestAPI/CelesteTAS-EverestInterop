using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CelesteStudio.Util;
using Eto.Forms;

namespace CelesteStudio.Data;

public enum MenuEntry {
    File_New, File_Open, File_OpenPrevious, File_Save, File_SaveAs, File_RecordTAS, File_Quit,
    Settings_SendInputs,
    View_ShowGameInfo, View_ShowSubpixelIndicator, View_AlwaysOnTop, View_WrapComments, View_ShowFoldingIndicator,
    
    Editor_Cut, Editor_Copy, Editor_Paste,
    Editor_Undo, Editor_Redo,
    Editor_SelectAll, Editor_SelectBlock,
    Editor_Find, Editor_GoTo, Editor_ToggleFolding,
    Editor_DeleteSelectedLines,
    Editor_InsertRemoveBreakpoint, Editor_InsertRemoveSavestateBreakpoint, Editor_RemoveAllUncommentedBreakpoints, Editor_RemoveAllBreakpoints, Editor_CommentUncommentAllBreakpoints, Editor_CommentUncommentInputs, Editor_CommentUncommentText,
    Editor_InsertRoomName, Editor_InsertCurrentTime, Editor_RemoveAllTimestamps, Editor_InsertModInfo, Editor_InsertConsoleLoadCommand, Editor_InsertSimpleConsoleLoadCommand,
    Editor_SwapSelectedLR, Editor_SwapSelectedJK, Editor_SwapSelectedXC, Editor_CombineConsecutiveSameInputs, Editor_ForceCombineInputFrames, Editor_SplitFrames,
    Editor_OpenReadFileGoToPlayLine,
    
    Status_CopyGameInfoToClipboard, Status_ReconenctStudioCeleste,
    Status_EditCustomInfoTemplate, Status_ClearWatchEntityInfo,
    
    StatusPopout_AlwaysOnTop,
    
    Game_Start, Game_Restart, Game_FrameAdvance, Game_Pause,
}
public enum MenuEntryCategory { File, Settings, View, Editor, Status, StatusPopout, GameHotkeys }

public static class MenuEntryExtensions {
    private static readonly Dictionary<MenuEntry, Keys> DefaultKeyBindings = new() {
        { MenuEntry.File_New, Application.Instance.CommonModifier | Keys.N },
        { MenuEntry.File_Open, Application.Instance.CommonModifier | Keys.O },
        { MenuEntry.File_OpenPrevious, Application.Instance.AlternateModifier | Keys.Left },
        { MenuEntry.File_Save, Application.Instance.CommonModifier | Keys.S },
        { MenuEntry.File_SaveAs, Application.Instance.CommonModifier | Keys.Shift | Keys.S },
        { MenuEntry.File_RecordTAS, Keys.None },
        { MenuEntry.File_Quit, Keys.None },

        { MenuEntry.Settings_SendInputs, Application.Instance.CommonModifier | Keys.D },

        { MenuEntry.View_ShowGameInfo, Keys.None },
        { MenuEntry.View_ShowSubpixelIndicator, Keys.None },
        { MenuEntry.View_AlwaysOnTop, Keys.None },
        { MenuEntry.View_WrapComments, Keys.None },
        { MenuEntry.View_ShowFoldingIndicator, Keys.None },

        { MenuEntry.Editor_Cut, Application.Instance.CommonModifier | Keys.X },
        { MenuEntry.Editor_Copy, Application.Instance.CommonModifier | Keys.C },
        { MenuEntry.Editor_Paste, Application.Instance.CommonModifier | Keys.V },
        { MenuEntry.Editor_Undo, Application.Instance.CommonModifier | Keys.Z },
        { MenuEntry.Editor_Redo, Application.Instance.CommonModifier | Keys.Shift | Keys.Z },
        { MenuEntry.Editor_SelectAll, Application.Instance.CommonModifier | Keys.A },
        { MenuEntry.Editor_SelectBlock, Application.Instance.CommonModifier | Keys.W },
        { MenuEntry.Editor_Find, Application.Instance.CommonModifier | Keys.F },
        { MenuEntry.Editor_GoTo, Application.Instance.CommonModifier | Keys.G },
        { MenuEntry.Editor_ToggleFolding, Application.Instance.CommonModifier | Keys.Minus },
        { MenuEntry.Editor_DeleteSelectedLines, Application.Instance.CommonModifier | Keys.Y },
        { MenuEntry.Editor_InsertRemoveBreakpoint, Application.Instance.CommonModifier | Keys.Period },
        { MenuEntry.Editor_InsertRemoveSavestateBreakpoint, Application.Instance.CommonModifier | Keys.Shift | Keys.Period },
        { MenuEntry.Editor_RemoveAllUncommentedBreakpoints, Application.Instance.CommonModifier | Keys.P },
        { MenuEntry.Editor_RemoveAllBreakpoints, Application.Instance.CommonModifier | Keys.Shift | Keys.P },
        { MenuEntry.Editor_CommentUncommentAllBreakpoints, Application.Instance.CommonModifier | Application.Instance.AlternateModifier | Keys.P },
        { MenuEntry.Editor_CommentUncommentInputs, Application.Instance.CommonModifier | Keys.K },
        { MenuEntry.Editor_CommentUncommentText, Application.Instance.CommonModifier | Keys.Shift | Keys.K },
        { MenuEntry.Editor_InsertRoomName, Application.Instance.CommonModifier | Keys.R },
        { MenuEntry.Editor_InsertCurrentTime, Application.Instance.CommonModifier | Keys.T },
        { MenuEntry.Editor_RemoveAllTimestamps, Application.Instance.CommonModifier | Keys.Shift | Keys.T },
        { MenuEntry.Editor_InsertModInfo, Keys.None },
        { MenuEntry.Editor_InsertConsoleLoadCommand, Application.Instance.CommonModifier | Keys.Shift | Keys.R },
        { MenuEntry.Editor_InsertSimpleConsoleLoadCommand, Application.Instance.CommonModifier | Application.Instance.AlternateModifier | Keys.R },
        { MenuEntry.Editor_SwapSelectedLR, Keys.None },
        { MenuEntry.Editor_SwapSelectedJK, Keys.None },
        { MenuEntry.Editor_SwapSelectedXC, Keys.None },
        { MenuEntry.Editor_CombineConsecutiveSameInputs, Application.Instance.CommonModifier | Keys.L },
        { MenuEntry.Editor_ForceCombineInputFrames, Application.Instance.CommonModifier | Keys.Shift | Keys.L },
        { MenuEntry.Editor_SplitFrames, Keys.None },
        { MenuEntry.Editor_OpenReadFileGoToPlayLine, Keys.None },
        
        { MenuEntry.Status_CopyGameInfoToClipboard, Application.Instance.CommonModifier | Keys.Shift | Keys.C },
        { MenuEntry.Status_ReconenctStudioCeleste, Application.Instance.CommonModifier | Keys.Shift | Keys.D },
        { MenuEntry.Status_EditCustomInfoTemplate, Keys.None },
        { MenuEntry.Status_ClearWatchEntityInfo, Keys.None },
        
        { MenuEntry.StatusPopout_AlwaysOnTop, Keys.None },
        
        { MenuEntry.Game_Start, Keys.None },
        { MenuEntry.Game_Pause, Keys.None },
        { MenuEntry.Game_Restart, Keys.None },
        { MenuEntry.Game_FrameAdvance, Keys.None },
    };
    private static readonly Dictionary<MenuEntry, string> EntryNames = new() {
        { MenuEntry.File_New, "&New File" },
        { MenuEntry.File_Open, "&Open File..." },
        { MenuEntry.File_OpenPrevious, "Open &Previous File" },
        { MenuEntry.File_Save, "Save" },
        { MenuEntry.File_SaveAs, "&Save As..." },
        { MenuEntry.File_RecordTAS, "&Record TAS..." },
        { MenuEntry.File_Quit, "Quit" },

        { MenuEntry.Settings_SendInputs, "&Send Inputs to Celeste" },

        { MenuEntry.View_ShowGameInfo, "Show Game Info" },
        { MenuEntry.View_ShowSubpixelIndicator, "Show Subpixel Indicator" },
        { MenuEntry.View_AlwaysOnTop, "Always on Top" },
        { MenuEntry.View_WrapComments, "Word Wrap Comments" },
        { MenuEntry.View_ShowFoldingIndicator, "Show Fold Indicators" },

        { MenuEntry.Editor_Cut, "Cut" },
        { MenuEntry.Editor_Copy, "Copy" },
        { MenuEntry.Editor_Paste, "Paste" },
        { MenuEntry.Editor_Undo, "Undo" },
        { MenuEntry.Editor_Redo, "Redo" },
        { MenuEntry.Editor_SelectAll, "Select All" },
        { MenuEntry.Editor_SelectBlock, "Select Block" },
        { MenuEntry.Editor_Find, "Find..." },
        { MenuEntry.Editor_GoTo, "Go To..." },
        { MenuEntry.Editor_ToggleFolding, "Toggle Folding" },
        { MenuEntry.Editor_DeleteSelectedLines, "Delete Selected Lines" },
        { MenuEntry.Editor_InsertRemoveBreakpoint, "Insert / Remove Breakpoint" },
        { MenuEntry.Editor_InsertRemoveSavestateBreakpoint, "Insert / Remove Savestate Breakpoint" },
        { MenuEntry.Editor_RemoveAllUncommentedBreakpoints, "Remove All Uncommented Breakpoints" },
        { MenuEntry.Editor_RemoveAllBreakpoints, "Remove All Breakpoints" },
        { MenuEntry.Editor_CommentUncommentAllBreakpoints, "Comment / Uncomment All Breakpoints" },
        { MenuEntry.Editor_CommentUncommentInputs, "Comment / Uncomment Inputs" },
        { MenuEntry.Editor_CommentUncommentText, "Comment / Uncomment Text" },
        { MenuEntry.Editor_InsertRoomName, "Insert Room Name" },
        { MenuEntry.Editor_InsertCurrentTime, "Insert Current In-Game Time" },
        { MenuEntry.Editor_RemoveAllTimestamps, "Remove All Timestamps" },
        { MenuEntry.Editor_InsertModInfo, "Insert Mod Info" },
        { MenuEntry.Editor_InsertConsoleLoadCommand, "Insert Console Load Command" },
        { MenuEntry.Editor_InsertSimpleConsoleLoadCommand, "Insert Simple Console Load Command" },
        { MenuEntry.Editor_SwapSelectedLR, "Swap Selected L and R" },
        { MenuEntry.Editor_SwapSelectedJK, "Swap Selected J and K" },
        { MenuEntry.Editor_SwapSelectedXC, "Swap Selected X and C" },
        { MenuEntry.Editor_CombineConsecutiveSameInputs, "Combine Consecutive Same Inputs" },
        { MenuEntry.Editor_ForceCombineInputFrames, "Force Combine Input Frames" },
        { MenuEntry.Editor_SplitFrames, "Split Input Frames" },
        { MenuEntry.Editor_OpenReadFileGoToPlayLine, "Open Read File / Go To Play Line" },
        
        { MenuEntry.Status_CopyGameInfoToClipboard, "&Copy Game Info to Clipboard" },
        { MenuEntry.Status_ReconenctStudioCeleste, "&Reconnect Studio and Celeste" },
        { MenuEntry.Status_EditCustomInfoTemplate, "&Edit Custom Info Template" },
        { MenuEntry.Status_ClearWatchEntityInfo, "Clear Watch Entity Info" },
        
        { MenuEntry.StatusPopout_AlwaysOnTop, "Always on Top" },
        
        { MenuEntry.Game_Start, "Start" },
        { MenuEntry.Game_Pause, "Pause" },
        { MenuEntry.Game_Restart, "Restart" },
        { MenuEntry.Game_FrameAdvance, "Advance Frame" },
    };
    private static readonly Dictionary<MenuEntryCategory, MenuEntry[]> Categories = new() {
        { MenuEntryCategory.File, [
            MenuEntry.File_New, MenuEntry.File_Open, MenuEntry.File_OpenPrevious, MenuEntry.File_Save, MenuEntry.File_SaveAs, MenuEntry.File_RecordTAS, MenuEntry.File_Quit] },

        { MenuEntryCategory.Settings, [
            MenuEntry.Settings_SendInputs] },

        { MenuEntryCategory.View, [
            MenuEntry.View_ShowGameInfo, MenuEntry.View_ShowSubpixelIndicator, MenuEntry.View_AlwaysOnTop, MenuEntry.View_WrapComments, MenuEntry.View_ShowFoldingIndicator] },

        { MenuEntryCategory.Editor, [
            MenuEntry.Editor_Cut, MenuEntry.Editor_Copy, MenuEntry.Editor_Paste,
            MenuEntry.Editor_Undo, MenuEntry.Editor_Redo,
            MenuEntry.Editor_SelectAll, MenuEntry.Editor_SelectBlock,
            MenuEntry.Editor_Find, MenuEntry.Editor_GoTo, MenuEntry.Editor_ToggleFolding,
            MenuEntry.Editor_DeleteSelectedLines,
            MenuEntry.Editor_InsertRemoveBreakpoint, MenuEntry.Editor_InsertRemoveSavestateBreakpoint, MenuEntry.Editor_RemoveAllUncommentedBreakpoints, MenuEntry.Editor_RemoveAllBreakpoints, MenuEntry.Editor_CommentUncommentAllBreakpoints, MenuEntry.Editor_CommentUncommentInputs, MenuEntry.Editor_CommentUncommentText,
            MenuEntry.Editor_InsertRoomName, MenuEntry.Editor_InsertCurrentTime, MenuEntry.Editor_RemoveAllTimestamps, MenuEntry.Editor_InsertModInfo, MenuEntry.Editor_InsertConsoleLoadCommand, MenuEntry.Editor_InsertSimpleConsoleLoadCommand,
            MenuEntry.Editor_SwapSelectedLR, MenuEntry.Editor_SwapSelectedJK, MenuEntry.Editor_SwapSelectedXC, MenuEntry.Editor_CombineConsecutiveSameInputs, MenuEntry.Editor_ForceCombineInputFrames, MenuEntry.Editor_SplitFrames,
            MenuEntry.Editor_OpenReadFileGoToPlayLine] },

        { MenuEntryCategory.Status, [
            MenuEntry.Status_CopyGameInfoToClipboard, MenuEntry.Status_ReconenctStudioCeleste,
            MenuEntry.Status_EditCustomInfoTemplate, MenuEntry.Status_ClearWatchEntityInfo] },

        { MenuEntryCategory.StatusPopout, [MenuEntry.StatusPopout_AlwaysOnTop] },
        
        { MenuEntryCategory.GameHotkeys, [MenuEntry.Game_Start, MenuEntry.Game_Pause, MenuEntry.Game_Restart, MenuEntry.Game_FrameAdvance] },
    };
    
#if DEBUG
    public static void VerifyData() {
        // Ensures that every entry has all the required data
        foreach (var entry in Enum.GetValues<MenuEntry>()) {
            if (!DefaultKeyBindings.ContainsKey(entry)) {
                throw new Exception($"DefaultHotkeys does not contain an entry for '{entry}'");
            }
            if (!EntryNames.ContainsKey(entry)) {
                throw new Exception($"EntryNames does not contain an entry for '{entry}'");
            }
            
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
        MenuEntryCategory.Editor => "Editor - Context Menu",
        MenuEntryCategory.Status => "Status - Context Menu",
        MenuEntryCategory.StatusPopout => "Status Popout - Context Menu",
        MenuEntryCategory.GameHotkeys => "Game Hotkeys",
        _ => throw new UnreachableException(),
    };
    
    public static string GetName(this MenuEntry entry) => EntryNames[entry];
    public static Keys GetDefaultHotkey(this MenuEntry entry) => DefaultKeyBindings[entry];
    public static Keys GetHotkey(this MenuEntry entry) => Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut) ? shortcut : DefaultKeyBindings[entry];
 
    public static CheckMenuItem ToCheckbox(this MenuEntry entry) =>
        new() {
            Text = EntryNames[entry],
            Shortcut = Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut) ? shortcut : DefaultKeyBindings[entry],
        };
    public static MenuItem ToAction(this MenuEntry entry, Action action) =>
        MenuUtils.CreateAction(
            EntryNames[entry],
            Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut) ? shortcut : DefaultKeyBindings[entry],
            action);
    public static MenuItem ToSettingToggle(this MenuEntry entry, string settingName, Action<bool>? onChanged = null) =>
        MenuUtils.CreateSettingToggle(
            EntryNames[entry],
            settingName,
            Settings.Instance.KeyBindings.TryGetValue(entry, out var shortcut) ? shortcut : DefaultKeyBindings[entry],
            onChanged);
}