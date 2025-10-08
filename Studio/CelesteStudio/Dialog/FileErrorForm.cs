using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;
using System.Collections.Generic;

namespace CelesteStudio.Dialog;

public class FileErrorForm : FloatingForm {
    private FileErrorForm(IEnumerable<(int Row, string Error)> errors) {
        Label text;
        ListBox errorList;

        Title = "File Errors";
        Icon = Assets.AppIcon;

        const int spacing = 10;
        Content = new StackLayout {
            Padding = spacing,
            Spacing = spacing,
            Items = {
                (text = new Label {
                    Text = "Something went wrong while generating this file!\nPlease see the list below for additional details:",
                    Font = SystemFonts.Bold(12.0f),
                }),
                (errorList = new ListBox { Font = FontManager.StatusFont, Width = 600, Height = 500 }),
            },
        };
        SizeChanged += (_, _) => {
            errorList.Width = Width - 2*spacing;
            errorList.Height = Height - text.Height - 3*spacing;
        };

        foreach ((int row, string error) in errors) {
            errorList.Items.Add(new ListItem {
                Text = $"Row {row+1}: {error}",
                Key = row.ToString(),
            });
        }
        errorList.SelectedIndexChanged += (_, _) => {
            if (!int.TryParse(errorList.Items[errorList.SelectedIndex].Key, out int errorRow)) {
                return;
            }

            var editor = Studio.Instance.Editor;
            editor.Document.Caret = editor.ClampCaret(new CaretPosition(errorRow));
            editor.Document.Selection.Clear();
            editor.ScrollCaretIntoView(center: true);
        };

        Studio.RegisterWindow(this);
    }

    public static void Show(IEnumerable<(int Row, string Error)> errors) => new FileErrorForm(errors).Show();
}
