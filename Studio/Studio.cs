using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CelesteStudio.Communication;
using CelesteStudio.Entities;
using CelesteStudio.RichText;
using StudioCommunication;

namespace CelesteStudio;

public partial class Studio : BaseForm {
    private static readonly string MaxStatusHeight20Line = "".PadRight(20, '\n') + (PlatformUtils.Mono ? "." : "");
    public static Studio Instance;
    public static Version Version { get; private set; }
    private static List<string> RecentFiles => Settings.Instance.RecentFiles;
    public readonly List<InputRecord> InputRecords = new() {new InputRecord("")};
    private readonly StringBuilder statusBarBuilder = new();

    private DateTime lastChanged = DateTime.MinValue;
    private FormWindowState lastWindowState = FormWindowState.Normal;
    private States tasStates;
    private ToolTip tooltip;
    private ToolTip titleBarTooltip;
    private int totalFrames, currentFrame;
    private bool updating;

    private bool DisableTyping => tasStates.HasFlag(States.Enable) && !tasStates.HasFlag(States.FrameStep) && StudioCommunicationBase.Initialized ||
                                  CommunicationWrapper.Forwarding;

    private string TitleBarText =>
        (string.IsNullOrEmpty(CurrentFileName) ? "Celeste.tas" : Path.GetFileName(CurrentFileName))
        + " - Studio v"
        + Version.ToString(3)
        + (string.IsNullOrEmpty(CurrentFileName) ? string.Empty : "   " + CurrentFileName);

    private string CurrentFileName {
        get => richText.CurrentFileName;
        set => richText.CurrentFileName = value;
    }

    private Tuple<string, int> previousFile;

    public Studio(string[] args) {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version;

        InitializeComponent();
        MonoFix();
        InitSettings();
        InitLocationSize();
        InitTitleBarTooltip();
        InitMenu();
        InitDragDrop();
        InitFont(Settings.Instance.Font ?? fontDialog.Font);
        EnableStudio(false);
        TryOpenFile(args);

        Text = TitleBarText;
    }

    private void InitSettings() {
        Settings.Load();
        Settings.StartWatcher();
        VisibleChanged += (sender, args) => {
            if (!Visible) {
                SaveSettings();
            }
        };

        TopMost = Settings.Instance.AlwaysOnTop;
    }

    private void InitLocationSize() {
        DesktopLocation = Settings.Instance.Location;
        Size = Settings.Instance.Size;

        if (!IsTitleBarVisible()) {
            DesktopLocation = new Point(100, 100);
            Size = new Size(400, 800);
        }
    }

    private void InitTitleBarTooltip() {
        NonClientMouseHover += (_, _) => {
            if (TitleRectangle.Contains(Cursor.Position) && !string.IsNullOrEmpty(CurrentFileName)) {
                titleBarTooltip ??= new ToolTip();
                titleBarTooltip.Show(CurrentFileName, this, TitleRectangle.Left - Left + 1, TitleRectangle.Bottom - Top, int.MaxValue);
            }
        };

        NonClientMouseLeave += (_, _) => {
            if (!TitleRectangle.Contains(Cursor.Position)) {
                titleBarTooltip?.Hide(this);
            }
        };

        richText.MouseHover += (_, _) => titleBarTooltip?.Hide(this);
    }

    private void InitMenu() {
        richText.MouseClick += (sender, args) => {
            if (DisableTyping) {
                return;
            }

            if ((args.Button & MouseButtons.Right) == MouseButtons.Right) {
                if (richText.Selection.IsEmpty) {
                    richText.Selection.Start = richText.PointToPlace(args.Location);
                    richText.Invalidate();
                }

                tasTextContextMenuStrip.Show(Cursor.Position);
            } else if (ModifierKeys == Keys.Control && (args.Button & MouseButtons.Left) == MouseButtons.Left) {
                TryOpenReadFile();
                TryGoToPlayLine();
            }
        };

        statusBar.MouseClick += (sender, args) => {
            if ((args.Button & MouseButtons.Right) == 0) {
                return;
            }

            statusBarContextMenuStrip.Show(Cursor.Position);
        };

        openRecentMenuItem.DropDownItemClicked += (sender, args) => {
            ToolStripItem clickedItem = args.ClickedItem;
            if (clickedItem.Text == "Clear") {
                openPreviousFileToolStripMenuItem.Enabled = false;
                RecentFiles.Clear();
                if (File.Exists(CurrentFileName)) {
                    RecentFiles.Add(CurrentFileName);
                }

                return;
            }

            if (!File.Exists(clickedItem.Text)) {
                openRecentMenuItem.Owner.Hide();
                RecentFiles.Remove(clickedItem.Text);
            }

            OpenFile(clickedItem.Text);
        };

        openBackupToolStripMenuItem.DropDownItemClicked += (sender, args) => {
            ToolStripItem clickedItem = args.ClickedItem;
            string backupFolder = richText.BackupFolder;
            if (clickedItem.Text == "Delete All Files") {
                DialogResult result = MessageBox.Show("Are you sure you want to delete all backups of this file?", "Delete All Backups",
                    MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes) {
                    Directory.Delete(backupFolder, true);
                }

                return;
            } else if (clickedItem.Text == "Show Older Files...") {
                if (!Directory.Exists(backupFolder)) {
                    Directory.CreateDirectory(backupFolder);
                }

                Process.Start(backupFolder);
                return;
            }

            string filePath = Path.Combine(backupFolder, clickedItem.Text);
            if (!File.Exists(filePath)) {
                openRecentMenuItem.Owner.Hide();
            }

            OpenFile(filePath);
        };

        settingsToolStripMenuItem.DropDown.Opacity = 0f;
    }

    private void InitDragDrop() {
        richText.DragDrop += (sender, args) => {
            string[] fileList = (string[]) args.Data.GetData(DataFormats.FileDrop, false);
            if (fileList.Length > 0 && fileList[0].EndsWith(".tas")) {
                OpenFile(fileList[0]);
            }
        };

        richText.DragEnter += (sender, args) => {
            string[] fileList = (string[]) args.Data.GetData(DataFormats.FileDrop, false);
            if (fileList.Length > 0 && fileList[0].EndsWith(".tas")) {
                args.Effect = DragDropEffects.Copy;
            }
        };
    }

    private void InitFont(Font font) {
        richText.Font = font;
        lblStatus.Font = new Font(font.FontFamily, (font.Size - 1) * 0.8f, font.Style);
    }

    private void CreateRecentFilesMenu() {
        openRecentMenuItem.DropDownItems.Clear();
        if (RecentFiles.Count == 0) {
            openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem("Nothing") {
                Enabled = false
            });
        } else {
            for (var i = RecentFiles.Count - 1; i >= 20; i--) {
                RecentFiles.Remove(RecentFiles[i]);
            }

            foreach (var fileName in RecentFiles) {
                openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem(fileName) {
                    Checked = CurrentFileName == fileName
                });
            }

