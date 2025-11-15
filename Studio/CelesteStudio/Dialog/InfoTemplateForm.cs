using CelesteStudio.Communication;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CelesteStudio.Dialog;

public class InfoTemplateForm : Form {

    public InfoTemplateForm() {
        string[] infoTemplate = CommunicationWrapper.GetCustomInfoTemplate().SplitLines().ToArray();
        string[] evaluatedTemplate = CommunicationWrapper.EvaluateInfoTemplateAsync(infoTemplate).Result;

        var editorScrollable = new Scrollable {
            Width = Size.Width,
            Height = Size.Height,
        }.FixBorder();
        var editor = new InfoTemplateEditor(Document.Create(infoTemplate), editorScrollable);
        editorScrollable.Content = editor;

        var previewScrollable = new Scrollable {
            Width = Size.Width,
            Height = Size.Height,
        }.FixBorder();
        var preview = new TextViewer(Document.Create(evaluatedTemplate), previewScrollable) { ShowLineNumbers = false };
        previewScrollable.Content = preview;
        
        // On WPF, the scroll size needs to be set manually
        if (Eto.Platform.Instance.IsWpf) {
            editor.PreferredSizeChanged += size => editorScrollable.ScrollSize = size;
            preview.PreferredSizeChanged += size => previewScrollable.ScrollSize = size;
        }

        editor.Document.TextChanged += (_, _, _) => Task.Run(async () => {
            string[] evaluated = await CommunicationWrapper.EvaluateInfoTemplateAsync(editor.Document.Lines.ToArray());
            await Application.Instance.InvokeAsync(() => {
                using var __ = preview.Document.Update();
                using var patch = new Document.Patch(preview.Document);

                patch.DeleteRange(0, preview.Document.Lines.Count - 1);
                patch.InsertRange(0, evaluated);
            });
        });

        var templateLabel = new Label { Text = "Template", Font = SystemFonts.Bold(14.0f) };
        var previewLabel = new Label { Text = "Preview", Font = SystemFonts.Bold(14.0f) };

        var confirmButton = new Button((_, _) => {
            var lines = editor.Document.Lines
                // Trim leading empty lines
                .SkipWhile(string.IsNullOrWhiteSpace)
                // Trim trailing empty lines
                .Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse();

            CommunicationWrapper.SetCustomInfoTemplate(string.Join('\n', lines));
            Close();
        }) { Text = "&OK" };
        var cancelButton = new Button((_, _) => Close()) { Text = "&Cancel" };
        var buttonsLayout = new DynamicLayout();
        buttonsLayout.BeginHorizontal();
        buttonsLayout.Add(cancelButton);
        buttonsLayout.AddSpace();
        buttonsLayout.Add(confirmButton);

        Title = "Edit Info-Template";
        Icon = Assets.AppIcon;

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
            const int border = 1;
            editorScrollable.Width = previewScrollable.Width = buttonsLayout.Width = Math.Max(0, ClientSize.Width - padding*2 - border*2);

            int extraHeight = templateLabel.Height + previewLabel.Height + buttonsLayout.Height + padding*6;
            editorScrollable.Height = previewScrollable.Height = (Height - extraHeight) / 2;
        };

        BackgroundColor = Settings.Instance.Theme.Background;

        Width = 600;
        Height = 700;

        Studio.RegisterWindow(this);
    }
}
