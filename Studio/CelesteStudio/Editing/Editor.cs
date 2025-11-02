using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Dialog;
using CelesteStudio.Editing.AutoCompletion;
using CelesteStudio.Editing.ContextActions;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication;
using StudioCommunication.Util;

namespace CelesteStudio.Editing;

public sealed class Editor : TextEditor {
    private static readonly Regex UncommentedBreakpointRegex = new(@"^\s*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex CommentedBreakpointRegex = new(@"^\s*#\s*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex AllBreakpointRegex = new(@"^\s*#?\s*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new(@"^\s*#+\s*(\d+:)?\d{1,2}:\d{2}\.\d{3}\(\d+\)", RegexOptions.Compiled);

    private const int MaxRepeatCount = 9_999_999;

    #region Bindings

    private static InstanceActionBinding<Editor> CreateAction(string identifier, string displayName, Hotkey defaultHotkey, Action<Editor> action)
        => new(identifier, displayName, Binding.Category.Editor, defaultHotkey, action);

    private static readonly InstanceBinding SetFrameCountToStepAmount = CreateAction("Editor_SetFrameCountToStepAmount", "Set Frame Count to current Step Amount", Hotkey.None, editor => editor.OnSetFrameCountToStepAmount());

    private static readonly InstanceBinding InsertRemoveBreakpoint = CreateAction("Editor_InsertRemoveBreakpoint", "Insert / Remove Breakpoint", Hotkey.KeyCtrl(Keys.Period), editor => editor.InsertOrRemoveText(UncommentedBreakpointRegex, "***"));
    private static readonly InstanceBinding InsertRemoveSavestateBreakpoint = CreateAction("Editor_InsertRemoveSavestateBreakpoint", "Insert / Remove Savestate Breakpoint", Hotkey.KeyCtrl(Keys.Period | Keys.Shift), editor => editor.InsertOrRemoveText(UncommentedBreakpointRegex, "***S"));
    private static readonly InstanceBinding RemoveAllUncommentedBreakpoints = CreateAction("Editor_RemoveAllUncommentedBreakpoints", "Remove All Uncommented Breakpoints", Hotkey.KeyCtrl(Keys.P), editor => editor.RemoveLinesMatching(UncommentedBreakpointRegex));
    private static readonly InstanceBinding RemoveAllBreakpoints = CreateAction("Editor_RemoveAllBreakpoints", "Remove All Breakpoints", Hotkey.KeyCtrl(Keys.P | Keys.Shift), editor => editor.RemoveLinesMatching(AllBreakpointRegex));
    private static readonly InstanceBinding ToggleCommentBreakpoints = CreateAction("Editor_CommentUncommentAllBreakpoints", "Comment / Uncomment All Breakpoints", Hotkey.KeyCtrl(Keys.P | Application.Instance.AlternateModifier), editor => editor.OnToggleCommentBreakpoints());
    private static readonly InstanceBinding ToggleCommentInputs = CreateAction("Editor_CommentUncommentInputs", "Comment / Uncomment Inputs", Hotkey.KeyCtrl(Keys.K), editor => editor.OnToggleCommentInputs());
    private static readonly InstanceBinding ToggleCommentText = CreateAction("Editor_CommentUncommentText", "Comment / Uncomment Text", Hotkey.KeyCtrl(Keys.K | Keys.Shift), editor => editor.OnToggleCommentText());

    private static readonly InstanceBinding InsertRoomName = CreateAction("Editor_InsertRoomName", "Insert current Room Name", Hotkey.KeyCtrl(Keys.R), editor => editor.InsertLine($"#lvl_{CommunicationWrapper.LevelName.Trim()}"));
    private static readonly InstanceBinding InsertChapterTime = CreateAction("Editor_InsertCurrentChapterTime", "Insert current ChapterTime", Hotkey.KeyCtrl(Keys.T), editor => editor.InsertLine($"#{CommunicationWrapper.ChapterTime}"));
    private static readonly InstanceBinding RemoveAllTimestamps = CreateAction("Editor_RemoveAllTimestamps", "Remove All Timestamps", Hotkey.KeyCtrl(Keys.T | Keys.Shift), editor => editor.RemoveLinesMatching(TimestampRegex));

    private static readonly InstanceBinding InsertPlayerPosition = CreateAction("Editor_InsertCurrentPosition", "Insert current Player Position", Hotkey.None, editor => {
        string xPos = (CommunicationWrapper.PlayerPosition.X + CommunicationWrapper.PlayerPositionRemainder.X).ToFormattedString(CommunicationWrapper.GameSettings.PositionDecimals);
        string yPos = (CommunicationWrapper.PlayerPosition.Y + CommunicationWrapper.PlayerPositionRemainder.Y).ToFormattedString(CommunicationWrapper.GameSettings.PositionDecimals);
        editor.InsertLine($"# Pos: {xPos}, {yPos}");
    });
    private static readonly InstanceBinding InsertPlayerSpeed = CreateAction("Editor_InsertCurrentSpeed", "Insert current Player Speed", Hotkey.None, editor => {
        string xSpeed = CommunicationWrapper.PlayerSpeed.X.ToFormattedString(CommunicationWrapper.GameSettings.SpeedDecimals);
        string ySpeed = CommunicationWrapper.PlayerSpeed.Y.ToFormattedString(CommunicationWrapper.GameSettings.SpeedDecimals);
        editor.InsertLine($"# Speed: {xSpeed}, {ySpeed}");
    });
    private static readonly InstanceBinding InsertModInfo = CreateAction("Editor_InsertModInfo", "Insert Mod Info", Hotkey.None, editor => {
        if (CommunicationWrapper.GetModInfo() is var modInfo && !string.IsNullOrWhiteSpace(modInfo)) {
            editor.InsertLine(modInfo);
        }
    });
    private static readonly InstanceBinding InsertConsoleLoadCommand = CreateAction("Editor_InsertConsoleLoadCommand", "Insert Exact \"console load\" Command", Hotkey.KeyCtrl(Keys.R | Keys.Shift), editor => {
        if (CommunicationWrapper.GetConsoleCommand(simple: false) is var command && !string.IsNullOrWhiteSpace(command)) {
            editor.InsertLine(command);
        }
    });
    private static readonly InstanceBinding InsertSimpleConsoleLoadCommand = CreateAction("Editor_InsertSimpleConsoleLoadCommand", "Insert Simple \"console load\" Command", Hotkey.KeyCtrl(Keys.R | Application.Instance.AlternateModifier), editor => {
        if (CommunicationWrapper.GetConsoleCommand(simple: true) is var command && !string.IsNullOrWhiteSpace(command)) {
            editor.InsertLine(command);
        }
    });

    private static readonly InstanceBinding OpenContextActionsMenu = CreateAction("Editor_OpenContextActionsMenu", "Open Context-Actions Menu...", Hotkey.KeyAlt(Keys.Enter), editor => {
        editor.contextActionsMenu.Refresh();
        editor.Recalc();
    });

    private static readonly ConditionalInstanceActionBinding<Editor> FrameOperationAdd = new("Editor_FrameOperationAdd", "Add", Binding.Category.FrameOperations, Hotkey.Char('+'), editor => editor.OnFrameOperation(CalculationOperator.Add), preferTextHotkey: true);
    private static readonly ConditionalInstanceActionBinding<Editor> FrameOperationSub = new("Editor_FrameOperationSub", "Subtract", Binding.Category.FrameOperations, Hotkey.Char('-'), editor => editor.OnFrameOperation(CalculationOperator.Sub), preferTextHotkey: true);
    private static readonly ConditionalInstanceActionBinding<Editor> FrameOperationMul = new("Editor_FrameOperationMul", "Multiply", Binding.Category.FrameOperations, Hotkey.Char('*'), editor => editor.OnFrameOperation(CalculationOperator.Mul), preferTextHotkey: true);
    private static readonly ConditionalInstanceActionBinding<Editor> FrameOperationDiv = new("Editor_FrameOperationDiv", "Divide", Binding.Category.FrameOperations, Hotkey.Char('/'), editor => editor.OnFrameOperation(CalculationOperator.Div), preferTextHotkey: true);
    private static readonly ConditionalInstanceActionBinding<Editor> FrameOperationSet = new("Editor_FrameOperationSet", "Set", Binding.Category.FrameOperations, Hotkey.Char('='), editor => editor.OnFrameOperation(CalculationOperator.Set), preferTextHotkey: true);

    public static new readonly InstanceBinding[] AllBindings = [
        Cut, Copy, Paste,
        Undo, Redo,
        SelectAll, SelectBlock,
        Find, GoTo, ToggleFolding,
        DeleteSelectedLines, SetFrameCountToStepAmount,
        InsertRemoveBreakpoint, InsertRemoveSavestateBreakpoint, RemoveAllUncommentedBreakpoints, RemoveAllBreakpoints, ToggleCommentBreakpoints, ToggleCommentInputs, ToggleCommentText,
        InsertRoomName, InsertChapterTime, RemoveAllTimestamps,
        InsertPlayerPosition, InsertPlayerSpeed, InsertModInfo, InsertConsoleLoadCommand, InsertSimpleConsoleLoadCommand,
        OpenAutoCompleteMenu, OpenContextActionsMenu,
        FrameOperationAdd, FrameOperationSub, FrameOperationMul, FrameOperationDiv, FrameOperationSet,
    ];

    #endregion

    private readonly ContextActionsMenu contextActionsMenu;

    private SyntaxHighlighter highlighter;

    /// Indicates last modification time, used to check if the user is currently typing
    public DateTime LastModification = DateTime.UtcNow;

    // A toast is a small message box which is temporarily shown in the middle of the screen
    private string toastMessage = string.Empty;
    private CancellationTokenSource? toastTokenSource;

    // Simple math operations like +, -, *, / can be performed on action line's frame counts
    private CalculationState? calculationState = null;

    public Editor(Document document, Scrollable scrollable) : base(document, scrollable) {
        PreDocumentChanged += oldDocument => {
            if (oldDocument != null && Settings.Instance.AutoSave) {
                FormatLines(Enumerable.Range(0, oldDocument.Lines.Count).ToArray());
                FixInvalidInputs();
                oldDocument.Save();
            }
        };
        PostDocumentChanged += newDocument => {
            // Ensure everything is properly formatted
            FormatLines(Enumerable.Range(0, newDocument.Lines.Count).ToArray());

            FixInvalidInputs();
            Recalc();
            ScrollCaretIntoView();

            // Format room labels
            Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
            Settings.Changed += () => {
                Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
            };
        };
        TextChanged += (_, _, _) => {
            LastModification = DateTime.UtcNow;

            Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
            ScrollCaretIntoView();
        };
        FixupText += (_, insertions, _) => {
            FormatLines(insertions.Keys);
            FixInvalidInputs();
        };
        CaretMoved += (_, oldCaret, newCaret) => {
            if (oldCaret.Row != newCaret.Row) {
                FixInvalidInput(newCaret.Row);
            }
        };

        StyleConfig.Initialize(this);
        FileRefactor.Initialize(this);

        autoCompleteMenu = new CommandAutoCompleteMenu(this);
        contextActionsMenu = new ContextActionsMenu(this);

        Focus();

        highlighter = new(FontManager.SKEditorFontRegular, FontManager.SKEditorFontBold, FontManager.SKEditorFontItalic, FontManager.SKEditorFontBoldItalic);
        Settings.FontChanged += () => {
            highlighter = new(FontManager.SKEditorFontRegular, FontManager.SKEditorFontBold, FontManager.SKEditorFontItalic, FontManager.SKEditorFontBoldItalic);
            Recalc();
        };

        CommunicationWrapper.StateUpdated += (prevState, state) => {
            if (prevState.CurrentLine == state.CurrentLine &&
                prevState.SaveStateLines.SequenceEqual(state.SaveStateLines) &&
                prevState.CurrentLineSuffix == state.CurrentLineSuffix) {
                // Nothing to do
                return;
            }

            if (Settings.Instance.SyncCaretWithPlayback && state.CurrentLine != -1) {
                if (Document.Caret.Row != state.CurrentLine) {
                    ClosePopupMenu();
                }

                Document.Caret.Row = state.CurrentLine;
                Document.Caret.Col = DesiredVisualCol = ActionLine.MaxFramesDigits;
                Document.Caret = ClampCaret(Document.Caret);
                Document.Selection.Clear();

                ScrollCaretIntoView(center: true);
            }

            Invalidate();
        };

        // Commands
        var commandsMenu = new SubMenuItem { Text = "Insert Other Command" };

        CommunicationWrapper.CommandsChanged += _ => {
            GenerateCommandMenu();
            Recalc();
        };
        // Update command separator
        Settings.Changed += () => {
            GenerateCommandMenu();
            Recalc();
        };

        GenerateCommandMenu();

        void GenerateCommandMenu() {
            commandsMenu.Items.Clear();

            foreach (string? commandName in CommandInfo.CommandOrder) {
                if (commandName == null && commandsMenu.Items.Count != 0) {
                    commandsMenu.Items.Add(new SeparatorMenuItem());
                } else if (CommunicationWrapper.Commands.FirstOrDefault(cmd => cmd.Name == commandName) is var command && !string.IsNullOrEmpty(command.Name)) {
                    commandsMenu.Items.Add(CreateCommandInsert(command));
                }
            }

            // 3rd party commands (i.e. added through the API by another mod)
            var thirdPartyCommands = CommunicationWrapper.Commands
                .Where(command => !CommandInfo.CommandOrder.Contains(command.Name) && !CommandInfo.HiddenCommands.Contains(command.Name))
                .ToArray();

            if (thirdPartyCommands.Any()) {
                if (commandsMenu.Items.Count != 0) {
                    commandsMenu.Items.Add(new SeparatorMenuItem());
                }
                foreach (var command in thirdPartyCommands) {
                    commandsMenu.Items.Add(CreateCommandInsert(command));
                }
            }

            commandsMenu.Enabled = commandsMenu.Items.Count != 0;
        }

        ContextMenu = CreateMenu();
        Settings.KeyBindingsChanged += () => {
            // WPF doesn't like it when a UIElement has multiple parents, even if the other parent no longer exists
            ContextMenu.Items.Remove(commandsMenu);
            ContextMenu = CreateMenu();
        };

        Recalc();

        ContextMenu CreateMenu() => new() {
            Items = {
                Cut.CreateItem(this),
                Copy.CreateItem(this),
                Paste.CreateItem(this),
                new SeparatorMenuItem(),
                Undo.CreateItem(this),
                Redo.CreateItem(this),
                new SeparatorMenuItem(),
                SelectAll.CreateItem(this),
                SelectBlock.CreateItem(this),
                new SeparatorMenuItem(),
                Find.CreateItem(this),
                GoTo.CreateItem(this),
                ToggleFolding.CreateItem(this),
                new SeparatorMenuItem(),
                DeleteSelectedLines.CreateItem(this),
                SetFrameCountToStepAmount.CreateItem(this),
                new SeparatorMenuItem(),
                InsertRemoveBreakpoint.CreateItem(this),
                InsertRemoveSavestateBreakpoint.CreateItem(this),
                RemoveAllUncommentedBreakpoints.CreateItem(this),
                RemoveAllBreakpoints.CreateItem(this),
                ToggleCommentBreakpoints.CreateItem(this),
                ToggleCommentInputs.CreateItem(this),
                ToggleCommentText.CreateItem(this),
                new SeparatorMenuItem(),
                InsertRoomName.CreateItem(this),
                InsertChapterTime.CreateItem(this),
                RemoveAllTimestamps.CreateItem(this),
                new SeparatorMenuItem(),
                InsertPlayerPosition.CreateItem(this),
                InsertPlayerSpeed.CreateItem(this),
                InsertModInfo.CreateItem(this),
                InsertConsoleLoadCommand.CreateItem(this),
                InsertSimpleConsoleLoadCommand.CreateItem(this),
                commandsMenu,
                new SeparatorMenuItem(),
                OpenAutoCompleteMenu.CreateItem(this),
                OpenContextActionsMenu.CreateItem(this),
            }
        };

        MenuItem CreateCommandInsert(CommandInfo info) {
            var cmd = new Command { Shortcut = Keys.None };
            cmd.Executed += (_, _) => {
                InsertQuickEdit(info.Insert.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText));
                Recalc();
                ScrollCaretIntoView();
            };

            return new ButtonMenuItem(cmd) { Text = info.Name };
        }
    }

