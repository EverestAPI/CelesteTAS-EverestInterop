using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Controls;
using CelesteStudio.Data;
using CelesteStudio.Dialog;
using CelesteStudio.Editing.ContextActions;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication;
using StudioCommunication.Util;
using WrapLine = (string Line, int Index);
using WrapEntry = (int StartOffset, (string Line, int Index)[] Lines);

namespace CelesteStudio.Editing;

public sealed class Editor : SkiaDrawable {
    /// Reports insertions and deletions of the underlying document
    public event Action<Document, Dictionary<int, string>, Dictionary<int, string>> TextChanged = (_, _, _) => { };

    /// The currently open file has changed. The previous document might not be available (for example during startup).
    public event Action<Document?, Document> DocumentChanged = (_, _) => { };

    private Document? document;
    public Document Document {
        get => document!;
        set {
            if (document != null) {
                document.TextChanged -= HandleTextChanged;

                if (Settings.Instance.AutoSave) {
                    FixInvalidInputs();
                    document.Save();
                }
            }

            DocumentChanged(document, value);
            document = value;

            // Ensure everything is properly formatted
            FormatLines(Enumerable.Range(0, document.Lines.Count).ToArray());

            // Jump to end when file only 10 lines, else the start
            document.Caret = document.Lines.Count is > 0 and <= 10
                ? new CaretPosition(document.Lines.Count - 1, document.Lines[^1].Length)
                : new CaretPosition(0, 0);

            // Ensure everything is still valid when something has changed
            document.TextChanged += HandleTextChanged;

            // Reset various state
            ActivePopupMenu = null;

            FixInvalidInputs();
            Recalc();
            ScrollCaretIntoView();

            Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
            Settings.Changed += () => {
                Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
            };

            // Calculate total frame count
            TotalFrameCount = 0;
            foreach (string line in document.Lines) {
                if (!ActionLine.TryParse(line, out var actionLine)) {
                    continue;
                }
                TotalFrameCount += actionLine.FrameCount;
            }

            // Auto-collapses folds which are too long
            foreach (var fold in foldings) {
                if (Settings.Instance.MaxUnfoldedLines == 0) {
                    // Close everything
                    SetCollapse(fold.MinRow, true);
                    continue;
                }

                int lines = fold.MaxRow - fold.MinRow - 1; // Only the lines inside the fold are counted
                if (lines > Settings.Instance.MaxUnfoldedLines) {
                    SetCollapse(fold.MinRow, true);
                }
            }

            // Apply auto-collapses
            Recalc();

            return;

            void HandleTextChanged(Document _, Dictionary<int, string> insertions, Dictionary<int, string> deletions) {
                lastModification = DateTime.UtcNow;

                FormatLines(insertions.Keys);

                // Adjust total frame count
                foreach (string deletion in deletions.Values) {
                    if (!ActionLine.TryParse(deletion, out var actionLine)) {
                        continue;
                    }
                    TotalFrameCount -= actionLine.FrameCount;
                }
                foreach (string insertion in insertions.Values) {
                    if (!ActionLine.TryParse(insertion, out var actionLine)) {
                        continue;
                    }
                    TotalFrameCount += actionLine.FrameCount;
                }

                Task.Run(() => FileRefactor.FixRoomLabelIndices(Document.FilePath, StyleConfig.Current, Document.Caret.Row));
                Recalc();
                ScrollCaretIntoView();

                TextChanged(document, insertions, deletions);
            }
        }
    }

    private readonly Scrollable scrollable;
    // These values need to be stored, since WPF doesn't like accessing them directly from the scrollable
    private Point scrollablePosition;
    private Size scrollableSize;

    private const int offscreenLinePadding = 3;

    // Only expand width of line numbers for actually visible digits
    private int lastVisibleLineNumberDigits = -1;
    private int VisibleLineNumberDigits {
        get {
            int bottomVisualRow = (int)((scrollablePosition.Y + scrollableSize.Height) / Font.LineHeight()) + offscreenLinePadding;
            int bottomRow = Math.Min(Document.Lines.Count - 1, GetActualRow(bottomVisualRow));

            return bottomRow.Digits();
        }
    }

    private readonly PixelLayout pixelLayout = new();
    private readonly PopupMenu autoCompleteMenu = new();
    private readonly PopupMenu contextActionsMenu = new();

    private PopupMenu? activePopupMenu;
    public PopupMenu? ActivePopupMenu {
        get => activePopupMenu;
        set {
            if (activePopupMenu == value && (activePopupMenu?.Visible ?? false)) {
                return;
            }

            if (activePopupMenu != null && value == null) {
                activePopupMenu.Visible = false;
            }
            activePopupMenu = value;
            if (activePopupMenu != null) {
                activePopupMenu.Visible = true;
            }

            Recalc();
        }
    }

    /// Auto-complete entries for commands and snippets
    private readonly List<PopupMenu.Entry> baseAutoCompleteEntries = [];

    /// Cancellation token for fetching command-argument auto-complete entries
    private CancellationTokenSource? commandAutoCompleteTokenSource;
    /// Index of the currently fetched entries, to prevent flashing "Loading..." while typing
    private int lastAutoCompleteArgumentIndex = -1;

    // These should be ordered from most specific to most applicable.
    private readonly ContextAction[] contextActions = [
        new CombineConsecutiveSameInputs(),

        new SwapActions(Actions.Left, Actions.Right, MenuEntry.Editor_SwapSelectedLR),
        new SwapActions(Actions.Jump, Actions.Jump2, MenuEntry.Editor_SwapSelectedJK),
        new SwapActions(Actions.Dash, Actions.Dash2, MenuEntry.Editor_SwapSelectedXC),

        new ForceCombineInputFrames(),
        new SplitFrames(),

        new CreateRepeat(),
        new InlineRepeatCommand(),
        new InlineReadCommand(),

        new UseLineLink(MenuEntry.ContextActions_OpenReadFile),
        new UseLineLink(MenuEntry.ContextActions_GoToPlayLine),
    ];

    private SKFont Font => FontManager.SKEditorFontRegular;
    private SyntaxHighlighter highlighter;
    private const float LineNumberPadding = 5.0f;

    /// Indicates last modification time, used to check if the user is currently typing
    private DateTime lastModification = DateTime.UtcNow;

    /// Current total frame count (including commands if connected to Celeste)
    public int TotalFrameCount;

    // Offset from the left accounting for line numbers
    private float textOffsetX;

    // When editing a long line and moving to a short line, "remember" the column on the long line, unless the caret has been moved.
    private int desiredVisualCol;

    // Wrap long lines into multiple visual lines
    private readonly Dictionary<int, WrapEntry> commentLineWraps = new();

    // Foldings can collapse sections of the document
    private readonly List<Folding> foldings = [];

    // Visual lines are all lines shown in the editor
    // A single actual line may occupy multiple visual lines
    private int[] actualToVisualRows = [];
    private readonly List<int> visualToActualRows = [];

    // A toast is a small message box which is temporarily shown in the middle of the screen
    private string toastMessage = string.Empty;
    private CancellationTokenSource? toastTokenSource;

    // Simple math operations like +, -, *, / can be performed on action line's frame counts
    private CalculationState? calculationState = null;

    // Retain previous settings from Find-dialog
    private string lastFindQuery = string.Empty;
    private bool lastFindMatchCase = false;

