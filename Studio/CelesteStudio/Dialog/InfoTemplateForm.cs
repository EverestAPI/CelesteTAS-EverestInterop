using CelesteStudio.Controls;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
using System;

namespace CelesteStudio.Dialog;

public class InfoTemplateForm : Form {

    public InfoTemplateForm() {
        var editorScrollable = new Scrollable {
            Width = Size.Width,
            Height = Size.Height,
        }.FixBorder();
        var editor = new InfoTemplateEditor(Document.Create(["Hallo", "Welt", ":3"]), editorScrollable);
        editorScrollable.Content = editor;

        var previewScrollable = new Scrollable {
            Width = Size.Width,
            Height = Size.Height,
        }.FixBorder();
        var preview = new TextViewer(Document.Create(editor.Document.Lines), editorScrollable);
        previewScrollable.Content = preview;

        editor.Document.TextChanged += (_, insertions, deletions) => {
            Console.WriteLine($":3");
            using (preview.Document.Update()) {
                using var patch = new Document.Patch(preview.Document);

                patch.Insertions.AddRange(insertions);
                patch.Deletions.AddRange(deletions);
            }

            preview.Invalidate();
        };

        var templateLabel = new Label { Text = "Template", Font = SystemFonts.Bold(14.0f) };
        var previewLabel = new Label { Text = "Preview", Font = SystemFonts.Bold(14.0f) };

        var confirmButton = new Button((_, _) => Close()) { Text = "&OK" };
        var cancelButton = new Button((_, _) => Close()) { Text = "&Cancel" };
        var buttonsLayout = new DynamicLayout();
        buttonsLayout.BeginHorizontal();
        buttonsLayout.Add(cancelButton);
        buttonsLayout.AddSpace();
        buttonsLayout.Add(confirmButton);

        const int padding = 10;
        Content = new StackLayout {
            Padding = padding,
            Spacing = padding,
            Items = {
                new Panel { Content = templateLabel, Padding = 0 }, editorScrollable,
                new Panel { Content = previewLabel, Padding = 0 }, previewScrollable,
                new Panel { Content = buttonsLayout, Padding = 0 }
            }
        };

        SizeChanged += (_, _) => {
            editorScrollable.Width = previewScrollable.Width = buttonsLayout.Width = Width - padding*2 - 2; // Account for border

            int extraHeight = templateLabel.Height + previewLabel.Height + buttonsLayout.Height + padding*6;
            editorScrollable.Height = previewScrollable.Height = (Height - extraHeight) / 2;
        };

        BackgroundColor = Settings.Instance.Theme.Background;

        Width = 400;
        Height = 700;
    }
}