    public static readonly TimeSpan DefaultToastTime = TimeSpan.FromSeconds(2);
    public void ShowToastMessage(string message, TimeSpan time) {
        toastMessage = message;
        Invalidate();

        toastTokenSource?.Cancel();
        toastTokenSource?.Dispose();
        toastTokenSource = new CancellationTokenSource();
        Task.Run(() => {
            Task.Delay(time, toastTokenSource.Token).Wait();
            Application.Instance.Invoke(() => {
                toastMessage = string.Empty;
                Invalidate();
            });
        }, toastTokenSource.Token);
    }

    /// Removes invalid inputs, like frameless actions or leading zeros
    public void FixInvalidInputs() {
        using var __ = Document.Update();

        for (int row = 0; row < Document.Lines.Count; row++) {
            FixInvalidInput(row);

            if (Document.Caret.Row == row) {
                Document.Caret.Col = Math.Min(Document.Caret.Col, Document.Lines[row].Length);
            }
        }
    }
    private void FixInvalidInput(int row) {
        using var __ = Document.Update(raiseEvents: false);

        string line = Document.Lines[row];

        // Frameless action lines are only intended for editing and shouldn't be part of the final TAS
        if (ActionLine.TryParse(line, out var actionLine)) {
            actionLine.FrameCount = Math.Clamp(actionLine.FrameCount, 0, ActionLine.MaxFrames);

            Document.ReplaceLine(row, actionLine.ToString());
            return;
        }
        // Repeat command count should be clamped to a valid value
        // Needs to be kept in sync with the CelesteTAS' validation
        if (CommandLine.TryParse(line, out var commandLine) &&
            commandLine.IsCommand("Repeat") &&
            commandLine.Arguments.Length >= 1 &&
            int.TryParse(commandLine.Arguments[0], out int repeatCount)
        ) {
            commandLine.Arguments[0] = Math.Clamp(repeatCount, 1, MaxRepeatCount).ToString();

            Document.ReplaceLine(row, commandLine.ToString());
            return;
        }
    }

    #region General Helper Methods

    protected override void Recalc() {
        base.Recalc();

        // Recalculate line links
        Document.RemoveAnchorsIf(anchor => anchor.UserData is LineLinkAnchorData);
        for (int row = 0; row < Document.Lines.Count; row++) {
            GenerateLineLinks(row);
        }
    }

    /// Ensures that parsable action-line has the correct format
    public bool TryParseAndFormatActionLine(int row, out ActionLine actionLine) {
        using var __ = Document.Update(raiseEvents: false);

        if (ActionLine.TryParse(Document.Lines[row], out actionLine)) {
            Document.ReplaceLine(row, actionLine.ToString());
            return true;
        }
        actionLine = default;
        return false;
    }

    /// Applies the correct formatting to all specified lines
    private void FormatLines(ICollection<int> rows) {
        using var __ = Document.Update(raiseEvents: false);

        string[] lines = Document.Lines.ToArray();
        FileRefactor.FormatLines(lines, rows, StyleConfig.Current.ForceCorrectCommandCasing, StyleConfig.Current.CommandArgumentSeparator);

        foreach (int row in rows) {
            if (row == Document.Caret.Row) {
                continue;
            }

            Document.ReplaceLine(row, lines[row]);
        }
    }

    /// Tweaks frame counts between multiple lines and certain special cases for some commands
    private void AdjustNumericValues(int rowA, int rowB, int dir) {
        int topRow = Math.Min(rowA, rowB);
        int bottomRow = Math.Max(rowA, rowB);

        // Multiline is not supported for commands
        if (topRow == bottomRow && CommandLine.TryParse(Document.Lines[topRow], out var commandLine)) {
            using var __ = Document.Update();

            // Adjust repeat count
            if (commandLine.IsCommand("Repeat") &&
                commandLine.Arguments.Length >= 1 &&
                int.TryParse(commandLine.Arguments[0], out int repeatCount)
            ) {
                commandLine.Arguments[0] = Math.Clamp(repeatCount + dir, 1, MaxRepeatCount).ToString();

                Document.ReplaceLine(topRow, commandLine.ToString());
                return;
            }
        }

        var topLine = ActionLine.Parse(Document.Lines[topRow]);
        var bottomLine = ActionLine.Parse(Document.Lines[bottomRow]);

        if (topLine == null && bottomLine == null || dir == 0) {
            return;
        }

        using (Document.Update()) {
            // Adjust single line
            if (topRow == bottomRow ||
                topLine == null && bottomLine != null ||
                bottomLine == null && topLine != null)
            {
                var line = topLine ?? bottomLine!.Value;
                int row = topLine != null ? topRow : bottomRow;

                Document.ReplaceLine(row, (line with { FrameCount = Math.Clamp(line.FrameCount + dir, 0, ActionLine.MaxFrames) }).ToString());
            }
            // Move frames between lines
            else {
                if (dir > 0 && bottomLine!.Value.FrameCount > 0 && topLine!.Value.FrameCount < ActionLine.MaxFrames) {
                    Document.ReplaceLine(topRow,    (topLine.Value    with { FrameCount = Math.Min(topLine.Value.FrameCount    + 1, ActionLine.MaxFrames)  }).ToString());
                    Document.ReplaceLine(bottomRow, (bottomLine.Value with { FrameCount = Math.Max(bottomLine.Value.FrameCount - 1, 0)                     }).ToString());
                } else if (dir < 0 && bottomLine!.Value.FrameCount < ActionLine.MaxFrames && topLine!.Value.FrameCount > 0) {
                    Document.ReplaceLine(topRow,    (topLine.Value    with { FrameCount = Math.Max(topLine.Value.FrameCount    - 1, 0)                    }).ToString());
                    Document.ReplaceLine(bottomRow, (bottomLine.Value with { FrameCount = Math.Min(bottomLine.Value.FrameCount + 1, ActionLine.MaxFrames) }).ToString());
                }
            }
        }
    }

    #endregion