            openRecentMenuItem.DropDownItems.Add(new ToolStripSeparator());
            openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem("Clear"));
        }
    }

    private void CreateBackupFilesMenu() {
        openBackupToolStripMenuItem.DropDownItems.Clear();
        string backupFolder = richText.BackupFolder;
        List<string> files = Directory.Exists(backupFolder) ? Directory.GetFiles(backupFolder).ToList() : new List<string>();
        if (files.Count == 0) {
            openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Nothing") {
                Enabled = false
            });
        } else {
            files.Sort();
            files.Reverse();

            foreach (string filePath in files.Take(20)) {
                openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem(Path.GetFileName(filePath)) {
                    Checked = CurrentFileName == filePath
                });
            }

            if (files.Count > 20) {
                openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Show Older Files..."));
            }

            openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Delete All Files"));
        }
    }

    private bool IsTitleBarVisible() {
        int titleBarHeight = RectangleToScreen(ClientRectangle).Top - Top;
        Rectangle titleBar = new(Left, Top, Width, titleBarHeight);
        foreach (Screen screen in Screen.AllScreens) {
            if (screen.Bounds.IntersectsWith(titleBar)) {
                return true;
            }
        }

        return false;
    }

    private void SaveSettings() {
        Settings.Instance.Location = DesktopLocation;
        Settings.Instance.Size = Size;
        Settings.Save();
    }

    private void ShowTooltip(string text) {
        if (tooltip == null) {
            tooltip = new ToolTip();
        } else {
            tooltip.Hide(this);
        }

        Size textSize = TextRenderer.MeasureText(text, Font);
        tooltip.Show(text, this, Width / 2 - textSize.Width / 2, Height / 2 - textSize.Height / 2, 2000);
    }

    private void TASStudio_FormClosed(object sender, FormClosedEventArgs e) {
        Settings.StopWatcher();
        SaveSettings();
        StudioCommunicationServer.Instance?.SendPath(string.Empty);
        Thread.Sleep(50);
    }

    private void Studio_Shown(object sender, EventArgs e) {
        Thread updateThread = new(UpdateLoop);
        updateThread.IsBackground = true;
        updateThread.Start();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        // if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        if (msg.Msg is 0x100 or 0x104) {
            if (!richText.IsChanged && CommunicationWrapper.CheckControls(ref msg)) {
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Studio_KeyDown(object sender, KeyEventArgs e) {
        if (richText.ReadOnly) {
            return;
        }

        try {
            if (e.Modifiers == Keys.Control) {
                switch (e.KeyCode) {
                    case Keys.S: // Ctrl + S
                        richText.SaveFile();
                        break;
                    case Keys.O: // Ctrl + O
                        OpenFile();
                        break;
                    case Keys.OemQuestion: // Ctrl + /
                    case Keys.K: // Ctrl + K
                        CommentText(true);
                        break;
                    case Keys.P: // Ctrl + P
                        ClearUncommentedBreakpoints();
                        break;
                    case Keys.OemPeriod: // Ctrl + OemPeriod -> insert/remove breakpoint
                        InsertOrRemoveText(InputRecord.BreakpointRegex, "***");
                        break;
                    case Keys.R: // Ctrl + R
                        InsertRoomName();
                        break;
                    case Keys.F: // Ctrl + F
                        DialogUtils.ShowFindDialog(richText);
                        break;
                    case Keys.G: // Ctrl + G
                        DialogUtils.ShowGoToDialog(richText);
                        break;
                    case Keys.T: // Ctrl + T
                        InsertTime();
                        break;
                    case Keys.Down: // Ctrl + Down
                        GoDownCommentAndBreakpoint(e);
                        break;
                    case Keys.Up: // Ctrl + Up
                        GoUpCommentAndBreakpoint(e);
                        break;
                    case Keys.L: // Ctrl + L
                        CombineInputs(true);
                        break;
                }
            } else if (e.Modifiers == (Keys.Control | Keys.Shift)) {
                switch (e.KeyCode) {
                    case Keys.OemQuestion: // Ctrl + Shift + /
                    case Keys.K: // Ctrl + Shift + K
                        CommentText(false);
                        break;
                    case Keys.S: // Ctrl + Shift + S
                        SaveAsFile();
                        break;
                    case Keys.P: // Ctrl + Shift + P
                        ClearBreakpoints();
                        break;
                    case Keys.OemPeriod: // Ctrl + Shift + OemPeriod -> insert/remove savestate
                        InsertOrRemoveText(InputRecord.BreakpointRegex, "***S");
                        break;
                    case Keys.R: // Ctrl + Shift + R
                        InsertDataFromGame(GameDataType.ConsoleCommand, false);
                        break;
                    case Keys.C: // Ctrl + Shift + C
                        CopyGameInfo();
                        break;
                    case Keys.D: // Ctrl + Shift + D
                        StudioCommunicationServer.Instance?.ExternalReset();
                        break;
                    case Keys.L: // Ctrl + Shift + L
                        CombineInputs(false);
                        break;
                }
            } else if (e.Modifiers == (Keys.Control | Keys.Alt)) {
                switch (e.KeyCode) {
                    case Keys.C: // Ctrl + Alt + C
                        CopyFilePath();
                        break;
                    case Keys.P: // Ctrl + Alt + P
                        CommentUncommentAllBreakpoints();
                        break;
                    case Keys.R: // Ctrl + Alt + R
                        InsertDataFromGame(GameDataType.ConsoleCommand, true);
                        break;
                }
            }
        } catch (Exception ex) {
            MessageBox.Show(this, ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Console.Write(ex);
        }
    }

    private void SaveAsFile() {
        StudioCommunicationServer.Instance?.WriteWait();
        richText.SaveNewFile();
        StudioCommunicationServer.Instance?.SendPath(CurrentFileName);
        Text = TitleBarText;
        UpdateRecentFiles();
    }

    private void GoDownCommentAndBreakpoint(KeyEventArgs e) {
        List<int> commentLine = richText.FindLines(@"^\s*#|^\*\*\*");
        if (commentLine.Count > 0) {
            int line = commentLine.FirstOrDefault(i => i > richText.Selection.Start.iLine);
            if (line == 0) {
                line = richText.LinesCount - 1;
            }

            while (richText.Selection.Start.iLine < line) {
                richText.Selection.GoDown(e.Shift);
            }

            richText.ScrollLeft();
        } else {
            richText.Selection.GoDown(e.Shift);
            richText.ScrollLeft();
        }
    }

    private void GoUpCommentAndBreakpoint(KeyEventArgs e) {
        List<int> commentLine = richText.FindLines(@"^\s*#|^\*\*\*");
        if (commentLine.Count > 0) {
            int line = commentLine.FindLast(i => i < richText.Selection.Start.iLine);
            while (richText.Selection.Start.iLine > line) {
                richText.Selection.GoUp(e.Shift);
            }

            richText.ScrollLeft();
        } else {
            richText.Selection.GoUp(e.Shift);
            richText.ScrollLeft();
        }
    }

    public void TryOpenFile(string[] args) {
        if (args.Length > 0 && args[0] is { } filePath && filePath.EndsWith(".tas", StringComparison.InvariantCultureIgnoreCase) &&
            TryGetExactCasePath(filePath, out string exactPath)) {
            OpenFile(exactPath);
        }
    }

    private static bool TryGetExactCasePath(string path, out string exactPath) {
        bool result = false;
        exactPath = null;

        // DirectoryInfo accepts either a file path or a directory path, and most of its properties work for either.
        // However, its Exists property only works for a directory path.
        DirectoryInfo directory = new(path);
        if (File.Exists(path) || directory.Exists) {
            List<string> parts = new();

            DirectoryInfo parentDirectory = directory.Parent;
            while (parentDirectory != null) {
                FileSystemInfo entry = parentDirectory.EnumerateFileSystemInfos(directory.Name).First();
                parts.Add(entry.Name);

                directory = parentDirectory;
                parentDirectory = directory.Parent;
            }

            // Handle the root part (i.e., drive letter or UNC \\server\share).
            string root = directory.FullName;
            if (root.Contains(':')) {
                root = root.ToUpper();
            } else {
                string[] rootParts = root.Split('\\');
                root = string.Join("\\", rootParts.Select(part => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(part)));
            }

            parts.Add(root);
            parts.Reverse();
            exactPath = Path.Combine(parts.ToArray());
            result = true;
        }

        return result;
    }

    private void MonoFix() {
        if (PlatformUtils.Mono) {
            // remove the shortcut keys, otherwise they will be triggered when writing tas input
            fileToolStripMenuItem.Text = fileToolStripMenuItem.Text.Replace("&", "");
            settingsToolStripMenuItem.Text = settingsToolStripMenuItem.Text.Replace("&", "");
            toggleToolStripMenuItem.Text = toggleToolStripMenuItem.Text.Replace("&", "");
            helpToolStripMenuItem.Text = helpToolStripMenuItem.Text.Replace("&", "");
        }
    }

    public void OpenFile(string fileName = null, int startLine = 0) {
        if (fileName == CurrentFileName && fileName != null) {
            return;
        }

        StudioCommunicationServer.Instance?.WriteWait();

        Tuple<string, int> tuple = new(CurrentFileName, richText.Selection.Start.iLine);
        if (richText.OpenFile(fileName)) {
            previousFile = tuple;
            UpdateRecentFiles();
            richText.GoHome();
            if (startLine > 0) {
                startLine = Math.Min(startLine, richText.LinesCount - 1);
                richText.Selection = new Range(richText, 0, startLine, 0, startLine);
                richText.DoSelectionVisible();
            }
        }

        StudioCommunicationServer.Instance?.SendPath(CurrentFileName);
        Text = TitleBarText;
    }

    private void TryOpenReadFile() {
        string text = richText.CurrentStartLineText;
        string trimText = richText.CurrentStartLineText.Trim();
        Regex readRegex = new Regex("^#?read( |,)", RegexOptions.IgnoreCase);
        if (readRegex.IsMatch(trimText)) {
            Regex spaceRegex = new(@"^[^,]+?\s+[^,]");

            string[] args = spaceRegex.IsMatch(trimText) ? trimText.Split() : trimText.Split(',');

            bool jumpToEndLabel = false;
            int clickPosition = richText.Selection.Start.iChar;
            int leftSpace = text.Length - text.TrimStart().Length;
            if (args.Length >= 4 && args[0].Length + args[1].Length + args[2].Length + 2 + leftSpace < clickPosition) {
                jumpToEndLabel = true;
            }

            args = args.Select(text => text.Trim()).ToArray();
            if (args.Length >= 2) {
                string filePath = args[1];
                string fileDirectory = Path.GetDirectoryName(CurrentFileName);
                filePath = FindTheFile(fileDirectory, filePath);

                if (!File.Exists(filePath)) {
                    // for compatibility with tas files downloaded from discord
                    // discord will replace spaces in the file name with underscores
                    filePath = args[1].Replace(" ", "_");
                    filePath = FindTheFile(fileDirectory, filePath);
                }

                if (!File.Exists(filePath)) {
                    return;
                }

                int startLine = 0;

                if (jumpToEndLabel) {
                    startLine = GetLine(filePath, args[3]);
                } else if (args.Length >= 3) {
                    startLine = GetLine(filePath, args[2]);
                }

                OpenFile(filePath, startLine);
            }
        }

        string FindTheFile(string fileDirectory, string filePath) {
            // Check for full and shortened Read versions
            if (fileDirectory != null) {
                // Path.Combine can handle the case when filePath is an absolute path
                string absoluteOrRelativePath = Path.Combine(fileDirectory, filePath);
                if (File.Exists(absoluteOrRelativePath) && absoluteOrRelativePath != CurrentFileName) {
                    filePath = absoluteOrRelativePath;
                } else if (Directory.GetParent(absoluteOrRelativePath) is { } directoryInfo && Directory.Exists(directoryInfo.ToString())) {
                    string[] files = Directory.GetFiles(directoryInfo.ToString(), $"{Path.GetFileName(filePath)}*.tas");
                    if (files.FirstOrDefault(path => path != CurrentFileName) is { } shortenedFilePath) {
                        filePath = shortenedFilePath;
                    }
                }
            }

            return filePath;
        }
    }

    private void TryGoToPlayLine() {
        string lineText = richText.CurrentStartLineText.Trim();
        if (!new Regex(@"^#?play", RegexOptions.IgnoreCase).IsMatch(lineText)) {
            return;
        }

        Regex spaceRegex = new(@"^[^,]+?\s+[^,]");

        string[] args = spaceRegex.IsMatch(lineText) ? lineText.Split() : lineText.Split(',');
        args = args.Select(text => text.Trim()).ToArray();
        if (!new Regex(@"^#?play", RegexOptions.IgnoreCase).IsMatch(args[0]) || args.Length < 2) {
            return;
        }

        string lineOrLabel = args[1];
        int? lineNumber = null;
        if (int.TryParse(lineOrLabel, out int parseLine)) {
            lineNumber = parseLine;
        } else {
            List<int> findLines = richText.FindLines($"#{lineOrLabel}");
            if (findLines.Count > 0) {
                lineNumber = findLines.First();
            }
        }

        if (lineNumber.HasValue) {
            richText.GoToLine(lineNumber.Value);
        }
    }

    private static int GetLine(string path, string labelOrLineNumber) {
        if (int.TryParse(labelOrLineNumber, out int lineNumber)) {
            return lineNumber - 1;
        }

        int curLine = 0;
        foreach (string readLine in File.ReadLines(path)) {
            string line = readLine.Trim();
            if (line == $"#{labelOrLineNumber}") {
                return curLine;
            }
            curLine++;
        }

        return 0;
    }

    private void UpdateRecentFiles() {
        if (string.IsNullOrEmpty(CurrentFileName)) {
            return;
        }

        if (RecentFiles.Contains(CurrentFileName)) {
            RecentFiles.Remove(CurrentFileName);
        }

        RecentFiles.Insert(0, CurrentFileName);
        openPreviousFileToolStripMenuItem.Enabled = RecentFiles.Count >= 2;
        Settings.Instance.LastFileName = CurrentFileName;
        SaveSettings();
    }

    private void RemoveLinesMatching(string searchPattern) {
        Place prevSelectionStart = richText.Selection.Start;
        Place prevSelectionEnd = richText.Selection.End;

        List<int> breakpoints = richText.FindLines(searchPattern);
        richText.RemoveLines(breakpoints);

        var linesRemovedBeforeStart = breakpoints.Count(breakpointLine => breakpointLine < prevSelectionStart.iLine);
        var linesRemovedBeforeEnd = breakpoints.Count(breakpointLine => breakpointLine < prevSelectionEnd.iLine);

        prevSelectionStart.iLine = Math.Min(prevSelectionStart.iLine - linesRemovedBeforeStart, richText.LinesCount - 1);
        prevSelectionEnd.iLine = Math.Min(prevSelectionEnd.iLine - linesRemovedBeforeEnd, richText.LinesCount - 1);
        prevSelectionStart.iChar = Math.Min(prevSelectionStart.iChar, richText.Lines[prevSelectionStart.iLine].Length);
        prevSelectionEnd.iChar = Math.Min(prevSelectionEnd.iChar, richText.Lines[prevSelectionEnd.iLine].Length);

        richText.Selection = new Range(richText, prevSelectionStart, prevSelectionEnd);
    }

    private void ClearUncommentedBreakpoints() {
        RemoveLinesMatching(@"^\s*\*\*\*");
    }

    private void ClearBreakpoints() {
        RemoveLinesMatching(@"^\s*#*\s*\*\*\*");
    }

    private void CopyFilePath() {
        if (string.IsNullOrEmpty(CurrentFileName)) {
            return;
        }

        for (int i = 0; i < 5; i++) {
            try {
                Clipboard.SetDataObject(CurrentFileName, true);
                ShowTooltip("Copy file path success");
                return;
            } catch (ExternalException) {
                Win32Api.UnlockClipboard();
            }
        }

        MessageBox.Show("Failed to copy file path.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void CommentUncommentAllBreakpoints() {
        Range range = richText.Selection.Clone();

        List<int> uncommentedBreakpoints = richText.FindLines(@"^\s*\*\*\*");
        if (uncommentedBreakpoints.Count > 0) {
            foreach (int line in uncommentedBreakpoints) {
                richText.Selection = new Range(richText, 0, line, 0, line);
                richText.InsertText("#");
            }
        } else {
            List<int> breakpoints = richText.FindLines(@"^\s*#+\s*\*\*\*");
            foreach (int line in breakpoints) {
                richText.Selection = new Range(richText, 0, line, 0, line);
                richText.RemoveLinePrefix("#");
            }
        }

        richText.Selection = range;
        richText.ScrollLeft();
    }

    private void InsertOrRemoveText(Regex regex, string insertText) {
        int currentLine = richText.Selection.Start.iLine;
        if (regex.IsMatch(richText.Lines[currentLine])) {
            richText.RemoveLine(currentLine);
            if (currentLine == richText.LinesCount) {
                currentLine--;
            }
        } else if (currentLine >= 1 && regex.IsMatch(richText.Lines[currentLine - 1])) {
            currentLine--;
            richText.RemoveLine(currentLine);
        } else {
            InsertNewLine(insertText);
            currentLine++;
        }

        string text = richText.Lines[currentLine];
        InputRecord input = new(text);
        int cursor = 4;
        if (input.Frames == 0 && input.Actions == Actions.None) {
            cursor = text.Length;
        }

        richText.Selection = new Range(richText, cursor, currentLine, cursor, currentLine);
    }

    private void InsertRoomName() => InsertNewLine($"#lvl_{CommunicationWrapper.StudioInfo?.LevelName}");

    private void InsertTime() => InsertNewLine($"#{CommunicationWrapper.StudioInfo?.ChapterTime}");

    private void InsertDataFromGame(GameDataType gameDataType, object arg = null) {
        if (GetDataFromGame(gameDataType, arg) is { } gameData) {
            InsertNewLine(gameData);
        }
    }

    private string GetDataFromGame(GameDataType? gameDataTypes, object arg = null) {
        CommunicationWrapper.ReturnData = null;
        if (gameDataTypes.HasValue) {
            StudioCommunicationServer.Instance.GetDataFromGame(gameDataTypes.Value, arg);
        }

        int sleepTimeout = 150;
        while (CommunicationWrapper.ReturnData == null && sleepTimeout > 0) {
            Thread.Sleep(10);
            sleepTimeout -= 10;
        }

        if (CommunicationWrapper.ReturnData == null && sleepTimeout <= 0) {
            ShowTooltip("Getting data from the game timed out.");
        }

        return CommunicationWrapper.ReturnData == string.Empty ? null : CommunicationWrapper.ReturnData;
    }

    private void ToggleGameSetting(string settingName, object value, object sender, bool showResult = true) {
        if (StudioCommunicationServer.Instance == null) {
            return;
        }

        StudioCommunicationServer.Instance.ToggleGameSetting(settingName, value);
        if (showResult && GetDataFromGame(null) is { } settingStatus) {
            ShowTooltip($"{sender.ToString().Replace("&", "")}: {settingStatus}");
        }
    }

    private void InsertNewLine(string text) {
        text = text.Trim();
        int startLine = richText.Selection.Start.iLine;
        richText.Selection = new Range(richText, 0, startLine, 0, startLine);
        richText.InsertText(text + "\n");
        int lenght = text.Split('\n')[0].Length;
        richText.Selection = new Range(richText, lenght, startLine, lenght, startLine);
    }

    private void CopyGameInfo() {
        if (GetDataFromGame(GameDataType.ExactGameInfo) is { } exactGameInfo) {
            Clipboard.SetText(exactGameInfo);
        }
    }

    private void UpdateLoop() {
        bool lastHooked = false;
        while (true) {
            try {
                bool hooked = StudioCommunicationBase.Initialized;
                if (lastHooked != hooked) {
                    lastHooked = hooked;
                    Invoke((Action) delegate { EnableStudio(hooked); });
                }

                if (lastChanged.AddSeconds(0.3f) < DateTime.Now) {
                    lastChanged = DateTime.Now;
                    Invoke((Action) delegate {
                        if (!string.IsNullOrEmpty(CurrentFileName) && richText.IsChanged) {
                            richText.SaveFile();
                        }
                    });
                }

                if (hooked) {
                    UpdateValues();
                    FixSomeBugsWhenOutOfMinimized();
                    CommunicationWrapper.CheckForward();
                } else {
                    richText.ReadOnly = DisableTyping;
                }

                Thread.Sleep(14);
            } catch {
                // ignore
            }
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private void EnableStudio(bool hooked) {
        if (hooked) {
            try {
                if (string.IsNullOrEmpty(CurrentFileName)) {
                    newFileToolStripMenuItem_Click(null, null);
                }

                richText.Focus();
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        } else {
            UpdateStatusBar();

            if (File.Exists(Settings.Instance.LastFileName)
                && IsFileReadable(Settings.Instance.LastFileName)
                && string.IsNullOrEmpty(CurrentFileName)) {
                CurrentFileName = Settings.Instance.LastFileName;
                richText.ReloadFile();
            }

            StudioCommunicationServer.Run();
        }
    }

    private void UpdateValues() {
        if (InvokeRequired) {
            Invoke((Action) UpdateValues);
        } else {
            if (CommunicationWrapper.StudioInfo != null) {
                StudioInfo studioInfo = CommunicationWrapper.StudioInfo.Value;
                richText.PlayingLine = studioInfo.CurrentLine;
                richText.CurrentLineSuffix = studioInfo.CurrentLineSuffix;
                richText.SaveStateLine = studioInfo.SaveStateLine;
                currentFrame = studioInfo.CurrentFrameInTas;
                tasStates = (States) studioInfo.tasStates;
                if (tasStates.HasFlag(States.Enable) && !tasStates.HasFlag(States.FrameStep)) {
                    totalFrames = studioInfo.TotalFrames;
                }
            } else {
                currentFrame = 0;
                richText.PlayingLine = -1;
                richText.CurrentLineSuffix = string.Empty;
                richText.SaveStateLine = -1;
                tasStates = States.None;
            }

            richText.ReadOnly = DisableTyping;
            UpdateStatusBar();
        }
    }

    private void FixSomeBugsWhenOutOfMinimized() {
        if (lastWindowState == FormWindowState.Minimized && WindowState == FormWindowState.Normal) {
            richText.ScrollLeft();
            StudioCommunicationServer.Instance?.ExternalReset();
        }

        lastWindowState = WindowState;
    }

    private void tasText_LineRemoved(object sender, LineRemovedEventArgs e) {
        if (updating) {
            return;
        }

        int count = e.Count;
        while (count-- > 0) {
            InputRecord input = InputRecords[e.Index];
            totalFrames -= input.Frames;
            InputRecords.RemoveAt(e.Index);
        }

        UpdateStatusBar();
    }

    private void tasText_LineInserted(object sender, LineInsertedEventArgs e) {
        if (updating) {
            return;
        }

        RichText.RichText tas = (RichText.RichText) sender;
        int count = e.Count;
        while (count-- > 0) {
            InputRecord input = new(tas.GetLineText(e.Index + count));
            InputRecords.Insert(e.Index, input);
            totalFrames += input.Frames;
        }

        UpdateStatusBar();
    }

    private void UpdateStatusBar() {
        if (StudioCommunicationBase.Initialized) {
            string gameInfo = CommunicationWrapper.StudioInfo?.GameInfo ?? string.Empty;
            statusBarBuilder.Clear();
            if (currentFrame > 0) {
                statusBarBuilder.Append($"{currentFrame}/");
            }

            statusBarBuilder.Append($"{totalFrames}");

            int startLine = richText.Selection.Start.iLine;
            int endLine = richText.Selection.End.iLine;
            if (startLine != endLine) {
                if (endLine < startLine) {
                    int temp = startLine;
                    startLine = endLine;
                    endLine = temp;
                }

                int selectedFrames = 0;
                for (int i = startLine; i <= endLine && i < InputRecords.Count; i++) {
                    InputRecord input = InputRecords[i];
                    if (input.Frames > 0) {
                        selectedFrames += input.Frames;
                    }
                }

                if (selectedFrames > 0) {
                    statusBarBuilder.Append($" Selected: {selectedFrames}");
                }
            }

            statusBarBuilder.AppendLine();
            statusBarBuilder.Append(gameInfo);
            statusBarBuilder.Append(new string('\n', Math.Max(0, 7 - gameInfo.Split('\n').Length)));
            lblStatus.Text = statusBarBuilder.ToString();
        } else {
            lblStatus.Text = $"{totalFrames}\nSearching...";
        }

        int bottomExtraSpace = TextRenderer.MeasureText("\n", lblStatus.Font).Height / 5;
        if (Settings.Instance.ShowGameInfo) {
            int maxHeight = TextRenderer.MeasureText(MaxStatusHeight20Line, lblStatus.Font).Height + bottomExtraSpace;
            int statusBarHeight = TextRenderer.MeasureText(lblStatus.Text.Trim(), lblStatus.Font).Height + bottomExtraSpace;
            statusPanel.Height = Math.Min(maxHeight, statusBarHeight);
            statusPanel.AutoScrollMinSize = new Size(0, statusBarHeight);
            statusPanel.AutoScroll = statusBarHeight > maxHeight;
            statusBar.Height = statusBarHeight;
        } else {
            statusPanel.Height = 0;
        }

        richText.Height = ClientSize.Height - statusPanel.Height - menuStrip.Height;
    }

    private void tasText_TextChanged(object sender, TextChangedEventArgs e) {
        lastChanged = DateTime.Now;
        UpdateLines((RichText.RichText) sender, e.ChangedRange);
    }

    private void CommentText(bool toggle) {
        Range origRange = richText.Selection.Clone();

        origRange.Normalize();
        int start = origRange.Start.iLine;
        int end = origRange.End.iLine;

        List<InputRecord> selection = InputRecords.GetRange(start, end - start + 1);

        bool anyUncomment = selection.Any(record => !record.IsEmpty && !record.IsComment);

        StringBuilder result = new();
        foreach (InputRecord record in selection) {
            if (record.IsCommentRoom || record.IsCommentTime) {
                result.AppendLine(record.ToString());
            } else if (!toggle && anyUncomment || toggle && !record.IsComment) {
                if (!record.IsEmpty) {
                    result.Append("#");
                }

                result.AppendLine(record.ToString());
            } else {
                result.AppendLine(InputRecord.CommentSymbolRegex.Replace(record.ToString(), string.Empty));
            }
        }

        // remove last line break
        result.Length -= Environment.NewLine.Length;

        richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        richText.SelectedText = result.ToString();

        if (origRange.IsEmpty) {
            if (start < richText.LinesCount - 1) {
                start++;
            }

            richText.Selection = new Range(richText, 0, start, 0, start);
        } else {
            richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        }

        richText.ScrollLeft();
    }

    private void CombineInputs(bool sameActions) {
        Range origRange = richText.Selection.Clone();

        origRange.Normalize();
        int start = origRange.Start.iLine;
        int end = origRange.End.iLine;

        if (start == end) {
            if (!sameActions) {
                return;
            }

            InputRecord currentRecord = InputRecords[start];
            if (!currentRecord.IsInput) {
                return;
            }

            while (start > 1) {
                InputRecord prev = InputRecords[start - 1];
                if ((prev.IsInput || prev.IsEmpty) && prev.ActionsToString() == currentRecord.ActionsToString()) {
                    start--;
                } else {
                    break;
                }
            }

            while (end < InputRecords.Count - 1) {
                InputRecord next = InputRecords[end + 1];
                if ((next.IsInput || next.IsEmpty) && next.Actions == currentRecord.Actions &&
                    next.PressedKeys.SetEquals(currentRecord.PressedKeys)) {
                    end++;
                } else {
                    break;
                }
            }
        } else if (!sameActions) {
            // skip non input line
            while (start < end) {
                InputRecord current = InputRecords[start];
                if (!current.IsInput) {
                    start++;
                } else {
                    break;
                }
            }
        }

        if (start == end) {
            return;
        }

        List<InputRecord> selection = InputRecords.GetRange(start, end - start + 1);
        SortedDictionary<int, List<InputRecord>> groups = new();

        if (sameActions) {
            int index = start;
            InputRecord current = InputRecords[index];
            int currentIndex = index;
            groups[currentIndex] = new List<InputRecord> {current};
            while (++index <= end) {
                InputRecord next = InputRecords[index];

                // ignore empty line if combine succeeds
                int? nextIndex = null;
                if (next.IsEmptyOrZeroFrameInput && next.Next(record => !record.IsEmptyOrZeroFrameInput) is {IsInput: true} nextInput) {
                    nextIndex = InputRecords.IndexOf(nextInput);
                    if (nextIndex <= end) {
                        next = nextInput;
                    } else {
                        nextIndex = null;
                    }
                }

                if (current.IsInput && next.IsInput && current.ActionsToString() == next.ActionsToString() && !next.IsScreenTransition()) {
                    groups[currentIndex].Add(next);
                    if (nextIndex.HasValue) {
                        index = nextIndex.Value;
                    }
                } else {
                    current = InputRecords[index];
                    currentIndex = index;
                    groups[currentIndex] = new List<InputRecord> {current};
                }
            }
        } else {
            selection = selection.Where(record => !record.IsEmptyOrZeroFrameInput).ToList();
            selection.Sort((a, b) => !a.IsInput && b.IsInput ? 1 : 0);
            for (int i = 0; i < selection.Count; i++) {
                InputRecord inputRecord = selection[i];
                int groupIndex = inputRecord.IsInput ? 0 : i;
                if (!groups.ContainsKey(groupIndex)) {
                    groups[groupIndex] = new List<InputRecord>();
                }

                groups[groupIndex].Add(inputRecord);
            }
        }

        StringBuilder result = new();
        foreach (List<InputRecord> groupInputs in groups.Values) {
            if (groupInputs.Count > 1 && groupInputs.First().IsInput) {
                int combinedFrames = groupInputs.Sum(record => record.Frames);
                if (combinedFrames < 10000) {
                    result.AppendLine(new InputRecord(combinedFrames, groupInputs.First().ActionsToString()).ToString());
                } else {
                    ShowTooltip("Combine failed because the combined frames were greater than 9999");
                    if (sameActions) {
                        foreach (InputRecord inputRecord in groupInputs) {
                            result.AppendLine(inputRecord.ToString());
                        }
                    } else {
                        return;
                    }
                }
            } else {
                foreach (InputRecord inputRecord in groupInputs) {
                    result.AppendLine(inputRecord.ToString());
                }
            }
        }

        // remove last line break
        result.Length -= Environment.NewLine.Length;

        richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        richText.SelectedText = result.ToString();
        richText.ScrollLeft();
    }

    private void ConvertDashToDemoDash() {
        Range origRange = richText.Selection.Clone();

        origRange.Normalize();
        int start = origRange.Start.iLine;
        int end = origRange.End.iLine;

        while (start < end) {
            InputRecord startRecord = InputRecords[start];
            if (!startRecord.IsInput) {
                start++;
            } else {
                break;
            }
        }

        if (start == end) {
            return;
        }

        List<InputRecord> result = new();

        for (int i = start; i <= end; i++) {
            InputRecord current = InputRecords[i];

            if (current.IsInput && current.Actions is (Actions.Dash | Actions.Down) or (Actions.Dash2 | Actions.Down) &&
                current.Frames is >= 1 and <= 4) {
                Actions dash = current.HasActions(Actions.Dash) ? Actions.Dash : Actions.Dash2;
                InputRecord next = i == end ? null : InputRecords[i + 1];

                // ignore empty line if convert succeeds
                int? nextIndex = null;
                if (next?.IsEmptyOrZeroFrameInput == true && next.Next(record => !record.IsEmptyOrZeroFrameInput) is {IsInput: true} nextInput) {
                    nextIndex = InputRecords.IndexOf(nextInput);
                    if (nextIndex <= end) {
                        next = nextInput;
                    } else {
                        nextIndex = null;
                    }
                }

                if (next is {IsInput: true} && next.HasActions(dash)) {
                    next.Frames += current.Frames;
                    next.Actions = next.Actions & ~Actions.Dash & ~Actions.Dash2 | GetDemoDashActions(current);
                    result.Add(next);
                    if (nextIndex.HasValue) {
                        i = nextIndex.Value;
                    } else {
                        i++;
                    }

                    continue;
                } else {
                    // 11,D,X
                    // 40
                    // #lvl_roomName
                    // 4,D,X <- dont convert this input
                    if (current.Previous(record => record.IsInput) is not { } previous || !previous.IsScreenTransition() ||
                        previous.Previous(record => record.IsInput) is not { } previous2 || previous2.Actions != (dash | Actions.Down) ||
                        previous2.Frames > 15 - current.Frames) {
                        current.Actions = GetDemoDashActions(current);
                    }
                }
            }

            result.Add(current);
        }

        richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        richText.SelectedText = string.Join(Environment.NewLine, result);
        richText.ScrollLeft();

        Actions GetDemoDashActions(InputRecord current) {
            Actions demoDash = Actions.DemoDash;
            InputRecord neighboringInput = current.Previous(record => record.IsInput) ?? current.Next(record => record.IsInput);
            if (neighboringInput?.HasActions(demoDash) == true) {
                demoDash = Actions.DemoDash2;
            }

            return demoDash;
        }
    }

    private void UpdateLines(RichText.RichText tas, Range range) {
        if (updating) {
            return;
        }

        updating = true;

        int start = range.Start.iLine;
        int end = range.End.iLine;
        if (start > end) {
            int temp = start;
            start = end;
            end = temp;
        }

        int originalStart = start;

        bool modified = false;
        StringBuilder sb = new();
        Place place = new(0, end);
        while (start <= end) {
            InputRecord oldInput = InputRecords.Count > start ? InputRecords[start] : null;
            string text = tas[start].Text;
            InputRecord newInput = new(text);
            if (oldInput != null) {
                totalFrames -= oldInput.Frames;

                string formattedText = newInput.ToString();

                bool featherAngle = oldInput.HasActions(Actions.Feather)
                                    && !string.IsNullOrEmpty(newInput.AngleStr)
                                    && string.IsNullOrEmpty(newInput.UpperLimitStr)
                                    && text[text.Length - 1] == ','
                                    && text.Substring(0, text.Length - 1) == formattedText;

                if (text != formattedText && !featherAngle) {
                    Range oldRange = tas.Selection;
                    if (!string.IsNullOrEmpty(formattedText)) {
                        InputRecord.ProcessExclusiveActions(oldInput, newInput);
                        formattedText = newInput.ToString();

                        int index = oldRange.Start.iChar + formattedText.Length - text.Length;
                        if (index < 0) {
                            index = 0;
                        }

                        if (index > 4) {
                            index = 4;
                        }

                        if (oldInput.Frames == newInput.Frames) {
                            index = newInput.HasActions(Actions.Feather) ? formattedText.Length : 4;

                            if (!oldInput.HasActions(Actions.DashOnly) && newInput.HasActions(Actions.DashOnly)) {
                                index = formattedText.IndexOf(",A", StringComparison.InvariantCultureIgnoreCase) + 2;
                            }

                            if (!oldInput.HasActions(Actions.MoveOnly) && newInput.HasActions(Actions.MoveOnly)) {
                                index = formattedText.IndexOf(",M", StringComparison.InvariantCultureIgnoreCase) + 2;
                            }

                            if (!oldInput.HasActions(Actions.PressedKey) && newInput.HasActions(Actions.PressedKey)) {
                                index = formattedText.IndexOf(",P", StringComparison.InvariantCultureIgnoreCase) + 2;
                            }
                        }

                        place = new Place(index, start);
                    }

                    modified = true;
                } else {
                    place = new Place(4, start);
                    if (tas.Selection.IsEmpty && oldInput.HasActions(Actions.Feather) && newInput.HasActions(Actions.Feather)) {
                        string oldText = oldInput.ToString();
                        string newText = newInput.ToString();

                        if (newText.Substring(0, newText.Length - 1) == oldText) {
                            Range clone = tas.Selection.Clone();
                            tas.Selection = new Range(tas, newText.Length - 1, end, newText.Length, end);
                            tas.InsertText(newText.Last().ToString());
                            tas.Selection = clone;
                        }
                    }
                }

                text = formattedText;
                InputRecords[start] = newInput;
            } else {
                place = new Place(text.Length, start);
            }

            if (start < end) {
                sb.AppendLine(text);
            } else {
                sb.Append(text);
            }

            totalFrames += newInput.Frames;

            start++;
        }

        if (modified) {
            tas.Selection = new Range(tas, 0, originalStart, tas[end].Count, end);
            tas.SelectedText = sb.ToString();
            tas.Selection = new Range(tas, place.iChar, end, place.iChar, end);
        }

        if (tas.IsChanged) {
            Text = TitleBarText + " ***";
        }

        UpdateStatusBar();

        updating = false;
    }

    private void tasText_NoChanges(object sender, EventArgs e) {
        Text = TitleBarText;
    }

    private void tasText_FileOpening(object sender, EventArgs e) {
        InputRecords.Clear();
        totalFrames = 0;
        UpdateStatusBar();
    }

    private void tasText_LineNeeded(object sender, LineNeededEventArgs e) {
        InputRecord record = new(e.SourceLineText);
        e.DisplayedLineText = record.ToString();
    }

    private bool IsFileReadable(string fileName) {
        try {
            using (FileStream stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.None)) {
                stream.Close();
            }
        } catch (IOException) {
            return false;
        }

        //file is not locked
        return true;
    }

    private void autoRemoveExclusiveActionsToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.AutoRemoveMutuallyExclusiveActions = !Settings.Instance.AutoRemoveMutuallyExclusiveActions;
    }

    private void alwaysOnTopToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.AlwaysOnTop = !Settings.Instance.AlwaysOnTop;
        TopMost = Settings.Instance.AlwaysOnTop;
    }

    private void homeMenuItem_Click(object sender, EventArgs e) {
        Process.Start("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
    }

    private void settingsToolStripMenuItem_Opened(object sender, EventArgs e) {
        settingsToolStripMenuItem.DropDown.Opacity = 1f;
        sendInputsToCelesteMenuItem.Checked = Settings.Instance.SendInputsToCeleste;
        autoRemoveExclusiveActionsToolStripMenuItem.Checked = Settings.Instance.AutoRemoveMutuallyExclusiveActions;
        alwaysOnTopToolStripMenuItem.Checked = Settings.Instance.AlwaysOnTop;
        showGameInfoToolStripMenuItem.Checked = Settings.Instance.ShowGameInfo;
        enabledAutoBackupToolStripMenuItem.Checked = Settings.Instance.AutoBackupEnabled;
        backupRateToolStripMenuItem.Text = $"Backup Rate (minutes): {Settings.Instance.AutoBackupRate}";
        backupFileCountsToolStripMenuItem.Text = $"Backup File Count: {Settings.Instance.AutoBackupCount}";
    }

    private void openPreviousFileToolStripMenuItem_Click(object sender, EventArgs e) {
        if (RecentFiles.Count <= 1) {
            return;
        }

        string fileName = RecentFiles[1];

        if (!File.Exists(fileName)) {
            RecentFiles.Remove(fileName);
        }

        int startLine = 0;
        if (previousFile != null && previousFile.Item1 == fileName) {
            startLine = previousFile.Item2;
        }

        OpenFile(fileName, startLine);
    }

    private void sendInputsToCelesteMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.SendInputsToCeleste = !Settings.Instance.SendInputsToCeleste;
        if (settingsToolStripMenuItem.DropDown.Opacity == 0f) {
            ShowTooltip((Settings.Instance.SendInputsToCeleste ? "Enable" : "Disable") + " Send Inputs to Celeste");
        }

        settingsToolStripMenuItem.DropDown.Opacity = 0f;
    }

    private void openFileMenuItem_Click(object sender, EventArgs e) {
        OpenFile();
    }

    private void fileToolStripMenuItem_DropDownOpened(object sender, EventArgs e) {
        CreateRecentFilesMenu();
        CreateBackupFilesMenu();
        openPreviousFileToolStripMenuItem.Enabled = RecentFiles.Count >= 2;
    }

    private void insertRemoveBreakPointToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertOrRemoveText(InputRecord.BreakpointRegex, "***");
    }

    private void insertRemoveSavestateBreakPointToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertOrRemoveText(InputRecord.BreakpointRegex, "***S");
    }

    private void saveAsToolStripMenuItem_Click(object sender, EventArgs e) {
        SaveAsFile();
    }

    private void integrateReadFilesToolStripMenuItem_Click(object sender, EventArgs e) {
        IntegrateReadFiles.Generate();
    }

    private void commentUncommentTextToolStripMenuItem_Click(object sender, EventArgs e) {
        CommentText(true);
    }

    private void removeAllUncommentedBreakpointsToolStripMenuItem_Click(object sender, EventArgs e) {
        ClearUncommentedBreakpoints();
    }

    private void removeAllBreakpointsToolStripMenuItem_Click(object sender, EventArgs e) {
        ClearBreakpoints();
    }

    private void commentUncommentAllBreakpointsToolStripMenuItem_Click(object sender, EventArgs e) {
        CommentUncommentAllBreakpoints();
    }

    private void insertRoomNameToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertRoomName();
    }

    private void insertCurrentInGameTimeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertTime();
    }

    private void insertConsoleLoadCommandToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertDataFromGame(GameDataType.ConsoleCommand, false);
    }

    private void insertSimpleConsoleLoadCommandToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertDataFromGame(GameDataType.ConsoleCommand, true);
    }

    private void enforceLegalToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EnforceLegal");
    }

    private void assertToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Assert, Condition, Expected, Actual");
    }

    private void unsafeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Unsafe");
    }

    private void safeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Safe");
    }

    private void readToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Read, File Name, Starting Line, (Ending Line)");
    }

    private void playToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Play, Starting Line");
    }

    private void setToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Set, (Mod).Setting, Value");
    }

    private void invokeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Invoke, Entity.Method, Parameter");
    }

    private void pressToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Press, Key1, Key2...");
    }

    private void analogueModeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("AnalogMode, Ignore/Circle/Square/Precise");
    }

    private void stunPauseToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("StunPause\n\nEndStunPause");
    }

    private void endStunPauseToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndStunPause");
    }

    private void stunPauseModeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("StunPauseMode, Simulate/Input");
    }

    private void autoInputToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("AutoInput, 2\n   1,S,N\n  10,O\nStartAutoInput\n\nEndAutoInput");
    }

    private void startAutoInputToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("StartAutoInput");
    }

    private void endAutoInputToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndAutoInput");
    }

    private void skipAutoInputToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("SkipAutoInput");
    }

    private void startExportToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("ExportGameInfo (Path) (Entities)");
    }

    private void finishExportToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndExportGameInfo");
    }

    private void startExportRoomInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("ExportRoomInfo dump_room_info.txt");
    }

    private void finishExportRoomInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndExportRoomInfo");
    }

    private void addToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Add, (input line)");
    }

    private void skipToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Skip");
    }

    private void startExportLibTASToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("ExportLibTAS (Path)");
    }

    private void finishExportLibTASToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndExportLibTAS");
    }

    private void completeInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertDataFromGame(GameDataType.CompleteInfoCommand);
    }

    private void recordCountToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("RecordCount: 1");
    }

    private void fileTimeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("FileTime:");
    }

    private void chapterTimeToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("ChapterTime:");
    }

    private void repeatToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("Repeat 2\n\nEndRepeat");
    }

    private void endRepeatToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("EndRepeat");
    }

    private void exitGameToolStripMenuItem_Click(object sender, EventArgs e) {
        InsertNewLine("ExitGame");
    }

    private void copyGamerDataMenuItem_Click(object sender, EventArgs e) {
        CopyGameInfo();
    }

    private void fontToolStripMenuItem_Click(object sender, EventArgs e) {
        if (fontDialog.ShowDialog() != DialogResult.Cancel) {
            //check monospace font
            SizeF sizeM = RichText.RichText.GetCharSize(fontDialog.Font, 'M');
            SizeF sizeDot = RichText.RichText.GetCharSize(fontDialog.Font, '.');
            if (sizeM == sizeDot) {
                InitFont(fontDialog.Font);
                Settings.Instance.Font = fontDialog.Font;
            } else {
                ShowTooltip("Only monospaced font is allowed");
            }
        }
    }

    private void reconnectStudioAndCelesteToolStripMenuItem_Click(object sender, EventArgs e) {
        StudioCommunicationServer.Instance?.ExternalReset();
    }

    private void insertModInfoStripMenuItem1_Click(object sender, EventArgs e) {
        InsertDataFromGame(GameDataType.ModInfo);
    }

    private void SwapActionKeys(char key1, char key2) {
        if (richText.Selection.IsEmpty) {
            return;
        }

        Range range = richText.Selection.Clone();
        range.Normalize();

        int start = range.Start.iLine;
        int end = range.End.iLine;

        richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        string text = richText.SelectedText;

        StringBuilder sb = new();
        Regex swapKeyRegex = new($"{key1}|{key2}");
        foreach (string lineText in text.Split('\n')) {
            if (InputRecord.InputFrameRegex.IsMatch(lineText)) {
                sb.AppendLine(swapKeyRegex.Replace(lineText, match => match.Value == key1.ToString() ? key2.ToString() : key1.ToString()));
            } else {
                sb.AppendLine(lineText);
            }
        }

        richText.SelectedText = sb.ToString().Substring(0, sb.Length - 2);
        richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
        richText.ScrollLeft();
    }

    private void swapDashKeysStripMenuItem_Click(object sender, EventArgs e) {
        SwapActionKeys('C', 'X');
    }

    private void swapJumpKeysToolStripMenuItem_Click(object sender, EventArgs e) {
        SwapActionKeys('J', 'K');
    }

    private void swapSelectedLAndRToolStripMenuItem_Click(object sender, EventArgs e) {
        SwapActionKeys('L', 'R');
    }

    private void combineConsecutiveSameInputsToolStripMenuItem_Click(object sender, EventArgs e) {
        CombineInputs(true);
    }

    private void forceCombineInputsToolStripMenuItem_Click(object sender, EventArgs e) {
        CombineInputs(false);
    }

    private void convertDashToDemoDashToolStripMenuItem_Click(object sender, EventArgs e) {
        ConvertDashToDemoDash();
    }

    private void openReadFileToolStripMenuItem_Click(object sender, EventArgs e) {
        TryOpenReadFile();
        TryGoToPlayLine();
    }

    private void showGameInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.ShowGameInfo = !Settings.Instance.ShowGameInfo;
        SaveSettings();
        if (Settings.Instance.ShowGameInfo) {
            StudioCommunicationServer.Instance?.ExternalReset();
        }
    }

    private void convertToLibTASInputsToolStripMenuItem_Click(object sender, EventArgs e) {
        if (!StudioCommunicationBase.Initialized || Process.GetProcessesByName("Celeste").Length == 0) {
            MessageBox.Show("This feature requires the support of CelesteTAS mod, please launch the game.",
                "Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        using (SaveFileDialog dialog = new()) {
            dialog.DefaultExt = ".txt";
            dialog.AddExtension = true;
            dialog.Filter = "TXT|*.txt";
            dialog.FilterIndex = 0;
            if (!string.IsNullOrEmpty(CurrentFileName)) {
                dialog.InitialDirectory = Path.GetDirectoryName(CurrentFileName);
                dialog.FileName = Path.GetFileNameWithoutExtension(CurrentFileName) + "_libTAS_inputs.txt";
            } else {
                dialog.FileName = "libTAS_inputs.txt";
            }

            if (dialog.ShowDialog() == DialogResult.OK) {
                StudioCommunicationServer.Instance.ConvertToLibTas(dialog.FileName);
            }
        }
    }

    private void newFileToolStripMenuItem_Click(object sender, EventArgs e) {
        int index = 1;
        string gamePath = Path.Combine(Directory.GetCurrentDirectory(), "TAS Files");
        if (!Directory.Exists(gamePath)) {
            Directory.CreateDirectory(gamePath);
        }

        string initText = $"RecordCount: 1{Environment.NewLine}";
        if (StudioCommunicationBase.Initialized && Process.GetProcessesByName("Celeste").Length > 0) {
            if (GetDataFromGame(GameDataType.ConsoleCommand, true) is { } simpleConsoleCommand) {
                initText += $"{Environment.NewLine}{simpleConsoleCommand}{Environment.NewLine}   1{Environment.NewLine}";
                if (GetDataFromGame(GameDataType.ModUrl) is { } modUrl) {
                    initText = modUrl + initText;
                }
            }
        }

        initText += $"{Environment.NewLine}#Start{Environment.NewLine}";

        string fileName = Path.Combine(gamePath, $"Untitled-{index}.tas");
        while (File.Exists(fileName) && File.ReadAllText(fileName) != initText) {
            index++;
            fileName = Path.Combine(gamePath, $"Untitled-{index}.tas");
        }

        File.WriteAllText(fileName, initText);

        OpenFile(fileName);
    }

    private void toggleHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowHitboxes", null, sender);
    }

    private void toggleTriggerHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowTriggerHitboxes", null, sender);
    }

    private void unloadedRoomsHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowUnloadedRoomsHitboxes", null, sender);
    }

    private void cameraHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowCameraHitboxes", null, sender);
    }

    private void toggleSimplifiedHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("SimplifiedHitboxes", null, sender);
    }

    private void switchActualCollideHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowActualCollideHitboxes", null, sender);
    }

    private void toggleSimplifiedGraphicsToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("SimplifiedGraphics", null, sender);
    }

    private void toggleGameplayToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("ShowGameplay", null, sender);
    }

    private void toggleCenterCameraToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("CenterCamera", null, sender);
    }

    private void switchInfoHUDToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoHud", null, sender);
    }

    private void tASInputInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoTasInput", null, sender);
    }

    private void gameInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoGame", null, sender);
    }

    private void watchEntityInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoWatchEntity", null, sender);
    }

    private void customInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoCustom", null, sender);
    }

    private void subpixelIndicatorToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("InfoSubpixelIndicator", null, sender);
    }

    private void SetDecimals(string settingName, object sender, bool floatNumber = false) {
        if (StudioCommunicationServer.Instance == null) {
            return;
        }

        string settingNameValid = settingName.Replace(" ", "");

        string decimals = "2";
        if (GetDataFromGame(GameDataType.SettingValue, settingNameValid) is { } settingValue) {
            decimals = settingValue;
        }

        if (!DialogUtils.ShowInputDialog(settingName, ref decimals)) {
            return;
        }

        if (!floatNumber && int.TryParse(decimals, out int d)) {
            ToggleGameSetting(settingNameValid, d, sender, false);
        } else if (floatNumber && float.TryParse(decimals, out float f)) {
            ToggleGameSetting(settingNameValid, f, sender, false);
        }
    }

    private void positionDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Position Decimals", sender);
    }

    private void speedDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Speed Decimals", sender);
    }

    private void velocityDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Velocity Decimals", sender);
    }

    private void angleDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Angle Decimals", sender);
    }

    private void customInfoDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Custom Info Decimals", sender);
    }

    private void subpixelIndicatorDecimalsToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Subpixel Indicator Decimals", sender);
    }

    private void unitOfSpeedToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("SpeedUnit", null, sender);
    }

    private void fastForwardSpeedToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Fast Forward Speed", sender);
    }

    private void slowForwardSpeedToolStripMenuItem_Click(object sender, EventArgs e) {
        SetDecimals("Slow Forward Speed", sender, true);
    }

    private void copyCustomInfoTemplateToClipboardToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("Copy Custom Info Template to Clipboard", null, sender);
    }

    private void setCustomInfoTemplateFromClipboardToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("Set Custom Info Template From Clipboard", null, sender);
    }

    private void clearCustomInfoTemplateToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("Clear Custom Info Template", null, sender);
    }

    private void clearWatchEntityInfoToolStripMenuItem_Click(object sender, EventArgs e) {
        ToggleGameSetting("Clear Watch Entity Info", null, sender);
    }

    private void enabledAutoBackupToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.AutoBackupEnabled = !Settings.Instance.AutoBackupEnabled;
        SaveSettings();
    }

    private void backupRateToolStripMenuItem_Click(object sender, EventArgs e) {
        string origRate = Settings.Instance.AutoBackupRate.ToString();
        if (!DialogUtils.ShowInputDialog("Backup Rate (minutes)", ref origRate)) {
            return;
        }

        if (string.IsNullOrEmpty(origRate)) {
            Settings.Instance.AutoBackupRate = 0;
        } else if (int.TryParse(origRate, out int count)) {
            Settings.Instance.AutoBackupRate = Math.Max(0, count);
        }

        backupRateToolStripMenuItem.Text = $"Backup Rate (minutes): {Settings.Instance.AutoBackupRate}";
    }

    private void backupFileCountsToolStripMenuItem_Click(object sender, EventArgs e) {
        string origCount = Settings.Instance.AutoBackupCount.ToString();
        if (!DialogUtils.ShowInputDialog("Backup File Count", ref origCount)) {
            return;
        }

        if (string.IsNullOrEmpty(origCount)) {
            Settings.Instance.AutoBackupCount = 0;
        } else if (int.TryParse(origCount, out int count)) {
            Settings.Instance.AutoBackupCount = Math.Max(0, count);
        }

        backupFileCountsToolStripMenuItem.Text = $"Backup File Count: {Settings.Instance.AutoBackupCount}";
    }

    public void SetControlsColor(Themes themes) {
        Color foreColor = ColorUtils.HexToColor(themes.Status);
        Color backColor = ColorUtils.HexToColor(themes.Status, 1);
        Color dividerColor = ColorUtils.HexToColor(themes.ServiceLine, 0);

        BackColor = ColorUtils.HexToColor(themes.Background);

        lblStatus.ForeColor = foreColor;
        lblStatus.BackColor = backColor;

        statusPanel.Controls[0].ForeColor = foreColor;
        statusPanel.Controls[0].BackColor = backColor;

        menuStrip.ForeColor = foreColor;
        menuStrip.BackColor = backColor;
        menuStrip.Renderer = new ThemesRenderer(themes);

        dividerLabel.BackColor = dividerColor;
    }

    private void lightToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.ThemesType = ThemesType.Light;
        Themes.ResetThemes();
        SaveSettings();
    }

    private void darkToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.ThemesType = ThemesType.Dark;
        Themes.ResetThemes();
        SaveSettings();
    }

    private void customToolStripMenuItem_Click(object sender, EventArgs e) {
        Settings.Instance.ThemesType = ThemesType.Custom;
        Themes.ResetThemes();
        SaveSettings();
    }

    private void themesToolStripMenuItem_DropDownOpened(object sender, EventArgs e) {
        lightToolStripMenuItem.Checked = Settings.Instance.ThemesType == ThemesType.Light;
        darkToolStripMenuItem.Checked = Settings.Instance.ThemesType == ThemesType.Dark;
        customToolStripMenuItem.Checked = Settings.Instance.ThemesType == ThemesType.Custom;
    }

    public void UseImmersiveDarkMode(bool enabled) {
        Win32Api.UseImmersiveDarkMode(base.Handle, enabled);

        ClientSize = new Size(ClientSize.Width + 1, ClientSize.Height);
        ClientSize = new Size(ClientSize.Width - 1, ClientSize.Height);
    }
}