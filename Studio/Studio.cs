using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
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

        //private GameMemory memory = new GameMemory();
        private DateTime lastChanged = DateTime.MinValue;
        private FormWindowState lastWindowState = FormWindowState.Normal;
        private int totalFrames, currentFrame;

        private bool updating;

        public Studio() {
            InitializeComponent();
            InitMenu();
            InitDragDrop();
            InitFont(Settings.Default.Font ?? fontDialog.Font);

            Text = TitleBarText;
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

            lines.Add(new InputRecord(""));
            EnableStudio(false);

            DesktopLocation = Settings.Default.DesktopLocation;
            Size = Settings.Default.Size;

            if (!IsTitleBarVisible()) {
                DesktopLocation = new Point(0, 0);
            }

            Instance = this;
        }

        private string TitleBarText =>
            (string.IsNullOrEmpty(LastFileName) ? "Celeste.tas" : Path.GetFileName(LastFileName))
            + " - Studio v"
            + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private string defaultFileName {
            get {
                string fileName = "";
                if (Environment.OSVersion.Platform == PlatformID.Unix) {
                    if (null == (fileName = Environment.GetEnvironmentVariable("CELESTE_TAS_FILE"))) {
                        fileName = Environment.GetEnvironmentVariable("HOME") + "/.steam/steam/steamapps/common/Celeste/Celeste.tas";
                    }
                } else if (CommunicationWrapper.gamePath != null) {
                    fileName = Path.Combine(CommunicationWrapper.gamePath, "Celeste.tas");
                }

                return fileName;
            }
        }

        private string LastFileName {
            get => tasText.LastFileName;
            set => tasText.LastFileName = value;
        }

        private StringCollection RecentFiles => Settings.Default.RecentFiles ??= new StringCollection();

        [STAThread]
        public static void Main() {
            RunSingleton(() => {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Studio());
            });
        }

        private static void RunSingleton(Action action) {
            string appGuid =
                ((GuidAttribute) Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(GuidAttribute), false).GetValue(0)).Value;

            string mutexId = $"Global\\{{{appGuid}}}";

            var allowEveryoneRule =
                new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid
                        , null)
                    , MutexRights.FullControl
                    , AccessControlType.Allow
                );
            var securitySettings = new MutexSecurity();
            securitySettings.AddAccessRule(allowEveryoneRule);

            using (var mutex = new Mutex(false, mutexId, out _, securitySettings)) {
                var hasHandle = false;
                try {
                    try {
                        hasHandle = mutex.WaitOne(TimeSpan.Zero, false);
                        if (hasHandle == false) {
                            MessageBox.Show("Studio already running", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    } catch (AbandonedMutexException) {
                        hasHandle = true;
                    }

                    // Perform your work here.
                    action();
                } finally {
                    if (hasHandle) {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private void InitMenu() {
            tasText.MouseClick += (sender, args) => {
                if ((args.Button & MouseButtons.Right) == 0) {
                    return;
                }

                if (tasText.Selection.IsEmpty) {
                    tasText.Selection.Start = tasText.PointToPlace(args.Location);
                    tasText.Invalidate();
                }

                tasTextContextMenuStrip.Show(Cursor.Position);
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
        }

        private void InitDragDrop() {
            tasText.DragDrop += (sender, args) => {
                string[] fileList = (string[]) args.Data.GetData(DataFormats.FileDrop, false);
                if (fileList.Length > 0 && fileList[0].EndsWith(".tas")) {
                    OpenFile(fileList[0]);
                }
            };
            tasText.DragEnter += (sender, args) => {
                string[] fileList = (string[]) args.Data.GetData(DataFormats.FileDrop, false);
                if (fileList.Length > 0 && fileList[0].EndsWith(".tas")) {
                    args.Effect = DragDropEffects.Copy;
                }
            };
        }

        private void InitFont(Font font) {
            tasText.Font = font;
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
                        Checked = LastFileName == fileName
                    });
                }

                openRecentMenuItem.DropDownItems.Add(new ToolStripSeparator());

                openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem("Clear"));
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

        private void TASStudio_FormClosed(object sender, FormClosedEventArgs e) {
            Settings.Default.DesktopLocation = DesktopLocation;
            Settings.Default.Size = Size;
            Settings.Default.Font = fontDialog.Font;
            Settings.Default.Save();
            StudioCommunicationServer.instance?.SendPath(string.Empty);
            Thread.Sleep(50);
        }

        private void Studio_Shown(object sender, EventArgs e) {
            Thread updateThread = new(UpdateLoop);
            updateThread.IsBackground = true;
            updateThread.Start();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            if ((msg.Msg == 0x100) || (msg.Msg == 0x104)) {
                if (!tasText.IsChanged && CommunicationWrapper.CheckControls(ref msg)) {
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
                    tasText.SaveFile();
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
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.T) {
                    InsertTime();
                } else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.C) {
                    CopyGameData();
                } else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.D) {
                    ToggleUpdatingHotkeys();
                } else if (e.Modifiers == (Keys.Shift | Keys.Control) && e.KeyCode == Keys.D) {
                    StudioCommunicationServer.instance?.ExternalReset();
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
            StudioCommunicationServer.instance?.WriteWait();
            tasText.SaveNewFile();
            StudioCommunicationServer.instance?.SendPath(LastFileName);
            Text = TitleBarText;
            UpdateRecentFiles();
        }

        private void GoDownCommentAndBreakpoint(KeyEventArgs e) {
            List<int> commentLine = tasText.FindLines(@"^\s*#|^\*\*\*");
            if (commentLine.Count > 0) {
                int line = commentLine.FirstOrDefault(i => i > tasText.Selection.Start.iLine);
                if (line == 0) {
                    line = tasText.LinesCount - 1;
                }

                while (tasText.Selection.Start.iLine < line) {
                    tasText.Selection.GoDown(e.Shift);
                }

                tasText.ScrollLeft();
            } else {
                tasText.Selection.GoDown(e.Shift);
                tasText.ScrollLeft();
            }
        }

        private void GoUpCommentAndBreakpoint(KeyEventArgs e) {
            List<int> commentLine = tasText.FindLines(@"^\s*#|^\*\*\*");
            if (commentLine.Count > 0) {
                int line = commentLine.FindLast(i => i < tasText.Selection.Start.iLine);
                while (tasText.Selection.Start.iLine > line) {
                    tasText.Selection.GoUp(e.Shift);
                }

                tasText.ScrollLeft();
            } else {
                tasText.Selection.GoUp(e.Shift);
                tasText.ScrollLeft();
            }
        }

        private void ToggleUpdatingHotkeys() {
            CommunicationWrapper.updatingHotkeys = !CommunicationWrapper.updatingHotkeys;
            Settings.Default.UpdatingHotkeys = CommunicationWrapper.updatingHotkeys;
        }

        private void OpenFile(string fileName = null) {
            StudioCommunicationServer.instance?.WriteWait();
            if (tasText.OpenFile(fileName)) {
                UpdateRecentFiles();
                tasText.GoHome();
            }

            StudioCommunicationServer.instance?.SendPath(LastFileName);
            Text = TitleBarText;
        }

        private void UpdateRecentFiles() {
            if (!RecentFiles.Contains(LastFileName)) {
                RecentFiles.Insert(0, LastFileName);
                Settings.Default.Save();
            }

            Settings.Default.LastFileName = LastFileName;
        }

        private void ClearUncommentedBreakpoints() {
            var line = Math.Min(tasText.Selection.Start.iLine, tasText.Selection.End.iLine);
            List<int> breakpoints = tasText.FindLines(@"^\s*\*\*\*");
            tasText.RemoveLines(breakpoints);
            tasText.Selection.Start = new Place(0, Math.Min(line, tasText.LinesCount - 1));
        }

        private void ClearBreakpoints() {
            var line = Math.Min(tasText.Selection.Start.iLine, tasText.Selection.End.iLine);
            List<int> breakpoints = tasText.FindLines(@"^\s*#*\s*\*\*\*");
            tasText.RemoveLines(breakpoints);
            tasText.Selection.Start = new Place(0, Math.Min(line, tasText.LinesCount - 1));
        }

        private void CommentUncommentAllBreakpoints() {
            Range range = tasText.Selection.Clone();

            List<int> uncommentedBreakpoints = tasText.FindLines(@"^\s*\*\*\*");
            if (uncommentedBreakpoints.Count > 0) {
                foreach (int line in uncommentedBreakpoints) {
                    tasText.Selection = new Range(tasText, 0, line, 0, line);
                    tasText.InsertText("#");
                }
            } else {
                List<int> breakpoints = tasText.FindLines(@"^\s*#+\s*\*\*\*");
                foreach (int line in breakpoints) {
                    tasText.Selection = new Range(tasText, 0, line, 0, line);
                    tasText.RemoveLinePrefix("#");
                }
            }

            tasText.Selection = range;
            tasText.ScrollLeft();
        }

        private void InsertOrRemoveText(Regex regex, string insertText) {
            int currentLine = tasText.Selection.Start.iLine;
            if (regex.IsMatch(tasText.Lines[currentLine])) {
                tasText.RemoveLine(currentLine);
                if (currentLine == tasText.LinesCount) {
                    currentLine--;
                }
            } else if (currentLine >= 1 && regex.IsMatch(tasText.Lines[currentLine - 1])) {
                currentLine--;
                tasText.RemoveLine(currentLine);
            } else {
                InsertNewLine(insertText);
                currentLine++;
            }

            string text = tasText.Lines[currentLine];
            InputRecord input = new(text);
            int cursor = 4;
            if (input.Frames == 0 && input.Actions == Actions.None) {
                cursor = text.Length;
            }

            tasText.Selection = new Range(tasText, cursor, currentLine, cursor, currentLine);
        }

        private void InsertRoomName() => InsertNewLine("#lvl_" + CommunicationWrapper.LevelName());

        private void InsertTime() => InsertNewLine('#' + CommunicationWrapper.Timer());

        private void InsertConsoleLoadCommand() {
            CommunicationWrapper.command = null;
            StudioCommunicationServer.instance.GetConsoleCommand();
            Thread.Sleep(100);

            if (CommunicationWrapper.command == null) {
                return;
            }

            InsertNewLine(CommunicationWrapper.command);
        }

        private void InsertModInfo() {
            CommunicationWrapper.command = null;
            StudioCommunicationServer.instance.GetModInfo();
            Thread.Sleep(100);

            if (CommunicationWrapper.command == null) {
                return;
            }

            InsertNewLine(CommunicationWrapper.command);
        }

        private void InsertNewLine(string text) {
            text = text.Trim();
            int startLine = tasText.Selection.Start.iLine;
            tasText.Selection = new Range(tasText, 0, startLine, 0, startLine);
            tasText.InsertText(text + "\n");
            tasText.Selection = new Range(tasText, text.Length, startLine, text.Length, startLine);
        }

        private void CopyGameData() {
            if (string.IsNullOrEmpty(CommunicationWrapper.gameData)) {
                return;
            }

            Clipboard.SetText(CommunicationWrapper.gameData);
        }

        private DialogResult ShowInputDialog(string title, ref string input) {
            Size size = new(200, 70);
            DialogResult result = DialogResult.Cancel;

            using (Form inputBox = new()) {
                inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputBox.ClientSize = size;
                inputBox.Text = title;
                inputBox.StartPosition = FormStartPosition.CenterParent;
                inputBox.MinimizeBox = false;
                inputBox.MaximizeBox = false;

                TextBox textBox = new();
                textBox.Size = new Size(size.Width - 10, 23);
                textBox.Location = new Point(5, 5);
                textBox.Font = tasText.Font;
                textBox.Text = input;
                textBox.MaxLength = 1;
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
            }

            return result;
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
                            if (!string.IsNullOrEmpty(LastFileName) && tasText.IsChanged) {
                                tasText.SaveFile();
                            }
                        });
                    }

                    if (hooked) {
                        UpdateValues();
                        ScrollLeftWhenOutOfMinimized();
                        tasText.Invalidate();
                        if (CommunicationWrapper.fastForwarding) {
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

        public void EnableStudio(bool hooked) {
            if (hooked) {
                try {
                    string fileName = defaultFileName;
                    if (!File.Exists(fileName)) {
                        File.WriteAllText(fileName, string.Empty);
                    }

                    if (string.IsNullOrEmpty(LastFileName)) {
                        tasText.OpenBindingFile(fileName, Encoding.ASCII);
                        LastFileName = fileName;
                    }

                    tasText.Focus();
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            } else {
                UpdateStatusBar();

                if (Settings.Default.RememberLastFileName
                    && File.Exists(Settings.Default.LastFileName)
                    && IsFileReadable(Settings.Default.LastFileName)
                    && string.IsNullOrEmpty(LastFileName)) {
                    LastFileName = Settings.Default.LastFileName;
                    tasText.ReloadFile();
                }

                StudioCommunicationServer.Run();
            }
        }

        public void UpdateValues() {
            if (InvokeRequired) {
                Invoke((Action) UpdateValues);
            } else {
                string tas = CommunicationWrapper.state;
                if (!string.IsNullOrEmpty(tas)) {
                    string[] values = tas.Split(',');
                    int currentLine = int.Parse(values[0]);
                    if (tasText.CurrentLine != currentLine) {
                        tasText.CurrentLine = currentLine;
                    }

                    tasText.CurrentLineText = values[1];
                    currentFrame = int.Parse(values[2]);
                    totalFrames = int.Parse(values[3]);
                    tasText.SaveStateLine = int.Parse(values[4]);
                } else {
                    currentFrame = 0;
                    if (tasText.CurrentLine >= 0) {
                        tasText.CurrentLine = -1;
                    }

                    tasText.SaveStateLine = -1;
                }

                UpdateStatusBar();
            }
        }

        private void ScrollLeftWhenOutOfMinimized() {
            if (lastWindowState == FormWindowState.Minimized && WindowState == FormWindowState.Normal) {
                tasText.ScrollLeft();
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
                string gameData = CommunicationWrapper.gameData;
                lblStatus.Text = "(" + (currentFrame > 0 ? currentFrame + "/" : "")
                                     + totalFrames + ") \n" + gameData
                                     + new string('\n', Math.Max(0, 7 - gameData.Split('\n').Length));
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
                statusBar.Height = 0;
            }

            tasText.Height = ClientSize.Height - statusPanel.Height - menuStrip.Height;
        }

        private void tasText_TextChanged(object sender, TextChangedEventArgs e) {
            lastChanged = DateTime.Now;
            UpdateLines((RichText.RichText) sender, e.ChangedRange);
        }

        private void CommentText() {
            Range range = tasText.Selection.Clone();

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end) {
                int temp = start;
                start = end;
                end = temp;
            }

            tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            string text = tasText.SelectedText;

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

            tasText.SelectedText = sb.ToString();
            if (range.IsEmpty) {
                if (start < tasText.LinesCount - 1) {
                    start++;
                }

                tasText.Selection = new Range(tasText, 0, start, 0, start);
            } else {
                tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            }

            tasText.ScrollLeft();
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
                                index = 4;
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

        private void tasText_FileOpened(object sender, EventArgs e) {
            try {
                tasText.SaveFile();
            } catch { }
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

        private void rememberCurrentFileMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.RememberLastFileName = !Settings.Default.RememberLastFileName;
        }

        private void autoRemoveExclusiveActionsToolStripMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.AutoRemoveMutuallyExclusiveActions = !Settings.Default.AutoRemoveMutuallyExclusiveActions;
        }

        private void homeMenuItem_Click(object sender, EventArgs e) {
            Process.Start("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        }

        private void settingsToolStripMenuItem_Opened(object sender, EventArgs e) {
            rememberCurrentFileMenuItem.Checked = Settings.Default.RememberLastFileName;
            sendInputsToCelesteMenuItem.Checked = Settings.Default.UpdatingHotkeys;
            autoRemoveExclusiveActionsToolStripMenuItem.Checked = Settings.Default.AutoRemoveMutuallyExclusiveActions;
            showGameInfoToolStripMenuItem.Checked = Settings.Default.ShowGameInfo;
        }

        private void openCelesteTasMenuItem_Click(object sender, EventArgs e) {
            string fileName = defaultFileName;
            if (string.IsNullOrEmpty(fileName)) {
                return;
            }

            if (!File.Exists(fileName)) {
                File.WriteAllText(fileName, string.Empty);
            }

            OpenFile(fileName);
        }

        private void sendInputsToCelesteMenuItem_Click(object sender, EventArgs e) {
            ToggleUpdatingHotkeys();
        }

        private void openFileMenuItem_Click(object sender, EventArgs e) {
            OpenFile();
        }

        private void fileToolStripMenuItem_DropDownOpened(object sender, EventArgs e) {
            CreateRecentFilesMenu();
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
            InsertNewLine("StartExport (Path) (Entities)");
        }

        private void finishExportToolStripMenuItem_Click(object sender, EventArgs e) {
            InsertNewLine("FinishExport");
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

        private void copyGamerDataMenuItem_Click(object sender, EventArgs e) {
            CopyGameData();
        }

        private void fontToolStripMenuItem_Click(object sender, EventArgs e) {
            if (fontDialog.ShowDialog() != DialogResult.Cancel) {
                InitFont(fontDialog.Font);
            }
        }

        private void reconnectStudioAndCelesteToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ExternalReset();
        }

        private void insertModInfoStripMenuItem1_Click(object sender, EventArgs e) {
            InsertModInfo();
        }

        private void SwapActionKeys(char key1, char key2) {
            if (tasText.Selection.IsEmpty) {
                return;
            }

            Range range = tasText.Selection.Clone();

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end) {
                int temp = start;
                start = end;
                end = temp;
            }

            tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            string text = tasText.SelectedText;

            StringBuilder sb = new();
            Regex swapKeyRegex = new($"{key1}|{key2}");
            foreach (string lineText in text.Split('\n')) {
                if (SyntaxHighlighter.InputRecordRegex.IsMatch(lineText)) {
                    sb.AppendLine(swapKeyRegex.Replace(lineText, match => match.Value == key1.ToString() ? key2.ToString() : key1.ToString()));
                } else {
                    sb.AppendLine(lineText);
                }
            }

            tasText.SelectedText = sb.ToString().Substring(0, sb.Length - 2);
            tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            tasText.ScrollLeft();
        }

        private void swapDashKeysStripMenuItem_Click(object sender, EventArgs e) {
            SwapActionKeys('C', 'X');
        }

        private void swapJumpKeysToolStripMenuItem_Click(object sender, EventArgs e) {
            SwapActionKeys('J', 'K');
        }

        private void showGameInfoToolStripMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.ShowGameInfo = !Settings.Default.ShowGameInfo;
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
                if (!string.IsNullOrEmpty(LastFileName)) {
                    dialog.InitialDirectory = Path.GetDirectoryName(LastFileName);
                    dialog.FileName = Path.GetFileNameWithoutExtension(LastFileName) + "_libTAS_inputs.txt";
                } else {
                    dialog.FileName = "libTAS_inputs.txt";
                }

                if (dialog.ShowDialog() == DialogResult.OK) {
                    StudioCommunicationServer.instance.ConvertToLibTas(dialog.FileName);
                }
            }
        }

        private void newFileToolStripMenuItem_Click(object sender, EventArgs e) {
            int index = 1;
            string tempPath = Path.GetTempPath();
            string fileName = Path.Combine(tempPath, $"Untitled-{index}.tas");
            while (File.Exists(fileName) && new FileInfo(fileName).Length > 0) {
                index++;
                fileName = Path.Combine(tempPath, $"Untitled-{index}.tas");
            }

            File.WriteAllText(fileName, string.Empty);

            OpenFile(fileName);
        }

        private void toggleHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("ShowHitboxes");
        }

        private void toggleTriggerHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("HideTriggerHitboxes");
        }

        private void toggleSimplifiedHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("SimplifiedHitboxes");
        }

        private void switchActualCollideHitboxesToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("ShowActualCollideHitboxes");
        }

        private void toggleSimplifiedGraphicsToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("SimplifiedGraphics");
        }

        private void toggleGameplayToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("HideGameplay");
        }

        private void toggleCenterCameraToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("CenterCamera");
        }

        private void toggleRoundPositionToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("RoundPosition");
        }

        private void toggleRoundSpeedToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("RoundSpeed");
        }

        private void roundVelocityToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("RoundVelocity");
        }

        private void switchInfoHUDToolStripMenuItem_Click(object sender, EventArgs e) {
            StudioCommunicationServer.instance?.ToggleGameSetting("InfoHud");
        }
    }
}