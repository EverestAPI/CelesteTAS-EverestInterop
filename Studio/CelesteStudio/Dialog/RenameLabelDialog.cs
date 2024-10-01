using CelesteStudio.Communication;
using Eto.Drawing;
using Eto.Forms;
using System;

namespace CelesteStudio.Dialog;

public class RenameLabelDialog : Dialog<bool> {
    private readonly TextBox labelNameBox;

    private RenameLabelDialog(string labelName) {
        labelNameBox = new TextBox { Text = labelName, Width = 200 };

        Title = "Rename Label";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            VerticalContentAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Items = { new Label { Text = "Label Name" }, labelNameBox },
        };
        Icon = Assets.AppIcon;

        DefaultButton = new Button((_, _) => Close(true)) { Text = "&Rename" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Studio.RegisterDialog(this);
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    public static string Show(string labelName) {
        var dialog = new RenameLabelDialog(labelName);
        if (!dialog.ShowModal()) {
            return labelName;
        }

        if (string.IsNullOrWhiteSpace(dialog.labelNameBox.Text)) {
            MessageBox.Show("An label name is not valid!", MessageBoxButtons.OK, MessageBoxType.Error);
            return labelName;
        }

        return dialog.labelNameBox.Text;
    }
}
