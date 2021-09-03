using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CelesteStudio.Communication;
using CelesteStudio.Entities;
using CelesteStudio.Properties;
using CelesteStudio.RichText;
using StudioCommunication;

namespace CelesteStudio {
    public partial class Studio : Form {
        private const string MaxStatusHeight20Line = "\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n";

        public static Studio Instance;

        private readonly List<InputRecord> lines = new();

        private DateTime lastChanged = DateTime.MinValue;
        private FormWindowState lastWindowState = FormWindowState.Normal;
        private State tasState;
        private ToolTip tooltip;
        private int totalFrames, currentFrame;
        private bool updating;

        public Studio(string[] args) {
            Instance = this;

            UpgradeSettings();
            InitializeComponent();
            InitMenu();
            InitDragDrop();
            InitFont(Settings.Default.Font ?? fontDialog.Font);

            Text = TitleBarText;

            lines.Add(new InputRecord(""));
            EnableStudio(false);

            DesktopLocation = Settings.Default.DesktopLocation;
            Size = Settings.Default.Size;

            if (!IsTitleBarVisible()) {
                DesktopLocation = new Point(0, 0);
            }

            TryOpenFile(args);
        }

        private bool DisableTyping => tasState.HasFlag(State.Enable) && !tasState.HasFlag(State.FrameStep);

        private string TitleBarText =>
            (string.IsNullOrEmpty(CurrentFileName) ? "Celeste.tas" : Path.GetFileName(CurrentFileName))
            + " - Studio v"
            + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private string CurrentFileName {
            get => richText.CurrentFileName;
            set => richText.CurrentFileName = value;
        }

        private static StringCollection RecentFiles => Settings.Default.RecentFiles ??= new StringCollection();

        private void UpgradeSettings() {
            if (string.IsNullOrEmpty(Settings.Default.UpgradeVersion) ||
                new Version(Settings.Default.UpgradeVersion) < Assembly.GetExecutingAssembly().GetName().Version) {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
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
                    RecentFiles.Clear();
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
                    Directory.Delete(backupFolder, true);
                    return;
                } else if (clickedItem.Text == "Open Backup Folder") {
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
                for (int i = files.Count - 1; i >= 20; i--) {
                    files.Remove(files[i]);
                }

                foreach (string filePath in files) {
                    openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem(Path.GetFileName(filePath)) {
                        Checked = CurrentFileName == filePath
                    });
                }

                openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
                openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Delete All Files"));
                openBackupToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Open Backup Folder"));
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
            Settings.Default.DesktopLocation = DesktopLocation;
            Settings.Default.Size = Size;
            Settings.Default.Font = fontDialog.Font;
            Settings.Default.Save();
        }

        private void ShowTooltip(string text) {
            tooltip?.Hide(this);
            tooltip = new ToolTip();
            Size textSize = TextRenderer.MeasureText(text, Font);
            tooltip.Show(text, this, Width / 2 - textSize.Width / 2, Height / 2 - textSize.Height / 2, 3000);
        }

