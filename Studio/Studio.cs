using System;
using System.Collections.Generic;
using System.Drawing;
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
using CelesteStudio.Entities;
using Microsoft.Win32;
using CelesteStudio.Communication;
using CelesteStudio.Properties;
using CelesteStudio.RichText;

namespace CelesteStudio
{
    public partial class Studio : Form
    {
        public static Studio instance;
        private FormWindowState lastWindowState = FormWindowState.Normal;

        private List<InputRecord> Lines = new List<InputRecord>();
        private int totalFrames = 0, currentFrame = 0;
        private bool updating = false;
        //private GameMemory memory = new GameMemory();
        private DateTime lastChanged = DateTime.MinValue;
        private const string RegKey = @"HKEY_CURRENT_USER\SOFTWARE\CeletseStudio\Form";
        private string titleBarText {
            get =>
                (string.IsNullOrEmpty(lastFileName) ? "Celeste.tas" : Path.GetFileName(lastFileName))
                + " - Studio v"
                + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }

        private string defaultFileName {
            get {
                string fileName = "";
                if (Environment.OSVersion.Platform == PlatformID.Unix) {
                    if (null == (fileName = Environment.GetEnvironmentVariable("CELESTE_TAS_FILE")))
                        fileName = Environment.GetEnvironmentVariable("HOME") + "/.steam/steam/steamapps/common/Celeste/Celeste.tas";
                }
				else if (CommunicationWrapper.gamePath != null) {
                    fileName = Path.Combine(CommunicationWrapper.gamePath, "Celeste.tas");
                }

                return fileName;
            }
        }

        private string lastFileName {
            get => tasText.LastFileName;
            set => tasText.LastFileName = value;
        }

        float scaleFactor => DeviceDpi / 96f;

