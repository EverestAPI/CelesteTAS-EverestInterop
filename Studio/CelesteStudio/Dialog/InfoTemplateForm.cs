using CelesteStudio.Communication;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
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

        editor.Document.TextChanged += (_, insertions, deletions) => Task.Run(async () => {
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

        Width = 600;
        Height = 700;
    }
}
