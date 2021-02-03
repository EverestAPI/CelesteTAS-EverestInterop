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
			this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.openFileMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.openCelesteTasMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.openRecentMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
			this.rememberCurrentFileMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.sendInputsToCelesteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this.homeMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.hotkeyToolTip = new System.Windows.Forms.ToolTip(this.components);
			this.birdButton = new System.Windows.Forms.Button();
			this.tasText = new CelesteStudio.RichText.RichText();
			this.statusBar.SuspendLayout();
			this.contextMenuStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// statusBar
			// 
			this.statusBar.AutoSize = false;
			this.statusBar.Font = new System.Drawing.Font("Courier New", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte) (0)));
			this.statusBar.ImageScalingSize = new System.Drawing.Size(20, 20);
			this.statusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {this.lblStatus});
			this.statusBar.Location = new System.Drawing.Point(0, 619);
			this.statusBar.Name = "statusBar";
			this.statusBar.Size = new System.Drawing.Size(308, 55);
			this.statusBar.TabIndex = 1;
			// 
			// lblStatus
			// 
			this.lblStatus.Font = new System.Drawing.Font("Courier New", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte) (0)));
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Overflow = System.Windows.Forms.ToolStripItemOverflow.Never;
			this.lblStatus.Size = new System.Drawing.Size(293, 50);
			this.lblStatus.Spring = true;
			this.lblStatus.Text = "Searching...";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.TopLeft;
			// 
			// contextMenuStrip
			// 
			this.contextMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
			this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {this.openFileMenuItem, this.openCelesteTasMenuItem, this.openRecentMenuItem, this.toolStripSeparator2, this.rememberCurrentFileMenuItem, this.sendInputsToCelesteMenuItem, this.toolStripSeparator1, this.homeMenuItem});
			this.contextMenuStrip.Name = "contextMenuStrip1";
			this.contextMenuStrip.Size = new System.Drawing.Size(329, 148);
			this.contextMenuStrip.Opened += new System.EventHandler(this.contextMenuStrip_Opened);
			// 
			// openFileMenuItem
			// 
			this.openFileMenuItem.Name = "openFileMenuItem";
			this.openFileMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys) ((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
			this.openFileMenuItem.Size = new System.Drawing.Size(328, 22);
			this.openFileMenuItem.Text = "Open File...";
			this.openFileMenuItem.Click += new System.EventHandler(this.openFileMenuItem_Click);
			// 
			// openCelesteTasMenuItem
			// 
			this.openCelesteTasMenuItem.Name = "openCelesteTasMenuItem";
			this.openCelesteTasMenuItem.Size = new System.Drawing.Size(328, 22);
			this.openCelesteTasMenuItem.Text = "Open Celeste.tas";
			this.openCelesteTasMenuItem.Click += new System.EventHandler(this.openCelesteTasMenuItem_Click);
			// 
			// openRecentMenuItem
			// 
			this.openRecentMenuItem.Name = "openRecentMenuItem";
			this.openRecentMenuItem.Size = new System.Drawing.Size(328, 22);
			this.openRecentMenuItem.Text = "Open Recent";
			// 
			// toolStripSeparator2
			// 
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			this.toolStripSeparator2.Size = new System.Drawing.Size(325, 6);
			// 
			// rememberCurrentFileMenuItem
			// 
			this.rememberCurrentFileMenuItem.Name = "rememberCurrentFileMenuItem";
			this.rememberCurrentFileMenuItem.Size = new System.Drawing.Size(328, 22);
			this.rememberCurrentFileMenuItem.Text = "Remember the Current File for Next Launch";
			this.rememberCurrentFileMenuItem.Click += new System.EventHandler(this.rememberCurrentFileMenuItem_Click);
			// 
			// sendInputsToCelesteMenuItem
			// 
			this.sendInputsToCelesteMenuItem.Name = "sendInputsToCelesteMenuItem";
			this.sendInputsToCelesteMenuItem.Size = new System.Drawing.Size(328, 22);
			this.sendInputsToCelesteMenuItem.Text = "Send Inputs to Celeste";
			this.sendInputsToCelesteMenuItem.Click += new System.EventHandler(this.sendInputsToCelesteMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(325, 6);
			// 
			// homeMenuItem
			// 
			this.homeMenuItem.Name = "homeMenuItem";
			this.homeMenuItem.Size = new System.Drawing.Size(328, 22);
			this.homeMenuItem.Text = "Home";
			this.homeMenuItem.Click += new System.EventHandler(this.homeMenuItem_Click);
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
			// birdButton
			// 
			this.birdButton.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.birdButton.BackColor = System.Drawing.Color.Transparent;
			this.birdButton.Cursor = System.Windows.Forms.Cursors.Hand;
			this.birdButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int) (((byte) (240)))), ((int) (((byte) (240)))), ((int) (((byte) (240)))));
			this.birdButton.FlatAppearance.BorderSize = 0;
			this.birdButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.birdButton.Image = global::CelesteStudio.Properties.Resources.bird;
			this.birdButton.Location = new System.Drawing.Point(278, 649);
			this.birdButton.Name = "birdButton";
			this.birdButton.Size = new System.Drawing.Size(30, 25);
			this.birdButton.TabIndex = 2;
			this.birdButton.TabStop = false;
			this.birdButton.UseVisualStyleBackColor = false;
			this.birdButton.Click += new System.EventHandler(this.birdButton_Click);
			// 
			// tasText
			// 
			this.tasText.ActiveLineColor = System.Drawing.Color.Lime;
			this.tasText.AllowDrop = true;
			this.tasText.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
			this.tasText.AutoIndent = false;
			this.tasText.AutoScrollMinSize = new System.Drawing.Size(33, 84);
			this.tasText.BackBrush = null;
			this.tasText.ChangedLineColor = System.Drawing.Color.DarkOrange;
			this.tasText.CommentPrefix = "#";
			this.tasText.CurrentLineColor = System.Drawing.Color.FromArgb(((int) (((byte) (64)))), ((int) (((byte) (64)))), ((int) (((byte) (64)))));
			this.tasText.CurrentLineText = null;
			this.tasText.Cursor = System.Windows.Forms.Cursors.IBeam;
			this.tasText.DisabledColor = System.Drawing.Color.FromArgb(((int) (((byte) (100)))), ((int) (((byte) (180)))), ((int) (((byte) (180)))), ((int) (((byte) (180)))));
			this.tasText.Font = new System.Drawing.Font("Courier New", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte) (0)));
			this.tasText.ForeColor = System.Drawing.Color.Black;
			this.tasText.Language = CelesteStudio.RichText.Language.TAS;
			this.tasText.LastFileName = null;
			this.tasText.LineNumberColor = System.Drawing.Color.Black;
			this.tasText.Location = new System.Drawing.Point(0, 0);
			this.tasText.Name = "tasText";
			this.tasText.Paddings = new System.Windows.Forms.Padding(0);
			this.tasText.SaveToFileName = null;
			this.tasText.SelectionColor = System.Drawing.Color.FromArgb(((int) (((byte) (50)))), ((int) (((byte) (0)))), ((int) (((byte) (0)))), ((int) (((byte) (0)))));
			this.tasText.Size = new System.Drawing.Size(308, 591);
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
			// Studio
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(308, 674);
			this.Controls.Add(this.birdButton);
			this.Controls.Add(this.statusBar);
			this.Controls.Add(this.tasText);
			this.Icon = ((System.Drawing.Icon) (resources.GetObject("$this.Icon")));
			this.KeyPreview = true;
			this.MinimumSize = new System.Drawing.Size(200, 186);
			this.Name = "Studio";
			this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			this.Text = "Studio";
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.TASStudio_FormClosed);
			this.Load += new System.EventHandler(this.Studio_Load);
			this.Shown += new System.EventHandler(this.Studio_Shown);
			this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Studio_KeyDown);
			this.statusBar.ResumeLayout(false);
			this.statusBar.PerformLayout();
			this.contextMenuStrip.ResumeLayout(false);
			this.ResumeLayout(false);
		}

		private System.Windows.Forms.ToolStripMenuItem openFileMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;

		private System.Windows.Forms.ToolStripMenuItem sendInputsToCelesteMenuItem;

		private System.Windows.Forms.ToolStripMenuItem openCelesteTasMenuItem;

		private System.Windows.Forms.ToolStripMenuItem homeMenuItem;

		private System.Windows.Forms.ToolStripMenuItem openRecentMenuItem;

		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

		#endregion
		private System.Windows.Forms.StatusStrip statusBar;
		public RichText.RichText tasText;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem rememberCurrentFileMenuItem;
        private System.Windows.Forms.ToolTip hotkeyToolTip;
        private System.Windows.Forms.Button birdButton;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
    }
}

