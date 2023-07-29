﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CelesteStudio.Entities;
using StudioCommunication;

namespace CelesteStudio.RichText;

public class RichText : UserControl {
    private const int minLeftIndent = 8;
    private const int maxBracketSearchIterations = 1000;
    private const int maxLinesForFolding = 3000;
    private const int minLinesForAccuracy = 100000;
    private const int WM_IME_SETCONTEXT = 0x0281;
    private const int WM_HSCROLL = 0x114;
    private const int WM_VSCROLL = 0x115;
    private const int SB_ENDSCROLL = 0x8;

    private const Keys AltShift = Keys.Alt | Keys.Shift;
    private static readonly Regex AllSpaceRegex = new(@"^\s+$", RegexOptions.Compiled);

    internal readonly List<LineInfo> lineInfos = new();

    private readonly Range selection;
    private readonly List<VisualMarker> visibleMarkers = new();
    internal bool allowInsertRemoveLines = true;
    private Brush backBrush;

    private bool caretVisible;

    private Color changedLineBgColor,
        changedLineTextColor,
        currentLineColor,
        currentTextColor,
        playingLineBgColor,
        playingLineTextColor,
        foldingIndicatorColor,
        indentBackColor,
        paddingBackColor,
        lineNumberColor,
        saveStateBgColor,
        saveStateTextColor,
        serviceLinesColor,
        selectionColor;

    private int playingLine;
    private string currentLineSuffix;
    private string descriptionFile;
    private int saveStateLine = -1;

    protected Dictionary<int, int> foldingPairs = new();
    public bool InsertLocked = false;
    private Language language;
    private Keys lastModifiers;
    private DateTime lastNavigatedDateTime;
    private uint lineNumberStartValue;
    private TextSource lines;
    private IntPtr m_hImc;

    private bool mouseIsDrag,
        multiline,
        needRecalc,
        showLineNumbers,
        showFoldingLines,
        needRecalcFoldingLines,
        wordWrap,
        scrollBars,
        handledChar,
        highlightFoldingIndicator,
        isChanged;

    private Range rightBracketPosition,
        rightBracketPosition2,
        updatingRange,
        leftBracketPosition,
        leftBracketPosition2,
        delayedTextChangedRange;

    public int SaveStateLine {
        get => saveStateLine;
        set {
            if (saveStateLine == value) {
                return;
            }

            saveStateLine = value;
            Invalidate();
        }
    }

    private int startFoldingLine = -1,
        updating,
        wordWrapLinesCount,
        maxLineLength = 0,
        preferredLineWidth,
        leftPadding,
        lineInterval,
        endFoldingLine = -1,
        charHeight;

    private WordWrapMode wordWrapMode = WordWrapMode.WordWrapControlWidth;

    public RichText() : this(null) { }

