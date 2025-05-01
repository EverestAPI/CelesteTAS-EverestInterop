using CelesteStudio.Controls;
using Eto.Drawing;
using Eto.Forms;
using System;

namespace CelesteStudio.Dialog;

public class WhatsNewDialog : Eto.Forms.Dialog {
    private const int PageWidth = 500;
    private const int PageHeight = 500;
    private const int ImageWidth = 450;

    private WhatsNewDialog(string title, string markdownContent) {
        var pages = LegacyMarkdown.Parse(markdownContent, new Size(PageWidth, PageHeight));
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

            const int padding = 10;

            var buttonsLayout = new DynamicLayout { Width = PageWidth + ImageWidth - padding * 2 };
            buttonsLayout.BeginHorizontal();
            buttonsLayout.Add(prevButton);
            buttonsLayout.AddSpace();
            buttonsLayout.Add(new Label { Text = $"Page {currentPage + 1} / {pages.Count}"});
            buttonsLayout.AddSpace();
            buttonsLayout.Add(nextButton);

            var pageLayout = new StackLayout { Orientation = Orientation.Horizontal, Spacing = padding, Height = PageHeight };
            pageLayout.Items.Add(pages[currentPage].Page);

            foreach (string meta in pages[currentPage].Meta) {
                if (meta.StartsWith("image:")) {
                    pageLayout.Items.Add(Bitmap.FromResource(meta["image:".Length..]));
                }
            }

            return new StackLayout {
                Width = PageWidth + ImageWidth + padding * 2,
                Padding = padding,
                Items = {
                    new StackLayoutItem { Control = pageLayout, HorizontalAlignment = HorizontalAlignment.Center },
                    buttonsLayout,
                }
            };
        }

        Title = title;

        Studio.RegisterDialog(this);
    }

    public static void Show(string title, string markdown) => new WhatsNewDialog(title, markdown).ShowModal();
}
