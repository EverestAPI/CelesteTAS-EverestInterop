using CelesteStudio.Controls;
using CelesteStudio.Dialog;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WrapLine = (string Line, int Index);
using WrapEntry = (int StartOffset, (string Line, int Index)[] Lines);

namespace CelesteStudio.Editing;

public class TextViewer : SkiaDrawable {
    /// Allows applying fixes / formatting to text changes, after the caret has moved away
    public event Action<Document, CaretPosition, CaretPosition> CaretMoved = (_, _, _) => { };
    public void OnCaretMoved(CaretPosition oldPosition, CaretPosition newPosition) => CaretMoved(document!, oldPosition, newPosition);

    /// Reports insertions and deletions of the underlying document
    public event Action<Document, Dictionary<int, string>, Dictionary<int, string>> TextChanged = (_, _, _) => { };
    /// Allows applying fixes / formatting to text changes, before being commited to the undo-stack
    public event Action<Document, Dictionary<int, string>, Dictionary<int, string>> FixupText = (_, _, _) => { };

    /// The currently open file has changed. The previous document might not be available (for example during startup).
    public event Action<Document?> PreDocumentChanged = _ => { };
    public event Action<Document> PostDocumentChanged = _ => { };

    /// Invoked when the preferred size of the game info is changed
    public event Action<Size>? PreferredSizeChanged;

