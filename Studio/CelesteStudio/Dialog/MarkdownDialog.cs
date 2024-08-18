using Eto.Drawing;
using Eto.Forms;
using Markdig;
using Markdig.Syntax;
using System;
using System.IO;
using Markdown = CelesteStudio.Controls.Markdown;

namespace CelesteStudio.Dialog;

/// Displays basic markdown inside a dialog
/// This markdown renderer supports the following elements:
/// - Bold,
/// - Italic,
/// - Underline,
/// - Strikethrough
/// - Code (Inline/Block)
/// - Headers 1-6,
/// - Hyperlinks
/// - Page breaks
public class WhatsNewDialog : Eto.Forms.Dialog {

    private WhatsNewDialog(string title, string markdwonText) {
        var pages = Markdown.Parse(new Size(500, 300));

        Title = title;
        Content = new Panel {
            Padding = 10,
            Content = pages[0],
        };
        Icon = Assets.AppIcon;
        Studio.RegisterDialog(this);

        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    public static void Show(string title, string markdown) => new WhatsNewDialog(title, markdown).ShowModal();
}
