using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class FontDialog : Dialog<bool> {
    private class FontPreview : Drawable {
        private Font font = null!;
        private SyntaxHighlighter? highlighter;
        
        public void SetFont(string fontFamily, float size) {
            font = FontManager.CreateFont(fontFamily, size);
            highlighter = new SyntaxHighlighter(font,
                FontManager.CreateFont(fontFamily, size, FontStyle.Bold),
                FontManager.CreateFont(fontFamily, size, FontStyle.Italic),
                FontManager.CreateFont(fontFamily, size, FontStyle.Bold | FontStyle.Italic));
            
            Invalidate();
        }
        
        public FontPreview() {
            BackgroundColor = Settings.Instance.Theme.Background;
            Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.Background;
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            if (highlighter == null)
                return;
            
            string[] previewText = [
                "#Start",
                "   7,L,Z",
                "***S",
                "  38,R,J,AD",
                "  17,F,192.59",
                "Set, Player.Speed.X, 200",
                " 145,R,D",
            ];
            
            e.Graphics.AntiAlias = true;
            
            float yPos = 0.0f;
            float maxWidth = 0.0f;
            foreach (var line in previewText) {
                highlighter.DrawLine(e.Graphics, 0.0f, yPos, line);
                maxWidth = Math.Max(maxWidth, font.MeasureWidth(line));
                yPos += font.LineHeight();
            }
            
            Size = new((int)maxWidth, (int)yPos);
            
            base.OnPaint(e);
        }
    }
    
    private static string[]? cachedFontFamilies;
    
    private readonly FontPreview preview;
    private readonly NumericStepper editorFontSize;
    private readonly NumericStepper statusFontSize;

    private string fontFamily = Settings.Instance.FontFamily;
    
    private FontDialog() {
        preview = new FontPreview();
        preview.SetFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize);
        
        editorFontSize = new NumericStepper {
            Value = Settings.Instance.EditorFontSize,
            MinValue = 1.0f,
            Increment = 1.0f,
            DecimalPlaces = 1,
            Width = 100,
        };
        statusFontSize = new NumericStepper {
            Value = Settings.Instance.StatusFontSize,
            MinValue = 1.0f,
            Increment = 1.0f,
            DecimalPlaces = 1,
            Width = 100,
        };
        
        Title = "Font";
        Content = CreateDialogContent();
        Icon = Assets.AppIcon;
        
        DefaultButton = new Button((_, _) => Close(true)) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
        
        // Fetch font list asynchronously
        if (cachedFontFamilies == null) {
            Task.Run(() => {
                var fonts = new List<string>();
                foreach (var family in Fonts.AvailableFontFamilies) {
                    // Check if the font is monospaced
                    var font = new Font(family, 12.0f);
                    if (Math.Abs(font.MeasureString("I").Width - font.MeasureString("X").Width) > 0.01f)
                        continue;
                    
                    fonts.Add(family.Name);
                }
                cachedFontFamilies = fonts.ToArray();
                Application.Instance.Invoke(() => Content = CreateDialogContent());
            });
        }
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
    
    private Control CreateDialogContent() {
        var settingsPanel = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new StackLayout {
                    Spacing = 10,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Orientation = Orientation.Horizontal,
                    Items = { new Label { Text = "Editor Font Size" }, editorFontSize },
                },
                new StackLayout {
                    Spacing = 10,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Orientation = Orientation.Horizontal,
                    Items = { new Label { Text = "Status Font Size" }, statusFontSize },
                },
                new Label { Text = "  Preview:" }.WithFontStyle(FontStyle.Bold),
                new Scrollable {
                    Content = preview,
                    Width = 250,
                    Height = 200,
                }.FixBorder()
            }
        };
        
        if (cachedFontFamilies != null) {
            var list = new ListBox { Width = 250, Height = 330 };
            list.SelectedValue = Settings.Instance.FontFamily;
            
            // Add built-in font
            list.Items.Add(new ListItem {
                Text = FontManager.FontFamilyBuiltinDisplayName,
                Key = FontManager.FontFamilyBuiltin,
            });
            foreach (var family in cachedFontFamilies) {
                list.Items.Add(new ListItem {
                    Text = family,
                    Key = family,
                });
            }
            
            // Select current font
            foreach (var item in list.Items) {
                if (item.Key == Settings.Instance.FontFamily) {
                    list.SelectedValue = item;
                    break;
                }
                    
            }
            
            editorFontSize.ValueChanged += (_, _) => UpdateFont();
            list.SelectedValueChanged += (_, _) => UpdateFont();
            
            return new StackLayout {
                Padding = 10,
                Orientation = Orientation.Horizontal,
                Items = { list, settingsPanel }
            };
            
            void UpdateFont() {
                if (list.SelectedValue is not ListItem item)
                    return;
                
                fontFamily = item.Key;
                preview.SetFont(item.Key, (float)editorFontSize.Value);
            }
        } else {
            var loadingPanel = new StackLayout {
                Spacing = 10,
                Width = 250, 
                Height = 330,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = {
                    new Spinner { Enabled = true },
                    new Label { Text = "Loading font list..." }.WithFontStyle(FontStyle.Italic),
                },
            };
            
            return new StackLayout {
                Padding = 10,
                Orientation = Orientation.Horizontal,
                Items = { loadingPanel, settingsPanel }
            };
        }
    }
    
    public static void Show() {
        var dialog = new FontDialog();
        if (!dialog.ShowModal())
            return;
        
        Settings.Instance.FontFamily = dialog.fontFamily;
        Settings.Instance.EditorFontSize = (float)dialog.editorFontSize.Value;
        Settings.Instance.StatusFontSize = (float)dialog.statusFontSize.Value;
        Settings.OnFontChanged();
        Settings.Save();
    }
}