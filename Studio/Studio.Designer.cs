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
			this.openCelesteTasToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.openRecentStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.rememberCurrentFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this.homeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.tasText = new CelesteStudio.RichText.RichText();
			this.statusBar.SuspendLayout();
			this.contextMenuStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// statusBar
			// 
			this.statusBar.AutoSize = false;
			this.statusBar.Font = new System.Drawing.Font("Courier New", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.statusBar.ImageScalingSize = new System.Drawing.Size(20, 20);
			this.statusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus});
			this.statusBar.Location = new System.Drawing.Point(0, 788);
			this.statusBar.Name = "statusBar";
			this.statusBar.Padding = new System.Windows.Forms.Padding(1, 0, 19, 0);
			this.statusBar.Size = new System.Drawing.Size(411, 111);
			this.statusBar.TabIndex = 1;
			// 
			// lblStatus
			// 
			this.lblStatus.Font = new System.Drawing.Font("Courier New", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Overflow = System.Windows.Forms.ToolStripItemOverflow.Never;
			this.lblStatus.Size = new System.Drawing.Size(391, 105);
			this.lblStatus.Spring = true;
			this.lblStatus.Text = "Searching...";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.TopLeft;
			// 
			// contextMenuStrip
			// 
			this.contextMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
			this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openCelesteTasToolStripMenuItem,
            this.openRecentStripMenuItem,
            this.rememberCurrentFileToolStripMenuItem,
            this.toolStripSeparator1,
            this.homeToolStripMenuItem});
			this.contextMenuStrip.Name = "contextMenuStrip1";
			this.contextMenuStrip.Size = new System.Drawing.Size(364, 106);
			this.contextMenuStrip.Opened += new System.EventHandler(this.contextMenuStrip_Opened);
			// 
			// openCelesteTasToolStripMenuItem
			// 
			this.openCelesteTasToolStripMenuItem.Name = "openCelesteTasToolStripMenuItem";
			this.openCelesteTasToolStripMenuItem.Size = new System.Drawing.Size(363, 24);
			this.openCelesteTasToolStripMenuItem.Text = "Open Celeste.tas";
			this.openCelesteTasToolStripMenuItem.Click += new System.EventHandler(this.openCelesteTasToolStripMenuItem_Click);
			// 
			// openRecentStripMenuItem
			// 
			this.openRecentStripMenuItem.Name = "openRecentStripMenuItem";
			this.openRecentStripMenuItem.Size = new System.Drawing.Size(363, 24);
			this.openRecentStripMenuItem.Text = "Open Recent";
			// 
			// rememberCurrentFileToolStripMenuItem
			// 
			this.rememberCurrentFileToolStripMenuItem.Name = "rememberCurrentFileToolStripMenuItem";
			this.rememberCurrentFileToolStripMenuItem.Size = new System.Drawing.Size(363, 24);
			this.rememberCurrentFileToolStripMenuItem.Text = "Remember the Current File for Next Launch";
			this.rememberCurrentFileToolStripMenuItem.Click += new System.EventHandler(this.rememberCurrentFileToolStripMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(360, 6);
			// 
			// homeToolStripMenuItem
			// 
			this.homeToolStripMenuItem.Name = "homeToolStripMenuItem";
			this.homeToolStripMenuItem.Size = new System.Drawing.Size(363, 24);
			this.homeToolStripMenuItem.Text = "Home";
			this.homeToolStripMenuItem.Click += new System.EventHandler(this.homeToolStripMenuItem_Click);
			// 
			// tasText
			// 
			this.tasText.ActiveLineColor = System.Drawing.Color.Lime;
			this.tasText.AllowDrop = true;
			this.tasText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tasText.AutoIndent = false;
			this.tasText.AutoScrollMinSize = new System.Drawing.Size(39, 26);
			this.tasText.BackBrush = null;
			this.tasText.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
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
			this.tasText.Location = new System.Drawing.Point(0, 0);
			this.tasText.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.tasText.Name = "tasText";
			this.tasText.Paddings = new System.Windows.Forms.Padding(0);
			this.tasText.SaveToFileName = null;
			this.tasText.SelectionColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
			this.tasText.Size = new System.Drawing.Size(410, 787);
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
			this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(411, 899);
			this.Controls.Add(this.statusBar);
			this.Controls.Add(this.tasText);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.KeyPreview = true;
			this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.MinimumSize = new System.Drawing.Size(261, 235);
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

		private System.Windows.Forms.ToolStripMenuItem openCelesteTasToolStripMenuItem;

		private System.Windows.Forms.ToolStripMenuItem homeToolStripMenuItem;

		private System.Windows.Forms.ToolStripMenuItem openRecentStripMenuItem;

		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

		#endregion
		private System.Windows.Forms.StatusStrip statusBar;
		private System.Windows.Forms.ToolStripStatusLabel lblStatus;
		public RichText.RichText tasText;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem rememberCurrentFileToolStripMenuItem;
    }
}

