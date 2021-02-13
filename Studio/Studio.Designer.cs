using CelesteStudio.RichText;

namespace CelesteStudio {
	partial class Studio {
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if (disposing && (components != null)) {
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Studio));
            this.statusBar = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.hotkeyToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.statusBarContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.copyPlayerDataMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.reconnectStudioAndCelesteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openFileMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openCelesteTasMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openRecentMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.saveAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.rememberCurrentFileMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.sendInputsToCelesteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.homeMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.dividerLabel = new System.Windows.Forms.Label();
            this.tasTextContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.insertRemoveBreakPointToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertRemoveSavestateBreakPointToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.removeAllBreakpointsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.commentUncommentTextToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertRoomNameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertCurrentInGameTimeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertConsoleLoadCommandToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertOtherCommandToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.enforceLegalToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.unsafeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.readToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.playToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.setToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.analogueModeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.startExportToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.finishExportToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator5 = new System.Windows.Forms.ToolStripSeparator();
            this.addToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.skipToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tasText = new CelesteStudio.RichText.RichText();
            this.restoreSettingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusBar.SuspendLayout();
            this.statusBarContextMenuStrip.SuspendLayout();
            this.menuStrip.SuspendLayout();
            this.tasTextContextMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // statusBar
            // 
            this.statusBar.AutoSize = false;
            this.statusBar.Font = new System.Drawing.Font("Courier New", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusBar.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.statusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus});
            this.statusBar.Location = new System.Drawing.Point(0, 619);
            this.statusBar.Name = "statusBar";
            this.statusBar.Size = new System.Drawing.Size(308, 55);
            this.statusBar.TabIndex = 1;
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new System.Drawing.Font("Courier New", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Overflow = System.Windows.Forms.ToolStripItemOverflow.Never;
            this.lblStatus.Size = new System.Drawing.Size(293, 50);
            this.lblStatus.Spring = true;
            this.lblStatus.Text = "Searching...";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            // 
            // hotkeyToolTip
            // 
            this.hotkeyToolTip.AutomaticDelay = 200;
            this.hotkeyToolTip.AutoPopDelay = 5000;
            this.hotkeyToolTip.InitialDelay = 200;
            this.hotkeyToolTip.IsBalloon = true;
            this.hotkeyToolTip.ReshowDelay = 200;
            this.hotkeyToolTip.ShowAlways = true;
            this.hotkeyToolTip.ToolTipTitle = "Fact: Birds are hard to catch";
            // 
            // statusBarContextMenuStrip
            // 
            this.statusBarContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.copyPlayerDataMenuItem,
            this.reconnectStudioAndCelesteToolStripMenuItem});
            this.statusBarContextMenuStrip.Name = "statusBarMenuStrip";
            this.statusBarContextMenuStrip.Size = new System.Drawing.Size(334, 48);
            // 
            // copyPlayerDataMenuItem
            // 
            this.copyPlayerDataMenuItem.Name = "copyPlayerDataMenuItem";
            this.copyPlayerDataMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.C)));
            this.copyPlayerDataMenuItem.Size = new System.Drawing.Size(333, 22);
            this.copyPlayerDataMenuItem.Text = "Copy Player Data to Clipboard";
            this.copyPlayerDataMenuItem.Click += new System.EventHandler(this.copyPlayerDataMenuItem_Click);
            // 
            // reconnectStudioAndCelesteToolStripMenuItem
            // 
            this.reconnectStudioAndCelesteToolStripMenuItem.Name = "reconnectStudioAndCelesteToolStripMenuItem";
            this.reconnectStudioAndCelesteToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.D)));
            this.reconnectStudioAndCelesteToolStripMenuItem.Size = new System.Drawing.Size(333, 22);
            this.reconnectStudioAndCelesteToolStripMenuItem.Text = "Reconnect Studio and Celeste";
            this.reconnectStudioAndCelesteToolStripMenuItem.Click += new System.EventHandler(this.reconnectStudioAndCelesteToolStripMenuItem_Click);
            // 
            // menuStrip
            // 
            this.menuStrip.BackColor = System.Drawing.SystemColors.Control;
            this.menuStrip.Font = new System.Drawing.Font("Courier New", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.settingsToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(308, 24);
            this.menuStrip.TabIndex = 3;
            this.menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openFileMenuItem,
            this.openCelesteTasMenuItem,
            this.openRecentMenuItem,
            this.toolStripSeparator1,
            this.saveAsToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(52, 20);
            this.fileToolStripMenuItem.Text = "&File";
            this.fileToolStripMenuItem.DropDownOpened += new System.EventHandler(this.fileToolStripMenuItem_DropDownOpened);
            // 
            // openFileMenuItem
            // 
            this.openFileMenuItem.Name = "openFileMenuItem";
            this.openFileMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.openFileMenuItem.Size = new System.Drawing.Size(260, 22);
            this.openFileMenuItem.Text = "&Open File...";
            this.openFileMenuItem.Click += new System.EventHandler(this.openFileMenuItem_Click);
            // 
            // openCelesteTasMenuItem
            // 
            this.openCelesteTasMenuItem.Name = "openCelesteTasMenuItem";
            this.openCelesteTasMenuItem.Size = new System.Drawing.Size(260, 22);
            this.openCelesteTasMenuItem.Text = "Open &Celeste.tas";
            this.openCelesteTasMenuItem.Click += new System.EventHandler(this.openCelesteTasMenuItem_Click);
            // 
            // openRecentMenuItem
            // 
            this.openRecentMenuItem.Name = "openRecentMenuItem";
            this.openRecentMenuItem.Size = new System.Drawing.Size(260, 22);
            this.openRecentMenuItem.Text = "Open &Recent";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(257, 6);
            // 
            // saveAsToolStripMenuItem
            // 
            this.saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            this.saveAsToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.S)));
            this.saveAsToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
            this.saveAsToolStripMenuItem.Text = "&Save As...";
            this.saveAsToolStripMenuItem.Click += new System.EventHandler(this.saveAsToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.rememberCurrentFileMenuItem,
            this.sendInputsToCelesteMenuItem});
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(84, 20);
            this.settingsToolStripMenuItem.Text = "&Settings";
            this.settingsToolStripMenuItem.DropDownOpened += new System.EventHandler(this.settingsToolStripMenuItem_Opened);
            // 
            // rememberCurrentFileMenuItem
            // 
            this.rememberCurrentFileMenuItem.Name = "rememberCurrentFileMenuItem";
            this.rememberCurrentFileMenuItem.Size = new System.Drawing.Size(404, 22);
            this.rememberCurrentFileMenuItem.Text = "&Remember the Current File for Next Launch";
            this.rememberCurrentFileMenuItem.Click += new System.EventHandler(this.rememberCurrentFileMenuItem_Click);
            // 
            // sendInputsToCelesteMenuItem
            // 
            this.sendInputsToCelesteMenuItem.Name = "sendInputsToCelesteMenuItem";
            this.sendInputsToCelesteMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D)));
            this.sendInputsToCelesteMenuItem.Size = new System.Drawing.Size(404, 22);
            this.sendInputsToCelesteMenuItem.Text = "&Send Inputs to Celeste";
            this.sendInputsToCelesteMenuItem.Click += new System.EventHandler(this.sendInputsToCelesteMenuItem_Click);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.homeMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(52, 20);
            this.helpToolStripMenuItem.Text = "&Help";
            // 
            // homeMenuItem
            // 
            this.homeMenuItem.Name = "homeMenuItem";
            this.homeMenuItem.Size = new System.Drawing.Size(108, 22);
            this.homeMenuItem.Text = "&Home";
            this.homeMenuItem.Click += new System.EventHandler(this.homeMenuItem_Click);
            // 
            // dividerLabel
            // 
            this.dividerLabel.BackColor = System.Drawing.SystemColors.ActiveBorder;
            this.dividerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.dividerLabel.Location = new System.Drawing.Point(0, 24);
            this.dividerLabel.Name = "dividerLabel";
            this.dividerLabel.Size = new System.Drawing.Size(308, 1);
            this.dividerLabel.TabIndex = 4;
            // 
            // tasTextContextMenuStrip
            // 
            this.tasTextContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.insertRemoveBreakPointToolStripMenuItem,
            this.insertRemoveSavestateBreakPointToolStripMenuItem,
            this.removeAllBreakpointsToolStripMenuItem,
            this.toolStripSeparator2,
            this.commentUncommentTextToolStripMenuItem,
            this.insertRoomNameToolStripMenuItem,
            this.insertCurrentInGameTimeToolStripMenuItem,
            this.insertConsoleLoadCommandToolStripMenuItem,
            this.insertOtherCommandToolStripMenuItem});
            this.tasTextContextMenuStrip.Name = "tasTextContextMenuStrip";
            this.tasTextContextMenuStrip.Size = new System.Drawing.Size(426, 208);
            // 
            // insertRemoveBreakPointToolStripMenuItem
            // 
            this.insertRemoveBreakPointToolStripMenuItem.Name = "insertRemoveBreakPointToolStripMenuItem";
            this.insertRemoveBreakPointToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.OemPeriod)));
            this.insertRemoveBreakPointToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertRemoveBreakPointToolStripMenuItem.Text = "Insert/Remove Breakpoint";
            this.insertRemoveBreakPointToolStripMenuItem.Click += new System.EventHandler(this.insertRemoveBreakPointToolStripMenuItem_Click);
            // 
            // insertRemoveSavestateBreakPointToolStripMenuItem
            // 
            this.insertRemoveSavestateBreakPointToolStripMenuItem.Name = "insertRemoveSavestateBreakPointToolStripMenuItem";
            this.insertRemoveSavestateBreakPointToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.OemPeriod)));
            this.insertRemoveSavestateBreakPointToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertRemoveSavestateBreakPointToolStripMenuItem.Text = "Insert/Remove Savestate Breakpoint";
            this.insertRemoveSavestateBreakPointToolStripMenuItem.Click += new System.EventHandler(this.insertRemoveSavestateBreakPointToolStripMenuItem_Click);
            // 
            // removeAllBreakpointsToolStripMenuItem
            // 
            this.removeAllBreakpointsToolStripMenuItem.Name = "removeAllBreakpointsToolStripMenuItem";
            this.removeAllBreakpointsToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P)));
            this.removeAllBreakpointsToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.removeAllBreakpointsToolStripMenuItem.Text = "Remove All Breakpoints";
            this.removeAllBreakpointsToolStripMenuItem.Click += new System.EventHandler(this.toolStripMenuItem1_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(422, 6);
            // 
            // commentUncommentTextToolStripMenuItem
            // 
            this.commentUncommentTextToolStripMenuItem.Name = "commentUncommentTextToolStripMenuItem";
            this.commentUncommentTextToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.K)));
            this.commentUncommentTextToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.commentUncommentTextToolStripMenuItem.Text = "Comment/Uncomment Text";
            this.commentUncommentTextToolStripMenuItem.Click += new System.EventHandler(this.commentUncommentTextToolStripMenuItem_Click);
            // 
            // insertRoomNameToolStripMenuItem
            // 
            this.insertRoomNameToolStripMenuItem.Name = "insertRoomNameToolStripMenuItem";
            this.insertRoomNameToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.R)));
            this.insertRoomNameToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertRoomNameToolStripMenuItem.Text = "Insert Room Name";
            this.insertRoomNameToolStripMenuItem.Click += new System.EventHandler(this.insertRoomNameToolStripMenuItem_Click);
            // 
            // insertCurrentInGameTimeToolStripMenuItem
            // 
            this.insertCurrentInGameTimeToolStripMenuItem.Name = "insertCurrentInGameTimeToolStripMenuItem";
            this.insertCurrentInGameTimeToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.T)));
            this.insertCurrentInGameTimeToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertCurrentInGameTimeToolStripMenuItem.Text = "Insert Current In-Game Time";
            this.insertCurrentInGameTimeToolStripMenuItem.Click += new System.EventHandler(this.insertCurrentInGameTimeToolStripMenuItem_Click);
            // 
            // insertConsoleLoadCommandToolStripMenuItem
            // 
            this.insertConsoleLoadCommandToolStripMenuItem.Name = "insertConsoleLoadCommandToolStripMenuItem";
            this.insertConsoleLoadCommandToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.R)));
            this.insertConsoleLoadCommandToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertConsoleLoadCommandToolStripMenuItem.Text = "Insert Console Load Command";
            this.insertConsoleLoadCommandToolStripMenuItem.Click += new System.EventHandler(this.insertConsoleLoadCommandToolStripMenuItem_Click);
            // 
            // insertOtherCommandToolStripMenuItem
            // 
            this.insertOtherCommandToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.enforceLegalToolStripMenuItem,
            this.unsafeToolStripMenuItem,
            this.readToolStripMenuItem,
            this.playToolStripMenuItem,
            this.setToolStripMenuItem,
            this.restoreSettingsToolStripMenuItem,
            this.toolStripSeparator3,
            this.analogueModeToolStripMenuItem,
            this.toolStripSeparator4,
            this.startExportToolStripMenuItem,
            this.finishExportToolStripMenuItem,
            this.toolStripSeparator5,
            this.addToolStripMenuItem,
            this.skipToolStripMenuItem});
            this.insertOtherCommandToolStripMenuItem.Name = "insertOtherCommandToolStripMenuItem";
            this.insertOtherCommandToolStripMenuItem.Size = new System.Drawing.Size(425, 22);
            this.insertOtherCommandToolStripMenuItem.Text = "Insert Other Command";
            // 
            // enforceLegalToolStripMenuItem
            // 
            this.enforceLegalToolStripMenuItem.Name = "enforceLegalToolStripMenuItem";
            this.enforceLegalToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.enforceLegalToolStripMenuItem.Text = "EnforceLegal";
            this.enforceLegalToolStripMenuItem.ToolTipText = "This is used at the start of fullgame files.\r\nIt prevents the use of commands whi" +
    "ch would not be legal in a run.";
            this.enforceLegalToolStripMenuItem.Click += new System.EventHandler(this.enforceLegalToolStripMenuItem_Click);
            // 
            // unsafeToolStripMenuItem
            // 
            this.unsafeToolStripMenuItem.Name = "unsafeToolStripMenuItem";
            this.unsafeToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.unsafeToolStripMenuItem.Text = "Unsafe";
            this.unsafeToolStripMenuItem.ToolTipText = "The TAS will normally only run inside levels.\r\nConsole load normally forces the T" +
    "AS to load the debug save.\r\nUnsafe allows the TAS to run anywhere, on any save.";
            this.unsafeToolStripMenuItem.Click += new System.EventHandler(this.unsafeToolStripMenuItem_Click);
            // 
            // readToolStripMenuItem
            // 
            this.readToolStripMenuItem.Name = "readToolStripMenuItem";
            this.readToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.readToolStripMenuItem.Text = "Read";
            this.readToolStripMenuItem.ToolTipText = "Will read inputs from the specified file.";
            this.readToolStripMenuItem.Click += new System.EventHandler(this.readToolStripMenuItem_Click);
            // 
            // playToolStripMenuItem
            // 
            this.playToolStripMenuItem.Name = "playToolStripMenuItem";
            this.playToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.playToolStripMenuItem.Text = "Play";
            this.playToolStripMenuItem.ToolTipText = "A simplified Read command which skips to the starting line in the current file.\r\n" +
    "Useful for splitting a large level into larger chunks.";
            this.playToolStripMenuItem.Click += new System.EventHandler(this.playToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(177, 6);
            // 
            // setToolStripMenuItem
            // 
            this.setToolStripMenuItem.Name = "setToolStripMenuItem";
            this.setToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.setToolStripMenuItem.Text = "Set";
            this.setToolStripMenuItem.ToolTipText = "Sets the specified setting to the specified value.";
            this.setToolStripMenuItem.Click += new System.EventHandler(this.setToolStripMenuItem_Click);
            // 
            // analogueModeToolStripMenuItem
            // 
            this.analogueModeToolStripMenuItem.Name = "analogueModeToolStripMenuItem";
            this.analogueModeToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.analogueModeToolStripMenuItem.Text = "AnalogueMode";
            this.analogueModeToolStripMenuItem.Click += new System.EventHandler(this.analogueModeToolStripMenuItem_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            this.toolStripSeparator4.Size = new System.Drawing.Size(177, 6);
            // 
            // startExportToolStripMenuItem
            // 
            this.startExportToolStripMenuItem.Name = "startExportToolStripMenuItem";
            this.startExportToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.startExportToolStripMenuItem.Text = "StartExport";
            this.startExportToolStripMenuItem.ToolTipText = "Dumps data to a file, which can be used to analyze desyncs.";
            this.startExportToolStripMenuItem.Click += new System.EventHandler(this.startExportToolStripMenuItem_Click);
            // 
            // finishExportToolStripMenuItem
            // 
            this.finishExportToolStripMenuItem.Name = "finishExportToolStripMenuItem";
            this.finishExportToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.finishExportToolStripMenuItem.Text = "FinishExport";
            this.finishExportToolStripMenuItem.ToolTipText = "Dumps data to a file, which can be used to analyze desyncs.";
            this.finishExportToolStripMenuItem.Click += new System.EventHandler(this.finishExportToolStripMenuItem_Click);
            // 
            // toolStripSeparator5
            // 
            this.toolStripSeparator5.Name = "toolStripSeparator5";
            this.toolStripSeparator5.Size = new System.Drawing.Size(177, 6);
            // 
            // addToolStripMenuItem
            // 
            this.addToolStripMenuItem.Name = "addToolStripMenuItem";
            this.addToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.addToolStripMenuItem.Text = "Add";
            this.addToolStripMenuItem.ToolTipText = "Serve as instructions to the libTAS converter.\r\nOdds are you don\'t need to worry " +
    "about this.";
            this.addToolStripMenuItem.Click += new System.EventHandler(this.addToolStripMenuItem_Click);
            // 
            // skipToolStripMenuItem
            // 
            this.skipToolStripMenuItem.Name = "skipToolStripMenuItem";
            this.skipToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.skipToolStripMenuItem.Text = "Skip";
            this.skipToolStripMenuItem.ToolTipText = "Serve as instructions to the libTAS converter.\r\nOdds are you don\'t need to worry " +
    "about this.";
            this.skipToolStripMenuItem.Click += new System.EventHandler(this.skipToolStripMenuItem_Click);
            // 
            // tasText
            // 
            this.tasText.ActiveLineColor = System.Drawing.Color.Lime;
            this.tasText.AllowDrop = true;
            this.tasText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tasText.AutoIndent = false;
            this.tasText.AutoScrollMinSize = new System.Drawing.Size(33, 84);
            this.tasText.BackBrush = null;
            this.tasText.ChangedLineColor = System.Drawing.Color.DarkOrange;
            this.tasText.CommentPrefix = "#";
            this.tasText.CurrentLineColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.tasText.CurrentLineText = null;
            this.tasText.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.tasText.DisabledColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(180)))), ((int)(((byte)(180)))), ((int)(((byte)(180)))));
            this.tasText.Font = new System.Drawing.Font("Courier New", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tasText.ForeColor = System.Drawing.Color.Black;
            this.tasText.Language = CelesteStudio.RichText.Language.TAS;
            this.tasText.LastFileName = null;
            this.tasText.LineNumberColor = System.Drawing.Color.Black;
            this.tasText.Location = new System.Drawing.Point(0, 24);
            this.tasText.Name = "tasText";
            this.tasText.Paddings = new System.Windows.Forms.Padding(0);
            this.tasText.SelectionColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.tasText.Size = new System.Drawing.Size(308, 567);
            this.tasText.TabIndex = 0;
            this.tasText.TabLength = 0;
            this.tasText.TextChanged += new System.EventHandler<CelesteStudio.RichText.TextChangedEventArgs>(this.tasText_TextChanged);
            this.tasText.NoChanges += new System.EventHandler(this.tasText_NoChanges);
            this.tasText.FileOpening += new System.EventHandler(this.tasText_FileOpening);
            this.tasText.FileOpened += new System.EventHandler(this.tasText_FileOpened);
            this.tasText.LineInserted += new System.EventHandler<CelesteStudio.RichText.LineInsertedEventArgs>(this.tasText_LineInserted);
            this.tasText.LineNeeded += new System.EventHandler<CelesteStudio.RichText.LineNeededEventArgs>(this.tasText_LineNeeded);
            this.tasText.LineRemoved += new System.EventHandler<CelesteStudio.RichText.LineRemovedEventArgs>(this.tasText_LineRemoved);
            // 
            // restoreSettingsToolStripMenuItem
            // 
            this.restoreSettingsToolStripMenuItem.Name = "restoreSettingsToolStripMenuItem";
            this.restoreSettingsToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.restoreSettingsToolStripMenuItem.Text = "RestoreSettings";
            this.restoreSettingsToolStripMenuItem.Click += new System.EventHandler(this.restoreSettingsToolStripMenuItem_Click);
            // 
            // Studio
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.ClientSize = new System.Drawing.Size(308, 674);
            this.Controls.Add(this.dividerLabel);
            this.Controls.Add(this.statusBar);
            this.Controls.Add(this.menuStrip);
            this.Controls.Add(this.tasText);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip;
            this.MinimumSize = new System.Drawing.Size(200, 186);
            this.Name = "Studio";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Studio";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.TASStudio_FormClosed);
            this.Shown += new System.EventHandler(this.Studio_Shown);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Studio_KeyDown);
            this.statusBar.ResumeLayout(false);
            this.statusBar.PerformLayout();
            this.statusBarContextMenuStrip.ResumeLayout(false);
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.tasTextContextMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.StatusStrip statusBar;
		public RichText.RichText tasText;
        private System.Windows.Forms.ToolTip hotkeyToolTip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ContextMenuStrip statusBarContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem copyPlayerDataMenuItem;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.Label dividerLabel;
        private System.Windows.Forms.ToolStripMenuItem openFileMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openCelesteTasMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openRecentMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem rememberCurrentFileMenuItem;
        private System.Windows.Forms.ToolStripMenuItem sendInputsToCelesteMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem homeMenuItem;
        private System.Windows.Forms.ContextMenuStrip tasTextContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem insertRemoveBreakPointToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem insertRemoveSavestateBreakPointToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveAsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem commentUncommentTextToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removeAllBreakpointsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem insertRoomNameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem insertCurrentInGameTimeToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem insertConsoleLoadCommandToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem insertOtherCommandToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem enforceLegalToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem unsafeToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem readToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem playToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem setToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem analogueModeToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
        private System.Windows.Forms.ToolStripMenuItem startExportToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem finishExportToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
        private System.Windows.Forms.ToolStripMenuItem addToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem skipToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem reconnectStudioAndCelesteToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem restoreSettingsToolStripMenuItem;
    }
}