    public RichText(TextSource ts = null) {
        //drawing optimization
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        Font = new Font(FontFamily.GenericMonospace, 9.75f);
        InitTextSource(ts == null ? CreateTextSource() : ts);
        if (lines.Count == 0) {
            lines.InsertLine(0, lines.CreateLine());
        }

        selection = new Range(this) {Start = new Place(0, 0)};

        Cursor = Cursors.IBeam;
        BackColor = Color.White;
        LineNumberColor = Color.Teal;
        IndentBackColor = Color.White;
        ServiceLinesColor = Color.Silver;
        FoldingIndicatorColor = Color.Green;
        CurrentLineColor = Color.FromArgb(50, 0, 0, 0);
        CurrentTextColor = Color.Green;
        ChangedLineBgColor = Color.Transparent;
        ChangedLineTextColor = Color.Teal;
        PlayingLineBgColor = Color.Transparent;
        PlayingLineTextColor = Color.Teal;
        SaveStateTextColor = Color.White;
        SaveStateBgColor = Color.SteelBlue;
        HighlightFoldingIndicator = true;
        ShowLineNumbers = true;
        TabLength = 4;
        FoldedBlockStyle = new FoldedBlockStyle(Brushes.Gray, null, FontStyle.Regular);
        SelectionColor = Color.Blue;
        BracketsStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(80, Color.Lime)));
        BracketsStyle2 = new MarkerStyle(new SolidBrush(Color.FromArgb(60, Color.Red)));
        AllowSeveralTextStyleDrawing = false;
        LeftBracket = '\x0';
        RightBracket = '\x0';
        LeftBracket2 = '\x0';
        RightBracket2 = '\x0';
        SyntaxHighlighter = new SyntaxHighlighter();
        language = Language.Custom;
        PreferredLineWidth = 0;
        needRecalc = true;
        lastNavigatedDateTime = DateTime.Now;
        AutoIndent = true;
        CommentPrefix = "//";
        lineNumberStartValue = 1;
        multiline = true;
        scrollBars = true;
        AcceptsTab = true;
        AcceptsReturn = true;
        caretVisible = true;
        CaretColor = Color.Black;
        PlayingLine = -1;
        Paddings = new Padding(0, 0, 0, 0);
        PaddingBackColor = Color.Transparent;
        DisabledColor = Color.FromArgb(100, 180, 180, 180);
        needRecalcFoldingLines = true;
        AllowDrop = true;
        base.AutoScroll = true;
    }

    public string CurrentFileName { get; set; }

    public string CurrentStartLineText => Lines[Selection.Start.iLine];
    public string CurrentEndLineText => Lines[Selection.End.iLine];

    /// <summary>
    /// Indicates if tab characters are accepted as input
    /// </summary>
    [DefaultValue(true)]
    [Description("Indicates if tab characters are accepted as input.")]
    public bool AcceptsTab { get; set; }

    /// <summary>
    /// Indicates if return characters are accepted as input
    /// </summary>
    [DefaultValue(true)]
    [Description("Indicates if return characters are accepted as input.")]
    public bool AcceptsReturn { get; set; }

    /// <summary>
    /// Shows or hides the caret
    /// </summary>
    [DefaultValue(true)]
    [Description("Shows or hides the caret")]
    public bool CaretVisible {
        get => caretVisible;
        set {
            caretVisible = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Background color for current line
    /// </summary>
    [DefaultValue(typeof(Color), "Transparent")]
    [Description("Background color for current line. Set to Color.Transparent to hide current line highlighting")]
    public Color CurrentLineColor {
        get => currentLineColor;
        set {
            currentLineColor = value;
            Invalidate();
        }
    }

    [DefaultValue(typeof(Color), "Green")]
    [Description("Background color for current line. Set to Color.Transparent to hide current line highlighting")]
    public Color CurrentTextColor {
        get => currentTextColor;
        set {
            currentTextColor = value;
            Invalidate();
        }
    }

    [DefaultValue(typeof(Color), "Transparent")]
    [Description("Background color for active line. Set to Color.Transparent to hide current line highlighting")]
    public Color PlayingLineBgColor {
        get => playingLineBgColor;
        set {
            playingLineBgColor = value;
            Invalidate();
        }
    }

    public Color PlayingLineTextColor {
        get => playingLineTextColor;
        set {
            playingLineTextColor = value;
            Invalidate();
        }
    }

    [DefaultValue(typeof(int), "-1"), Browsable(false)]
    public int PlayingLine {
        get => playingLine;
        set {
            if (playingLine == value) {
                return;
            }

            playingLine = value;
            if (playingLine >= 0) {
                Selection = new Range(this, 4, playingLine, 4, playingLine);
                DoSelectionVisible();
            }

            Invalidate();
        }
    }

    [DefaultValue(typeof(string), null), Browsable(false)]
    public string CurrentLineSuffix {
        get => currentLineSuffix;
        set {
            if (currentLineSuffix == value) {
                return;
            }

            currentLineSuffix = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Background color for highlighting of changed lines
    /// </summary>
    [DefaultValue(typeof(Color), "Transparent")]
    [Description(
        "Background color for highlighting of changed lines. Set to Color.Transparent to hide changed line highlighting"
    )]
    public Color ChangedLineBgColor {
        get => changedLineBgColor;
        set {
            changedLineBgColor = value;
            Invalidate();
        }
    }

    public Color ChangedLineTextColor {
        get => changedLineTextColor;
        set {
            changedLineTextColor = value;
            Invalidate();
        }
    }

    public Color SaveStateBgColor {
        get => saveStateBgColor;
        set {
            saveStateBgColor = value;
            Invalidate();
        }
    }

    public Color SaveStateTextColor {
        get => saveStateTextColor;
        set {
            saveStateTextColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Fore color (default style color)
    /// </summary>
    public override Color ForeColor {
        get => base.ForeColor;
        set {
            base.ForeColor = value;
            lines.InitDefaultStyle();
            Invalidate();
        }
    }

    /// <summary>
    /// Height of char in pixels
    /// </summary>
    [Description("Height of char in pixels")]
    public int CharHeight {
        get => charHeight;
        private set {
            charHeight = value;
            OnCharSizeChanged();
        }
    }

    /// <summary>
    /// Interval between lines (in pixels)
    /// </summary>
    [Description("Interval between lines in pixels")]
    [DefaultValue(0)]
    public int LineInterval {
        get => lineInterval;
        set {
            lineInterval = value;
            Font = Font;
            Invalidate();
        }
    }

    /// <summary>
    /// Width of char in pixels
    /// </summary>
    [Description("Width of char in pixels")]
    public int CharWidth { get; private set; }

    /// <summary>
    /// Spaces count for tab
    /// </summary>
    [DefaultValue(4)]
    [Description("Spaces count for tab")]
    public int TabLength { get; set; }

    /// <summary>
    /// Text was changed
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsChanged {
        get => isChanged;
        set {
            if (!value) {
                lines.ClearIsChanged();
                NoChanges?.Invoke(this, new EventArgs());
            }

            isChanged = value;
        }
    }

    /// <summary>
    /// Text version
    /// </summary>
    /// <remarks>This counter is incremented each time changes the text</remarks>
    [Browsable(false)]
    public int TextVersion { get; private set; }

    /// <summary>
    /// Read only
    /// </summary>
    [DefaultValue(false)]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Shows line numbers.
    /// </summary>
    [DefaultValue(true)]
    [Description("Shows line numbers.")]
    public bool ShowLineNumbers {
        get => showLineNumbers;
        set {
            showLineNumbers = value;
            NeedRecalc();
            Invalidate();
        }
    }

    /// <summary>
    /// Shows vertical lines between folding start line and folding end line.
    /// </summary>
    [DefaultValue(false)]
    [Description("Shows vertical lines between folding start line and folding end line.")]
    public bool ShowFoldingLines {
        get => showFoldingLines;
        set {
            showFoldingLines = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Color of line numbers.
    /// </summary>
    [DefaultValue(typeof(Color), "Teal")]
    [Description("Color of line numbers.")]
    public Color LineNumberColor {
        get => lineNumberColor;
        set {
            lineNumberColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Start value of first line number.
    /// </summary>
    [DefaultValue(typeof(uint), "1")]
    [Description("Start value of first line number.")]
    public uint LineNumberStartValue {
        get => lineNumberStartValue;
        set {
            lineNumberStartValue = value;
            needRecalc = true;
            Invalidate();
        }
    }

    /// <summary>
    /// Background color of indent area
    /// </summary>
    [DefaultValue(typeof(Color), "White")]
    [Description("Background color of indent area")]
    public Color IndentBackColor {
        get => indentBackColor;
        set {
            indentBackColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Background color of padding area
    /// </summary>
    [DefaultValue(typeof(Color), "Transparent")]
    [Description("Background color of padding area")]
    public Color PaddingBackColor {
        get => paddingBackColor;
        set {
            paddingBackColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Color of disabled component
    /// </summary>
    [DefaultValue(typeof(Color), "100;180;180;180")]
    [Description("Color of disabled component")]
    public Color DisabledColor { get; set; }

    /// <summary>
    /// Color of caret
    /// </summary>
    [DefaultValue(typeof(Color), "Black")]
    [Description("Color of caret.")]
    public Color CaretColor { get; set; }

    /// <summary>
    /// Color of service lines (folding lines, borders of blocks etc.)
    /// </summary>
    [DefaultValue(typeof(Color), "Silver")]
    [Description("Color of service lines (folding lines, borders of blocks etc.)")]
    public Color ServiceLinesColor {
        get => serviceLinesColor;
        set {
            serviceLinesColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Padings of text area
    /// </summary>
    [Browsable(true)]
    [Description("Paddings of text area.")]
    public Padding Paddings { get; set; }

    //hide parent padding
    [Browsable(false)]
    public new Padding Padding {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    //hide RTL
    [Browsable(false)]
    public new bool RightToLeft {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    /// <summary>
    /// Color of folding area indicator
    /// </summary>
    [DefaultValue(typeof(Color), "Green")]
    [Description("Color of folding area indicator.")]
    public Color FoldingIndicatorColor {
        get => foldingIndicatorColor;
        set {
            foldingIndicatorColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Enables folding indicator (left vertical line between folding bounds)
    /// </summary>
    [DefaultValue(true)]
    [Description("Enables folding indicator (left vertical line between folding bounds)")]
    public bool HighlightFoldingIndicator {
        get => highlightFoldingIndicator;
        set {
            highlightFoldingIndicator = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Left indent in pixels
    /// </summary>
    [Browsable(false)]
    [Description("Left indent in pixels")]
    public int LeftIndent { get; private set; }

    /// <summary>
    /// Left padding in pixels
    /// </summary>
    [DefaultValue(0)]
    [Description("Width of left service area (in pixels)")]
    public int LeftPadding {
        get => leftPadding;
        set {
            leftPadding = value;
            Invalidate();
        }
    }

    /// <summary>
    /// This property draws vertical line after defined char position.
    /// Set to 0 for disable drawing of vertical line.
    /// </summary>
    [DefaultValue(0)]
    [Description("This property draws vertical line after defined char position. Set to 0 for disable drawing of vertical line.")]
    public int PreferredLineWidth {
        get => preferredLineWidth;
        set {
            preferredLineWidth = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Styles
    /// Maximum style count is 16
    /// </summary>
    [Browsable(false)]
    public Style[] Styles => lines.Styles;

    /// <summary>
    /// Default text style
    /// This style is using when no one other TextStyle is not defined in Char.style
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextStyle DefaultStyle {
        get => lines.DefaultStyle;
        set => lines.DefaultStyle = value;
    }

    /// <summary>
    /// Style for rendering Selection area
    /// </summary>
    [Browsable(false)]
    public SelectionStyle SelectionStyle { get; set; }

    /// <summary>
    /// Style for folded block rendering
    /// </summary>
    [Browsable(false)]
    public TextStyle FoldedBlockStyle { get; set; }

    /// <summary>
    /// Style for brackets highlighting
    /// </summary>
    [Browsable(false)]
    public MarkerStyle BracketsStyle { get; set; }

    /// <summary>
    /// Style for alternative brackets highlighting
    /// </summary>
    [Browsable(false)]
    public MarkerStyle BracketsStyle2 { get; set; }

    /// <summary>
    /// Opening bracket for brackets highlighting.
    /// Set to '\x0' for disable brackets highlighting.
    /// </summary>
    [DefaultValue('\x0')]
    [Description("Opening bracket for brackets highlighting. Set to '\\x0' for disable brackets highlighting.")]
    public char LeftBracket { get; set; }

    /// <summary>
    /// Closing bracket for brackets highlighting.
    /// Set to '\x0' for disable brackets highlighting.
    /// </summary>
    [DefaultValue('\x0')]
    [Description("Closing bracket for brackets highlighting. Set to '\\x0' for disable brackets highlighting.")]
    public char RightBracket { get; set; }

    /// <summary>
    /// Alternative opening bracket for brackets highlighting.
    /// Set to '\x0' for disable brackets highlighting.
    /// </summary>
    [DefaultValue('\x0')]
    [Description(
        "Alternative opening bracket for brackets highlighting. Set to '\\x0' for disable brackets highlighting.")]
    public char LeftBracket2 { get; set; }

    /// <summary>
    /// Alternative closing bracket for brackets highlighting.
    /// Set to '\x0' for disable brackets highlighting.
    /// </summary>
    [DefaultValue('\x0')]
    [Description(
        "Alternative closing bracket for brackets highlighting. Set to '\\x0' for disable brackets highlighting.")]
    public char RightBracket2 { get; set; }

    /// <summary>
    /// Comment line prefix.
    /// </summary>
    [DefaultValue("//")]
    [Description("Comment line prefix.")]
    public string CommentPrefix { get; set; }

    /// <summary>
    /// This property specifies which part of the text will be highlighted as you type (by built-in highlighter).
    /// </summary>
    /// <remarks>When a user enters text, a component of rebuilding the highlight (because the text is changed).
    /// This property specifies exactly which section of the text will be re-highlighted.
    /// This can be useful to highlight multi-line comments, for example.</remarks>
    [DefaultValue(typeof(HighlightingRangeType), "ChangedRange")]
    [Description("This property specifies which part of the text will be highlighted as you type.")]
    public HighlightingRangeType HighlightingRangeType { get; set; }

    /// <summary>
    /// Is keyboard in replace mode (wide caret) ?
    /// </summary>
    [Browsable(false)]
    public bool IsReplaceMode =>
        InsertLocked && Selection.IsEmpty &&
        Selection.Start.iChar < lines[Selection.Start.iLine].Count;

    /// <summary>
    /// Allows text rendering several styles same time.
    /// </summary>
    [Browsable(true)]
    [DefaultValue(false)]
    [Description("Allows text rendering several styles same time.")]
    public bool AllowSeveralTextStyleDrawing { get; set; }

    /// <summary>
    /// Allows AutoIndent. Inserts spaces before new line.
    /// </summary>
    [DefaultValue(true)]
    [Description("Allows auto indent. Inserts spaces before line chars.")]
    public bool AutoIndent { get; set; }

    /// <summary>
    /// Language for highlighting by built-in highlighter.
    /// </summary>
    [Browsable(true)]
    [DefaultValue(typeof(Language), "Custom")]
    [Description("Language for highlighting by built-in highlighter.")]
    public Language Language {
        get => language;
        set {
            language = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Syntax Highlighter
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SyntaxHighlighter SyntaxHighlighter { get; set; }

    /// <summary>
    /// XML file with description of syntax highlighting.
    /// This property works only with Language == Language.Custom.
    /// </summary>
    [Browsable(true)]
    [DefaultValue(null)]
    [Description("XML file with description of syntax highlighting. This property works only with Language == Language.Custom.")]
    public string DescriptionFile {
        get => descriptionFile;
        set {
            descriptionFile = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Position of left highlighted bracket.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Range LeftBracketPosition => leftBracketPosition;

    /// <summary>
    /// Position of right highlighted bracket.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Range RightBracketPosition => rightBracketPosition;

    /// <summary>
    /// Position of left highlighted alternative bracket.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Range LeftBracketPosition2 => leftBracketPosition2;

    /// <summary>
    /// Position of right highlighted alternative bracket.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Range RightBracketPosition2 => rightBracketPosition2;

    /// <summary>
    /// Start line index of current highlighted folding area. Return -1 if start of area is not found.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int StartFoldingLine => startFoldingLine;

    /// <summary>
    /// End line index of current highlighted folding area. Return -1 if end of area is not found.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int EndFoldingLine => endFoldingLine;

    /// <summary>
    /// TextSource
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextSource TextSource {
        get => lines;
        set => InitTextSource(value);
    }

    /// <summary>
    /// Returns current visible range of text
    /// </summary>
    [Browsable(false)]
    public Range VisibleRange => GetRange(PointToPlace(new Point(LeftIndent, 0)), PointToPlace(new Point(ClientSize.Width, ClientSize.Height)));

    /// <summary>
    /// Current selection range
    /// </summary>
    [Browsable(false)]
    public Range Selection {
        get => selection;
        set {
            selection.BeginUpdate();
            selection.Start = value.Start;
            selection.End = value.End;
            selection.EndUpdate();
            Invalidate();
        }
    }

    /// <summary>
    /// Background color.
    /// It is used if BackBrush is null.
    /// </summary>
    [DefaultValue(typeof(Color), "White")]
    [Description("Background color.")]
    public override Color BackColor {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    /// <summary>
    /// Background brush.
    /// If Null then BackColor is used.
    /// </summary>
    [Browsable(false)]
    public Brush BackBrush {
        get => backBrush;
        set {
            backBrush = value;
            Invalidate();
        }
    }

    [Browsable(true)]
    [DefaultValue(true)]
    [Description("Scollbars visibility.")]
    public bool ShowScrollBars {
        get => scrollBars;
        set {
            if (value == scrollBars) {
                return;
            }

            scrollBars = value;
            needRecalc = true;
            Invalidate();
        }
    }

    /// <summary>
    /// Multiline
    /// </summary>
    [Browsable(true)]
    [DefaultValue(true)]
    [Description("Multiline mode.")]
    public bool Multiline {
        get => multiline;
        set {
            if (multiline == value) {
                return;
            }

            multiline = value;
            needRecalc = true;
            if (multiline) {
                base.AutoScroll = true;
                ShowScrollBars = true;
            } else {
                base.AutoScroll = false;
                ShowScrollBars = false;
                if (lines.Count > 1) {
                    lines.RemoveLine(1, lines.Count - 1);
                }

                lines.Manager.ClearHistory();
            }

            Invalidate();
        }
    }

    /// <summary>
    /// WordWrap.
    /// </summary>
    [Browsable(true)]
    [DefaultValue(false)]
    [Description("WordWrap.")]
    public bool WordWrap {
        get => wordWrap;
        set {
            if (wordWrap == value) {
                return;
            }

            wordWrap = value;
            if (wordWrap) {
                Selection.ColumnSelectionMode = false;
            }

            RecalcWordWrap(0, LinesCount - 1);
            Invalidate();
        }
    }

    /// <summary>
    /// WordWrap mode.
    /// </summary>
    [Browsable(true)]
    [DefaultValue(typeof(WordWrapMode), "WordWrapControlWidth")]
    [Description("WordWrap mode.")]
    public WordWrapMode WordWrapMode {
        get => wordWrapMode;
        set {
            if (wordWrapMode == value) {
                return;
            }

            wordWrapMode = value;
            RecalcWordWrap(0, LinesCount - 1);
            Invalidate();
        }
    }


    /// <summary>
    /// Count of lines with wordwrap effect
    /// </summary>
    [Browsable(false)]
    public int WordWrapLinesCount {
        get {
            if (needRecalc) {
                Recalc();
            }

            return wordWrapLinesCount;
        }
    }

    /// <summary>
    /// Do not change this property
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool AutoScroll {
        get => base.AutoScroll;
        set { ; }
    }

    /// <summary>
    /// Count of lines
    /// </summary>
    [Browsable(false)]
    public int LinesCount => lines.Count;

    /// <summary>
    /// Gets or sets char and styleId for given place
    /// This property does not fire OnTextChanged event
    /// </summary>
    public Char this[Place place] {
        get => lines[place.iLine][place.iChar];
        set => lines[place.iLine][place.iChar] = value;
    }

    /// <summary>
    /// Gets Line
    /// </summary>
    public Line this[int iLine] => lines[iLine];

    /// <summary>
    /// Text of control
    /// </summary>
    [Browsable(true)]
    [Localizable(true)]
    [Editor(
        "System.ComponentModel.Design.MultilineStringEditor, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
        typeof(UITypeEditor))]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [Description("Text of the control.")]
    public override string Text {
        get {
            var sel = new Range(this);
            sel.SelectAll();
            return sel.Text;
        }

        set {
            SetAsCurrentTB();

            Selection.ColumnSelectionMode = false;

            Selection.BeginUpdate();
            try {
                Selection.SelectAll();
                InsertText(value);
                GoHome();
            } finally {
                Selection.EndUpdate();
            }
        }
    }

    /// <summary>
    /// Text lines
    /// </summary>
    [Browsable(false)]
    public IList<string> Lines => lines.Lines;

    /// <summary>
    /// Gets colored text as HTML
    /// </summary>
    /// <remarks>For more flexibility you can use ExportToHTML class also</remarks>
    [Browsable(false)]
    public string Html {
        get {
            var exporter = new ExportToHTML();
            exporter.UseNbsp = false;
            exporter.UseStyleTag = false;
            exporter.UseBr = false;
            return "<pre>" + exporter.GetHtml(this) + "</pre>";
        }
    }

    /// <summary>
    /// Text of current selection
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedText {
        get => Selection.Text;
        set => InsertText(value);
    }

    /// <summary>
    /// Start position of selection
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart {
        get => Math.Min(PlaceToPosition(Selection.Start), PlaceToPosition(Selection.End));
        set => Selection.Start = PositionToPlace(value);
    }

    /// <summary>
    /// Length of selected text
    /// </summary>
    [Browsable(false)]
    [DefaultValue(0)]
    public int SelectionLength {
        get => Math.Abs(PlaceToPosition(Selection.Start) - PlaceToPosition(Selection.End));
        set {
            if (value > 0) {
                Selection.End = PositionToPlace(SelectionStart + value);
            }
        }
    }

    /// <summary>
    /// Font
    /// </summary>
    /// <remarks>Use only monospaced font</remarks>
    [DefaultValue(typeof(Font), "Courier New, 9.75")]
    public override Font Font {
        get => base.Font;
        set {
            base.Font = value;
            //check monospace font
            SizeF sizeM = GetCharSize(base.Font, 'M');
            SizeF sizeDot = GetCharSize(base.Font, '.');
            if (sizeM != sizeDot) {
                base.Font = new Font("Courier New", base.Font.SizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
            }

            //clac size
            SizeF size = GetCharSize(base.Font, 'M');
            CharWidth = (int) Math.Round(size.Width * 1f /*0.85*/) - 1 /*0*/;
            CharHeight = lineInterval + (int) Math.Round(size.Height * 1f /*0.9*/) - 1 /*0*/;
            //
            NeedRecalc();
            Invalidate();
        }
    }

    private new Size AutoScrollMinSize {
        set {
            if (scrollBars) {
                base.AutoScroll = true;
                Size newSize = value;
                if (WordWrap) {
                    int maxWidth = GetMaxLineWordWrapedWidth();
                    newSize = new Size(Math.Min(newSize.Width, maxWidth), newSize.Height);
                }

                base.AutoScrollMinSize = newSize;
            } else {
                base.AutoScroll = false;
                base.AutoScrollMinSize = new Size(0, 0);
                VerticalScroll.Visible = false;
                HorizontalScroll.Visible = false;
                HorizontalScroll.Maximum = value.Width;
                VerticalScroll.Maximum = value.Height;
            }
        }

        get {
            if (scrollBars) {
                return base.AutoScrollMinSize;
            } else {
                return new Size(HorizontalScroll.Maximum, VerticalScroll.Maximum);
            }
        }
    }

    /// <summary>
    /// Indicates that IME is allowed (for CJK language entering)
    /// </summary>
    [Browsable(false)]
    public bool ImeAllowed =>
        ImeMode != ImeMode.Disable &&
        ImeMode != ImeMode.Off &&
        ImeMode != ImeMode.NoControl;

    /// <summary>
    /// Is undo enabled?
    /// </summary>
    [Browsable(false)]
    public bool UndoEnabled => lines.Manager.UndoEnabled;

    /// <summary>
    /// Is redo enabled?
    /// </summary>
    [Browsable(false)]
    public bool RedoEnabled => lines.Manager.RedoEnabled;

    private int LeftIndentLine => LeftIndent - minLeftIndent / 2 - 3;

    /// <summary>
    /// Range of all text
    /// </summary>
    [Browsable(false)]
    public Range Range => new(this, new Place(0, 0), new Place(lines[lines.Count - 1].Count, lines.Count - 1));

    /// <summary>
    /// Color of selected area
    /// </summary>
    [DefaultValue(typeof(Color), "Blue")]
    [Description("Color of selected area.")]
    public virtual Color SelectionColor {
        get => selectionColor;
        set {
            selectionColor = value;
            SelectionStyle = new SelectionStyle(new SolidBrush(selectionColor));
            Invalidate();
        }
    }

    public string BackupFolder {
        get {
            if (string.IsNullOrEmpty(CurrentFileName)) {
                return string.Empty;
            }

            string validDir;
            if (CurrentFileIsBackup()) {
                validDir = Directory.GetParent(CurrentFileName).FullName;
            } else {
                validDir = $"{Path.GetFileName(CurrentFileName)}_{CurrentFileName.GetHashCode()}";
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "TAS Files", "Backups", validDir);
        }
    }

    private bool CurrentFileIsBackup() {
        if (string.IsNullOrEmpty(CurrentFileName)) {
            return false;
        }

        return Directory.GetParent(CurrentFileName) is { } folder &&
               folder.Parent?.FullName == Path.Combine(Directory.GetCurrentDirectory(), "TAS Files", "Backups");
    }

    /// <summary>
    /// Occurs when VisibleRange is changed
    /// </summary>
    public virtual void OnVisibleRangeChanged() {
        needRecalcFoldingLines = true;

        if (VisibleRangeChanged != null) {
            VisibleRangeChanged(this, new EventArgs());
        }
    }

    /// <summary>
    /// Invalidates the entire surface of the control and causes the control to be redrawn.
    /// This method is thread safe and does not require Invoke.
    /// </summary>
    public new void Invalidate() {
        if (InvokeRequired) {
            BeginInvoke(new MethodInvoker(Invalidate));
        } else {
            base.Invalidate();
        }
    }

    protected virtual void OnCharSizeChanged() {
        VerticalScroll.SmallChange = charHeight;
        VerticalScroll.LargeChange = 10 * charHeight;
        HorizontalScroll.SmallChange = CharWidth;
    }

    /// <summary>
    /// TextChanged event.
    /// It occurs after insert, delete, clear, undo and redo operations.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs after insert, delete, clear, undo and redo operations.")]
    public new event EventHandler<TextChangedEventArgs> TextChanged;

    /// <summary>
    /// TextChanging event.
    /// It occurs before insert, delete, clear, undo and redo operations.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs before insert, delete, clear, undo and redo operations.")]
    public event EventHandler<TextChangingEventArgs> TextChanging;

    /// <summary>
    /// SelectionChanged event.
    /// It occurs after changing of selection.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs after changing of selection.")]
    public event EventHandler SelectionChanged;

    /// <summary>
    /// VisibleRangeChanged event.
    /// It occurs after changing of visible range.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs after changing of visible range.")]
    public event EventHandler VisibleRangeChanged;

    public event EventHandler NoChanges;
    public event EventHandler FileOpening;
    public event EventHandler FileOpened;
    public event EventHandler FileSaving;

    /// <summary>
    /// TextChangedDelayed event.
    /// It occurs after insert, delete, clear, undo and redo operations.
    /// This event occurs with a delay relative to TextChanged, and fires only once.
    /// </summary>
    [Browsable(true)]
    [Description(
        "It occurs after insert, delete, clear, undo and redo operations. This event occurs with a delay relative to TextChanged, and fires only once.")]
    public event EventHandler<TextChangedEventArgs> TextChangedDelayed;

    /// <summary>
    /// SelectionChangedDelayed event.
    /// It occurs after changing of selection.
    /// This event occurs with a delay relative to SelectionChanged, and fires only once.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs after changing of selection. This event occurs with a delay relative to SelectionChanged, and fires only once.")]
    public event EventHandler SelectionChangedDelayed;

    /// <summary>
    /// VisibleRangeChangedDelayed event.
    /// It occurs after changing of visible range.
    /// This event occurs with a delay relative to VisibleRangeChanged, and fires only once.
    /// </summary>
    [Browsable(true)]
    [Description(
        "It occurs after changing of visible range. This event occurs with a delay relative to VisibleRangeChanged, and fires only once.")]
    public event EventHandler VisibleRangeChangedDelayed;

    /// <summary>
    /// It occurs when user click on VisualMarker.
    /// </summary>
    [Browsable(true)]
    [Description("It occurs when user click on VisualMarker.")]
    public event EventHandler<VisualMarkerEventArgs> VisualMarkerClick;

    /// <summary>
    /// It occurs when visible char is enetering (alphabetic, digit, punctuation, DEL, BACKSPACE)
    /// </summary>
    /// <remarks>Set Handle to True for cancel key</remarks>
    [Browsable(true)]
    [Description("It occurs when visible char is enetering (alphabetic, digit, punctuation, DEL, BACKSPACE).")]
    public event KeyPressEventHandler KeyPressing;

    /// <summary>
    /// It occurs when visible char is enetered (alphabetic, digit, punctuation, DEL, BACKSPACE)
    /// </summary>
    [Browsable(true)]
    [Description("It occurs when visible char is enetered (alphabetic, digit, punctuation, DEL, BACKSPACE).")]
    public event KeyPressEventHandler KeyPressed;

    /// <summary>
    /// It occurs when calculates AutoIndent for new line
    /// </summary>
    [Browsable(true)]
    [Description("It occurs when calculates AutoIndent for new line.")]
    public event EventHandler<AutoIndentEventArgs> AutoIndentNeeded;

    /// <summary>
    /// It occurs when line background is painting
    /// </summary>
    [Browsable(true)]
    [Description("It occurs when line background is painting.")]
    public event EventHandler<PaintLineEventArgs> PaintLine;

    /// <summary>
    /// Occurs when line was inserted/added
    /// </summary>
    [Browsable(true)]
    [Description("Occurs when line was inserted/added.")]
    public event EventHandler<LineInsertedEventArgs> LineInserted;

    [Browsable(true)]
    [Description("Occurs when line was read in.")]
    public event EventHandler<LineNeededEventArgs> LineNeeded;

    /// <summary>
    /// Occurs when line was removed
    /// </summary>
    [Browsable(true)]
    [Description("Occurs when line was removed.")]
    public event EventHandler<LineRemovedEventArgs> LineRemoved;

    /// <summary>
    /// Occurs when current highlighted folding area is changed.
    /// Current folding area see in StartFoldingLine and EndFoldingLine.
    /// </summary>
    /// <remarks></remarks>
    [Browsable(true)]
    [Description("Occurs when current highlighted folding area is changed.")]
    public event EventHandler<EventArgs> FoldingHighlightChanged;

    /// <summary>
    /// Occurs when undo/redo stack is changed
    /// </summary>
    /// <remarks></remarks>
    [Browsable(true)]
    [Description("Occurs when undo/redo stack is changed.")]
    public event EventHandler<EventArgs> UndoRedoStateChanged;

    private TextSource CreateTextSource() {
        return new(this);
    }

    private void SetAsCurrentTB() {
        TextSource.CurrentTB = this;
    }

    public void SetTextSource(TextSource ts) {
        InitTextSource(ts);
        IsChanged = false;
        OnVisibleRangeChanged();
        UpdateHighlighting();
    }

    private void InitTextSource(TextSource ts) {
        if (lines != null) {
            ts.LineInserted -= ts_LineInserted;
            ts.LineRemoved -= ts_LineRemoved;
            ts.TextChanged -= ts_TextChanged;
            ts.RecalcNeeded -= ts_RecalcNeeded;
            ts.TextChanging -= ts_TextChanging;

            FileTextSource fs = ts as FileTextSource;
            if (fs != null) {
                fs.LineNeeded -= ts_LineNeeded;
            }

            lines.Dispose();
        }

        lineInfos.Clear();

        lines = ts;

        if (ts != null) {
            ts.LineInserted += ts_LineInserted;
            ts.LineRemoved += ts_LineRemoved;
            ts.TextChanged += ts_TextChanged;
            ts.RecalcNeeded += ts_RecalcNeeded;
            ts.TextChanging += ts_TextChanging;
            FileTextSource fs = ts as FileTextSource;
            if (fs != null) {
                fs.LineNeeded += ts_LineNeeded;
            }

            while (lineInfos.Count < ts.Count) {
                lineInfos.Add(new LineInfo(-1));
            }
        }

        isChanged = false;
        needRecalc = true;
    }

    private void ts_TextChanging(object sender, TextChangingEventArgs e) {
        if (TextSource.CurrentTB == this) {
            string text = e.InsertingText;
            OnTextChanging(ref text);
            e.InsertingText = text;
        }
    }

    private void ts_RecalcNeeded(object sender, TextSource.TextChangedEventArgs e) {
        if (e.iFromLine == e.iToLine && !WordWrap && lines.Count > minLinesForAccuracy) {
            RecalcScrollByOneLine(e.iFromLine);
        } else {
            needRecalc = true;
        }
    }

    /// <summary>
    /// Call this method if the recalc of the position of lines is needed.
    /// </summary>
    public void NeedRecalc() {
        needRecalc = true;
    }

    private void ts_TextChanged(object sender, TextSource.TextChangedEventArgs e) {
        if (e.iFromLine == e.iToLine && !WordWrap) {
            RecalcScrollByOneLine(e.iFromLine);
        } else {
            needRecalc = true;
        }

        Invalidate();
        if (TextSource.CurrentTB == this) {
            OnTextChanged(e.iFromLine, e.iToLine);
        }
    }

    private void ts_LineRemoved(object sender, LineRemovedEventArgs e) {
        lineInfos.RemoveRange(e.Index, e.Count);
        OnLineRemoved(e.Index, e.Count, e.RemovedLineUniqueIds);
    }

    private void ts_LineInserted(object sender, LineInsertedEventArgs e) {
        VisibleState newState = VisibleState.Visible;
        if (e.Index >= 0 && e.Index < lineInfos.Count && lineInfos[e.Index].VisibleState == VisibleState.Hidden) {
            newState = VisibleState.Hidden;
        }

        var temp = new List<LineInfo>(e.Count);
        for (int i = 0; i < e.Count; i++) {
            temp.Add(new LineInfo(-1) {VisibleState = newState});
        }

        lineInfos.InsertRange(e.Index, temp);

        OnLineInserted(e.Index, e.Count);
    }

    private void ts_LineNeeded(object sender, LineNeededEventArgs e) {
        LineNeeded?.Invoke(sender, e);
    }

    /// <summary>
    /// Navigates forward (by Line.LastVisit property)
    /// </summary>
    public bool NavigateForward() {
        DateTime min = DateTime.Now;
        int iLine = -1;
        for (int i = 0; i < LinesCount; i++) {
            if (lines.IsLineLoaded(i)) {
                if (lines[i].LastVisit > lastNavigatedDateTime && lines[i].LastVisit < min) {
                    min = lines[i].LastVisit;
                    iLine = i;
                }
            }
        }

        if (iLine >= 0) {
            Navigate(iLine);
            return true;
        } else {
            return false;
        }
    }

    /// <summary>
    /// Navigates backward (by Line.LastVisit property)
    /// </summary>
    public bool NavigateBackward() {
        var max = new DateTime();
        int iLine = -1;
        for (int i = 0; i < LinesCount; i++) {
            if (lines.IsLineLoaded(i)) {
                if (lines[i].LastVisit < lastNavigatedDateTime && lines[i].LastVisit > max) {
                    max = lines[i].LastVisit;
                    iLine = i;
                }
            }
        }

        if (iLine >= 0) {
            Navigate(iLine);
            return true;
        } else {
            return false;
        }
    }

    /// <summary>
    /// Navigates to defined line, without Line.LastVisit reseting
    /// </summary>
    public void Navigate(int iLine) {
        if (iLine >= LinesCount) {
            return;
        }

        lastNavigatedDateTime = lines[iLine].LastVisit;
        Selection.Start = new Place(0, iLine);
        DoSelectionVisible();
    }

    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        m_hImc = NativeMethodsWrapper.ImmGetContext(Handle);
    }

    public void AddVisualMarker(VisualMarker marker) {
        visibleMarkers.Add(marker);
    }

    public virtual void OnTextChangedDelayed(Range changedRange) {
        if (TextChangedDelayed != null) {
            TextChangedDelayed(this, new TextChangedEventArgs(changedRange));
        }
    }

    public virtual void OnSelectionChangedDelayed() {
        RecalcScrollByOneLine(Selection.Start.iLine);
        //highlight brackets
        ClearBracketsPositions();
        if (LeftBracket != '\x0' && RightBracket != '\x0') {
            HighlightBrackets(LeftBracket, RightBracket, ref leftBracketPosition, ref rightBracketPosition);
        }

        if (LeftBracket2 != '\x0' && RightBracket2 != '\x0') {
            HighlightBrackets(LeftBracket2, RightBracket2, ref leftBracketPosition2, ref rightBracketPosition2);
        }

        //remember last visit time
        if (Selection.IsEmpty && Selection.Start.iLine < LinesCount) {
            if (lastNavigatedDateTime != lines[Selection.Start.iLine].LastVisit) {
                lines[Selection.Start.iLine].LastVisit = DateTime.Now;
                lastNavigatedDateTime = lines[Selection.Start.iLine].LastVisit;
            }
        }

        if (SelectionChangedDelayed != null) {
            SelectionChangedDelayed(this, new EventArgs());
        }
    }

    public virtual void OnVisibleRangeChangedDelayed() {
        if (VisibleRangeChangedDelayed != null) {
            VisibleRangeChangedDelayed(this, new EventArgs());
        }
    }

    /// <summary>
    /// Adds new style
    /// </summary>
    /// <returns>Layer index of this style</returns>
    public int AddStyle(Style style) {
        if (style == null) {
            return -1;
        }

        int i = GetStyleIndex(style);
        if (i >= 0) {
            return i;
        }

        for (i = Styles.Length - 1; i >= 0; i--) {
            if (Styles[i] != null) {
                break;
            }
        }

        i++;
        if (i >= Styles.Length) {
            throw new Exception("Maximum count of Styles is exceeded");
        }

        Styles[i] = style;
        return i;
    }

    /// <summary>
    /// Gets length of given line
    /// </summary>
    /// <param name="iLine">Line index</param>
    /// <returns>Length of line</returns>
    public int GetLineLength(int iLine) {
        if (iLine < 0 || iLine >= lines.Count) {
            throw new ArgumentOutOfRangeException("Line index out of range");
        }

        return lines[iLine].Count;
    }

    /// <summary>
    /// Get range of line
    /// </summary>
    /// <param name="iLine">Line index</param>
    public Range GetLine(int iLine) {
        if (iLine < 0 || iLine >= lines.Count) {
            throw new ArgumentOutOfRangeException("Line index out of range");
        }

        var sel = new Range(this);
        sel.Start = new Place(0, iLine);
        sel.End = new Place(lines[iLine].Count, iLine);
        return sel;
    }

    /// <summary>
    /// Copy selected text into Clipboard
    /// </summary>
    public void Copy() {
        if (Selection.IsEmpty) {
            ExpandLine();
        }

        if (!Selection.IsEmpty) {
            var exp = new ExportToHTML();
            exp.UseBr = false;
            exp.UseNbsp = false;
            exp.UseStyleTag = true;
            string html = "<pre>" + exp.GetHtml(Selection.Clone()) + "</pre>";
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, true, Selection.Text);
            data.SetData(DataFormats.Html, PrepareHtmlForClipboard(html));
            for (int i = 0; i < 5; i++) {
                try {
                    Clipboard.SetDataObject(data, true);
                    return;
                } catch (ExternalException) {
                    Win32Api.UnlockClipboard();
                }
            }

            MessageBox.Show("Failed to copy text.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static MemoryStream PrepareHtmlForClipboard(string html) {
        Encoding enc = Encoding.UTF8;

        string begin = "Version:0.9\r\nStartHTML:{0:000000}\r\nEndHTML:{1:000000}"
                       + "\r\nStartFragment:{2:000000}\r\nEndFragment:{3:000000}\r\n";

        string html_begin = "<html>\r\n<head>\r\n"
                            + "<meta http-equiv=\"Content-Type\""
                            + " content=\"text/html; charset=" + enc.WebName + "\">\r\n"
                            + "<title>HTML clipboard</title>\r\n</head>\r\n<body>\r\n"
                            + "<!--StartFragment-->";

        string html_end = "<!--EndFragment-->\r\n</body>\r\n</html>\r\n";

        string begin_sample = String.Format(begin, 0, 0, 0, 0);

        int count_begin = enc.GetByteCount(begin_sample);
        int count_html_begin = enc.GetByteCount(html_begin);
        int count_html = enc.GetByteCount(html);
        int count_html_end = enc.GetByteCount(html_end);

        string html_total = String.Format(
            begin
            , count_begin
            , count_begin + count_html_begin + count_html + count_html_end
            , count_begin + count_html_begin
            , count_begin + count_html_begin + count_html
        ) + html_begin + html + html_end;

        return new MemoryStream(enc.GetBytes(html_total));
    }


    /// <summary>
    /// Cut selected text into Clipboard
    /// </summary>
    public void Cut() {
        if (Selection.IsEmpty) {
            ExpandLine();
        }

        if (!Selection.IsEmpty) {
            Copy();
            ClearSelected();
        }
    }

    /// <summary>
    /// Paste text from clipboard into selection position
    /// </summary>
    public void Paste() {
        string text = null;
        if (Clipboard.ContainsText()) {
            text = Clipboard.GetText();
        }

        if (text != null) {
            InsertText(text);
        }
    }

    /// <summary>
    /// Select all chars of text
    /// </summary>
    public void SelectAll() {
        Selection.SelectAll();
    }

    public void GoToLine(int line) {
        line = Math.Min(LinesCount - 1, Math.Max(0, line));
        Selection = new Range(this, 0, line, 0, line);
        DoSelectionVisible();
    }

    /// <summary>
    /// Move caret to end of text
    /// </summary>
    public void GoEnd() {
        if (lines.Count > 0) {
            Selection.Start = new Place(lines[lines.Count - 1].Count, lines.Count - 1);
        } else {
            Selection.Start = new Place(0, 0);
        }

        DoCaretVisible();
    }

    /// <summary>
    /// Move caret to first position
    /// </summary>
    public void GoHome() {
        Selection.Start = new Place(0, 0);
        VerticalScroll.Value = 0;
        HorizontalScroll.Value = 0;
    }

    /// <summary>
    /// Clear text, styles, history, caches
    /// </summary>
    public void Clear() {
        Selection.BeginUpdate();
        try {
            Selection.SelectAll();
            ClearSelected();
            lines.Manager.ClearHistory();
            Invalidate();
        } finally {
            Selection.EndUpdate();
        }
    }

    /// <summary>
    /// Clear buffer of styles
    /// </summary>
    public void ClearStylesBuffer() {
        for (int i = 0; i < Styles.Length; i++) {
            Styles[i] = null;
        }
    }

    /// <summary>
    /// Clear style of all text
    /// </summary>
    public void ClearStyle(StyleIndex styleIndex) {
        foreach (Line line in lines) {
            line.ClearStyle(styleIndex);
        }

        for (int i = 0; i < lineInfos.Count; i++) {
            SetVisibleState(i, VisibleState.Visible);
        }

        Invalidate();
    }


    /// <summary>
    /// Clears undo and redo stacks
    /// </summary>
    public void ClearUndo() {
        lines.Manager.ClearHistory();
    }

    /// <summary>
    /// Insert text into current selection position
    /// </summary>
    /// <param name="text"></param>
    public void InsertText(string text) {
        if (text == null) {
            return;
        }

        lines.Manager.BeginAutoUndoCommands();
        try {
            if (!Selection.IsEmpty) {
                lines.Manager.ExecuteCommand(new ClearSelectedCommand(TextSource));
            }

            lines.Manager.ExecuteCommand(new InsertTextCommand(TextSource, text));
            if (updating <= 0) {
                DoCaretVisible();
            }
        } finally {
            lines.Manager.EndAutoUndoCommands();
        }

        //
        Invalidate();
    }

    /// <summary>
    /// Insert text into current selection position (with predefined style)
    /// </summary>
    /// <param name="text"></param>
    public void InsertText(string text, Style style) {
        if (text == null) {
            return;
        }

        //remember last caret position
        Place last = Selection.Start;
        //insert text
        InsertText(text);
        //get range
        var range = new Range(this, last, Selection.Start);
        //set style for range
        range.SetStyle(style);
    }

    /// <summary>
    /// Append string to end of the Text
    /// </summary>
    /// <param name="text"></param>
    public void AppendText(string text) {
        if (text == null) {
            return;
        }

        Selection.ColumnSelectionMode = false;

        Place oldStart = Selection.Start;
        Place oldEnd = Selection.End;

        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        try {
            if (lines.Count > 0) {
                Selection.Start = new Place(lines[lines.Count - 1].Count, lines.Count - 1);
            } else {
                Selection.Start = new Place(0, 0);
            }

            lines.Manager.ExecuteCommand(new InsertTextCommand(TextSource, text));
        } finally {
            lines.Manager.EndAutoUndoCommands();
            Selection.Start = oldStart;
            Selection.End = oldEnd;
            Selection.EndUpdate();
        }

        //
        Invalidate();
    }

    /// <summary>
    /// Returns index of the style in Styles
    /// -1 otherwise
    /// </summary>
    /// <param name="style"></param>
    /// <returns>Index of the style in Styles</returns>
    public int GetStyleIndex(Style style) {
        return Array.IndexOf(Styles, style);
    }

    /// <summary>
    /// Returns StyleIndex mask of given styles
    /// </summary>
    /// <param name="styles"></param>
    /// <returns>StyleIndex mask of given styles</returns>
    public StyleIndex GetStyleIndexMask(Style[] styles) {
        StyleIndex mask = StyleIndex.None;
        foreach (Style style in styles) {
            int i = GetStyleIndex(style);
            if (i >= 0) {
                mask |= Range.ToStyleIndex(i);
            }
        }

        return mask;
    }

    internal int GetOrSetStyleLayerIndex(Style style) {
        int i = GetStyleIndex(style);
        if (i < 0) {
            i = AddStyle(style);
        }

        return i;
    }

    public static SizeF GetCharSize(Font font, char c) {
        Size sz2 = TextRenderer.MeasureText("<" + c.ToString() + ">", font);
        Size sz3 = TextRenderer.MeasureText("<>", font);

        return new SizeF(sz2.Width - sz3.Width + 1, /*sz2.Height*/font.Height);
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_HSCROLL || m.Msg == WM_VSCROLL) {
            if (m.WParam.ToInt32() != SB_ENDSCROLL) {
                Invalidate();
            }
        }

        base.WndProc(ref m);

        if (ImeAllowed) {
            if (m.Msg == WM_IME_SETCONTEXT && m.WParam.ToInt32() == 1) {
                NativeMethodsWrapper.ImmAssociateContext(Handle, m_hImc);
            }
        }
    }

    protected override void OnScroll(ScrollEventArgs se) {
        base.OnScroll(se);
        OnVisibleRangeChanged();
        Invalidate();
    }

    private void InsertChar(char c) {
        lines.Manager.BeginAutoUndoCommands();
        try {
            if (!Selection.IsEmpty) {
                lines.Manager.ExecuteCommand(new ClearSelectedCommand(TextSource));
            }

            lines.Manager.ExecuteCommand(new InsertCharCommand(TextSource, c));
        } finally {
            lines.Manager.EndAutoUndoCommands();
        }

        Invalidate();
    }

    /// <summary>
    /// Deletes selected chars
    /// </summary>
    public void ClearSelected() {
        if (!Selection.IsEmpty) {
            lines.Manager.ExecuteCommand(new ClearSelectedCommand(TextSource));
            Invalidate();
        }
    }

    /// <summary>
    /// Deletes current line(s)
    /// </summary>
    public void ClearCurrentLine() {
        Selection.Expand();
        lines.Manager.ExecuteCommand(new ClearSelectedCommand(TextSource));
        if (Selection.Start.iLine == 0) {
            if (!Selection.GoRightThroughFolded()) {
                return;
            }
        }

        if (Selection.Start.iLine > 0) {
            lines.Manager.ExecuteCommand(new InsertCharCommand(TextSource, '\b')); //backspace
        }

        Invalidate();
    }

    private void Recalc() {
        if (!needRecalc) {
            return;
        }

        needRecalc = false;
        //calc min left indent
        LeftIndent = LeftPadding;
        long maxLineNumber = LinesCount + lineNumberStartValue - 1;
        int charsForLineNumber = 2 + (maxLineNumber > 0 ? (int) Math.Log10(maxLineNumber) : 0);
        if (Created) {
            if (ShowLineNumbers) {
                LeftIndent += charsForLineNumber * CharWidth + minLeftIndent + 1;
            }
        } else {
            needRecalc = true;
        }

        //calc max line length and count of wordWrapLines
        wordWrapLinesCount = 0;

        maxLineLength = RecalcMaxLineLength();

        //adjust AutoScrollMinSize
        int minWidth = LeftIndent + (maxLineLength) * CharWidth + 2 + Paddings.Left + Paddings.Right;
        if (wordWrap) {
            switch (WordWrapMode) {
                case WordWrapMode.WordWrapControlWidth:
                case WordWrapMode.CharWrapControlWidth:
                    maxLineLength = Math.Min(maxLineLength, (ClientSize.Width - LeftIndent - Paddings.Left - Paddings.Right) / CharWidth);
                    minWidth = 0;
                    break;
                case WordWrapMode.WordWrapPreferredWidth:
                case WordWrapMode.CharWrapPreferredWidth:
                    maxLineLength = Math.Min(maxLineLength, PreferredLineWidth);
                    minWidth = LeftIndent + PreferredLineWidth * CharWidth + 2 + Paddings.Left + Paddings.Right;
                    break;
            }
        }

        AutoScrollMinSize = new Size(minWidth, (wordWrapLinesCount + 3) * CharHeight + Paddings.Top + Paddings.Bottom);
    }

    private void RecalcScrollByOneLine(int iLine) {
        if (iLine >= lines.Count) {
            return;
        }

        int maxLineLength = lines[iLine].Count;
        if (this.maxLineLength < maxLineLength && !WordWrap) {
            this.maxLineLength = maxLineLength;
        }

        int minWidth = LeftIndent + (maxLineLength) * CharWidth + 2 + Paddings.Left + Paddings.Right;
        if (AutoScrollMinSize.Width < minWidth) {
            AutoScrollMinSize = new Size(minWidth, AutoScrollMinSize.Height);
        }
    }

    private int RecalcMaxLineLength() {
        int maxLineLength = 0;
        TextSource lines = this.lines;
        int count = lines.Count;
        int charHeight = CharHeight;
        int topIndent = Paddings.Top;

        for (int i = 0; i < count; i++) {
            int lineLength = lines.GetLineLength(i);
            LineInfo lineInfo = lineInfos[i];
            if (lineLength > maxLineLength && lineInfo.VisibleState == VisibleState.Visible) {
                maxLineLength = lineLength;
            }

            lineInfo.startY = wordWrapLinesCount * charHeight + topIndent;
            wordWrapLinesCount += lineInfo.WordWrapStringsCount;
            lineInfos[i] = lineInfo;
        }

        return maxLineLength;
    }

    private int GetMaxLineWordWrapedWidth() {
        if (wordWrap) {
            switch (wordWrapMode) {
                case WordWrapMode.WordWrapControlWidth:
                case WordWrapMode.CharWrapControlWidth:
                    return ClientSize.Width;
                case WordWrapMode.WordWrapPreferredWidth:
                case WordWrapMode.CharWrapPreferredWidth:
                    return LeftIndent + PreferredLineWidth * CharWidth + 2 + Paddings.Left + Paddings.Right;
            }
        }

        return int.MaxValue;
    }

    private void RecalcWordWrap(int fromLine, int toLine) {
        int maxCharsPerLine = 0;
        bool charWrap = false;

        switch (WordWrapMode) {
            case WordWrapMode.WordWrapControlWidth:
                maxCharsPerLine = (ClientSize.Width - LeftIndent - Paddings.Left - Paddings.Right) / CharWidth;
                break;
            case WordWrapMode.CharWrapControlWidth:
                maxCharsPerLine = (ClientSize.Width - LeftIndent - Paddings.Left - Paddings.Right) / CharWidth;
                charWrap = true;
                break;
            case WordWrapMode.WordWrapPreferredWidth:
                maxCharsPerLine = PreferredLineWidth;
                break;
            case WordWrapMode.CharWrapPreferredWidth:
                maxCharsPerLine = PreferredLineWidth;
                charWrap = true;
                break;
        }

        for (int iLine = fromLine; iLine <= toLine; iLine++) {
            if (lines.IsLineLoaded(iLine)) {
                if (!wordWrap) {
                    lineInfos[iLine].CutOffPositions.Clear();
                } else {
                    LineInfo li = lineInfos[iLine];
                    li.CalcCutOffs(maxCharsPerLine, ImeAllowed, charWrap, lines[iLine]);
                    lineInfos[iLine] = li;
                }
            }
        }

        needRecalc = true;
    }

    protected override void OnClientSizeChanged(EventArgs e) {
        base.OnClientSizeChanged(e);
        if (WordWrap) {
            RecalcWordWrap(0, lines.Count - 1);
            Invalidate();
        }

        OnVisibleRangeChanged();
    }

    /// <summary>
    /// Scroll control for display defined rectangle
    /// </summary>
    /// <param name="rect"></param>
    private void DoVisibleRectangle(Rectangle rect) {
        int oldV = VerticalScroll.Value;
        int v = VerticalScroll.Value;
        int h = HorizontalScroll.Value;

        if (rect.Bottom > ClientRectangle.Height) {
            v += rect.Bottom - ClientRectangle.Height;
        } else if (rect.Top < 0) {
            v += rect.Top;
        }

        if (rect.Right > ClientRectangle.Width) {
            h += rect.Right - ClientRectangle.Width;
        } else if (rect.Left < LeftIndent) {
            h += rect.Left - LeftIndent;
        }

        //
        if (!Multiline) {
            v = 0;
        }

        //
        v = Math.Max(0, v);
        h = Math.Max(0, h);
        //
        try {
            if (VerticalScroll.Visible || !ShowScrollBars) {
                VerticalScroll.Value = v;
            }

            if (HorizontalScroll.Visible || !ShowScrollBars) {
                HorizontalScroll.Value = h;
            }
        } catch (ArgumentOutOfRangeException) {
            ;
        }

        if (ShowScrollBars) {
            //some magic for update scrolls
            base.AutoScrollMinSize -= new Size(1, 0);
            base.AutoScrollMinSize += new Size(1, 0);
        }

        //
        if (oldV != VerticalScroll.Value) {
            OnVisibleRangeChanged();
        }
    }

    /// <summary>
    /// Scroll control for display caret
    /// </summary>
    public void DoCaretVisible() {
        Invalidate();
        Recalc();
        Point car = PlaceToPoint(Selection.Start);
        car.Offset(-CharWidth, 0);
        DoVisibleRectangle(new Rectangle(car, new Size(2 * CharWidth, 2 * CharHeight)));
    }

    /// <summary>
    /// Scroll control left
    /// </summary>
    public void ScrollLeft() {
        Invalidate();
        HorizontalScroll.Value = 0;
        AutoScrollMinSize -= new Size(1, 0);
        AutoScrollMinSize += new Size(1, 0);
    }

    /// <summary>
    /// Scroll control for display selection area
    /// </summary>
    public void DoSelectionVisible() {
        if (lineInfos[Selection.End.iLine].VisibleState != VisibleState.Visible) {
            ExpandBlock(Selection.End.iLine);
        }

        if (lineInfos[Selection.Start.iLine].VisibleState != VisibleState.Visible) {
            ExpandBlock(Selection.Start.iLine);
        }

        Recalc();
        DoVisibleRectangle(new Rectangle(PlaceToPoint(new Place(0, Selection.End.iLine)),
            new Size(2 * CharWidth, 2 * CharHeight)));

        Point car = PlaceToPoint(Selection.Start);
        Point car2 = PlaceToPoint(Selection.End);
        car.Offset(-CharWidth, -ClientSize.Height / 2);
        DoVisibleRectangle(new Rectangle(car, new Size(Math.Abs(car2.X - car.X), ClientSize.Height))); //Math.Abs(car2.Y-car.Y) + 2 * CharHeight

        Invalidate();
    }

    /// <summary>
    /// Scroll control for display given range
    /// </summary>
    public void DoRangeVisible(Range range) {
        range = range.Clone();
        range.Normalize();
        range.End = new Place(range.End.iChar, Math.Min(range.End.iLine, range.Start.iLine + ClientSize.Height / CharHeight));

        if (lineInfos[range.End.iLine].VisibleState != VisibleState.Visible) {
            ExpandBlock(range.End.iLine);
        }

        if (lineInfos[range.Start.iLine].VisibleState != VisibleState.Visible) {
            ExpandBlock(range.Start.iLine);
        }

        Recalc();
        DoVisibleRectangle(new Rectangle(PlaceToPoint(new Place(0, range.Start.iLine)),
            new Size(2 * CharWidth, (1 + range.End.iLine - range.Start.iLine) * CharHeight)));

        Invalidate();
    }

    protected override void OnCreateControl() {
        base.OnCreateControl();
        if (ParentForm != null) {
            ParentForm.Deactivate += (sender, args) => lastModifiers = Keys.None;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);

        if (e.KeyCode == Keys.ShiftKey) {
            lastModifiers &= ~Keys.Shift;
        }

        if (e.KeyCode == Keys.Alt) {
            lastModifiers &= ~Keys.Alt;
        }

        if (e.KeyCode == Keys.ControlKey) {
            lastModifiers &= ~Keys.Control;
        }
    }

    public bool OpenFile(string fileName) {
        lastModifiers = Keys.None;
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName)) {
            using OpenFileDialog diag = new();
            diag.Filter = "TAS|*.tas";
            diag.FilterIndex = 0;
            if (!string.IsNullOrEmpty(CurrentFileName)) {
                diag.InitialDirectory = Path.GetDirectoryName(CurrentFileName);
            } else if (Environment.OSVersion.Platform == PlatformID.Unix
                       && Directory.Exists("~/.steam/steam/steamapps/common/Celeste")) {
                diag.InitialDirectory = Path.GetFullPath("~/.steam/steam/steamapps/common/Celeste");
            }

            if (diag.ShowDialog() == DialogResult.OK) {
                CurrentFileName = diag.FileName;
                OpenBindingFile(diag.FileName, Encoding.ASCII);
                return true;
            }
        } else {
            CurrentFileName = fileName;
            OpenBindingFile(fileName, Encoding.ASCII);
            return true;
        }

        return false;
    }

    public void ReloadFile() {
        if (!string.IsNullOrEmpty(CurrentFileName) && File.Exists(CurrentFileName)) {
            OpenBindingFile(CurrentFileName, Encoding.ASCII);
        }
    }

    public void SaveNewFile() {
        lastModifiers = Keys.None;
        using SaveFileDialog diag = new();
        diag.DefaultExt = ".tas";
        diag.AddExtension = true;
        diag.Filter = "TAS|*.tas";
        diag.FilterIndex = 0;
        if (!string.IsNullOrEmpty(CurrentFileName)) {
            diag.InitialDirectory = Path.GetDirectoryName(CurrentFileName);
            diag.FileName = Path.GetFileName(CurrentFileName);
        } else {
            diag.FileName = "Kalimba.tas";
        }

        if (diag.ShowDialog() == DialogResult.OK) {
            CurrentFileName = diag.FileName;
            SaveFile();
        }
    }

    public void SaveFile() {
        FileSaving?.Invoke(this, new EventArgs());
        SaveToFile(CurrentFileName, Encoding.ASCII);
        TryBackupFile();
    }

    private void TryBackupFile() {
        if (!Settings.Instance.AutoBackupEnabled || string.IsNullOrEmpty(CurrentFileName)) {
            return;
        }

        string backupDir = BackupFolder;
        if (!Directory.Exists(backupDir)) {
            Directory.CreateDirectory(backupDir);
        }

        string[] files = Directory.GetFiles(backupDir);
        if (files.Length > 0) {
            DateTime lastFileTime = File.GetLastWriteTime(files.Last());
            if (Settings.Instance.AutoBackupRate > 0 && lastFileTime.AddMinutes(Settings.Instance.AutoBackupRate) > DateTime.Now) {
                return;
            }

            if (Settings.Instance.AutoBackupCount > 0 && files.Length >= Settings.Instance.AutoBackupCount) {
                foreach (string path in files.Take(files.Length - Settings.Instance.AutoBackupCount + 1)) {
                    File.Delete(path);
                }
            }
        }

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(CurrentFileName);
        if (CurrentFileIsBackup() && fileNameWithoutExtension.Length > 24) {
            fileNameWithoutExtension = fileNameWithoutExtension.Substring(0, fileNameWithoutExtension.Length - 24);
        }

        string backupFileName = Path.Combine(backupDir, fileNameWithoutExtension + DateTime.Now.ToString("_yyyy-MM-dd_HH-mm-ss-fff") + ".tas");
        File.Copy(CurrentFileName, Path.Combine(backupDir, backupFileName));
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);

        if (Focused) {
            lastModifiers = e.Modifiers;
        }

        handledChar = false;

        if (e.Handled) {
            handledChar = true;
            return;
        }

        switch (e.KeyCode) {
            case Keys.G:
                if (e.Modifiers == Keys.Control) {
                    ShowGoToDialog();
                }

                break;
            case Keys.C:
                if (e.Modifiers == Keys.Control) {
                    Copy();
                }

                //	if (e.Modifiers == (Keys.Control | Keys.Shift))
                //		CommentSelected();
                break;
            case Keys.X:
                if (e.Modifiers == Keys.Control && !ReadOnly) {
                    Cut();
                }

                break;
            //case Keys.S:
            //	if (e.Modifiers == Keys.Control) {
            //		SaveFile();
            //	} else if (e.Modifiers == (Keys.Control | Keys.Shift)) {
            //		SaveNewFile();
            //	}
            //	break;
            //case Keys.O:
            //	if (e.Modifiers == Keys.Control) {
            //		OpenFile();
            //	}
            //	break;
            case Keys.V:
                if (e.Modifiers == Keys.Control && !ReadOnly) {
                    Paste();
                }

                break;
            case Keys.A:
                if (e.Modifiers == Keys.Control) {
                    Selection.SelectAll();
                }

                break;
            case Keys.W:
                if (e.Modifiers == Keys.Control) {
                    Selection.SelectBlock();
                }

                break;
            case Keys.Z:
                if (e.Modifiers == Keys.Control && !ReadOnly) {
                    Undo();
                }

                if (e.Modifiers == (Keys.Control | Keys.Shift) && !ReadOnly) {
                    Redo();
                }

                break;
            //case Keys.R:
            //	if (e.Modifiers == Keys.Control && !ReadOnly)
            //		Redo();
            //	break;
            case Keys.U:
                if (e.Modifiers == (Keys.Control | Keys.Shift)) {
                    LowerCase();
                }

                if (e.Modifiers == Keys.Control) {
                    UpperCase();
                }

                break;
            case Keys.OemMinus:
                if (e.Modifiers == Keys.Control) {
                    NavigateBackward();
                }

                if (e.Modifiers == (Keys.Control | Keys.Shift)) {
                    NavigateForward();
                }

                break;
            case Keys.Y:
                if (ReadOnly) {
                    break;
                }

                if (e.Modifiers == Keys.Control) {
                    ExpandLine();
                    ClearSelected();
                    TryMoveCursorBehindFrame();
                }

                break;

            case Keys.Back:
                if (ReadOnly) {
                    break;
                }

                if (e.Modifiers == Keys.Alt) {
                    Undo();
                } else if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    if (OnKeyPressing('\b')) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    } else {
                        Place start = Selection.Start;
                        List<InputRecord> inputRecords = Studio.Instance.InputRecords;
                        if (start.iChar == 0 && start.iLine > 0 && inputRecords[start.iLine - 1].IsInput &&
                            inputRecords[start.iLine].ToString().Trim() != string.Empty) {
                            Selection.GoLeft(false);
                        } else {
                            InsertChar('\b');
                        }
                    }

                    OnKeyPressed('\b');
                } else if (e.Modifiers == Keys.Control) {
                    if (OnKeyPressing('\b')) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    }

                    Selection.GoWordLeft(true);
                    ClearSelected();
                    OnKeyPressed('\b');
                }

                break;

            case Keys.Delete:
                if (ReadOnly) {
                    break;
                }

                if (e.Modifiers == Keys.None) {
                    if (OnKeyPressing((char) 0xff)) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    } else {
                        Place start = Selection.Start;

                        //if line contains only spaces then delete line
                        if (this[start.iLine].StartSpacesCount == this[start.iLine].Count) {
                            RemoveSpacesAfterCaret();
                        }

                        List<InputRecord> inputRecords = Studio.Instance.InputRecords;
                        if (start.iChar == this[start.iLine].Count && start.iLine < lines.Count - 1 && inputRecords[start.iLine].IsInput &&
                            inputRecords[start.iLine + 1].ToString().Trim() != string.Empty) {
                            // ignore
                        } else if (Selection.GoRightThroughFolded()) {
                            int iLine = Selection.Start.iLine;
                            InsertChar((char) 1);

                            //if removed \n then trim spaces
                            if (iLine != Selection.Start.iLine && AutoIndent) {
                                if (Selection.Start.iChar > 0) {
                                    RemoveSpacesAfterCaret();
                                }
                            }
                        }
                    }

                    OnKeyPressed((char) 0xff);
                } else if (e.Modifiers == Keys.Control) {
                    if (OnKeyPressing((char) 0xff)) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    } else {
                        Selection.GoWordRight(true);
                        ClearSelected();
                    }

                    OnKeyPressed((char) 0xff);
                } else if (e.Modifiers == Keys.Shift) {
                    if (OnKeyPressing((char) 0xff)) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    } else {
                        //remove current line
                        if (!ReadOnly) {
                            if (Selection.Start.iLine >= 0 && Selection.Start.iLine < LinesCount) {
                                int iLine = Selection.Start.iLine;
                                RemoveLines(new List<int>() {iLine});
                                Selection.Start = new Place(0, Math.Max(0, Math.Min(iLine, LinesCount - 1)));
                            }
                        }
                    }

                    OnKeyPressed((char) 0xff);
                }

                break;
            case Keys.Space:
                if (ReadOnly) {
                    break;
                }

                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    if (OnKeyPressing(' ')) //KeyPress event processed key
                    {
                        break;
                    }

                    if (!Selection.IsEmpty) {
                        ClearSelected();
                    }

                    //replace mode? select forward char
                    if (IsReplaceMode) {
                        Selection.GoRight(true);
                        Selection.Inverse();
                    }

                    InsertChar(' ');
                    OnKeyPressed(' ');
                }

                break;

            case Keys.Left:
                if (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift)) {
                    Selection.GoWordLeft(e.Shift);
                }

                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    Selection.GoLeft(e.Shift);
                }

                if (e.Modifiers == AltShift) {
                    CheckAndChangeSelectionType();
                    if (Selection.ColumnSelectionMode) {
                        Selection.GoLeft_ColumnSelectionMode();
                    }
                }

                break;
            case Keys.Right:
                if (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift)) {
                    Selection.GoWordRight(e.Shift);
                }

                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    Selection.GoRight(e.Shift);
                }

                if (e.Modifiers == AltShift) {
                    CheckAndChangeSelectionType();
                    if (Selection.ColumnSelectionMode) {
                        Selection.GoRight_ColumnSelectionMode();
                    }
                }

                break;
            case Keys.Up:
                if (e.Modifiers is Keys.None or Keys.Shift) {
                    Selection.GoUp(e.Shift);
                    ScrollLeft();
                    if (e.Modifiers == Keys.None) {
                        TryMoveCursorBehindFrame();
                    }
                } else if (e.Modifiers == AltShift) {
                    CheckAndChangeSelectionType();
                    if (Selection.ColumnSelectionMode) {
                        Selection.GoUp_ColumnSelectionMode();
                    }
                } else if (e.Modifiers == Keys.Alt) {
                    if (!ReadOnly && !Selection.ColumnSelectionMode) {
                        MoveSelectedLinesUp();
                    }
                } else if (e.Modifiers == (Keys.Control | Keys.Shift)) {
                    TweakFrames(true);
                }

                break;
            case Keys.Down:
                if (e.Modifiers is Keys.None or Keys.Shift) {
                    Selection.GoDown(e.Shift);
                    ScrollLeft();
                    if (e.Modifiers == Keys.None) {
                        TryMoveCursorBehindFrame();
                    }
                } else if (e.Modifiers == AltShift) {
                    CheckAndChangeSelectionType();
                    if (Selection.ColumnSelectionMode) {
                        Selection.GoDown_ColumnSelectionMode();
                    }
                } else if (e.Modifiers == Keys.Alt) {
                    if (!ReadOnly && !Selection.ColumnSelectionMode) {
                        MoveSelectedLinesDown();
                    }
                } else if (e.Modifiers == (Keys.Control | Keys.Shift)) {
                    TweakFrames(false);
                }

                break;
            case Keys.PageUp:
                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    Selection.GoPageUp(e.Shift);
                    ScrollLeft();
                }

                break;
            case Keys.PageDown:
                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    Selection.GoPageDown(e.Shift);
                    ScrollLeft();
                }

                break;
            case Keys.Home:
                if (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift)) {
                    Selection.GoFirst(e.Shift);
                }

                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    GoHome(e.Shift);
                    ScrollLeft();
                }

                break;
            case Keys.End:
                if (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift)) {
                    Selection.GoLast(e.Shift);
                }

                if (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift) {
                    Selection.GoEnd(e.Shift);
                }

                break;
            case Keys.Alt:
                return;
            default:
                if ((e.Modifiers & Keys.Control) != 0) {
                    return;
                }

                if ((e.Modifiers & Keys.Alt) != 0) {
                    if ((MouseButtons & MouseButtons.Left) != 0) {
                        CheckAndChangeSelectionType();
                    }

                    return;
                }

                if (e.KeyCode == Keys.ShiftKey) {
                    return;
                }

                break;
        }

        e.Handled = true;

        DoCaretVisible();
        Invalidate();
    }

    private void ExpandLine() {
        Selection.Expand();
        if (LinesCount <= 1) {
            // ignored
        } else if (Selection.End.iLine is var endLine && endLine < LinesCount - 1) {
            Selection.End = new Place(0, endLine + 1);
        } else if (Selection.Start.iLine > 0) {
            Place end = Selection.End;
            int startLine = Selection.Start.iLine - 1;
            Selection.Start = new Place(Lines[startLine].Length, startLine);
            Selection.End = end;
        }
    }

    /// <summary>
    /// Moves selected lines down
    /// </summary>
    public virtual void MoveSelectedLinesDown() {
        var prevSelection = Selection.Clone();
        Selection.Expand();
        int iLine = Selection.Start.iLine;
        if (Selection.End.iLine >= LinesCount - 1) {
            Selection = prevSelection;
            return;
        }

        string text = SelectedText;
        var temp = new List<int>();
        for (int i = Selection.Start.iLine; i <= Selection.End.iLine; i++) {
            temp.Add(i);
        }

        RemoveLines(temp);
        Selection.Start = new Place(GetLineLength(iLine), iLine);
        SelectedText = "\n" + text;
        Selection.Start = new Place(prevSelection.Start.iChar, prevSelection.Start.iLine + 1);
        Selection.End = new Place(prevSelection.End.iChar, prevSelection.End.iLine + 1);
    }

    /// <summary>
    /// Moves selected lines up
    /// </summary>
    public virtual void MoveSelectedLinesUp() {
        var prevSelection = Selection.Clone();
        Selection.Expand();
        int iLine = Selection.Start.iLine;
        if (iLine == 0) {
            Selection = prevSelection;
            return;
        }

        string text = SelectedText;
        var temp = new List<int>();
        for (int i = Selection.Start.iLine; i <= Selection.End.iLine; i++) {
            temp.Add(i);
        }

        RemoveLines(temp);
        Selection.Start = new Place(0, iLine - 1);
        SelectedText = text + "\n";
        Selection.Start = new Place(prevSelection.Start.iChar, prevSelection.Start.iLine - 1);
        Selection.End = new Place(prevSelection.End.iChar, prevSelection.End.iLine - 1);
    }

    private void GoHome(bool shift) {
        Selection.BeginUpdate();
        try {
            int iLine = Selection.Start.iLine;
            int spaces = this[iLine].StartSpacesCount;
            if (Selection.Start.iChar <= spaces && Selection.Start.iChar > 0) {
                Selection.GoHome(shift);
            } else {
                Selection.GoHome(shift);
                for (int i = 0; i < spaces; i++) {
                    Selection.GoRight(shift);
                }
            }
        } finally {
            Selection.EndUpdate();
        }
    }

    /// <summary>
    /// Convert selected text to upper case
    /// </summary>
    public void UpperCase() {
        Range old = Selection.Clone();
        SelectedText = SelectedText.ToUpper();
        Selection.Start = old.Start;
        Selection.End = old.End;
    }

    /// <summary>
    /// Convert selected text to lower case
    /// </summary>
    public void LowerCase() {
        Range old = Selection.Clone();
        SelectedText = SelectedText.ToLower();
        Selection.Start = old.Start;
        Selection.End = old.End;
    }

    /// <summary>
    /// Insert/remove comment prefix into selected lines
    /// </summary>
    public void CommentSelected() {
        CommentSelected(CommentPrefix);
    }

    /// <summary>
    /// Insert/remove comment prefix into selected lines
    /// </summary>
    public void CommentSelected(string commentPrefix) {
        if (string.IsNullOrEmpty(commentPrefix)) {
            return;
        }

        Selection.Normalize();
        bool isCommented = lines[Selection.Start.iLine].Text.TrimStart().StartsWith(commentPrefix);
        if (isCommented) {
            RemoveLinePrefix(commentPrefix);
        } else {
            InsertLinePrefix(commentPrefix);
        }
    }

    public void OnKeyPressing(KeyPressEventArgs args) {
        if (KeyPressing != null) {
            KeyPressing(this, args);
        }
    }

    private bool OnKeyPressing(char c) {
        var args = new KeyPressEventArgs(c);
        OnKeyPressing(args);
        return args.Handled;
    }

    public void OnKeyPressed(char c) {
        var args = new KeyPressEventArgs(c);
        if (KeyPressed != null) {
            KeyPressed(this, args);
        }
    }

    protected override bool ProcessMnemonic(char charCode) {
        if (Focused) {
            return ProcessKeyPress(charCode) || base.ProcessMnemonic(charCode);
        } else {
            return false;
        }
    }

    private bool ProcessKeyPress(char c) {
        if (handledChar) {
            return true;
        }

        if (c == ' ') {
            return true;
        }

        if (c == '\b' && (lastModifiers & Keys.Alt) != 0) {
            return true;
        }

        if (char.IsControl(c) && c != '\r' && c != '\t') {
            return false;
        }

        if (ReadOnly || !Enabled) {
            return false;
        }


        if (lastModifiers != Keys.None &&
            lastModifiers != Keys.Shift &&
            lastModifiers != (Keys.Control | Keys.Alt) && //ALT+CTRL is special chars (AltGr)
            lastModifiers != (Keys.Shift | Keys.Control | Keys.Alt) && //SHIFT + ALT + CTRL is special chars (AltGr)
            (lastModifiers != (Keys.Alt) || char.IsLetterOrDigit(c)) //may be ALT+LetterOrDigit is mnemonic code
           ) {
            return false; //do not process Ctrl+? and Alt+? keys
        }

        char sourceC = c;
        if (OnKeyPressing(sourceC)) //KeyPress event processed key
        {
            return true;
        }

        if (c == '\r' && !AcceptsReturn) {
            return false;
        }

        //tab?
        if (c == '\t') {
            if (!AcceptsTab) {
                return false;
            }

            if (TabLength == 0) {
                return true;
            }

            if ((lastModifiers & Keys.Shift) == 0) {
                if (Selection.IsEmpty) {
                    //ClearSelected();
                    int spaces = TabLength - (Selection.Start.iChar % TabLength);
                    //replace mode? select forward chars
                    if (IsReplaceMode) {
                        for (int i = 0; i < spaces; i++) {
                            Selection.GoRight(true);
                        }

                        Selection.Inverse();
                    }

                    InsertText(new String(' ', spaces));
                } else {
                    IncreaseIndent();
                }
            } else if (Selection.IsEmpty) {
                int spaces = Selection.Start.iChar % TabLength;
                spaces = spaces == 0 ? TabLength : spaces;

                for (int i = 0; i < spaces; i++) {
                    Selection.GoLeft(true);
                    if (Selection.Text.Trim() != "") {
                        Selection.GoRight(true);
                        break;
                    }
                }

                ClearSelected();
            } else {
                DecreaseIndent();
            }
        } else {
            //replace \r on \n
            if (c == '\r') {
                c = '\n';
            }

            //replace mode? select forward char
            if (IsReplaceMode) {
                Selection.GoRight(true);
                Selection.Inverse();
            }

            //insert char
            if (c == '\n') {
                string line = CurrentStartLineText;
                if (Selection.Start.iChar > 0) {
                    if (AllSpaceRegex.IsMatch(line.Substring(0, Math.Max(line.Length, Selection.Start.iChar)))) {
                        Selection.GoHome(false);
                    } else {
                        Selection.GoEnd(false);
                    }
                }
            }

            InsertChar(c);
            //do autoindent
            DoAutoIndentIfNeed();
        }

        DoCaretVisible();
        Invalidate();

        OnKeyPressed(sourceC);

        return true;
    }

    private void DoAutoIndentIfNeed() {
        if (Selection.ColumnSelectionMode) {
            return;
        }

        if (AutoIndent) {
            DoCaretVisible();
            int needSpaces = CalcAutoIndent(Selection.Start.iLine);
            if (this[Selection.Start.iLine].AutoIndentSpacesNeededCount != needSpaces) {
                DoAutoIndent(Selection.Start.iLine);
                this[Selection.Start.iLine].AutoIndentSpacesNeededCount = needSpaces;
            }
        }
    }

    private void RemoveSpacesAfterCaret() {
        if (!Selection.IsEmpty) {
            return;
        }

        while (Selection.CharAfterStart == ' ') {
            Selection.GoRight(true);
        }

        ClearSelected();
    }

    /// <summary>
    /// Inserts autoindent's spaces in the line
    /// </summary>
    public virtual void DoAutoIndent(int iLine) {
        if (Selection.ColumnSelectionMode) {
            return;
        }

        Place oldStart = Selection.Start;
        //
        int needSpaces = CalcAutoIndent(iLine);
        //
        int spaces = lines[iLine].StartSpacesCount;
        int needToInsert = needSpaces - spaces;
        if (needToInsert < 0) {
            needToInsert = -Math.Min(-needToInsert, spaces);
        }

        //insert start spaces
        if (needToInsert == 0) {
            return;
        }

        Selection.Start = new Place(0, iLine);
        if (needToInsert > 0) {
            InsertText(new String(' ', needToInsert));
        } else {
            Selection.Start = new Place(0, iLine);
            Selection.End = new Place(-needToInsert, iLine);
            ClearSelected();
        }

        Selection.Start = new Place(Math.Min(lines[iLine].Count, Math.Max(0, oldStart.iChar + needToInsert)), iLine);
    }

    /// <summary>
    /// Returns needed start space count for the line
    /// </summary>
    public virtual int CalcAutoIndent(int iLine) {
        if (iLine < 0 || iLine >= LinesCount) {
            return 0;
        }


        EventHandler<AutoIndentEventArgs> calculator = AutoIndentNeeded;
        if (calculator == null) {
            if (Language != Language.Custom && SyntaxHighlighter != null) {
                calculator = SyntaxHighlighter.AutoIndentNeeded;
            } else {
                calculator = CalcAutoIndentShiftByCodeFolding;
            }
        }

        int needSpaces = 0;

        var stack = new Stack<AutoIndentEventArgs>();
        //calc indent for previous lines, find stable line
        int i;
        for (i = iLine - 1; i >= 0; i--) {
            var args = new AutoIndentEventArgs(i, lines[i].Text, i > 0 ? lines[i - 1].Text : "", TabLength);
            calculator(this, args);
            stack.Push(args);
            if (args.Shift == 0 && args.LineText.Trim() != "") {
                break;
            }
        }

        int indent = lines[i >= 0 ? i : 0].StartSpacesCount;
        while (stack.Count != 0) {
            indent += stack.Pop().ShiftNextLines;
        }

        //clalc shift for current line
        var a = new AutoIndentEventArgs(iLine, lines[iLine].Text, iLine > 0 ? lines[iLine - 1].Text : "", TabLength);
        calculator(this, a);
        needSpaces = indent + a.Shift;

        return needSpaces;
    }

    internal virtual void CalcAutoIndentShiftByCodeFolding(object sender, AutoIndentEventArgs args) {
        //inset TAB after start folding marker
        if (string.IsNullOrEmpty(lines[args.iLine].FoldingEndMarker) &&
            !string.IsNullOrEmpty(lines[args.iLine].FoldingStartMarker)) {
            args.ShiftNextLines = TabLength;
            return;
        }

        //remove TAB before end folding marker
        if (!string.IsNullOrEmpty(lines[args.iLine].FoldingEndMarker) &&
            string.IsNullOrEmpty(lines[args.iLine].FoldingStartMarker)) {
            args.Shift = -TabLength;
            args.ShiftNextLines = -TabLength;
            return;
        }
    }

    private int GetMinStartSpacesCount(int fromLine, int toLine) {
        if (fromLine > toLine) {
            return 0;
        }

        int result = int.MaxValue;
        for (int i = fromLine; i <= toLine; i++) {
            int count = lines[i].StartSpacesCount;
            if (count < result) {
                result = count;
            }
        }

        return result;
    }

    /// <summary>
    /// Undo last operation
    /// </summary>
    public void Undo() {
        lines.Manager.Undo();
        DoCaretVisible();
        Invalidate();
    }

    /// <summary>
    /// Redo
    /// </summary>
    public void Redo() {
        lines.Manager.Redo();
        DoCaretVisible();
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) {
        if (keyData == Keys.Tab && !AcceptsTab) {
            return false;
        }

        if (keyData == Keys.Enter && !AcceptsReturn) {
            return false;
        }

        if ((keyData & Keys.Alt) == Keys.None) {
            Keys keys = keyData & Keys.KeyCode;
            if (keys == Keys.Return) {
                return true;
            }
        }

        if ((keyData & Keys.Alt) != Keys.Alt) {
            switch ((keyData & Keys.KeyCode)) {
                case Keys.Prior:
                case Keys.Next:
                case Keys.End:
                case Keys.Home:
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;

                case Keys.Escape:
                    return false;

                case Keys.Tab:
                    return (keyData & Keys.Control) == Keys.None;
            }
        }

        return base.IsInputKey(keyData);
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        if (BackBrush == null) {
            base.OnPaintBackground(e);
        } else {
            e.Graphics.FillRectangle(BackBrush, ClientRectangle);
        }
    }

    /// <summary>
    /// Draw control
    /// </summary>
    protected override void OnPaint(PaintEventArgs e) {
        if (needRecalc) {
            Recalc();
        }

        if (needRecalcFoldingLines) {
            RecalcFoldingLines();
        }

        visibleMarkers.Clear();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var servicePen = new Pen(ServiceLinesColor);
        Brush changedLineBrush = new SolidBrush(ChangedLineBgColor);
        Brush activeLineBrush = new SolidBrush(PlayingLineBgColor);
        Brush saveStateLineBrush = new SolidBrush(SaveStateBgColor);
        Brush indentBrush = new SolidBrush(IndentBackColor);
        Brush paddingBrush = new SolidBrush(PaddingBackColor);
        Brush currentLineBrush = new SolidBrush(CurrentLineColor);
        //draw padding area
        //top
        e.Graphics.FillRectangle(paddingBrush, 0, -VerticalScroll.Value, ClientSize.Width, Math.Max(0, Paddings.Top - 1));
        //bottom
        int bottomPaddingStartY = wordWrapLinesCount * charHeight + Paddings.Top;
        e.Graphics.FillRectangle(paddingBrush, 0, bottomPaddingStartY - VerticalScroll.Value, ClientSize.Width, ClientSize.Height);
        //right
        int rightPaddingStartX = LeftIndent + maxLineLength * CharWidth + Paddings.Left + 1;
        e.Graphics.FillRectangle(paddingBrush, rightPaddingStartX - HorizontalScroll.Value, 0, ClientSize.Width, ClientSize.Height);
        //left
        e.Graphics.FillRectangle(paddingBrush, LeftIndentLine, 0, LeftIndent - LeftIndentLine - 1, ClientSize.Height);
        if (HorizontalScroll.Value <= Paddings.Left) {
            e.Graphics.FillRectangle(paddingBrush, LeftIndent - HorizontalScroll.Value - 2, 0, Math.Max(0, Paddings.Left - 1), ClientSize.Height);
        }

        int leftTextIndent = Math.Max(LeftIndent, LeftIndent + Paddings.Left - HorizontalScroll.Value);
        int textWidth = rightPaddingStartX - HorizontalScroll.Value - leftTextIndent;
        //draw indent area
        e.Graphics.FillRectangle(indentBrush, 0, 0, LeftIndentLine, ClientSize.Height);
        if (LeftIndent > minLeftIndent) {
            e.Graphics.DrawLine(servicePen, LeftIndentLine, 0, LeftIndentLine, ClientSize.Height);
        }

        //draw preferred line width
        if (PreferredLineWidth > 0) {
            e.Graphics.DrawLine(servicePen,
                new Point(LeftIndent + Paddings.Left + PreferredLineWidth * CharWidth - HorizontalScroll.Value + 1, 0),
                new Point(LeftIndent + Paddings.Left + PreferredLineWidth * CharWidth - HorizontalScroll.Value + 1, Height));
        }

        int firstChar = (Math.Max(0, HorizontalScroll.Value - Paddings.Left)) / CharWidth;
        int lastChar = (HorizontalScroll.Value + ClientSize.Width) / CharWidth;
        //draw chars
        int startLine = YtoLineIndex(VerticalScroll.Value);
        int iLine;
        for (iLine = startLine; iLine < lines.Count; iLine++) {
            Line line = lines[iLine];
            LineInfo lineInfo = lineInfos[iLine];

            if (lineInfo.startY > VerticalScroll.Value + ClientSize.Height) {
                break;
            }

            if (lineInfo.startY + lineInfo.WordWrapStringsCount * CharHeight < VerticalScroll.Value) {
                continue;
            }

            if (lineInfo.VisibleState == VisibleState.Hidden) {
                continue;
            }

            int y = lineInfo.startY - VerticalScroll.Value;

            e.Graphics.SmoothingMode = SmoothingMode.None;
            //draw line background
            if (lineInfo.VisibleState == VisibleState.Visible) {
                if (line.BackgroundBrush != null) {
                    e.Graphics.FillRectangle(line.BackgroundBrush,
                        new Rectangle(leftTextIndent, y, textWidth, CharHeight * lineInfo.WordWrapStringsCount));
                }
            }

            //draw current line background
            if (CurrentLineColor != Color.Transparent && iLine == Selection.Start.iLine) {
                e.Graphics.FillRectangle(currentLineBrush, new Rectangle(leftTextIndent, y, ClientSize.Width, CharHeight));
            }

            //draw changed line marker
            if (ChangedLineBgColor != Color.Transparent && line.IsChanged) {
                e.Graphics.FillRectangle(changedLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
            }

            if (PlayingLineBgColor != Color.Transparent && iLine == PlayingLine) {
                e.Graphics.FillRectangle(activeLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
            }

            //draw savestate line background
            if (iLine == SaveStateLine) {
                if (SaveStateLine == PlayingLine) {
                    e.Graphics.FillRectangle(saveStateLineBrush, new RectangleF(-10, y, 15, CharHeight + 1));
                } else {
                    e.Graphics.FillRectangle(saveStateLineBrush, new RectangleF(-10, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight + 1));
                }
            }

            if (!string.IsNullOrEmpty(currentLineSuffix) && iLine == PlayingLine) {
                using var lineNumberBrush = new SolidBrush(currentTextColor);
                SizeF size = e.Graphics.MeasureString(currentLineSuffix, Font, 0, StringFormat.GenericTypographic);
                e.Graphics.DrawString(currentLineSuffix, Font, lineNumberBrush,
                    new RectangleF(ClientSize.Width - size.Width - 10, y, size.Width, CharHeight), StringFormat.GenericTypographic);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            //OnPaint event
            if (lineInfo.VisibleState == VisibleState.Visible) {
                OnPaintLine(new PaintLineEventArgs(iLine, new Rectangle(LeftIndent, y, Width, CharHeight * lineInfo.WordWrapStringsCount),
                    e.Graphics, e.ClipRectangle));
            }

            //draw line number
            if (ShowLineNumbers) {
                Color lineNumberColor = LineNumberColor;
                if (iLine == PlayingLine) {
                    lineNumberColor = PlayingLineTextColor;
                } else if (iLine == SaveStateLine) {
                    lineNumberColor = SaveStateTextColor;
                } else if (line.IsChanged) {
                    lineNumberColor = ChangedLineTextColor;
                }

                using var lineNumberBrush = new SolidBrush(lineNumberColor);
                if (PlatformUtils.Wine) {
                    e.Graphics.DrawString((iLine + lineNumberStartValue).ToString().PadLeft(LinesCount.ToString().Length, ' '), Font,
                        lineNumberBrush,
                        new RectangleF(4, y, LeftIndent + 8, CharHeight),
                        new StringFormat(StringFormatFlags.DirectionRightToLeft));
                } else {
                    int x = PlatformUtils.Mono ? -20 : -10;
                    e.Graphics.DrawString((iLine + lineNumberStartValue).ToString(), Font, lineNumberBrush,
                        new RectangleF(x, y, LeftIndent - minLeftIndent - 2 + 10, CharHeight),
                        new StringFormat(StringFormatFlags.DirectionRightToLeft));
                }
            }

            //create markers
            if (lineInfo.VisibleState == VisibleState.StartOfHiddenBlock) {
                visibleMarkers.Add(new ExpandFoldingMarker(iLine, new Rectangle(LeftIndentLine - 4, y + CharHeight / 2 - 3, 8, 8)));
            }

            if (!string.IsNullOrEmpty(line.FoldingStartMarker) && lineInfo.VisibleState == VisibleState.Visible &&
                string.IsNullOrEmpty(line.FoldingEndMarker)) {
                visibleMarkers.Add(new CollapseFoldingMarker(iLine, new Rectangle(LeftIndentLine - 4, y + CharHeight / 2 - 3, 8, 8)));
            }

            if (lineInfo.VisibleState == VisibleState.Visible && !string.IsNullOrEmpty(line.FoldingEndMarker) &&
                string.IsNullOrEmpty(line.FoldingStartMarker)) {
                e.Graphics.DrawLine(servicePen, LeftIndentLine, y + CharHeight * lineInfo.WordWrapStringsCount - 1, LeftIndentLine + 4,
                    y + CharHeight * lineInfo.WordWrapStringsCount - 1);
            }

            //draw wordwrap strings of line
            for (int iWordWrapLine = 0; iWordWrapLine < lineInfo.WordWrapStringsCount; iWordWrapLine++) {
                y = lineInfo.startY + iWordWrapLine * CharHeight - VerticalScroll.Value;
                try {
                    //draw chars
                    DrawLineChars(e, firstChar, lastChar, iLine, iWordWrapLine, LeftIndent + Paddings.Left - HorizontalScroll.Value, y);
                } catch (ArgumentOutOfRangeException) {
                    // ignore
                }
            }
        }

        int endLine = iLine - 1;

        //draw folding lines
        if (ShowFoldingLines) {
            DrawFoldingLines(e, startLine, endLine);
        }

        //draw column selection
        if (Selection.ColumnSelectionMode) {
            if (SelectionStyle.BackgroundBrush is SolidBrush) {
                var color = ((SolidBrush) SelectionStyle.BackgroundBrush).Color;
                var p1 = PlaceToPoint(Selection.Start);
                var p2 = PlaceToPoint(Selection.End);
                using (var pen = new Pen(color)) {
                    e.Graphics.DrawRectangle(pen,
                        Rectangle.FromLTRB(Math.Min(p1.X, p2.X) - 1, Math.Min(p1.Y, p2.Y), Math.Max(p1.X, p2.X),
                            Math.Max(p1.Y, p2.Y) + CharHeight));
                }
            }
        }

        //draw brackets highlighting
        if (BracketsStyle != null && leftBracketPosition != null && rightBracketPosition != null) {
            BracketsStyle.Draw(e.Graphics, PlaceToPoint(leftBracketPosition.Start), leftBracketPosition);
            BracketsStyle.Draw(e.Graphics, PlaceToPoint(rightBracketPosition.Start), rightBracketPosition);
        }

        if (BracketsStyle2 != null && leftBracketPosition2 != null && rightBracketPosition2 != null) {
            BracketsStyle2.Draw(e.Graphics, PlaceToPoint(leftBracketPosition2.Start), leftBracketPosition2);
            BracketsStyle2.Draw(e.Graphics, PlaceToPoint(rightBracketPosition2.Start), rightBracketPosition2);
        }

        e.Graphics.SmoothingMode = SmoothingMode.None;
        //draw folding indicator
        if ((startFoldingLine >= 0 || endFoldingLine >= 0) && Selection.Start == Selection.End) {
            if (endFoldingLine < lineInfos.Count) {
                //folding indicator
                int startFoldingY = (startFoldingLine >= 0 ? lineInfos[startFoldingLine].startY : 0) -
                    VerticalScroll.Value + CharHeight / 2;
                int endFoldingY = (endFoldingLine >= 0
                    ? lineInfos[endFoldingLine].startY +
                      (lineInfos[endFoldingLine].WordWrapStringsCount - 1) * CharHeight
                    : (WordWrapLinesCount + 1) * CharHeight) - VerticalScroll.Value + CharHeight;

                using (var indicatorPen = new Pen(Color.FromArgb(100, FoldingIndicatorColor), 4)) {
                    e.Graphics.DrawLine(indicatorPen, LeftIndent - 5, startFoldingY, LeftIndent - 5, endFoldingY);
                }
            }
        }

        //draw markers
        foreach (VisualMarker m in visibleMarkers) {
            m.Draw(e.Graphics, servicePen);
        }

        //draw caret
        Point car = PlaceToPoint(Selection.Start);

        if ((Focused || IsDragDrop) && car.X >= LeftIndent && CaretVisible) {
            int carWidth = IsReplaceMode ? CharWidth : 1;
            //CreateCaret(Handle, 0, carWidth, CharHeight);
            NativeMethodsWrapper.SetCaretPos(car.X, car.Y);
            //ShowCaret(Handle);
            using (Pen pen = new(CaretColor)) {
                e.Graphics.DrawLine(pen, car.X, car.Y, car.X, car.Y + CharHeight - 1);
            }
        } else {
            NativeMethodsWrapper.HideCaret(Handle);
        }

        //draw disabled mask
        if (!Enabled) {
            using (var brush = new SolidBrush(DisabledColor)) {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        //dispose resources
        servicePen.Dispose();
        changedLineBrush.Dispose();
        activeLineBrush.Dispose();
        indentBrush.Dispose();
        currentLineBrush.Dispose();
        paddingBrush.Dispose();

        base.OnPaint(e);
    }

    protected virtual void DrawFoldingLines(PaintEventArgs e, int startLine, int endLine) {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using (var pen = new Pen(Color.FromArgb(200, ServiceLinesColor)) {DashStyle = DashStyle.Dot}) {
            foreach (var iLine in foldingPairs) {
                if (iLine.Key < endLine && iLine.Value > startLine) {
                    var line = lines[iLine.Key];
                    int y = lineInfos[iLine.Key].startY - VerticalScroll.Value + CharHeight;
                    y += y % 2;

                    int y2;

                    if (iLine.Value >= LinesCount) {
                        y2 = lineInfos[LinesCount - 1].startY + CharHeight - VerticalScroll.Value;
                    } else if (lineInfos[iLine.Value].VisibleState == VisibleState.Visible) {
                        int d = 0;
                        int spaceCount = line.StartSpacesCount;
                        if (lines[iLine.Value].Count <= spaceCount || lines[iLine.Value][spaceCount].c == ' ') {
                            d = CharHeight;
                        }

                        y2 = lineInfos[iLine.Value].startY - VerticalScroll.Value + d;
                    } else {
                        continue;
                    }

                    int x = LeftIndent + Paddings.Left + line.StartSpacesCount * CharWidth - HorizontalScroll.Value;
                    if (x >= LeftIndent + Paddings.Left) {
                        e.Graphics.DrawLine(pen, x, y >= 0 ? y : 0, x, y2 < ClientSize.Height ? y2 : ClientSize.Height);
                    }
                }
            }
        }
    }

    private void TryMoveCursorBehindFrame() {
        Place start = Selection.Start;
        if (Selection.IsEmpty && InputRecord.InputFrameRegex.IsMatch(Lines[start.iLine])) {
            Selection.Start = new Place(4, start.iLine);
        }
    }

    private void DrawLineChars(PaintEventArgs e, int firstChar, int lastChar, int iLine, int iWordWrapLine, int x, int y) {
        Line line = lines[iLine];
        LineInfo lineInfo = lineInfos[iLine];
        int from = lineInfo.GetWordWrapStringStartPosition(iWordWrapLine);
        int to = lineInfo.GetWordWrapStringFinishPosition(iWordWrapLine, line);

        int startX = x;
        if (startX < LeftIndent) {
            firstChar++;
        }

        lastChar = Math.Min(to - from, lastChar);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        //folded block ?
        if (lineInfo.VisibleState == VisibleState.StartOfHiddenBlock) {
            //rendering by FoldedBlockStyle
            FoldedBlockStyle.Draw(e.Graphics, new Point(startX + firstChar * CharWidth, y),
                new Range(this, from + firstChar, iLine, from + lastChar + 1, iLine));
        } else {
            //render by custom styles
            StyleIndex currentStyleIndex = StyleIndex.None;
            int iLastFlushedChar = firstChar - 1;

            for (int iChar = firstChar; iChar <= lastChar; iChar++) {
                StyleIndex style = line[from + iChar].style;
                if (currentStyleIndex != style) {
                    FlushRendering(e.Graphics, currentStyleIndex,
                        new Point(startX + (iLastFlushedChar + 1) * CharWidth, y),
                        new Range(this, from + iLastFlushedChar + 1, iLine, from + iChar, iLine));
                    iLastFlushedChar = iChar - 1;
                    currentStyleIndex = style;
                }
            }

            FlushRendering(e.Graphics, currentStyleIndex, new Point(startX + (iLastFlushedChar + 1) * CharWidth, y),
                new Range(this, from + iLastFlushedChar + 1, iLine, from + lastChar + 1, iLine));
        }

        //draw selection
        if (!Selection.IsEmpty && lastChar >= firstChar) {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            var textRange = new Range(this, from + firstChar, iLine, from + lastChar + 1, iLine);
            textRange = Selection.GetIntersectionWith(textRange);
            if (textRange != null && SelectionStyle != null) {
                SelectionStyle.Draw(e.Graphics, new Point(startX + (textRange.Start.iChar - from) * CharWidth, y),
                    textRange);
            }
        }
    }

    private void FlushRendering(Graphics gr, StyleIndex styleIndex, Point pos, Range range) {
        if (range.End > range.Start) {
            int mask = 1;
            bool hasTextStyle = false;
            for (int i = 0; i < Styles.Length; i++) {
                if (Styles[i] != null && ((int) styleIndex & mask) != 0) {
                    Style style = Styles[i];
                    bool isTextStyle = style is TextStyle;
                    if (!hasTextStyle || !isTextStyle || AllowSeveralTextStyleDrawing)
                        //cancelling secondary rendering by TextStyle
                    {
                        try {
                            style.Draw(gr, pos, range); //rendering
                        } catch (ArgumentOutOfRangeException) {
                            // ignore
                        }
                    }

                    hasTextStyle |= isTextStyle;
                }

                mask = mask << 1;
            }

            //draw by default renderer
            if (!hasTextStyle) {
                try {
                    DefaultStyle.Draw(gr, pos, range);
                } catch (ArgumentOutOfRangeException) {
                    // ignore
                }
            }
        }
    }

    protected override void OnEnter(EventArgs e) {
        base.OnEnter(e);
        mouseIsDrag = false;
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left) {
            VisualMarker marker = FindVisualMarkerForPoint(e.Location);
            //click on marker
            if (marker != null) {
                mouseIsDrag = false;
                OnMarkerClick(e, marker);
                return;
            }

            mouseIsDrag = true;
            //
            CheckAndChangeSelectionType();
            //click on text
            Place oldEnd = Selection.End;
            Selection.BeginUpdate();
            if (Selection.ColumnSelectionMode) {
                Selection.Start = PointToPlaceSimple(e.Location);
                Selection.ColumnSelectionMode = true;
            } else {
                Selection.Start = PointToPlace(e.Location);
            }

            if ((lastModifiers & Keys.Shift) != 0) {
                Selection.End = oldEnd;
            }

            Selection.EndUpdate();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if ((e.Button & MouseButtons.Left) != 0) {
            mouseIsDrag = false;
        }
    }

    private void CheckAndChangeSelectionType() {
        //change selection type to ColumnSelectionMode
        if ((ModifierKeys & Keys.Alt) != 0 && !WordWrap) {
            Selection.ColumnSelectionMode = true;
        } else {
            //change selection type to Range
            Selection.ColumnSelectionMode = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        if (lastModifiers == Keys.Control) {
            if ((e.Delta < 0 && Font.Size > 6) || (Font.Size < 300 && e.Delta > 0)) {
                Font = new Font(Font.FontFamily, Font.Size + (e.Delta > 0 ? 1 : -1), Font.Style);
                Invalidate();
            }
        } else if ((lastModifiers & Keys.Shift) == Keys.Shift) {
            TweakFrames(e.Delta > 0, e);
        } else {
            Invalidate();
            base.OnMouseWheel(e);
            OnVisibleRangeChanged();
        }
    }

    private void TweakFrames(bool up, MouseEventArgs e = null) {
        if (selection.Start.iLine != selection.End.iLine) {
            e = null;
        }

        Range origSelection = selection.Clone();

        int startLine = selection.Start.iLine;
        int endLine = e == null ? selection.End.iLine : PointToPlace(e.Location).iLine;
        if (startLine > endLine) {
            int temp = startLine;
            startLine = endLine;
            endLine = temp;
        }

        InputRecord startRecord = Studio.Instance.InputRecords[startLine];
        InputRecord endRecord = Studio.Instance.InputRecords[endLine];

        if (!startRecord.IsInput && !endRecord.IsInput) {
            return;
        }

        if (!startRecord.IsInput) {
            startRecord = endRecord;
        } else if (!endRecord.IsInput) {
            endRecord = startRecord;
        }

        if (startRecord == endRecord) {
            if (up) {
                startRecord.Frames++;
            } else if (startRecord.Frames > 0) {
                startRecord.Frames--;
            }
        } else {
            if (up) {
                if (endRecord.Frames <= 0) {
                    return;
                }

                startRecord.Frames++;
                endRecord.Frames--;
            } else {
                if (startRecord.Frames <= 0) {
                    return;
                }

                startRecord.Frames--;
                endRecord.Frames++;
            }
        }


        List<string> result = new();
        for (int i = startLine; i <= endLine; i++) {
            result.Add(Studio.Instance.InputRecords[i].ToString());
        }

        Selection = new Range(this, 0, startLine, GetLineLength(endLine), endLine);
        SelectedText = string.Join("\n", result);
        Selection = origSelection;

        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);

        if (e.Button == MouseButtons.Left && mouseIsDrag) {
            Place oldEnd = Selection.End;
            Selection.BeginUpdate();
            if (Selection.ColumnSelectionMode) {
                Selection.Start = PointToPlaceSimple(e.Location);
                Selection.ColumnSelectionMode = true;
            } else {
                Selection.Start = PointToPlace(e.Location);
            }

            Selection.End = oldEnd;
            Selection.EndUpdate();
            DoCaretVisible();
            Invalidate();
            return;
        }

        VisualMarker marker = FindVisualMarkerForPoint(e.Location);
        if (marker != null) {
            Cursor = marker.Cursor;
        } else {
            Cursor = Cursors.IBeam;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);

        VisualMarker m = FindVisualMarkerForPoint(e.Location);
        if (m != null) {
            OnMarkerDoubleClick(m);
            return;
        }

        Place p = PointToPlace(e.Location);
        int fromX = p.iChar;
        int toX = p.iChar;

        for (int i = p.iChar; i < lines[p.iLine].Count; i++) {
            char c = lines[p.iLine][i].c;
            if (char.IsLetterOrDigit(c) || c == '_') {
                toX = i + 1;
            } else {
                break;
            }
        }

        for (int i = p.iChar - 1; i >= 0; i--) {
            char c = lines[p.iLine][i].c;
            if (char.IsLetterOrDigit(c) || c == '_') {
                fromX = i;
            } else {
                break;
            }
        }

        Selection.Start = new Place(toX, p.iLine);
        Selection.End = new Place(fromX, p.iLine);

        Invalidate();
    }

    private int YtoLineIndex(int y) {
        int i = lineInfos.BinarySearch(new LineInfo(-10), new LineYComparer(y));
        i = i < 0 ? -i - 2 : i;
        if (i < 0) {
            return 0;
        }

        if (i > lines.Count - 1) {
            return lines.Count - 1;
        }

        return i;
    }

    /// <summary>
    /// Gets nearest line and char position from coordinates
    /// </summary>
    /// <param name="point">Point</param>
    /// <returns>Line and char position</returns>
    public Place PointToPlace(Point point) {
        point.Offset(HorizontalScroll.Value, VerticalScroll.Value);
        point.Offset(-LeftIndent - Paddings.Left, 0);
        int iLine = YtoLineIndex(point.Y);
        int y = 0;

        for (; iLine < lines.Count; iLine++) {
            y = lineInfos[iLine].startY + lineInfos[iLine].WordWrapStringsCount * CharHeight;
            if (y > point.Y && lineInfos[iLine].VisibleState == VisibleState.Visible) {
                break;
            }
        }

        if (iLine >= lines.Count) {
            iLine = lines.Count - 1;
        }

        if (lineInfos[iLine].VisibleState != VisibleState.Visible) {
            iLine = FindPrevVisibleLine(iLine);
        }

        //
        int iWordWrapLine = lineInfos[iLine].WordWrapStringsCount;
        do {
            iWordWrapLine--;
            y -= CharHeight;
        } while (y > point.Y);

        if (iWordWrapLine < 0) {
            iWordWrapLine = 0;
        }

        //
        int start = lineInfos[iLine].GetWordWrapStringStartPosition(iWordWrapLine);
        int finish = lineInfos[iLine].GetWordWrapStringFinishPosition(iWordWrapLine, lines[iLine]);
        int x = (int) Math.Round((float) point.X / CharWidth);
        x = x < 0 ? start : start + x;
        if (x > finish) {
            x = finish + 1;
        }

        if (x > lines[iLine].Count) {
            x = lines[iLine].Count;
        }

        return new Place(x, iLine);
    }

    private Place PointToPlaceSimple(Point point) {
        point.Offset(HorizontalScroll.Value, VerticalScroll.Value);
        point.Offset(-LeftIndent - Paddings.Left, 0);
        int iLine = YtoLineIndex(point.Y);
        int x = (int) Math.Round((float) point.X / CharWidth);
        if (x < 0) {
            x = 0;
        }

        return new Place(x, iLine);
    }

    /// <summary>
    /// Gets nearest absolute text position for given point
    /// </summary>
    /// <param name="point">Point</param>
    /// <returns>Position</returns>
    public int PointToPosition(Point point) {
        return PlaceToPosition(PointToPlace(point));
    }

    /// <summary>
    /// Fires TextChanging event
    /// </summary>
    public virtual void OnTextChanging(ref string text) {
        ClearBracketsPositions();

        if (TextChanging != null) {
            var args = new TextChangingEventArgs {InsertingText = text};
            TextChanging(this, args);
            text = args.InsertingText;
            if (args.Cancel) {
                text = string.Empty;
            }
        }
    }

    public virtual void OnTextChanging() {
        string temp = null;
        OnTextChanging(ref temp);
    }

    public void UpdateHighlighting() {
        var r = new Range(this);
        r.SelectAll();
        OnSyntaxHighlight(new TextChangedEventArgs(r));
    }

    /// <summary>
    /// Fires TextChanged event
    /// </summary>
    public virtual void OnTextChanged() {
        var r = new Range(this);
        r.SelectAll();
        OnTextChanged(new TextChangedEventArgs(r));
    }

    /// <summary>
    /// Fires TextChanged event
    /// </summary>
    public virtual void OnTextChanged(int fromLine, int toLine) {
        var r = new Range(this);
        r.Start = new Place(0, Math.Min(fromLine, toLine));
        r.End = new Place(lines[Math.Max(fromLine, toLine)].Count, Math.Max(fromLine, toLine));
        OnTextChanged(new TextChangedEventArgs(r));
    }

    /// <summary>
    /// Fires TextChanged event
    /// </summary>
    public virtual void OnTextChanged(Range r) {
        OnTextChanged(new TextChangedEventArgs(r));
    }

    public void BeginUpdate() {
        if (updating == 0) {
            updatingRange = null;
        }

        updating++;
    }

    public void EndUpdate() {
        updating--;

        if (updating == 0 && updatingRange != null) {
            updatingRange.Expand();
            OnTextChanged(updatingRange);
        }
    }

    /// <summary>
    /// Fires TextChanged event
    /// </summary>
    protected virtual void OnTextChanged(TextChangedEventArgs args) {
        args.ChangedRange.Normalize();

        if (updating > 0) {
            if (updatingRange == null) {
                updatingRange = args.ChangedRange.Clone();
            } else {
                if (updatingRange.Start.iLine > args.ChangedRange.Start.iLine) {
                    updatingRange.Start = new Place(0, args.ChangedRange.Start.iLine);
                }

                if (updatingRange.End.iLine < args.ChangedRange.End.iLine) {
                    updatingRange.End = new Place(lines[args.ChangedRange.End.iLine].Count,
                        args.ChangedRange.End.iLine);
                }

                updatingRange = updatingRange.GetIntersectionWith(Range);
            }

            return;
        }

        IsChanged = true;
        TextVersion++;
        MarkLinesAsChanged(args.ChangedRange);
        //
        if (wordWrap) {
            RecalcWordWrap(args.ChangedRange.Start.iLine, args.ChangedRange.End.iLine);
        }

        //
        base.OnTextChanged(args);

        //dalayed event stuffs
        if (delayedTextChangedRange == null) {
            delayedTextChangedRange = args.ChangedRange.Clone();
        } else {
            delayedTextChangedRange = delayedTextChangedRange.GetUnionWith(args.ChangedRange);
        }

        OnSyntaxHighlight(args);

        if (TextChanged != null) {
            TextChanged(this, args);
        }

        base.OnTextChanged(EventArgs.Empty);

        OnVisibleRangeChanged();
    }

    private void MarkLinesAsChanged(Range range) {
        for (int iLine = range.Start.iLine; iLine <= range.End.iLine; iLine++) {
            if (iLine >= 0 && iLine < lines.Count) {
                lines[iLine].IsChanged = true;
            }
        }
    }

    /// <summary>
    /// Fires SelectionCnaged event
    /// </summary>
    public virtual void OnSelectionChanged() {
        //find folding markers for highlighting
        if (HighlightFoldingIndicator) {
            HighlightFoldings();
        }

        if (SelectionChanged != null) {
            SelectionChanged(this, new EventArgs());
        }
    }

    //find folding markers for highlighting
    private void HighlightFoldings() {
        if (LinesCount == 0) {
            return;
        }

        //
        int prevStartFoldingLine = startFoldingLine;
        int prevEndFoldingLine = endFoldingLine;
        //
        startFoldingLine = -1;
        endFoldingLine = -1;
        //
        string marker = null;
        int counter = 0;
        for (int i = Selection.Start.iLine; i >= Math.Max(Selection.Start.iLine - maxLinesForFolding, 0); i--) {
            bool hasStartMarker = lines.LineHasFoldingStartMarker(i);
            bool hasEndMarker = lines.LineHasFoldingEndMarker(i);

            if (hasEndMarker && hasStartMarker) {
                continue;
            }

            if (hasStartMarker) {
                counter--;
                if (counter == -1) //found start folding
                {
                    startFoldingLine = i;
                    marker = lines[i].FoldingStartMarker;
                    break;
                }
            }

            if (hasEndMarker && i != Selection.Start.iLine) {
                counter++;
            }
        }

        if (startFoldingLine >= 0) {
            //find end of block
            endFoldingLine = FindEndOfFoldingBlock(startFoldingLine, maxLinesForFolding);
            if (endFoldingLine == startFoldingLine) {
                endFoldingLine = -1;
            }
        }

        if (startFoldingLine != prevStartFoldingLine || endFoldingLine != prevEndFoldingLine) {
            OnFoldingHighlightChanged();
        }
    }

    protected virtual void OnFoldingHighlightChanged() {
        if (FoldingHighlightChanged != null) {
            FoldingHighlightChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnGotFocus(EventArgs e) {
        SetAsCurrentTB();
        base.OnGotFocus(e);
        //Invalidate(new Rectangle(PlaceToPoint(Selection.Start), new Size(2, CharHeight+1)));
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e) {
        base.OnLostFocus(e);
        //Invalidate(new Rectangle(PlaceToPoint(Selection.Start), new Size(2, CharHeight+1)));
        Invalidate();
    }

    /// <summary>
    /// Gets absolute text position from line and char position
    /// </summary>
    /// <param name="point">Line and char position</param>
    /// <returns>Point of char</returns>
    public int PlaceToPosition(Place point) {
        if (point.iLine < 0 || point.iLine >= lines.Count ||
            point.iChar >= lines[point.iLine].Count + Environment.NewLine.Length) {
            return -1;
        }

        int result = 0;
        for (int i = 0; i < point.iLine; i++) {
            result += lines[i].Count + Environment.NewLine.Length;
        }

        result += point.iChar;

        return result;
    }

    /// <summary>
    /// Gets line and char position from absolute text position
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public Place PositionToPlace(int pos) {
        if (pos < 0) {
            return new Place(0, 0);
        }

        for (int i = 0; i < lines.Count; i++) {
            int lineLength = lines[i].Count + Environment.NewLine.Length;
            if (pos < lines[i].Count) {
                return new Place(pos, i);
            }

            if (pos < lineLength) {
                return new Place(lines[i].Count, i);
            }

            pos -= lineLength;
        }

        if (lines.Count > 0) {
            return new Place(lines[lines.Count - 1].Count, lines.Count - 1);
        } else {
            return new Place(0, 0);
        }

        //throw new ArgumentOutOfRangeException("Position out of range");
    }

    /// <summary>
    /// Gets absolute char position from char position
    /// </summary>
    public Point PositionToPoint(int pos) {
        return PlaceToPoint(PositionToPlace(pos));
    }

    /// <summary>
    /// Gets point for given line and char position
    /// </summary>
    /// <param name="place">Line and char position</param>
    /// <returns>Coordiantes</returns>
    public Point PlaceToPoint(Place place) {
        if (place.iLine >= lineInfos.Count) {
            return new Point();
        }

        int y = lineInfos[place.iLine].startY;
        //
        int iWordWrapIndex = lineInfos[place.iLine].GetWordWrapStringIndex(place.iChar);
        y += iWordWrapIndex * CharHeight;
        int x = (place.iChar - lineInfos[place.iLine].GetWordWrapStringStartPosition(iWordWrapIndex)) * CharWidth;
        //
        y = y - VerticalScroll.Value;
        x = LeftIndent + Paddings.Left + x - HorizontalScroll.Value;

        return new Point(x, y);
    }

    /// <summary>
    /// Get range of text
    /// </summary>
    /// <param name="fromPos">Absolute start position</param>
    /// <param name="toPos">Absolute finish position</param>
    /// <returns>Range</returns>
    public Range GetRange(int fromPos, int toPos) {
        var sel = new Range(this);
        sel.Start = PositionToPlace(fromPos);
        sel.End = PositionToPlace(toPos);
        return sel;
    }

    /// <summary>
    /// Get range of text
    /// </summary>
    /// <param name="fromPlace">Line and char position</param>
    /// <param name="toPlace">Line and char position</param>
    /// <returns>Range</returns>
    public Range GetRange(Place fromPlace, Place toPlace) {
        return new(this, fromPlace, toPlace);
    }

    /// <summary>
    /// Finds ranges for given regex pattern
    /// </summary>
    /// <param name="regexPattern">Regex pattern</param>
    /// <returns>Enumeration of ranges</returns>
    public IEnumerable<Range> GetRanges(string regexPattern) {
        var range = new Range(this);
        range.SelectAll();
        //
        foreach (Range r in range.GetRanges(regexPattern, RegexOptions.None)) {
            yield return r;
        }
    }

    /// <summary>
    /// Finds ranges for given regex pattern
    /// </summary>
    /// <param name="regexPattern">Regex pattern</param>
    /// <returns>Enumeration of ranges</returns>
    public IEnumerable<Range> GetRanges(string regexPattern, RegexOptions options) {
        var range = new Range(this);
        range.SelectAll();
        //
        foreach (Range r in range.GetRanges(regexPattern, options)) {
            yield return r;
        }
    }

    /// <summary>
    /// Get text of given line
    /// </summary>
    /// <param name="iLine">Line index</param>
    /// <returns>Text</returns>
    public string GetLineText(int iLine) {
        if (iLine < 0 || iLine >= lines.Count) {
            throw new ArgumentOutOfRangeException("Line index out of range");
        }

        var sb = new StringBuilder(lines[iLine].Count);
        foreach (Char c in lines[iLine]) {
            sb.Append(c.c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exapnds folded block
    /// </summary>
    /// <param name="iLine">Start line</param>
    public void ExpandFoldedBlock(int iLine) {
        if (iLine < 0 || iLine >= lines.Count) {
            throw new ArgumentOutOfRangeException("Line index out of range");
        }

        //find all hidden lines afetr iLine
        int end = iLine;
        for (; end < LinesCount - 1; end++) {
            if (lineInfos[end + 1].VisibleState != VisibleState.Hidden) {
                break;
            }
        }

        ExpandBlock(iLine, end);
    }

    /// <summary>
    /// Expand collapsed block
    /// </summary>
    public void ExpandBlock(int fromLine, int toLine) {
        int from = Math.Min(fromLine, toLine);
        int to = Math.Max(fromLine, toLine);
        for (int i = from; i <= to; i++) {
            SetVisibleState(i, VisibleState.Visible);
        }

        needRecalc = true;
        Invalidate();
    }

    /// <summary>
    /// Expand collapsed block
    /// </summary>
    /// <param name="iLine">Any line inside collapsed block</param>
    public void ExpandBlock(int iLine) {
        if (lineInfos[iLine].VisibleState == VisibleState.Visible) {
            return;
        }

        for (int i = iLine; i < LinesCount; i++) {
            if (lineInfos[i].VisibleState == VisibleState.Visible) {
                break;
            } else {
                SetVisibleState(i, VisibleState.Visible);
                needRecalc = true;
            }
        }

        for (int i = iLine - 1; i >= 0; i--) {
            if (lineInfos[i].VisibleState == VisibleState.Visible) {
                break;
            } else {
                SetVisibleState(i, VisibleState.Visible);
                needRecalc = true;
            }
        }

        Invalidate();
    }

    /// <summary>
    /// Collapses all folding blocks
    /// </summary>
    public void CollapseAllFoldingBlocks() {
        for (int i = 0; i < LinesCount; i++) {
            if (lines.LineHasFoldingStartMarker(i)) {
                int iFinish = FindEndOfFoldingBlock(i);
                if (iFinish >= 0) {
                    CollapseBlock(i, iFinish);
                    i = iFinish;
                }
            }
        }

        OnVisibleRangeChanged();
    }

    /// <summary>
    /// Exapnds all folded blocks
    /// </summary>
    /// <param name="iLine"></param>
    public void ExpandAllFoldingBlocks() {
        for (int i = 0; i < LinesCount; i++) {
            SetVisibleState(i, VisibleState.Visible);
        }

        OnVisibleRangeChanged();
        Invalidate();
    }

    /// <summary>
    /// Collapses folding block
    /// </summary>
    /// <param name="iLine">Start folding line</param>
    public void CollapseFoldingBlock(int iLine) {
        if (iLine < 0 || iLine >= lines.Count) {
            throw new ArgumentOutOfRangeException("Line index out of range");
        }

        if (string.IsNullOrEmpty(lines[iLine].FoldingStartMarker)) {
            throw new ArgumentOutOfRangeException("This line is not folding start line");
        }

        //find end of block
        int i = FindEndOfFoldingBlock(iLine);
        //collapse
        if (i >= 0) {
            CollapseBlock(iLine, i);
        }
    }

    private int FindEndOfFoldingBlock(int iStartLine) {
        return FindEndOfFoldingBlock(iStartLine, int.MaxValue);
    }

    protected virtual int FindEndOfFoldingBlock(int iStartLine, int maxLines) {
        //find end of block
        int i;
        string marker = lines[iStartLine].FoldingStartMarker;
        Stack<string> stack = new();

        for (i = iStartLine /*+1*/; i < LinesCount; i++) {
            if (lines.LineHasFoldingEndMarker(i)) {
                string m = lines[i].FoldingEndMarker;
                while (stack.Count > 0 && stack.Pop() != m) {
                    ;
                }

                if (stack.Count == 0) {
                    return i;
                }
            }

            if (lines.LineHasFoldingStartMarker(i)) {
                stack.Push(lines[i].FoldingStartMarker);
            }

            maxLines--;
            if (maxLines < 0) {
                return i;
            }
        }

        //return -1;
        return LinesCount - 1;
    }

    /// <summary>
    /// Start foilding marker for the line
    /// </summary>
    public string GetLineFoldingStartMarker(int iLine) {
        if (lines.LineHasFoldingStartMarker(iLine)) {
            return lines[iLine].FoldingStartMarker;
        }

        return null;
    }

    /// <summary>
    /// End foilding marker for the line
    /// </summary>
    public string GetLineFoldingEndMarker(int iLine) {
        if (lines.LineHasFoldingEndMarker(iLine)) {
            return lines[iLine].FoldingEndMarker;
        }

        return null;
    }

    protected virtual void RecalcFoldingLines() {
        if (!needRecalcFoldingLines) {
            return;
        }

        needRecalcFoldingLines = false;
        if (!ShowFoldingLines) {
            return;
        }

        foldingPairs.Clear();
        //
        var range = VisibleRange;
        int startLine = Math.Max(range.Start.iLine - maxLinesForFolding, 0);
        int endLine = Math.Min(range.End.iLine + maxLinesForFolding, Math.Max(range.End.iLine, LinesCount - 1));
        var stack = new Stack<int>();
        for (int i = startLine; i <= endLine; i++) {
            bool hasStartMarker = lines.LineHasFoldingStartMarker(i);
            bool hasEndMarker = lines.LineHasFoldingEndMarker(i);

            if (hasEndMarker && hasStartMarker) {
                continue;
            }

            if (hasStartMarker) {
                stack.Push(i);
            }

            if (hasEndMarker) {
                string m = lines[i].FoldingEndMarker;
                while (stack.Count > 0) {
                    int iStartLine = stack.Pop();
                    foldingPairs[iStartLine] = i;
                    if (m == lines[iStartLine].FoldingStartMarker) {
                        break;
                    }
                }
            }
        }

        while (stack.Count > 0) {
            foldingPairs[stack.Pop()] = endLine + 1;
        }
    }

    /// <summary>
    /// Collapse text block
    /// </summary>
    public void CollapseBlock(int fromLine, int toLine) {
        int from = Math.Min(fromLine, toLine);
        int to = Math.Max(fromLine, toLine);
        if (from == to) {
            return;
        }

        //find first non empty line
        for (; from <= to; from++) {
            if (GetLineText(from).Trim().Length > 0) {
                //hide lines
                for (int i = from + 1; i <= to; i++) {
                    SetVisibleState(i, VisibleState.Hidden);
                }

                SetVisibleState(from, VisibleState.StartOfHiddenBlock);
                Invalidate();
                break;
            }
        }

        //Move caret outside
        from = Math.Min(fromLine, toLine);
        to = Math.Max(fromLine, toLine);
        int newLine = FindNextVisibleLine(to);
        if (newLine == to) {
            newLine = FindPrevVisibleLine(@from);
        }

        Selection.Start = new Place(0, newLine);
        //
        needRecalc = true;
        Invalidate();
    }


    internal int FindNextVisibleLine(int iLine) {
        if (iLine >= lines.Count - 1) {
            return iLine;
        }

        int old = iLine;
        do {
            iLine++;
        } while (iLine < lines.Count - 1 && lineInfos[iLine].VisibleState != VisibleState.Visible);

        if (lineInfos[iLine].VisibleState != VisibleState.Visible) {
            return old;
        } else {
            return iLine;
        }
    }


    internal int FindPrevVisibleLine(int iLine) {
        if (iLine <= 0) {
            return iLine;
        }

        int old = iLine;
        do {
            iLine--;
        } while (iLine > 0 && lineInfos[iLine].VisibleState != VisibleState.Visible);

        if (lineInfos[iLine].VisibleState != VisibleState.Visible) {
            return old;
        } else {
            return iLine;
        }
    }

    private VisualMarker FindVisualMarkerForPoint(Point p) {
        foreach (VisualMarker m in visibleMarkers) {
            if (m.rectangle.Contains(p)) {
                return m;
            }
        }

        return null;
    }

    /// <summary>
    /// Insert TAB into front of seletcted lines
    /// </summary>
    public void IncreaseIndent() {
        if (Selection.IsEmpty) {
            return;
        }

        Range old = Selection.Clone();
        int from = Math.Min(Selection.Start.iLine, Selection.End.iLine);
        int to = Math.Max(Selection.Start.iLine, Selection.End.iLine);
        BeginUpdate();
        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        for (int i = from; i <= to; i++) {
            if (lines[i].Count == 0) {
                continue;
            }

            Selection.Start = new Place(0, i);
            lines.Manager.ExecuteCommand(new InsertTextCommand(TextSource, new String(' ', TabLength)));
        }

        lines.Manager.EndAutoUndoCommands();
        Selection.Start = new Place(0, from);
        Selection.End = new Place(lines[to].Count, to);
        needRecalc = true;
        Selection.EndUpdate();
        EndUpdate();
        Invalidate();
    }

    /// <summary>
    /// Remove TAB from front of seletcted lines
    /// </summary>
    public void DecreaseIndent() {
        if (Selection.IsEmpty) {
            return;
        }

        Range old = Selection.Clone();
        int from = Math.Min(Selection.Start.iLine, Selection.End.iLine);
        int to = Math.Max(Selection.Start.iLine, Selection.End.iLine);
        BeginUpdate();
        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        for (int i = from; i <= to; i++) {
            Selection.Start = new Place(0, i);
            Selection.End = new Place(Math.Min(lines[i].Count, TabLength), i);
            if (Selection.Text.Trim() == "") {
                ClearSelected();
            }
        }

        lines.Manager.EndAutoUndoCommands();
        Selection.Start = new Place(0, from);
        Selection.End = new Place(lines[to].Count, to);
        needRecalc = true;
        EndUpdate();
        Selection.EndUpdate();
    }

    /// <summary>
    /// Insert autoindents into selected lines
    /// </summary>
    public void DoAutoIndent() {
        if (Selection.ColumnSelectionMode) {
            return;
        }

        Range r = Selection.Clone();
        r.Normalize();
        //
        BeginUpdate();
        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        //
        for (int i = r.Start.iLine; i <= r.End.iLine; i++) {
            DoAutoIndent(i);
        }

        //
        lines.Manager.EndAutoUndoCommands();
        Selection.Start = r.Start;
        Selection.End = r.End;
        Selection.Expand();
        //
        Selection.EndUpdate();
        EndUpdate();
    }

    /// <summary>
    /// Insert prefix into front of seletcted lines
    /// </summary>
    public void InsertLinePrefix(string prefix) {
        Range old = Selection.Clone();
        int from = Math.Min(Selection.Start.iLine, Selection.End.iLine);
        int to = Math.Max(Selection.Start.iLine, Selection.End.iLine);
        BeginUpdate();
        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        int spaces = GetMinStartSpacesCount(from, to);
        for (int i = from; i <= to; i++) {
            Selection.Start = new Place(spaces, i);
            lines.Manager.ExecuteCommand(new InsertTextCommand(TextSource, prefix));
        }

        Selection.Start = new Place(0, from);
        Selection.End = new Place(lines[to].Count, to);
        needRecalc = true;
        lines.Manager.EndAutoUndoCommands();
        Selection.EndUpdate();
        EndUpdate();
        Invalidate();
    }

    /// <summary>
    /// Remove prefix from front of seletcted lines
    /// </summary>
    public void RemoveLinePrefix(string prefix) {
        Range old = Selection.Clone();
        int from = Math.Min(Selection.Start.iLine, Selection.End.iLine);
        int to = Math.Max(Selection.Start.iLine, Selection.End.iLine);
        BeginUpdate();
        Selection.BeginUpdate();
        lines.Manager.BeginAutoUndoCommands();
        for (int i = from; i <= to; i++) {
            string text = lines[i].Text;
            string trimmedText = text.TrimStart();
            if (trimmedText.StartsWith(prefix)) {
                int spaces = text.Length - trimmedText.Length;
                Selection.Start = new Place(spaces, i);
                Selection.End = new Place(spaces + prefix.Length, i);
                ClearSelected();
            }
        }

        Selection.Start = new Place(0, from);
        Selection.End = new Place(lines[to].Count, to);
        needRecalc = true;
        lines.Manager.EndAutoUndoCommands();
        Selection.EndUpdate();
        EndUpdate();
    }

    /// <summary>
    /// Begins AutoUndo block.
    /// All changes of text between BeginAutoUndo() and EndAutoUndo() will be canceled in one operation Undo.
    /// </summary>
    public void BeginAutoUndo() {
        lines.Manager.BeginAutoUndoCommands();
    }

    /// <summary>
    /// Ends AutoUndo block.
    /// All changes of text between BeginAutoUndo() and EndAutoUndo() will be canceled in one operation Undo.
    /// </summary>
    public void EndAutoUndo() {
        lines.Manager.EndAutoUndoCommands();
    }

    public virtual void OnVisualMarkerClick(MouseEventArgs args, StyleVisualMarker marker) {
        if (VisualMarkerClick != null) {
            VisualMarkerClick(this, new VisualMarkerEventArgs(marker.Style, marker, args));
        }
    }

    protected virtual void OnMarkerClick(MouseEventArgs args, VisualMarker marker) {
        if (marker is StyleVisualMarker) {
            OnVisualMarkerClick(args, marker as StyleVisualMarker);
            return;
        }

        if (marker is CollapseFoldingMarker) {
            CollapseFoldingBlock((marker as CollapseFoldingMarker).iLine);
            OnVisibleRangeChanged();
            Invalidate();
            return;
        }

        if (marker is ExpandFoldingMarker) {
            ExpandFoldedBlock((marker as ExpandFoldingMarker).iLine);
            OnVisibleRangeChanged();
            Invalidate();
            return;
        }

        if (marker is FoldedAreaMarker) {
            //select folded block
            int iStart = (marker as FoldedAreaMarker).iLine;
            int iEnd = FindEndOfFoldingBlock(iStart);
            if (iEnd < 0) {
                return;
            }

            Selection.BeginUpdate();
            Selection.Start = new Place(0, iStart);
            Selection.End = new Place(lines[iEnd].Count, iEnd);
            Selection.EndUpdate();
            Invalidate();
            return;
        }
    }

    protected virtual void OnMarkerDoubleClick(VisualMarker marker) {
        if (marker is FoldedAreaMarker) {
            ExpandFoldedBlock((marker as FoldedAreaMarker).iLine);
            Invalidate();
            return;
        }
    }

    private void ClearBracketsPositions() {
        leftBracketPosition = null;
        rightBracketPosition = null;
        leftBracketPosition2 = null;
        rightBracketPosition2 = null;
    }

    private void HighlightBrackets(char LeftBracket, char RightBracket, ref Range leftBracketPosition,
        ref Range rightBracketPosition) {
        if (!Selection.IsEmpty) {
            return;
        }

        if (LinesCount == 0) {
            return;
        }

        //
        Range oldLeftBracketPosition = leftBracketPosition;
        Range oldRightBracketPosition = rightBracketPosition;
        Range range = Selection.Clone(); //need clone because we will move caret
        int counter = 0;
        int maxIterations = maxBracketSearchIterations;
        while (range.GoLeftThroughFolded()) //move caret left
        {
            if (range.CharAfterStart == LeftBracket) {
                counter++;
            }

            if (range.CharAfterStart == RightBracket) {
                counter--;
            }

            if (counter == 1) {
                //highlighting
                range.End = new Place(range.Start.iChar + 1, range.Start.iLine);
                leftBracketPosition = range;
                break;
            }

            //
            maxIterations--;
            if (maxIterations <= 0) {
                break;
            }
        }

        //
        range = Selection.Clone(); //need clone because we will move caret
        counter = 0;
        maxIterations = maxBracketSearchIterations;
        do {
            if (range.CharAfterStart == LeftBracket) {
                counter++;
            }

            if (range.CharAfterStart == RightBracket) {
                counter--;
            }

            if (counter == -1) {
                //highlighting
                range.End = new Place(range.Start.iChar + 1, range.Start.iLine);
                rightBracketPosition = range;
                break;
            }

            //
            maxIterations--;
            if (maxIterations <= 0) {
                break;
            }
        } while (range.GoRightThroughFolded()); //move caret right

        if (oldLeftBracketPosition != leftBracketPosition ||
            oldRightBracketPosition != rightBracketPosition) {
            Invalidate();
        }
    }

    public virtual void OnSyntaxHighlight(TextChangedEventArgs args) {
        Range range;

        switch (HighlightingRangeType) {
            case HighlightingRangeType.VisibleRange:
                range = VisibleRange.GetUnionWith(args.ChangedRange);
                break;
            case HighlightingRangeType.AllTextRange:
                range = Range;
                break;
            default:
                range = args.ChangedRange;
                break;
        }

        if (SyntaxHighlighter != null) {
            if (Language == Language.Custom && !string.IsNullOrEmpty(DescriptionFile)) {
                SyntaxHighlighter.HighlightSyntax(DescriptionFile, range);
            } else {
                SyntaxHighlighter.HighlightSyntax(Language, range);
            }
        }
    }

    private void InitializeComponent() {
        SuspendLayout();
        //
        // FastColoredTextBox
        //
        Name = "FastColoredTextBox";
        ResumeLayout(false);
    }

    /// <summary>
    /// Prints range of text
    /// </summary>
    public virtual void Print(Range range, PrintDialogSettings settings) {
        //prepare export with wordwrapping
        var exporter = new ExportToHTML();
        exporter.UseBr = true;
        exporter.UseForwardNbsp = true;
        exporter.UseNbsp = true;
        exporter.UseStyleTag = false;
        exporter.IncludeLineNumbers = settings.IncludeLineNumbers;

        if (range == null) {
            range = Range;
        }

        if (range.Text == string.Empty) {
            return;
        }

        //generate HTML
        string HTML = exporter.GetHtml(range);
        HTML = "<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\"><head><title>" + PrepareHtmlText(settings.Title) +
               "</title></head>" + HTML + SelectHTMLRangeScript();
        string tempFile = Path.GetTempPath() + "fctb.html";
        File.WriteAllText(tempFile, HTML);

        //create wb
        var wb = new WebBrowser();
        wb.Tag = settings;
        wb.Visible = false;
        wb.Location = new Point(-1000, -1000);
        wb.Parent = this;
        wb.StatusTextChanged += new EventHandler(wb_StatusTextChanged);
        wb.Navigate(tempFile);
    }

    protected virtual string PrepareHtmlText(string s) {
        return s.Replace("<", "&lt;").Replace(">", "&gt;").Replace("&", "&amp;");
    }

    void wb_StatusTextChanged(object sender, EventArgs e) {
        var wb = sender as WebBrowser;
        if (wb.StatusText.Contains("#print")) {
            var settings = wb.Tag as PrintDialogSettings;
            try {
                //show print dialog
                if (settings.ShowPrintPreviewDialog) {
                    wb.ShowPrintPreviewDialog();
                } else {
                    if (settings.ShowPageSetupDialog) {
                        wb.ShowPageSetupDialog();
                    }

                    if (settings.ShowPrintDialog) {
                        wb.ShowPrintDialog();
                    } else {
                        wb.Print();
                    }
                }
            } finally {
                //destroy webbrowser
                wb.Parent = null;
                wb.Dispose();
            }
        }
    }

    /// <summary>
    /// Prints all text
    /// </summary>
    public void Print(PrintDialogSettings settings) {
        Print(Range, settings);
    }

    /// <summary>
    /// Prints all text, without any dialog windows
    /// </summary>
    public void Print() {
        Print(Range, new PrintDialogSettings {ShowPageSetupDialog = false, ShowPrintDialog = false, ShowPrintPreviewDialog = false});
    }

    private string SelectHTMLRangeScript() {
        var sel = Selection.Clone();
        sel.Normalize();
        int start = PlaceToPosition(sel.Start) - sel.Start.iLine;
        int len = sel.Text.Length - (sel.End.iLine - sel.Start.iLine);
        return string.Format(
            @"<script type=""text/javascript"">
try{{
	var sel = document.selection;
	var rng = sel.createRange();
	rng.moveStart(""character"", {0});
	rng.moveEnd(""character"", {1});
	rng.select();
}}catch(ex){{}}
window.status = ""#print"";
</script>", start, len);
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (disposing) {
            if (SyntaxHighlighter != null) {
                SyntaxHighlighter.Dispose();
            }

            if (Font != null) {
                Font.Dispose();
            }

            if (TextSource != null) {
                TextSource.Dispose();
            }
        }
    }

    protected virtual void OnPaintLine(PaintLineEventArgs e) {
        PaintLine?.Invoke(this, e);
    }

    internal void OnLineInserted(int index) {
        OnLineInserted(index, 1);
    }

    internal void OnLineInserted(int index, int count) {
        LineInserted?.Invoke(this, new LineInsertedEventArgs(index, count));
    }

    internal void OnLineRemoved(int index, int count, List<int> removedLineIds) {
        if (count > 0) {
            LineRemoved?.Invoke(this, new LineRemovedEventArgs(index, count, removedLineIds));
        }
    }

    /// <summary>
    /// Open file binding mode
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="enc"></param>
    public void OpenBindingFile(string fileName, Encoding enc) {
        try {
            FileOpening?.Invoke(this, new EventArgs());
            var fts = new FileTextSource(this);
            InitTextSource(fts);
            fts.OpenFile(fileName, enc);
            IsChanged = false;
            OnVisibleRangeChanged();
            UpdateHighlighting();
            FileOpened?.Invoke(this, new EventArgs());
        } catch {
            InitTextSource(CreateTextSource());
            lines.InsertLine(0, TextSource.CreateLine());
            IsChanged = false;
            throw;
        }
    }

    /// <summary>
    /// Close file binding mode
    /// </summary>
    public void CloseBindingFile() {
        if (lines is FileTextSource) {
            var fts = lines as FileTextSource;
            fts.CloseFile();

            InitTextSource(CreateTextSource());
            lines.InsertLine(0, TextSource.CreateLine());
            IsChanged = false;
            Invalidate();
        }
    }

    /// <summary>
    /// Save text to the file
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="enc"></param>
    public void SaveToFile(string fileName, Encoding enc) {
        if (string.IsNullOrEmpty(fileName)) {
            return;
        }

        lines.SaveToFile(fileName, enc);
        IsChanged = false;
        OnVisibleRangeChanged();
        UpdateHighlighting();
    }

    /// <summary>
    /// Set VisibleState of line
    /// </summary>
    public void SetVisibleState(int iLine, VisibleState state) {
        LineInfo li = lineInfos[iLine];
        li.VisibleState = state;
        lineInfos[iLine] = li;
        needRecalc = true;
    }

    /// <summary>
    /// Returns VisibleState of the line
    /// </summary>
    public VisibleState GetVisibleState(int iLine) {
        return lineInfos[iLine].VisibleState;
    }

    /// <summary>
    /// Shows Goto dialog form
    /// </summary>
    public void ShowGoToDialog() {
        lastModifiers = Keys.None;
        var form = new GoToForm();
        form.TotalLineCount = LinesCount;
        form.SelectedLineNumber = Selection.Start.iLine + 1;

        if (form.ShowDialog() == DialogResult.OK) {
            int line = Math.Min(LinesCount - 1, Math.Max(0, form.SelectedLineNumber - 1));
            Selection = new Range(this, 0, line, 0, line);
            DoSelectionVisible();
        }
    }

    /// <summary>
    /// Occurs when undo/redo stack is changed
    /// </summary>
    public void OnUndoRedoStateChanged() {
        if (UndoRedoStateChanged != null) {
            UndoRedoStateChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Search lines by regex pattern
    /// </summary>
    public List<int> FindLines(string searchPattern, RegexOptions options = RegexOptions.None) {
        List<int> iLines = new();
        foreach (var r in Range.GetRangesByLines(searchPattern, options)) {
            iLines.Add(r.Start.iLine);
        }

        return iLines;
    }

    /// <summary>
    /// Removes given lines
    /// </summary>
    public void RemoveLines(List<int> iLines) {
        TextSource.Manager.ExecuteCommand(new RemoveLinesCommand(TextSource, iLines));
        if (iLines.Count > 0) {
            IsChanged = true;
        }

        if (LinesCount == 0) {
            Text = "";
        }

        NeedRecalc();
        Invalidate();
    }

    /// <summary>
    /// Removes given line
    /// </summary>
    public void RemoveLine(int line) {
        Selection = new Range(this, 0, line, 0, line);
        ClearCurrentLine();
    }

    #region Nested type: LineYComparer

    private class LineYComparer : IComparer<LineInfo> {
        private readonly int Y;

        public LineYComparer(int Y) {
            this.Y = Y;
        }

        #region IComparer<LineInfo> Members

        public int Compare(LineInfo x, LineInfo y) {
            if (x.startY == -10) {
                return -y.startY.CompareTo(Y);
            } else {
                return x.startY.CompareTo(Y);
            }
        }

        #endregion
    }

    #endregion

    #region Drag and drop

    private bool IsDragDrop { get; set; }

    protected override void OnDragEnter(DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.Text)) {
            e.Effect = DragDropEffects.Copy;
            IsDragDrop = true;
        }

        base.OnDragEnter(e);
    }

    protected override void OnDragDrop(DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.Text)) {
            if (ParentForm != null) {
                ParentForm.Activate();
            }

            Focus();
            var p = PointToClient(new Point(e.X, e.Y));
            Selection.Start = PointToPlace(p);
            InsertText(e.Data.GetData(DataFormats.Text).ToString());
            IsDragDrop = false;
        }

        base.OnDragDrop(e);
    }

    protected override void OnDragOver(DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.Text)) {
            var p = PointToClient(new Point(e.X, e.Y));
            Selection.Start = PointToPlace(p);
            Invalidate();
        }

        base.OnDragOver(e);
    }

    protected override void OnDragLeave(EventArgs e) {
        IsDragDrop = false;
        base.OnDragLeave(e);
    }

    #endregion
}

public class PaintLineEventArgs : PaintEventArgs {
    public PaintLineEventArgs(int iLine, Rectangle rect, Graphics gr, Rectangle clipRect)
        : base(gr, clipRect) {
        LineIndex = iLine;
        LineRect = rect;
    }

    public int LineIndex { get; private set; }
    public Rectangle LineRect { get; private set; }
}

public class LineInsertedEventArgs : EventArgs {
    public LineInsertedEventArgs(int index, int count) {
        Index = index;
        Count = count;
    }

    /// <summary>
    /// Inserted line index
    /// </summary>
    public int Index { get; private set; }

    /// <summary>
    /// Count of inserted lines
    /// </summary>
    public int Count { get; private set; }
}

public class LineRemovedEventArgs : EventArgs {
    public LineRemovedEventArgs(int index, int count, List<int> removedLineIds) {
        Index = index;
        Count = count;
        RemovedLineUniqueIds = removedLineIds;
    }

    /// <summary>
    /// Removed line index
    /// </summary>
    public int Index { get; private set; }

    /// <summary>
    /// Count of removed lines
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// UniqueIds of removed lines
    /// </summary>
    public List<int> RemovedLineUniqueIds { get; private set; }
}

/// <summary>
/// TextChanged event argument
/// </summary>
public class TextChangedEventArgs : EventArgs {
    /// <summary>
    /// Constructor
    /// </summary>
    public TextChangedEventArgs(Range changedRange) {
        ChangedRange = changedRange;
    }

    /// <summary>
    /// This range contains changed area of text
    /// </summary>
    public Range ChangedRange { get; set; }
}

public class TextChangingEventArgs : EventArgs {
    public string InsertingText { get; set; }

    /// <summary>
    /// Set to true if you want to cancel text inserting
    /// </summary>
    public bool Cancel { get; set; }
}

public enum WordWrapMode {
    /// <summary>
    /// Word wrapping by control width
    /// </summary>
    WordWrapControlWidth,

    /// <summary>
    /// Word wrapping by preferred line width (PreferredLineWidth)
    /// </summary>
    WordWrapPreferredWidth,

    /// <summary>
    /// Char wrapping by control width
    /// </summary>
    CharWrapControlWidth,

    /// <summary>
    /// Char wrapping by preferred line width (PreferredLineWidth)
    /// </summary>
    CharWrapPreferredWidth
}

public class PrintDialogSettings {
    public PrintDialogSettings() {
        ShowPrintPreviewDialog = true;
        Title = "";
        Footer = "";
        Header = "";
        Footer = "";
        Header = "";
    }

    public bool ShowPageSetupDialog { get; set; }
    public bool ShowPrintDialog { get; set; }
    public bool ShowPrintPreviewDialog { get; set; }

    /// <summary>
    /// Title of page. If you want to print Title on the page, insert code &amp;w in Footer or Header.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Footer of page.
    /// Here you can use special codes: &amp;w (Window title), &amp;D, &amp;d (Date), &amp;t(), &amp;4 (Time), &amp;p (Current page number), &amp;P (Total number of pages),  &amp;&amp; (A single ampersand), &amp;b (Right justify text, Center text. If &amp;b occurs once, then anything after the &amp;b is right justified. If &amp;b occurs twice, then anything between the two &amp;b is centered, and anything after the second &amp;b is right justified).
    /// More detailed see <see cref="http://msdn.microsoft.com/en-us/library/aa969429(v=vs.85).aspx">here</see>
    /// </summary>
    public string Footer { get; set; }

    /// <summary>
    /// Header of page
    /// Here you can use special codes: &amp;w (Window title), &amp;D, &amp;d (Date), &amp;t(), &amp;4 (Time), &amp;p (Current page number), &amp;P (Total number of pages),  &amp;&amp; (A single ampersand), &amp;b (Right justify text, Center text. If &amp;b occurs once, then anything after the &amp;b is right justified. If &amp;b occurs twice, then anything between the two &amp;b is centered, and anything after the second &amp;b is right justified).
    /// More detailed see <see cref="http://msdn.microsoft.com/en-us/library/aa969429(v=vs.85).aspx">here</see>
    /// </summary>
    public string Header { get; set; }

    /// <summary>
    /// Prints line numbers
    /// </summary>
    public bool IncludeLineNumbers { get; set; }
}

public class AutoIndentEventArgs : EventArgs {
    public AutoIndentEventArgs(int iLine, string lineText, string prevLineText, int tabLength) {
        this.iLine = iLine;
        LineText = lineText;
        PrevLineText = prevLineText;
        TabLength = tabLength;
    }

    public int iLine { get; internal set; }
    public int TabLength { get; internal set; }
    public string LineText { get; internal set; }
    public string PrevLineText { get; internal set; }

    /// <summary>
    /// Additional spaces count for this line, relative to previous line
    /// </summary>
    public int Shift { get; set; }

    /// <summary>
    /// Additional spaces count for next line, relative to previous line
    /// </summary>
    public int ShiftNextLines { get; set; }
}

/// <summary>
/// Type of highlighting
/// </summary>
public enum HighlightingRangeType {
    /// <summary>
    /// Highlight only changed range of text. Highest performance.
    /// </summary>
    ChangedRange,

    /// <summary>
    /// Highlight visible range of text. Middle performance.
    /// </summary>
    VisibleRange,

    /// <summary>
    /// Highlight all (visible and invisible) text. Lowest performance.
    /// </summary>
    AllTextRange
}