    protected override void HandleKeyDown(KeyEventArgs e, bool forwardToText) {
        string lineTrimmed = Document.Lines[Document.Caret.Row].TrimStart();

        // Send inputs to Celeste if applicable
        bool isActionLine = lineTrimmed.StartsWith("***") ||
                            ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _ );
        bool isComment = lineTrimmed.StartsWith('#');
        bool isTyping = (DateTime.UtcNow - LastModification).TotalSeconds < Settings.Instance.SendInputsTypingTimeout;
        bool sendInputs =
            (Settings.Instance.SendInputsOnActionLines && isActionLine) ||
            (Settings.Instance.SendInputsOnComments && isComment) ||
            (Settings.Instance.SendInputsOnCommands && !isActionLine && !isComment);

        if (Settings.Instance.SendInputsToCeleste && CommunicationWrapper.Connected && !isTyping && sendInputs) {
            if (CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
                e.Handled = true;
                return;
            }

            // Handle alternative game hotkeys

            // On WPF, the order of events is
            // - OnKeyDown(Equal, char.MaxValue)
            // - OnTextInput(+)
            // - OnKeyDown(None, +)
            // So since we handle the hotkey in OnTextInput`, we don't want to handle it again here.
            //
            // On GTK we get
            // - OnKeyDown(Equal, +) or OnKeyDown(None, ^)
            // - OnTextInput(+)
            // So again char events are just handled in OnTextInput.
            if (e.Key != Keys.None) {
                var hotkey = Hotkey.FromEvent(e);
                foreach (var binding in CommunicationWrapper.AllBindings) {
                    foreach (var entry in binding.Entries) {
                        if (Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey) == hotkey && entry.Action()) {
                            Recalc();
                            ScrollCaretIntoView();

                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
        }

        // Prevent editing file on accident
        if (Settings.Instance.SendInputsToCeleste && CommunicationWrapper.PlaybackRunning && Settings.Instance.SendInputsDisableWhileRunning) {
            e.Handled = true;
            return;
        }

        base.HandleKeyDown(e, forwardToText: false);
        if (e.Handled) {
            return;
        }

        if (calculationState != null) {
            CalculationHandleKey(e);
            Invalidate();
            return;
        }

        switch (e.Key) {
            // Adjust frame count
            case Keys.Up when e.HasCommonModifier() && e.Shift:
                if (Document.Selection.Empty) {
                    AdjustNumericValues(Document.Caret.Row, Document.Caret.Row, 1);
                } else {
                    AdjustNumericValues(Document.Selection.Start.Row, Document.Selection.End.Row, 1);
                }

                e.Handled = true;
                break;
            case Keys.Down when e.HasCommonModifier() && e.Shift:
                if (Document.Selection.Empty) {
                    AdjustNumericValues(Document.Caret.Row, Document.Caret.Row, -1);
                } else {
                    AdjustNumericValues(Document.Selection.Start.Row, Document.Selection.End.Row, -1);
                }

                e.Handled = true;
                break;

            // Allow zoom in/out
            case Keys.Equal when e.HasCommonModifier():
                AdjustZoom(+0.1f);
                break;
            case Keys.Minus when e.HasCommonModifier():
                AdjustZoom(-0.1f);
                break;

            case Keys.C when e.HasCommonModifier() && e.HasAlternateModifier():
                Clipboard.Instance.Clear();
                Clipboard.Instance.Text = Document.FilePath;
                ShowToastMessage("Copied current file path to Clipboard", DefaultToastTime);

                e.Handled = true;
                break;
            // Use Ctrl+/ as an alternative for Ctrl+K
            case Keys.Slash when e.HasCommonModifier():
                OnToggleCommentInputs();
                e.Handled = true;
                break;
            case Keys.Slash when e.Shift && e.HasCommonModifier():
                OnToggleCommentText();
                e.Handled = true;
                break;
            case Keys.F2:
            {
                // Rename label
                string line = Document.Lines[Document.Caret.Row];
                if (CommentLine.IsLabel(line)) {
                    string oldLabel = line["#".Length..];
                    string newLabel = RenameLabelDialog.Show(oldLabel);

                    FileRefactor.RefactorLabelName(Document.FilePath, oldLabel, newLabel);

                    using var __ = Document.Update();
                    Document.ReplaceLine(Document.Caret.Row, $"#{newLabel}");
                }
                e.Handled = true;
                break;
            }
            default:
                if (forwardToText) {
                    // macOS will make a beep sounds when the event isn't handled
                    // ..that also means OnTextInput won't be called..
                    if (Eto.Platform.Instance.IsMac) {
                        e.Handled = true;
                        if (e.KeyChar != char.MaxValue) {
                            OnTextInput(new TextInputEventArgs(e.KeyChar.ToString()));
                        }
                    } else {
                        BaseOnKeyDown(e);
                    }
                }
                break;
        }

        // If nothing handled this, and it's not a character, send it anyway
        if (Settings.Instance.SendInputsToCeleste && Settings.Instance.SendInputsNonWritable && !Platform.IsWpf && CommunicationWrapper.Connected && !isTyping && !sendInputs && !e.Handled && e.KeyChar == char.MaxValue && CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
            e.Handled = true;
            return;
        }

        if (e.Handled) {
            Recalc();
        }
    }

    protected override bool CheckHotkey(Hotkey hotkey) {
        // Handle global bindings
        foreach (var binding in Studio.GetAllStudioBindings()) {
            foreach (var entry in binding.Entries) {
                if (Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey) == hotkey && entry.Action()) {
                    Recalc();
                    ScrollCaretIntoView();

                    return true;
                }
            }
        }
        // Handle editor bindings
        foreach (var binding in AllBindings) {
            foreach (var entry in binding.InstanceEntries) {
                if (Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey) == hotkey && entry.Action(this)) {
                    Recalc();
                    ScrollCaretIntoView();

                    return true;
                }
            }
        }

        // Handle snippets
        foreach (var snippet in Settings.Instance.Snippets) {
            if (snippet.Enabled && snippet.Hotkey == hotkey) {
                InsertQuickEdit(snippet.Insert);
                Recalc();
                ScrollCaretIntoView();

                return true;
            }
        }

        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        var mods = e.Modifiers;
        if (e.Key is Keys.LeftShift or Keys.RightShift) mods &= ~Keys.Shift;
        if (e.Key is Keys.LeftControl or Keys.RightControl) mods &= ~Keys.Control;
        if (e.Key is Keys.LeftAlt or Keys.RightAlt) mods &= ~Keys.Alt;
        if (e.Key is Keys.LeftApplication or Keys.RightApplication) mods &= ~Keys.Application;
        UpdateMouseCursor(PointFromScreen(Mouse.Position), mods);

        string lineTrimmed = Document.Lines[Document.Caret.Row].TrimStart();

        // Send inputs to Celeste if applicable
        bool isActionLine = lineTrimmed.StartsWith("***") ||
                            ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _ );
        bool isComment = lineTrimmed.StartsWith('#');
        bool isTyping = (DateTime.UtcNow - LastModification).TotalSeconds < Settings.Instance.SendInputsTypingTimeout;
        bool sendInputs =
            (Settings.Instance.SendInputsOnActionLines && isActionLine) ||
            (Settings.Instance.SendInputsOnComments && isComment) ||
            (Settings.Instance.SendInputsOnCommands && !isActionLine && !isComment) ||
            (Settings.Instance.SendInputsNonWritable && !Platform.IsWpf && e.KeyChar == ushort.MaxValue);

