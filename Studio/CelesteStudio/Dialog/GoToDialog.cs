using System.Linq;
using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Dialog;

public class GoToDialog : Dialog<int> {
    private GoToDialog(Document document, Window? parentWindow, bool supportLabels) {
        var lineSelector = new NumericStepper {
            MinValue = 1,
            MaxValue = document.Lines.Count,
            Increment = 1,
            Value = document.Caret.Row + 1,
            Width = 150,
        };

        if (supportLabels) {
            var labels = document.Lines
                .Select((line, row) => (line, row))
                .Where(pair => CommentLine.IsLabel(pair.line))
                .Select(pair => pair with { line = pair.line[1..] }) // Remove the #
                .ToArray();

            var dropdown = new DropDown { Width = 150 };

            if (labels.Length == 0) {
                dropdown.Enabled = false;
            } else {
                foreach (var (label, row) in labels) {
                    dropdown.Items.Add(new ListItem { Text = label, Key = row.ToString() });
                }

                // Find current label
                dropdown.SelectedKey = labels.Reverse().FirstOrDefault(pair => pair.row <= document.Caret.Row, labels[0]).row.ToString();
                // TODO: Make this a user preference
                // dropdown.SelectedKeyChanged += (_, _) => lineSelector.Value = int.Parse(dropdown.SelectedKey);
                dropdown.SelectedKeyChanged += (_, _) => Close(int.Parse(dropdown.SelectedKey));
            }

            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                Items = {
                    new StackLayout {
                        Spacing = 10,
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Items = { new Label { Text = "Line" }, lineSelector }
                    },
                    new StackLayout {
                        Spacing = 10,
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Items = { new Label { Text = "Label" }, dropdown }
                    }
                }
            };
        } else {
            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                Items = {
                    new StackLayout {
                        Spacing = 10,
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Items = { new Label { Text = "Line" }, lineSelector }
                    }
                }
            };
        }

        Title = "Go To";

        DefaultButton = new Button((_, _) => Close((int)lineSelector.Value - 1)) { Text = "&Go" };
        AbortButton = new Button((_, _) => Close(document.Caret.Row)) { Text = "&Cancel" };

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Studio.RegisterDialog(this, parentWindow);
    }

    public static int Show(Document document, Control owner, bool supportLabels = false) => new GoToDialog(document, owner.ParentWindow, supportLabels).ShowModal(owner);
}