        private void TASStudio_FormClosed(object sender, FormClosedEventArgs e) {
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
            try {
                if (e.Modifiers == (Keys.Shift | Keys.Control) && e.KeyCode == Keys.S) {
                    SaveAsFile();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.S) {
                    richText.SaveFile();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.O) {
                    OpenFile();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.K) {
                    CommentText();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.P) {
                    ClearUncommentedBreakpoints();
                } else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.P) {
                    ClearBreakpoints();
                } else if (e.Modifiers == (Keys.Control | Keys.Alt) && e.KeyCode == Keys.P) {
                    CommentUncommentAllBreakpoints();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.OemPeriod) {
                    InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***");
                } else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.OemPeriod) {
                    InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***S");
                } else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.R) {
                    InsertConsoleLoadCommand();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.R) {
                    InsertRoomName();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.F) {
                    ShowFindDialog();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.T) {
                    InsertTime();
                } else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.C) {
                    CopyGameInfo();
                } else if (e.Modifiers == (Keys.Shift | Keys.Control) && e.KeyCode == Keys.D) {
                    StudioCommunicationServer.Instance?.ExternalReset();
                } else if (e.KeyCode == Keys.Down && (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift))) {
                    GoDownCommentAndBreakpoint(e);
                } else if (e.KeyCode == Keys.Up && (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift))) {
                    GoUpCommentAndBreakpoint(e);
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

        private void ToggleUpdatingHotkeys() {
            Settings.Default.UpdatingHotkeys = !Settings.Default.UpdatingHotkeys;
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

        private void OpenFile(string fileName = null, int startLine = 0) {
            if (fileName == CurrentFileName && fileName != null) {
                return;
            }

            StudioCommunicationServer.Instance?.WriteWait();
            if (richText.OpenFile(fileName)) {
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
            string lineText = richText.Lines[richText.Selection.Start.iLine].Trim();
            if (lineText.StartsWith("read", StringComparison.InvariantCultureIgnoreCase)) {
                Regex spaceRegex = new(@"^[^,]+?\s+[^,]");

                string[] args = spaceRegex.IsMatch(lineText) ? lineText.Split() : lineText.Split(',');
                args = args.Select(text => text.Trim()).ToArray();
                if (args[0].Equals("read", StringComparison.InvariantCultureIgnoreCase) && args.Length >= 2) {
                    string filePath = args[1];
                    string fileDirectory = Path.GetDirectoryName(CurrentFileName);
                    // Check for full and shortened Read versions
                    if (fileDirectory != null) {
                        // Path.Combine can handle the case when filePath is an absolute path
                        string absoluteOrRelativePath = Path.Combine(fileDirectory, filePath);
                        if (File.Exists(absoluteOrRelativePath) && absoluteOrRelativePath != CurrentFileName) {
                            filePath = absoluteOrRelativePath;
                        } else {
                            string[] files = Directory.GetFiles(fileDirectory, $"{filePath}*.tas");
                            if (files.FirstOrDefault(path => path != CurrentFileName) is { } shortenedFilePath) {
                                filePath = shortenedFilePath;
                            }
                        }
                    }

                    if (!File.Exists(filePath)) {
                        return;
                    }

                    int startLine = 0;
                    if (args.Length >= 3) {
                        startLine = GetLine(filePath, args[2]);
                    }

                    OpenFile(filePath, startLine);
                }
            }
        }
        
        private void TryGoToPlayLine() {
            string lineText = richText.Lines[richText.Selection.Start.iLine].Trim();
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
                return lineNumber;
            }

            int curLine = 0;
            using StreamReader sr = new(path);
            while (!sr.EndOfStream) {
                curLine++;
                string line = sr.ReadLine()?.TrimEnd();
                if (line == "#" + labelOrLineNumber) {
                    return curLine - 1;
                }
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
            Settings.Default.LastFileName = CurrentFileName;
            SaveSettings();
        }

        private void ClearUncommentedBreakpoints() {
            var line = Math.Min(richText.Selection.Start.iLine, richText.Selection.End.iLine);
            List<int> breakpoints = richText.FindLines(@"^\s*\*\*\*");
            richText.RemoveLines(breakpoints);
            richText.Selection.Start = new Place(0, Math.Min(line, richText.LinesCount - 1));
        }

        private void ClearBreakpoints() {
            var line = Math.Min(richText.Selection.Start.iLine, richText.Selection.End.iLine);
            List<int> breakpoints = richText.FindLines(@"^\s*#*\s*\*\*\*");
            richText.RemoveLines(breakpoints);
            richText.Selection.Start = new Place(0, Math.Min(line, richText.LinesCount - 1));
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

        private void InsertConsoleLoadCommand() {
            CommunicationWrapper.ReturnData = null;
            StudioCommunicationServer.Instance.GetConsoleCommand();
            Thread.Sleep(100);

            if (CommunicationWrapper.ReturnData == null) {
                return;
            }

            InsertNewLine(CommunicationWrapper.ReturnData);
        }

        private void InsertModInfo() {
            CommunicationWrapper.ReturnData = null;
            StudioCommunicationServer.Instance.GetModInfo();
            Thread.Sleep(100);

            if (CommunicationWrapper.ReturnData == null) {
                return;
            }

            InsertNewLine(CommunicationWrapper.ReturnData);
        }

        private void InsertNewLine(string text) {
            text = text.Trim();
            int startLine = richText.Selection.Start.iLine;
            richText.Selection = new Range(richText, 0, startLine, 0, startLine);
            richText.InsertText(text + "\n");
            richText.Selection = new Range(richText, text.Length, startLine, text.Length, startLine);
        }

        private void CopyGameInfo() {
            if (string.IsNullOrEmpty(CommunicationWrapper.StudioInfo?.ExactGameInfo)) {
                return;
            }

            Clipboard.SetText(CommunicationWrapper.StudioInfo.ExactGameInfo);
        }

        private DialogResult ShowInputDialog(string title, ref string input) {
            Size size = new(200, 70);
            DialogResult result = DialogResult.Cancel;

            using Form inputBox = new();
            inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
            inputBox.ClientSize = size;
            inputBox.Text = title;
            inputBox.StartPosition = FormStartPosition.CenterParent;
            inputBox.MinimizeBox = false;
            inputBox.MaximizeBox = false;

            TextBox textBox = new();
            textBox.Size = new Size(size.Width - 10, 20);
            textBox.Location = new Point(5, 10);
            textBox.Font = new Font(FontFamily.GenericSansSerif, 11);
            textBox.Text = input;
            inputBox.Controls.Add(textBox);

            Button okButton = new();
            okButton.DialogResult = DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new Size(75, 23);
            okButton.Text = "&OK";
            okButton.Location = new Point(size.Width - 80 - 80, 39);
            inputBox.Controls.Add(okButton);

            Button cancelButton = new();
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(75, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new Point(size.Width - 80, 39);
            inputBox.Controls.Add(cancelButton);

            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton;

            result = inputBox.ShowDialog(this);
            input = textBox.Text;

            return result;
        }

        private void ShowFindDialog() {
            const int padding = 10;
            const int buttonWidth = 75;
            const int buttonHeight = 25;

            Size size = new(300, buttonHeight * 2 + padding * 3);
            bool pressEnter = false;

            using Form inputBox = new();
            inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
            inputBox.ClientSize = size;
            inputBox.Text = "Find";
            inputBox.StartPosition = FormStartPosition.CenterParent;
            inputBox.MinimizeBox = false;
            inputBox.MaximizeBox = false;
            inputBox.KeyPreview = true;
            inputBox.KeyDown += (sender, args) => {
                if (args.KeyCode == Keys.Escape) {
                    inputBox.Close();
                } else {
                    pressEnter = args.KeyCode == Keys.Enter;
                }
            };

            TextBox textBox = new();
            textBox.Size = new Size(size.Width - 3 * padding - buttonWidth, buttonHeight);
            textBox.Location = new Point(padding, padding);
            textBox.Font = new Font(FontFamily.GenericSansSerif, 11);
            textBox.ForeColor = Color.FromArgb(50, 50, 50);
            textBox.Text = richText.SelectedText;
            textBox.SelectAll();
            textBox.KeyDown += (sender, args) => pressEnter = args.KeyCode == Keys.Enter;
            textBox.KeyPress += (sender, args) => {
                if (pressEnter) {
                    args.Handled = true;
                    QuickFind(textBox.Text, true);
                }
            };
            inputBox.KeyPress += (sender, args) => {
                if (pressEnter) {
                    args.Handled = true;
                    QuickFind(textBox.Text, true);
                }
            };
            inputBox.Controls.Add(textBox);

            Button nextButton = new();
            nextButton.Name = "nextButton";
            nextButton.Size = new Size(buttonWidth, buttonHeight);
            nextButton.Text = "&Next";
            nextButton.Location = new Point(textBox.Right + padding, textBox.Top);
            nextButton.Click += (sender, args) => QuickFind(textBox.Text, true);
            inputBox.Controls.Add(nextButton);

            Button previousButton = new();
            previousButton.Name = "previouButton";
            previousButton.Size = new Size(buttonWidth, buttonHeight);
            previousButton.Text = "&Previous";
            previousButton.Location = new Point(nextButton.Left, nextButton.Bottom + padding);
            previousButton.Click += (sender, args) => QuickFind(textBox.Text, false);
            inputBox.Controls.Add(previousButton);

            CheckBox caseCheckbox = new();
            caseCheckbox.Size = new Size(textBox.Width, buttonHeight);
            caseCheckbox.Text = "&Match case";
            caseCheckbox.Location = new Point(textBox.Left, textBox.Bottom + padding);
            caseCheckbox.Checked = Settings.Default.FindMatchCase;
            caseCheckbox.CheckedChanged += (sender, args) => Settings.Default.FindMatchCase = caseCheckbox.Checked;
            inputBox.Controls.Add(caseCheckbox);

            inputBox.ShowDialog(this);
        }

        private void QuickFind(string text, bool next, bool fromStart = false) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            Range selection = richText.Selection;
            selection.Normalize();

            int startLine;
            int? startIndex;
            if (next) {
                startLine = selection.Start.iLine;
                startIndex = selection.Start.iChar + selection.Text.Length;
            } else {
                startLine = selection.End.iLine;
                startIndex = selection.End.iChar - selection.Text.Length;
            }

            if (fromStart) {
                startLine = next ? 0 : richText.LinesCount - 1;
                startIndex = next ? 0 : richText.Lines.LastOrDefault()?.Length ?? 0;
            }

            int? resultLine = null;
            int? resultIndex = null;
            StringComparison comparison =
                Settings.Default.FindMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase;
            if (next) {
                for (int i = startLine; i < richText.LinesCount; i++) {
                    startIndex ??= 0;
                    string lineText = richText[i].Text;
                    int textLength = lineText.Length;
                    if (textLength >= startIndex.Value &&
                        lineText.Substring(startIndex.Value, lineText.Length - startIndex.Value)
                            .IndexOf(text, comparison) is var findIndex and >= 0) {
                        resultLine = i;
                        resultIndex = findIndex + startIndex;
                        break;
                    } else {
                        startIndex = null;
                    }
                }
            } else {
                for (int i = startLine; i >= 0; i--) {
                    startIndex ??= richText[i].Text.Length;
                    if (startIndex.Value >= 0 &&
                        richText[i].Text.Substring(0, startIndex.Value).LastIndexOf(text, StringComparison.InvariantCultureIgnoreCase) is var
                            findIndex and >= 0) {
                        resultLine = i;
                        resultIndex = findIndex;
                        break;
                    } else {
                        startIndex = null;
                    }
                }
            }

            if (resultLine is { } line && resultIndex is { } index) {
                richText.Selection = new Range(richText, index, line, index + text.Length, line);
                richText.DoSelectionVisible();
            } else {
                if (fromStart) {
                    MessageBox.Show($"Can't find the text \"{text}\"", "Celeste Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } else {
                    QuickFind(text, next, true);
                }
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
                        richText.Invalidate();
                        if (CommunicationWrapper.FastForwarding) {
                            CommunicationWrapper.CheckFastForward();
                        }
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

                if (File.Exists(Settings.Default.LastFileName)
                    && IsFileReadable(Settings.Default.LastFileName)
                    && string.IsNullOrEmpty(CurrentFileName)) {
                    CurrentFileName = Settings.Default.LastFileName;
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
                    StudioInfo studioInfo = CommunicationWrapper.StudioInfo;
                    if (richText.CurrentLine != studioInfo.CurrentLine) {
                        richText.CurrentLine = studioInfo.CurrentLine;
                    }

                    richText.CurrentLineText = studioInfo.CurrentLineText;
                    currentFrame = studioInfo.CurrentFrame;
                    totalFrames = studioInfo.TotalFrames;
                    richText.SaveStateLine = studioInfo.SaveStateLine;
                    tasState = studioInfo.TasState;
                } else {
                    currentFrame = 0;
                    if (richText.CurrentLine >= 0) {
                        richText.CurrentLine = -1;
                    }

                    richText.SaveStateLine = -1;
                    tasState = State.None;
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
            int count = e.Count;
            while (count-- > 0) {
                InputRecord input = lines[e.Index];
                totalFrames -= input.Frames;
                lines.RemoveAt(e.Index);
            }

            UpdateStatusBar();
        }

        private void tasText_LineInserted(object sender, LineInsertedEventArgs e) {
            RichText.RichText tas = (RichText.RichText) sender;
            int count = e.Count;
            while (count-- > 0) {
                InputRecord input = new(tas.GetLineText(e.Index + count));
                lines.Insert(e.Index, input);
                totalFrames += input.Frames;
            }

            UpdateStatusBar();
        }

        private void UpdateStatusBar() {
            if (StudioCommunicationBase.Initialized) {
                string gameInfo = CommunicationWrapper.StudioInfo?.GameInfo ?? string.Empty;
                lblStatus.Text = "(" + (currentFrame > 0 ? currentFrame + "/" : "")
                                     + totalFrames + ") \n" + gameInfo
                                     + new string('\n', Math.Max(0, 7 - gameInfo.Split('\n').Length));
            } else {
                lblStatus.Text = "(" + totalFrames + ")\r\nSearching...";
            }

            int bottomExtraSpace = TextRenderer.MeasureText("\n", lblStatus.Font).Height / 5;
            if (Settings.Default.ShowGameInfo) {
                int maxHeight = TextRenderer.MeasureText(MaxStatusHeight20Line, lblStatus.Font).Height + bottomExtraSpace;
                int statusBarHeight = TextRenderer.MeasureText(lblStatus.Text.Trim(), lblStatus.Font).Height + bottomExtraSpace;
                statusPanel.Height = Math.Min(maxHeight, statusBarHeight);
                statusPanel.AutoScrollMinSize = new Size(0, statusBarHeight);
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

        private void CommentText() {
            Range range = richText.Selection.Clone();

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end) {
                int temp = start;
                start = end;
                end = temp;
            }

            richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
            string text = richText.SelectedText;

            int i = 0;
            bool startLine = true;
            StringBuilder sb = new(text.Length + end - start);
            while (i < text.Length) {
                char c = text[i++];
                if (startLine) {
                    if (c != '#') {
                        if (c != '\r') {
                            sb.Append('#');
                        }

                        sb.Append(c);
                    }

                    startLine = false;
                } else if (c == '\n') {
                    sb.AppendLine();
                    startLine = true;
                } else if (c != '\r') {
                    sb.Append(c);
                }
            }

            richText.SelectedText = sb.ToString();
            if (range.IsEmpty) {
                if (start < richText.LinesCount - 1) {
                    start++;
                }

                richText.Selection = new Range(richText, 0, start, 0, start);
            } else {
                richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
            }

            richText.ScrollLeft();
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
                InputRecord old = lines.Count > start ? lines[start] : null;
                string text = tas[start++].Text;
                InputRecord input = new(text);
                if (old != null) {
                    totalFrames -= old.Frames;

                    string line = input.ToString();

                    bool featherAngle = old.HasActions(Actions.Feather)
                                        && !string.IsNullOrEmpty(input.AngleStr)
                                        && string.IsNullOrEmpty(input.UpperLimitStr)
                                        && text[text.Length - 1] == ','
                                        && text.Substring(0, text.Length - 1) == line;
                    if (text != line && !featherAngle) {
                        if (old.Frames == 0 && input.Frames == 0 && old.ZeroPadding == input.ZeroPadding && old.Equals(input) &&
                            line.Length >= text.Length) {
                            line = string.Empty;
                        }

                        Range oldRange = tas.Selection;
                        if (!string.IsNullOrEmpty(line)) {
                            InputRecord.ProcessExclusiveActions(old, input);
                            line = input.ToString();

                            int index = oldRange.Start.iChar + line.Length - text.Length;
                            if (index < 0) {
                                index = 0;
                            }

                            if (index > 4) {
                                index = 4;
                            }

                            if (old.Frames == input.Frames && old.ZeroPadding == input.ZeroPadding) {
                                index = input.HasActions(Actions.Feather) ? line.Length : 4;
                            }

                            place = new Place(index, start - 1);
                        }

                        modified = true;
                    } else {
                        place = new Place(4, start - 1);
                    }

                    text = line;
                    lines[start - 1] = input;
                } else {
                    place = new Place(text.Length, start - 1);
                }

                if (start <= end) {
                    sb.AppendLine(text);
                } else {
                    sb.Append(text);
                }

                totalFrames += input.Frames;
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
            lines.Clear();
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
            Settings.Default.AutoRemoveMutuallyExclusiveActions = !Settings.Default.AutoRemoveMutuallyExclusiveActions;
        }

        private void homeMenuItem_Click(object sender, EventArgs e) {
            Process.Start("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        }

        private void settingsToolStripMenuItem_Opened(object sender, EventArgs e) {
            settingsToolStripMenuItem.DropDown.Opacity = 1f;
            sendInputsToCelesteMenuItem.Checked = Settings.Default.UpdatingHotkeys;
            autoRemoveExclusiveActionsToolStripMenuItem.Checked = Settings.Default.AutoRemoveMutuallyExclusiveActions;
            showGameInfoToolStripMenuItem.Checked = Settings.Default.ShowGameInfo;
            enabledAutoBackupToolStripMenuItem.Checked = Settings.Default.AutoBackupEnabled;
            backupRateToolStripMenuItem.Text = $"Backup Rate (minutes): {Settings.Default.AutoBackupRate}";
            backupFileCountsToolStripMenuItem.Text = $"Backup File Count: {Settings.Default.AutoBackupCount}";
        }

        private void openPreviousFileToolStripMenuItem_Click(object sender, EventArgs e) {
            if (RecentFiles.Count <= 1) {
                return;
            }

            string fileName = RecentFiles[1];

            if (!File.Exists(fileName)) {
                RecentFiles.Remove(fileName);
            }

            OpenFile(fileName);
        }

        private void sendInputsToCelesteMenuItem_Click(object sender, EventArgs e) {
            ToggleUpdatingHotkeys();
            if (settingsToolStripMenuItem.DropDown.Opacity == 0f) {
                ShowTooltip((Settings.Default.UpdatingHotkeys ? "Enable" : "Disable") + " Send Inputs to Celeste");
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
            InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***");
        }

        private void insertRemoveSavestateBreakPointToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***S");
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e) {
            SaveAsFile();
        }

        private void commentUncommentTextToolStripMenuItem_Click(object sender, EventArgs e) {
            CommentText();
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
            InsertConsoleLoadCommand();
        }

        private void enforceLegalToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("EnforceLegal");
        }

        private void unsafeToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("Unsafe");
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

        private void analogueModeToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("AnalogMode, Ignore/Circle/Square/Precise");
        }

        private void startExportToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("StartExportGameInfo (Path) (Entities)");
        }

        private void finishExportToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("FinishExportGameInfo");
        }

        private void startExportRoomInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("StartExportRoomInfo dump_room_info.txt");
        }

        private void finishExportRoomInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("FinishExportRoomInfo");
        }

        private void addToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("Add, (input line)");
        }

        private void skipToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("Skip");
        }

        private void startExportLibTASToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("StartExportLibTAS (Path)");
        }

        private void finishExportLibTASToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("FinishExportLibTAS");
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

        private void copyGamerDataMenuItem_Click(object sender, EventArgs e) {
            CopyGameInfo();
        }

        private void fontToolStripMenuItem_Click(object sender, EventArgs e) {
            if (fontDialog.ShowDialog() != DialogResult.Cancel) {
                InitFont(fontDialog.Font);
            }
        }

        private void reconnectStudioAndCelesteToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ExternalReset();
        }

        private void insertModInfoStripMenuItem1_Click(object sender, EventArgs e) {
            InsertModInfo();
        }

        private void SwapActionKeys(char key1, char key2) {
            if (richText.Selection.IsEmpty) {
                return;
            }

            Range range = richText.Selection.Clone();

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end) {
                int temp = start;
                start = end;
                end = temp;
            }

            richText.Selection = new Range(richText, 0, start, richText[end].Count, end);
            string text = richText.SelectedText;

            StringBuilder sb = new();
            Regex swapKeyRegex = new($"{key1}|{key2}");
            foreach (string lineText in text.Split('\n')) {
                if (SyntaxHighlighter.InputRecordRegex.IsMatch(lineText)) {
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

        private void openReadFileToolStripMenuItem_Click(object sender, EventArgs e) {
            TryOpenReadFile();
            TryGoToPlayLine();
        }

        private void showGameInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.ShowGameInfo = !Settings.Default.ShowGameInfo;
            SaveSettings();
            if (Settings.Default.ShowGameInfo) {
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

            string fileName = Path.Combine(gamePath, $"Untitled-{index}.tas");
            while (File.Exists(fileName) && new FileInfo(fileName).Length != 14) {
                index++;
                fileName = Path.Combine(gamePath, $"Untitled-{index}.tas");
            }

            File.WriteAllText(fileName, "RecordCount: 1");

            OpenFile(fileName);
        }

        private void toggleHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("ShowHitboxes");
        }

        private void toggleTriggerHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("HideTriggerHitboxes");
        }

        private void toggleSimplifiedHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("SimplifiedHitboxes");
        }

        private void switchActualCollideHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("ShowActualCollideHitboxes");
        }

        private void toggleSimplifiedGraphicsToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("SimplifiedGraphics");
        }

        private void toggleGameplayToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("HideGameplay");
        }

        private void toggleCenterCameraToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("CenterCamera");
        }

        private void switchInfoHUDToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoHud");
        }

        private void tASInputInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoTasInput");
        }

        private void gameInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoGame");
        }

        private void watchEntityInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoWatchEntity");
        }

        private void customInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoCustom");
        }

        private void subpixelIndicatorToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("InfoSubPixelIndicator");
        }

        private void toggleRoundPositionToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("RoundPosition");
        }

        private void toggleRoundSpeedToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("RoundSpeed");
        }

        private void roundVelocityToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("RoundVelocity");
        }

        private void roundCustomInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("RoundCustomInfo");
        }

        private void copyCustomInfoTemplateToClipboardToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("Copy Custom Info Template to Clipboard");
        }

        private void setCustomInfoTemplateFromClipboardToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.Instance?.ToggleGameSetting("Set Custom Info Template From Clipboard");
        }

        private void enabledAutoBackupToolStripMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.AutoBackupEnabled = !Settings.Default.AutoBackupEnabled;
            SaveSettings();
        }

        private void backupRateToolStripMenuItem_Click(object sender, EventArgs e) {
            string origRate = Settings.Default.AutoBackupRate.ToString();
            if (ShowInputDialog("Backup Rate (minutes)", ref origRate) != DialogResult.OK) {
                return;
            }

            if (string.IsNullOrEmpty(origRate)) {
                Settings.Default.AutoBackupRate = 0;
            } else if (int.TryParse(origRate, out int count)) {
                Settings.Default.AutoBackupRate = Math.Max(0, count);
            }

            backupRateToolStripMenuItem.Text = $"Backup Rate (minutes): {Settings.Default.AutoBackupRate}";
        }

        private void backupFileCountsToolStripMenuItem_Click(object sender, EventArgs e) {
            string origCount = Settings.Default.AutoBackupCount.ToString();
            if (ShowInputDialog("Backup File Count", ref origCount) != DialogResult.OK) {
                return;
            }

            if (string.IsNullOrEmpty(origCount)) {
                Settings.Default.AutoBackupCount = 0;
            } else if (int.TryParse(origCount, out int count)) {
                Settings.Default.AutoBackupCount = Math.Max(0, count);
            }

            backupFileCountsToolStripMenuItem.Text = $"Backup File Count: {Settings.Default.AutoBackupCount}";
        }
    }
}