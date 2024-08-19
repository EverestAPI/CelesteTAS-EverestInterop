using CelesteStudio.Controls;
using Eto.Drawing;
using Eto.Forms;
using System;

namespace CelesteStudio.Dialog;

public class WhatsNewDialog : Eto.Forms.Dialog {
    private const int PageWidth = 600;
    private const int PageHeight = 300;

    private WhatsNewDialog(string title, string markdownContent) {
        var pages = Markdown.Parse(markdownContent, new Size(PageWidth, PageHeight));
        int currentPage = 0;

        Content = GenerateContent();
        Control GenerateContent() {
            var nextButton = new Button { Text = "Next", Enabled = pages.Count - 1 > currentPage };
            nextButton.Click += (_, _) => {
                currentPage = Math.Min(currentPage + 1, pages.Count - 1);
                Content = GenerateContent();
            };

            var prevButton = new Button { Text = "Previous", Enabled = currentPage > 0 };
            prevButton.Click += (_, _) => {
                currentPage = Math.Max(currentPage - 1, 0);
                Content = GenerateContent();
            };

            var buttonsLayout = new DynamicLayout { Width = PageWidth };
            buttonsLayout.BeginHorizontal();
            buttonsLayout.Add(prevButton);
            buttonsLayout.AddSpace();
            buttonsLayout.Add(new Label { Text = $"Page {currentPage + 1} / {pages.Count}"});
            buttonsLayout.AddSpace();
            buttonsLayout.Add(nextButton);

            return new StackLayout {
                Padding = 10,
                Items = {
                    pages[currentPage],
                    buttonsLayout,
                }
            };
        }

        Title = title;
        Icon = Assets.AppIcon;
        Studio.RegisterDialog(this);

        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    public static void Show(string title, string markdown) => new WhatsNewDialog(title, markdown).ShowModal();
}