        private FileList recentFiles => Settings.Default.RecentFiles ?? (Settings.Default.RecentFiles = new FileList());

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
                    if (hasHandle)
                        mutex.ReleaseMutex();
                }
            }
        }

        public Studio()
        {
            InitializeComponent();
            InitMenu();
            InitBirdButton();

            Text = titleBarText;
			Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.CreateSpecificCulture("en-US");

			Lines.Add(new InputRecord(""));
            EnableStudio(false);

            DesktopLocation = new Point(RegRead("x", DesktopLocation.X), RegRead("y", DesktopLocation.Y));

            Size size = new Size(RegRead("w", Size.Width), RegRead("h", Size.Height));
			if (size != Size.Empty)
				Size = size;

			if (!IsTitleBarVisible())
				DesktopLocation = new Point(0, 0);

            instance = this;
        }

        private void InitMenu() {
            openRecentMenuItem.DropDownItemClicked += (sender, args) => {
                ToolStripItem clickedItem = args.ClickedItem;
                if (clickedItem.Text == "Clear") {
                    recentFiles.Clear();
                    return;
                }
                if (!File.Exists(clickedItem.Text)) {
                    contextMenuStrip.Close();
                }
                OpenFile(clickedItem.Text);
            };
        }

        private void InitBirdButton() {
            Size origSize = birdButton.Image.Size;
            birdButton.Image = new Bitmap(birdButton.Image, new Size((int)(origSize.Width * scaleFactor), (int)(origSize.Height * scaleFactor)));
            hotkeyToolTip.ReshowDelay = 200;
            hotkeyToolTip.AutoPopDelay = 20000;
            hotkeyToolTip.InitialDelay = 200;
            hotkeyToolTip.ToolTipTitle = "Fact: Birds are hard to catch";
            hotkeyToolTip.SetToolTip(birdButton, @"
Ctrl + O: Open file (Updates Celeste.tas as well)

Ctrl + Shift + S: Save as (Updates Celeste.tas as well)

Ctrl + D: Toggle sending inputs to Celeste

Ctrl + Shift + D: Refresh connection between Studio and Celeste

Ctrl + Shift + C: Copy player data to clipboard

Ctrl + K: Block comment/uncomment

Ctrl + P: Remove all breakpoints

Ctrl + .: Insert/Remove breakpoint

Ctrl + Shift + .: Insert/Remove savestate breakpoint

Ctrl + R: Insert room name

Ctrl + Shift + R: Insert console load command at current location

Ctrl + T: Insert current in-game time

Ctrl + Down/Up: Go to comment or breakpoint");
        }

        private void CreateRecentFilesMenu() {
            openRecentMenuItem.DropDownItems.Clear();
            if (recentFiles.Count == 0) {
                openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem("Nothing") {
                    Enabled = false
                });
            } else {
                for (var i = recentFiles.Count - 1; i >= 10; i--) {
                    recentFiles.Remove(recentFiles[i]);
                }
                foreach (var fileName in recentFiles) {
                    openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem(fileName) {
                        Checked = lastFileName == fileName
                    });
                }

                openRecentMenuItem.DropDownItems.Add(new ToolStripSeparator());

                openRecentMenuItem.DropDownItems.Add(new ToolStripMenuItem("Clear"));
            }
        }

        private bool IsTitleBarVisible()
        {
            int titleBarHeight = RectangleToScreen(ClientRectangle).Top - Top;
            Rectangle titleBar = new Rectangle(Left, Top, Width, titleBarHeight);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.IntersectsWith(titleBar))
                    return true;
            }
            return false;
        }

        private void TASStudio_FormClosed(object sender, FormClosedEventArgs e)
        {
            RegWrite("x", DesktopLocation.X);
            RegWrite("y", DesktopLocation.Y);
            RegWrite("w", Size.Width); RegWrite("h", Size.Height);
            Settings.Default.Save();
            BackupCelesteTas();
        }

        private void Studio_Shown(object sender, EventArgs e)
        {
            Thread updateThread = new Thread(UpdateLoop);
            updateThread.IsBackground = true;
            updateThread.Start();
        }

        private void BackupCelesteTas() {
            if (string.IsNullOrEmpty(defaultFileName) || !File.Exists(defaultFileName)) return;
            try {
                File.Copy(defaultFileName, "Celeste_bak.tas", true);
            } catch (Exception) {
                // ignore
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            if ((msg.Msg == 0x100) || (msg.Msg == 0x104)) {
                if (!tasText.IsChanged && CommunicationWrapper.CheckControls(ref msg))
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Studio_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Modifiers == (Keys.Shift | Keys.Control) && e.KeyCode == Keys.S)
                {
                    StudioCommunicationServer.instance?.WriteWait();
                    tasText.SaveNewFile();
                    StudioCommunicationServer.instance?.SendPath(Path.GetDirectoryName(lastFileName));
                    Text = titleBarText;
                    UpdateRecentFiles();
                }
                else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.S)
                {
                    tasText.SaveFile();
                }
                else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.O) {
                    OpenFile();
                }
                else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.K)
                {
                    CommentText();
                }
                else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.P)
                {
                    ClearBreakpointsAndSaveState();
				}
                else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.OemPeriod)
                {
                    InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***");
                }
                else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.OemPeriod) {
                    InsertOrRemoveText(SyntaxHighlighter.BreakPointRegex, "***S");
                }
				else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.R)
				{
					AddConsoleCommand();
				}
				else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.R) 
				{
					AddRoom();
				}
				else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.T)
				{
					AddTime();
				}
				else if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.C)
				{
					CopyPlayerData();
				}
				else if (e.Modifiers == Keys.Control && e.KeyCode == Keys.D) {
                    ToggleUpdatingHotkeys();
                }
				else if (e.Modifiers == (Keys.Shift | Keys.Control) && e.KeyCode == Keys.D) {
					StudioCommunicationServer.instance?.ExternalReset();
				}
                else if (e.KeyCode == Keys.Down && (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift))) {
                    GoDownCommentAndBreakpoint(e);
                } else if (e.KeyCode == Keys.Up && (e.Modifiers == Keys.Control || e.Modifiers == (Keys.Control | Keys.Shift))) {
                    GoUpCommentAndBreakpoint(e);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.Write(ex);
            }
        }

        private void GoDownCommentAndBreakpoint(KeyEventArgs e) {
            List<int> commentLine = tasText.FindLines(@"^\s*#|^\*\*\*");
            if (commentLine.Count > 0) {
                int line = commentLine.FirstOrDefault(i => i > tasText.Selection.Start.iLine);
                if (line == 0) line = tasText.LinesCount - 1;
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
            if (tasText.TextSource.Manager.UndoEnabled && (string.IsNullOrEmpty(lastFileName) || lastFileName == defaultFileName)) {
                DialogResult result = MessageBox.Show("Celeste.tas progress will be lost If you open another file, do you want to continue?",
                    "Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.No) return;
            }

            StudioCommunicationServer.instance?.WriteWait();
            if (tasText.OpenFile(fileName)) {
                UpdateRecentFiles();
            }

            StudioCommunicationServer.instance?.SendPath(Path.GetDirectoryName(lastFileName));
            Text = titleBarText;
        }

        private void UpdateRecentFiles() {
            if (lastFileName != defaultFileName) {
                if (!recentFiles.Contains(lastFileName)) {
                    recentFiles.Insert(0, lastFileName);
                }

                if (lastFileName != defaultFileName) {
                    Settings.Default.LastFileName = lastFileName;
                }
            }
        }

        private void ClearBreakpointsAndSaveState()
        {
            List<int> breakpoints = tasText.FindLines(@"\*\*\*", RegexOptions.None);
            List<int> saveStates = tasText.FindLines(@"^\s*savestate\s*$", RegexOptions.IgnoreCase);
            tasText.RemoveLines(breakpoints.Union(saveStates).ToList());
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
            }else {
                AddNewLine(insertText);
                currentLine++;
            }
            string text = tasText.Lines[currentLine];
            InputRecord input = new InputRecord(text);
            int cursor = 4;
            if (input.Frames == 0 && input.Actions == Actions.None) {
                cursor = text.Length;
            }
            tasText.Selection = new Range(tasText, cursor, currentLine, cursor, currentLine);
        }

        private void AddRoom() => AddNewLine("#lvl_" + CommunicationWrapper.LevelName());

		private void AddTime() => AddNewLine('#' + CommunicationWrapper.Timer());

		private void AddConsoleCommand()
		{
			CommunicationWrapper.command = null;
			StudioCommunicationServer.instance.GetConsoleCommand();
			Thread.Sleep(100);

			if (CommunicationWrapper.command == null)
				return;

			AddNewLine(CommunicationWrapper.command);
		}

		private void AddNewLine(string text) {
            text = text.Trim();
			int startLine = tasText.Selection.Start.iLine;
			tasText.Selection = new Range(tasText, 0, startLine, 0, startLine);
            tasText.InsertText(text + "\n");
			tasText.Selection = new Range(tasText, text.Length, startLine, text.Length, startLine);
		}

		private void CopyPlayerData() {
            if (string.IsNullOrEmpty(CommunicationWrapper.playerData)) return;
			Clipboard.SetText(CommunicationWrapper.playerData);
		}

        private DialogResult ShowInputDialog(string title, ref string input)
        {
            Size size = new Size(200, 70);
            DialogResult result = DialogResult.Cancel;

            using (Form inputBox = new Form())
            {
                inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputBox.ClientSize = size;
                inputBox.Text = title;
                inputBox.StartPosition = FormStartPosition.CenterParent;
                inputBox.MinimizeBox = false;
                inputBox.MaximizeBox = false;

                TextBox textBox = new TextBox();
                textBox.Size = new Size(size.Width - 10, 23);
                textBox.Location = new Point(5, 5);
                textBox.Font = tasText.Font;
                textBox.Text = input;
                textBox.MaxLength = 1;
                inputBox.Controls.Add(textBox);

                Button okButton = new Button();
                okButton.DialogResult = DialogResult.OK;
                okButton.Name = "okButton";
                okButton.Size = new Size(75, 23);
                okButton.Text = "&OK";
                okButton.Location = new Point(size.Width - 80 - 80, 39);
                inputBox.Controls.Add(okButton);

                Button cancelButton = new Button();
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
        private void UpdateLoop()
        {
            bool lastHooked = false;
            while (true)
            {
                try
                {
                    bool hooked = StudioCommunicationServer.Initialized;
                    if (lastHooked != hooked)
                    {
                        lastHooked = hooked;
                        this.Invoke((Action)delegate () { EnableStudio(hooked); });
                    }
                    if (lastChanged.AddSeconds(0.3f) < DateTime.Now)
                    {
                        lastChanged = DateTime.Now;
                        this.Invoke((Action)delegate ()
                        {
                            if ((!string.IsNullOrEmpty(lastFileName) || !string.IsNullOrEmpty(tasText.SaveToFileName)) && tasText.IsChanged)
                            {
                                tasText.SaveFile();
                            }
                        });
                    }
                    if (hooked)
                    {
                        UpdateValues();
                        ScrollLeftWhenOutOfMinimized();
                        tasText.Invalidate();
                        if (CommunicationWrapper.fastForwarding)
                            CommunicationWrapper.CheckFastForward();
                    }

                    Thread.Sleep(14);
                }
                catch //(Exception e) 
                {
                    //Console.Write(e);
                }
            }
        }

        public void EnableStudio(bool hooked)
        {
            if (hooked)
            {
                try
                {
                    string fileName = defaultFileName;
                    if (!File.Exists(fileName)) { File.WriteAllText(fileName, string.Empty); }

                    if (string.IsNullOrEmpty(lastFileName))
                    {
                        if (string.IsNullOrEmpty(tasText.SaveToFileName))
                        {
                            tasText.OpenBindingFile(fileName, Encoding.ASCII);
                        }
                        lastFileName = fileName;
                    }
                    tasText.SaveToFileName = fileName;
                    if (lastFileName != tasText.SaveToFileName)
                    {
                        tasText.SaveFile(true);
                    }
                    tasText.Focus();
                } catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            else {
                UpdateStatusBar();

                if (Settings.Default.RememberLastFileName
                    && File.Exists(Settings.Default.LastFileName)
                    && Settings.Default.LastFileName != defaultFileName
                    && IsFileReadable(Settings.Default.LastFileName)
                    && string.IsNullOrEmpty(lastFileName)) {
                    lastFileName = Settings.Default.LastFileName;
                    tasText.ReloadFile();
                }

                StudioCommunicationServer.Run();
            }
        }
        public void UpdateValues()
        {
            if (InvokeRequired)
            {
                Invoke((Action)UpdateValues);
            }
            else
            {
                string tas = CommunicationWrapper.state;
                if (!string.IsNullOrEmpty(tas))
                {
                    int index = tas.IndexOf('[');
                    string num = tas.Substring(0, index);
                    int temp = 0;
                    if (int.TryParse(num, out temp))
                    {
                        temp--;
                        if (tasText.CurrentLine != temp)
                        {
                            tasText.CurrentLine = temp;
                        }
                    }

                    index = tas.IndexOf(':');
                    int pIndex = tas.IndexOf(')', index);
                    if (pIndex >= 0)
                    {
                        num = tas.Substring(index + 2, tas.IndexOf(')', index) - index - 2);
                    }
                    if (int.TryParse(num, out temp))
                    {
                        currentFrame = temp;
                    }

                    index = tas.IndexOf('(');
                    int index2 = tas.IndexOf(' ', index);
                    if (index2 >= 0)
                    {
                        num = tas.Substring(index + 1, index2 - index - 1);
                        if (tasText.CurrentLineText != num)
                        {
                            tasText.CurrentLineText = Environment.OSVersion.Platform == PlatformID.Unix ? num + "     ." : num;
                        }
                    }
                    index = tas.IndexOf(']');
                    if (index >= 0 && int.TryParse(tas.Substring(index + 1), out temp)) {
                        tasText.SaveStateLine = temp;
                    }
                }
                else
                {
                    currentFrame = 0;
                    if (tasText.CurrentLine >= 0)
                    {
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

        private void tasText_LineRemoved(object sender, LineRemovedEventArgs e)
        {
            int count = e.Count;
            while (count-- > 0)
            {
                InputRecord input = Lines[e.Index];
                totalFrames -= input.Frames;
                Lines.RemoveAt(e.Index);
            }

            UpdateStatusBar();
        }
        private void tasText_LineInserted(object sender, LineInsertedEventArgs e)
        {
            RichText.RichText tas = (RichText.RichText)sender;
            int count = e.Count;
            while (count-- > 0)
            {
                InputRecord input = new InputRecord(tas.GetLineText(e.Index + count));
                Lines.Insert(e.Index, input);
                totalFrames += input.Frames;
            }

            UpdateStatusBar();
        }
        private void UpdateStatusBar()
        {
            if (StudioCommunicationServer.Initialized)
            {
                string playeroutput = CommunicationWrapper.playerData;
                lblStatus.Text = "(" + (currentFrame > 0 ? currentFrame + "/" : "") 
                    + totalFrames + ") \n" + playeroutput 
                    + new string('\n', 7 - playeroutput.Split('\n').Length);
            }
            else
            {
                lblStatus.Text = "(" + totalFrames + ")\r\nSearching...";
            }
            int bottomExtraSpace = TextRenderer.MeasureText("\n", lblStatus.Font).Height / 5;
            statusBar.Height = TextRenderer.MeasureText(lblStatus.Text.Trim(), lblStatus.Font).Height + bottomExtraSpace;
            tasText.Height = ClientSize.Height - statusBar.Height;
        }

        private void tasText_TextChanged(object sender, TextChangedEventArgs e)
        {
            lastChanged = DateTime.Now;
            UpdateLines((RichText.RichText)sender, e.ChangedRange);
        }
        private void CommentText()
        {
            Range range = tasText.Selection.Clone();

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }
            tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            string text = tasText.SelectedText;

            int i = 0;
            bool startLine = true;
            StringBuilder sb = new StringBuilder(text.Length + end - start);
            while (i < text.Length)
            {
                char c = text[i++];
                if (startLine)
                {
                    if (c != '#')
                    {
                        if (c != '\r')
                            sb.Append('#');
                        sb.Append(c);
                    }
                    startLine = false;
                }
                else if (c == '\n')
                {
                    sb.AppendLine();
                    startLine = true;
                }
                else if (c != '\r')
                {
                    sb.Append(c);
                }
            }

            tasText.SelectedText = sb.ToString();
            if (range.IsEmpty)
            {
                if (start < tasText.LinesCount - 1)
                {
                    start++;
                }
                tasText.Selection = new Range(tasText, 0, start, 0, start);
            }
            else
            {
                tasText.Selection = new Range(tasText, 0, start, tasText[end].Count, end);
            }
            tasText.ScrollLeft();
        }
        private void UpdateLines(RichText.RichText tas, Range range)
        {
            if (updating) { return; }
            updating = true;

            int start = range.Start.iLine;
            int end = range.End.iLine;
            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }
            int originalStart = start;

            bool modified = false;
            StringBuilder sb = new StringBuilder();
            Place place = new Place(0, end);
            while (start <= end)
            {
                InputRecord old = Lines.Count > start ? Lines[start] : null;
                string text = tas[start++].Text;
                InputRecord input = new InputRecord(text);
                if (old != null)
                {
                    totalFrames -= old.Frames;

                    string line = input.ToString();
                    if (text != line)
                    {
                        if (old.Frames == 0 && input.Frames == 0 && old.ZeroPadding == input.ZeroPadding && old.Equals(input) && line.Length >= text.Length)
                        {
                            line = string.Empty;
                        }

                        Range oldRange = tas.Selection;
                        if (!string.IsNullOrEmpty(line))
                        {
                            int index = oldRange.Start.iChar + line.Length - text.Length;
                            if (index < 0) { index = 0; }
                            if (index > 4) { index = 4; }
                            if (old.Frames == input.Frames && old.ZeroPadding == input.ZeroPadding) { index = 4; }

                            place = new Place(index, start - 1);
                        }
                        modified = true;
                    }
                    else
                    {
                        place = new Place(4, start - 1);
                    }

                    text = line;
                    Lines[start - 1] = input;
                }
                else
                {
                    place = new Place(text.Length, start - 1);
                }

                if (start <= end)
                {
                    sb.AppendLine(text);
                }
                else
                {
                    sb.Append(text);
                }

                totalFrames += input.Frames;
            }

            if (modified)
            {
                tas.Selection = new Range(tas, 0, originalStart, tas[end].Count, end);
                tas.SelectedText = sb.ToString();
                tas.Selection = new Range(tas, place.iChar, end, place.iChar, end);
            }

            if (tas.IsChanged)
            {
                Text = titleBarText + " ***";
            }

            UpdateStatusBar();

            updating = false;
        }
        private void tasText_NoChanges(object sender, EventArgs e)
        {
            Text = titleBarText;
        }
        private void tasText_FileOpening(object sender, EventArgs e)
        {
            Lines.Clear();
            totalFrames = 0;
            UpdateStatusBar();
        }
        private void tasText_LineNeeded(object sender, LineNeededEventArgs e)
        {
            InputRecord record = new InputRecord(e.SourceLineText);
            e.DisplayedLineText = record.ToString();
        }
        private void tasText_FileOpened(object sender, EventArgs e)
        {
            try
            {
                tasText.SaveFile(true);
            }
            catch { }
        }
        private T RegRead<T>(string name, T def)
        {
            object o = null;
            try
            {
                o = Registry.GetValue(RegKey, name, null);
            }
            catch { }

            if (o is T)
            {
                return (T)o;
            }

            return def;
        }
        private void RegWrite<T>(string name, T val)
        {
            try
            {
                Registry.SetValue(RegKey, name, val);
            }
            catch { }
        }

        private bool IsFileReadable(string fileName)
        {
            try
            {
                using(FileStream stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return false;
            }

            //file is not locked
            return true;
        }

        private void rememberCurrentFileMenuItem_Click(object sender, EventArgs e) {
            Settings.Default.RememberLastFileName = !Settings.Default.RememberLastFileName;
            ((ToolStripMenuItem) sender).Checked = Settings.Default.RememberLastFileName;
        }

        private void homeMenuItem_Click(object sender, EventArgs e) {
            System.Diagnostics.Process.Start("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        }

        private void contextMenuStrip_Opened(object sender, EventArgs e) {
            rememberCurrentFileMenuItem.Checked = Settings.Default.RememberLastFileName;
            sendInputsToCelesteMenuItem.Checked = Settings.Default.UpdatingHotkeys;
            CreateRecentFilesMenu();
        }

		private void Studio_Load(object sender, EventArgs e) {

		}

		private void openCelesteTasMenuItem_Click(object sender, EventArgs e) {
            string fileName = defaultFileName;
            if (string.IsNullOrEmpty(fileName)) return;
            if (!File.Exists(fileName)) { File.WriteAllText(fileName, string.Empty); }
            OpenFile(fileName);
        }

        private void birdButton_Click(object sender, EventArgs e)
        {
            contextMenuStrip.Show(birdButton, 0, birdButton.Height);
        }

        private void sendInputsToCelesteMenuItem_Click(object sender, EventArgs e) {
            ToggleUpdatingHotkeys();
        }

        private void openFileMenuItem_Click(object sender, EventArgs e) {
            OpenFile();
        }
    }
}
