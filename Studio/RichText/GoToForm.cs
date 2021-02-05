using System;
using System.Windows.Forms;

namespace CelesteStudio.RichText {
public sealed partial class GoToForm : Form {
    public GoToForm() {
        InitializeComponent();
    }

    public int SelectedLineNumber { get; set; }
    public int TotalLineCount { get; set; }

    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        this.label.Text = String.Format("Line number (1 - {0}):", this.TotalLineCount);
        this.textBox.DataBindings.Add("Text", this, "SelectedLineNumber", true, DataSourceUpdateMode.OnPropertyChanged);
    }
}
}