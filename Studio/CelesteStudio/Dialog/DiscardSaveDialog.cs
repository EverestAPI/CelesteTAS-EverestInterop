using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class DiscardSaveDialog : Dialog<bool?> {

    private DiscardSaveDialog() {
        ShowInTaskbar = false;
        Title = "Unsaved Changes";
        Content = new Label { Text = "You have unsaved changes!\nIf you don't save them, they could be lost forever." };

        const int width = 100;
        const int spacing = 5;

        var discard = new Button((_, _) => Close(true)) { Text = "&Discard", Width = width, BackgroundColor = Color.FromRgb(0xF44336) };
        var cancel = new Button((_, _) => Close(null)) { Text = "&Cancel", Width = width };
        var save = new Button((_, _) => Close(false)) { Text = "&Save", Width = width };

        Content = new StackLayout {
            Padding = 5,
            Spacing = 5,
            Items = {
                new Label {
                    Text = "You have unsaved changes!\nIf you don't save them, they could be lost forever.",
                    TextAlignment = TextAlignment.Center,
                    Width = width*3 + spacing*2,
                },
                new StackLayout {
                    Padding = 5,
                    Spacing = spacing,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { save, cancel, discard }
                }
            }
        };

        DefaultButton = save;
        AbortButton = cancel;

        Studio.RegisterDialog(this);
    }

    public static bool? Show() {
        return new DiscardSaveDialog().ShowModal();
    }
}