        if (Settings.Instance.SendInputsToCeleste && CommunicationWrapper.Connected && !isTyping && sendInputs && CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: true)) {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    // Support collapsing sections with '##' comments
    protected override bool GetFoldingHeaderDepth(string trimmed, out int depth) {
        if (!trimmed.StartsWith("##")) {
            depth = -1;
            return false;
        }

        depth = 0;
        for (int i = 2; i < trimmed.Length; i++) {
            if (trimmed[i] == '#') {
                depth++;
                continue;
            }
            break;
        }

        return true;
    }
    protected override int GetFoldingHeaderText(string line) {
        // Find begging of text
        int startIdx = 0;
        for (; startIdx < line.Length; startIdx++) {
            char c = line[startIdx];
            if (c != '#' && !char.IsWhiteSpace(c)) {
                break;
            }
        }
        return startIdx;
    }

    // Support wrapping long comments across multiple lines
    protected override bool ShouldWrapLine(string line, out int textStartIdx) {
        if (!Settings.Instance.WordWrapComments || !line.StartsWith("#")) {
            textStartIdx = -1;
            return false;
        }

        for (int idx = 0; idx < line.Length; idx++) {
            char c = line[idx];
            if (c != '#' && !char.IsWhiteSpace(c)) {
                textStartIdx = idx;
                return true;
            }
        }

        textStartIdx = line.Length;
        return true;
    }

    #region Action Line Calculation

    private void StartCalculation(CalculationOperator op) {
        calculationState = new CalculationState(op, Document.Caret.Row);
    }

    private void CalculationHandleKey(KeyEventArgs e) {
        if (calculationState == null) {
            return;
        }

        switch (e.Key) {
            case Keys.Escape:
                calculationState = null;
                e.Handled = true;
                return;
            case Keys.Enter:
                CommitCalculation();
                calculationState = null;
                e.Handled = true;
                return;
            case Keys.Backspace:
                calculationState.Operand = calculationState.Operand[..Math.Max(0, calculationState.Operand.Length - 1)];
                e.Handled = true;
                return;
            case Keys.Down:
                CommitCalculation(stealFrom: 1);
                calculationState = null;
                e.Handled = true;
                return;
            case Keys.Up:
                CommitCalculation(stealFrom: -1);
                calculationState = null;
                e.Handled = true;
                return;
            case >= Keys.D0 and <= Keys.D9 when !e.Shift: {
                int num = e.Key - Keys.D0;
                calculationState.Operand += num;
                e.Handled = true;
                return;
            }
            // Allow A-Z to be handled by the action line editing
            case >= Keys.A and <= Keys.Z: {
                calculationState = null;
                e.Handled = false;
                return;
            }
        }

        e.Handled = false;
    }
    private bool CalculationHandleText(char c) {
        if (calculationState == null) {
            return false;
        }

        if (c is >= '0' and <= '9') {
            int num = c - '0';
            calculationState.Operand += num;
            return true;
        }

        // Allow A-Z to be handled by the action line editing
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') {
            calculationState = null;
        }

        return false;
    }

    private void CommitCalculation(int stealFrom = 0) {
        if (calculationState == null ||
            calculationState.Operand.Length == 0 ||
            !int.TryParse(calculationState.Operand, out int operand) ||
            !TryParseAndFormatActionLine(calculationState.Row, out var actionLine))
        {
            return;
        }

        using var __ = Document.Update();

        var newActionLine = calculationState.Operator.Apply(actionLine, operand);
        Document.ReplaceLine(calculationState.Row, newActionLine.ToString());

        if (stealFrom != 0) {
            for (int stealFromRow = calculationState.Row + stealFrom; stealFromRow >= 0 && stealFromRow < Document.Lines.Count; stealFromRow += stealFrom) {
                if (!ActionLine.TryParse(Document.Lines[stealFromRow], out var stealFromActionLine)) {
                    continue;
                }

                int frameDelta = newActionLine.FrameCount - actionLine.FrameCount;

                Document.ReplaceLine(stealFromRow, (stealFromActionLine with {
                    FrameCount = Math.Clamp(stealFromActionLine.FrameCount - frameDelta, 0, ActionLine.MaxFrames)
                }).ToString());
                break;
            }
        }
    }

    #endregion

    #region Line Links

    public enum LineLinkType { None, OpenReadFile, GoToPlayLine }
    public record struct LineLinkAnchorData {
        public required LineLinkType Type;
        public required Action OnUse;
    }

    private void GenerateLineLinks(int row) {
        GenerateOpenReadFileLink(row);
        GenerateGotoPlayLineLink(row);
    }

    /// Link to open the Read-command on the line
    private void GenerateOpenReadFileLink(int row) {
        if (!CommandLine.TryParse(Document.Lines[row], out var commandLine) ||
            !commandLine.IsCommand("Read") || commandLine.Arguments.Length < 1)
        {
            return;
        }

        var documentPath = Studio.Instance.Editor.Document.FilePath;
        if (documentPath == Document.ScratchFile) {
            return;
        }
        if (Path.GetDirectoryName(documentPath) is not { } documentDir) {
            return;
        }

        var fullPath = Path.Combine(documentDir, $"{commandLine.Arguments[0]}.tas");
        if (!File.Exists(fullPath)) {
            return;
        }

        var lines = File.ReadAllText(fullPath)
            .ReplaceLineEndings(Document.NewLine.ToString())
            .SplitDocumentLines()
            .Select((line, i) => (line, i))
            .ToArray();

        int? startLabelRow = null;
        if (commandLine.Arguments.Length > 1) {
            (var label, startLabelRow) = lines
                .FirstOrDefault(pair => pair.line == $"#{commandLine.Arguments[1]}");
            if (label == null) {
                return;
            }
        }
        int? endLabelRow = null;
        if (commandLine.Arguments.Length > 2) {
            (var label, endLabelRow) = lines
                .FirstOrDefault(pair => pair.line == $"#{commandLine.Arguments[2]}");
            if (label == null) {
                return;
            }
        }

        int startLinkLength = commandLine.Command.Length
            + commandLine.ArgumentSeparator.Length
            + commandLine.Arguments[0].Length
            - 1;
        if (commandLine.Arguments.Length > 1) {
            startLinkLength += commandLine.ArgumentSeparator.Length
                          +  commandLine.Arguments[1].Length;
        }

        Document.AddAnchor(new Anchor {
            Row = row,
            MinCol = 0,
            MaxCol = startLinkLength,
            UserData = new LineLinkAnchorData {
                Type = LineLinkType.OpenReadFile,
                OnUse = () => OpenFile(startLabelRow),
            }
        });

        if (endLabelRow.HasValue) {
            Document.AddAnchor(new Anchor {
                Row = row,
                MinCol = startLinkLength + commandLine.ArgumentSeparator.Length + 1,
                MaxCol = startLinkLength + commandLine.ArgumentSeparator.Length + commandLine.Arguments[2].Length,
                UserData = new LineLinkAnchorData {
                    Type = LineLinkType.None,
                    OnUse = () => OpenFile(endLabelRow),
                }
            });
        }

        return;

        void OpenFile(int? targetRow) {
            Studio.Instance.OpenFileInEditor(fullPath);
            if (targetRow is {} caretRow) {
                Document.Caret.Row = caretRow;
                Document.Caret.Col = DesiredVisualCol = Document.Lines[caretRow].Length;
            } else {
                Document.Caret = new CaretPosition(0, 0);
            }
            Recalc();
            ScrollCaretIntoView(center: true);
        }
    }

    /// Link to goto the Play-command's target line
    private void GenerateGotoPlayLineLink(int row) {
        if (!CommandLine.TryParse(Document.Lines[row], out var commandLine) ||
            !commandLine.IsCommand("Play") || commandLine.Arguments.Length < 1)
        {
            return;
        }

        (var label, int labelRow) = Document.Lines
            .Select((line, i) => (line, i))
            .FirstOrDefault(pair => pair.line == $"#{commandLine.Arguments[0]}");
        if (label == null) {
            return;
        }

        Document.AddAnchor(new Anchor {
            Row = row,
            MinCol = 0,
            MaxCol = Document.Lines[row].Length - 1,
            UserData = new LineLinkAnchorData {
                Type = LineLinkType.GoToPlayLine,
                OnUse = () => {
                    Document.Caret.Row = labelRow;
                    Document.Caret.Col = DesiredVisualCol = Document.Lines[labelRow].Length;
                    Recalc();
                    ScrollCaretIntoView(center: true);
                },
            }
        });
    }

    private Anchor? LocationToLineLink(PointF location) {
        if (location.X < scrollablePosition.X + textOffsetX ||
            location.X > scrollablePosition.X + scrollableSize.Width)
        {
            return null;
        }

        var (position, _) = LocationToCaretPosition(location);
        position = ClampCaret(position);
        return Document.FindFirstAnchor(anchor => anchor.IsPositionInside(position) && anchor.UserData is LineLinkAnchorData);
    }

    #endregion

    #region Editing Actions

    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0])) {
            return;
        }

        if (e.Text.Length == 1 && CheckHotkey(Hotkey.Char(e.Text[0]))) {
            return;
        }

        if (calculationState != null && CalculationHandleText(e.Text[0])) {
            Invalidate();
            return;
        }

        string line;
        ActionLine actionLine;
        int leadingSpaces;

        using var __ = Document.Update();

        Document.Caret = ClampCaret(Document.Caret);

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();

            // Account for frame count not moving
            line = Document.Lines[Document.Caret.Row];
            leadingSpaces = line.Length - line.TrimStart().Length;
            if (ActionLine.TryParse(line, out actionLine)) {
                int frameDigits = actionLine.Frames.Length;
                Document.Caret.Col += ActionLine.MaxFramesDigits - (leadingSpaces + frameDigits);
            }
        } else {
            line = Document.Lines[Document.Caret.Row];
        }

        char typedCharacter = char.ToUpper(e.Text[0]);
        var oldCaret = Document.Caret;

        // Create breakpoints
        if (typedCharacter == '*' && !line.TrimStart().StartsWith('#') && !FastForwardLine.TryParse(line, out _)) {
            if (string.IsNullOrWhiteSpace(line)) {
                Document.ReplaceLine(Document.Caret.Row, "***");
                Document.Caret.Col = "***".Length;
            } else {
                InsertLine("***");
            }
        }
        // Manually handle action line
        else if (TryParseAndFormatActionLine(Document.Caret.Row, out actionLine) && e.Text.Length == 1) {
            ClearQuickEdits();

            line = Document.Lines[Document.Caret.Row];
            leadingSpaces = line.Length - line.TrimStart().Length;

            // Handle custom bindings
            int customBindStart = GetColumnOfAction(actionLine, Actions.PressedKey);
            int customBindEnd = customBindStart + actionLine.CustomBindings.Count;
            if (customBindStart != -1 && Document.Caret.Col >= customBindStart && Document.Caret.Col <= customBindEnd && typedCharacter is >= 'A' and <= 'Z') {
                bool alreadyExists = !actionLine.CustomBindings.Add(typedCharacter);
                if (alreadyExists) {
                    actionLine.CustomBindings.Remove(typedCharacter);
                    Document.Caret.Col = customBindEnd - 1;
                } else {
                    Document.Caret.Col = customBindEnd + 1;
                }

                // Skip regular logic
                Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());
                goto FinishEdit;
            }

            var typedAction = typedCharacter.ActionForChar();

            // Handle feather inputs
            int featherStart = GetColumnOfAction(actionLine, Actions.Feather);
            if (featherStart != -1 && Document.Caret.Col > featherStart && (typedCharacter is '.' or ',' or (>= '0' and <= '9'))) {
                int newCol;
                if (typedCharacter == '.' && Document.Caret.Col > 0 && line[Document.Caret.Col - 1] == ActionLine.Delimiter) {
                    // Auto-insert the leading 0
                    line = line.Insert(Document.Caret.Col, "0.");
                    newCol = Document.Caret.Col + 2;
                } else {
                    line = line.Insert(Document.Caret.Col, typedCharacter.ToString());
                    newCol = Document.Caret.Col + 1;
                }

                if (ActionLine.TryParse(line, out var newActionLine, ignoreInvalidFloats: false)) {
                    actionLine = newActionLine;
                    Document.Caret.Col = newCol;
                }
            }
            // Handle dash-only/move-only/custom bindings
            else if (typedAction is Actions.DashOnly or Actions.MoveOnly or Actions.PressedKey) {
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction, Settings.Instance.AutoRemoveMutuallyExclusiveActions);

                if (actionLine.Actions.HasFlag(typedAction)) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, typedAction);
                } else {
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                }
            }
            // Handle regular inputs
            else if (typedAction != Actions.None) {
                int dashOnlyStart = GetColumnOfAction(actionLine, Actions.DashOnly);
                int dashOnlyEnd = dashOnlyStart + actionLine.Actions.GetDashOnly().Count();
                if (dashOnlyStart != -1 && Document.Caret.Col >= dashOnlyStart && Document.Caret.Col <= dashOnlyEnd)
                    typedAction = typedAction.ToDashOnlyActions();

                int moveOnlyStart = GetColumnOfAction(actionLine, Actions.MoveOnly);
                int moveOnlyEnd = moveOnlyStart + actionLine.Actions.GetMoveOnly().Count();
                if (moveOnlyStart != -1 && Document.Caret.Col >= moveOnlyStart && Document.Caret.Col <= moveOnlyEnd)
                    typedAction = typedAction.ToMoveOnlyActions();

                // Toggle it
                actionLine.Actions = actionLine.Actions.ToggleAction(typedAction, Settings.Instance.AutoRemoveMutuallyExclusiveActions);

                // Warp the cursor after the number
                if (typedAction == Actions.Feather && actionLine.Actions.HasFlag(Actions.Feather)) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.Feather) + 1;
                } else if (typedAction == Actions.Feather && !actionLine.Actions.HasFlag(Actions.Feather)) {
                    actionLine.FeatherAngle = null;
                    actionLine.FeatherMagnitude = null;
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                } else if (typedAction is Actions.LeftDashOnly or Actions.RightDashOnly or Actions.UpDashOnly or Actions.DownDashOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.DashOnly) + actionLine.Actions.GetDashOnly().Count();
                } else if (typedAction is Actions.LeftMoveOnly or Actions.RightMoveOnly or Actions.UpMoveOnly or Actions.DownMoveOnly) {
                    Document.Caret.Col = GetColumnOfAction(actionLine, Actions.MoveOnly) + actionLine.Actions.GetMoveOnly().Count();
                } else {
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                }
            }
            // If the key we entered is a number
            else if (typedCharacter is >= '0' and <= '9' && Document.Caret.Col <= ActionLine.MaxFramesDigits) {
                int caretIndex = Math.Clamp(Document.Caret.Col - leadingSpaces, 0, actionLine.Frames.Length);

                // Jam the number into the current position
                string framesLeft = actionLine.Frames[..caretIndex];
                string framesRight = actionLine.Frames[caretIndex..];
                actionLine.Frames = $"{framesLeft}{typedCharacter}{framesRight}";

                // Cap at max frames
                if (actionLine.FrameCount > ActionLine.MaxFrames) {
                    actionLine.FrameCount = ActionLine.MaxFrames;
                    Document.Caret.Col = ActionLine.MaxFramesDigits;
                }
                // Cap at max frame length
                else if (actionLine.Frames.Length > ActionLine.MaxFramesDigits) {
                    actionLine.Frames = Math.Clamp(actionLine.FrameCount, 0, ActionLine.MaxFrames).ToString().PadLeft(ActionLine.MaxFramesDigits, '0');
                }
            }

            // Allow commenting out the line
            if (typedCharacter == '#' && Document.Caret.Col <= leadingSpaces) {
                Document.ReplaceLine(Document.Caret.Row, $"#{actionLine}");
            } else {
                Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());
            }

            FinishEdit:
            Document.Caret = ClampCaret(Document.Caret);
        }
        // Manually handle breakpoints
        else if (FastForwardLine.TryParse(Document.Lines[Document.Caret.Row], out var fastForward)) {
            if (typedCharacter == '!') {
                fastForward.ForceStop = !fastForward.ForceStop;
                if (fastForward.ForceStop) {
                    Document.Caret.Col++;
                } else {
                    Document.Caret.Col--;
                }

                Document.ReplaceLine(Document.Caret.Row, fastForward.ToString());
            } else if (char.ToUpper(typedCharacter) == 'S') {
                fastForward.SaveState = !fastForward.SaveState;
                if (fastForward.SaveState) {
                    Document.Caret.Col++;
                } else {
                    Document.Caret.Col--;
                }

                Document.ReplaceLine(Document.Caret.Row, fastForward.ToString());
            } else if (typedCharacter is >= '0' and <= '9' or '.') {
                Document.Insert(e.Text);
            }
        }
        // Just write it as text
        else {
            // Encourage having a space before comments (so they aren't labels)
            // However still allow easily inserting multiple #'s
            if (e.Text == "#") {
                var currLine = Document.Lines[Document.Caret.Row];
                bool onlyComment = currLine.All(c => char.IsWhiteSpace(c) || c == '#');

                if (onlyComment) {
                    var newLine = $"{currLine.TrimEnd()}# ";
                    Document.ReplaceLine(Document.Caret.Row, newLine);
                    Document.Caret.Col = newLine.Length;
                } else {
                    Document.Insert("#");
                }
            } else {
                Document.Insert(e.Text);
            }

            // But turn it into an action line if possible
            if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out var newActionLine)) {
                ClearQuickEdits();

                Document.ReplaceLine(Document.Caret.Row, newActionLine.ToString());
                Document.Caret.Col = ActionLine.MaxFramesDigits;
            }
        }

        if (oldCaret.Row != Document.Caret.Row) {
            FixInvalidInput(oldCaret.Row);
        }

        DesiredVisualCol = Document.Caret.Col;
        autoCompleteMenu!.Refresh();
    }

    protected override void OnDelete(CaretMovementType direction) {
        using var __ = Document.Update();

        // To be reused, because C# is stupid
        string line;
        ActionLine actionLine;

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();

            // Account for frame count not moving
            line = Document.Lines[Document.Caret.Row];
            if (ActionLine.TryParse(line, out actionLine)) {
                int leadingSpaces = line.Length - line.TrimStart().Length;
                int frameDigits = actionLine.Frames.Length;

                Document.Caret.Col += ActionLine.MaxFramesDigits - (leadingSpaces + frameDigits);
            }
            return;
        }

        var caret = Document.Caret;
        line = Document.Lines[Document.Caret.Row];

        if (TryParseAndFormatActionLine(Document.Caret.Row, out actionLine)) {
            caret.Col = SnapColumnToActionLine(actionLine, caret.Col);

            // Handle frame count
            if (caret.Col == ActionLine.MaxFramesDigits && direction is CaretMovementType.WordLeft or CaretMovementType.CharLeft ||
                caret.Col < ActionLine.MaxFramesDigits
            ) {
                line = actionLine.ToString();
                int leadingSpaces = line.Length - line.TrimStart().Length;
                int caretIndex = Math.Clamp(caret.Col - leadingSpaces, 0, actionLine.Frames.Length);

                string framesLeft = actionLine.Frames[..caretIndex];
                string framesRight = actionLine.Frames[caretIndex..];

                if (actionLine.Frames.Length == 0) {
                    // Fully delete the line if it's frameless
                    line = string.Empty;
                } else if (framesLeft.Length == 0 && direction is CaretMovementType.WordLeft or CaretMovementType.CharLeft) {
                    // Delete empty line above
                    if (Document.Caret.Row > 0 && string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row - 1])) {
                        Document.RemoveLine(Document.Caret.Row - 1);
                        Document.Caret.Row--;
                        DesiredVisualCol = Document.Caret.Col;
                        return;
                    }
                } else {
                    if (direction == CaretMovementType.WordLeft) {
                        actionLine.Frames = framesRight;
                        caretIndex = 0;
                    } else if (direction == CaretMovementType.WordRight) {
                        actionLine.Frames = framesLeft;
                    } else if (direction == CaretMovementType.CharLeft) {
                        actionLine.Frames = $"{framesLeft[..^1]}{framesRight}";
                        caretIndex--;
                    } else if (direction == CaretMovementType.CharRight) {
                        actionLine.Frames = $"{framesLeft}{framesRight[1..]}";
                    }

                    line = actionLine.ToString();
                    caret.Col = ActionLine.MaxFramesDigits - actionLine.Frames.Length + caretIndex;
                }

                goto FinishDeletion; // Skip regular deletion behaviour
            }

            // Handle feather angle/magnitude
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            if (featherColumn != -1 && caret.Col >= featherColumn) {
                int angleMagnitudeCommaColumn = line.IndexOf(ActionLine.Delimiter, featherColumn + 1) + 1;

                if (caret.Col == featherColumn + 1 && direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft) {
                    var actions = GetActionsFromColumn(actionLine, caret.Col, direction);
                    actionLine.Actions &= ~actions;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                } else if (caret.Col == featherColumn && direction is CaretMovementType.CharRight or CaretMovementType.WordRight ||
                           caret.Col == angleMagnitudeCommaColumn && direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft && angleMagnitudeCommaColumn != line.Length)
                {
                    // delete the angle and replace it with the magnitude
                    actionLine.FeatherAngle = actionLine.FeatherMagnitude;
                    actionLine.FeatherMagnitude = null;
                    caret.Col = featherColumn;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                } else if (caret.Col == angleMagnitudeCommaColumn - 1 &&
                           direction is CaretMovementType.CharRight or CaretMovementType.WordRight)
                {
                    actionLine.FeatherMagnitude = null;
                    line = actionLine.ToString();
                    goto FinishDeletion;
                }
            }

            // Remove blank lines with delete at the end of a line
            if (caret.Col == line.Length &&
                caret.Row < Document.Lines.Count - 1 &&
                string.IsNullOrWhiteSpace(Document.Lines[caret.Row + 1]))
            {
                if (direction == CaretMovementType.CharRight) {
                    Document.RemoveLine(caret.Row + 1);
                    goto FinishDeletion;
                } else if (direction == CaretMovementType.WordRight) {
                    while (caret.Row < Document.Lines.Count - 1 && string.IsNullOrWhiteSpace(Document.Lines[caret.Row + 1])) {
                        Document.RemoveLine(caret.Row + 1);
                    }
                    goto FinishDeletion;
                }
            }

            int newColumn = direction switch {
                CaretMovementType.CharLeft => GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < caret.Col, caret.Col),
                CaretMovementType.WordLeft => GetHardSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < caret.Col, caret.Col),
                CaretMovementType.CharRight => GetSoftSnapColumns(actionLine).FirstOrDefault(c => c > caret.Col, caret.Col),
                CaretMovementType.WordRight => GetHardSnapColumns(actionLine).FirstOrDefault(c => c > caret.Col, caret.Col),
                _ => caret.Col,
            };

            line = line.Remove(Math.Min(newColumn, caret.Col), Math.Abs(newColumn - caret.Col));
            caret.Col = Math.Min(newColumn, caret.Col);

            FinishDeletion:
            if (ActionLine.TryParse(line, out var newActionLine)) {
                line = newActionLine.ToString();
            } else if (string.IsNullOrWhiteSpace(line)) {
                line = string.Empty;
                caret.Col = 0;
            }

            Document.ReplaceLine(caret.Row, line);
            Document.Caret = ClampCaret(caret);
        } else if (FastForwardLine.TryParse(line, out _) &&
                   SnapColumnToFastForward(Document.Caret.Col) is var snappedCol &&
                       (snappedCol == 0            && direction is CaretMovementType.CharRight or CaretMovementType.WordRight ||
                        snappedCol == "***".Length && direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft)
        ) {
            // Remove breakpoint
            Document.RemoveLine(Document.Caret.Row);
        } else {
            Document.Caret = GetNewTextCaretPosition(direction);

            if (caret.Row == Document.Caret.Row) {
                Document.RemoveRangeInLine(caret.Row, caret.Col, Document.Caret.Col);
                Document.Caret.Col = Math.Min(Document.Caret.Col, caret.Col);
                Document.Caret = ClampCaret(Document.Caret, wrapLine: true);

                autoCompleteMenu!.Refresh(open: false);
            } else {
                var min = Document.Caret < caret ? Document.Caret : caret;
                var max = Document.Caret < caret ? caret : Document.Caret;

                RemoveRange(min, max);
                Document.Caret = min;

                // Ensure new line is correctly formatted
                Document.ReplaceLine(Document.Caret.Row, FileRefactor.FormatLine(Document.Lines[Document.Caret.Row], StyleConfig.Current.ForceCorrectCommandCasing, StyleConfig.Current.CommandArgumentSeparator));

                ClosePopupMenu();
            }
        }

        DesiredVisualCol = Document.Caret.Col;
    }

    protected override void OnEnter(bool splitLines, bool up) {
        using var __ = Document.Update();

        string line = Document.Lines[Document.Caret.Row];
        string lineTrimmedStart = line.TrimStart();
        int leadingSpaces = line.Length - lineTrimmedStart.Length;

        // Auto-split on first and last column since nothing is broken there
        bool autoSplit = Document.Caret.Col <= leadingSpaces || Document.Caret.Col == line.Length;

        int offset = up ? 0 : 1;
        if (autoSplit || splitLines && !ActionLine.TryParse(line, out _)) {
            if (!Document.Selection.Empty) {
                RemoveRange(Document.Selection.Min, Document.Selection.Max);
                Document.Caret = Document.Selection.Min;
                Document.Selection.Clear();

                line = Document.Lines[Document.Caret.Row];
                lineTrimmedStart = line.TrimStart();
                leadingSpaces = line.Length - lineTrimmedStart.Length;
            } else if (line.Trim() == "#") {
                // Replace empty comment
                Document.ReplaceLine(Document.Caret.Row, string.Empty);
                Document.Caret.Col = DesiredVisualCol = 0;
                return;
            }

            // Auto-insert # for multiline comments (not labels, not folds!)
            // Additionally don't auto-multiline when caret is before #
            if (Settings.Instance.AutoMultilineComments && Document.Caret.Col > leadingSpaces && lineTrimmedStart.StartsWith("# ")) {
                const string prefix = "# ";

                Document.Caret.Col = Math.Max(Document.Caret.Col, prefix.Length);

                string beforeCaret = line[(prefix.Length + leadingSpaces)..Document.Caret.Col];
                string afterCaret = line[Document.Caret.Col..];

                int newRow = Document.Caret.Row + offset;

                Document.ReplaceLine(Document.Caret.Row, prefix + (up ? afterCaret : beforeCaret));
                Document.InsertLine(newRow, prefix + (up ? beforeCaret : afterCaret));
                Document.Caret.Row = newRow;
                Document.Caret.Col = DesiredVisualCol = prefix.Length + (up ? beforeCaret.Length : 0);
            } else {
                Document.Insert($"{Document.NewLine}");
            }
        } else {
            int newRow = Document.Caret.Row + offset;
            if (GetCollapse(Document.Caret.Row) is { } collapse) {
                newRow = (up ? collapse.MinRow : collapse.MaxRow) + offset;
            }

            // Auto-insert # for multiline comments (not labels, not folds!)
            string prefix = Settings.Instance.AutoMultilineComments && line.StartsWith("# ") ? "# " : "";

            Document.InsertLine(newRow, prefix);
            Document.Caret.Row = newRow;
            Document.Caret.Col = DesiredVisualCol = Math.Max(Document.Caret.Col, prefix.Length);
        }

        Document.Selection.Clear();
    }

    protected override void OnPaste() {
        if (!Clipboard.Instance.ContainsText) {
            return;
        }

        using var __ = Document.Update();

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }

        string line = Document.Lines[Document.Caret.Row];
        string clipboardText = Clipboard.Instance.Text.ReplaceLineEndings(Document.NewLine.ToString());

        // Prevent splitting the action-line in half or inserting garbage into the middle
        if (ActionLine.TryParse(line, out _)) {
            // Trim leading / trailing blank lines
            string[] insertLines = clipboardText.Trim(Document.NewLine).SplitDocumentLines();

            // Insert into the action-line if it stays valid
            if (insertLines.Length == 1) {
                Document.Insert(insertLines[0]);

                if (ActionLine.TryParseStrict(Document.Lines[Document.Caret.Row], out var actionLine)) {
                    // Still valid

                    // Cap at max frames
                    if (actionLine.FrameCount > ActionLine.MaxFrames) {
                        actionLine.FrameCount = ActionLine.MaxFrames;
                        Document.Caret.Col = ActionLine.MaxFramesDigits;
                    }
                    // Cap at max frame length
                    else if (actionLine.Frames.Length > ActionLine.MaxFramesDigits) {
                        actionLine.Frames = Math.Clamp(actionLine.FrameCount, 0, ActionLine.MaxFrames).ToString().PadLeft(ActionLine.MaxFramesDigits, '0');
                    }

                    // Account for frame count not moving
                    string newLine = Document.Lines[Document.Caret.Row];
                    int leadingSpaces = newLine.Length - newLine.TrimStart().Length;
                    int frameDigits = actionLine.Frames.Length;
                    Document.Caret.Col += ActionLine.MaxFramesDigits - (leadingSpaces + frameDigits);

                    Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());

                    return;
                }

                // Revert
                Document.ReplaceLine(Document.Caret.Row, line);
            }

            // Otherwise insert below
            Document.InsertLines(Document.Caret.Row + 1, insertLines);
            Document.Caret.Row += insertLines.Length;
            Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
        }
        // Apply similar logic to breakpoints
        else if (FastForwardLine.TryParse(line, out var prevFastForward)) {
            string[] insertLines = clipboardText.Trim(Document.NewLine).SplitDocumentLines();

            if (insertLines.Length == 1) {
                Document.Insert(insertLines[0]);

                if (FastForwardLine.TryParse(Document.Lines[Document.Caret.Row], out var nextFastForward)
                    && prevFastForward.SaveState == nextFastForward.SaveState
                    && nextFastForward.SpeedText.All(c => c is >= '0' and <= '9' or '.')
                ) {
                    // Still valid
                    Document.ReplaceLine(Document.Caret.Row, nextFastForward.ToString());
                    return;
                }

                // Revert
                Document.ReplaceLine(Document.Caret.Row, line);
            }

            // Otherwise insert below
            Document.InsertLines(Document.Caret.Row + 1, insertLines);
            Document.Caret.Row += insertLines.Length;
            Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
        } else {
            Document.Insert(clipboardText);
        }
    }

    protected override void OnGoTo() {
        Document.Caret.Row = GoToDialog.Show(Document, owner: this, supportLabels: true);
        Document.Caret = ClampCaret(Document.Caret);
        Document.Selection.Clear();

        ScrollCaretIntoView();
    }

    private void OnSetFrameCountToStepAmount() {
        if (!CommunicationWrapper.Connected) {
            return;
        }

        using var __ = Document.Update();

        if (CommunicationWrapper.CurrentLine >= 0 && CommunicationWrapper.CurrentLine < Document.Lines.Count && CommunicationWrapper.CurrentFrameInInput > 0 &&
            ActionLine.TryParse(Document.Lines[CommunicationWrapper.CurrentLine], out var actionLine))
        {
            Document.ReplaceLine(CommunicationWrapper.CurrentLine, (actionLine with { FrameCount = Math.Clamp(CommunicationWrapper.CurrentFrameInInput, 0, ActionLine.MaxFrames) }).ToString());
        }
    }

    private void OnToggleCommentBreakpoints() {
        using var __ = Document.Update();

        Document.Selection.Normalize();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = 0;
            maxRow = Document.Lines.Count - 1;
        }

        // Only uncomment if all breakpoints are currently commented
        // Otherwise just comment uncommented breakpoints
        bool allCommented = true;
        for (int row = minRow; row <= maxRow; row++) {
            string line = Document.Lines[row];

            if (UncommentedBreakpointRegex.IsMatch(line)) {
                allCommented = false;
                break;
            }
        }

        for (int row = minRow; row <= maxRow; row++) {
            string line = Document.Lines[row];
            if (allCommented && CommentedBreakpointRegex.IsMatch(line)) {
                int hashIdx = line.IndexOf('#');
                string newLine = line.Remove(hashIdx, 1).TrimStart();
                Document.ReplaceLine(row, newLine);

                // Shift everything over
                int offset = line.Length - newLine.Length;
                if (row == minRow && !Document.Selection.Empty) {
                    Document.Selection.Start.Col -= offset;
                }
                if (row == maxRow && !Document.Selection.Empty) {
                    Document.Selection.End.Col -= offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col -= offset;
                }
            } else if (!allCommented && UncommentedBreakpointRegex.IsMatch(line)) {
                Document.ReplaceLine(row, $"# {line}");

                // Shift everything over
                const int offset = 2;
                if (row == minRow && !Document.Selection.Empty) {
                    Document.Selection.Start.Col += offset;
                }
                if (row == maxRow && !Document.Selection.Empty) {
                    Document.Selection.End.Col += offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col += offset;
                }
            }
        }
    }

    private void OnToggleCommentInputs() {
        using var __ = Document.Update();

        Document.Selection.Normalize();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }

        int oldCol = Document.Caret.Col;

        for (int row = minRow; row <= maxRow; row++) {
            string line = Document.Lines[row];
            string lineTrimmed = line.TrimStart();

            // Ignore blank lines
            if (string.IsNullOrEmpty(lineTrimmed) || lineTrimmed == "# ") {
                continue;
            }

            if (lineTrimmed.StartsWith('#')) {
                if (lineTrimmed.StartsWith("#lvl_") || TimestampRegex.IsMatch(lineTrimmed)) {
                    continue; // Ignore
                }

                if (!CommentedBreakpointRegex.IsMatch(lineTrimmed) // Check for breakpoints
                    && !CommentLine.IsLabel(lineTrimmed) // Check for commands
                    && !ActionLine.TryParse(lineTrimmed[1..], out _) // Check for action lines
                ) {
                    continue; // Ignore
                }

                int hashIdx = lineTrimmed.IndexOf('#');
                string newLine = FileRefactor.FormatLine(lineTrimmed.Remove(hashIdx, 1));
                Document.ReplaceLine(row, newLine);

                // Shift everything over
                int offset = line.Length - newLine.Length;
                if (row == minRow) {
                    Document.Selection.Start.Col -= offset;
                }
                if (row == maxRow) {
                    Document.Selection.End.Col -= offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col -= offset;
                }
            } else {
                int offset;
                if (char.IsLetter(line[0])) {
                    // Comment commands as labels
                    Document.ReplaceLine(row, $"#{line}");
                    offset = 1;
                } else {
                    Document.ReplaceLine(row, $"# {line}");
                    offset = 2;
                }

                // Shift everything over
                if (row == minRow) {
                    Document.Selection.Start.Col += offset;
                }
                if (row == maxRow) {
                    Document.Selection.End.Col += offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col += offset;
                }
            }
        }

        // Jump to next line for single-line edits
        if (minRow == maxRow && minRow == Document.Caret.Row && Document.Selection.Empty) {
            Document.Caret.Col = oldCol;
            Document.Caret.Row = Math.Min(Document.Lines.Count - 1, Document.Caret.Row + 1);
        }
    }

    private void OnToggleCommentText() {
        using var __ = Document.Update();

        Document.Selection.Normalize();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }

        // Only remove # when all lines start with it. Otherwise, add another
        bool allCommented = true;
        for (int row = minRow; row <= maxRow; row++) {
            string line = Document.Lines[row];

            if (!line.TrimStart().StartsWith('#')) {
                allCommented = false;
                break;
            }
        }

        int oldCol = Document.Caret.Col;

        for (int row = minRow; row <= maxRow; row++) {
            string line = Document.Lines[row];

            if (allCommented) {
                int hashIdx = line.IndexOf('#');
                string newLine = FileRefactor.FormatLine(line.Remove(hashIdx, 1));
                Document.ReplaceLine(row, newLine);

                // Shift everything over
                int offset = line.Length - newLine.Length;
                if (row == minRow) {
                    Document.Selection.Start.Col -= offset;
                }
                if (row == maxRow) {
                    Document.Selection.End.Col -= offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col -= offset;
                }
            } else {
                Document.ReplaceLine(row, $"# {line}");

                // Shift everything over
                const int offset = 2;
                if (row == minRow) {
                    Document.Selection.Start.Col += offset;
                }
                if (row == maxRow) {
                    Document.Selection.End.Col += offset;
                }
                if (row == Document.Caret.Row) {
                    Document.Caret.Col += offset;
                }
            }
        }

        // Jump to next line for single-line edits
        if (minRow == maxRow && minRow == Document.Caret.Row && Document.Selection.Empty) {
            Document.Caret.Col = oldCol;
            Document.Caret.Row = Math.Min(Document.Lines.Count - 1, Document.Caret.Row + 1);
        }
    }

    private void InsertOrRemoveText(Regex regex, string text) {
        using var __ = Document.Update();

        CollapseSelection();

        int insertDir = Settings.Instance.InsertDirection == InsertDirection.Above ? -1 : 1;

        // Check current line
        if (regex.IsMatch(Document.Lines[Document.Caret.Row])) {
            Document.RemoveLine(Document.Caret.Row);
        }
        // Check line in insert direction as well
        else if (Document.Caret.Row + insertDir >= 0 && Document.Caret.Row + insertDir < Document.Lines.Count && regex.IsMatch(Document.Lines[Document.Caret.Row + insertDir])) {
            Document.RemoveLine(Document.Caret.Row + insertDir);
            if (Settings.Instance.InsertDirection == InsertDirection.Above)
                Document.Caret.Row--;
        }
        // Otherwise insert new breakpoint
        else {
            InsertLine(text);
        }
    }

    private void RemoveLinesMatching(Regex regex) {
        using var __ = Document.Update();

        for (int row = Document.Lines.Count - 1; row >= 0; row--) {
            if (!regex.IsMatch(Document.Lines[row]))
                continue;

            Document.RemoveLine(row);

            if (Document.Caret.Row >= row)
                Document.Caret.Row--;
            if (Document.Selection.Start.Row >= row)
                Document.Selection.Start.Row--;
            if (Document.Selection.End.Row >= row)
                Document.Selection.End.Row--;
        }
    }

    private bool OnFrameOperation(CalculationOperator op) {
        if (!ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            return false;
        }

        if (calculationState != null) {
            // Cancel with same operation again
            if (op == calculationState.Operator && calculationState.Operand.Length == 0) {
                calculationState = null;
                Invalidate();
                return true;
            }

            CommitCalculation();
        }

        StartCalculation(op);
        Invalidate();
        return true;
    }

    #endregion

    #region Caret Movement

    public override CaretPosition ClampCaret(CaretPosition position, bool wrapLine = false, SnappingDirection direction = SnappingDirection.Ignore) {
        position = base.ClampCaret(position, wrapLine, direction);

        // Clamp to action line if possible
        string line = Document.Lines[position.Row];
        if (ActionLine.TryParse(line, out var actionLine)) {
            position.Col = Math.Min(line.Length, SnapColumnToActionLine(actionLine, position.Col, direction));
        }
        // Clamp to breakpoint if possible
        if (FastForwardLine.TryParse(line, out _)) {
            position.Col = Math.Min(line.Length, SnapColumnToFastForward(position.Col, direction));
        }

        return position;
    }

    protected override CaretPosition GetCaretMovementTarget(CaretMovementType direction) {
        string line = Document.Lines[Document.Caret.Row];
        var position = Document.Caret;

        var currentActionLine = ActionLine.Parse(line);
        var currentFastForward = FastForwardLine.Parse(line);
        if (currentActionLine is { } actionLine) {
            position.Col = Math.Min(line.Length, SnapColumnToActionLine(actionLine, position.Col));
            int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Length;

            // Line wrapping
            if (position.Row > 0 && position.Col == leadingSpaces &&
                direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft)
            {
                position.Row = GetNextVisualLinePosition(-1, position).Row;
                position.Col = DesiredVisualCol = Document.Lines[position.Row].Length;
            } else if (position.Row < Document.Lines.Count - 1 && position.Col == line.Length &&
                       direction is CaretMovementType.CharRight or CaretMovementType.WordRight)
            {
                position.Row = GetNextVisualLinePosition( 1, position).Row;
                position.Col = DesiredVisualCol = 0;
            } else {
                // Regular action line movement
                return direction switch {
                    CaretMovementType.CharLeft  => ClampCaret(new CaretPosition(position.Row, GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < position.Col, position.Col)), wrapLine: true),
                    CaretMovementType.CharRight => ClampCaret(new CaretPosition(position.Row, GetSoftSnapColumns(actionLine).FirstOrDefault(c => c > position.Col, position.Col)), wrapLine: true),
                    CaretMovementType.WordLeft  => ClampCaret(new CaretPosition(position.Row, GetHardSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < position.Col, position.Col)), wrapLine: true),
                    CaretMovementType.WordRight => ClampCaret(new CaretPosition(position.Row, GetHardSnapColumns(actionLine).FirstOrDefault(c => c > position.Col, position.Col)), wrapLine: true),
                    CaretMovementType.LineStart => ClampCaret(new CaretPosition(position.Row, leadingSpaces)),
                    CaretMovementType.LineEnd   => ClampCaret(new CaretPosition(position.Row, line.Length)),
                    _ => GetNewTextCaretPosition(direction),
                };
            }

            return position;
        } else if (currentFastForward != null) {
            position.Col = Math.Min(line.Length, SnapColumnToFastForward(position.Col));

            // Line wrapping
            if (position.Row > 0 && position.Col == 0 &&
                direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft)
            {
                position.Row = GetNextVisualLinePosition(-1, position).Row;
                position.Col = DesiredVisualCol = Document.Lines[position.Row].Length;
            } else if (position.Row < Document.Lines.Count - 1 && position.Col == line.Length &&
                       direction is CaretMovementType.CharRight or CaretMovementType.WordRight)
            {
                position.Row = GetNextVisualLinePosition( 1, position).Row;
                position.Col = DesiredVisualCol = 0;
            } else {
                // Regular breakpoint movement
                return direction switch {
                    CaretMovementType.CharLeft  => ClampCaret(new CaretPosition(position.Row, position.Col == "***".Length ? 0 : position.Col - 1), wrapLine: true),
                    CaretMovementType.CharRight => ClampCaret(new CaretPosition(position.Row, position.Col == 0 ? "***".Length : position.Col + 1), wrapLine: true),
                    CaretMovementType.WordLeft  => ClampCaret(new CaretPosition(position.Row, position.Col > "***".Length ? "***".Length : 0), wrapLine: true),
                    CaretMovementType.WordRight => ClampCaret(new CaretPosition(position.Row, position.Col < "***".Length ? "***".Length : line.Length), wrapLine: true),
                    CaretMovementType.LineStart => ClampCaret(new CaretPosition(position.Row, 0)),
                    CaretMovementType.LineEnd   => ClampCaret(new CaretPosition(position.Row, line.Length)),
                    _ => GetNewTextCaretPosition(direction),
                };
            }

            return position;
        } else {
            return base.GetCaretMovementTarget(direction);
        }
    }
    protected override void MoveCaretTo(CaretPosition newCaret, bool updateSelection) {
        var oldCaret = Document.Caret;
        var oldActionLine = ActionLine.Parse(Document.Lines[oldCaret.Row]);

        base.MoveCaretTo(newCaret, updateSelection);

        // When going from a non-action-line to an action-line, snap the caret to the frame count
        if (oldActionLine == null && TryParseAndFormatActionLine(newCaret.Row, out _)) {
            Document.Caret.Col = DesiredVisualCol = ActionLine.MaxFramesDigits;
        }

        // If the selection is multi-line, always select the entire start/end line if it's an action line
        if (updateSelection && Settings.Instance.AutoSelectFullActionLine && Document.Selection.Start.Row != Document.Selection.End.Row) {
            string startLine = Document.Lines[Document.Selection.Start.Row];
            string endLine = Document.Lines[Document.Selection.End.Row];
            if (ActionLine.Parse(startLine) != null) {
                Document.Selection.Start.Col = Document.Selection.Start < Document.Selection.End ? 0 : startLine.Length;
            }
            if (ActionLine.Parse(Document.Lines[Document.Selection.End.Row]) != null) {
                Document.Selection.End.Col = Document.Selection.Start < Document.Selection.End ? endLine.Length : 0;
            }
        }
    }

    #endregion

    #region Mouse Interactions

    protected override void OnMouseDown(MouseEventArgs e) {
        // Clear frame operation state
        calculationState = null;

        if (e.Buttons.HasFlag(MouseButtons.Primary) && e.HasCommonModifier() && LocationToLineLink(e.Location) is { } linkAnchor) {
            ((LineLinkAnchorData)linkAnchor.UserData!).OnUse();

            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) {
        // Adjust frame count
        if (e.Modifiers.HasFlag(Keys.Shift)) {
            if (Document.Selection.Empty) {
                var (position, _) = LocationToCaretPosition(e.Location);
                position = ClampCaret(position);
                AdjustNumericValues(Document.Caret.Row, position.Row, Math.Sign(e.Delta.Height));
            } else {
                AdjustNumericValues(Document.Selection.Start.Row, Document.Selection.End.Row, Math.Sign(e.Delta.Height));
            }

            e.Handled = true;
            return;
        }
        // Zoom in/out
        if (e.HasCommonModifier()) {
            const float scrollSpeed = 0.1f;
            AdjustZoom(Math.Sign(e.Delta.Height) * scrollSpeed);

            e.Handled = true;
            return;
        }

        base.OnMouseWheel(e);
    }

    private void AdjustZoom(float zoomDelta) {
        float oldCarY = Font.LineHeight() * GetVisualPosition(Document.Caret).Row;
        float oldOffset = scrollablePosition.Y - oldCarY;

        Settings.Instance.FontZoom *= 1.0f + zoomDelta;
        Settings.OnFontChanged();
        Recalc();

        float newCarY = Font.LineHeight() * GetVisualPosition(Document.Caret).Row;

        // Adjust scroll to keep caret centered
        scrollable.ScrollPosition = scrollablePosition with {
            Y = (int)(newCarY + oldOffset)
        };
    }

    protected override void UpdateMouseCursor(PointF location, Keys modifiers) {
        // Maybe a bit out-of-place here, but this is required to update the underline of line links
        Invalidate();

        if (modifiers.HasCommonModifier() && LocationToLineLink(location) != null) {
            Cursor = Cursors.Pointer;
            return;
        }

        base.UpdateMouseCursor(location, modifiers);
    }

    #endregion

    #region Drawing

    protected override void DrawLine(SKCanvas canvas, string line, float x, float y) {
        highlighter.DrawLine(canvas, x, y, line);
    }

    protected override void DrawLineNumberBackground(SKCanvas canvas, SKPaint fillPaint, SKPaint strokePaint, float yPos) {
        base.DrawLineNumberBackground(canvas, fillPaint, strokePaint, yPos);

        // Highlight playing / savestate line
        if (!CommunicationWrapper.Connected) {
            return;
        }

        if (CommunicationWrapper.CurrentLine != -1 && CommunicationWrapper.CurrentLine < actualToVisualRows.Length) {
            fillPaint.ColorF = Settings.Instance.Theme.PlayingLineBg.ToSkia();
            canvas.DrawRect(
                x: scrollablePosition.X,
                y: actualToVisualRows[CommunicationWrapper.CurrentLine] * Font.LineHeight(),
                w: textOffsetX - LineNumberPadding,
                h: Font.LineHeight(),
                fillPaint);
        }
        foreach (int saveStateLine in CommunicationWrapper.SaveStateLines) {
            if (saveStateLine < 0 || saveStateLine >= actualToVisualRows.Length) {
                continue;
            }

            fillPaint.ColorF = Settings.Instance.Theme.SavestateBg.ToSkia();
            if (saveStateLine == CommunicationWrapper.CurrentLine) {
                canvas.DrawRect(
                    x: scrollablePosition.X,
                    y: actualToVisualRows[saveStateLine] * Font.LineHeight(),
                    w: 5.0f,
                    h: Font.LineHeight(),
                    fillPaint);
            } else {
                canvas.DrawRect(
                    x: scrollablePosition.X,
                    y: actualToVisualRows[saveStateLine] * Font.LineHeight(),
                    w: textOffsetX - LineNumberPadding,
                    h: Font.LineHeight(),
                    fillPaint);
            }
        }
    }
    protected override Color GetLineNumberBackground(int row) {
        int currVisualRow = actualToVisualRows[row];

        if (CommunicationWrapper.CurrentLine >= 0 && CommunicationWrapper.CurrentLine < actualToVisualRows.Length &&
            actualToVisualRows[CommunicationWrapper.CurrentLine] == currVisualRow
        ) {
            return Settings.Instance.Theme.PlayingLineFg;
        }

        if (CommunicationWrapper.SaveStateLines.Any(line => line >= 0 && line < actualToVisualRows.Length && actualToVisualRows[line] == currVisualRow)) {
            return Settings.Instance.Theme.SavestateFg;
        }

        return Settings.Instance.Theme.LineNumber;
    }

    public override void Draw(SKSurface surface) {
        base.Draw(surface);

        var canvas = surface.Canvas;

        using var strokePaint = new SKPaint();
        strokePaint.Style = SKPaintStyle.Stroke;

        using var fillPaint = new SKPaint();
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = true;


        // Underline current line-link
        if (Keyboard.Modifiers.HasCommonModifier() && LocationToLineLink(PointFromScreen(Mouse.Position)) is { } linkAnchor) {
            strokePaint.Color = Settings.Instance.Theme.CommandPaint.ForegroundColor.Color; // Only commands can have line-links anyway
            strokePaint.StrokeWidth = 1.0f;
            strokePaint.StrokeCap = SKStrokeCap.Round;

            float y = actualToVisualRows[linkAnchor.Row] * Font.LineHeight();

            canvas.DrawLine(
                x0: textOffsetX + linkAnchor.MinCol * Font.CharWidth(), y0: y + Font.Offset() + Font.Metrics.UnderlinePosition ?? 0.0f + 1.0f,
                x1: textOffsetX + (linkAnchor.MaxCol + 1) * Font.CharWidth(), y1: y + Font.Offset() + Font.Metrics.UnderlinePosition ?? 0.0f + 1.0f,
                strokePaint);
        }

        // Draw quick-edits
        foreach (var anchor in GetQuickEdits()) {
            const float padding = 1.0f;

            float y = Font.LineHeight() * actualToVisualRows[anchor.Row];
            float x = Font.CharWidth() * anchor.MinCol;
            float w = Font.CharWidth() * anchor.MaxCol - x;

            bool selected = Document.Caret.Row == anchor.Row &&
                            Document.Caret.Col >= anchor.MinCol &&
                            Document.Caret.Col <= anchor.MaxCol;

            strokePaint.Color = selected ? SKColors.White : SKColors.Gray;
            strokePaint.StrokeWidth = selected ? 2.0f : 1.0f;

            canvas.DrawRect(
                x + textOffsetX - padding, y - padding, w + padding * 2.0f, Font.LineHeight() + padding * 2.0f,
                strokePaint);
        }

        // Draw suffix text
        if (CommunicationWrapper.Connected &&
            CommunicationWrapper.CurrentLine != -1 &&
            CommunicationWrapper.CurrentLine < actualToVisualRows.Length)
        {
            var font = FontManager.SKEditorFontBold;

            const float padding = 10.0f;
            float suffixWidth = font.MeasureWidth(CommunicationWrapper.CurrentLineSuffix);

            fillPaint.ColorF = Settings.Instance.Theme.PlayingFrame.ToSkia();
            canvas.DrawText(CommunicationWrapper.CurrentLineSuffix,
                x: scrollablePosition.X + scrollableSize.Width - suffixWidth - padding,
                y: actualToVisualRows[CommunicationWrapper.CurrentLine] * font.LineHeight() + font.Offset(),
                font, fillPaint);
        }

        // Draw calculate operation
        if (calculationState is not null) {
            string calculateLine = $"{calculationState.Operator.Char()}{calculationState.Operand}";

            float padding = Font.CharWidth() * 0.5f;
            float x = textOffsetX + Font.CharWidth() * ActionLine.MaxFramesDigits + Font.CharWidth() * 0.5f;
            float y = Font.LineHeight() * GetVisualPosition(Document.Caret).Row;
            float w = Font.CharWidth() * calculateLine.Length + 2 * padding;
            float h = Font.LineHeight();

            fillPaint.ColorF = Settings.Instance.Theme.CalculateBg.ToSkia();
            canvas.DrawRoundRect(x, y, w, h, 4.0f, 4.0f, fillPaint);
            fillPaint.ColorF = Settings.Instance.Theme.CalculateFg.ToSkia();
            canvas.DrawText(calculateLine, x + padding, y + Font.Offset(), Font, fillPaint);
        }

        // Draw toast message box
        if (!string.IsNullOrWhiteSpace(toastMessage)) {
            string[] lines = toastMessage.SplitDocumentLines();

            var font = FontManager.SKPopupFont;

            float width = font.CharWidth() * lines.Select(line => line.Length).Aggregate(Math.Max);
            float height = font.LineHeight() * lines.Length;
            float x = scrollablePosition.X + (scrollableSize.Width - width) / 2.0f;
            float y = scrollablePosition.Y + (scrollableSize.Height - height) / 2.0f;
            float padding = Settings.Instance.Theme.PopupMenuBorderPadding;

            canvas.DrawRoundRect(
                x: x - padding, y: y - padding,
                w: width + padding * 2.0f, h: height + padding * 2.0f,
                rx: Settings.Instance.Theme.PopupMenuBorderRounding, ry: Settings.Instance.Theme.PopupMenuBorderRounding,
                Settings.Instance.Theme.PopupMenuBgPaint);

            foreach (string line in lines) {
                canvas.DrawText(line, x, y + Font.Offset(), font, Settings.Instance.Theme.PopupMenuFgPaint);
                y += Font.LineHeight();
            }
        }
    }

    #endregion

    #region Helper Methods

    // For movement without Ctrl
    private static IReadOnlyList<int> GetSoftSnapColumns(ActionLine actionLine) {
        int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Length;

        List<int> softSnapColumns = [];
        // Frame count
        softSnapColumns.AddRange(Enumerable.Range(leadingSpaces, actionLine.Frames.Length + 1));
        // Actions
        foreach (var action in actionLine.Actions.Sorted()) {
            int column = GetColumnOfAction(actionLine, action);
            softSnapColumns.Add(column);

            if (action == Actions.DashOnly)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.Actions.GetDashOnly().Count()));
            if (action == Actions.MoveOnly)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.Actions.GetMoveOnly().Count()));
            if (action == Actions.PressedKey)
                softSnapColumns.AddRange(Enumerable.Range(column + 1, actionLine.CustomBindings.Count));
        }
        // Feather angle/magnitude
        if (actionLine.Actions.HasFlag(Actions.Feather)) {
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            softSnapColumns.AddRange(Enumerable.Range(featherColumn, actionLine.ToString().Length + 1 - featherColumn));
        }

        return softSnapColumns.AsReadOnly();
    }

    // For movement with Ctrl
    private static IReadOnlyList<int> GetHardSnapColumns(ActionLine actionLine) {
        int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Length;

        List<int> hardSnapColumns =
        [
            leadingSpaces,
            ActionLine.MaxFramesDigits,
        ];

        // Actions
        if (actionLine.Actions != Actions.None) {
            hardSnapColumns.Add(GetColumnOfAction(actionLine, actionLine.Actions.Sorted().Last()) + actionLine.CustomBindings.Count);

        // Feather angle/magnitude
        if (actionLine.Actions.HasFlag(Actions.Feather)) {
            int featherColumn = GetColumnOfAction(actionLine, Actions.Feather);
            string line = actionLine.ToString();

            int decimalColumn = featherColumn + 1;
            while (decimalColumn <= line.Length && line[decimalColumn - 1] != '.') {
                decimalColumn++;
            }
            hardSnapColumns.Add(decimalColumn);
            hardSnapColumns.Add(decimalColumn + 1);

            if (actionLine.FeatherMagnitude != null) {
                hardSnapColumns.Add(featherColumn + 1);
                int borderColumn = featherColumn + 1;
                while (borderColumn <= line.Length && line[borderColumn - 1] != ',') {
                    borderColumn++;
                }
                hardSnapColumns.Add(borderColumn);
                hardSnapColumns.Add(borderColumn + 1);

                decimalColumn = borderColumn + 1;
                while (decimalColumn <= line.Length && line[decimalColumn - 1] != '.') {
                    decimalColumn++;
                }
                hardSnapColumns.Add(decimalColumn);
                hardSnapColumns.Add(decimalColumn + 1);
            }
            hardSnapColumns.Add(line.Length + 1);
        }}

        return hardSnapColumns.AsReadOnly();
    }

    private static int GetColumnOfAction(ActionLine actionLine, Actions action) {
        int index = actionLine.Actions.Sorted().IndexOf(action);
        if (index < 0) return -1;

        int dashOnlyIndex = actionLine.Actions.Sorted().IndexOf(Actions.DashOnly);
        int moveOnlyIndex = actionLine.Actions.Sorted().IndexOf(Actions.MoveOnly);
        int customBindingIndex = actionLine.Actions.Sorted().IndexOf(Actions.PressedKey);

        int additionalOffset = 0;

        if (dashOnlyIndex != -1 && index > dashOnlyIndex)
            additionalOffset += actionLine.Actions.GetDashOnly().Count();
        if (moveOnlyIndex != -1 && index > moveOnlyIndex)
            additionalOffset += actionLine.Actions.GetMoveOnly().Count();
        if (customBindingIndex != -1 && index > customBindingIndex)
            additionalOffset += actionLine.CustomBindings.Count;

        return ActionLine.MaxFramesDigits + (index + 1) * 2 + additionalOffset;
    }

    private static Actions GetActionsFromColumn(ActionLine actionLine, int column, CaretMovementType direction) {
        var lineText = actionLine.ToString();

        if ((column <= ActionLine.MaxFramesDigits + 1) &&
            direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft) {
            return Actions.None; // There are no actions to the left of the caret
        }
        if ((column <= ActionLine.MaxFramesDigits || column >= lineText.Length) &&
            direction is CaretMovementType.CharRight or CaretMovementType.WordRight) {
            return Actions.None; // There are no actions to the right of the caret
        }

        if (direction == CaretMovementType.CharLeft) {
            //  15,R|,X => R
            return lineText[column - 2].ActionForChar();
        } else if (direction == CaretMovementType.CharRight) {
            //  15,R|,X => X
            return lineText[column].ActionForChar();
        } else if (direction == CaretMovementType.WordLeft) {
            //  15,R,D|,X => R,D
            var actions = Actions.None;
            while (column > ActionLine.MaxFramesDigits + 1) {
                actions |= lineText[column - 2].ActionForChar();
                column -= 2;
            }
            return actions;
        } else {
            //  15,R|,D,X => D,X
            var actions = Actions.None;
            while (column < lineText.Length) {
                actions |= lineText[column].ActionForChar();
                column += 2;
            }
            return actions;
        }
    }

    public static int SnapColumnToActionLine(ActionLine actionLine, int column, SnappingDirection direction = SnappingDirection.Ignore) {
        // Snap to the closest valid column
        int nextLeft = GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c <= column, -1);
        int nextRight = GetSoftSnapColumns(actionLine).FirstOrDefault(c => c >= column, -1);

        if (nextLeft == column || nextRight == column) return column;

        if (nextLeft == -1 && nextRight == -1) return column;
        if (nextLeft == -1) return nextRight;
        if (nextRight == -1) return nextLeft;

        return direction switch {
            // Choose the closest one
            SnappingDirection.Ignore => column - nextLeft < nextRight - column
                ? nextLeft
                : nextRight,

            SnappingDirection.Left => nextLeft,
            SnappingDirection.Right => nextRight,

            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }
    private static int SnapColumnToFastForward(int column, SnappingDirection direction = SnappingDirection.Ignore) {
        if (column == 1) {
            return direction switch {
                SnappingDirection.Ignore or SnappingDirection.Left => 0,
                SnappingDirection.Right => "***".Length,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }
        if (column == 2) {
            return direction switch {
                SnappingDirection.Left => 0,
                SnappingDirection.Ignore or SnappingDirection.Right => "***".Length,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }

        return column;
    }

    #endregion
}