    private static readonly Regex UncommentedBreakpointRegex = new(@"^\s*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex CommentedBreakpointRegex = new(@"^\s*#+\*\*\*", RegexOptions.Compiled);
    private static readonly Regex AllBreakpointRegex = new(@"^\s*#*\*\*\*", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new(@"^\s*#+\s*(\d+:)?\d{1,2}:\d{2}\.\d{3}\(\d+\)", RegexOptions.Compiled);

    public Editor(Document document, Scrollable scrollable) {
        this.document = document;
        this.scrollable = scrollable;

        StyleConfig.Initialize(this);
        FileRefactor.Initialize(this);

        DocumentChanged(null, document);

        CanFocus = true;
        Cursor = Cursors.IBeam;

        autoCompleteMenu.Visible = false;
        contextActionsMenu.Visible = false;

        pixelLayout.Add(autoCompleteMenu, 0, 0);
        pixelLayout.Add(contextActionsMenu, 0, 0);
        Content = pixelLayout;

        Focus();

        highlighter = new(FontManager.SKEditorFontRegular, FontManager.SKEditorFontBold, FontManager.SKEditorFontItalic, FontManager.SKEditorFontBoldItalic);
        Settings.FontChanged += () => {
            highlighter = new(FontManager.SKEditorFontRegular, FontManager.SKEditorFontBold, FontManager.SKEditorFontItalic, FontManager.SKEditorFontBoldItalic);
            Recalc();
        };

        BackgroundColor = Settings.Instance.Theme.Background;
        Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.Background;

        // Need to redraw the line numbers when scrolling horizontally
        scrollable.Scroll += (_, _) => {
            scrollablePosition = scrollable.ScrollPosition;

            // Only update if required
            int newVisibleDigits = VisibleLineNumberDigits;
            if (lastVisibleLineNumberDigits != newVisibleDigits) {
                lastVisibleLineNumberDigits = newVisibleDigits;
                Recalc();
            }

            Invalidate();
        };
        scrollable.SizeChanged += (_, _) => {
            // Ignore vertical and small horizontal size changes
            scrollableSize.Height = scrollable.ClientSize.Height;
            if (Math.Abs(scrollableSize.Width - scrollable.ClientSize.Width) <= 0.1f) {
                Invalidate(); // Update SkiaDrawable
                return;
            }

            scrollableSize.Width = scrollable.ClientSize.Width;

            // Update wrapped lines
            if (Settings.Instance.WordWrapComments) {
                Recalc();
            } else {
                Invalidate(); // Update SkiaDrawable
            }
        };

        CommunicationWrapper.StateUpdated += (prevState, state) => {
            if (!state.FileNeedsReload) {
                TotalFrameCount = state.TotalFrames;
            }

            if (prevState.CurrentLine == state.CurrentLine &&
                prevState.SaveStateLine == state.SaveStateLine &&
                prevState.CurrentLineSuffix == state.CurrentLineSuffix) {
                // Nothing to do
                return;
            }

            if (Settings.Instance.SyncCaretWithPlayback && state.CurrentLine != -1) {
                Document.Caret.Row = state.CurrentLine;
                Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
                Document.Caret = ClampCaret(Document.Caret);
                Document.Selection.Clear();

                ScrollCaretIntoView(center: true);
            }

            Invalidate();
        };

        // Commands
        var commandsMenu = new SubMenuItem { Text = "Insert Other Command" };

        CommunicationWrapper.CommandsChanged += _ => {
            GenerateBaseAutoCompleteEntries();
            GenerateCommandMenu();
            Recalc();
        };
        // Update command separator
        Settings.Changed += () => {
            GenerateBaseAutoCompleteEntries();
            GenerateCommandMenu();
            Recalc();
        };

        GenerateBaseAutoCompleteEntries();
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
                MenuEntry.Editor_Cut.ToAction(OnCut),
                MenuEntry.Editor_Copy.ToAction(OnCopy),
                MenuEntry.Editor_Paste.ToAction(OnPaste),
                new SeparatorMenuItem(),
                MenuEntry.Editor_Undo.ToAction(OnUndo),
                MenuEntry.Editor_Redo.ToAction(OnRedo),
                new SeparatorMenuItem(),
                MenuEntry.Editor_SelectAll.ToAction(OnSelectAll),
                MenuEntry.Editor_SelectBlock.ToAction(OnSelectBlock),
                new SeparatorMenuItem(),
                MenuEntry.Editor_Find.ToAction(OnFind),
                MenuEntry.Editor_GoTo.ToAction(OnGoTo),
                MenuEntry.Editor_ToggleFolding.ToAction(OnToggleFolding),
                new SeparatorMenuItem(),
                MenuEntry.Editor_DeleteSelectedLines.ToAction(OnDeleteSelectedLines),
                MenuEntry.Editor_SetFrameCountToStepAmount.ToAction(OnSetFrameCountToStepAmount),
                new SeparatorMenuItem(),
                MenuEntry.Editor_InsertRemoveBreakpoint.ToAction(() => InsertOrRemoveText(UncommentedBreakpointRegex, "***")),
                MenuEntry.Editor_InsertRemoveSavestateBreakpoint.ToAction(() => InsertOrRemoveText(UncommentedBreakpointRegex, "***S")),
                MenuEntry.Editor_RemoveAllUncommentedBreakpoints.ToAction(() => RemoveLinesMatching(UncommentedBreakpointRegex)),
                MenuEntry.Editor_RemoveAllBreakpoints.ToAction(() => RemoveLinesMatching(AllBreakpointRegex)),
                MenuEntry.Editor_CommentUncommentAllBreakpoints.ToAction(OnToggleCommentBreakpoints),
                MenuEntry.Editor_CommentUncommentInputs.ToAction(OnToggleCommentInputs),
                MenuEntry.Editor_CommentUncommentText.ToAction(OnToggleCommentText),
                new SeparatorMenuItem(),
                MenuEntry.Editor_InsertRoomName.ToAction(OnInsertRoomName),
                MenuEntry.Editor_InsertCurrentTime.ToAction(OnInsertTime),
                MenuEntry.Editor_RemoveAllTimestamps.ToAction(() => RemoveLinesMatching(TimestampRegex)),
                MenuEntry.Editor_InsertCurrentPosition.ToAction(OnInsertPosition),
                MenuEntry.Editor_InsertCurrentSpeed.ToAction(OnInsertSpeed),
                MenuEntry.Editor_InsertModInfo.ToAction(OnInsertModInfo),
                MenuEntry.Editor_InsertConsoleLoadCommand.ToAction(OnInsertConsoleLoadCommand),
                MenuEntry.Editor_InsertSimpleConsoleLoadCommand.ToAction(OnInsertSimpleConsoleLoadCommand),
                commandsMenu,
                new SeparatorMenuItem(),
                MenuEntry.Editor_OpenAutoCompleteMenu.ToAction(() => {
                    UpdateAutoComplete();
                    Recalc();
                }),
                MenuEntry.Editor_OpenContextActionsMenu.ToAction(() => {
                    UpdateContextActions();
                    Recalc();
                }),
            }
        };

        MenuItem CreateCommandInsert(CommandInfo info) {
            var cmd = new Command { Shortcut = Keys.None };
            cmd.Executed += (_, _) => {
                InsertQuickEdit(info.Insert);
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
        }
    }
    private void FixInvalidInput(int row) {
        using var __ = Document.Update(raiseEvents: false);

        // Frameless action lines are only intended for editing and shouldn't be part of the final TAS
        if (ActionLine.TryParse(Document.Lines[row], out var actionLine)) {
            actionLine.FrameCount = Math.Clamp(actionLine.FrameCount, 0, ActionLine.MaxFrames);

            Document.ReplaceLine(row, actionLine.ToString());
        }
    }

    #region General Helper Methods

    /// Recalculates all values and invalidates the paint.
    private void Recalc() {
        // Ensure there is always at least 1 line
        if (Document.Lines.Count == 0) {
            using var __ = Document.Update(raiseEvents: false);
            Document.InsertLine(0, string.Empty);
        }

        // Snap caret
        Document.Caret.Row = Math.Clamp(Document.Caret.Row, 0, Document.Lines.Count - 1);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);

        // Calculate bounds, apply wrapping, create foldings
        float width = 0.0f, height = 0.0f;

        commentLineWraps.Clear();
        foldings.Clear();

        var activeCollapses = new HashSet<int>();
        var activeFoldings = new Dictionary<int, int>(); // depth -> startRow

        Array.Resize(ref actualToVisualRows, Document.Lines.Count);
        visualToActualRows.Clear();

        // Recalculate line links
        Document.RemoveAnchorsIf(anchor => anchor.UserData is LineLinkAnchorData);

        // Assign all collapsed lines to the visual row of the collapse start
        for (int row = 0, visualRow = 0; row < Document.Lines.Count; row++) {
            var line = Document.Lines[row];
            var trimmed = line.TrimStart();

            bool startedCollapse = false;
            if (Document.FindFirstAnchor(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData) != null) {
                activeCollapses.Add(row);

                if (activeCollapses.Count == 1) {
                    startedCollapse = true;
                }
            }

            // Skip collapsed lines, but still process the starting line of a collapse
            // Needs to be done before checking for the collapse end
            bool skipLine = activeCollapses.Any() && !startedCollapse;

            // Create foldings for lines with the same amount of #'s (minimum 2)
            if (trimmed.StartsWith("##")) {
                int depth = 0;
                for (int i = 2; i < trimmed.Length; i++) {
                    if (trimmed[i] == '#') {
                        depth++;
                        continue;
                    }
                    break;
                }

                if (activeFoldings.Remove(depth, out int startRow)) {
                    // Find begging of text
                    var startLine = Document.Lines[startRow];
                    int startIdx = 0;
                    for (; startIdx < startLine.Length; startIdx++) {
                        char c = startLine[startIdx];
                        if (c != '#' && !char.IsWhiteSpace(c)) {
                            break;
                        }
                    }

                    // End the previous folding and start a new one if the end comment isn't empty
                    bool isStartStop = !string.IsNullOrWhiteSpace(trimmed[(depth + 2)..]);

                    foldings.Add(new Folding {
                        MinRow = startRow, MaxRow = row - (isStartStop ? 1 : 0),
                        StartCol = startIdx,
                    });

                    activeCollapses.Remove(startRow);

                    if (isStartStop) {
                        activeFoldings[depth] = row;
                        skipLine = false;
                    }
                } else {
                    activeFoldings[depth] = row;
                }
            }

            if (skipLine) {
                actualToVisualRows[row] = Math.Max(0, visualRow - 1);
                continue;
            } else {
                actualToVisualRows[row] = visualRow;
            }

            GenerateLineLinks(row);

            // Wrap comments into multiple lines when hitting the left edge
            if (Settings.Instance.WordWrapComments && trimmed.StartsWith("#")) {
                var wrappedLines = new List<WrapLine>();

                const int charPadding = 1;
                float charWidth = (scrollableSize.Width - textOffsetX) / Font.CharWidth() - 1 - charPadding; // -1 because we overshoot by 1 while iterating

                int idx = 0;
                int startOffset = -1;
                while (idx < line.Length) {
                    int subIdx = 0;
                    int startIdx = idx;
                    int endIdx = -1;
                    for (; idx < line.Length; idx++, subIdx++) {
                        char c = line[idx];

                        // Skip first #'s and whitespace
                        if (startOffset == -1) {
                            if (c == '#' || char.IsWhiteSpace(c)) {
                                continue;
                            }

                            startOffset = idx;
                        }

                        // End the line if we're beyond the width and have reached whitespace
                        if (char.IsWhiteSpace(c)) {
                            endIdx = idx;
                        }
                        if (idx == line.Length - 1) {
                            endIdx = line.Length;
                        }

                        if (endIdx != -1 && (startIdx == 0 && subIdx >= charWidth ||
                                             startIdx != 0 && subIdx + startOffset >= charWidth))
                        {
                            break;
                        }
                    }

                    // The comment only contains #'s and whitespace. Abort wrapping
                    if (endIdx == -1) {
                        wrappedLines = [(line, 0)];
                        break;
                    }

                    if (idx != line.Length) {
                        // Snap index back to line break
                        idx = endIdx + 1;
                    }

                    var subLine = line[startIdx..endIdx];
                    wrappedLines.Add((subLine, startIdx));
                }

                // Only store actual wraps to improve performance
                if (wrappedLines.Count > 1) {
                    commentLineWraps.Add(row, (startOffset, wrappedLines.ToArray()));
                    width = wrappedLines.Aggregate(width, (widthAccum, wrap) => {
                        int xIdent = wrap.Index == 0 ? 0 : startOffset;
                        float lineWidth = Font.CharWidth() * (xIdent + wrap.Line.Length);
                        return Math.Max(widthAccum, lineWidth);
                    });
                    height += Font.LineHeight() * wrappedLines.Count;

                    visualRow += wrappedLines.Count;
                    for (int i = 0; i < wrappedLines.Count; i++) {
                        visualToActualRows.Add(row);
                    }

                    continue;
                }
            }

            width = Math.Max(width, Font.MeasureWidth(line));
            height += Font.LineHeight();

            visualRow += 1;
            visualToActualRows.Add(row);
        }

        // Clear invalid foldings
        Document.RemoveAnchorsIf(anchor => anchor.UserData is CollapseAnchorData && foldings.All(fold => fold.MinRow != anchor.Row));


        // Calculate line numbers width
        const float foldButtonPadding = 5.0f;
        bool hasFoldings = Settings.Instance.ShowFoldIndicators && foldings.Count != 0;
        int visibleDigits = VisibleLineNumberDigits;

        // Only when the alignment is to the left, the folding indicator can fit into the existing space
        float foldingWidth = !hasFoldings ? 0.0f : Settings.Instance.LineNumberAlignment switch {
             LineNumberAlignment.Left => Font.CharWidth() * (foldings[^1].MinRow.Digits() + 1) + foldButtonPadding,
             LineNumberAlignment.Right => Font.CharWidth() * (visibleDigits + 1) + foldButtonPadding,
             _ => throw new UnreachableException(),
        };
        textOffsetX = Math.Max(foldingWidth, Font.CharWidth() * visibleDigits) + LineNumberPadding * 3.0f;

        const float paddingRight = 50.0f;
        const float paddingBottom = 100.0f;

        // Apparently you need to set the size from the parent on WPF?
        if (Eto.Platform.Instance.IsWpf) {
            scrollable.ScrollSize = new((int)(width + textOffsetX + paddingRight), (int)(height + paddingBottom));
        } else {
            Size = new((int)(width + textOffsetX + paddingRight), (int)(height + paddingBottom));
        }

        // Update popup-menu position
        if (ActivePopupMenu != null) {
            const int menuXOffset = 8;
            const int menuYOffset = 7;
            const int menuLPadding = 7;
            const int menuRPadding = 20;

            float carX = Font.CharWidth() * Document.Caret.Col;
            float carY = Font.LineHeight() * (actualToVisualRows[Document.Caret.Row] + 1);

            int menuX, menuY;

            void UpdateMenuH() {
                menuX = (int)(carX + scrollablePosition.X + textOffsetX + menuXOffset);
                int menuMaxRight = scrollablePosition.X + scrollableSize.Width - menuRPadding - (ActivePopupMenu.VScrollBarVisible ? Studio.ScrollBarSize : 0);
                int menuMaxW = menuMaxRight - menuX;

                // Try moving the menu to the left when there isn't enough space, before having to shrink it
                if (menuMaxW < ActivePopupMenu.ContentWidth) {
                    menuX = (int)Math.Max(menuMaxRight - ActivePopupMenu.ContentWidth, scrollablePosition.X + textOffsetX + menuLPadding);
                    menuMaxW = menuMaxRight - menuX;
                }

                ActivePopupMenu.ContentWidth = Math.Min(ActivePopupMenu.ContentWidth, menuMaxW);
            }
            void UpdateMenuV() {
                int menuYBelow = (int)(carY + menuYOffset);
                int menuYAbove = (int)Math.Max(carY - Font.LineHeight() - menuYOffset - ActivePopupMenu.ContentHeight, scrollablePosition.Y + menuYOffset);

                int menuMaxHBelow = (int)(scrollablePosition.Y + scrollableSize.Height - Font.LineHeight() - menuYBelow) - (ActivePopupMenu.HScrollBarVisible ? Studio.ScrollBarSize : 0);
                int menuMaxHAbove = (int)(scrollablePosition.Y + carY - Font.LineHeight() - menuYOffset - menuYAbove);

                // Chose above / below caret depending on which provides more height. Default to below
                int menuMaxH;
                if (Math.Min(ActivePopupMenu.ContentHeight, menuMaxHBelow) >= Math.Min(ActivePopupMenu.ContentHeight, menuMaxHAbove)) {
                    menuY = menuYBelow;
                    menuMaxH = menuMaxHBelow;
                } else {
                    menuY = menuYAbove;
                    menuMaxH = menuMaxHAbove;
                }

                ActivePopupMenu.ContentHeight = Math.Min(ActivePopupMenu.ContentHeight, menuMaxH);
            }

            // Both depend on each-other, so one needs to be updated twice
            UpdateMenuH();
            UpdateMenuV();
            UpdateMenuH();

            ActivePopupMenu.Recalc();
            pixelLayout.Move(ActivePopupMenu, menuX, menuY);
        }

        Invalidate();
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

    private void AdjustFrameCounts(int rowA, int rowB, int dir) {
        int topRow = Math.Min(rowA, rowB);
        int bottomRow = Math.Max(rowA, rowB);

        var topLine = ActionLine.Parse(Document.Lines[topRow]);
        var bottomLine = ActionLine.Parse(Document.Lines[bottomRow]);

        if (topLine == null && bottomLine == null || dir == 0)
            return;

        using (Document.Update()) {
            // Adjust single line
            if (topRow == bottomRow ||
                topLine == null && bottomLine != null ||
                bottomLine == null && topLine != null)
            {
                var line = topLine ?? bottomLine!.Value;
                int row = topLine != null ? topRow : bottomRow;

                if (dir > 0) {
                    Document.ReplaceLine(row, (line with { FrameCount = Math.Min(line.FrameCount + 1, ActionLine.MaxFrames) }).ToString());
                } else {
                    Document.ReplaceLine(row, (line with { FrameCount = Math.Max(line.FrameCount - 1, 0) }).ToString());
                }
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

    private CaretPosition GetVisualPosition(CaretPosition position) {
        if (!commentLineWraps.TryGetValue(position.Row, out var wrap))
            return new CaretPosition(actualToVisualRows[position.Row], position.Col);

        // TODO: Maybe don't use LINQ here for performance?
        var (line, lineIdx) = wrap.Lines
            .Select((line, idx) => (line, idx))
            .Reverse()
            .FirstOrDefault(line => line.line.Index <= position.Col);

        int xIdent = lineIdx == 0 ? 0 : wrap.StartOffset;

        return new CaretPosition(
            actualToVisualRows[position.Row] + lineIdx,
            position.Col - line.Index + xIdent);
    }
    private CaretPosition GetActualPosition(CaretPosition position) {
        if (position.Row < 0) {
            return new CaretPosition(0, 0);
        }
        if (position.Row >= visualToActualRows.Count) {
            int actualRow = visualToActualRows[^1];
            int lineLength = Document.Lines[actualRow].Length;
            return new CaretPosition(actualRow, lineLength);
        }

        int row = GetActualRow(position.Row);

        int col = position.Col;
        if (commentLineWraps.TryGetValue(row, out var wrap)) {
            int idx = position.Row - actualToVisualRows[row];
            if (idx >= 0 && idx < wrap.Lines.Length) {
                int xIdent = idx == 0 ? 0 : wrap.StartOffset;
                var line = wrap.Lines[idx];

                col = Math.Clamp(col, xIdent, xIdent + line.Line.Length);
                col += line.Index - xIdent;
            }
        }

        return new CaretPosition(row, col);
    }

    private int GetActualRow(int visualRow, int? defaultRow = null) {
        if (visualRow < 0) {
            return defaultRow ?? 0;
        }
        if (visualRow >= visualToActualRows.Count) {
            return defaultRow ?? visualToActualRows[^1];
        }

        return visualToActualRows[visualRow];
    }

    private string GetVisualLine(int visualRow) {
        int row = GetActualRow(visualRow);

        if (commentLineWraps.TryGetValue(row, out var wrap)) {
            int idx = visualRow - actualToVisualRows[row];
            if (idx == 0) {
                return wrap.Lines[idx].Line;
            } else {
                return $"{new string(' ', wrap.StartOffset)}{wrap.Lines[idx].Line}";
            }
        }

        return Document.Lines[row];
    }

    #endregion

    protected override void OnKeyDown(KeyEventArgs e) {
        var mods = e.Modifiers;
        if (e.Key is Keys.LeftShift or Keys.RightShift) mods |= Keys.Shift;
        if (e.Key is Keys.LeftControl or Keys.RightControl) mods |= Keys.Control;
        if (e.Key is Keys.LeftAlt or Keys.RightAlt) mods |= Keys.Alt;
        if (e.Key is Keys.LeftApplication or Keys.RightApplication) mods |= Keys.Application;
        UpdateMouseCursor(PointFromScreen(Mouse.Position), mods);

        string lineTrimmed = Document.Lines[Document.Caret.Row].TrimStart();

        // Send inputs to Celeste if applicable
        bool isActionLine = lineTrimmed.StartsWith("***") ||
                            ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _ );
        bool isComment = lineTrimmed.StartsWith('#');
        bool isTyping = (DateTime.UtcNow - lastModification).TotalSeconds < Settings.Instance.SendInputsTypingTimeout;
        bool sendInputs =
            (Settings.Instance.SendInputsOnActionLines && isActionLine) ||
            (Settings.Instance.SendInputsOnComments && isComment) ||
            (Settings.Instance.SendInputsOnCommands && !isActionLine && !isComment);

        if (Settings.Instance.SendInputsToCeleste && CommunicationWrapper.Connected && !isTyping && sendInputs && CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
            e.Handled = true;
            return;
        }

        // Prevent editing file on accident
        if (Settings.Instance.SendInputsToCeleste && CommunicationWrapper.PlaybackRunning && Settings.Instance.SendInputsDisableWhileRunning) {
            e.Handled = true;
            return;
        }

        // While there are quick-edits available, Tab will cycle through them
        // Using tab doesn't feel "right" for the context actions menu
        if (ActivePopupMenu?.HandleKeyDown(e, useTabComplete: ActivePopupMenu == autoCompleteMenu && !GetQuickEdits().Any()) ?? false) {
            e.Handled = true;
            Recalc();
            return;
        }

        if (GetQuickEdits().Any()) {
            // Cycle
            if (e.Key == Keys.Tab) {
                if (e.Shift) {
                    SelectPrevQuickEdit();
                } else {
                    SelectNextQuickEdit();
                }

                // Don't start a new base auto-complete. Only arguments
                if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                    UpdateAutoComplete();
                } else {
                    CloseAutoCompletePopup();
                }

                e.Handled = true;
                Recalc();
                return;
            }
            // Cancel
            if (e.Key == Keys.Escape) {
                ClearQuickEdits();
                Document.Selection.Clear();

                e.Handled = true;
                Recalc();
                return;
            }
            // Finish + Go to end
            if (e.Key == Keys.Enter) {
                SelectQuickEditIndex(GetQuickEdits().Count() - 1);
                ClearQuickEdits();
                Document.Caret = Document.Selection.Max;
                Document.Selection.Clear();

                e.Handled = true;
                Recalc();
                return;
            }
        }

        if (calculationState != null) {
            CalculationHandleKey(e);
            Invalidate();
            return;
        }

        // Forward hotkeys from menu entries / snippets
        if (e.Key != Keys.None) {
            // Check for menu items
            var items = ContextMenu.Items
                .Concat(Studio.Instance.GameInfo.ContextMenu.Items)
                .Concat(Studio.Instance.Menu.Items)
                .Concat(Studio.Instance.GlobalHotkeys);
            foreach (var item in items) {
                if (item.Shortcut != e.KeyData) {
                    continue;
                }

                item.PerformClick();
                Recalc();
                e.Handled = true;
                return;
            }

            // Handle context actions
            foreach (var contextAction in contextActions) {
                if (contextAction.Entry.GetHotkey() == e.KeyData && contextAction.Check() is { } action) {
                    action.OnUse();
                    Recalc();
                    ScrollCaretIntoView();

                    e.Handled = true;
                    return;
                }
            }

            // Try to paste snippets
            foreach (var snippet in Settings.Instance.Snippets) {
                if (snippet.Enabled && snippet.Hotkey == e.KeyData) {
                    InsertQuickEdit(snippet.Insert);
                    Recalc();
                    ScrollCaretIntoView();

                    e.Handled = true;
                    return;
                }
            }
        }

        switch (e.Key) {
            case Keys.Backspace:
                OnDelete(e.HasCommonModifier() ? CaretMovementType.WordLeft : CaretMovementType.CharLeft);
                e.Handled = true;
                break;
            case Keys.Delete:
                OnDelete(e.HasCommonModifier() ? CaretMovementType.WordRight : CaretMovementType.CharRight);
                e.Handled = true;
                break;
            case Keys.Enter:
                OnEnter(e.HasCommonModifier(), up: e.Shift);
                e.Handled = true;
                break;
            case Keys.Left when !e.HasAlternateModifier(): // Prevent Alt+Left from getting handled
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.WordLeft : CaretMovementType.CharLeft, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.WordRight : CaretMovementType.CharRight, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Up:
                if (e.HasCommonModifier() && e.Shift) {
                    // Adjust frame count
                    if (Document.Selection.Empty) {
                        AdjustFrameCounts(Document.Caret.Row, Document.Caret.Row, 1);
                    } else {
                        AdjustFrameCounts(Document.Selection.Start.Row, Document.Selection.End.Row, 1);
                    }
                } else if (e.HasAlternateModifier()) {
                    // Move lines
                    using (Document.Update()) {
                        if (Document.Caret.Row > 0 && Document.Selection is { Empty: false, Min.Row: > 0 }) {
                            var line = Document.Lines[Document.Selection.Min.Row - 1];
                            Document.RemoveLine(Document.Selection.Min.Row - 1);
                            Document.InsertLine(Document.Selection.Max.Row, line);

                            Document.Selection.Start.Row--;
                            Document.Selection.End.Row--;
                            Document.Caret.Row--;
                        } else if (Document.Caret.Row > 0 && Document.Selection.Empty) {
                            Document.SwapLines(Document.Caret.Row, Document.Caret.Row - 1);
                            Document.Caret.Row--;
                        }
                    }
                } else {
                    MoveCaret(e.HasCommonModifier() ? CaretMovementType.LabelUp : CaretMovementType.LineUp, updateSelection: e.Shift);
                }

                e.Handled = true;
                break;
            case Keys.Down:
                if (e.HasCommonModifier() && e.Shift) {
                    // Adjust frame count
                    if (Document.Selection.Empty) {
                        AdjustFrameCounts(Document.Caret.Row, Document.Caret.Row, -1);
                    } else {
                        AdjustFrameCounts(Document.Selection.Start.Row, Document.Selection.End.Row, -1);
                    }
                } else if (e.HasAlternateModifier()) {
                    // Move lines
                    using (Document.Update()) {
                        if (Document.Caret.Row < Document.Lines.Count - 1 && !Document.Selection.Empty && Document.Selection.Max.Row < Document.Lines.Count - 1) {
                            var line = Document.Lines[Document.Selection.Max.Row + 1];
                            Document.RemoveLine(Document.Selection.Max.Row + 1);
                            Document.InsertLine(Document.Selection.Min.Row, line);

                            Document.Selection.Start.Row++;
                            Document.Selection.End.Row++;
                        } else if (Document.Caret.Row < Document.Lines.Count - 1 && Document.Selection.Empty) {
                            Document.SwapLines(Document.Caret.Row, Document.Caret.Row + 1);
                            Document.Caret.Row++;
                        }
                    }
                } else {
                    MoveCaret(e.HasCommonModifier() ? CaretMovementType.LabelDown : CaretMovementType.LineDown, updateSelection: e.Shift);
                }

                e.Handled = true;
                break;
            case Keys.PageUp:
                MoveCaret(CaretMovementType.PageUp, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.PageDown:
                MoveCaret(CaretMovementType.PageDown, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.DocumentStart : CaretMovementType.LineStart, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.End:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.DocumentEnd : CaretMovementType.LineEnd, updateSelection: e.Shift);
                e.Handled = true;
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
                if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _) && CalculationExtensions.TryParse(e.KeyChar) is { } op) {
                    StartCalculation(op);
                    e.Handled = true;
                }

                // macOS will make a beep sounds when the event isn't handled
                // ..that also means OnTextInput won't be called..
                if (Eto.Platform.Instance.IsMac) {
                    e.Handled = true;
                    if (e.KeyChar != char.MaxValue) {
                        OnTextInput(new TextInputEventArgs(e.KeyChar.ToString()));
                    }
                } else {
                    base.OnKeyDown(e);
                }

                break;
        }

        // If nothing handled this, and it's not a character, send it anyway
        if (Settings.Instance.SendInputsToCeleste && Settings.Instance.SendInputsNonWritable && !Platform.IsWpf && CommunicationWrapper.Connected && !isTyping && !sendInputs && !e.Handled && e.KeyChar == char.MaxValue && CommunicationWrapper.SendKeyEvent(e.Key, e.Modifiers, released: false)) {
            e.Handled = true;
            return;
        }

        Recalc();
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
        bool isTyping = (DateTime.UtcNow - lastModification).TotalSeconds < Settings.Instance.SendInputsTypingTimeout;
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

    #region Action Line Calculation

    private void StartCalculation(CalculationOperator op) {
        calculationState = new CalculationState(op, Document.Caret.Row);
    }

    private void CalculationHandleKey(KeyEventArgs e) {
        if (calculationState == null)
            return;

        switch (e.Key)
        {
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
            case >= Keys.D0 and <= Keys.D9 when !e.Shift:
            {
                var num = e.Key - Keys.D0;
                calculationState.Operand += num;
                e.Handled = true;
                return;
            }
        }

        if (CalculationExtensions.TryParse(e.KeyChar) is { } op) {
            // Cancel with same operation again
            if (op == calculationState.Operator && calculationState.Operand.Length == 0) {
                calculationState = null;
                e.Handled = true;
                return;
            }

            CommitCalculation();
            StartCalculation(op);
            e.Handled = true;
        } else {
            // Allow A-Z to be handled by the action line editing
            if (e.Key is >= Keys.A and <= Keys.Z) {
                calculationState = null;
                e.Handled = false;
                return;
            }

            e.Handled = false;
        }
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
            int stealFromRow = calculationState.Row + stealFrom;
            if (stealFromRow >= 0 && stealFromRow < Document.Lines.Count && ActionLine.TryParse(Document.Lines[stealFromRow], out var stealFromActionLine)) {
                int frameDelta = newActionLine.FrameCount - actionLine.FrameCount;

                Document.ReplaceLine(stealFromRow, (stealFromActionLine with {
                    FrameCount = Math.Clamp(stealFromActionLine.FrameCount - frameDelta, 0, ActionLine.MaxFrames)
                }).ToString());
            }
        }
    }

    #endregion

    // Helper methods to only close a specific menu type and not close an unrelated menu
    private void CloseAutoCompletePopup() {
        if (ActivePopupMenu == autoCompleteMenu) {
            ActivePopupMenu = null;
            lastAutoCompleteArgumentIndex = -1;
        }
    }
    private void CloseContextActionsPopup() {
        if (ActivePopupMenu == contextActionsMenu) {
            ActivePopupMenu = null;
        }
    }

    #region Auto Complete

    private void GenerateBaseAutoCompleteEntries() {
        baseAutoCompleteEntries.Clear();

        // Snippets
        foreach (var snippet in Settings.Instance.Snippets) {
            if (!string.IsNullOrWhiteSpace(snippet.Shortcut) && snippet.Enabled &&
                CreateEntry(snippet.Shortcut, snippet.Insert, "Snippet", hasArguments: false) is { } entry)
            {
                baseAutoCompleteEntries.Add(entry);
            }
        }

        // Commands
        foreach (string? commandName in CommandInfo.CommandOrder) {
            if (commandName != null && CommunicationWrapper.Commands.FirstOrDefault(cmd => cmd.Name == commandName) is var command && !string.IsNullOrEmpty(command.Name) &&
                CreateEntry(command.Name, command.Insert.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText), "Command", command.HasArguments) is { } entry)
            {
                baseAutoCompleteEntries.Add(entry);
            }
        }
        foreach (var command in CommunicationWrapper.Commands) {
            if (!CommandInfo.CommandOrder.Contains(command.Name) && !CommandInfo.HiddenCommands.Contains(command.Name) &&
                CreateEntry(command.Name, command.Insert.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText), "Command", command.HasArguments) is { } entry)
            {
                baseAutoCompleteEntries.Add(entry);
            }
        }