    private Document? document;
    public Document Document {
        get => document!;
        set {
            if (document != null) {
                document.TextChanged -= HandleTextChanged;
                document.FixupPatch -= HandleFixupPatch;
            }

            PreDocumentChanged(document);
            document = value;
            PostDocumentChanged(value);

            // Jump to end when file only 10 lines, else the start
            document.Caret = document.Lines.Count is > 0 and <= 10
                ? new CaretPosition(document.Lines.Count - 1, document.Lines[^1].Length)
                : new CaretPosition(0, 0);

            // Ensure everything is still valid when something has changed
            document.TextChanged += HandleTextChanged;
            document.FixupPatch += HandleFixupPatch;

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

            // Ensure everything is in a valid state
            Recalc();

            return;

            void HandleTextChanged(Document _, Dictionary<int, string> insertions, Dictionary<int, string> deletions) {
                Recalc();
                TextChanged(document, insertions, deletions);
            }
            Document.Patch? HandleFixupPatch(Document _, Dictionary<int, string> insertions, Dictionary<int, string> deletions) {
                var fixup = Document.Update(raiseEvents: false);

                FixupText(document, insertions, deletions);

                // Clamp caret/selection
                Document.Selection.Start.Row = Math.Clamp(Document.Selection.Start.Row, 0, Document.Lines.Count - 1);
                Document.Selection.End.Row = Math.Clamp(Document.Selection.End.Row, 0, Document.Lines.Count - 1);
                Document.Caret.Row = Math.Clamp(Document.Caret.Row, 0, Document.Lines.Count - 1);
                Document.Selection.Start.Col = Math.Clamp(Document.Selection.Start.Col, 0, Document.Lines[Document.Selection.Start.Row].Length);
                Document.Selection.End.Col = Math.Clamp(Document.Selection.End.Col, 0, Document.Lines[Document.Selection.End.Row].Length);
                Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, Document.Lines[Document.Caret.Row].Length);

                fixup.Discard(); // We don't want to part of the regular undo stack
                return fixup.Patches.Count > 0 ? fixup.Patches.Aggregate(Document.Patch.Merge) : null;
            }
        }
    }

    #region Bindings

    private static InstanceActionBinding<TextViewer> CreateAction(string identifier, string displayName, Hotkey defaultHotkey, Action<TextViewer> action)
        => new(identifier, displayName, Binding.Category.Editor, defaultHotkey, action);

    protected static readonly InstanceBinding Copy = CreateAction("Editor_Copy", "Copy", Hotkey.KeyCtrl(Keys.C), editor => editor.OnCopy());

    protected static readonly InstanceBinding SelectAll = CreateAction("Editor_SelectAll", "Select All", Hotkey.KeyCtrl(Keys.A), editor => editor.OnSelectAll());
    protected static readonly InstanceBinding SelectBlock = CreateAction("Editor_Cut", "Select Block", Hotkey.KeyCtrl(Keys.W), editor => editor.OnSelectBlock());

    protected static readonly InstanceBinding Find = CreateAction("Editor_Find", "Find...", Hotkey.KeyCtrl(Keys.F), editor => editor.OnFind());
    protected static readonly InstanceBinding GoTo = CreateAction("Editor_GoTo", "Go To...", Hotkey.KeyCtrl(Keys.G), editor => editor.OnGoTo());
    protected static readonly InstanceBinding ToggleFolding = CreateAction("Editor_ToggleFolding", "Toggle Folding", Hotkey.KeyCtrl(Keys.Minus), editor => editor.OnToggleFolding());

    public static readonly InstanceBinding[] AllBindings = [
        Copy,
        SelectAll, SelectBlock,
        Find, GoTo, ToggleFolding,
    ];

    #endregion

    protected const int OffscreenLinePadding = 3;
    public const float LineNumberPadding = 5.0f;

    // Only expand width of line numbers for actually visible digits
    private int lastVisibleLineNumberDigits = -1;
    private int VisibleLineNumberDigits {
        get {
            int bottomVisualRow = (int)((scrollablePosition.Y + scrollableSize.Height) / Font.LineHeight()) + OffscreenLinePadding;
            int bottomRow = Math.Min(Document.Lines.Count - 1, GetActualRow(bottomVisualRow));

            return (bottomRow + 1).Digits();
        }
    }

    protected readonly Scrollable scrollable;
    // These values need to be stored, since WPF doesn't like accessing them directly from the scrollable
    protected Point scrollablePosition;
    protected Size scrollableSize;

    protected virtual SKFont Font => FontManager.SKEditorFontRegular;

    private readonly PixelLayout pixelLayout = new();
    private readonly Panel activePopupPanel = new();
    internal PopupMenu? ActivePopupMenu => activePopupPanel.Visible ? activePopupPanel.Content as PopupMenu : null;

    /// Toggle for displaying line numbers
    public bool ShowLineNumbers = true;

    // Padding values to expand past the content area
    public float PaddingRight = 50.0f;
    public float PaddingBottom = 100.0f;

    // When editing a long line and moving to a short line, "remember" the column on the long line, unless the caret has been moved.
    public int DesiredVisualCol;
    // Offset from the left accounting for line numbers
    protected float textOffsetX;

    // Wrap long lines into multiple visual lines
    private readonly Dictionary<int, WrapEntry> lineWraps = new();
    // Foldings can collapse sections of the document
    private readonly List<Folding> foldings = [];

    // Visual lines are all lines shown in the editor
    // A single actual line may occupy multiple visual lines
    protected int[] actualToVisualRows = [];
    private readonly List<int> visualToActualRows = [];

    // Retain previous settings from Find-dialog
    private string lastFindQuery = string.Empty;
    private bool lastFindMatchCase = false;

    public TextViewer(Document document, Scrollable scrollable) {
        this.scrollable = scrollable;
        Document = document;

        CanFocus = true;
        Cursor = Cursors.IBeam;

        SetupBackgroundColor();

        pixelLayout.Add(activePopupPanel, 0, 0);
        Content = pixelLayout;

        // Need to redraw the line numbers when scrolling horizontally
        scrollable.Scroll += (_, _) => {
            scrollablePosition = scrollable.ScrollPosition;

            // Only update if required
            if (ShowLineNumbers && VisibleLineNumberDigits is var newVisibleDigits && lastVisibleLineNumberDigits != newVisibleDigits) {
                lastVisibleLineNumberDigits = newVisibleDigits;
                Recalc();
            } else {
                RecalcPopupMenu();
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

        ContextMenu = CreateContextMenu();
        Settings.KeyBindingsChanged += () => ContextMenu = CreateContextMenu();
        Settings.ThemeChanged += () => ContextMenu = CreateContextMenu();
    }

    public virtual ContextMenu CreateContextMenu() {
        return new() {
            Items = {
                Copy.CreateItem(this),
                new SeparatorMenuItem(),
                SelectAll.CreateItem(this),
                SelectBlock.CreateItem(this),
                new SeparatorMenuItem(),
                Find.CreateItem(this),
                GoTo.CreateItem(this),
                ToggleFolding.CreateItem(this),
            }
        };
    }

    /// Recalculates all values and invalidates the paint.
    protected virtual void Recalc() {
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

        lineWraps.Clear();
        foldings.Clear();

        var activeCollapses = new HashSet<int>();
        var activeFoldings = new Dictionary<int, int>(); // depth -> startRow

        Array.Resize(ref actualToVisualRows, Document.Lines.Count);
        visualToActualRows.Clear();

        // Assign all collapsed lines to the visual row of the collapse start
        for (int row = 0, visualRow = 0; row < Document.Lines.Count; row++) {
            string line = Document.Lines[row];
            string trimmed = line.TrimStart();

            bool startedCollapse = false;
            if (Document.FindFirstAnchor(anchor => anchor.Row == row && anchor.UserData is CollapseAnchorData) != null) {
                activeCollapses.Add(row);

                if (activeCollapses.Count == 1) {
                    startedCollapse = true;
                }
            }

            // Skip collapsed lines, but still process the starting line of a collapse
            // Needs to be done before checking for the collapse end
            bool skipLine = activeCollapses.Count != 0 && !startedCollapse;

            // Create foldings for lines with the same amount of #'s (minimum 2)
            if (GetFoldingHeaderDepth(trimmed, out int depth)) {
                if (activeFoldings.Remove(depth, out int startRow)) {
                    int startIdx = GetFoldingHeaderText(Document.Lines[startRow]);

                    // End the previous folding and start a new one if the end comment isn't empty
                    int endStartIdx = GetFoldingHeaderText(trimmed);
                    bool isStartStop = endStartIdx < trimmed.Length && !string.IsNullOrWhiteSpace(trimmed[endStartIdx..]);

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

            // Wrap comments into multiple lines when hitting the left edge
            if (ShouldWrapLine(trimmed, out int startOffset)) {
                var wrappedLines = new List<WrapLine>();

                const int charPadding = 1;
                float charWidth = (scrollableSize.Width - textOffsetX) / Font.CharWidth() - 1 - charPadding; // -1 because we overshoot by 1 while iterating

                int idx = startOffset;
                while (idx < line.Length) {
                    int subIdx = idx == startOffset ? startOffset : 0;
                    int startIdx = idx == startOffset ? 0 : idx;
                    int endIdx = -1;
                    for (; idx < line.Length; idx++, subIdx++) {
                        char c = line[idx];

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
                    lineWraps.Add(row, (startOffset, wrappedLines.ToArray()));
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
        int visibleDigits = ShowLineNumbers ? VisibleLineNumberDigits : 0;

        // Only when the alignment is to the left, the folding indicator can fit into the existing space
        float foldingWidth = !hasFoldings ? 0.0f : Settings.Instance.LineNumberAlignment switch {
             LineNumberAlignment.Left => Font.CharWidth() * ((foldings[^1].MinRow + 1).Digits() + 1) + foldButtonPadding,
             LineNumberAlignment.Right => Font.CharWidth() * (visibleDigits + 1) + foldButtonPadding,
             _ => throw new UnreachableException(),
        };
        textOffsetX = ShowLineNumbers
            ? Math.Max(foldingWidth, Font.CharWidth() * visibleDigits) + LineNumberPadding * 3.0f
            : hasFoldings
                ? foldingWidth + LineNumberPadding * 3.0f
                : LineNumberPadding;

        var targetSize = new Size((int)(width + textOffsetX + PaddingRight), (int)(height + PaddingBottom));
        Size = targetSize;
        PreferredSizeChanged?.Invoke(targetSize);

        RecalcPopupMenu();
        Invalidate();
    }
    public void RecalcPopupMenu() {
        // Update popup-menu position
        if (ActivePopupMenu is not { } menu) {
            return;
        }

        const int menuXOffset = 8;
        const int menuYOffset = 7;
        const int menuLPadding = 7;
        const int menuRPadding = 20;

        const float menuMaxHeightFactor = 0.5f;
        const int menuMinMaxHeight = 250;

        float carX = Font.CharWidth() * Document.Caret.Col;
        float carY = Font.LineHeight() * (actualToVisualRows[Document.Caret.Row] + 1);

        int menuX, menuY;

        void UpdateMenuH() {
            menuX = (int)(carX + scrollablePosition.X + textOffsetX + menuXOffset);
            int menuMaxRight = scrollablePosition.X + scrollableSize.Width - menuRPadding - (menu.VScrollBarVisible ? Studio.ScrollBarSize : 0);
            int menuMaxW = menuMaxRight - menuX;

            // Try moving the menu to the left when there isn't enough space, before having to shrink it
            int recommendedWidth = menu.RecommendedWidth;
            if (menuMaxW < recommendedWidth) {
                menuX = (int)Math.Max(menuMaxRight - recommendedWidth, scrollablePosition.X + textOffsetX + menuLPadding);
                menuMaxW = menuMaxRight - menuX;
            }

            menu.ContentWidth = Math.Min(recommendedWidth, menuMaxW);
        }
        void UpdateMenuV() {
            int menuMaxHeight = Math.Max(menuMinMaxHeight, (int)(scrollableSize.Height * menuMaxHeightFactor));
            int menuHeight = Math.Min(menuMaxHeight, menu.ContentHeight);

            int menuYBelow = (int)(carY + menuYOffset);
            int menuYAbove = (int)Math.Max(carY - Font.LineHeight() - menuYOffset - menuHeight, scrollablePosition.Y + menuYOffset);

            int menuMaxHBelow = (int)(scrollablePosition.Y + scrollableSize.Height - Font.LineHeight() - menuYBelow) - (menu.HScrollBarVisible ? Studio.ScrollBarSize : 0);
            int menuMaxHAbove = (int)(Math.Min(scrollablePosition.Y + scrollableSize.Height, carY) - Font.LineHeight() - menuYOffset - menuYAbove) - (menu.HScrollBarVisible ? Studio.ScrollBarSize : 0);

            // Chose above / below caret depending on which provides more height. Default to below
            int menuMaxH;
            if (Math.Min(menuHeight, menuMaxHBelow) >= Math.Min(menuHeight, menuMaxHAbove)) {
                menuY = menuYBelow;
                menuMaxH = menuMaxHBelow;
            } else {
                menuY = menuYAbove;
                menuMaxH = menuMaxHAbove;
            }

            menu.ContentHeight = Math.Min(menuHeight, menuMaxH);
        }

        // Calculate required content size
        menu.Recalc();
        // Both depend on each-other, so one needs to be updated twice
        UpdateMenuH();
        UpdateMenuV();
        UpdateMenuH();

        pixelLayout.Move(activePopupPanel, menuX, menuY);

        Invalidate();
    }

    protected void BaseOnKeyDown(KeyEventArgs e) => base.OnKeyDown(e);
    protected override void OnKeyDown(KeyEventArgs e) {
        var mods = e.Modifiers;
        if (e.Key is Keys.LeftShift or Keys.RightShift) mods |= Keys.Shift;
        if (e.Key is Keys.LeftControl or Keys.RightControl) mods |= Keys.Control;
        if (e.Key is Keys.LeftAlt or Keys.RightAlt) mods |= Keys.Alt;
        if (e.Key is Keys.LeftApplication or Keys.RightApplication) mods |= Keys.Application;
        UpdateMouseCursor(PointFromScreen(Mouse.Position), mods);

        // Handle bindings
        if (e.Key != Keys.None && CheckHotkey(Hotkey.FromEvent(e))) {
            e.Handled = true;
            return;
        }

        switch (e.Key) {
            case Keys.Left when !e.HasAlternateModifier(): // Prevent Alt+Left from getting handled
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.WordLeft : CaretMovementType.CharLeft, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.WordRight : CaretMovementType.CharRight, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Up:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.LabelUp : CaretMovementType.LineUp, updateSelection: e.Shift);
                e.Handled = true;
                break;
            case Keys.Down:
                MoveCaret(e.HasCommonModifier() ? CaretMovementType.LabelDown : CaretMovementType.LineDown, updateSelection: e.Shift);
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
        }

        if (e.Handled) {
            Recalc();
            ScrollCaretIntoView();
        }
    }

    protected virtual bool CheckHotkey(Hotkey hotkey) {
        // Handle bindings
        foreach (var binding in AllBindings) {
            foreach (var entry in binding.InstanceEntries) {
                if (Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey) == hotkey && entry.Action(this)) {
                    Recalc();
                    ScrollCaretIntoView();

                    return true;
                }
            }
        }

        return false;
    }

    #region Visual Rows

    protected CaretPosition GetVisualPosition(CaretPosition position) {
        if (!lineWraps.TryGetValue(position.Row, out var wrap))
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
        if (lineWraps.TryGetValue(row, out var wrap)) {
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

    protected int GetActualRow(int visualRow, int? defaultRow = null) {
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

        if (lineWraps.TryGetValue(row, out var wrap)) {
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

    #region Editing Actions

    protected void OnCopy() {
        if (Document.Selection.Empty) {
            // Just copy entire line
            Clipboard.Instance.Clear();
            Clipboard.Instance.Text = Document.Lines[Document.Caret.Row] + Document.NewLine;
        } else {
            Clipboard.Instance.Clear();
            Clipboard.Instance.Text = Document.GetSelectedText();
        }
    }

    private void OnSelectAll() {
        Document.Selection.Start = new CaretPosition(0, 0);
        Document.Selection.End = new CaretPosition(Document.Lines.Count - 1, Document.Lines[^1].Length);
        Recalc();
    }

    private void OnSelectBlock() {
        // Search first empty line above/below caret
        int above = Document.Caret.Row;
        while (above > 0 && !string.IsNullOrWhiteSpace(Document.Lines[above - 1])) {
            above--;
        }

        int below = Document.Caret.Row;
        while (below < Document.Lines.Count - 1 && !string.IsNullOrWhiteSpace(Document.Lines[below + 1])) {
            below++;
        }

        Document.Selection.Start = new CaretPosition(above, 0);
        Document.Selection.End = new CaretPosition(below, Document.Lines[below].Length);
        Recalc();
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

    protected virtual void OnGoTo() {
        Document.Caret.Row = GoToDialog.Show(Document, owner: this);
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

    #endregion

    #region Popup Menu

    public void OpenPopupMenu(PopupMenu menu) {
        activePopupPanel.Content = menu;
        activePopupPanel.Visible = true;

        menu.Visible = true;
    }
    public void ClosePopupMenu() {
        if (ActivePopupMenu is { } menu) {
            menu.Visible = false;
        }

        activePopupPanel.Visible = false;
    }

    #endregion

    #region Caret Movement

    public enum SnappingDirection { Ignore, Left, Right }

    public virtual CaretPosition ClampCaret(CaretPosition position, bool wrapLine = false, SnappingDirection direction = SnappingDirection.Ignore) {
        // Wrap around to prev/next line
        if (wrapLine && position.Row > 0 && position.Col < 0) {
            position.Row = GetNextVisualLinePosition(-1, position).Row;
            position.Col = DesiredVisualCol = Document.Lines[position.Row].Length;
        } else if (wrapLine && position.Row < Document.Lines.Count && position.Col > Document.Lines[position.Row].Length) {
            position.Row = GetNextVisualLinePosition( 1, position).Row;
            position.Col = DesiredVisualCol = 0;
        }

        int maxVisualRow = GetActualRow(actualToVisualRows[^1]);

        // Clamp to document (also visually)
        position.Row = Math.Clamp(position.Row, 0, Math.Min(maxVisualRow, Document.Lines.Count - 1));
        position.Col = Math.Clamp(position.Col, 0, Document.Lines[position.Row].Length);

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

    /// Move caret to appropriate target position for current context
    protected virtual CaretPosition GetCaretMovementTarget(CaretMovementType direction) {
        // Regular text movement
        return GetNewTextCaretPosition(direction);
    }
    protected virtual void MoveCaretTo(CaretPosition newCaret, bool updateSelection) {
        var oldCaret = Document.Caret;

        // Apply / Update desired column
        var newVisualPos = GetVisualPosition(newCaret);
        if (oldCaret.Row != newCaret.Row) {
            newVisualPos.Col = DesiredVisualCol;
        } else {
            DesiredVisualCol = newVisualPos.Col;
        }
        Document.Caret = ClampCaret(GetActualPosition(newVisualPos));

        if (updateSelection) {
            if (Document.Selection.Empty) {
                Document.Selection.Start = oldCaret;
            }

            Document.Selection.End = Document.Caret;
        } else {
            Document.Selection.Start = Document.Selection.End = Document.Caret;
        }

        OnCaretMoved(oldCaret, Document.Caret);

        ClosePopupMenu();
    }
    /// Properly move caret into desired direction
    /// Will adjust the currently active selection if requested
    protected virtual void MoveCaret(CaretMovementType direction, bool updateSelection) {
        if (!Document.Selection.Empty && !updateSelection) {
            Document.Caret = direction switch {
                CaretMovementType.CharLeft  or CaretMovementType.WordLeft  or CaretMovementType.LineUp   or CaretMovementType.PageUp   or CaretMovementType.LabelUp   or CaretMovementType.LineStart => Document.Selection.Min,
                CaretMovementType.CharRight or CaretMovementType.WordRight or CaretMovementType.LineDown or CaretMovementType.PageDown or CaretMovementType.LabelDown or CaretMovementType.LineEnd   => Document.Selection.Max,
                _ => Document.Caret,
            };
        }

        MoveCaretTo(GetCaretMovementTarget(direction), updateSelection);
        ScrollCaretIntoView(center: direction is CaretMovementType.LabelUp or CaretMovementType.LabelDown);
    }

    // For regular text movement
    protected CaretPosition GetNewTextCaretPosition(CaretMovementType direction) =>
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

    protected CaretPosition GetNextVisualLinePosition(int dist, CaretPosition position) {
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

    protected void CollapseSelection() {
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
        if (e.Buttons.HasFlag(MouseButtons.Primary)) {
            // Refocus in case something unfocused the editor
            Focus();

            if (LocationToFolding(e.Location) is { } folding) {
                ToggleCollapse(folding.MinRow);

                e.Handled = true;
                Recalc();
                return;
            }

            primaryMouseButtonDown = true;

            var (actualPos, visualPos) = LocationToCaretPosition(e.Location);

            DesiredVisualCol = visualPos.Col;
            MoveCaretTo(actualPos, e.Modifiers.HasFlag(Keys.Shift));
            ScrollCaretIntoView();

            e.Handled = true;
            Recalc();
            return;
        }

        if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
            ContextMenu?.Show();
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

            DesiredVisualCol = visual.Col;
            ScrollCaretIntoView();

            ClosePopupMenu();

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
        if (Settings.Instance.ScrollSpeed > 0.0f) {
            // Manually forward since WPF won't do it for us
            if (Eto.Platform.Instance.IsWpf && ActivePopupMenu != null) {
                var pos = ActivePopupMenu.PointFromScreen(Mouse.Position);
                if (pos.X >= 0.0f & pos.X <= ActivePopupMenu.Width &&
                    pos.Y >= 0.0f & pos.Y <= ActivePopupMenu.Height)
                {
                    ActivePopupMenu.ScrollPosition = ActivePopupMenu.ScrollPosition with {
                        Y = Math.Clamp((int)(ActivePopupMenu.ScrollPosition.Y - e.Delta.Height * ActivePopupMenu.EntryHeight * Settings.Instance.ScrollSpeed), 0, ActivePopupMenu.ScrollSize.Height - ActivePopupMenu.ClientSize.Height)
                    };
                    e.Handled = true;
                    return;
                }
            }

            // Manually scroll to respect our scroll speed
            scrollable.ScrollPosition = scrollable.ScrollPosition with {
                Y = Math.Clamp((int)(scrollable.ScrollPosition.Y - e.Delta.Height * Font.LineHeight() * Settings.Instance.ScrollSpeed), 0, Math.Max(0, Height - scrollable.ClientSize.Height))
            };
            e.Handled = true;
        }

        base.OnMouseWheel(e);
    }

    protected virtual void UpdateMouseCursor(PointF location, Keys modifiers) {
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

    protected (CaretPosition Actual, CaretPosition Visual) LocationToCaretPosition(PointF location, SnappingDirection direction = SnappingDirection.Ignore) {
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

    #endregion

    #region Folding

    protected struct Folding {
        public int MinRow, MaxRow;

        public int StartCol;
    }
    private struct CollapseAnchorData;

    protected virtual bool GetFoldingHeaderDepth(string trimmed, out int depth) {
        depth = -1;
        return false;
    }
    protected virtual int GetFoldingHeaderText(string line) {
        return -1;
    }

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

    protected Folding? GetCollapse(int row) {
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

    #region Line Wrapping

    protected virtual bool ShouldWrapLine(string line, out int textStartIdx) {
        textStartIdx = -1;
        return false;
    }

    #endregion

    #region Drawing

    public override int DrawX => scrollablePosition.X;
    public override int DrawY => scrollablePosition.Y;
    public override int DrawWidth => scrollable.Width;
    public override int DrawHeight => scrollable.Height;
    public override bool CanDraw => !Document.UpdateInProgress;

    protected virtual void SetupBackgroundColor() {
        BackgroundColor = Settings.Instance.Theme.Background;
        Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.Background;
    }

    protected virtual void DrawLine(SKCanvas canvas, string line, float x, float y) {
        canvas.DrawText(line, x, y + Font.Offset(), Font, Settings.Instance.Theme.StatusFgPaint);
    }
    protected virtual void DrawCurrentLineHighlight(SKCanvas canvas, float carY, SKPaint fillPaint) {
        fillPaint.ColorF = Settings.Instance.Theme.CurrentLine.ToSkia();
        canvas.DrawRect(
            x: scrollablePosition.X,
            y: carY,
            w: scrollable.Width,
            h: Font.LineHeight(),
            fillPaint);
    }
    protected virtual void DrawWrappedLine(SKCanvas canvas, string subLine, float x, float y) {
        canvas.DrawText(subLine, x, y + Font.Offset(), Font, Settings.Instance.Theme.CommentPaint.ForegroundColor);
    }
    protected virtual void DrawCollapseBox(SKCanvas canvas, float x, float y, float w, float h) {
        const float foldingPadding = 1.0f;
        canvas.DrawRect(
            x: x - foldingPadding,
            y: y - foldingPadding,
            w: w + foldingPadding * 2.0f,
            h: h + foldingPadding * 2.0f,
            Settings.Instance.Theme.CommentBoxPaint);
    }

    protected virtual void DrawLineNumberBackground(SKCanvas canvas, SKPaint fillPaint, SKPaint strokePaint, float yPos) {
        fillPaint.ColorF = Settings.Instance.Theme.Background.ToSkia();
        canvas.DrawRect(
            x: scrollablePosition.X,
            y: scrollablePosition.Y,
            w: textOffsetX - LineNumberPadding,
            h: scrollableSize.Height,
            fillPaint);

        strokePaint.ColorF = Settings.Instance.Theme.ServiceLine.ToSkia();
        canvas.DrawLine(
            x0: scrollablePosition.X + textOffsetX - LineNumberPadding, y0: 0.0f,
            x1: scrollablePosition.X + textOffsetX - LineNumberPadding, y1: yPos + scrollableSize.Height,
            strokePaint);
    }
    protected virtual Color GetLineNumberBackground(int row) {
        return Settings.Instance.Theme.LineNumber;
    }

    public override void Draw(SKSurface surface) {
        var canvas = surface.Canvas;

        using var strokePaint = new SKPaint();
        strokePaint.Style = SKPaintStyle.Stroke;

        using var fillPaint = new SKPaint();
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = true;

        // To be reused below. Kinda annoying how C# handles out parameter conflicts
        WrapEntry wrap;

        int topVisualRow = (int)(scrollablePosition.Y / Font.LineHeight()) - OffscreenLinePadding;
        int bottomVisualRow = (int)((scrollablePosition.Y + scrollableSize.Height) / Font.LineHeight()) + OffscreenLinePadding;
        int topRow = Math.Max(0, GetActualRow(topVisualRow));
        int bottomRow = Math.Min(Document.Lines.Count - 1, GetActualRow(bottomVisualRow));

        // Draw text
        float yPos = actualToVisualRows[topRow] * Font.LineHeight();
        for (int row = topRow; row <= bottomRow; row++) {
            string line = Document.Lines[row];

            if (GetCollapse(row) is { } collapse) {
                float width = 0.0f;
                float height = 0.0f;
                if (lineWraps.TryGetValue(row, out wrap)) {
                    for (int i = 0; i < wrap.Lines.Length; i++) {
                        string subLine = wrap.Lines[i].Line;
                        float xIdent = i == 0 ? 0 : wrap.StartOffset * Font.CharWidth();

                        DrawWrappedLine(canvas, subLine, textOffsetX + xIdent, yPos);
                        yPos += Font.LineHeight();
                        width = Math.Max(width, Font.MeasureWidth(subLine) + xIdent);
                        height += Font.LineHeight();
                    }
                } else {
                    DrawLine(canvas, line, textOffsetX, yPos);
                    yPos += Font.LineHeight();
                    width = Font.MeasureWidth(line);
                    height = Font.LineHeight();
                }

                DrawCollapseBox(canvas,
                    x: Font.CharWidth() * collapse.StartCol + textOffsetX,
                    y: yPos - height,
                    w: width - Font.CharWidth() * collapse.StartCol,
                    h: height);

                row = collapse.MaxRow;
                continue;
            }

            if (lineWraps.TryGetValue(row, out wrap)) {
                for (int i = 0; i < wrap.Lines.Length; i++) {
                    string subLine = wrap.Lines[i].Line;
                    float xIdent = i == 0 ? 0 : wrap.StartOffset * Font.CharWidth();

                    DrawWrappedLine(canvas, subLine, textOffsetX + xIdent, yPos);
                    yPos += Font.LineHeight();
                }
            } else {
                DrawLine(canvas, line, textOffsetX, yPos);
                yPos += Font.LineHeight();
            }
        }

        var caretPos = GetVisualPosition(Document.Caret);
        float carX = Font.CharWidth() * caretPos.Col + textOffsetX;
        float carY = Font.LineHeight() * caretPos.Row;

        // Highlight caret line
        DrawCurrentLineHighlight(canvas, carY, fillPaint);

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

        // Draw line numbers
        if (ShowLineNumbers) {
            DrawLineNumberBackground(canvas, fillPaint, strokePaint, yPos);

            yPos = actualToVisualRows[topRow] * Font.LineHeight();
            for (int row = topRow; row <= bottomRow; row++) {
                int oldRow = row;
                var numberString = (row + 1).ToString();

                fillPaint.ColorF = GetLineNumberBackground(row).ToSkia();

                if (Settings.Instance.LineNumberAlignment == LineNumberAlignment.Left) {
                    canvas.DrawText(numberString, scrollablePosition.X + LineNumberPadding, yPos + Font.Offset(), Font, fillPaint);
                } else if (Settings.Instance.LineNumberAlignment == LineNumberAlignment.Right) {
                    float ident = Font.CharWidth() * (bottomRow.Digits() - (row + 1).Digits());
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

                if (lineWraps.TryGetValue(oldRow, out wrap)) {
                    yPos += Font.LineHeight() * wrap.Lines.Length;
                } else {
                    yPos += Font.LineHeight();
                }
            }
        }
    }

    #endregion
}
