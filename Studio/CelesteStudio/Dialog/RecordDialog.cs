using System;
using CelesteStudio.Communication;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class RecordDialog : Dialog<bool> {
    private readonly TextBox textBox;
    
    private RecordDialog() {
        textBox = new TextBox { Text = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", Width = 200 };
        
        Title = "Record TAS";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            VerticalContentAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Items = { new Label { Text = "File Name" }, textBox },
        };
        Icon = Assets.AppIcon;
        
        DefaultButton = new Button((_, _) => Close(true)) { Text = "&Record" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
    
    public static void Show() {
        var dialog = new RecordDialog();
        if (!dialog.ShowModal())
            return;
        
        if (string.IsNullOrWhiteSpace(dialog.textBox.Text)) {
            MessageBox.Show("An empty file name is not valid!", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }
        
        CommunicationWrapper.RecordTAS(dialog.textBox.Text);
    }
}