        return;

        PopupMenu.Entry? CreateEntry(string name, string insert, string extra, bool hasArguments) {
            if (CreateQuickEditAction(insert, hasArguments) is not { } action) {
                return null;
            }

            return new PopupMenu.Entry {
                SearchText = name,
                DisplayText = name,
                ExtraText = extra,
                OnUse = action,
            };
        }
    }

    /// Creates an action, which will insert the quick edit when invoked
    private Action? CreateQuickEditAction(string insert, bool hasArguments) {
        var quickEdit = QuickEdit.Parse(insert);
        if (quickEdit == null) {
            return null;
        }

        return () => {
            var oldCaret = Document.Caret;

            using var __ = Document.Update();
            Document.ReplaceLine(oldCaret.Row, quickEdit.Value.ActualText);

            ClearQuickEdits();

            if (quickEdit.Value.Selections.Length > 0) {
                for (int i = 0; i < quickEdit.Value.Selections.Length; i++) {
                    var selection = quickEdit.Value.Selections[i];
                    var defaultText = quickEdit.Value.ActualText.SplitDocumentLines()[selection.Min.Row][selection.Min.Col..selection.Max.Col];

                    // Quick-edit selections are relative, not absolute
                    Document.AddAnchor(new Anchor {
                        Row = selection.Min.Row + oldCaret.Row,
                        MinCol = selection.Min.Col, MaxCol = selection.Max.Col,
                        UserData = new QuickEditAnchorData { Index = i, DefaultText = defaultText },
                        OnRemoved = ClearQuickEdits,
                    });
                }
                SelectQuickEditIndex(0);
            } else {
                desiredVisualCol = Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
            }

            if (hasArguments) {
                // Keep open for arguments (but not a new base auto-complete)
                if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                    UpdateAutoComplete();
                } else {
                    CloseAutoCompletePopup();
                }
            } else {
                ActivePopupMenu = null;
            }
        };
    }

    private void UpdateAutoComplete(bool open = true) {
        string line = Document.Lines[Document.Caret.Row];
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, line.Length);

        // Ignore text to the right of the caret to auto-completion
        line = line[..Document.Caret.Col].TrimStart();

        // Don't auto-complete on comments or action lines
        if (line.StartsWith('#') || line.StartsWith('*') || ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            CloseAutoCompletePopup();
            return;
        }

        if (open) {
            ActivePopupMenu = autoCompleteMenu;
        }
        if (ActivePopupMenu == null) {
            lastAutoCompleteArgumentIndex = -1;
            return;
        }

        // Use auto-complete entries for current command
        var commandLine = CommandLine.Parse(line);
        var fullCommandLine = CommandLine.Parse(Document.Lines[Document.Caret.Row]);

        if (commandLine == null || commandLine.Value.Arguments.Length == 0 ||
            fullCommandLine == null || fullCommandLine.Value.Arguments.Length == 0)
        {
            autoCompleteMenu.Entries.Clear();
            autoCompleteMenu.Entries.AddRange(baseAutoCompleteEntries);
            autoCompleteMenu.Filter = line;
            lastAutoCompleteArgumentIndex = -1;
        } else {
            var command = CommunicationWrapper.Commands.FirstOrDefault(cmd => string.Equals(cmd.Name, commandLine.Value.Command, StringComparison.OrdinalIgnoreCase));

            var loadingEntry = new PopupMenu.Entry {
                DisplayText = string.Empty,
                SearchText = string.Empty,
                ExtraText = string.Empty,
                OnUse = null!,
                Disabled = true,
            };

            if (!string.IsNullOrEmpty(command.Name) && command.HasArguments) {
                var lastArgRegion = commandLine.Value.Regions[^1];

                commandAutoCompleteTokenSource?.Cancel();
                commandAutoCompleteTokenSource?.Dispose();
                commandAutoCompleteTokenSource = new CancellationTokenSource();

                // Don't clear on same argument to prevent flashing "Loading..."
                if (lastAutoCompleteArgumentIndex != commandLine.Value.Arguments.Length) {
                    autoCompleteMenu.Entries.Clear();
                    autoCompleteMenu.Entries.Add(loadingEntry);
                    autoCompleteMenu.Recalc();
                    Recalc();
                }
                lastAutoCompleteArgumentIndex = commandLine.Value.Arguments.Length;

                var token = commandAutoCompleteTokenSource.Token;
                Task.Run(async () => {
                    int loadingDots = 0;

                    while (!token.IsCancellationRequested && ActivePopupMenu == autoCompleteMenu && await Application.Instance.InvokeAsync(() => ActivePopupMenu.Visible).ConfigureAwait(false)) {
                        if (!CommunicationWrapper.Connected) {
                            loadingEntry.DisplayText = "Connection with Celeste required for command auto-complete!";
                            await Application.Instance.InvokeAsync(() => {
                                autoCompleteMenu.Entries.Clear();
                                autoCompleteMenu.Entries.Add(loadingEntry);
                                autoCompleteMenu.Recalc();
                                Recalc();
                            }).ConfigureAwait(false);

                            break;
                        }

                        loadingDots = (loadingDots + 1).Mod(4);
                        loadingEntry.DisplayText = $"Loading{new string('.', loadingDots)}{new string(' ', 3 - loadingDots)}";

                        (var commandEntries, bool done) = await CommunicationWrapper.RequestAutoCompleteEntries(command.Name, commandLine.Value.Arguments, Document.FilePath, Document.Caret.Row).ConfigureAwait(false);

                        var menuEntries = commandEntries.Select(entry => new PopupMenu.Entry {
                            SearchText = entry.FullName,
                            DisplayText = entry.Name,
                            ExtraText = entry.Extra,
                            OnUse = () => {
                                using var __ = Document.Update();

                                string insert = entry.FullName;

                                var selectedQuickEdit = GetQuickEdits()
                                    .FirstOrDefault(anchor => Document.Caret.Row == anchor.Row &&
                                                              Document.Caret.Col >= anchor.MinCol &&
                                                              Document.Caret.Col <= anchor.MaxCol);

                                // Jump to the next parameter and open the auto-complete menu if applicable
                                if (selectedQuickEdit != null) {
                                    // Replace the current quick-edit instead
                                    Document.ReplaceRangeInLine(selectedQuickEdit.Row, selectedQuickEdit.MinCol, selectedQuickEdit.MaxCol, insert);

                                    if (entry.IsDone) {
                                        var quickEdits = GetQuickEdits().ToArray();
                                        bool lastQuickEditSelected = quickEdits.Length != 0 &&
                                                                     quickEdits[^1].Row == Document.Caret.Row &&
                                                                     quickEdits[^1].MinCol <= Document.Caret.Col &&
                                                                     quickEdits[^1].MaxCol >= Document.Caret.Col;

                                        if (lastQuickEditSelected) {
                                            ClearQuickEdits();
                                            Document.Selection.Clear();
                                            Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;

                                            CloseAutoCompletePopup();
                                        } else {
                                            SelectNextQuickEdit();

                                            // Don't start a new base auto-complete. Only arguments
                                            if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                                                UpdateAutoComplete();
                                            } else {
                                                CloseAutoCompletePopup();
                                            }
                                        }
                                    } else {
                                        Document.Selection.Clear();
                                        Document.Caret.Col = selectedQuickEdit.MinCol + insert.Length;

                                        UpdateAutoComplete();
                                    }
                                } else {
                                    if (!entry.IsDone) {
                                        Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert);
                                        Document.Caret.Col = desiredVisualCol = lastArgRegion.StartCol + insert.Length;
                                        Document.Selection.Clear();

                                        UpdateAutoComplete();
                                    } else if (entry.HasNext ?? false/*command.Value.AutoCompleteEntries.Length != allArgs.Length - 1*/) {
                                        // Include separator for next argument
                                        Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert + commandLine.Value.ArgumentSeparator);
                                        Document.Caret.Col = desiredVisualCol = lastArgRegion.StartCol + insert.Length + commandLine.Value.ArgumentSeparator.Length;
                                        Document.Selection.Clear();

                                        UpdateAutoComplete();
                                    } else {
                                        Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert);
                                        Document.Caret.Col = desiredVisualCol = lastArgRegion.StartCol + insert.Length;
                                        Document.Selection.Clear();

                                        CloseAutoCompletePopup();
                                    }
                                }
                            },
                        });

                        await Application.Instance.InvokeAsync(() => {
                            autoCompleteMenu.Entries.Clear();
                            autoCompleteMenu.Entries.AddRange(menuEntries);
                            if (!done) {
                                autoCompleteMenu.Entries.Add(loadingEntry);
                            }

                            autoCompleteMenu.Recalc();
                            Recalc();
                        }).ConfigureAwait(false);

                        if (done) {
                            commandAutoCompleteTokenSource?.Dispose();
                            commandAutoCompleteTokenSource = null;
                            break;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(0.25f), token).ConfigureAwait(false);
                    }
                }, token);
            } else {
                autoCompleteMenu.Entries.Clear();
                autoCompleteMenu.Recalc();
                Recalc();
            }

            if (GetSelectedQuickEdit() is { } quickEdit && commandLine.Value.Arguments[^1] == quickEdit.DefaultText) {
                // Display all entries when quick-edit still contains the default
                autoCompleteMenu.Filter = string.Empty;
            } else {
                autoCompleteMenu.Filter = commandLine.Value.Arguments[^1];
            }
        }
    }

    #endregion

    #region Context Actions

    private void UpdateContextActions() {
        contextActionsMenu.Entries = contextActions
            .Select(contextAction => {
                return contextAction.Check() ?? new PopupMenu.Entry {
                    DisplayText = contextAction.Entry.GetName(),
                    SearchText = contextAction.Entry.GetName(),
                    ExtraText = contextAction.Entry.GetHotkey() != Keys.None ? contextAction.Entry.GetHotkey().ToShortcutString() : string.Empty,
                    Disabled = true,
                    OnUse = () => {},
                };
            })
            .OrderBy(entry => entry.Disabled ? 1 : 0)
            .ToList();

        if (contextActionsMenu.Entries.Count > 0) {
            ActivePopupMenu = contextActionsMenu;
        }
    }

    #endregion

    #region Quick Edit

    /*
     * Quick-edits are anchors to switch through with tab and edit
     * They are used by auto-complete snippets
     */

    private record struct QuickEditAnchorData { public required int Index; public required string DefaultText; }

    private void SelectNextQuickEdit() {
        var quickEdits = GetQuickEdits().ToArray();
        // Sort linearly inside the document
        Array.Sort(quickEdits, (a, b) => a.Row == b.Row
            ? a.MinCol - b.MinCol
            : a.Row - b.Row);

        // Try to goto the next index
        if (GetSelectedQuickEdit() is { } currQuickEdit) {
            SelectQuickEditIndex((currQuickEdit.Index + 1).Mod(quickEdits.Length));
            return;
        }

        // We aren't inside a quick-edit and don't have enough info to goto the next index
        // Therefore just go to the next selection linearly, ignoring the index
        var quickEdit = quickEdits
            .FirstOrDefault(anchor => anchor.Row == Document.Caret.Row && anchor.MinCol > Document.Caret.Col ||
                                      anchor.Row > Document.Caret.Row);

        if (quickEdit == null) {
            // Wrap to first one
            SelectQuickEditIndex(0);
            return;
        }

        if (Document.Caret.Row != quickEdit.Row) {
            FixInvalidInput(Document.Caret.Row);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = desiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    private void SelectPrevQuickEdit() {
        var quickEdits = GetQuickEdits().ToArray();
        // Sort linearly inside the document
        Array.Sort(quickEdits, (a, b) => a.Row == b.Row
            ? a.MinCol - b.MinCol
            : a.Row - b.Row);

        // Try to goto the prev index
        if (GetSelectedQuickEdit() is { } currQuickEdit) {
            SelectQuickEditIndex((currQuickEdit.Index - 1).Mod(quickEdits.Length));
            return;
        }

        // We aren't inside a quick-edit and don't have enough info to goto the prev index
        // Therefore just go to the prev selection linearly, ignoring the index
        var quickEdit = quickEdits
            .Reverse()
            .FirstOrDefault(anchor => anchor.Row == Document.Caret.Row && anchor.MinCol < Document.Caret.Col ||
                                      anchor.Row < Document.Caret.Row);

        if (quickEdit == null) {
            // Wrap to last one
            SelectQuickEditIndex(quickEdits.Length - 1);
            return;
        }

        if (Document.Caret.Row != quickEdit.Row) {
            FixInvalidInput(Document.Caret.Row);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = desiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    private void SelectQuickEditIndex(int index) {
        var quickEdit = Document.FindFirstAnchor(anchor => anchor.UserData is QuickEditAnchorData idx && idx.Index == index);
        if (quickEdit == null) {
            ClearQuickEdits();
            return;
        }

        SelectQuickEdit(quickEdit);
    }
    private void SelectQuickEdit(Anchor quickEdit) {
        if (Document.Caret.Row != quickEdit.Row) {
            FixInvalidInput(Document.Caret.Row);
        }

        Document.Caret.Row = quickEdit.Row;
        Document.Caret.Col = desiredVisualCol = quickEdit.MinCol;
        Document.Selection = new Selection {
            Start = new CaretPosition(quickEdit.Row, quickEdit.MinCol),
            End = new CaretPosition(quickEdit.Row, quickEdit.MaxCol),
        };
    }

    /// Returns the quick-edit which is currently under the caret
    private QuickEditAnchorData? GetSelectedQuickEdit() =>
        GetQuickEdits().FirstOrDefault(anchor => anchor.IsPositionInside(Document.Caret))?.UserData as QuickEditAnchorData?;

    private IEnumerable<Anchor> GetQuickEdits() => Document.FindAnchors(anchor => anchor.UserData is QuickEditAnchorData);
    private void ClearQuickEdits() => Document.RemoveAnchorsIf(anchor => anchor.UserData is QuickEditAnchorData);

    /// Inserts a new quick-edit text at the current row
    private void InsertQuickEdit(string insert) {
        if (QuickEdit.Parse(insert) is not { } quickEdit) {
            return;
        }

        using var __ = Document.Update();

        var oldCaret = Document.Caret;

        if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
            // Create a new empty line for the quick-edit to use
            CollapseSelection();

            if (Settings.Instance.InsertDirection == InsertDirection.Above) {
                Document.InsertLineAbove(string.Empty);
                Document.Caret.Row--;
                oldCaret.Row++;
            } else if (Settings.Instance.InsertDirection == InsertDirection.Below) {
                Document.InsertLineBelow(string.Empty);
                Document.Caret.Row++;
            }
        }

        int row = Document.Caret.Row;
        Document.ReplaceLine(row, quickEdit.ActualText);

        if (oldCaret.Row != Document.Caret.Row) {
            FixInvalidInput(oldCaret.Row);
        }

        if (quickEdit.Selections.Length > 0) {
            for (int i = 0; i < quickEdit.Selections.Length; i++) {
                var selection = quickEdit.Selections[i];
                var defaultText = quickEdit.ActualText.SplitDocumentLines()[selection.Min.Row][selection.Min.Col..selection.Max.Col];

                // Quick-edit selections are relative, not absolute
                Document.AddAnchor(new Anchor {
                    Row = selection.Min.Row + row,
                    MinCol = selection.Min.Col, MaxCol = selection.Max.Col,
                    UserData = new QuickEditAnchorData { Index = i, DefaultText = defaultText },
                    OnRemoved = ClearQuickEdits,
                });
            }
            SelectQuickEditIndex(0);
        } else if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
            int newLines = quickEdit.ActualText.Count(c => c == Document.NewLine);
            Document.Caret.Row = row + newLines;
            Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
        } else {
            Document.Caret = oldCaret;
        }
    }

    #endregion

    #region Folding

    private struct Folding {
        public int MinRow, MaxRow;

        public int StartCol;
    }
    private struct CollapseAnchorData;

    private void ToggleCollapse(int row) {
        if (Document.FindFirstAnchor(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData) == null) {
            Document.AddAnchor(new Anchor {
                MinCol = 0, MaxCol = Document.Lines[row].Length,
                Row = row,
                UserData = new CollapseAnchorData()
            });

            if (foldings.FirstOrDefault(f => f.MinRow == row) is var fold) {
                // Keep caret outside collapse
                if (Document.Caret.Row >= fold.MinRow && Document.Caret.Row <= fold.MaxRow) {
                    Document.Caret.Row = fold.MinRow;
                    Document.Caret = ClampCaret(Document.Caret);
                }

                // Clear selection if it's inside the collapse
                if (!Document.Selection.Empty) {
                    bool minInside = Document.Selection.Min.Row >= fold.MinRow && Document.Selection.Min.Row <= fold.MaxRow;
                    bool maxInside = Document.Selection.Max.Row >= fold.MinRow && Document.Selection.Max.Row <= fold.MaxRow;

                    if (minInside && maxInside) {
                        Document.Selection.Clear();
                        Document.Caret.Row = fold.MinRow;
                        Document.Caret = ClampCaret(Document.Caret);
                    } else if (minInside) {
                        Document.Caret = ClampCaret(Document.Selection.Max);
                        Document.Selection.Clear();
                    } else if (maxInside) {
                        Document.Caret = ClampCaret(Document.Selection.Min);
                        Document.Selection.Clear();
                    }
                }
            }
        } else {
            Document.RemoveAnchorsIf(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData);
        }
    }
    private void SetCollapse(int row, bool collapse) {
        if (collapse && Document.FindFirstAnchor(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData) == null) {
            Document.AddAnchor(new Anchor {
                MinCol = 0, MaxCol = Document.Lines[row].Length,
                Row = row,
                UserData = new CollapseAnchorData()
            });

            if (foldings.FirstOrDefault(f => f.MinRow == row) is var fold) {
                // Keep caret outside collapse
                if (Document.Caret.Row >= fold.MinRow && Document.Caret.Row <= fold.MaxRow) {
                    Document.Caret.Row = fold.MinRow;
                    Document.Caret = ClampCaret(Document.Caret);
                }

                // Clear selection if it's inside the collapse
                if (!Document.Selection.Empty) {
                    bool minInside = Document.Selection.Min.Row >= fold.MinRow && Document.Selection.Min.Row <= fold.MaxRow;
                    bool maxInside = Document.Selection.Min.Row >= fold.MinRow && Document.Selection.Min.Row <= fold.MaxRow;

                    if (minInside && maxInside) {
                        Document.Selection.Clear();
                        Document.Caret.Row = fold.MinRow;
                        Document.Caret = ClampCaret(Document.Caret);
                    } else if (minInside) {
                        Document.Caret = ClampCaret(Document.Selection.Max);
                        Document.Selection.Clear();
                    } else if (maxInside) {
                        Document.Caret = ClampCaret(Document.Selection.Min);
                        Document.Selection.Clear();
                    }
                }
            }
        } else if (!collapse) {
            Document.RemoveAnchorsIf(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData);
        }
    }
    private Folding? GetCollapse(int row) {
        if (Document.FindFirstAnchor(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData) == null) {
            return null;
        }

        var folding = foldings.FirstOrDefault(fold => fold.MinRow == row);
        if (folding.MinRow == folding.MaxRow) {
            return null;
        }

        return folding;
    }

    #endregion

    #region Line Links

    public record struct LineLinkAnchorData {
        public required MenuEntry? Entry;
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
                Entry = MenuEntry.ContextActions_OpenReadFile,
                OnUse = () => OpenFile(startLabelRow),
            }
        });

        if (endLabelRow.HasValue) {
            Document.AddAnchor(new Anchor {
                Row = row,
                MinCol = startLinkLength + commandLine.ArgumentSeparator.Length + 1,
                MaxCol = startLinkLength + commandLine.ArgumentSeparator.Length + commandLine.Arguments[2].Length,
                UserData = new LineLinkAnchorData {
                    Entry = null,
                    OnUse = () => OpenFile(endLabelRow),
                }
            });
        }

        return;

        void OpenFile(int? targetRow) {
            Studio.Instance.OpenFile(fullPath);
            if (targetRow is {} caretRow) {
                Document.Caret.Row = caretRow;
                Document.Caret.Col = desiredVisualCol = Document.Lines[caretRow].Length;
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
                Entry = MenuEntry.ContextActions_GoToPlayLine,
                OnUse = () => {
                    Document.Caret.Row = labelRow;
                    Document.Caret.Col = desiredVisualCol = Document.Lines[labelRow].Length;
                    Recalc();
                    ScrollCaretIntoView(center: true);
                },
            }
        });
    }

    #endregion

    #region Editing Actions

    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0])) {
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
        }

        char typedCharacter = char.ToUpper(e.Text[0]);

        var oldCaret = Document.Caret;

        // If it's an action line, handle it ourselves
        if (TryParseAndFormatActionLine(Document.Caret.Row, out actionLine) && e.Text.Length == 1) {
            ClearQuickEdits();

            if (CalculationExtensions.TryParse(typedCharacter) is { } op) {
                if (calculationState != null) {
                    // Cancel with same operation again
                    if (op == calculationState.Operator && calculationState.Operand.Length == 0) {
                        calculationState = null;
                        Invalidate();
                        return;
                    }

                    CommitCalculation();
                }

                StartCalculation(op);
                Invalidate();
                return;
            }

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

        desiredVisualCol = Document.Caret.Col;

        UpdateAutoComplete();
    }

    private void OnDelete(CaretMovementType direction) {
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
                caret.Col < ActionLine.MaxFramesDigits) {
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
                        desiredVisualCol = Document.Caret.Col;
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
        } else {
            Document.Caret = GetNewTextCaretPosition(direction);

            if (caret.Row == Document.Caret.Row) {
                Document.RemoveRangeInLine(caret.Row, caret.Col, Document.Caret.Col);
                Document.Caret.Col = Math.Min(Document.Caret.Col, caret.Col);
                Document.Caret = ClampCaret(Document.Caret, wrapLine: true);

                UpdateAutoComplete(open: false);
            } else {
                var min = Document.Caret < caret ? Document.Caret : caret;
                var max = Document.Caret < caret ? caret : Document.Caret;

                RemoveRange(min, max);
                Document.Caret = min;

                ActivePopupMenu = null;
            }
        }

        desiredVisualCol = Document.Caret.Col;
    }

    private void OnEnter(bool splitLines, bool up) {
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
                Document.Caret.Col = desiredVisualCol = 0;
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
                Document.Caret.Col = desiredVisualCol = prefix.Length + (up ? beforeCaret.Length : 0);
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
            Document.Caret.Col = desiredVisualCol = Math.Max(Document.Caret.Col, prefix.Length);
        }

        Document.Selection.Clear();
    }

    private void OnUndo() {
        Document.Selection.Clear();
        Document.Undo();
    }

    private void OnRedo() {
        Document.Selection.Clear();
        Document.Redo();
    }

    private void OnCut() {
        using var __ = Document.Update();

        OnCopy();

        if (Document.Selection.Empty) {
            Document.RemoveLine(Document.Caret.Row);
        } else if (Document.Selection.Min.Col == 0 && Document.Selection.Max.Col == Document.Lines[Document.Selection.Max.Row].Length) {
            // Remove the lines entirely when the selection is the full range
            Document.RemoveLines(Document.Selection.Min.Row, Document.Selection.Max.Row);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        } else {
            OnDelete(CaretMovementType.None);
        }
    }

    private void OnCopy() {
        if (Document.Selection.Empty) {
            // Just copy entire line
            Clipboard.Instance.Clear();
            Clipboard.Instance.Text = Document.Lines[Document.Caret.Row] + Document.NewLine;
        } else {
            Clipboard.Instance.Clear();
            Clipboard.Instance.Text = Document.GetSelectedText();
        }
    }

    private void OnPaste() {
        if (!Clipboard.Instance.ContainsText)
            return;

        using var __ = Document.Update();

        if (!Document.Selection.Empty) {
            RemoveRange(Document.Selection.Min, Document.Selection.Max);
            Document.Caret = Document.Selection.Min;
            Document.Selection.Clear();
        }

        var clipboardText = Clipboard.Instance.Text.ReplaceLineEndings(Document.NewLine.ToString());

        // Prevent splitting the action-line in half or inserting garbage into the middle
        if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            // Trim leading / trailing blank lines
            var insertLines = clipboardText.Trim(Document.NewLine).SplitDocumentLines();

            // Insert into the action-line if it stays valid
            if (insertLines.Length == 1) {
                string oldLine = Document.Lines[Document.Caret.Row];
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
                    string line = Document.Lines[Document.Caret.Row];
                    int leadingSpaces = line.Length - line.TrimStart().Length;
                    int frameDigits = actionLine.Frames.Length;
                    Document.Caret.Col += ActionLine.MaxFramesDigits - (leadingSpaces + frameDigits);

                    Document.ReplaceLine(Document.Caret.Row, actionLine.ToString());

                    return;
                }

                Document.ReplaceLine(Document.Caret.Row, oldLine);
            }

            // Otherwise insert below
            Document.InsertLines(Document.Caret.Row + 1, insertLines);
            Document.Caret.Row += insertLines.Length;
            Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;
        } else {
            Document.Insert(clipboardText);
        }
    }

    private void OnSelectAll() {
        Document.Selection.Start = new CaretPosition(0, 0);
        Document.Selection.End = new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length);
    }

    private void OnSelectBlock() {
        // Search first empty line above/below caret
        int above = Document.Caret.Row;
        while (above > 0 && !string.IsNullOrWhiteSpace(Document.Lines[above - 1]))
            above--;

        int below = Document.Caret.Row;
        while (below < Document.Lines.Count - 1 && !string.IsNullOrWhiteSpace(Document.Lines[below + 1]))
            below++;

        Document.Selection.Start = new CaretPosition(above, 0);
        Document.Selection.End = new CaretPosition(below, Document.Lines[below].Length);
    }

    private void OnFind() {
        if (!Document.Selection.Empty) {
            var min = Document.Selection.Min;
            var max = Document.Selection.Max;

            // Clamp to current line
            if (min < new CaretPosition(Document.Caret.Row, 0)) {
                min = new CaretPosition(Document.Caret.Row, 0);
            }
            if (max > new CaretPosition(Document.Caret.Row, Document.Lines[Document.Caret.Row].Length)) {
                max = new CaretPosition(Document.Caret.Row, Document.Lines[Document.Caret.Row].Length);
            }

            lastFindQuery = Document.GetTextInRange(min, max);
        }

        FindDialog.Show(this, ref lastFindQuery, ref lastFindMatchCase);
    }

    private void OnGoTo() {
        Document.Caret.Row = GoToDialog.Show(Document);
        Document.Caret = ClampCaret(Document.Caret);
        Document.Selection.Clear();

        ScrollCaretIntoView();
    }

    private void OnToggleFolding() {
        // Find current region
        var folding = foldings.FirstOrDefault(fold => fold.MinRow <= Document.Caret.Row && fold.MaxRow >= Document.Caret.Row);
        if (folding.MinRow == folding.MaxRow) {
            return;
        }

        ToggleCollapse(folding.MinRow);
        Document.Caret.Row = folding.MinRow;
        Document.Caret.Col = Document.Lines[folding.MinRow].Length;
    }

    private void OnDeleteSelectedLines() {
        using var __ = Document.Update();

        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;
        if (Document.Selection.Empty) {
            minRow = maxRow = Document.Caret.Row;
        }

        Document.RemoveLines(minRow, maxRow);
        Document.Selection.Clear();
        Document.Caret.Row = minRow;
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
                Document.ReplaceLine(row, line.Remove(hashIdx, 1));

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else if (!allCommented && UncommentedBreakpointRegex.IsMatch(line)) {
                Document.ReplaceLine(row, $"#{line}");

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }

        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);
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
                if ((!CommentLine.IsLabel(lineTrimmed) && !lineTrimmed.StartsWith("#***") && !ActionLine.TryParse(lineTrimmed[1..], out _)) || lineTrimmed.StartsWith("#lvl_") || TimestampRegex.IsMatch(lineTrimmed)) {
                    // Ignore non-input comments and special labels
                    continue;
                }

                int hashIdx = line.IndexOf('#');
                Document.ReplaceLine(row, line.Remove(hashIdx, 1));

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else {
                Document.ReplaceLine(row, $"#{line}");

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }

        // Jump to next line for single-line edits
        if (minRow == maxRow && minRow == Document.Caret.Row && Document.Selection.Empty) {
            Document.Caret.Col = oldCol;
            Document.Caret.Row = Math.Min(Document.Lines.Count - 1, Document.Caret.Row + 1);
        }

        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);
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
                Document.ReplaceLine(row, line.Remove(hashIdx, 1));

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col--;
                if (row == maxRow)
                    Document.Selection.End.Col--;
                if (row == Document.Caret.Row)
                    Document.Caret.Col--;
            } else {
                Document.ReplaceLine(row, $"#{line}");

                // Shift everything over
                if (row == minRow)
                    Document.Selection.Start.Col++;
                if (row == maxRow)
                    Document.Selection.End.Col++;
                if (row == Document.Caret.Row)
                    Document.Caret.Col++;
            }
        }

        // Jump to next line for single-line edits
        if (minRow == maxRow && minRow == Document.Caret.Row && Document.Selection.Empty) {
            Document.Caret.Col = oldCol;
            Document.Caret.Row = Math.Min(Document.Lines.Count - 1, Document.Caret.Row + 1);
        }

        // Clamp new column
        Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
        Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);
    }

    private void OnInsertRoomName() {
        if (CommunicationWrapper.Connected) {
            InsertLine($"#lvl_{CommunicationWrapper.LevelName}");
        }
    }

    private void OnInsertTime() {
        if (CommunicationWrapper.Connected) {
            InsertLine($"#{CommunicationWrapper.ChapterTime}");
        }
    }

    private void OnInsertPosition() {
        if (CommunicationWrapper.Connected) {
            string xPos = (CommunicationWrapper.PlayerPosition.X + CommunicationWrapper.PlayerPositionRemainder.X).ToFormattedString(CommunicationWrapper.GameSettings.PositionDecimals);
            string yPos = (CommunicationWrapper.PlayerPosition.Y + CommunicationWrapper.PlayerPositionRemainder.Y).ToFormattedString(CommunicationWrapper.GameSettings.PositionDecimals);
            InsertLine($"# Pos: {xPos}, {yPos}");
        }
    }

    private void OnInsertSpeed() {
        if (CommunicationWrapper.Connected) {
            string xSpeed = CommunicationWrapper.PlayerSpeed.X.ToFormattedString(CommunicationWrapper.GameSettings.SpeedDecimals);
            string ySpeed = CommunicationWrapper.PlayerSpeed.Y.ToFormattedString(CommunicationWrapper.GameSettings.SpeedDecimals);
            InsertLine($"# Speed: {xSpeed}, {ySpeed}");
        }
    }

    private void OnInsertModInfo() {
        if (CommunicationWrapper.GetModInfo() is var modInfo && !string.IsNullOrWhiteSpace(modInfo)) {
            InsertLine(modInfo);
        }
    }

    private void OnInsertConsoleLoadCommand() {
        if (CommunicationWrapper.GetConsoleCommand(simple: false) is var command && !string.IsNullOrWhiteSpace(command)) {
            InsertLine(command);
        }
    }

    private void OnInsertSimpleConsoleLoadCommand() {
        if (CommunicationWrapper.GetConsoleCommand(simple: true) is var command && !string.IsNullOrWhiteSpace(command)) {
            InsertLine(command);
        }
    }

    private void InsertLine(string text) {
        using var __ = Document.Update();

        CollapseSelection();

        if (Settings.Instance.InsertDirection == InsertDirection.Above) {
            Document.InsertLineAbove(text);

            if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
                Document.Caret.Row--;
                Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
            }
        } else if (Settings.Instance.InsertDirection == InsertDirection.Below) {
            Document.InsertLineBelow(text);

            if (Settings.Instance.CaretInsertPosition == CaretInsertPosition.AfterInsert) {
                int newLines = text.Count(c => c == Document.NewLine) + 1;
                Document.Caret.Row += newLines;
                Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
            }
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

    /// Deletes text in the specified range, while accounting for collapsed
    private void RemoveRange(CaretPosition min, CaretPosition max) {
        if (GetCollapse(min.Row) is { } collapse) {
            var foldMin = new CaretPosition(collapse.MinRow, 0);
            var foldMax = new CaretPosition(collapse.MinRow, Document.Lines[collapse.MinRow].Length);
            if (min <= foldMin && max >= foldMax) {
                // Entire folding is selected, so just remove it entirely
                Document.RemoveRange(min, max);
                return;
            }

            // Only partially selected, so don't delete the collapsed content, only the stuff around it
            if (min.Row == max.Row) {
                Document.RemoveRange(min, max);
            } else {
                Document.RemoveRange(min, new CaretPosition(collapse.MinRow, Document.Lines[collapse.MinRow].Length));
                Document.RemoveRange(new CaretPosition(collapse.MaxRow, Document.Lines[collapse.MaxRow].Length), max);
            }
        } else {
            Document.RemoveRange(min, max);
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

    #endregion

    #region Caret Movement

    public enum SnappingDirection { Ignore, Left, Right }

    public CaretPosition ClampCaret(CaretPosition position, bool wrapLine = false, SnappingDirection direction = SnappingDirection.Ignore) {
        // Wrap around to prev/next line
        if (wrapLine && position.Row > 0 && position.Col < 0) {
            position.Row = GetNextVisualLinePosition(-1, position).Row;
            position.Col = desiredVisualCol = Document.Lines[position.Row].Length;
        } else if (wrapLine && position.Row < Document.Lines.Count && position.Col > Document.Lines[position.Row].Length) {
            position.Row = GetNextVisualLinePosition( 1, position).Row;
            position.Col = desiredVisualCol = 0;
        }

        int maxVisualRow = GetActualRow(actualToVisualRows[^1]);

        // Clamp to document (also visually)
        position.Row = Math.Clamp(position.Row, 0, Math.Min(maxVisualRow, Document.Lines.Count - 1));
        position.Col = Math.Clamp(position.Col, 0, Document.Lines[position.Row].Length);

        // Clamp to action line if possible
        string line = Document.Lines[position.Row];
        if (ActionLine.TryParse(line, out var actionLine)) {
            position.Col = Math.Min(line.Length, SnapColumnToActionLine(actionLine, position.Col, direction));
        }

        return position;
    }

    public void ScrollCaretIntoView(bool center = false) {
        // Clamp just to be sure
        Document.Caret = ClampCaret(Document.Caret);

        // Minimum distance to the edges
        const float xLookAhead = 50.0f;
        const float yLookAhead = 50.0f;

        var caretPos = GetVisualPosition(Document.Caret);
        float carX = Font.CharWidth() * caretPos.Col;
        float carY = Font.LineHeight() * caretPos.Row;

        float top = scrollablePosition.Y;
        float bottom = (scrollableSize.Height) + scrollablePosition.Y;

        const float scrollStopPadding = 10.0f;

        int scrollX = scrollablePosition.X;
        if (Font.MeasureWidth(GetVisualLine(caretPos.Row)) < (scrollableSize.Width - textOffsetX - scrollStopPadding)) {
            // Don't scroll when the line is shorter anyway
            scrollX = 0;
        } else if (ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            // Always scroll horizontally on action lines, since we want to stay as left as possible
            scrollX = (int)((carX + xLookAhead) - (scrollableSize.Width - textOffsetX));
        } else {
            // Just scroll left/right when near the edge
            float left = scrollablePosition.X;
            float right = scrollablePosition.X + scrollableSize.Width - textOffsetX;
            if (left - carX > -xLookAhead)
                scrollX = (int)(carX - xLookAhead);
            else if (right - carX < xLookAhead)
                scrollX = (int)(carX + xLookAhead - (scrollableSize.Width - textOffsetX));
        }

        int scrollY = scrollablePosition.Y;
        if (center) {
            // Keep line in the center
            scrollY = (int)(carY - scrollableSize.Height / 2.0f);
        } else {
            // Scroll up/down when near the top/bottom
            if (top - carY > -yLookAhead)
                scrollY = (int)(carY - yLookAhead);
            else if (bottom - carY < yLookAhead)
                scrollY = (int)(carY + yLookAhead - (scrollableSize.Height));
        }

        // Avoid jittering while the game info panel keeps changing size (for example while running to a breakpoint)
        if (center && Math.Abs(scrollY - scrollablePosition.Y) < Font.LineHeight()) {
            scrollY = scrollablePosition.Y;
        }

        scrollable.ScrollPosition = new Point(
            Math.Max(0, scrollX),
            Math.Max(0, scrollY));
    }

    private void MoveCaret(CaretMovementType direction, bool updateSelection) {
        var line = Document.Lines[Document.Caret.Row];
        var oldCaret = Document.Caret;

        ActionLine? currentActionLine = ActionLine.Parse(line);
        if (currentActionLine is { } actionLine) {
            Document.Caret.Col = Math.Min(line.Length, SnapColumnToActionLine(actionLine, Document.Caret.Col));
            int leadingSpaces = ActionLine.MaxFramesDigits - actionLine.Frames.Length;

            // Line wrapping
            if (Document.Caret.Row > 0 && Document.Caret.Col == leadingSpaces &&
                direction is CaretMovementType.CharLeft or CaretMovementType.WordLeft)
            {
                Document.Caret.Row = GetNextVisualLinePosition(-1, Document.Caret).Row;
                Document.Caret.Col = desiredVisualCol = Document.Lines[Document.Caret.Row].Length;
            } else if (Document.Caret.Row < Document.Lines.Count - 1 && Document.Caret.Col == Document.Lines[Document.Caret.Row].Length &&
                       direction is CaretMovementType.CharRight or CaretMovementType.WordRight)
            {
                Document.Caret.Row = GetNextVisualLinePosition( 1, Document.Caret).Row;
                Document.Caret.Col = desiredVisualCol = 0;
            } else {
                // Regular action line movement
                Document.Caret = direction switch {
                    CaretMovementType.CharLeft  => ClampCaret(new CaretPosition(Document.Caret.Row, GetSoftSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < Document.Caret.Col, Document.Caret.Col)), wrapLine: true),
                    CaretMovementType.CharRight => ClampCaret(new CaretPosition(Document.Caret.Row, GetSoftSnapColumns(actionLine).FirstOrDefault(c => c > Document.Caret.Col, Document.Caret.Col)), wrapLine: true),
                    CaretMovementType.WordLeft  => ClampCaret(new CaretPosition(Document.Caret.Row, GetHardSnapColumns(actionLine).Reverse().FirstOrDefault(c => c < Document.Caret.Col, Document.Caret.Col)), wrapLine: true),
                    CaretMovementType.WordRight => ClampCaret(new CaretPosition(Document.Caret.Row, GetHardSnapColumns(actionLine).FirstOrDefault(c => c > Document.Caret.Col, Document.Caret.Col)), wrapLine: true),
                    CaretMovementType.LineStart => ClampCaret(new CaretPosition(Document.Caret.Row, leadingSpaces)),
                    CaretMovementType.LineEnd   => ClampCaret(new CaretPosition(Document.Caret.Row, line.Length)),
                    _ => GetNewTextCaretPosition(direction),
                };
            }
        } else {
            // Regular text movement
            Document.Caret = GetNewTextCaretPosition(direction);
        }

        // Apply / Update desired column
        var newVisualPos = GetVisualPosition(Document.Caret);
        if (oldCaret.Row != Document.Caret.Row) {
            newVisualPos.Col = desiredVisualCol;
        } else {
            desiredVisualCol = newVisualPos.Col;
        }
        Document.Caret = ClampCaret(GetActualPosition(newVisualPos));

        if (oldCaret.Row != Document.Caret.Row) {
            FixInvalidInput(oldCaret.Row);
        }

        // When going from a non-action-line to an action-line, snap the caret to the frame count
        if (direction is CaretMovementType.LineUp or CaretMovementType.LineDown or CaretMovementType.PageUp or CaretMovementType.PageDown or CaretMovementType.LabelUp or CaretMovementType.LabelDown &&
            currentActionLine == null && TryParseAndFormatActionLine(Document.Caret.Row, out _))
        {
            Document.Caret.Col = desiredVisualCol = ActionLine.MaxFramesDigits;
        }

        if (updateSelection) {
            if (Document.Selection.Empty) {
                Document.Selection.Start = oldCaret;
            }

            Document.Selection.End = Document.Caret;

            // If the selection is multi-line, always select the entire start/end line if it's an action line
            if (Settings.Instance.AutoSelectFullActionLine && Document.Selection.Start.Row != Document.Selection.End.Row) {
                string startLine = Document.Lines[Document.Selection.Start.Row];
                string endLine = Document.Lines[Document.Selection.End.Row];
                if (ActionLine.Parse(startLine) != null) {
                    Document.Selection.Start.Col = Document.Selection.Start < Document.Selection.End ? 0 : startLine.Length;
                }
                if (ActionLine.Parse(Document.Lines[Document.Selection.End.Row]) != null) {
                    Document.Selection.End.Col = Document.Selection.Start < Document.Selection.End ? endLine.Length : 0;
                }
            }
        } else {
            Document.Selection.Clear();
        }

        ActivePopupMenu = null;

        Document.Caret = Document.Caret;
        ScrollCaretIntoView(center: direction is CaretMovementType.LabelUp or CaretMovementType.LabelDown);
    }

    // For regular text movement
    private CaretPosition GetNewTextCaretPosition(CaretMovementType direction) =>
        direction switch {
            CaretMovementType.None => Document.Caret,
            CaretMovementType.CharLeft => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Caret.Col - 1), wrapLine: true),
            CaretMovementType.CharRight => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Caret.Col + 1), wrapLine: true),
            CaretMovementType.WordLeft => ClampCaret(GetNextWordCaretPosition(-1), wrapLine: true),
            CaretMovementType.WordRight => ClampCaret(GetNextWordCaretPosition(1), wrapLine: true),
            CaretMovementType.LineUp => ClampCaret(GetNextVisualLinePosition(-1, Document.Caret)),
            CaretMovementType.LineDown => ClampCaret(GetNextVisualLinePosition(1, Document.Caret)),
            CaretMovementType.LabelUp => ClampCaret(GetLabelPosition(-1)),
            CaretMovementType.LabelDown => ClampCaret(GetLabelPosition(1)),
            // TODO: Page Up / Page Down
            CaretMovementType.PageUp => ClampCaret(GetNextVisualLinePosition(-1, Document.Caret)),
            CaretMovementType.PageDown => ClampCaret(GetNextVisualLinePosition(1, Document.Caret)),
            CaretMovementType.LineStart => ClampCaret(new CaretPosition(Document.Caret.Row, 0)),
            CaretMovementType.LineEnd => ClampCaret(new CaretPosition(Document.Caret.Row, Document.Lines[Document.Caret.Row].Length)),
            CaretMovementType.DocumentStart => ClampCaret(new CaretPosition(0, 0)),
            CaretMovementType.DocumentEnd => ClampCaret(new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length)),
            _ => throw new UnreachableException()
        };

    private enum CharType { Alphanumeric, Symbol, Whitespace }
    private CaretPosition GetNextWordCaretPosition(int dir) {
        var newPosition = Document.Caret;
        var line = Document.Lines[newPosition.Row];

        // Prepare wrap-around for ClampCaret()
        if (dir == -1 && Document.Caret.Col == 0)
            return new CaretPosition(Document.Caret.Row, -1);
        if (dir == 1 && Document.Caret.Col == line.Length)
            return new CaretPosition(Document.Caret.Row, line.Length + 1);

        // The caret is to the left of the character. So offset 1 to the left when going that direction
        int offset = dir == -1 ? -1 : 0;

        CharType type;
        if (char.IsLetterOrDigit(line[newPosition.Col + offset]))
            type = CharType.Alphanumeric;
        else if (char.IsWhiteSpace(line[newPosition.Col + offset]))
            type = CharType.Whitespace;
        else
            // Probably a symbol
            type = CharType.Symbol;

        while (newPosition.Col + offset >= 0 && newPosition.Col + offset < line.Length && IsSame(line[newPosition.Col + offset], type))
            newPosition.Col += dir;

        return newPosition;

        static bool IsSame(char c, CharType type) {
            return type switch {
                CharType.Alphanumeric => char.IsLetterOrDigit(c),
                CharType.Whitespace => char.IsWhiteSpace(c),
                CharType.Symbol => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c), // Everything not alphanumeric of whitespace is considered a symbol
                _ => throw new UnreachableException(),
            };
        }
    }

    private CaretPosition GetNextVisualLinePosition(int dist, CaretPosition position) {
        var visualPos = GetVisualPosition(position);
        return GetActualPosition(new CaretPosition(visualPos.Row + dist, visualPos.Col));
    }

    private CaretPosition GetLabelPosition(int dir) {
        int row = Document.Caret.Row;

        row += dir;
        while (row >= 0 && row < Document.Lines.Count) {
            string line = Document.Lines[row];

            // Go to the next label / breakpoint
            if (CommentLine.IsLabel(Document.Lines[row]) || line.TrimStart().StartsWith("***")) {
                break;
            }

            row += dir;
        }

        return new CaretPosition(row, Document.Caret.Col);
    }

    void CollapseSelection() {
        if (Document.Selection.Empty) return;

        var collapseToRow = Settings.Instance.InsertDirection switch {
            InsertDirection.Above => Document.Selection.Min,
            InsertDirection.Below => Document.Selection.Max,
            _ => throw new ArgumentOutOfRangeException()
        };
        Document.Selection.Clear();
        Document.Caret = collapseToRow;
    }

    #endregion

    #region Mouse Interactions

    private bool primaryMouseButtonDown = false;

    protected override void OnMouseDown(MouseEventArgs e) {
        calculationState = null;

        if (e.Buttons.HasFlag(MouseButtons.Primary)) {
            // Refocus in case something unfocused the editor
            Focus();

            if (LocationToFolding(e.Location) is { } folding) {
                ToggleCollapse(folding.MinRow);

                e.Handled = true;
                Recalc();
                return;
            }

            if (e.HasCommonModifier() && LocationToLineLink(e.Location) is { } linkAnchor) {
                ((LineLinkAnchorData)linkAnchor.UserData!).OnUse();

                e.Handled = true;
                return;
            }

            primaryMouseButtonDown = true;

            var oldCaret = Document.Caret;
            (Document.Caret, var visual) = LocationToCaretPosition(e.Location);

            if (e.Modifiers.HasFlag(Keys.Shift)) {
                if (Document.Selection.Empty) {
                    Document.Selection.Start = oldCaret;
                }

                Document.Caret = ClampCaret(Document.Caret, direction: oldCaret < Document.Caret ? SnappingDirection.Left : SnappingDirection.Right);
                Document.Selection.End = Document.Caret;
            } else {
                Document.Caret = ClampCaret(Document.Caret);
                Document.Selection.Start = Document.Selection.End = Document.Caret;
            }

            desiredVisualCol = visual.Col;
            ScrollCaretIntoView();

            if (oldCaret.Row != Document.Caret.Row) {
                FixInvalidInput(oldCaret.Row);
            }

            ActivePopupMenu = null;

            e.Handled = true;
            Recalc();
            return;
        }

        if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
            ContextMenu.Show();
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Primary)) {
            primaryMouseButtonDown = false;
            e.Handled = true;
        }

        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        if (primaryMouseButtonDown) {
            (Document.Caret, var visual) = LocationToCaretPosition(e.Location, SnappingDirection.Left);
            Document.Caret = Document.Selection.End = ClampCaret(Document.Caret, direction: Document.Selection.Start < Document.Caret ? SnappingDirection.Left : SnappingDirection.Right);

            desiredVisualCol = visual.Col;
            ScrollCaretIntoView();

            ActivePopupMenu = null;

            Recalc();
        }

        UpdateMouseCursor(e.Location, e.Modifiers);

        base.OnMouseMove(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        var position = Document.Caret;
        string line = Document.Lines[position.Row];

        // Select clicked word
        int startIdx = position.Col;
        int endIdx = position.Col;
        while (startIdx > 0 && ShouldExpand(line[startIdx-1])) {
            startIdx--;
        }
        while (endIdx < line.Length && ShouldExpand(line[endIdx])) {
            endIdx++;
        }

        Document.Selection.Start = position with { Col = startIdx };
        Document.Selection.End = position with { Col = endIdx };

        e.Handled = true;
        Recalc();
        return;

        static bool ShouldExpand(char c) => !char.IsWhiteSpace(c) && (!char.IsPunctuation(c) || c is '*' or '_');
    }


    protected override void OnMouseWheel(MouseEventArgs e) {
        // Adjust frame count
        if (e.Modifiers.HasFlag(Keys.Shift)) {
            if (Document.Selection.Empty) {
                var (position, _) = LocationToCaretPosition(e.Location);
                position = ClampCaret(position);
                AdjustFrameCounts(Document.Caret.Row, position.Row, Math.Sign(e.Delta.Height));
            } else {
                AdjustFrameCounts(Document.Selection.Start.Row, Document.Selection.End.Row, Math.Sign(e.Delta.Height));
            }

            e.Handled = true;
            return;
        }
        // Zoom in/out
        if (e.HasCommonModifier()) {
            float oldCarY = Font.LineHeight() * GetVisualPosition(Document.Caret).Row;
            float oldOffset = scrollablePosition.Y - oldCarY;

            const float scrollSpeed = 0.1f;
            if (e.Delta.Height > 0.0f) {
                Settings.Instance.FontZoom *= 1.0f + scrollSpeed;
            } else if (e.Delta.Height < 0.0f) {
                Settings.Instance.FontZoom *= 1.0f - scrollSpeed;
            }

            Settings.OnFontChanged();
            Recalc();

            float newCarY = Font.LineHeight() * GetVisualPosition(Document.Caret).Row;

            // Adjust scroll to keep caret centered
            scrollable.ScrollPosition = scrollablePosition with {
                Y = (int)(newCarY + oldOffset)
            };

            e.Handled = true;
            return;
        }

        if (Settings.Instance.ScrollSpeed > 0.0f) {
            // Manually scroll to respect our scroll speed
            scrollable.ScrollPosition = scrollable.ScrollPosition with {
                Y = Math.Clamp((int)(scrollable.ScrollPosition.Y - e.Delta.Height * Font.LineHeight() * Settings.Instance.ScrollSpeed), 0, Height - scrollable.ClientSize.Height)
            };
            e.Handled = true;
        }

        base.OnMouseWheel(e);
    }

    private void UpdateMouseCursor(PointF location, Keys modifiers) {
        // Maybe a bit out-of-place here, but this is required to update the underline of line links
        Invalidate();

        if (modifiers.HasCommonModifier() && LocationToLineLink(location) != null) {
            Cursor = Cursors.Pointer;
            return;
        }

        if (LocationToFolding(location) != null) {
            Cursor = Cursors.Pointer;
        } else {
            // Prevent overriding cursor of popup menu
            if (ActivePopupMenu != null) {
                var pos = ActivePopupMenu.PointFromScreen(Mouse.Position);
                if (pos.X >= 0.0f & pos.X <= ActivePopupMenu.Width &&
                    pos.Y >= 0.0f & pos.Y <= ActivePopupMenu.Height)
                {
                    Cursor = null;
                    return;
                }
            }

            Cursor = Cursors.IBeam;
        }
    }

    private (CaretPosition Actual, CaretPosition Visual) LocationToCaretPosition(PointF location, SnappingDirection direction = SnappingDirection.Ignore) {
        location.X -= textOffsetX;

        int visualRow = (int)Math.Floor(location.Y / Font.LineHeight());
        int visualCol = (int)Math.Round(location.X / Font.CharWidth());

        var visualPos = new CaretPosition(visualRow, visualCol);

        return (GetActualPosition(visualPos), visualPos);
    }

    private Folding? LocationToFolding(PointF location) {
        if (!Settings.Instance.ShowFoldIndicators) {
            return null;
        }

        // Extend range through entire line numbers
        if (location.X >= scrollablePosition.X &&
            location.X <= scrollablePosition.X + textOffsetX - LineNumberPadding)
        {
            int row = GetActualRow((int) (location.Y / Font.LineHeight()));

            var folding = foldings.FirstOrDefault(fold => fold.MinRow == row);
            if (folding.MinRow == folding.MaxRow) {
                return null;
            }

            return folding;
        }

        return null;
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

    #region Drawing

    public override int DrawX => scrollablePosition.X;
    public override int DrawY => scrollablePosition.Y;
    public override int DrawWidth => scrollable.Width;
    public override int DrawHeight => scrollable.Height;
    public override bool CanDraw => !Document.UpdateInProgress;

    public override void Draw(SKSurface surface) {
        var canvas = surface.Canvas;

        using var strokePaint = new SKPaint();
        strokePaint.Style = SKPaintStyle.Stroke;

        using var fillPaint = new SKPaint();
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = true;

        // To be reused below. Kinda annoying how C# handles out parameter conflicts
        WrapEntry wrap;

        int topVisualRow = (int)(scrollablePosition.Y / Font.LineHeight()) - offscreenLinePadding;
        int bottomVisualRow = (int)((scrollablePosition.Y + scrollableSize.Height) / Font.LineHeight()) + offscreenLinePadding;
        int topRow = Math.Max(0, GetActualRow(topVisualRow));
        int bottomRow = Math.Min(Document.Lines.Count - 1, GetActualRow(bottomVisualRow));

        // Draw text
        float yPos = actualToVisualRows[topRow] * Font.LineHeight();
        for (int row = topRow; row <= bottomRow; row++) {
            string line = Document.Lines[row];

            if (GetCollapse(row) is { } collapse) {
                const float foldingPadding = 1.0f;

                float width = 0.0f;
                float height = 0.0f;
                if (commentLineWraps.TryGetValue(row, out wrap)) {
                    for (int i = 0; i < wrap.Lines.Length; i++) {
                        var subLine = wrap.Lines[i].Line;
                        float xIdent = i == 0 ? 0 : wrap.StartOffset * Font.CharWidth();

                        canvas.DrawText(subLine, textOffsetX + xIdent, yPos + Font.Offset(), Font, Settings.Instance.Theme.CommentPaint.ForegroundColor);
                        yPos += Font.LineHeight();
                        width = Math.Max(width, Font.MeasureWidth(subLine) + xIdent);
                        height += Font.LineHeight();
                    }
                } else {
                    highlighter.DrawLine(canvas, textOffsetX, yPos, line);
                    yPos += Font.LineHeight();
                    width = Font.MeasureWidth(line);
                    height = Font.LineHeight();
                }

                canvas.DrawRect(
                    x: Font.CharWidth() * collapse.StartCol + textOffsetX - foldingPadding,
                    y: yPos - height - foldingPadding,
                    w: width - Font.CharWidth() * collapse.StartCol + foldingPadding * 2.0f,
                    h: height + foldingPadding * 2.0f,
                    Settings.Instance.Theme.CommentBoxPaint);

                row = collapse.MaxRow;
                continue;
            }

            if (commentLineWraps.TryGetValue(row, out wrap)) {
                for (int i = 0; i < wrap.Lines.Length; i++) {
                    var subLine = wrap.Lines[i].Line;
                    float xIdent = i == 0 ? 0 : wrap.StartOffset * Font.CharWidth();

                    canvas.DrawText(subLine, textOffsetX + xIdent, yPos + Font.Offset(), Font, Settings.Instance.Theme.CommentPaint.ForegroundColor);
                    yPos += Font.LineHeight();
                }
            } else {
                highlighter.DrawLine(canvas, textOffsetX, yPos, line);
                yPos += Font.LineHeight();
            }
        }

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

        var caretPos = GetVisualPosition(Document.Caret);
        float carX = Font.CharWidth() * caretPos.Col + textOffsetX;
        float carY = Font.LineHeight() * caretPos.Row;

        // Highlight caret line
        fillPaint.ColorF = Settings.Instance.Theme.CurrentLine.ToSkia();
        canvas.DrawRect(
            x: scrollablePosition.X,
            y: carY,
            w: scrollable.Width,
            h: Font.LineHeight(),
            fillPaint);

        // Draw caret
        if (HasFocus) {
            strokePaint.ColorF = Settings.Instance.Theme.Caret.ToSkia();
            strokePaint.StrokeWidth = 1.0f;
            canvas.DrawLine(carX, carY, carX, carY + Font.LineHeight() - 1, strokePaint);
        }

        // Draw selection
        if (!Document.Selection.Empty) {
            var min = GetVisualPosition(Document.Selection.Min);
            var max = GetVisualPosition(Document.Selection.Max);

            fillPaint.ColorF = Settings.Instance.Theme.Selection.ToSkia();

            if (min.Row == max.Row) {
                float x = Font.CharWidth() * min.Col + textOffsetX;
                float w = Font.CharWidth() * (max.Col - min.Col);
                float y = Font.LineHeight() * min.Row;
                float h = Font.LineHeight();
                canvas.DrawRect(x, y, w, h, fillPaint);
            } else {
                var visualLine = GetVisualLine(min.Row);

                // When the selection starts at the beginning of the line, extend it to cover the LineNumberPadding as well
                float extendLeft = min.Col == 0 ? LineNumberPadding : 0.0f;
                float x = Font.CharWidth() * min.Col + textOffsetX - extendLeft;
                float w = visualLine.Length == 0 ? 0.0f : Font.MeasureWidth(visualLine[min.Col..]);
                float y = Font.LineHeight() * min.Row;
                canvas.DrawRect(x, y, w + extendLeft, Font.LineHeight(), fillPaint);

                // Cull off-screen lines
                for (int i = Math.Max(min.Row + 1, topVisualRow); i < Math.Min(max.Row, bottomVisualRow); i++) {
                    // Draw at least half a character for each line
                    w = Font.CharWidth() * Math.Max(0.5f, GetVisualLine(i).Length);
                    y = Font.LineHeight() * i;
                    canvas.DrawRect(textOffsetX - LineNumberPadding, y, w + LineNumberPadding, Font.LineHeight(), fillPaint);
                }

                w = Font.MeasureWidth(GetVisualLine(max.Row)[..max.Col]);
                y = Font.LineHeight() * max.Row;
                canvas.DrawRect(textOffsetX - LineNumberPadding, y, w + LineNumberPadding, Font.LineHeight(), fillPaint);
            }
        }

        // Draw calculate operation
        if (calculationState is not null) {
            string calculateLine = $"{calculationState.Operator.Char()}{calculationState.Operand}";

            float padding = Font.CharWidth() * 0.5f;
            float x = textOffsetX + Font.CharWidth() * ActionLine.MaxFramesDigits + Font.CharWidth() * 0.5f;
            float y = carY;
            float w = Font.CharWidth() * calculateLine.Length + 2 * padding;
            float h = Font.LineHeight();

            fillPaint.ColorF = Settings.Instance.Theme.CalculateBg.ToSkia();
            canvas.DrawRoundRect(x, y, w, h, 4.0f, 4.0f, fillPaint);
            fillPaint.ColorF = Settings.Instance.Theme.CalculateFg.ToSkia();
            canvas.DrawText(calculateLine, x + padding, y + Font.Offset(), Font, fillPaint);
        }

        // Draw line numbers
        {
            fillPaint.ColorF = Settings.Instance.Theme.Background.ToSkia();
            canvas.DrawRect(
                x: scrollablePosition.X,
                y: scrollablePosition.Y,
                w: textOffsetX - LineNumberPadding,
                h: scrollableSize.Height,
                fillPaint);

            // Highlight playing / savestate line
            if (CommunicationWrapper.Connected) {
                if (CommunicationWrapper.CurrentLine != -1 && CommunicationWrapper.CurrentLine < actualToVisualRows.Length) {
                    fillPaint.ColorF = Settings.Instance.Theme.PlayingLineBg.ToSkia();
                    canvas.DrawRect(
                        x: scrollablePosition.X,
                        y: actualToVisualRows[CommunicationWrapper.CurrentLine] * Font.LineHeight(),
                        w: textOffsetX - LineNumberPadding,
                        h: Font.LineHeight(),
                        fillPaint);
                }
                if (CommunicationWrapper.SaveStateLine != -1 && CommunicationWrapper.SaveStateLine < actualToVisualRows.Length) {
                    fillPaint.ColorF = Settings.Instance.Theme.SavestateBg.ToSkia();
                    if (CommunicationWrapper.SaveStateLine == CommunicationWrapper.CurrentLine) {
                        canvas.DrawRect(
                            x: scrollablePosition.X,
                            y: actualToVisualRows[CommunicationWrapper.SaveStateLine] * Font.LineHeight(),
                            w: 5.0f,
                            h: Font.LineHeight(),
                            fillPaint);
                    } else {
                        canvas.DrawRect(
                            x: scrollablePosition.X,
                            y: actualToVisualRows[CommunicationWrapper.SaveStateLine] * Font.LineHeight(),
                            w: textOffsetX - LineNumberPadding,
                            h: Font.LineHeight(),
                            fillPaint);
                    }
                }
            }

            yPos = actualToVisualRows[topRow] * Font.LineHeight();

            for (int row = topRow; row <= bottomRow; row++) {
                int oldRow = row;
                var numberString = (row + 1).ToString();

                bool isPlayingLine = CommunicationWrapper.CurrentLine >= 0 && CommunicationWrapper.CurrentLine < actualToVisualRows.Length &&
                                     actualToVisualRows[CommunicationWrapper.CurrentLine] == actualToVisualRows[row];
                bool isSaveStateLine = CommunicationWrapper.SaveStateLine >= 0 && CommunicationWrapper.SaveStateLine < actualToVisualRows.Length &&
                                       actualToVisualRows[CommunicationWrapper.SaveStateLine] == actualToVisualRows[row];

                if (isPlayingLine) {
                    fillPaint.ColorF = Settings.Instance.Theme.PlayingLineFg.ToSkia();
                } else if (isSaveStateLine) {
                    fillPaint.ColorF = Settings.Instance.Theme.SavestateFg.ToSkia();
                } else {
                    fillPaint.ColorF = Settings.Instance.Theme.LineNumber.ToSkia();
                }

                if (Settings.Instance.LineNumberAlignment == LineNumberAlignment.Left) {
                    canvas.DrawText(numberString, scrollablePosition.X + LineNumberPadding, yPos + Font.Offset(), Font, fillPaint);
                } else if (Settings.Instance.LineNumberAlignment == LineNumberAlignment.Right) {
                    float ident = Font.CharWidth() * (bottomRow - (row + 1).Digits());
                    canvas.DrawText(numberString, scrollablePosition.X + LineNumberPadding + ident, yPos + Font.Offset(), Font, fillPaint);
                }

                bool collapsed = false;
                if (GetCollapse(row) is { } collapse) {
                    row = collapse.MaxRow;
                    collapsed = true;
                }
                if (Settings.Instance.ShowFoldIndicators && foldings.FirstOrDefault(fold => fold.MinRow == oldRow) is var folding && folding.MinRow != folding.MaxRow) {
                    canvas.Save();
                    canvas.Translate(
                        dx: scrollablePosition.X + textOffsetX - LineNumberPadding * 2.0f - Font.CharWidth(),
                        dy: yPos + (Font.LineHeight() - Font.CharWidth()) / 2.0f);
                    canvas.Scale(Font.CharWidth());

                    canvas.DrawPath(collapsed ? Assets.CollapseClosedPath : Assets.CollapseOpenPath, fillPaint);

                    canvas.Restore();
                }

                if (commentLineWraps.TryGetValue(oldRow, out wrap)) {
                    yPos += Font.LineHeight() * wrap.Lines.Length;
                } else {
                    yPos += Font.LineHeight();
                }
            }

            strokePaint.ColorF = Settings.Instance.Theme.ServiceLine.ToSkia();
            canvas.DrawLine(
                x0: scrollablePosition.X + textOffsetX - LineNumberPadding, y0: 0.0f,
                x1: scrollablePosition.X + textOffsetX - LineNumberPadding, y1: yPos + scrollableSize.Height,
                strokePaint);
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

    #endregion
}
